using System;
using System.Security.Cryptography;
using Couchpotato.Crypto;

namespace Couchpotato.Protocol
{
    /// <summary>
    /// Encrypt-then-MAC records: ChaCha20 ciphertext followed by a 16-byte truncated HMAC-SHA256 tag.
    /// Each direction has its own keys and an implicit sequence number, so replayed, dropped or
    /// reordered records fail authentication.
    /// </summary>
    public sealed class SecureChannel
    {
        public const int TagLength = 16;

        readonly byte[] _sendEnc, _sendMac, _recvEnc, _recvMac;
        readonly object _sendLock = new object();
        ulong _sendSeq, _recvSeq;

        public SecureChannel(byte[] sendEnc, byte[] sendMac, byte[] recvEnc, byte[] recvMac)
        {
            _sendEnc = sendEnc; _sendMac = sendMac; _recvEnc = recvEnc; _recvMac = recvMac;
        }

        public byte[] Seal(byte[] plaintext)
        {
            lock (_sendLock)
            {
                var record = new byte[plaintext.Length + TagLength];
                Buffer.BlockCopy(plaintext, 0, record, 0, plaintext.Length);
                ChaCha20.Xor(_sendEnc, Nonce(_sendSeq), 0, record, 0, plaintext.Length);
                var tag = Tag(_sendMac, _sendSeq, record, plaintext.Length);
                Buffer.BlockCopy(tag, 0, record, plaintext.Length, TagLength);
                _sendSeq++;
                return record;
            }
        }

        /// <summary>Returns the plaintext, or null if the record is not authentic. Single reader only.</summary>
        public byte[] Open(byte[] record)
        {
            if (record == null || record.Length <= TagLength) return null;
            int n = record.Length - TagLength;
            var tag = Tag(_recvMac, _recvSeq, record, n);
            if (!Handshake.FixedTimeEquals(tag, 0, record, n, TagLength)) return null;

            var plaintext = new byte[n];
            Buffer.BlockCopy(record, 0, plaintext, 0, n);
            ChaCha20.Xor(_recvEnc, Nonce(_recvSeq), 0, plaintext, 0, n);
            _recvSeq++;
            return plaintext;
        }

        static byte[] Nonce(ulong seq)
        {
            var n = new byte[12];
            for (int i = 0; i < 8; i++) n[4 + i] = (byte)(seq >> (8 * i));
            return n;
        }

        static byte[] Tag(byte[] key, ulong seq, byte[] data, int length)
        {
            var s = new byte[8];
            for (int i = 0; i < 8; i++) s[i] = (byte)(seq >> (8 * i));
            using (var h = new HMACSHA256(key))
            {
                h.TransformBlock(s, 0, 8, null, 0);
                h.TransformFinalBlock(data, 0, length);
                return h.Hash;
            }
        }
    }
}
