using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using MagicRemote.Config;
using MagicRemote.Crypto;
using MagicRemote.Input;
using MagicRemote.Net;
using MagicRemote.Protocol;
using MagicRemote.WebOs;

namespace MagicRemote.Tests;

static class Program
{
    static readonly string RepoRoot = FindRepoRoot();

    static int Main()
    {
        // MR_TEST=<substring> runs matching tests only; MR_VERBOSE=1 shows the agent log;
        // MR_IPK_OUT=<file> writes the TV app package, e.g. to install it with another tool.
        Log.Sink = Environment.GetEnvironmentVariable("MR_VERBOSE") == "1" ? Console.WriteLine : _ => { };
        var ipkOut = Environment.GetEnvironmentVariable("MR_IPK_OUT");
        if (!string.IsNullOrEmpty(ipkOut))
        {
            File.WriteAllBytes(ipkOut, IpkBuilder.Build());
            Console.WriteLine("Wrote " + ipkOut);
            return 0;
        }
        if (Environment.GetEnvironmentVariable("MR_SERVE") == "1")
        {
            // Manual debugging: serve a paired test TV until stdin closes.
            using var env = new ServerEnv();
            Console.WriteLine($"{env.Port} {Convert.ToHexString(env.TvId).ToLowerInvariant()} {Convert.ToBase64String(env.Key)}");
            Console.In.ReadToEnd();
            Console.WriteLine(string.Join("|", env.Sink.Events));
            return 0;
        }
        var filter = Environment.GetEnvironmentVariable("MR_TEST") ?? "";
        var tests = typeof(Program).GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Where(m => m.Name.StartsWith("Test", StringComparison.Ordinal) && m.Name.Contains(filter)).ToList();
        int failed = 0;
        foreach (var t in tests)
        {
            try
            {
                var r = t.Invoke(null, null);
                if (r is Task task) task.GetAwaiter().GetResult();
                Console.WriteLine("PASS " + t.Name);
            }
            catch (Exception e)
            {
                failed++;
                Console.WriteLine("FAIL " + t.Name + ": " + (e.InnerException ?? e).Message);
            }
        }
        Console.WriteLine(failed == 0 ? $"All {tests.Count} tests passed." : $"{failed} of {tests.Count} tests failed.");
        return failed == 0 ? 0 : 1;
    }

    // ---------- crypto ----------

    static void TestChaCha20MatchesRfc8439()
    {
        var key = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
        var nonce = Hex("000000000000004a00000000");
        var data = Encoding.ASCII.GetBytes("Ladies and Gentlemen of the class of '99: If I could offer you only one tip for the future, sunscreen would be it.");
        ChaCha20.Xor(key, nonce, 1, data, 0, data.Length);
        Equal("6e2e359a2568f98041ba0728dd0d6981e97e7aec1d4360c20a27afccfd9fae0bf91b65c5524733ab8f593dabcd62b3571639d624e65152ab8f530c359f0861d807ca0dbf500d6a6156a38e088a22b65e52bc514d16ccf806818ce91ab77937365af90bbf74a35be6b40b8eedf2785e42874d",
              Convert.ToHexString(data).ToLowerInvariant());
    }

    static void TestHandshakeAndChannelRoundTrip()
    {
        var key = Handshake.RandomBytes(32);
        var (tv, pc) = Pair(key);
        var sealedMsg = tv.Seal(new byte[] { 1, 2, 3 });
        Equal("010203", Convert.ToHexString(pc.Open(sealedMsg)));
        Equal("09", Convert.ToHexString(tv.Open(pc.Seal(new byte[] { 9 }))));
    }

    static void TestChannelRejectsTamperingAndReplay()
    {
        var (tv, pc) = Pair(Handshake.RandomBytes(32));
        var first = tv.Seal(new byte[] { 1 });
        var tampered = (byte[])first.Clone();
        tampered[0] ^= 1;
        True(pc.Open(tampered) == null, "tampered record accepted");
        True(pc.Open(first) != null, "valid record rejected");
        True(pc.Open(first) == null, "replayed record accepted");
    }

