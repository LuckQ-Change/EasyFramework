using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using NUnit.Framework;

namespace EasyFramework.Tests
{
    public class NetworkModuleTests
    {
        [Test]
        public async Task Loopback_SendAndReceive_UsesFramingAndMainThreadDispatch()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            var manager = new ModuleManager();
            listener.Start();
            try
            {
                manager.Register<NetworkModule>();
                manager.InitAll();

                int connectedCalls = 0;
                NetMessage received = null;
                NetworkModule.Instance.OnConnected += () => connectedCalls++;
                NetworkModule.Instance.OnMessage += message => received = message;

                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                var acceptTask = listener.AcceptTcpClientAsync();
                Assert.IsTrue(await NetworkModule.Instance.ConnectAsync("127.0.0.1", port));
                using (var server = await acceptTask)
                {
                    manager.Update(0f);
                    Assert.AreEqual(1, connectedCalls);

                    byte[] outgoing = { 1, 2, 3 };
                    Assert.IsTrue(await NetworkModule.Instance.SendAsync(1001, outgoing));
                    byte[] sentFrame = await ReadExact(server.GetStream(), 11);
                    CollectionAssert.AreEqual(
                        new byte[] { 0, 0, 0, 7, 0, 0, 3, 233, 1, 2, 3 },
                        sentFrame);

                    byte[] incomingFrame = { 0, 0, 0, 6, 0, 0, 7, 210, 9, 8 };
                    await server.GetStream().WriteAsync(incomingFrame, 0, incomingFrame.Length);

                    for (int i = 0; i < 100 && received == null; i++)
                    {
                        await Task.Delay(10);
                        manager.Update(0f);
                    }

                    Assert.IsNotNull(received);
                    Assert.AreEqual(2002, received.MsgId);
                    CollectionAssert.AreEqual(new byte[] { 9, 8 }, received.Payload);
                }
            }
            finally
            {
                manager.ShutdownAll();
                listener.Stop();
            }
        }

        private static async Task<byte[]> ReadExact(NetworkStream stream, int count)
        {
            var buffer = new byte[count];
            int read = 0;
            while (read < count)
            {
                int length = await stream.ReadAsync(buffer, read, count - read);
                if (length == 0) throw new InvalidOperationException("Unexpected end of stream.");
                read += length;
            }
            return buffer;
        }
    }
}
