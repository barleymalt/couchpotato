using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace Couchpotato.WebOs
{
    /// <summary>
    /// Builds the webOS .ipk from the TV app embedded in this assembly, in the same layout as
    /// LG's ares-package: an ar archive holding debian-binary, control.tar.gz and data.tar.gz.
    /// </summary>
    public static class IpkBuilder
    {
        const string ResourcePrefix = "tvapp/";

        [DataContract]
        sealed class AppInfo
        {
            [DataMember(Name = "id")] public string Id { get; set; }
            [DataMember(Name = "version")] public string Version { get; set; }
        }

        public static string AppId => ReadAppInfo().Id;
        public static string ServiceId => AppId + ".service";
        public static string Version => ReadAppInfo().Version;

        public static byte[] Build() => Build(ReadTvAppFiles(), DateTimeOffset.UtcNow);

        public static byte[] Build(IDictionary<string, byte[]> files, DateTimeOffset time)
        {
            var info = ReadAppInfo(files["app/appinfo.json"]);
            string appId = info.Id, serviceId = appId + ".service";

            var packageInfo = "{\n  \"id\": \"" + appId + "\",\n  \"version\": \"" + info.Version + "\",\n  \"app\": \"" + appId +
                              "\",\n  \"services\": [\n    \"" + serviceId + "\"\n  ]\n}\n";

            var data = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var f in files)
            {
                if (f.Key.StartsWith("app/", StringComparison.Ordinal))
                    data["usr/palm/applications/" + appId + "/" + f.Key.Substring(4)] = f.Value;
                else if (f.Key.StartsWith("service/", StringComparison.Ordinal))
                    data["usr/palm/services/" + serviceId + "/" + f.Key.Substring(8)] = f.Value;
            }
            data["usr/palm/packages/" + appId + "/packageinfo.json"] = Encoding.UTF8.GetBytes(packageInfo);

            // The service directory must be writable: the service keeps its settings file there.
            var writableDirs = new HashSet<string> { "usr/palm/services/" + serviceId + "/" };
            var dataTar = Gzip(Tar(data, writableDirs, time));

            var control = "Package: " + appId + "\nVersion: " + info.Version + "\nSection: misc\nPriority: optional\n" +
                          "Architecture: all\nInstalled-Size: " + data.Values.Sum(v => v.Length) + "\n" +
                          "Maintainer: N/A <nobody@example.com>\nDescription: This is a webOS application.\n" +
                          "webOS-Package-Format-Version: 2\nwebOS-Packager-Version: x.y.x\n";
            var controlTar = Gzip(Tar(new SortedDictionary<string, byte[]> { ["control"] = Encoding.UTF8.GetBytes(control) },
                                      new HashSet<string>(), time));

            return Ar(new[]
            {
                ("debian-binary", Encoding.ASCII.GetBytes("2.0\n")),
                ("control.tar.gz", controlTar),
                ("data.tar.gz", dataTar),
            }, time);
        }

        /// <summary>Files of the TV app keyed by "app/..." or "service/...".</summary>
        public static Dictionary<string, byte[]> ReadTvAppFiles()
        {
            var asm = typeof(IpkBuilder).Assembly;
            var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var name in asm.GetManifestResourceNames())
            {
                var path = name.Replace('\\', '/');
                if (!path.StartsWith(ResourcePrefix, StringComparison.Ordinal)) continue;
                using (var s = asm.GetManifestResourceStream(name))
                using (var ms = new MemoryStream())
                {
                    s.CopyTo(ms);
                    files[path.Substring(ResourcePrefix.Length)] = ms.ToArray();
                }
            }
            return files;
        }

        static AppInfo ReadAppInfo()
        {
            using (var s = typeof(IpkBuilder).Assembly.GetManifestResourceStream(ResourcePrefix + "app/appinfo.json"))
            using (var ms = new MemoryStream())
            {
                s.CopyTo(ms);
                return ReadAppInfo(ms.ToArray());
            }
        }

        static AppInfo ReadAppInfo(byte[] json)
        {
            using (var ms = new MemoryStream(json))
                return (AppInfo)new DataContractJsonSerializer(typeof(AppInfo)).ReadObject(ms);
        }

        static byte[] Tar(SortedDictionary<string, byte[]> files, HashSet<string> writableDirs, DateTimeOffset time)
        {
            long mtime = time.ToUnixTimeSeconds();
            var dirs = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var path in files.Keys)
                for (int i = path.IndexOf('/'); i >= 0; i = path.IndexOf('/', i + 1))
                    dirs.Add(path.Substring(0, i + 1));

            var entries = dirs.Select(d => (Path: d, Data: (byte[])null))
                              .Concat(files.Select(f => (Path: f.Key, Data: f.Value)))
                              .OrderBy(e => e.Path, StringComparer.Ordinal);

            using (var ms = new MemoryStream())
            {
                foreach (var (path, content) in entries)
                {
                    bool isDir = content == null;
                    int mode = isDir ? (writableDirs.Contains(path) ? 0x1FF /* 0777 */ : 0x1ED /* 0755 */) : 0x1A4 /* 0644 */;
                    ms.Write(TarHeader(path, mode, isDir ? 0 : content.Length, mtime, isDir), 0, 512);
                    if (isDir) continue;
                    ms.Write(content, 0, content.Length);
                    int pad = (512 - content.Length % 512) % 512;
                    ms.Write(new byte[pad], 0, pad);
                }
                ms.Write(new byte[1024], 0, 1024);
                return ms.ToArray();
            }
        }

        static byte[] TarHeader(string path, int mode, long size, long mtime, bool isDir)
        {
            var h = new byte[512];
            string name = path, prefix = "";
            if (Encoding.UTF8.GetByteCount(name) > 100)
            {
                int split = path.LastIndexOf('/', path.Length - 2);
                prefix = path.Substring(0, split);
                name = path.Substring(split + 1);
                if (name.Length > 100 || prefix.Length > 155) throw new InvalidOperationException("Path too long for tar: " + path);
            }
            Put(h, 0, 100, name);
            PutOctal(h, 100, 8, mode);
            PutOctal(h, 108, 8, 0);
            PutOctal(h, 116, 8, 0);
            PutOctal(h, 124, 12, size);
            PutOctal(h, 136, 12, mtime);
            for (int i = 148; i < 156; i++) h[i] = (byte)' ';
            h[156] = (byte)(isDir ? '5' : '0');
            Put(h, 257, 6, "ustar");
            Put(h, 263, 2, "00");
            Put(h, 265, 32, "root");
            Put(h, 297, 32, "root");
            Put(h, 345, 155, prefix);
            int sum = h.Sum(b => b);
            PutOctal(h, 148, 7, sum);
            h[154] = 0;
            return h;
        }

        static void Put(byte[] h, int offset, int length, string value)
        {
            var b = Encoding.UTF8.GetBytes(value);
            Buffer.BlockCopy(b, 0, h, offset, Math.Min(b.Length, length));
        }

        static void PutOctal(byte[] h, int offset, int length, long value) =>
            Put(h, offset, length - 1, Convert.ToString(value, 8).PadLeft(length - 1, '0'));

        static byte[] Gzip(byte[] data)
        {
            using (var ms = new MemoryStream())
            {
                using (var gz = new GZipStream(ms, CompressionLevel.Optimal, true)) gz.Write(data, 0, data.Length);
                return ms.ToArray();
            }
        }

        static byte[] Ar(IEnumerable<(string Name, byte[] Data)> members, DateTimeOffset time)
        {
            using (var ms = new MemoryStream())
            {
                var magic = Encoding.ASCII.GetBytes("!<arch>\n");
                ms.Write(magic, 0, magic.Length);
                foreach (var (name, data) in members)
                {
                    var header = name.PadRight(16) + time.ToUnixTimeSeconds().ToString().PadRight(12) +
                                 "0     0     100644  " + data.Length.ToString().PadRight(10) + "`\n";
                    var hb = Encoding.ASCII.GetBytes(header);
                    ms.Write(hb, 0, hb.Length);
                    ms.Write(data, 0, data.Length);
                    if (data.Length % 2 == 1) ms.WriteByte((byte)'\n');
                }
                return ms.ToArray();
            }
        }
    }
}
