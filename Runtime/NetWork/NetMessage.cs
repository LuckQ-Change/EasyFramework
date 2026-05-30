namespace EasyFramework
{
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
