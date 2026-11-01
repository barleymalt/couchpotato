using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using MagicRemote.Input;

namespace MagicRemote.Config
{
    [DataContract]
    public sealed class PairedTv
    {
        [DataMember(Name = "id", Order = 1)] public string Id;
        [DataMember(Name = "name", Order = 2)] public string Name;
        /// <summary>Pairing key, protected with <see cref="ConfigStore.Protect"/>.</summary>
        [DataMember(Name = "key", Order = 3)] public string Key;
        [DataMember(Name = "address", Order = 4)] public string Address;
        [DataMember(Name = "pairedAt", Order = 5)] public string PairedAt;

        public override string ToString() => string.IsNullOrEmpty(Address) ? Name : Name + " (" + Address + ")";
    }

    [DataContract]
    public sealed class AgentConfig
    {
        public const int DefaultPort = 41234;

        [DataMember(Name = "port", Order = 1)] public int Port = DefaultPort;
        [DataMember(Name = "pcId", Order = 2)] public string PcId;
        /// <summary>Optional: part of the monitor name or "\\.\DISPLAYn" to use instead of auto-detection.</summary>
        [DataMember(Name = "display", Order = 3, EmitDefaultValue = false)] public string Display;
        /// <summary>Optional remote key overrides, e.g. "green": "alt+tab".</summary>
        [DataMember(Name = "keys", Order = 4, EmitDefaultValue = false)] public Dictionary<string, string> Keys;
        [DataMember(Name = "tvs", Order = 5)] public List<PairedTv> Tvs = new List<PairedTv>();
        /// <summary>Pointer style used while a TV is connected: windows, drop, classic, cartoon, fantasy, scifi, retro.</summary>
        [DataMember(Name = "pointer", Order = 6, EmitDefaultValue = false)] public string Pointer;
        /// <summary>small, medium, large or huge.</summary>
        [DataMember(Name = "pointerSize", Order = 7, EmitDefaultValue = false)] public string PointerSize;
    }

    /// <summary>Loads and saves config.json. All access goes through one lock; the file is replaced atomically.</summary>
    public sealed class ConfigStore
    {
        /// <summary>Hooks for at-rest protection of pairing keys (DPAPI on Windows). Identity by default.</summary>
        public static Func<byte[], byte[]> Protect = b => b;
        public static Func<byte[], byte[]> Unprotect = b => b;

        readonly string _path;
        readonly object _lock = new object();
        AgentConfig _config;
        KeyMap _keyMap;

        public IList<string> KeyMapErrors { get; private set; } = new List<string>();

        public ConfigStore(string path)
        {
            _path = path;
            _config = Load(path);
            bool changed = false;
            if (string.IsNullOrEmpty(_config.PcId)) { _config.PcId = Hex(Protocol.Handshake.RandomBytes(16)); changed = true; }
            if (_config.Port <= 0 || _config.Port > 65535) _config.Port = AgentConfig.DefaultPort;
            if (_config.Tvs == null) _config.Tvs = new List<PairedTv>();
            BuildKeyMap();
            if (changed) Save();
        }

        public string FilePath => _path;
        public int Port { get { lock (_lock) return _config.Port; } }
        public byte[] PcId { get { lock (_lock) return FromHex(_config.PcId); } }
        public string Display { get { lock (_lock) return _config.Display; } }
        public KeyMap KeyMap { get { lock (_lock) return _keyMap; } }
        public string Pointer { get { lock (_lock) return _config.Pointer ?? "drop"; } }
        public string PointerSize { get { lock (_lock) return _config.PointerSize ?? "large"; } }

        public void SetPointer(string style, string size)
        {
            lock (_lock)
            {
                _config.Pointer = style;
                _config.PointerSize = size;
                Save();
            }
        }

        /// <summary>What a remote key (e.g. "green") does now: its override, or the default.</summary>
        public string GetKeyBinding(string tvKey)
        {
            lock (_lock)
            {
                if (_config.Keys != null && _config.Keys.TryGetValue(tvKey, out var text)) return text;
                return KeyMap.Defaults.TryGetValue(tvKey, out var d) ? d : "none";
            }
        }

        /// <summary>Changes one remote key. Throws FormatException for an unknown binding; the default is stored as "no override".</summary>
        public void SetKeyBinding(string tvKey, string binding)
        {
            var text = (binding ?? "").Trim().ToLowerInvariant();
            Binding.Parse(text);
            lock (_lock)
            {
                if (_config.Keys == null) _config.Keys = new Dictionary<string, string>();
                if (KeyMap.Defaults.TryGetValue(tvKey, out var d) ? d == text : text == "none") _config.Keys.Remove(tvKey);
                else _config.Keys[tvKey] = text;
                if (_config.Keys.Count == 0) _config.Keys = null;
                BuildKeyMap();
                Save();
            }
        }

