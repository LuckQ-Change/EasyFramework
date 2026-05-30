using System;
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
        public NetEventType Type;
        public NetMessage Message;
        public string Reason;
    }

    public class TcpNetClient
    {
        private TcpClient _tcp;
        private NetworkStream _stream;
        private CancellationTokenSource _cts;
        private readonly object _writeLock = new object();
        internal readonly ConcurrentQueue<NetEvent> Events = new ConcurrentQueue<NetEvent>();

        public bool IsConnected { get; private set; }

        public async Task<bool> ConnectAsync(string host, int port)
        {
            if (IsConnected) return true;
            try
            {
                _tcp = new TcpClient();
                await _tcp.ConnectAsync(host, port).ConfigureAwait(false);
                _stream = _tcp.GetStream();
                _cts = new CancellationTokenSource();
                IsConnected = true;
                Events.Enqueue(new NetEvent { Type = NetEventType.Connected });
                _ = Task.Run(() => ReceiveLoop(_cts.Token));
                return true;
            }
            catch (Exception ex)
            {
                Cleanup(ex.Message);
                return false;
            }
        }

        public void Send(int msgId, byte[] payload)
        {
            if (!IsConnected) return;
            int payloadLen = payload?.Length ?? 0;
            int bodyLen = 4 + payloadLen;
            var buf = new byte[4 + bodyLen];

            WriteInt(buf, 0, bodyLen);
            WriteInt(buf, 4, msgId);
            if (payloadLen > 0)
            {
                Buffer.BlockCopy(payload, 0, buf, 8, payloadLen);
            }

            try
            {
                lock (_writeLock)
                {
                    _stream.Write(buf, 0, buf.Length);
                }
            }
            catch (Exception ex)
            {
                Cleanup(ex.Message);
            }
        }

        public void Disconnect()
        {
            Cleanup("manual");
        }

        private async Task ReceiveLoop(CancellationToken ct)
        {
            var header = new byte[4];
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    if (!await ReadExact(header, 4, ct).ConfigureAwait(false)) break;
                    int bodyLen = ReadInt(header, 0);
                    if (bodyLen < 4 || bodyLen > 1024 * 1024)
                    {
                        Cleanup($"bad frame length {bodyLen}");
                        return;
                    }

                    var body = new byte[bodyLen];
                    if (!await ReadExact(body, bodyLen, ct).ConfigureAwait(false)) break;

                    int msgId = ReadInt(body, 0);
                    var payload = new byte[bodyLen - 4];
                    Buffer.BlockCopy(body, 4, payload, 0, payload.Length);

                    Events.Enqueue(new NetEvent
                    {
                        Type = NetEventType.Message,
                        Message = new NetMessage(msgId, payload),
                    });
                }
                Cleanup("eof");
            }
            catch (Exception ex)
            {
                Cleanup(ex.Message);
            }
        }

        private async Task<bool> ReadExact(byte[] buf, int count, CancellationToken ct)
        {
            int read = 0;
            while (read < count)
            {
                int n = await _stream.ReadAsync(buf, read, count - read, ct).ConfigureAwait(false);
                if (n <= 0) return false;
                read += n;
            }
            return true;
        }

        private void Cleanup(string reason)
        {
            if (!IsConnected && _tcp == null) return;
            IsConnected = false;

            try { _cts?.Cancel(); } catch { }
            try { _stream?.Close(); } catch { }
            try { _tcp?.Close(); } catch { }

            _cts = null;
            _stream = null;
            _tcp = null;

            Events.Enqueue(new NetEvent { Type = NetEventType.Disconnected, Reason = reason });
        }

        private static void WriteInt(byte[] buf, int offset, int value)
        {
            buf[offset]     = (byte)((value >> 24) & 0xFF);
            buf[offset + 1] = (byte)((value >> 16) & 0xFF);
            buf[offset + 2] = (byte)((value >> 8) & 0xFF);
            buf[offset + 3] = (byte)(value & 0xFF);
        }

        private static int ReadInt(byte[] buf, int offset)
        {
            return (buf[offset] << 24)
                 | (buf[offset + 1] << 16)
                 | (buf[offset + 2] << 8)
                 | (buf[offset + 3]);
        }
    }
}
