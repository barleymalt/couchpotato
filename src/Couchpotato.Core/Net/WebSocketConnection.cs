using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Couchpotato.Net
{
    public sealed class ProtocolViolationException : Exception
    {
        public ProtocolViolationException(string message) : base(message) { }
    }

    /// <summary>
    /// Server side of RFC 6455, limited to what the TV app needs: small, unfragmented binary
    /// messages. Anything else (text frames, fragmentation, oversized or unmasked frames) closes
    /// the connection instead of being half-supported.
    /// </summary>
    public sealed class WebSocketConnection : IDisposable
    {
        public const int MaxPayload = 4096;
        const int MaxHeaderBytes = 8192;
        const string AcceptGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

        readonly Stream _stream;
        readonly object _writeLock = new object();

        public string Origin { get; private set; }

        WebSocketConnection(Stream stream) { _stream = stream; }

        public static WebSocketConnection Accept(Stream stream)
        {
            var lines = ReadHttpHeader(stream).Split(new[] { "\r\n" }, StringSplitOptions.None);
            var request = lines[0].Split(' ');
            if (request.Length != 3 || request[0] != "GET" || !request[2].StartsWith("HTTP/1.1", StringComparison.Ordinal))
                throw new ProtocolViolationException("Not a WebSocket upgrade request");

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 1; i < lines.Length; i++)
            {
                int colon = lines[i].IndexOf(':');
                if (colon <= 0) continue;
                var name = lines[i].Substring(0, colon).Trim();
                var value = lines[i].Substring(colon + 1).Trim();
                headers[name] = headers.TryGetValue(name, out var prev) ? prev + ", " + value : value;
            }

            if (!Contains(headers, "Upgrade", "websocket") || !Contains(headers, "Connection", "upgrade") ||
                !headers.TryGetValue("Sec-WebSocket-Version", out var version) || version != "13" ||
                !headers.TryGetValue("Sec-WebSocket-Key", out var key) || !IsValidKey(key))
                throw new ProtocolViolationException("Invalid WebSocket handshake");

            string accept;
            using (var sha1 = SHA1.Create())
                accept = Convert.ToBase64String(sha1.ComputeHash(Encoding.ASCII.GetBytes(key + AcceptGuid)));

            var response = Encoding.ASCII.GetBytes(
                "HTTP/1.1 101 Switching Protocols\r\n" +
                "Upgrade: websocket\r\n" +
                "Connection: Upgrade\r\n" +
                "Sec-WebSocket-Accept: " + accept + "\r\n\r\n");
            stream.Write(response, 0, response.Length);
            stream.Flush();

            headers.TryGetValue("Origin", out var origin);
            return new WebSocketConnection(stream) { Origin = origin };
        }

        /// <summary>Returns the next binary message, or null when the peer closed the connection.</summary>
        public byte[] Read()
        {
            while (true)
            {
                var h = ReadExact(2);
                bool fin = (h[0] & 0x80) != 0;
                int rsv = h[0] & 0x70;
                int opcode = h[0] & 0x0F;
                bool masked = (h[1] & 0x80) != 0;
                long length = h[1] & 0x7F;

                if (!fin || rsv != 0) throw new ProtocolViolationException("Fragmented or extended frames are not supported");
                if (!masked) throw new ProtocolViolationException("Client frames must be masked");
                if (length == 126) { var b = ReadExact(2); length = b[0] << 8 | b[1]; }
                else if (length == 127)
                {
                    var b = ReadExact(8);
                    length = 0;
                    for (int i = 0; i < 8; i++) length = length << 8 | b[i];
                }
                if (length < 0 || length > MaxPayload) throw new ProtocolViolationException("Frame too large");
                if (opcode >= 0x8 && length > 125) throw new ProtocolViolationException("Control frame too large");

                var mask = ReadExact(4);
                var payload = ReadExact((int)length);
                for (int i = 0; i < payload.Length; i++) payload[i] ^= mask[i & 3];

                switch (opcode)
                {
                    case 0x2:
                        return payload;
                    case 0x8:
                        TryWriteFrame(0x8, payload.Length >= 2 ? new[] { payload[0], payload[1] } : new byte[0]);
                        return null;
                    case 0x9:
                        WriteFrame(0xA, payload);
                        continue;
                    case 0xA:
                        continue;
                    default:
                        throw new ProtocolViolationException("Unsupported opcode " + opcode);
                }
            }
        }

        public void Write(byte[] payload) => WriteFrame(0x2, payload);

        public void Dispose()
        {
            TryWriteFrame(0x8, new byte[] { 0x03, 0xE8 });
            _stream.Dispose();
        }

        void TryWriteFrame(int opcode, byte[] payload)
        {
            try { WriteFrame(opcode, payload); } catch (IOException) { } catch (ObjectDisposedException) { }
        }

        void WriteFrame(int opcode, byte[] payload)
        {
            int n = payload.Length;
            byte[] header;
            if (n < 126) header = new[] { (byte)(0x80 | opcode), (byte)n };
            else if (n <= 0xFFFF) header = new[] { (byte)(0x80 | opcode), (byte)126, (byte)(n >> 8), (byte)n };
            else throw new ArgumentException("Payload too large");

            lock (_writeLock)
            {
                var frame = new byte[header.Length + n];
                Buffer.BlockCopy(header, 0, frame, 0, header.Length);
                Buffer.BlockCopy(payload, 0, frame, header.Length, n);
                _stream.Write(frame, 0, frame.Length);
                _stream.Flush();
            }
        }

        byte[] ReadExact(int n)
        {
            var b = new byte[n];
            int read = 0;
            while (read < n)
            {
                int r = _stream.Read(b, read, n - read);
                if (r <= 0) throw new EndOfStreamException();
                read += r;
            }
            return b;
        }

        static string ReadHttpHeader(Stream stream)
        {
            var sb = new StringBuilder();
            var one = new byte[1];
            while (sb.Length < MaxHeaderBytes)
            {
                if (stream.Read(one, 0, 1) <= 0) throw new EndOfStreamException();
                sb.Append((char)one[0]);
                int len = sb.Length;
                if (len >= 4 && sb[len - 4] == '\r' && sb[len - 3] == '\n' && sb[len - 2] == '\r' && sb[len - 1] == '\n')
                    return sb.ToString(0, len - 4);
            }
            throw new ProtocolViolationException("HTTP header too large");
        }

        static bool Contains(Dictionary<string, string> headers, string name, string token) =>
            headers.TryGetValue(name, out var v) && v.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;

        static bool IsValidKey(string key)
        {
            try { return Convert.FromBase64String(key).Length == 16; }
            catch (FormatException) { return false; }
        }
    }
}
