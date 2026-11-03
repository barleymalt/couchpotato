using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Couchpotato.Config;
using Couchpotato.Input;
using Couchpotato.Protocol;

namespace Couchpotato.Net
{
    /// <summary>
    /// Accepts TV connections. A TV gets to move the mouse only after proving it holds the key
    /// created when it was paired; every later message must be authentic and in sequence.
    /// </summary>
    public sealed class AgentServer : IDisposable
    {
        // Unauthenticated connections have their own small budget, so idle or slow peers can't
        // crowd out the TV. Each gets HandshakeTimeoutMs in total, not per read.
        const int MaxPending = 8;
        const int MaxPendingPerAddress = 2;
        const int MaxSessions = 8;
        const int HandshakeTimeoutMs = 5000;
        const int IdleTimeoutMs = 35000; // the TV pings every 10 s

        readonly ConfigStore _store;
        readonly IInputSink _sink;
        readonly FailureLimiter _limiter = new FailureLimiter();
        readonly Dictionary<string, DateTime> _failureLogged = new Dictionary<string, DateTime>();
        readonly Dictionary<IPAddress, int> _pending = new Dictionary<IPAddress, int>();
        TcpListener _listener;
        DiscoveryResponder _discovery;
        int _pendingTotal, _sessions;
        volatile bool _stopped;

        /// <summary>Raised with the TV name and its address. Called from network threads.</summary>
        public event Action<string, IPAddress> Connected;
        public event Action<string, IPAddress> Disconnected;
        /// <summary>Raised when a peer fails authentication: an unpaired TV or something else probing the port.</summary>
        public event Action<IPAddress> AuthenticationFailed;
        /// <summary>Raised with the webOS key code of each remote key press from a paired TV.</summary>
        public event Action<ushort> KeyPressed;
        /// <summary>Raised with the TV name and the version its app reports (TV apps from 1.4.0 on).</summary>
        public event Action<string, string> TvAppVersion;

        public AgentServer(ConfigStore store, IInputSink sink)
        {
            _store = store;
            _sink = sink;
        }

        public int Port { get; private set; }

        /// <summary>Refuse peers on this machine (loopback or one of its own addresses). TVs never connect from here.</summary>
        public bool RejectLocalPeers { get; set; }

        /// <param name="port">Overrides the configured port; tests pass 0 for any free port.</param>
        public void Start(IPAddress bindAddress = null, int? port = null)
        {
            _listener = new TcpListener(bindAddress ?? IPAddress.Any, port ?? _store.Port);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _discovery = new DiscoveryResponder(Port, _store.PcId, Port);
            Task.Run(AcceptLoopAsync);
            Log.Info("Listening on port " + Port);
        }

        async Task AcceptLoopAsync()
        {
            while (!_stopped)
            {
                TcpClient client;
                try { client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false); }
                catch (Exception) { if (_stopped) return; await Task.Delay(200).ConfigureAwait(false); continue; }

                var ip = ((IPEndPoint)client.Client.RemoteEndPoint).Address;
                if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
                // A paired TV's address is never blocked: a web page in the TV's browser could otherwise lock the TV out.
                if (!NetUtil.IsLocalNetwork(ip) || (RejectLocalPeers && NetUtil.IsOwnAddress(ip)) ||
                    (_limiter.IsBlocked(ip) && !_store.IsPairedAddress(ip.ToString())) || !TryAddPending(ip))
                {
                    client.Close();
                    continue;
                }
                new Thread(() => Serve(client, ip)) { IsBackground = true, Name = "mr-session" }.Start();
            }
        }

        bool TryAddPending(IPAddress ip)
        {
            lock (_pending)
            {
                _pending.TryGetValue(ip, out var n);
                if (_pendingTotal >= MaxPending || n >= MaxPendingPerAddress) return false;
                _pending[ip] = n + 1;
                _pendingTotal++;
                return true;
            }
        }

        void RemovePending(IPAddress ip)
        {
            lock (_pending)
            {
                if (--_pending[ip] == 0) _pending.Remove(ip);
                _pendingTotal--;
            }
        }

