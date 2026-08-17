using System;
using System.Threading;
using System.Threading.Tasks;

namespace EasyFramework
{
    public class NetworkModule : ModuleSingleton<NetworkModule>
    {
        private readonly TcpNetClient _client = new TcpNetClient();
        private int _maxEventsPerUpdate = 256;

        public bool IsConnected => _client.IsConnected;
        public int MaxEventsPerUpdate
        {
            get => _maxEventsPerUpdate;
            set
            {
                if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value));
                _maxEventsPerUpdate = value;
            }
        }
        public int MaxPendingEvents
        {
            get => _client.MaxPendingEvents;
            set => _client.MaxPendingEvents = value;
        }

        public event Action OnConnected;
        public event Action<string> OnDisconnected;
        public event Action<NetMessage> OnMessage;

        public Task<bool> ConnectAsync(string host, int port)
        {
            return _client.ConnectAsync(host, port);
        }

        public Task<bool> ConnectAsync(string host, int port, CancellationToken cancellationToken)
        {
            return _client.ConnectAsync(host, port, cancellationToken);
        }

        public void Send(int msgId, byte[] payload)
        {
            _client.Send(msgId, payload);
        }

        public Task<bool> SendAsync(
            int msgId,
            byte[] payload,
            CancellationToken cancellationToken = default)
        {
            return _client.SendAsync(msgId, payload, cancellationToken);
        }

        public void Disconnect()
        {
            _client.Disconnect();
        }

        protected override void OnUpdate(float deltaTime)
        {
            int budget = MaxEventsPerUpdate;
            int processed = 0;
            while (processed++ < budget && _client.TryDequeue(out var ev))
            {
                if (ev.Type == NetEventType.Message &&
                    ev.Generation != _client.CurrentGeneration) continue;

                try
                {
                    switch (ev.Type)
                    {
                        case NetEventType.Connected:
                            OnConnected?.Invoke();
                            break;
                        case NetEventType.Disconnected:
                            OnDisconnected?.Invoke(ev.Reason);
                            break;
                        case NetEventType.Message:
                            OnMessage?.Invoke(ev.Message);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("[Network] dispatch error.", ex);
                }
            }
        }

        protected override void OnShutdown()
        {
            _client.Disconnect();
            _client.ClearEvents();
            OnConnected = null;
            OnDisconnected = null;
            OnMessage = null;
        }
    }
}
