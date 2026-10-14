namespace EasyFramework
{
    /// <summary>保存已解帧的消息 ID 和负载数据。</summary>
    public class NetMessage
    {
        public int MsgId;
        public byte[] Payload;

        public NetMessage()
        {
        }

        public NetMessage(int msgId, byte[] payload)
        {
            MsgId = msgId;
            Payload = payload;
        }
    }
}
