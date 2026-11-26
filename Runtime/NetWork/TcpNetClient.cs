using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace EasyFramework
{
    /// <summary>提供基于 TCP 的分帧传输，按连接代数隔离收发与关闭。</summary>
    public class TcpNetClient : INetworkTransport
    {
        /// <summary>单次连接持有的套接字、取消令牌和关闭状态。</summary>
        private sealed class ConnectionContext
        {
            public int Generation;
            public TcpClient Tcp;
            public NetworkStream Stream;
            public CancellationTokenSource Cancellation;
            public CancellationToken Token;
            public readonly SemaphoreSlim SendGate = new SemaphoreSlim(1, 1);
            public int Connected;
            public bool IsClosed;
        }

        private readonly object _stateLock = new object();
        private readonly SemaphoreSlim _connectGate = new SemaphoreSlim(1, 1);
        private readonly ConcurrentQueue<NetworkTransportEvent> _events = new ConcurrentQueue<NetworkTransportEvent>();
        private ConnectionContext _current;
        private bool _isDisposed;
        private int _generation;
        private int _pendingEventCount;
        private int _maxFrameSize = 1024 * 1024;
        private int _maxPendingEvents = 4096;

        #region 公共方法

        /// <summary>单帧正文允许的最大字节数，包含四字节消息 ID。</summary>
        public int MaxFrameSize
        {
            get => Volatile.Read(ref _maxFrameSize);
            set
            {
                if (value < 4) throw new ArgumentOutOfRangeException(nameof(value));
                Volatile.Write(ref _maxFrameSize, value);
            }
        }

        /// <summary>接收消息的队列预算，包含已排队的控制事件。</summary>
        public int MaxPendingEvents
        {
            get => Volatile.Read(ref _maxPendingEvents);
            set
            {
                if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value));
                Volatile.Write(ref _maxPendingEvents, value);
            }
        }

        /// <summary>当前连接是否已建立且可用。</summary>
        public bool IsConnected
        {
            get
            {
                lock (_stateLock) return IsConnectionUsable(_current);
            }
        }

        /// <summary>最近一次连接尝试的代数，断开后仍保留供事件派发判断。</summary>
        public int LatestGeneration => Volatile.Read(ref _generation);

        /// <summary>连接指定 TCP 地址和端口。</summary>
        /// <param name="host">目标主机名或 IP 地址。</param>
        /// <param name="port">目标 TCP 端口。</param>
        /// <returns>连接建立或已有可用连接时返回 true。</returns>
        public Task<bool> ConnectAsync(string host, int port)
        {
            return ConnectAsync(host, port, CancellationToken.None);
        }

        /// <summary>建立可取消的 TCP 连接。</summary>
        /// <param name="host">目标主机名或 IP 地址。</param>
        /// <param name="port">目标 TCP 端口。</param>
        /// <param name="cancellationToken">取消排队、拨号或后续连接的令牌。</param>
        /// <returns>连接建立或已有可用连接时返回 true。</returns>
        public async Task<bool> ConnectAsync(string host, int port, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(host)) throw new ArgumentException("Host is required.", nameof(host));
            if (port <= 0 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port));

            await _connectGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                ConnectionContext previous;
                lock (_stateLock)
                {
                    ThrowIfDisposed();
                    if (IsConnectionUsable(_current)) return true;
                    previous = _current;
                }
                if (previous != null) Cleanup(previous, "reconnect");

                ConnectionContext context;
                lock (_stateLock)
                {
                    // 终止与发布新连接使用同一把锁，防止等待拨号的任务在停止后重新建连。
                    ThrowIfDisposed();
                    cancellationToken.ThrowIfCancellationRequested();
                    context = new ConnectionContext
                    {
                        Generation = Interlocked.Increment(ref _generation),
                        Tcp = new TcpClient(),
                        Cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken),
                    };
                    context.Token = context.Cancellation.Token;
                    _current = context;
                }

                try
                {
                    await ConnectSocketAsync(context, host, port).ConfigureAwait(false);

                    lock (_stateLock)
                    {
                        if (!ReferenceEquals(_current, context) || context.IsClosed) return false;
                        context.Stream = context.Tcp.GetStream();
                        context.Token.ThrowIfCancellationRequested();

                        // 建连状态和事件一起提交，断开时由同一把锁决定先后顺序。
                        Volatile.Write(ref context.Connected, 1);
                        EnqueueControl(new NetworkTransportEvent(context.Generation, NetworkTransportEventType.Connected));
                    }

                    _ = ReceiveLoop(context);
                    return true;
                }
                catch (Exception ex)
                {
                    Cleanup(context, context.Token.IsCancellationRequested ? "canceled" : ex.Message);
                    return false;
                }
            }
            finally
            {
                _connectGate.Release();
            }
        }

        /// <summary>提交消息发送并记录抛出的异常。</summary>
        /// <param name="msgId">消息 ID。</param>
        /// <param name="payload">消息负载，可为空。</param>
        public void Send(int msgId, byte[] payload)
        {
            _ = SendAndForgetAsync(msgId, payload);
        }

        /// <summary>将消息编码成完整帧并写入当前连接。</summary>
        /// <param name="msgId">消息 ID。</param>
        /// <param name="payload">消息负载，可为空。</param>
        /// <param name="cancellationToken">取消等待发送或写入的令牌。</param>
        /// <returns>写入成功时返回 true。</returns>
        public async Task<bool> SendAsync(
            int msgId,
            byte[] payload,
            CancellationToken cancellationToken = default)
        {
            ConnectionContext context;
            lock (_stateLock) context = _current;
            if (!IsConnectionUsable(context)) return false;

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
                        if (!ReferenceEquals(_current, context) || context.Connected == 0 || context.Token.IsCancellationRequested)
                            return false;
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
                Cleanup(context, context.Token.IsCancellationRequested ? "canceled" : ex.Message);
                return false;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        /// <summary>取消当前拨号或主动断开已建立的连接。</summary>
        public void Disconnect()
        {
            ConnectionContext context;
            lock (_stateLock) context = _current;
            if (context != null) Cleanup(context, "manual");
        }

        /// <summary>按队列顺序读取一个事件，队列为空时立即返回。</summary>
        /// <param name="transportEvent">读到的连接、消息或断开事件。</param>
        /// <returns>读到事件时返回 true。</returns>
        public bool TryDequeue(out NetworkTransportEvent transportEvent)
        {
            if (_events.TryDequeue(out transportEvent))
            {
                Interlocked.Decrement(ref _pendingEventCount);
                return true;
            }
            return false;
        }

        #endregion

        #region 生命周期

        /// <summary>关闭套接字和队列，并停止接受新的连接尝试。</summary>
        public void Dispose()
        {
            ConnectionContext context;
            lock (_stateLock)
            {
                if (_isDisposed) return;
                _isDisposed = true;
                context = _current;
                _current = null;
            }
            if (context != null) Cleanup(context, "disposed");
            while (TryDequeue(out _)) { }
        }

        #endregion

        #region 私有逻辑

        private void ThrowIfDisposed()
        {
            if (_isDisposed) throw new ObjectDisposedException(nameof(TcpNetClient));
        }

        private static bool IsConnectionUsable(ConnectionContext context) =>
            context != null && Volatile.Read(ref context.Connected) != 0 && !context.Token.IsCancellationRequested;

        private async Task SendAndForgetAsync(int msgId, byte[] payload)
        {
            try { await SendAsync(msgId, payload).ConfigureAwait(false); }
            catch (Exception ex) { Log.Error("[Network] send failed.", ex); }
        }

        private static async Task ConnectSocketAsync(ConnectionContext context, string host, int port)
        {
            // 底层拨号不接收令牌，取消时关闭本次套接字并结束等待，释放连接队列。
            var canceled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = context.Token.Register(() =>
            {
                canceled.TrySetResult(true);
                CloseTcp(context.Tcp);
            });
            context.Token.ThrowIfCancellationRequested();

            Task connect = context.Tcp.ConnectAsync(host, port);
            if (await Task.WhenAny(connect, canceled.Task).ConfigureAwait(false) != connect)
            {
                _ = ObserveCanceledConnectAsync(connect);
                context.Token.ThrowIfCancellationRequested();
            }
            await connect.ConfigureAwait(false);
        }

        private static async Task ObserveCanceledConnectAsync(Task connect)
        {
            // 已取消的底层拨号可能延迟完成，观察其异常以免影响后续连接。
            try { await connect.ConfigureAwait(false); }
            catch { }
        }

        private async Task ReceiveLoop(ConnectionContext context)
        {
            var header = new byte[4];
            try
            {
                // 取消连接时关闭套接字，让等待中的读取退出。
                using var registration = context.Token.Register(() => CloseTcp(context.Tcp));
                while (!context.Token.IsCancellationRequested)
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

                        if (!TryEnqueueMessage(context, new NetMessage(msgId, payload)))
                        {
                            Cleanup(context, context.Token.IsCancellationRequested ? "canceled" : "receive event queue overflow");
                            return;
                        }
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(body);
                    }
                }
                Cleanup(context, context.Token.IsCancellationRequested ? "canceled" : "eof");
            }
            catch (OperationCanceledException)
            {
                Cleanup(context, "canceled");
            }
            catch (Exception ex)
            {
                Cleanup(context, context.Token.IsCancellationRequested ? "canceled" : ex.Message);
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
                    buffer, read, count - read, context.Token).ConfigureAwait(false);
                if (length <= 0) return false;
                read += length;
            }
            return true;
        }

        private void Cleanup(ConnectionContext context, string reason)
        {
            if (context == null) return;

            lock (_stateLock)
            {
                if (context.IsClosed) return;
                context.IsClosed = true;
                if (ReferenceEquals(_current, context)) _current = null;
                Volatile.Write(ref context.Connected, 0);
            }
            CloseContext(context);

            lock (_stateLock)
            {
                if (_isDisposed) return;
                EnqueueControl(new NetworkTransportEvent(context.Generation, NetworkTransportEventType.Disconnected,
                    reason: reason));
            }
        }

        private static void CloseContext(ConnectionContext context)
        {
            try { context.Cancellation?.Cancel(); } catch { }
            try { context.Stream?.Close(); } catch { }
            CloseTcp(context.Tcp);
            try { context.Cancellation?.Dispose(); } catch { }
        }

        private static void CloseTcp(TcpClient tcp)
        {
            try { tcp?.Close(); } catch { }
        }

        private bool TryEnqueueMessage(ConnectionContext context, NetMessage message)
        {
            // 断开已提交时丢弃刚读完的帧，避免断开后继续派发同一连接的消息。
            lock (_stateLock)
            {
                if (!ReferenceEquals(_current, context) || context.IsClosed || context.Token.IsCancellationRequested)
                    return false;
                int count = Interlocked.Increment(ref _pendingEventCount);
                if (count > MaxPendingEvents)
                {
                    Interlocked.Decrement(ref _pendingEventCount);
                    return false;
                }
                _events.Enqueue(new NetworkTransportEvent(context.Generation, NetworkTransportEventType.Message, message));
                return true;
            }
        }

        private void EnqueueControl(NetworkTransportEvent transportEvent)
        {
            Interlocked.Increment(ref _pendingEventCount);
            _events.Enqueue(transportEvent);
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

        #endregion
    }
}
