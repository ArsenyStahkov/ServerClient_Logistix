using System;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Net.Sockets;
using System.IO;
using System.Buffers.Binary;
using System.Net;
using System.Threading.Channels;
using static System.Net.Mime.MediaTypeNames;

namespace Client
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void TextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            TextBox textBox = (TextBox)sender;
            textBox.MaxLength = 1;
            int numVal;
            bool isParsed = int.TryParse(textBox.Text, out numVal);

            if (isParsed)
            {
                if (numVal != 1 && numVal != 2)
                    TextBlock_1.Text = "The value must be 1 or 2!";
                else
                    TextBlock_1.Text = String.Empty;
            }
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            int port = 13400;
            try
            {
                TcpClient tcpClient = new TcpClient("127.0.0.1", port);
                Connection connection = new Connection(tcpClient, this);

                //while (true)
                //{
                    string input = TextBox_1.Text;
                    //if (input.Length == 0)
                        //break;
                    Connect(connection, input);
                //}
            }
            catch (Exception ex)
            {
                TextBlock_1.Text = ex.Message;
            }
        }

        async void Connect(Connection connection, string input)
        {
            await connection.SendMessageAsync(input);
        }

        public void AddText(string text)
        {
            TextBlock_1.Text = text;
        }
    }

    class Connection
    {
        private readonly TcpClient _client;
        private readonly NetworkStream _stream;
        private readonly EndPoint _remoteEndPoint;
        private readonly Task _readingTask;
        private readonly Task _writingTask;
        private readonly Channel<string> _channel;
        private MainWindow _window;
        bool disposed;

        public Connection(TcpClient client, MainWindow window)
        {
            _client = client;
            _stream = client.GetStream();
            _remoteEndPoint = client.Client.RemoteEndPoint;
            _channel = Channel.CreateUnbounded<string>();
            _readingTask = RunReadingLoop();
            _writingTask = RunWritingLoop();
            _window = window;
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

                    //_window = window;
                    _window.AddText(message);


                }
                _stream.Close();
            }
            catch (IOException)
            {
                //TextBlock_1.Text = "Connection is closed";
            }
            catch (Exception ex)
            {
                //TextBlock_1.Text = ex.GetType().Name + ": " + ex.Message;
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
}