    static void TestWrongKeyFailsBothWays()
    {
        var tvId = Handshake.RandomBytes(16);
        var tvNonce = Handshake.RandomBytes(16);
        var pcNonce = Handshake.RandomBytes(16);
        var good = Handshake.RandomBytes(32);
        var bad = Handshake.RandomBytes(32);
        var challenge = Handshake.BuildChallenge(good, tvId, tvNonce, pcNonce);
        True(!Handshake.TryParseChallenge(bad, tvId, tvNonce, challenge, out _), "TV trusted a PC with another key");
        True(!Handshake.VerifyResponse(good, tvId, tvNonce, pcNonce, Handshake.BuildResponse(bad, tvId, tvNonce, pcNonce)), "PC trusted a TV with another key");
        // The PC's proof must not be usable as the TV's proof.
        True(!Handshake.VerifyResponse(good, tvId, tvNonce, pcNonce, challenge.Skip(19).ToArray()), "reflected proof accepted");
    }

    // ---------- input ----------

    static void TestKeyMapDefaultsAndOverrides()
    {
        var errors = new List<string>();
        var map = new KeyMap(new Dictionary<string, string> { ["green"] = "alt+tab", ["red"] = "none", ["461"] = "browser-back", ["blue"] = "nonsense+key" }, errors);
        Equal("18,9", string.Join(",", map.Get(404).Keys));        // green → Alt+Tab
        True(map.Get(403) == null, "red should be unbound");
        Equal("166", string.Join(",", map.Get(461).Keys));          // back → browser back
        Equal(BindingKind.TvAction.ToString(), map.Get(406).Kind.ToString()); // bad override keeps the default
        Equal(1, errors.Count);
        Equal(BindingKind.Mouse.ToString(), map.Get(405).Kind.ToString());
    }

    static void TestInputSessionReleasesHeldInputOnDisconnect()
    {
        var sink = new RecordingSink();
        var sent = new List<byte[]>();
        var s = new InputSession(sink, () => new KeyMap(null), sent.Add);
        s.Handle(new byte[] { 4, 37, 0, 1 });        // left arrow down
        s.Handle(new byte[] { 2, 0, 1 });            // left button down
        s.Handle(new byte[] { 4, 37, 0, 1 });        // auto-repeat
        s.ReleaseAll();
        Equal("key 25 down|button Left down|key 25 down|button Left up|key 25 up", string.Join("|", sink.Events));
    }

    static void TestInputSessionMessages()
    {
        var sink = new RecordingSink();
        var sent = new List<byte[]>();
        var s = new InputSession(sink, () => new KeyMap(null), sent.Add);
        s.Handle(new byte[] { 3, 40, 0 });                                      // wheel +40 (down) → -120
        s.Handle(new byte[] { 5 }.Concat(Encoding.UTF8.GetBytes("a\u0007b")).ToArray()); // control chars stripped
        s.Handle(new byte[] { 5, 0xC3 });                                       // invalid UTF-8 ignored
        s.Handle(new byte[] { 4, 150, 1, 1 });                                  // blue down → ask TV for keyboard
        s.Handle(new byte[] { 4, 150, 1, 1 });                                  // held: no second toggle
        s.Handle(new byte[] { 7 });                                             // ping
        s.Handle(new byte[] { 6, 9 });                                          // unknown power action ignored
        Equal("wheel -120|text ab", string.Join("|", sink.Events));
        Equal("8101|82", string.Join("|", sent.Select(Convert.ToHexString)));
    }

    static void TestBlockedInputIsReportedToTv()
    {
        var sink = new RecordingSink { Accept = false, BlockedReason = "Blocked in Steam" };
        var sent = new List<byte[]>();
        var s = new InputSession(sink, () => new KeyMap(null), sent.Add);
        s.Handle(new byte[] { 1, 0, 0, 0, 0 });
        s.Handle(new byte[] { 1, 0, 0, 0, 0 });                  // only one notice per 10 s
        Equal(1, sent.Count);
        Equal("8301", Convert.ToHexString(sent[0], 0, 2));
        Equal("Blocked in Steam", Encoding.UTF8.GetString(sent[0], 2, sent[0].Length - 2));
    }

    // ---------- config ----------

