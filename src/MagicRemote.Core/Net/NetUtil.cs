using System;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace MagicRemote.Net
{
    public sealed class LocalInterface
    {
        public IPAddress Address;
        public IPAddress Broadcast;
        public string Mac;
    }

    public static class NetUtil
    {
        /// <summary>True for loopback, RFC 1918, link-local and CGNAT addresses: the only peers the agent talks to.</summary>
        public static bool IsLocalNetwork(IPAddress ip)
        {
            if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
            if (IPAddress.IsLoopback(ip)) return true;
            if (ip.AddressFamily != AddressFamily.InterNetwork) return false;
            var b = ip.GetAddressBytes();
            return b[0] == 10
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                || (b[0] == 192 && b[1] == 168)
                || (b[0] == 169 && b[1] == 254)
                || (b[0] == 100 && b[1] >= 64 && b[1] <= 127);
        }

        /// <summary>True for loopback and for any address of this machine's network adapters.</summary>
        public static bool IsOwnAddress(IPAddress ip)
        {
            if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
            return IPAddress.IsLoopback(ip) ||
                   NetworkInterface.GetAllNetworkInterfaces().Any(n => n.GetIPProperties().UnicastAddresses.Any(u => u.Address.Equals(ip)));
        }

        /// <summary>
        /// The local address Windows uses to reach <paramref name="peer"/>, with the MAC address and
        /// subnet broadcast of its network adapter (for Wake-on-LAN). Null if there is no route.
        /// </summary>
        public static LocalInterface FindInterfaceFor(IPAddress peer)
        {
            IPAddress local;
            try
            {
                // Connecting a UDP socket sends nothing; it only asks the routing table for the source address.
                using (var probe = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                {
                    probe.Connect(peer, 9);
                    local = ((IPEndPoint)probe.LocalEndPoint).Address;
                }
            }
            catch (SocketException)
            {
                return null;
            }

            var result = new LocalInterface { Address = local, Broadcast = IPAddress.Broadcast };
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                var ua = nic.GetIPProperties().UnicastAddresses.FirstOrDefault(u => u.Address.Equals(local));
                if (ua == null) continue;
                if (ua.IPv4Mask != null)
                {
                    var a = local.GetAddressBytes();
                    var m = ua.IPv4Mask.GetAddressBytes();
                    var broadcast = new byte[4];
                    for (int i = 0; i < 4; i++) broadcast[i] = (byte)(a[i] | ~m[i]);
                    result.Broadcast = new IPAddress(broadcast);
                }
                var mac = nic.GetPhysicalAddress().GetAddressBytes();
                if (mac.Length == 6) result.Mac = string.Join(":", mac.Select(x => x.ToString("x2")));
                break;
            }
            return result;
        }
    }
}