        void Serve(TcpClient client, IPAddress ip)
        {
            InputSession input = null;
            string tvName = null;
            bool pending = true, counted = false;
            var deadline = new Timer(_ => client.Close(), null, HandshakeTimeoutMs, Timeout.Infinite);
            try
            {
                client.NoDelay = true;
                var stream = client.GetStream();
                stream.ReadTimeout = HandshakeTimeoutMs;
                stream.WriteTimeout = HandshakeTimeoutMs;

                using (var ws = WebSocketConnection.Accept(stream))
                {
                    if (IsWebPageOrigin(ws.Origin))
                    {
                        Fail(ip, "a web page (origin " + ws.Origin + "), not the TV app");
                        return;
                    }

                    if (!Handshake.TryParseHello(ws.Read(), out var tvId, out var tvNonce)) { Fail(ip, "its first message isn't the TV app's hello"); return; }
                    var tv = _store.FindTv(tvId, out var key);
                    if (tv == null) { Fail(ip, "it isn't a TV paired with this PC; pair it again (Set up a TV)"); return; }

                    var pcNonce = Handshake.RandomBytes(Handshake.NonceLength);
                    ws.Write(Handshake.BuildChallenge(key, tvId, tvNonce, pcNonce));
                    if (!Handshake.VerifyResponse(key, tvId, tvNonce, pcNonce, ws.Read())) { Fail(ip, "TV '" + tv.Name + "' has a different pairing key; pair it again (Set up a TV) and choose Replace on the TV"); return; }

                    var channel = Handshake.CreateChannel(key, tvId, tvNonce, pcNonce, forPc: true);
                    deadline.Dispose();
                    RemovePending(ip);
                    pending = false;
                    if (Interlocked.Increment(ref _sessions) > MaxSessions) { Interlocked.Decrement(ref _sessions); return; }
                    counted = true;
                    _limiter.Reset(ip);
                    _store.UpdateTvAddress(tv.Id, ip.ToString());
                    stream.ReadTimeout = IdleTimeoutMs;

                    var name = tv.Name;
                    input = new InputSession(_sink, () => _store.KeyMap, m => ws.Write(channel.Seal(m)),
                                             code => KeyPressed?.Invoke(code), version => TvAppVersion?.Invoke(name, version));
                    tvName = name;
                    Log.Info("TV '" + tvName + "' connected from " + ip);
                    Connected?.Invoke(tvName, ip);

                    while (!_stopped)
                    {
                        var record = ws.Read();
                        if (record == null) break;
                        var message = channel.Open(record);
                        if (message == null)
                        {
                            Log.Warn("Dropping connection from " + ip + ": message failed authentication");
                            break;
                        }
                        // Forgetting or re-pairing the TV ends its live session too (within one ping).
                        if (!_store.HasTv(tv.Id))
                        {
                            Log.Info("TV '" + tvName + "' is no longer paired; disconnecting");
                            break;
                        }
                        input.Handle(message);
                    }
                }
            }
            catch (Exception e) when (e is IOException || e is SocketException || e is ProtocolViolationException || e is ObjectDisposedException)
            {
                Log.Info("Connection from " + ip + " ended: " + e.GetType().Name + " " + e.Message);
                if (tvName == null) Fail(ip, "the connection ended before it logged in");
            }
            catch (Exception e)
            {
                Log.Warn("Session error: " + e);
            }
            finally
            {
                deadline.Dispose();
                input?.ReleaseAll();
                client.Close();
                if (pending) RemovePending(ip);
                if (counted) Interlocked.Decrement(ref _sessions);
                if (tvName != null)
                {
                    Log.Info("TV '" + tvName + "' disconnected");
                    Disconnected?.Invoke(tvName, ip);
                }
            }
        }

        /// <summary>Records a refused connection and logs why (at most once a minute per address and reason; never keys).</summary>
        void Fail(IPAddress ip, string reason)
        {
            var key = ip + " " + reason;
            lock (_failureLogged)
            {
                if (!_failureLogged.TryGetValue(key, out var last) || DateTime.UtcNow - last > TimeSpan.FromMinutes(1))
                {
                    _failureLogged[key] = DateTime.UtcNow;
                    Log.Warn("Refused a connection from " + ip + ": " + reason);
                }
            }
            if (_limiter.RecordFailure(ip)) Log.Warn("Too many failed connections from " + ip + "; ignoring it for a while");
            AuthenticationFailed?.Invoke(ip);
        }

        /// <summary>The TV app sends no Origin or a file:// one. Web pages cannot be paired, so refuse them early.</summary>
        static bool IsWebPageOrigin(string origin) =>
            origin != null && (origin.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                               origin.StartsWith("https://", StringComparison.OrdinalIgnoreCase));

        public void Dispose()
        {
            _stopped = true;
            _listener?.Stop();
            _discovery?.Dispose();
        }

        /// <summary>Blocks an address for a while after repeated failures, to stop key guessing and log spam.</summary>
        sealed class FailureLimiter
        {
            const int MaxFailures = 5;
            static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
            static readonly TimeSpan BlockFor = TimeSpan.FromMinutes(5);

            readonly Dictionary<IPAddress, (int Count, DateTime First, DateTime BlockedUntil)> _entries =
                new Dictionary<IPAddress, (int, DateTime, DateTime)>();

            public bool IsBlocked(IPAddress ip)
            {
                lock (_entries) return _entries.TryGetValue(ip, out var e) && e.BlockedUntil > DateTime.UtcNow;
            }

            /// <summary>Returns true when this failure starts a block.</summary>
            public bool RecordFailure(IPAddress ip)
            {
                lock (_entries)
                {
                    var now = DateTime.UtcNow;
                    if (!_entries.TryGetValue(ip, out var e) || now - e.First > Window) e = (0, now, DateTime.MinValue);
                    e.Count++;
                    bool blocks = e.Count == MaxFailures;
                    if (blocks) e.BlockedUntil = now + BlockFor;
                    _entries[ip] = e;
                    return blocks;
                }
            }

            public void Reset(IPAddress ip)
            {
                lock (_entries) _entries.Remove(ip);
            }
        }
    }
}