    static void TestConfigRoundTripAndProtection()
    {
        var dir = Directory.CreateTempSubdirectory("mr-test-");
        try
        {
            ConfigStore.Protect = b => b.Select(x => (byte)(x ^ 0x5A)).ToArray();
            ConfigStore.Unprotect = ConfigStore.Protect;
            var path = Path.Combine(dir.FullName, "config.json");
            var store = new ConfigStore(path);
            var tvId = Handshake.RandomBytes(16);
            var key = Handshake.RandomBytes(32);
            store.AddTv(tvId, "Living room", key, "192.168.1.50");

            var reloaded = new ConfigStore(path);
            Equal(Convert.ToHexString(store.PcId), Convert.ToHexString(reloaded.PcId));
            var tv = reloaded.FindTv(tvId, out var readKey);
            Equal("Living room", tv.Name);
            Equal(Convert.ToBase64String(key), Convert.ToBase64String(readKey));
            True(!File.ReadAllText(path).Contains(Convert.ToBase64String(key)), "key stored unprotected");
        }
        finally
        {
            ConfigStore.Protect = b => b;
            ConfigStore.Unprotect = b => b;
            dir.Delete(true);
        }
    }

    static void TestButtonSettingsSaveOnlyChanges()
    {
        var dir = Directory.CreateTempSubdirectory("mr-test-");
        try
        {
            var path = Path.Combine(dir.FullName, "config.json");
            var store = new ConfigStore(path);
            Equal("win", store.GetKeyBinding("green"));
            store.SetKeyBinding("green", "Alt+Tab");
            Equal("18,9", string.Join(",", store.KeyMap.Get(404).Keys));
            Equal("alt+tab", new ConfigStore(path).GetKeyBinding("green"));

            bool rejected = false;
            try { store.SetKeyBinding("red", "ctrl+nonsense"); } catch (FormatException) { rejected = true; }
            True(rejected, "invalid binding accepted");
            Equal("power-menu", store.GetKeyBinding("red"));

            store.SetKeyBinding("green", "win");                      // back to the default: no override kept
            True(!File.ReadAllText(path).Contains("\"keys\""), "default stored as an override");
            store.SetKeyBinding("info", "win+r");
            store.ResetKeyBindings();
            Equal("none", store.GetKeyBinding("info"));

            store.SetPointer("fantasy", "huge");
            var reloaded = new ConfigStore(path);
            Equal("fantasy huge", reloaded.Pointer + " " + reloaded.PointerSize);
        }
        finally { dir.Delete(true); }
    }

    static void TestButtonCatalogIsConsistent()
    {
        foreach (var b in ButtonCatalog.RemoteButtons) True(KeyMap.TvKeys.ContainsKey(b.Key), "unknown remote key " + b.Key);
        foreach (var a in ButtonCatalog.Actions)
        {
            Binding.Parse(a.Value);
            Equal(a.Value, ButtonCatalog.ToBinding(ButtonCatalog.Describe(a.Value)));
        }
        Equal("ctrl+shift+esc", ButtonCatalog.ToBinding(" Ctrl+Shift+Esc "));
        Equal("green", ButtonCatalog.NameOf(404));
        True(ButtonCatalog.NameOf(8) == null, "the TV keyboard's backspace is not a remote button");

        var pressed = new List<ushort>();
        var s = new InputSession(new RecordingSink(), () => new KeyMap(null), _ => { }, pressed.Add);
        s.Handle(new byte[] { 4, 148, 1, 1 });
        s.Handle(new byte[] { 4, 148, 1, 0 });
        Equal("404", string.Join(",", pressed));
    }

    static void TestSettingUpTheSameTvAgainKeepsItsPairing()
    {
        var dir = Directory.CreateTempSubdirectory("mr-test-");
        try
        {
            var store = new ConfigStore(Path.Combine(dir.FullName, "config.json"));
            var tv = IPAddress.Parse("192.168.1.50");
            var first = Pairing.Create(store, "PC", tv, 41234);
            True(!first.Reused, "fresh pairing marked as reused");
            store.AddTv(first.TvId, "TV", first.Key, tv.ToString());

            var again = Pairing.Create(store, "PC", tv, 41234);
            True(again.Reused, "same TV got a new pairing");
            Equal(Convert.ToHexString(first.Key), Convert.ToHexString(again.Key));
            True(!Pairing.Create(store, "PC", IPAddress.Parse("192.168.1.51"), 41234).Reused, "other TV reused a pairing");
        }
        finally { dir.Delete(true); }
    }

