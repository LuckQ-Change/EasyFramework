using System;
using System.Threading;
using System.Threading.Tasks;

namespace EasyFramework
{
    /// <summary>从宿主独占的传输端读取事件，并在帧循环按预算派发。</summary>
    public class NetworkModule : Module<NetworkModule>, IFrameUpdate
    {
        private readonly INetworkTransport _transport;
        private int _maxEventsPerUpdate = 256;

        /// <summary>为此模块接管一份独立的传输端。</summary>
        /// <param name="transport">由模块在宿主停止时释放的传输端。</param>
        public NetworkModule(INetworkTransport transport)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        /// <summary>当前传输连接是否可用。</summary>
        public bool IsConnected => _transport.IsConnected;
        /// <summary>每帧最多读取的传输事件数。</summary>
        public int MaxEventsPerUpdate
        {
            get => _maxEventsPerUpdate;
            set
            {
                if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value));
                _maxEventsPerUpdate = value;
            }
        }
        /// <summary>传输端接收消息的队列预算。</summary>
        public int MaxPendingEvents
        {
            get => _transport.MaxPendingEvents;
            set
            {
                if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value));
                _transport.MaxPendingEvents = value;
            }
        }

        /// <summary>在宿主帧内通知连接已建立。</summary>
        public event Action OnConnected;
        /// <summary>在宿主帧内通知当前连接断开及其原因。</summary>
        public event Action<string> OnDisconnected;
        /// <summary>在宿主帧内通知收到一条已解帧的消息。</summary>
        public event Action<NetMessage> OnMessage;

        /// <summary>使用当前模块的传输端连接指定端点。</summary>
        /// <param name="host">目标主机名或 IP 地址。</param>
        /// <param name="port">目标端口。</param>
        /// <param name="cancellationToken">取消连接的令牌。</param>
        /// <returns>连接可用时返回 true。</returns>
        public Task<bool> ConnectAsync(string host, int port, CancellationToken cancellationToken = default)
        {
            return _transport.ConnectAsync(host, port, cancellationToken);
        }

        /// <summary>提交消息发送并记录抛出的异常。</summary>
        /// <param name="msgId">消息 ID。</param>
        /// <param name="payload">消息负载，可为空。</param>
        public void Send(int msgId, byte[] payload)
        {
            _ = SendAndForgetAsync(msgId, payload);
        }

        /// <summary>通过传输端异步发送消息。</summary>
        /// <param name="msgId">消息 ID。</param>
        /// <param name="payload">消息负载，可为空。</param>
        /// <param name="cancellationToken">取消发送的令牌。</param>
        /// <returns>发送成功时返回 true。</returns>
        public Task<bool> SendAsync(
            int msgId,
            byte[] payload,
            CancellationToken cancellationToken = default)
        {
            return _transport.SendAsync(msgId, payload, cancellationToken);
        }

        /// <summary>主动断开当前连接。</summary>
        public void Disconnect()
        {
            _transport.Disconnect();
        }

        void IFrameUpdate.Tick(FrameTime frameTime)
        {
            int budget = MaxEventsPerUpdate;
            int processed = 0;
            while (processed++ < budget && _transport.TryDequeue(out NetworkTransportEvent transportEvent))
            {
                // 丢弃旧连接的事件，预算仍计入本帧已读取的所有条目。
                if (transportEvent.Generation != _transport.LatestGeneration) continue;

                try
                {
                    switch (transportEvent.Type)
                    {
                        case NetworkTransportEventType.Connected:
                            OnConnected?.Invoke();
                            break;
                        case NetworkTransportEventType.Disconnected:
                            OnDisconnected?.Invoke(transportEvent.Reason);
                            break;
                        case NetworkTransportEventType.Message:
                            OnMessage?.Invoke(transportEvent.Message);
                            break;
                    }
                }
                catch (Exception exception)
                {
                    Log.Error("[Network] 派发传输事件失败。", exception);
                }
            }
        }

        protected override void OnShutdown()
        {
            OnConnected = null;
            OnDisconnected = null;
            OnMessage = null;
            _transport.Dispose();
        }

        private async Task SendAndForgetAsync(int msgId, byte[] payload)
        {
            try { await SendAsync(msgId, payload).ConfigureAwait(false); }
            catch (Exception exception) { Log.Error("[Network] 发送消息失败。", exception); }
        }
    }
}
