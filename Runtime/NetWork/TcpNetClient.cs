using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace EasyFramework
{
    internal enum NetEventType
    {
        Connected,
        Disconnected,
        Message,
    }

    internal struct NetEvent
    {
        public int Generation;
        public NetEventType Type;
        public NetMessage Message;
        public string Reason;
    }

    public class TcpNetClient
    {
        private sealed class ConnectionContext
        {
            public int Generation;
            public TcpClient Tcp;
            public NetworkStream Stream;
            public CancellationTokenSource Cancellation;
            public readonly SemaphoreSlim SendGate = new SemaphoreSlim(1, 1);
            public int Connected;
            public int Closed;
        }

        private readonly object _stateLock = new object();
        private readonly SemaphoreSlim _connectGate = new SemaphoreSlim(1, 1);
        private readonly ConcurrentQueue<NetEvent> _events = new ConcurrentQueue<NetEvent>();
        private ConnectionContext _current;
        private int _generation;
        private int _pendingEventCount;
        private int _maxFrameSize = 1024 * 1024;
        private int _maxPendingEvents = 4096;

        public int MaxFrameSize
        {
            get => Volatile.Read(ref _maxFrameSize);
            set
            {
                if (value < 4) throw new ArgumentOutOfRangeException(nameof(value));
                Volatile.Write(ref _maxFrameSize, value);
            }
        }

        public int MaxPendingEvents
        {
            get => Volatile.Read(ref _maxPendingEvents);
            set
            {
                if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value));
                Volatile.Write(ref _maxPendingEvents, value);
            }
        }

        public bool IsConnected
        {
            get
            {
                lock (_stateLock)
                {
                    return _current != null && Volatile.Read(ref _current.Connected) != 0;
                }
            }
        }

        internal int CurrentGeneration
        {
            get
            {
                lock (_stateLock) return _current?.Generation ?? 0;
            }
        }

        public Task<bool> ConnectAsync(string host, int port)
        {
            return ConnectAsync(host, port, CancellationToken.None);
        }

        public async Task<bool> ConnectAsync(string host, int port, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(host)) throw new ArgumentException("Host is required.", nameof(host));
            if (port <= 0 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port));

            await _connectGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (IsConnected) return true;

                ConnectionContext previous;
                lock (_stateLock) previous = _current;
                if (previous != null) Cleanup(previous, "reconnect");

                var context = new ConnectionContext
                {
                    Generation = Interlocked.Increment(ref _generation),
                    Tcp = new TcpClient(),
                    Cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken),
                };

                lock (_stateLock) _current = context;
                try
                {
                    await context.Tcp.ConnectAsync(host, port).ConfigureAwait(false);

                    lock (_stateLock)
                    {
                        if (!ReferenceEquals(_current, context) || context.Closed != 0)
                        {
                            CloseContext(context);
                            return false;
                        }
                        context.Stream = context.Tcp.GetStream();
                        Volatile.Write(ref context.Connected, 1);
                    }

                    EnqueueControl(new NetEvent
                    {
                        Generation = context.Generation,
                        Type = NetEventType.Connected,
                    });
                    _ = ReceiveLoop(context);
                    return true;
                }
                catch (Exception ex)
                {
                    Cleanup(context, ex.Message);
                    return false;
                }
            }
            finally
            {
                _connectGate.Release();
            }
        }

        public void Send(int msgId, byte[] payload)
        {
            _ = SendAndForgetAsync(msgId, payload);
        }

        public async Task<bool> SendAsync(
            int msgId,
            byte[] payload,
            CancellationToken cancellationToken = default)
        {
            ConnectionContext context;
            lock (_stateLock) context = _current;
            if (context == null || Volatile.Read(ref context.Connected) == 0) return false;

            int payloadLength = payload?.Length ?? 0;
            int bodyLength = checked(4 + payloadLength);
            int frameLength = checked(4 + bodyLength);
            if (bodyLength > MaxFrameSize) throw new ArgumentOutOfRangeException(nameof(payload));

            byte[] buffer = ArrayPool<byte>.Shared.Rent(frameLength);
            bool writeStarted = false;
            try
            {
                WriteInt(buffer, 0, bodyLength);
                WriteInt(buffer, 4, msgId);
                if (payloadLength > 0)
                {
                    Buffer.BlockCopy(payload, 0, buffer, 8, payloadLength);
                }

                await context.SendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    lock (_stateLock)
                    {
                        if (!ReferenceEquals(_current, context) || context.Connected == 0) return false;
                    }
                    writeStarted = true;
                    await context.Stream.WriteAsync(
                        buffer, 0, frameLength, cancellationToken).ConfigureAwait(false);
                    return true;
                }
                finally
                {
                    context.SendGate.Release();
                }
            }
            catch (OperationCanceledException) when (!writeStarted)
            {
                return false;
            }
            catch (Exception ex)
            {
                Cleanup(context, ex.Message);
                return false;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        private async Task SendAndForgetAsync(int msgId, byte[] payload)
        {
            try { await SendAsync(msgId, payload).ConfigureAwait(false); }
            catch (Exception ex) { Log.Error($"[Network] send failed: {ex}"); }
        }

        public void Disconnect()
        {
            ConnectionContext context;
            lock (_stateLock) context = _current;
            if (context != null) Cleanup(context, "manual");
        }

        internal bool TryDequeue(out NetEvent netEvent)
        {
            if (_events.TryDequeue(out netEvent))
            {
                Interlocked.Decrement(ref _pendingEventCount);
                return true;
            }
            return false;
        }

        internal void ClearEvents()
        {
            while (TryDequeue(out _)) { }
        }

        private async Task ReceiveLoop(ConnectionContext context)
        {
            var header = new byte[4];
            try
            {
                while (!context.Cancellation.IsCancellationRequested)
                {
                    if (!await ReadExact(context, header, 4).ConfigureAwait(false)) break;
                    int bodyLength = ReadInt(header, 0);
                    if (bodyLength < 4 || bodyLength > MaxFrameSize)
                    {
                        Cleanup(context, $"bad frame length {bodyLength}");
                        return;
                    }

                    byte[] body = ArrayPool<byte>.Shared.Rent(bodyLength);
                    try
                    {
                        if (!await ReadExact(context, body, bodyLength).ConfigureAwait(false)) break;

                        int msgId = ReadInt(body, 0);
                        var payload = new byte[bodyLength - 4];
                        Buffer.BlockCopy(body, 4, payload, 0, payload.Length);

                        if (!TryEnqueueMessage(new NetEvent
                            {
                                Generation = context.Generation,
                                Type = NetEventType.Message,
                                Message = new NetMessage(msgId, payload),
                            }))
                        {
                            Cleanup(context, "receive event queue overflow");
                            return;
                        }
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(body);
                    }
                }
                Cleanup(context, "eof");
            }
            catch (OperationCanceledException)
            {
                Cleanup(context, "canceled");
            }
            catch (Exception ex)
            {
                Cleanup(context, ex.Message);
            }
        }

        private static async Task<bool> ReadExact(
            ConnectionContext context,
            byte[] buffer,
            int count)
        {
            int read = 0;
            while (read < count)
            {
                int length = await context.Stream.ReadAsync(
                    buffer, read, count - read, context.Cancellation.Token).ConfigureAwait(false);
                if (length <= 0) return false;
                read += length;
            }
            return true;
        }

        private void Cleanup(ConnectionContext context, string reason)
        {
            if (context == null || Interlocked.Exchange(ref context.Closed, 1) != 0) return;

            lock (_stateLock)
            {
                if (ReferenceEquals(_current, context)) _current = null;
            }
            Volatile.Write(ref context.Connected, 0);
            CloseContext(context);

            EnqueueControl(new NetEvent
            {
                Generation = context.Generation,
                Type = NetEventType.Disconnected,
                Reason = reason,
            });
        }

        private static void CloseContext(ConnectionContext context)
        {
            try { context.Cancellation?.Cancel(); } catch { }
            try { context.Stream?.Close(); } catch { }
            try { context.Tcp?.Close(); } catch { }
        }

        private bool TryEnqueueMessage(NetEvent netEvent)
        {
            int count = Interlocked.Increment(ref _pendingEventCount);
            if (count > MaxPendingEvents)
            {
                Interlocked.Decrement(ref _pendingEventCount);
                return false;
            }
            _events.Enqueue(netEvent);
            return true;
        }

        private void EnqueueControl(NetEvent netEvent)
        {
            Interlocked.Increment(ref _pendingEventCount);
            _events.Enqueue(netEvent);
        }

        private static void WriteInt(byte[] buffer, int offset, int value)
        {
            buffer[offset] = (byte)((value >> 24) & 0xFF);
            buffer[offset + 1] = (byte)((value >> 16) & 0xFF);
            buffer[offset + 2] = (byte)((value >> 8) & 0xFF);
            buffer[offset + 3] = (byte)(value & 0xFF);
        }

        private static int ReadInt(byte[] buffer, int offset)
        {
            return (buffer[offset] << 24)
                 | (buffer[offset + 1] << 16)
                 | (buffer[offset + 2] << 8)
                 | buffer[offset + 3];
        }
    }
}