    static void TestPairingParamsAreShellSafe()
    {
        var dir = Directory.CreateTempSubdirectory("mr-test-");
        try
        {
            var store = new ConfigStore(Path.Combine(dir.FullName, "config.json"));
            var p = Pairing.Create(store, "Bob's PC'; rm -rf /", IPAddress.Loopback, 41234);
            True(!p.LaunchParamsJson.Contains('\''), "single quote in launch params");
            True(p.LaunchParamsJson.Contains("\"pcName\":\"Bobs PC rm -rf\","), p.LaunchParamsJson);
        }
        finally { dir.Delete(true); }
    }

    // ---------- packaging ----------

    static void TestIpkLayoutMatchesAresPackage()
    {
        var ipk = IpkBuilder.Build();
        var members = ReadAr(ipk);
        Equal("debian-binary,control.tar.gz,data.tar.gz", string.Join(",", members.Select(m => m.Name)));
        Equal("2.0\n", Encoding.ASCII.GetString(members[0].Data));

        var control = ReadTarGz(members[1].Data);
        Equal("control", string.Join(",", control.Keys));
        True(Encoding.UTF8.GetString(control["control"].Data).Contains("Package: com.magicremote.tv\n"), "control Package line");

        var data = ReadTarGz(members[2].Data);
        foreach (var path in new[]
        {
            "usr/palm/applications/com.magicremote.tv/appinfo.json",
            "usr/palm/applications/com.magicremote.tv/app.js",
            "usr/palm/services/com.magicremote.tv.service/service.js",
            "usr/palm/services/com.magicremote.tv.service/services.json",
            "usr/palm/packages/com.magicremote.tv/packageinfo.json",
        })
            True(data.ContainsKey(path), "missing " + path);

        var source = File.ReadAllBytes(Path.Combine(RepoRoot, "tvapp/app/app.js"));
        True(source.SequenceEqual(data["usr/palm/applications/com.magicremote.tv/app.js"].Data), "app.js content differs");
        Equal(Convert.ToInt32("777", 8), (int)data["usr/palm/services/com.magicremote.tv.service/"].Mode);
        Equal(Convert.ToInt32("755", 8), (int)data["usr/palm/applications/com.magicremote.tv/"].Mode);
        True(Encoding.UTF8.GetString(data["usr/palm/packages/com.magicremote.tv/packageinfo.json"].Data).Contains("\"com.magicremote.tv.service\""), "packageinfo services");
    }

    /// <summary>Versions are consistent, and the changelog says which TV app goes with the PC app.</summary>
    static void TestVersionsMatch()
    {
        var pc = typeof(IpkBuilder).Assembly.GetName().Version.ToString(3);
        var tv = IpkBuilder.Version;
        var service = File.ReadAllText(Path.Combine(RepoRoot, "tvapp/service/package.json"));
        True(service.Contains("\"version\": \"" + tv + "\""), "tvapp/service/package.json version differs from appinfo.json " + tv);
        var changelog = File.ReadAllText(Path.Combine(RepoRoot, "CHANGELOG.md"));
        True(changelog.Contains("## " + pc), "CHANGELOG.md has no entry for " + pc);
        True(changelog.Contains("TV app " + tv), "CHANGELOG.md doesn't say which TV app version goes with " + pc);
    }

    static void TestDevModeKeyFormatIsReadable()
    {
        var pem = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures/webos_rsa_test.pem"));
        _ = new Renci.SshNet.PrivateKeyFile(new MemoryStream(pem), "ABC123");
        bool rejected = false;
        try { _ = new Renci.SshNet.PrivateKeyFile(new MemoryStream(pem), "WRONG1"); }
        catch (Exception) { rejected = true; }
        True(rejected, "wrong passphrase accepted");
    }

    // ---------- network ----------

    static async Task TestServerWithDotNetWebSocketClient()
    {
        using var env = new ServerEnv();
        var (ws, channel) = await ConnectTvAsync(env);
        using var _ = ws;
        await SendAsync(ws, channel.Seal(new byte[] { 7 }));
        Equal("82", Convert.ToHexString(channel.Open(await ReceiveAsync(ws))));
        await SendAsync(ws, channel.Seal(new byte[] { 1, 0, 0x80, 0, 0x40 }));
        await SendAsync(ws, channel.Seal(new byte[] { 7 }));
        await ReceiveAsync(ws);
        Equal("move 0.500 0.250", string.Join("|", env.Sink.Events));
    }

