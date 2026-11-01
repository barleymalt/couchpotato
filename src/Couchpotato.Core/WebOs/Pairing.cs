using System;
using System.Linq;
using System.Net;
using Couchpotato.Config;
using Couchpotato.Net;
using Couchpotato.Protocol;

namespace Couchpotato.WebOs
{
    /// <summary>
    /// A new TV pairing. The key travels to the TV only inside the SSH session used for installing,
    /// as launch parameters of the TV app, so it never crosses the network in the clear.
    /// </summary>
    public sealed class Pairing
    {
        public byte[] TvId { get; private set; }
        public byte[] Key { get; private set; }
        /// <summary>Launch parameters for the TV app. Contains only shell-safe characters.</summary>
        public string LaunchParamsJson { get; private set; }

        /// <summary>True when an existing pairing was kept (setting up the same TV again, e.g. to update the TV app).</summary>
        public bool Reused { get; private set; }

        public static Pairing Create(ConfigStore store, string pcName, IPAddress tvAddress, int port)
        {
            var iface = NetUtil.FindInterfaceFor(tvAddress);
            var existing = store.FindTvByAddress(tvAddress.ToString(), out var existingKey);
            var p = existing != null
                ? new Pairing { TvId = ConfigStore.FromHex(existing.Id), Key = existingKey, Reused = true }
                : new Pairing { TvId = Handshake.RandomBytes(Handshake.IdLength), Key = Handshake.RandomBytes(32) };
            p.LaunchParamsJson =
                "{\"pair\":{" +
                "\"v\":1," +
                "\"tvId\":\"" + ConfigStore.Hex(p.TvId) + "\"," +
                "\"key\":\"" + Convert.ToBase64String(p.Key) + "\"," +
                "\"pcId\":\"" + ConfigStore.Hex(store.PcId) + "\"," +
                "\"pcName\":\"" + Sanitize(pcName) + "\"," +
                "\"host\":\"" + (iface?.Address.ToString() ?? "") + "\"," +
                "\"port\":" + port + "," +
                "\"mac\":\"" + (iface?.Mac ?? "") + "\"," +
                "\"broadcast\":\"" + (iface?.Broadcast.ToString() ?? "255.255.255.255") + "\"" +
                "}}";
            return p;
        }

        static string Sanitize(string name)
        {
            var clean = new string((name ?? "").Where(c => c < 128 && (char.IsLetterOrDigit(c) || c == ' ' || c == '-' || c == '_' || c == '.')).ToArray()).Trim();
            return clean.Length > 32 ? clean.Substring(0, 32) : clean.Length == 0 ? "PC" : clean;
        }
    }
}
