using System;
using System.Threading.Tasks;

namespace EasyFramework
{
    public class NetworkModule : ModuleSingleton<NetworkModule>
    {
        private readonly TcpNetClient _client = new TcpNetClient();

        public bool IsConnected => _client.IsConnected;

        public event Action OnConnected;
        public event Action<string> OnDisconnected;
        public event Action<NetMessage> OnMessage;

        public Task<bool> ConnectAsync(string host, int port)
        {
            return _client.ConnectAsync(host, port);
        }

        public void Send(int msgId, byte[] payload)
        {
            _client.Send(msgId, payload);
        }

        public void Disconnect()
        {
            _client.Disconnect();
        }

        protected override void OnUpdate(float deltaTime)
        {
            while (_client.Events.TryDequeue(out var ev))
            {
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
                    Log.Error($"[Network] dispatch error: {ex}");
                }
            }
        }

        protected override void OnShutdown()
        {
            _client.Disconnect();
        }
    }
}
