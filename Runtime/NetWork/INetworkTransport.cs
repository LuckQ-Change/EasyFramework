using System;
using System.Threading;
using System.Threading.Tasks;

namespace EasyFramework
{
    /// <summary>供所属宿主的网络模块独占使用的传输端。释放时关闭连接并清空队列。</summary>
    public interface INetworkTransport : IDisposable
    {
        /// <summary>当前连接是否可发送消息。</summary>
        bool IsConnected { get; }

        /// <summary>接收消息的队列预算，由传输端在入队时执行。</summary>
        int MaxPendingEvents { get; set; }

        /// <summary>最新连接尝试的代数。每次尝试使用新值，断开后保留该值以便派发断开事件。</summary>
        int LatestGeneration { get; }

        /// <summary>尝试连接指定端点。</summary>
        /// <param name="host">目标主机名或 IP 地址。</param>
        /// <param name="port">目标端口。</param>
        /// <param name="cancellationToken">取消连接操作的令牌。</param>
        /// <returns>连接可用时返回 true。</returns>
        Task<bool> ConnectAsync(string host, int port, CancellationToken cancellationToken);

        /// <summary>发送一条消息。</summary>
        /// <param name="msgId">消息 ID。</param>
        /// <param name="payload">消息负载，可为空。</param>
        /// <param name="cancellationToken">取消发送操作的令牌。</param>
        /// <returns>发送成功时返回 true。</returns>
        Task<bool> SendAsync(int msgId, byte[] payload, CancellationToken cancellationToken);

        /// <summary>主动断开当前连接，释放前仍可建立下一次连接。</summary>
        void Disconnect();

        /// <summary>按入队顺序读取一条已准备好的事件，队列为空时立即返回。</summary>
        /// <param name="transportEvent">读到的事件，含来源连接的代数。</param>
        /// <returns>读到事件时返回 true。</returns>
        bool TryDequeue(out NetworkTransportEvent transportEvent);
    }

    /// <summary>传输端提交给网络模块的事件类型。</summary>
    public enum NetworkTransportEventType
    {
        Connected,
        Disconnected,
        Message,
    }

    /// <summary>传输队列的一条事件，包含所属连接的代数。</summary>
    public readonly struct NetworkTransportEvent
    {
        /// <summary>事件所属的连接代数。</summary>
        public int Generation { get; }
        /// <summary>事件类型。</summary>
        public NetworkTransportEventType Type { get; }
        /// <summary>消息事件携带的解帧数据。传输端入队后不再修改该数据。</summary>
        public NetMessage Message { get; }
        /// <summary>断开事件携带的原因。</summary>
        public string Reason { get; }

        /// <summary>创建一条可跨线程入队的传输事件。</summary>
        /// <param name="generation">事件所属的连接代数。</param>
        /// <param name="type">事件类型。</param>
        /// <param name="message">已解帧的消息，仅消息事件填写。</param>
        /// <param name="reason">断开原因，仅断开事件填写。</param>
        public NetworkTransportEvent(int generation, NetworkTransportEventType type, NetMessage message = null,
            string reason = null)
        {
            Generation = generation;
            Type = type;
            Message = message;
            Reason = reason;
        }
    }
}
