using System.Buffers.Binary;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Threading.Channels;
using System.IO;

class Server
{
    public static async Task Main(string[] args)
    {
        int port = 13400;
        Console.WriteLine("Server started");
        using (TcpServer server = new TcpServer(port))
        {
            Task servertask = server.ListenAsync();
            while (true)
            {
                string input = Console.ReadLine();
                if (input == "stop")
                {
                    Console.WriteLine("Server stopped");
                    server.Stop();
                    break;
                }
            }
            await servertask;
        }
        Console.WriteLine("Press any key to exit");
        Console.ReadKey(true);
    }
}

class TcpServer : IDisposable
{
    private readonly TcpListener _listener;
    private readonly List<Connection> _clients;
    bool disposed;

    public TcpServer(int port)
    {
        _listener = new TcpListener(IPAddress.Any, port);
        _clients = new List<Connection>();
    }

    public async Task ListenAsync()
    {
        try
        {
            _listener.Start();
            Console.WriteLine("Server started on " + _listener.LocalEndpoint);
            while (true)
            {
                TcpClient client = await _listener.AcceptTcpClientAsync();
                Console.WriteLine("Connect: " + client.Client.RemoteEndPoint + " > " + client.Client.LocalEndPoint);
                lock (_clients)
                {
                    _clients.Add(new Connection(client, c => { lock (_clients) { _clients.Remove(c); } c.Dispose(); }));
                }
            }
        }
        catch (SocketException)
        {
            Console.WriteLine("Server stopped");
        }
    }

    public void Stop()
    {
        _listener.Stop();
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        if (disposed)
            throw new ObjectDisposedException(typeof(TcpServer).FullName);
        disposed = true;
        _listener.Stop();
        if (disposing)
        {
            lock (_clients)
            {
                if (_clients.Count > 0)
                {
                    Console.WriteLine("Disconnect the clients...");
                    foreach (Connection client in _clients)
                    {
                        client.Dispose();
                    }
                    Console.WriteLine("Clients are disconnected");
                }
            }
        }
    }

    ~TcpServer() => Dispose(false);
}

class Connection : IDisposable
{
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly EndPoint _remoteEndPoint;
    private readonly Task _readingTask;
    private readonly Task _writingTask;
    private readonly Action<Connection> _disposeCallback;
    private readonly Channel<string> _channel;
    bool disposed;

    public Connection(TcpClient client, Action<Connection> disposeCallback)
    {
        _client = client;
        _stream = client.GetStream();
        _remoteEndPoint = client.Client.RemoteEndPoint;
        _disposeCallback = disposeCallback;
        _channel = Channel.CreateUnbounded<string>();
        _readingTask = RunReadingLoop();
        _writingTask = RunWritingLoop();
    }

    private async Task RunReadingLoop()
    {
        await Task.Yield();
        try
        {
            byte[] headerBuffer = new byte[4];
            while (true)
            {
                int bytesReceived = await _stream.ReadAsync(headerBuffer, 0, 4);
                if (bytesReceived != 4)
                    break;
                int length = BinaryPrimitives.ReadInt32LittleEndian(headerBuffer);
                byte[] buffer = new byte[length];
                int count = 0;
                while (count < length)
                {
                    bytesReceived = await _stream.ReadAsync(buffer, count, buffer.Length - count);
                    count += bytesReceived;
                }
                string message = Encoding.UTF8.GetString(buffer);
                Console.WriteLine($"<< {_remoteEndPoint}: {message}");
                await SendMessageAsync(message);
            }
            Console.WriteLine($"Client {_remoteEndPoint} disconnected");
            _stream.Close();
        }
        catch (IOException)
        {
            Console.WriteLine($"Connection to {_remoteEndPoint} closed by the server");
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.GetType().Name + ": " + ex.Message);
        }
        if (!disposed)
            _disposeCallback(this);
    }

    public async Task SendMessageAsync(string message)
    {
        message = ChangeValue(message);
        await _channel.Writer.WriteAsync(message);
        Console.WriteLine("Result: " + message);
    }

    private async Task RunWritingLoop()
    {
        byte[] header = new byte[4];
        await foreach (string message in _channel.Reader.ReadAllAsync())
        {
            byte[] buffer = Encoding.UTF8.GetBytes(message);
            BinaryPrimitives.WriteInt32LittleEndian(header, buffer.Length);
            await _stream.WriteAsync(header, 0, header.Length);
            await _stream.WriteAsync(buffer, 0, buffer.Length);
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposed)
            throw new ObjectDisposedException(GetType().FullName);
        disposed = true;
        if (_client.Connected)
        {
            _channel.Writer.Complete();
            _stream.Close();
            Task.WaitAll(_readingTask, _writingTask);
        }
        if (disposing)
        {
            _client.Dispose();
        }
    }

    string ChangeValue(string str)
    {
        try
        {
            int val;
            bool isParsed = Int32.TryParse(str, out val);

            if (!isParsed)
                return "The value must be 1 or 2!";

            if (val != 1 && val != 2)
                return "The value must be 1 or 2!";

            val = val == 1 ? 3 : 4;

            return (val.ToString());
        }
        catch (Exception ex)
        {
            throw new Exception(ex.Message);
        }
    }

    ~Connection() => Dispose(false);
}