    static async Task TestServerRejectsUnknownTvAndBrowsers()
    {
        using var env = new ServerEnv();

        using (var ws = new ClientWebSocket())
        {
            await ws.ConnectAsync(new Uri($"ws://127.0.0.1:{env.Port}/"), CancellationToken.None);
            await SendAsync(ws, Handshake.BuildHello(Handshake.RandomBytes(16), Handshake.RandomBytes(16)));
            var r = await ws.ReceiveAsync(new byte[64], CancellationToken.None);
            Equal(WebSocketMessageType.Close.ToString(), r.MessageType.ToString());
        }

        using (var tcp = new TcpClient())
        {
            await tcp.ConnectAsync(IPAddress.Loopback, env.Port);
            var s = tcp.GetStream();
            var req = "GET / HTTP/1.1\r\nHost: x\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Version: 13\r\n" +
                      "Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\nOrigin: https://evil.example\r\n\r\n";
            await s.WriteAsync(Encoding.ASCII.GetBytes(req));
            var buf = new byte[512];
            int total = 0, n;
            while ((n = await s.ReadAsync(buf.AsMemory(total))) > 0) total += n;
            var text = Encoding.ASCII.GetString(buf, 0, total);
            True(text.StartsWith("HTTP/1.1 101"), "expected upgrade then close");
            True(total < 140, "server talked to a web page: " + total + " bytes");
        }
        True(env.Sink.Events.Count == 0, "input from an unauthenticated peer");
    }

    static async Task TestIdlePeersAreCutOffAndCantBlockTheTv()
    {
        using var env = new ServerEnv();
        var idle = new List<TcpClient>();
        for (int i = 0; i < 2; i++)
        {
            var tcp = new TcpClient();
            await tcp.ConnectAsync(IPAddress.Loopback, env.Port);
            var req = "GET / HTTP/1.1\r\nHost: x\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Version: 13\r\n" +
                      "Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\n\r\n";
            await tcp.GetStream().WriteAsync(Encoding.ASCII.GetBytes(req));
            idle.Add(tcp);
        }
        // Keep pinging (which the server answers) so only an absolute deadline can end these.
        var started = DateTime.UtcNow;
        var ping = new byte[] { 0x89, 0x80, 1, 2, 3, 4 };
        var closed = new bool[idle.Count];
        while (DateTime.UtcNow - started < TimeSpan.FromSeconds(9) && closed.Contains(false))
        {
            for (int i = 0; i < idle.Count; i++)
            {
                try { await idle[i].GetStream().WriteAsync(ping); }
                catch (IOException) { closed[i] = true; }
            }
            await Task.Delay(500);
        }
        True(!closed.Contains(false), "idle peers kept their slots for 9 s");
        True(DateTime.UtcNow - started > TimeSpan.FromSeconds(4), "closed too early");
        foreach (var c in idle) c.Dispose();

        // Repeated failures from the TV's own address must not lock the TV out.
        for (int i = 0; i < 6; i++)
        {
            using var ws = new ClientWebSocket();
            await ws.ConnectAsync(new Uri($"ws://127.0.0.1:{env.Port}/"), CancellationToken.None);
            await SendAsync(ws, Handshake.BuildHello(Handshake.RandomBytes(16), Handshake.RandomBytes(16)));
            await ws.ReceiveAsync(new byte[64], CancellationToken.None);
        }
        var (tv, _) = await ConnectTvAsync(env);
        tv.Dispose();
    }

    /// <summary>Administrator mode: nothing on the PC itself may pose as the TV, even with a valid key.</summary>
    static async Task TestAdminModeRefusesPeersOnThisPc()
    {
        using var env = new ServerEnv(rejectLocalPeers: true);
        bool refused = false;
        try { await ConnectTvAsync(env); }
        catch (Exception e) when (e is WebSocketException || e is IOException) { refused = true; }
        True(refused, "a local peer was accepted");
        True(NetUtil.IsOwnAddress(IPAddress.Loopback) && !NetUtil.IsOwnAddress(IPAddress.Parse("192.0.2.1")), "own address check");
    }

