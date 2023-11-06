using System.Buffers.Binary;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Threading.Channels;

class Client
{
    public static async Task Main(string[] args)
    {
        int port = 13400;
        Console.WriteLine("Client started");
        try
        {
            using TcpClient tcpClient = new TcpClient("127.0.0.1", port);
            using Connection connection = new Connection(tcpClient);
            Console.WriteLine($"Connected to server: {port}");
            while (true)
            {
                string input = Console.ReadLine();
                if (input.Length == 0)
                    break;
                await connection.SendMessageAsync(input);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }
        Console.WriteLine("Press any key to exit");
        Console.ReadKey(true);
    }
}

class Connection : IDisposable
{
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly EndPoint _remoteEndPoint;
    private readonly Task _readingTask;
    private readonly Task _writingTask;
    private readonly Channel<string> _channel;
    bool disposed;

    public Connection(TcpClient client)
    {
        _client = client;
        _stream = client.GetStream();
        _remoteEndPoint = client.Client.RemoteEndPoint;
        _channel = Channel.CreateUnbounded<string>();
        _readingTask = RunReadingLoop();
        _writingTask = RunWritingLoop();
    }

    private async Task RunReadingLoop()
    {
        try
        {
            byte[] headerBuffer = new byte[4];
            while (true)
            {
                int bytesReceived = await _stream.ReadAsync(headerBuffer, 0, headerBuffer.Length);
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
            }
            Console.WriteLine($"Server closed the connection");
            _stream.Close();
        }
        catch (IOException)
        {
            Console.WriteLine($"Connection is closed");
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.GetType().Name + ": " + ex.Message);
        }
    }

    public async Task SendMessageAsync(string message)
    {
        await _channel.Writer.WriteAsync(message);
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

    ~Connection() => Dispose(false);
}