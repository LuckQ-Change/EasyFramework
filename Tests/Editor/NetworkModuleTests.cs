using System;
using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace EasyFramework.Tests
{
    public class NetworkModuleTests
    {
        [UnityTest]
        public IEnumerator Loopback_SendAndReceive_UsesFramingAndMainThreadDispatch()
        {
            Task task = LoopbackCore();
            while (!task.IsCompleted) yield return null;
            if (task.IsFaulted) throw task.Exception.InnerException;
        }

        [UnityTest]
        public IEnumerator Reconnect_DropsQueuedEventsFromPreviousConnection()
        {
            Task task = ReconnectCore();
            while (!task.IsCompleted) yield return null;
            if (task.IsFaulted) throw task.Exception.InnerException;
        }

        private static async Task ReconnectCore()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            var manager = new ModuleManager();
            listener.Start();
            try
            {
                manager.Register<NetworkModule>();
                manager.InitAll();
                int connectedCalls = 0;
                int disconnectedCalls = 0;
                NetworkModule.Instance.OnConnected += () => connectedCalls++;
                NetworkModule.Instance.OnDisconnected += _ => disconnectedCalls++;
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;

                var firstAccept = listener.AcceptTcpClientAsync();
                Assert.IsTrue(await NetworkModule.Instance.ConnectAsync("127.0.0.1", port));
                using (var firstServer = await firstAccept)
                {
                    NetworkModule.Instance.Disconnect();
                    var secondAccept = listener.AcceptTcpClientAsync();
                    Assert.IsTrue(await NetworkModule.Instance.ConnectAsync("127.0.0.1", port));
                    using (var secondServer = await secondAccept)
                    {
                        manager.Update(0f);
                        Assert.AreEqual(1, connectedCalls);
                        Assert.AreEqual(0, disconnectedCalls);
                        Assert.IsTrue(NetworkModule.Instance.IsConnected);
                    }
                }
            }
            finally
            {
                manager.ShutdownAll();
                listener.Stop();
            }
        }

        private static async Task LoopbackCore()
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
                    await server.GetStream().WriteAsync(incomingFrame, 0, 5);
                    await Task.Delay(20);
                    manager.Update(0f);
                    Assert.IsNull(received, "半包尚未到齐时不能派发消息。");
                    await server.GetStream().WriteAsync(incomingFrame, 5, incomingFrame.Length - 5);

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
