﻿using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Threading.Tasks;
//using Microsoft.Extensions.Logging;
using System.Threading;
using Ninja.WebSockets;

namespace WebSockets.DemoServer
{
    public class WebServer : IDisposable
    {
        private TcpListener _listener;
        private bool _isDisposed = false;
        //ILogger _logger;
        private readonly IWebSocketServerFactory _webSocketServerFactory;
        //private readonly ILoggerFactory _loggerFactory;

        public WebServer(IWebSocketServerFactory webSocketServerFactory)
        {
            //_logger = loggerFactory.CreateLogger<WebServer>();
            _webSocketServerFactory = webSocketServerFactory;
            //_loggerFactory = loggerFactory;
        }

        private void ProcessTcpClient(TcpClient tcpClient)
        {
            Task.Run(() => ProcessTcpClientAsync(tcpClient));
        }

        private async Task ProcessTcpClientAsync(TcpClient tcpClient)
        {
            CancellationTokenSource source = new CancellationTokenSource();

            try
            {
                if (_isDisposed)
                {
                    return;
                }

                // this worker thread stays alive until either of the following happens:
                // Client sends a close conection request OR
                // An unhandled exception is thrown OR
                // The server is disposed
                //_logger.LogInformation("Server: Connection opened. Reading Http header from stream");

                // get a secure or insecure stream
                Stream stream = tcpClient.GetStream();
                WebSocketHttpContext context = await _webSocketServerFactory.ReadHttpHeaderFromStreamAsync(stream);
                if (context.IsWebSocketRequest)
                {
                    // disable ping pong for now (it is causing multi-threaded issues)
                    var options = new WebSocketServerOptions() { KeepAliveInterval = TimeSpan.Zero };
                    //_logger.LogInformation("Http header has requested an upgrade to Web Socket protocol. Negotiating Web Socket handshake");
                    WebSocket webSocket = await _webSocketServerFactory.AcceptWebSocketAsync(context, options);

                    //_logger.LogInformation("Web Socket handshake response sent. Stream ready.");
                    await RespondToWebSocketRequestAsync(webSocket, source.Token);
                }
                else
                {
                    //_logger.LogInformation("Http header contains no web socket upgrade request. Ignoring");
                }

                //_logger.LogInformation("Server: Connection closed");
            }
            catch (ObjectDisposedException)
            {
                // do nothing. This will be thrown if the Listener has been stopped
            }
            catch (Exception ex)
            {
                //_logger.LogError(ex.ToString());
            }
            finally
            {
                try
                {
                    tcpClient.Client.Close();
                    tcpClient.Close();
                    source.Cancel();
                }
                catch (Exception ex)
                {
                    //_logger.LogError($"Failed to close TCP connection: {ex}");
                }
            }
        }

        public async Task RespondToWebSocketRequestAsync(WebSocket webSocket, CancellationToken token)
        {
            const int bufferLen = 4 * 1024 * 1024; // 4MB
            ArraySegment<byte> buffer = new ArraySegment<byte>(new byte[bufferLen]);

            while (true)
            {
                WebSocketReceiveResult result = await webSocket.ReceiveAsync(buffer, token);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    //_logger.LogInformation($"Client initiated close. Status: {result.CloseStatus} Description: {result.CloseStatusDescription}");
                    break;
                }

                if (result.Count > bufferLen)
                {
                    await webSocket.CloseAsync(WebSocketCloseStatus.MessageTooBig,
                        $"Web socket frame cannot exceed buffer size of {bufferLen:#,##0} bytes. Send multiple frames instead.",
                        token);
                    break;
                }

                // just echo the message back to the client
                ArraySegment<byte> toSend = new ArraySegment<byte>(buffer.Array, buffer.Offset, result.Count);
                await webSocket.SendAsync(toSend, WebSocketMessageType.Binary, true, token);
            }
        }

        public async Task Listen(int port)
        {
            try
            {
                IPAddress localAddress = IPAddress.Any;
                _listener = new TcpListener(localAddress, port);
                _listener.Start();
                //_logger.LogInformation($"Server started listening on port {port}");
                while(true)
                {
                    TcpClient tcpClient = await _listener.AcceptTcpClientAsync();
                    ProcessTcpClient(tcpClient);
                }
            }
            catch (SocketException ex)
            {
                string message = string.Format("Error listening on port {0}. Make sure IIS or another application is not running and consuming your port.", port);
                throw new Exception(message, ex);
            }
        }
        
        public void Dispose()
        {
            if (!_isDisposed)
            {
                _isDisposed = true;

                // safely attempt to shut down the listener
                try
                {
                    if (_listener != null)
                    {
                        if (_listener.Server != null)
                        {
                            _listener.Server.Close();
                        }

                        _listener.Stop();
                    }
                }
                catch (Exception ex)
                {
                    //_logger.LogError( ex.ToString());
                }
                
                //_logger.LogInformation( "Web Server disposed");
            }
        }
    }
}