    static async Task TestForgettingATvEndsItsLiveSession()
    {
        using var env = new ServerEnv();
        var (ws, channel) = await ConnectTvAsync(env);
        using var _ = ws;
        await SendAsync(ws, channel.Seal(new byte[] { 7 }));
        await ReceiveAsync(ws);

        env.Store.RemoveTv(Convert.ToHexString(env.TvId).ToLowerInvariant());
        await SendAsync(ws, channel.Seal(new byte[] { 6, 1 }));   // shutdown request
        var r = await ws.ReceiveAsync(new byte[64], CancellationToken.None);
        Equal(WebSocketMessageType.Close.ToString(), r.MessageType.ToString());
        True(env.Sink.Events.Count == 0, "a forgotten TV still controlled the PC: " + string.Join("|", env.Sink.Events));
    }

    static void TestTvJavaScriptAgainstServer()
    {
        using var env = new ServerEnv();
        var output = RunNode("tests/js/interop.js", env.Port.ToString(), Convert.ToHexString(env.TvId).ToLowerInvariant(), Convert.ToBase64String(env.Key));
        Equal("ok", output.Trim());
        Thread.Sleep(300); // let the server notice the close and release held keys
        Equal("move 0.500 0.250|button Left down|button Left up|wheel -120|key D down|key D up|text héllo 👋|key 25 down|key 25 up",
              string.Join("|", env.Sink.Events));
        lock (env.TvAppVersions) Equal("Test TV 9.8.7", string.Join("|", env.TvAppVersions));

        var wrong = RunNode("tests/js/interop.js", env.Port.ToString(), Convert.ToHexString(env.TvId).ToLowerInvariant(), Convert.ToBase64String(Handshake.RandomBytes(32)));
        Equal("refused", wrong.Trim());
    }

    /// <summary>The TV background service, with fake webOS APIs, finding this PC agent by broadcast. Needs port 3000 free.</summary>
    static void TestTvServiceFlowAndDiscovery()
    {
        using var env = new ServerEnv();
        Equal("ok", RunNode("tests/js/service.test.js", env.Port.ToString(), Convert.ToHexString(env.Store.PcId).ToLowerInvariant()).Trim());
    }

    static void TestTvJavaScriptCrypto()
    {
        Equal("ok", RunNode("tests/js/crypto.test.js").Trim().Split('\n').Last());
    }

    // ---------- helpers ----------

    static (SecureChannel Tv, SecureChannel Pc) Pair(byte[] key)
    {
        var tvId = Handshake.RandomBytes(16);
        var tvNonce = Handshake.RandomBytes(16);
        var pcNonce = Handshake.RandomBytes(16);
        True(Handshake.TryParseHello(Handshake.BuildHello(tvId, tvNonce), out _, out _), "hello");
        True(Handshake.TryParseChallenge(key, tvId, tvNonce, Handshake.BuildChallenge(key, tvId, tvNonce, pcNonce), out var n), "challenge");
        True(Handshake.VerifyResponse(key, tvId, tvNonce, n, Handshake.BuildResponse(key, tvId, tvNonce, n)), "response");
        return (Handshake.CreateChannel(key, tvId, tvNonce, n, false), Handshake.CreateChannel(key, tvId, tvNonce, n, true));
    }

    static async Task<(ClientWebSocket, SecureChannel)> ConnectTvAsync(ServerEnv env)
    {
        var ws = new ClientWebSocket();
        await ws.ConnectAsync(new Uri($"ws://127.0.0.1:{env.Port}/"), CancellationToken.None);
        var tvNonce = Handshake.RandomBytes(16);
        await SendAsync(ws, Handshake.BuildHello(env.TvId, tvNonce));
        True(Handshake.TryParseChallenge(env.Key, env.TvId, tvNonce, await ReceiveAsync(ws), out var pcNonce), "PC proof");
        await SendAsync(ws, Handshake.BuildResponse(env.Key, env.TvId, tvNonce, pcNonce));
        return (ws, Handshake.CreateChannel(env.Key, env.TvId, tvNonce, pcNonce, forPc: false));
    }

