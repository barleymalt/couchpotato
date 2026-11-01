using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Couchpotato.WebOs
{
    public sealed class FoundTv
    {
        public IPAddress Address;
        public string Name;
        public override string ToString() => Name + " (" + Address + ")";
    }

    /// <summary>Finds LG webOS TVs on the local network with SSDP, the same way LG's phone app does.</summary>
    public static class TvFinder
    {
        const string SearchTarget = "urn:lge-com:service:webos-second-screen:1";

        public static async Task<IList<FoundTv>> SearchAsync(TimeSpan duration)
        {
            var request = Encoding.ASCII.GetBytes(
                "M-SEARCH * HTTP/1.1\r\nHOST: 239.255.255.250:1900\r\nMAN: \"ssdp:discover\"\r\nMX: 2\r\nST: " + SearchTarget + "\r\n\r\n");
            var target = new IPEndPoint(IPAddress.Parse("239.255.255.250"), 1900);
            var found = new Dictionary<IPAddress, (string Name, string Location)>();

            // One socket per interface: on PCs with VPN or Hyper-V adapters the default route may not reach the TV.
            var sockets = LocalIPv4Addresses().Select(a =>
            {
                try { return new UdpClient(new IPEndPoint(a, 0)); }
                catch (SocketException) { return null; }
            }).Where(s => s != null).ToList();

            try
            {
                foreach (var s in sockets)
                {
                    try { await s.SendAsync(request, request.Length, target).ConfigureAwait(false); }
                    catch (SocketException) { }
                }

                var deadline = DateTime.UtcNow + duration;
                var pending = sockets.ToDictionary(s => s, s => s.ReceiveAsync());
                while (pending.Count > 0)
                {
                    var remaining = deadline - DateTime.UtcNow;
                    if (remaining <= TimeSpan.Zero) break;
                    var timeout = Task.Delay(remaining);
                    var done = await Task.WhenAny(pending.Values.Cast<Task>().Concat(new[] { timeout })).ConfigureAwait(false);
                    if (done == timeout) break;

                    var socket = pending.First(p => p.Value == done).Key;
                    try
                    {
                        var result = await pending[socket].ConfigureAwait(false);
                        ParseResponse(Encoding.UTF8.GetString(result.Buffer), result.RemoteEndPoint.Address, found);
                        pending[socket] = socket.ReceiveAsync();
                    }
                    catch (Exception e) when (e is SocketException || e is ObjectDisposedException)
                    {
                        pending.Remove(socket);
                    }
                }
            }
            finally
            {
                foreach (var s in sockets) s.Close();
            }

            var tvs = new List<FoundTv>();
            foreach (var kv in found)
            {
                var name = kv.Value.Name ?? await FetchFriendlyNameAsync(kv.Value.Location).ConfigureAwait(false) ?? "LG TV";
                tvs.Add(new FoundTv { Address = kv.Key, Name = name });
            }
            return tvs.OrderBy(t => t.Name).ToList();
        }

        static void ParseResponse(string text, IPAddress from, Dictionary<IPAddress, (string, string)> found)
        {
            if (!text.StartsWith("HTTP/1.1 200", StringComparison.OrdinalIgnoreCase)) return;
            if (text.IndexOf(SearchTarget, StringComparison.OrdinalIgnoreCase) < 0) return;
            string Header(string name)
            {
                var m = Regex.Match(text, "^" + Regex.Escape(name) + @":\s*(.*?)\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase);
                return m.Success ? m.Groups[1].Value : null;
            }
            var dlnaName = Header("DLNADeviceName.lge.com");
            found[from] = (dlnaName != null ? Uri.UnescapeDataString(dlnaName) : null, Header("LOCATION"));
        }

        static async Task<string> FetchFriendlyNameAsync(string location)
        {
            if (!Uri.TryCreate(location, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttp) return null;
            try
            {
                using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) })
                {
                    var xml = XDocument.Parse(await http.GetStringAsync(uri).ConfigureAwait(false));
                    return xml.Descendants().FirstOrDefault(e => e.Name.LocalName == "friendlyName")?.Value;
                }
            }
            catch (Exception) { return null; }
        }

        static IEnumerable<IPAddress> LocalIPv4Addresses() =>
            NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback && n.SupportsMulticast)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Select(u => u.Address)
                .Where(a => a.AddressFamily == AddressFamily.InterNetwork);
    }
}
