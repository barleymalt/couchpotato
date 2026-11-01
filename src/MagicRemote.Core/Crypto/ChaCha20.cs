using System;

namespace MagicRemote.Crypto
{
    /// <summary>ChaCha20 stream cipher as specified in RFC 8439. Not an AEAD: callers authenticate separately.</summary>
    public static class ChaCha20
    {
        public static void Xor(byte[] key, byte[] nonce, uint counter, byte[] buffer, int offset, int count)
        {
            if (key == null || key.Length != 32) throw new ArgumentException("ChaCha20 key must be 32 bytes", nameof(key));
            if (nonce == null || nonce.Length != 12) throw new ArgumentException("ChaCha20 nonce must be 12 bytes", nameof(nonce));

            var state = new uint[16];
            state[0] = 0x61707865; state[1] = 0x3320646e; state[2] = 0x79622d32; state[3] = 0x6b206574;
            for (int i = 0; i < 8; i++) state[4 + i] = ReadUInt32(key, i * 4);
            state[12] = counter;
            for (int i = 0; i < 3; i++) state[13 + i] = ReadUInt32(nonce, i * 4);

            var x = new uint[16];
            var block = new byte[64];
            while (count > 0)
            {
                Array.Copy(state, x, 16);
                for (int round = 0; round < 10; round++)
                {
                    QuarterRound(x, 0, 4, 8, 12); QuarterRound(x, 1, 5, 9, 13);
                    QuarterRound(x, 2, 6, 10, 14); QuarterRound(x, 3, 7, 11, 15);
                    QuarterRound(x, 0, 5, 10, 15); QuarterRound(x, 1, 6, 11, 12);
                    QuarterRound(x, 2, 7, 8, 13); QuarterRound(x, 3, 4, 9, 14);
                }
                for (int i = 0; i < 16; i++) WriteUInt32(block, i * 4, x[i] + state[i]);

                int n = Math.Min(64, count);
                for (int i = 0; i < n; i++) buffer[offset + i] ^= block[i];
                offset += n;
                count -= n;
                state[12]++;
            }
        }

        static void QuarterRound(uint[] x, int a, int b, int c, int d)
        {
            x[a] += x[b]; x[d] = Rotl(x[d] ^ x[a], 16);
            x[c] += x[d]; x[b] = Rotl(x[b] ^ x[c], 12);
            x[a] += x[b]; x[d] = Rotl(x[d] ^ x[a], 8);
            x[c] += x[d]; x[b] = Rotl(x[b] ^ x[c], 7);
        }

        static uint Rotl(uint v, int n) => (v << n) | (v >> (32 - n));

        static uint ReadUInt32(byte[] b, int i) => (uint)(b[i] | b[i + 1] << 8 | b[i + 2] << 16 | b[i + 3] << 24);

        static void WriteUInt32(byte[] b, int i, uint v)
        {
            b[i] = (byte)v; b[i + 1] = (byte)(v >> 8); b[i + 2] = (byte)(v >> 16); b[i + 3] = (byte)(v >> 24);
        }
    }
}