    sealed class ServerEnv : IDisposable
    {
        readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("mr-test-");
        readonly AgentServer _server;
        public readonly RecordingSink Sink = new();
        public readonly byte[] TvId = Handshake.RandomBytes(16);
        public readonly byte[] Key = Handshake.RandomBytes(32);
        public readonly ConfigStore Store;
        public readonly List<string> TvAppVersions = new();
        public int Port => _server.Port;

        public ServerEnv(bool rejectLocalPeers = false)
        {
            var store = Store = new ConfigStore(Path.Combine(_dir.FullName, "config.json"));
            store.AddTv(TvId, "Test TV", Key, "127.0.0.1");
            _server = new AgentServer(store, Sink) { RejectLocalPeers = rejectLocalPeers };
            _server.TvAppVersion += (name, version) => { lock (TvAppVersions) TvAppVersions.Add(name + " " + version); };
            _server.Start(IPAddress.Loopback, 0);
        }

        public void Dispose()
        {
            _server.Dispose();
            _dir.Delete(true);
        }
    }

    sealed class RecordingSink : IInputSink
    {
        readonly List<string> _events = new();
        public bool Accept = true;
        public List<string> Events { get { lock (_events) return _events.ToList(); } }
        bool Add(string e) { lock (_events) _events.Add(e); return Accept; }
        public bool MoveTo(double x, double y) => Add(FormattableString.Invariant($"move {x:F3} {y:F3}"));
        public bool Button(MouseButton button, bool down) => Add($"button {button} {(down ? "down" : "up")}");
        public bool Wheel(int delta) => Add($"wheel {delta}");
        public bool Key(ushort vk, bool down) => Add($"key {vk:X} {(down ? "down" : "up")}");
        public bool Text(string text) => Add($"text {text}");
        public void Power(PowerAction action) => Add($"power {action}");
        public string BlockedReason { get; set; }
    }

    static async Task SendAsync(ClientWebSocket ws, byte[] data) =>
        await ws.SendAsync(data, WebSocketMessageType.Binary, true, CancellationToken.None);

    static async Task<byte[]> ReceiveAsync(ClientWebSocket ws)
    {
        var buf = new byte[4096];
        var r = await ws.ReceiveAsync(buf, CancellationToken.None);
        return buf.Take(r.Count).ToArray();
    }

    static string RunNode(string script, params string[] args)
    {
        var psi = new ProcessStartInfo("node") { WorkingDirectory = RepoRoot, RedirectStandardOutput = true, RedirectStandardError = true };
        psi.ArgumentList.Add(script);
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi);
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(20000)) { p.Kill(); throw new Exception("node timed out"); }
        if (p.ExitCode != 0) throw new Exception("node failed: " + stdout.Result + stderr.Result);
        return stdout.Result;
    }

    static List<(string Name, byte[] Data)> ReadAr(byte[] ar)
    {
        Equal("!<arch>\n", Encoding.ASCII.GetString(ar, 0, 8));
        var list = new List<(string, byte[])>();
        int o = 8;
        while (o < ar.Length)
        {
            var header = Encoding.ASCII.GetString(ar, o, 60);
            Equal("`\n", header.Substring(58, 2));
            int size = int.Parse(header.Substring(48, 10).Trim());
            list.Add((header.Substring(0, 16).Trim(), ar.Skip(o + 60).Take(size).ToArray()));
            o += 60 + size + (size % 2);
        }
        return list;
    }

    static Dictionary<string, (byte[] Data, UnixFileMode Mode)> ReadTarGz(byte[] tgz)
    {
        var result = new Dictionary<string, (byte[], UnixFileMode)>();
        using var reader = new TarReader(new GZipStream(new MemoryStream(tgz), CompressionMode.Decompress));
        TarEntry e;
        while ((e = reader.GetNextEntry()) != null)
        {
            var ms = new MemoryStream();
            e.DataStream?.CopyTo(ms);
            result[e.Name] = (ms.ToArray(), e.Mode);
        }
        return result;
    }

    static byte[] Hex(string s) => Convert.FromHexString(s);

    static void True(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"expected <{expected}> got <{actual}>");
    }

    static string FindRepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "tvapp"))) d = d.Parent;
        return d?.FullName ?? throw new Exception("repo root not found");
    }
}
