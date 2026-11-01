using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Couchpotato.Net
{
    /// <summary>
    /// Answers the TV's UDP broadcast "where is PC &lt;pcId&gt;?" so the TV finds the PC after its
    /// IP address changes. It reveals nothing but the port; authentication happens on connect.
    /// </summary>
    public sealed class DiscoveryResponder : IDisposable
    {
        static readonly byte[] Request = Encoding.ASCII.GetBytes("MRDISCOVER1");
        static readonly byte[] Reply = Encoding.ASCII.GetBytes("MRHERE1");

        readonly UdpClient _udp;
        readonly byte[] _pcId;
        readonly int _tcpPort;
        volatile bool _stopped;

        public DiscoveryResponder(int port, byte[] pcId, int tcpPort)
        {
            _pcId = pcId;
            _tcpPort = tcpPort;
            _udp = new UdpClient(new IPEndPoint(IPAddress.Any, port));
            Task.Run(RunAsync);
        }

        // Async receive: closing the socket ends it on every OS, where a blocked Receive() may not return.
        async Task RunAsync()
        {
            while (!_stopped)
            {
                try
                {
                    var request = await _udp.ReceiveAsync().ConfigureAwait(false);
                    var from = request.RemoteEndPoint;
                    if (!NetUtil.IsLocalNetwork(from.Address) || !IsRequestForUs(request.Buffer)) continue;

                    var reply = new byte[Reply.Length + _pcId.Length + 2];
                    Buffer.BlockCopy(Reply, 0, reply, 0, Reply.Length);
                    Buffer.BlockCopy(_pcId, 0, reply, Reply.Length, _pcId.Length);
                    reply[reply.Length - 2] = (byte)_tcpPort;
                    reply[reply.Length - 1] = (byte)(_tcpPort >> 8);
                    await _udp.SendAsync(reply, reply.Length, from).ConfigureAwait(false);
                }
                catch (SocketException) { if (_stopped) return; } // e.g. ICMP port unreachable from an earlier reply
                catch (ObjectDisposedException) { return; }
            }
        }

        bool IsRequestForUs(byte[] data)
        {
            if (data.Length != Request.Length + _pcId.Length) return false;
            for (int i = 0; i < Request.Length; i++) if (data[i] != Request[i]) return false;
            for (int i = 0; i < _pcId.Length; i++) if (data[Request.Length + i] != _pcId[i]) return false;
            return true;
        }

        public void Dispose()
        {
            _stopped = true;
            _udp.Close();
        }
    }
}
