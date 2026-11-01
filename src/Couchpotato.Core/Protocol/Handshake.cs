using System;
using System.Security.Cryptography;
using System.Text;

namespace Couchpotato.Protocol
{
    /// <summary>
    /// Mutual challenge-response over a 256-bit pre-shared key, then per-direction session keys.
    /// See docs/DESIGN.md for the message layout.
    /// </summary>
    public static class Handshake
    {
        public const byte Version = 1;
        public const int IdLength = 16;
        public const int NonceLength = 16;
        public const int ProofLength = 32;
        public const int HelloLength = 3 + IdLength + NonceLength;
        public const int ChallengeLength = 3 + NonceLength + ProofLength;

        static readonly byte[] Magic = { (byte)'M', (byte)'R', Version };

        public static byte[] BuildHello(byte[] tvId, byte[] tvNonce) => Concat(Magic, tvId, tvNonce);

        public static bool TryParseHello(byte[] m, out byte[] tvId, out byte[] tvNonce)
        {
            tvId = tvNonce = null;
            if (m == null || m.Length != HelloLength || !HasMagic(m)) return false;
            tvId = Slice(m, 3, IdLength);
            tvNonce = Slice(m, 3 + IdLength, NonceLength);
            return true;
        }

        public static byte[] BuildChallenge(byte[] key, byte[] tvId, byte[] tvNonce, byte[] pcNonce) =>
            Concat(Magic, pcNonce, Mac(key, "MR1 pc proof", tvId, tvNonce, pcNonce));

        /// <summary>TV side: checks the PC knows the key and returns its nonce.</summary>
        public static bool TryParseChallenge(byte[] key, byte[] tvId, byte[] tvNonce, byte[] m, out byte[] pcNonce)
        {
            pcNonce = null;
            if (m == null || m.Length != ChallengeLength || !HasMagic(m)) return false;
            var nonce = Slice(m, 3, NonceLength);
            var expected = Mac(key, "MR1 pc proof", tvId, tvNonce, nonce);
            if (!FixedTimeEquals(expected, 0, m, 3 + NonceLength, ProofLength)) return false;
            pcNonce = nonce;
            return true;
        }

        public static byte[] BuildResponse(byte[] key, byte[] tvId, byte[] tvNonce, byte[] pcNonce) =>
            Mac(key, "MR1 tv proof", tvId, tvNonce, pcNonce);

        public static bool VerifyResponse(byte[] key, byte[] tvId, byte[] tvNonce, byte[] pcNonce, byte[] m)
        {
            if (m == null || m.Length != ProofLength) return false;
            var expected = Mac(key, "MR1 tv proof", tvId, tvNonce, pcNonce);
            return FixedTimeEquals(expected, 0, m, 0, ProofLength);
        }

        public static SecureChannel CreateChannel(byte[] key, byte[] tvId, byte[] tvNonce, byte[] pcNonce, bool forPc)
        {
            var tvEnc = Mac(key, "MR1 tv>pc enc", tvId, tvNonce, pcNonce);
            var tvMac = Mac(key, "MR1 tv>pc mac", tvId, tvNonce, pcNonce);
            var pcEnc = Mac(key, "MR1 pc>tv enc", tvId, tvNonce, pcNonce);
            var pcMac = Mac(key, "MR1 pc>tv mac", tvId, tvNonce, pcNonce);
            return forPc
                ? new SecureChannel(pcEnc, pcMac, tvEnc, tvMac)
                : new SecureChannel(tvEnc, tvMac, pcEnc, pcMac);
        }

        public static byte[] RandomBytes(int n)
        {
            var b = new byte[n];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(b);
            return b;
        }

        internal static byte[] Mac(byte[] key, string label, params byte[][] parts)
        {
            using (var h = new HMACSHA256(key))
            {
                var l = Encoding.ASCII.GetBytes(label);
                h.TransformBlock(l, 0, l.Length, null, 0);
                foreach (var p in parts) h.TransformBlock(p, 0, p.Length, null, 0);
                h.TransformFinalBlock(new byte[0], 0, 0);
                return h.Hash;
            }
        }

        internal static bool FixedTimeEquals(byte[] a, int aOffset, byte[] b, int bOffset, int length)
        {
            int diff = 0;
            for (int i = 0; i < length; i++) diff |= a[aOffset + i] ^ b[bOffset + i];
            return diff == 0;
        }

        static bool HasMagic(byte[] m) => m[0] == Magic[0] && m[1] == Magic[1] && m[2] == Magic[2];

        static byte[] Slice(byte[] b, int offset, int length)
        {
            var r = new byte[length];
            Buffer.BlockCopy(b, offset, r, 0, length);
            return r;
        }

        static byte[] Concat(params byte[][] parts)
        {
            int n = 0;
            foreach (var p in parts) n += p.Length;
            var r = new byte[n];
            int o = 0;
            foreach (var p in parts) { Buffer.BlockCopy(p, 0, r, o, p.Length); o += p.Length; }
            return r;
        }
    }
}
