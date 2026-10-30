using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace StadionTracking
{
    internal enum RunnerConnectionEventKind { Connecting, Connected, Disconnected, Error, Message }

    internal readonly struct RunnerConnectionEvent
    {
        internal readonly RunnerConnectionEventKind Kind;
        internal readonly string Detail;
        internal RunnerConnectionEvent(RunnerConnectionEventKind kind, string detail = null)
        { Kind = kind; Detail = detail; }
    }

    /// <summary>One server connection and its retry loop; never accesses Unity objects.</summary>
    internal sealed class RunnerServerConnection : IDisposable
    {
        private const int ConnectTimeoutMilliseconds = 5000;
        private const int InitialRetryMilliseconds = 500;
        private const int MaximumRetryMilliseconds = 10000;
        private const int MaximumMessageBytes = 65536;
        private const int MaximumIncomingMessages = 128;
        private readonly ConcurrentQueue<RunnerConnectionEvent> incoming = new ConcurrentQueue<RunnerConnectionEvent>();
        private readonly ConcurrentQueue<string> outgoing = new ConcurrentQueue<string>();
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly Task connectionTask;
        private bool disposed;

        internal RunnerServerConnection(Uri uri, string deviceId)
        {
            connectionTask = ConnectLoop(uri, deviceId, lifetime.Token);
        }

        internal bool TryRead(out RunnerConnectionEvent message) => incoming.TryDequeue(out message);
        internal void Send(string message) => outgoing.Enqueue(message);

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            lifetime.Cancel();
            _ = ReleaseWhenStopped();
        }

        private async Task ReleaseWhenStopped()
        {
            try { await connectionTask.ConfigureAwait(false); }
            finally { lifetime.Dispose(); }
        }

        private async Task ConnectLoop(Uri uri, string deviceId, CancellationToken token)
        {
            int delay = InitialRetryMilliseconds;
            int attempt = 0;
            while (!token.IsCancellationRequested)
            {
                incoming.Enqueue(new RunnerConnectionEvent(RunnerConnectionEventKind.Connecting,
                    "Verbinde mit " + uri.GetLeftPart(UriPartial.Path) + " · Versuch " + (++attempt)));
                try
                {
                    using (var socket = new ClientWebSocket())
                    {
                        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(10);
                        await ConnectWithTimeout(socket, uri, token).ConfigureAwait(false);
                        await Send(socket, new JObject { ["type"] = "hello", ["role"] = "headset", ["deviceId"] = deviceId,
                            ["platform"] = "Magic Leap 2" }.ToString(), token).ConfigureAwait(false);
                        if (token.IsCancellationRequested) return;
                        incoming.Enqueue(new RunnerConnectionEvent(RunnerConnectionEventKind.Connected));
                        delay = InitialRetryMilliseconds;
                        using (var connection = CancellationTokenSource.CreateLinkedTokenSource(token))
                        {
                            var receive = Receive(socket, connection.Token);
                            try
                            {
                                while (!receive.IsCompleted && socket.State == WebSocketState.Open && !token.IsCancellationRequested)
                                {
                                    while (outgoing.TryDequeue(out var json)) await Send(socket, json, connection.Token).ConfigureAwait(false);
                                    await Task.WhenAny(receive, Task.Delay(25, connection.Token)).ConfigureAwait(false);
                                }
                                await receive.ConfigureAwait(false);
                            }
                            finally { connection.Cancel(); socket.Abort(); }
                        }
                    }
                }
                catch (Exception e) when (!token.IsCancellationRequested)
                { incoming.Enqueue(new RunnerConnectionEvent(RunnerConnectionEventKind.Error, DescribeFailure(e, uri))); }
                catch (Exception) when (token.IsCancellationRequested) { }
                finally
                {
                    if (!token.IsCancellationRequested)
                    {
                        incoming.Enqueue(new RunnerConnectionEvent(RunnerConnectionEventKind.Disconnected));
                        while (outgoing.TryDequeue(out _)) { }
                    }
                }
                try { await Task.Delay(delay, token).ConfigureAwait(false); } catch (OperationCanceledException) { break; }
                delay = Math.Min(delay * 2, MaximumRetryMilliseconds);
            }
        }
        private static async Task ConnectWithTimeout(ClientWebSocket socket, Uri uri, CancellationToken token)
        {
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                var connecting = socket.ConnectAsync(uri, token);
                var timeout = Task.Delay(ConnectTimeoutMilliseconds, deadline.Token);
                if (await Task.WhenAny(connecting, timeout).ConfigureAwait(false) != connecting)
                {
                    // Some Unity/Mono versions ignore cancellation during the HTTP upgrade.
                    socket.Abort();
                    _ = ObserveStoppedConnection(connecting);
                    token.ThrowIfCancellationRequested();
                    throw new TimeoutException("WebSocket handshake timed out.");
                }
                deadline.Cancel();
                await connecting.ConfigureAwait(false);
            }
        }

        private static async Task ObserveStoppedConnection(Task connecting)
        {
            try { await connecting.ConfigureAwait(false); }
            catch (Exception) { /* Expected when a timed-out or replaced socket is aborted. */ }
        }

        internal static string DescribeFailure(Exception error, Uri uri)
        {
            string reason = error is OperationCanceledException || error is TimeoutException ? "Zeitüberschreitung beim Verbindungsaufbau." : null;
            for (Exception cause = error; cause != null && reason == null; cause = cause.InnerException)
            {
                if (cause is OperationCanceledException || cause is TimeoutException) { reason = "Zeitüberschreitung beim Verbindungsaufbau."; break; }
                if (!(cause is SocketException socket)) continue;
                switch (socket.SocketErrorCode)
                {
                    case SocketError.ConnectionRefused: reason = "Verbindung abgelehnt. Läuft der Server auf diesem Port?"; break;
                    case SocketError.HostNotFound: case SocketError.NoData: reason = "Servername konnte nicht aufgelöst werden."; break;
                    case SocketError.NetworkUnreachable: case SocketError.HostUnreachable: reason = "Server im WLAN nicht erreichbar."; break;
                    case SocketError.TimedOut: reason = "Zeitüberschreitung. WLAN, Adresse und Firewall prüfen."; break;
                }
            }
            return uri.GetLeftPart(UriPartial.Path) + "\n" + (reason ?? error.Message);
        }

        private static Task Send(ClientWebSocket socket, string json, CancellationToken token)
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            return socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, token);
        }
        private async Task Receive(ClientWebSocket socket, CancellationToken token)
        {
            var buffer = new byte[4096];
            while (socket.State == WebSocketState.Open)
            {
                using (var message = new MemoryStream())
                {
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token).ConfigureAwait(false);
                        if (result.MessageType == WebSocketMessageType.Close) return;
                        if (result.MessageType != WebSocketMessageType.Text || message.Length + result.Count > MaximumMessageBytes)
                            throw new InvalidDataException("Invalid or oversized server message.");
                        message.Write(buffer, 0, result.Count);
                    } while (!result.EndOfMessage);
                    if (incoming.Count >= MaximumIncomingMessages) throw new InvalidDataException("Server command queue is full.");
                    if (token.IsCancellationRequested) return;
                    incoming.Enqueue(new RunnerConnectionEvent(RunnerConnectionEventKind.Message, Encoding.UTF8.GetString(message.ToArray())));
                }
            }
        }
    }
}