        public void ResetKeyBindings()
        {
            lock (_lock)
            {
                _config.Keys = null;
                BuildKeyMap();
                Save();
            }
        }

        /// <summary>The pairing for a TV at this address, if any, so setting it up again keeps its key.</summary>
        public PairedTv FindTvByAddress(string address, out byte[] key)
        {
            key = null;
            lock (_lock)
            {
                var tv = _config.Tvs.FirstOrDefault(t => t.Address == address);
                if (tv == null) return null;
                key = Unprotect(Convert.FromBase64String(tv.Key));
                return Clone(tv);
            }
        }

        public IList<PairedTv> Tvs
        {
            get { lock (_lock) return _config.Tvs.Select(Clone).ToList(); }
        }

        /// <summary>Returns the TV and its raw key, or null if the id is not paired.</summary>
        public PairedTv FindTv(byte[] tvId, out byte[] key)
        {
            key = null;
            var hex = Hex(tvId);
            lock (_lock)
            {
                var tv = _config.Tvs.FirstOrDefault(t => string.Equals(t.Id, hex, StringComparison.OrdinalIgnoreCase));
                if (tv == null) return null;
                key = Unprotect(Convert.FromBase64String(tv.Key));
                return Clone(tv);
            }
        }

        public void AddTv(byte[] tvId, string name, byte[] key, string address)
        {
            lock (_lock)
            {
                var id = Hex(tvId);
                _config.Tvs.RemoveAll(t => t.Id == id);
                _config.Tvs.Add(new PairedTv
                {
                    Id = id,
                    Name = name,
                    Key = Convert.ToBase64String(Protect(key)),
                    Address = address,
                    PairedAt = DateTime.UtcNow.ToString("o"),
                });
                Save();
            }
        }

        public bool HasTv(string id)
        {
            lock (_lock) return _config.Tvs.Any(t => t.Id == id);
        }

        public bool IsPairedAddress(string address)
        {
            lock (_lock) return _config.Tvs.Any(t => t.Address == address);
        }

        public void RemoveTv(string id)
        {
            lock (_lock)
            {
                _config.Tvs.RemoveAll(t => t.Id == id);
                Save();
            }
        }

        /// <summary>After re-pairing a TV, drops its old pairing (same address, different id).</summary>
        public void RemoveOtherPairings(string keepId, string address)
        {
            lock (_lock)
            {
                if (_config.Tvs.RemoveAll(t => t.Id != keepId && t.Address == address) > 0) Save();
            }
        }

        public void UpdateTvAddress(string id, string address)
        {
            lock (_lock)
            {
                var tv = _config.Tvs.FirstOrDefault(t => t.Id == id);
                if (tv == null || tv.Address == address) return;
                tv.Address = address;
                Save();
            }
        }

        /// <summary>Re-reads the file, e.g. after the user edited key bindings by hand.</summary>
        public void Reload()
        {
            lock (_lock)
            {
                var fresh = Load(_path);
                fresh.PcId = string.IsNullOrEmpty(fresh.PcId) ? _config.PcId : fresh.PcId;
                if (fresh.Tvs == null) fresh.Tvs = new List<PairedTv>();
                _config = fresh;
                BuildKeyMap();
            }
        }

        void BuildKeyMap()
        {
            var errors = new List<string>();
            _keyMap = new KeyMap(_config.Keys, errors);
            KeyMapErrors = errors;
        }

        void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path));
            var tmp = _path + ".tmp";
            using (var fs = File.Create(tmp))
            using (var writer = JsonReaderWriterFactory.CreateJsonWriter(fs, Encoding.UTF8, false, true, "  "))
            {
                Serializer().WriteObject(writer, _config);
            }
            if (File.Exists(_path)) File.Replace(tmp, _path, null);
            else File.Move(tmp, _path);
        }

        static AgentConfig Load(string path)
        {
            if (!File.Exists(path)) return new AgentConfig();
            using (var fs = File.OpenRead(path))
                return (AgentConfig)Serializer().ReadObject(fs) ?? new AgentConfig();
        }

        static DataContractJsonSerializer Serializer() =>
            new DataContractJsonSerializer(typeof(AgentConfig), new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });

        static PairedTv Clone(PairedTv t) => new PairedTv { Id = t.Id, Name = t.Name, Key = t.Key, Address = t.Address, PairedAt = t.PairedAt };

        public static string Hex(byte[] b) => BitConverter.ToString(b).Replace("-", "").ToLowerInvariant();

        public static byte[] FromHex(string s)
        {
            var b = new byte[s.Length / 2];
            for (int i = 0; i < b.Length; i++) b[i] = Convert.ToByte(s.Substring(i * 2, 2), 16);
            return b;
        }
    }
}
