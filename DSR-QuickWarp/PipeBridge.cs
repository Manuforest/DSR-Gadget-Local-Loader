using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;

namespace DSR_QuickWarp
{
    internal sealed class PipeBridge
    {
        internal const string PipeName = "DSRQuickWarp";

        private readonly Func<string, string> _handler;
        private readonly object _gate = new object();
        private Thread _thread;
        private volatile bool _running;
        private NamedPipeServerStream _server;

        internal PipeBridge(Func<string, string> handler)
        {
            _handler = handler;
        }

        internal void Start()
        {
            if (_running)
                return;

            _running = true;
            _thread = new Thread(ServerLoop)
            {
                IsBackground = true,
                Name = "DSR QuickWarp Pipe"
            };
            _thread.Start();
        }

        internal void Stop()
        {
            _running = false;
            lock (_gate)
            {
                try
                {
                    if (_server != null)
                        _server.Dispose();
                }
                catch
                {
                }
            }

            if (_thread != null && _thread.IsAlive)
                _thread.Join(1000);
        }

        private void ServerLoop()
        {
            while (_running)
            {
                NamedPipeServerStream server = null;
                try
                {
                    server = new NamedPipeServerStream(
                        PipeName,
                        PipeDirection.InOut,
                        1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.None,
                        4096,
                        4096);

                    lock (_gate)
                        _server = server;

                    server.WaitForConnection();
                    if (!_running)
                        break;

                    using (StreamReader reader = new StreamReader(server, Encoding.UTF8, false, 1024, true))
                    using (StreamWriter writer = new StreamWriter(server, new UTF8Encoding(false), 1024, true) { AutoFlush = true })
                    {
                        while (_running && server.IsConnected)
                        {
                            string command = reader.ReadLine();
                            if (command == null)
                                break;

                            string response = _handler(command);
                            writer.Write(response);
                            writer.Flush();
                        }
                    }
                }
                catch (IOException)
                {
                }
                catch (ObjectDisposedException)
                {
                }
                catch (Exception)
                {
                    Thread.Sleep(250);
                }
                finally
                {
                    lock (_gate)
                    {
                        if (ReferenceEquals(_server, server))
                            _server = null;
                    }
                    if (server != null)
                        server.Dispose();
                }
            }
        }
    }
}
