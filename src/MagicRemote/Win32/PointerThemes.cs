using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace MagicRemote.Win32
{
    /// <summary>
    /// Big, TV-friendly pointer styles, shown while a TV is connected. They replace Windows' pointers
    /// for the session; restoring reloads the user's own pointer scheme, so nothing is left changed.
    /// Built-in styles come from PNGs in the exe (Busy turns, Working is Normal plus a small Busy, both
    /// as animated pointers); pointer packs are Windows .cur/.ani sets dropped in <see cref="PacksFolder"/>.
    /// </summary>
    static class PointerThemes
    {
        static readonly KeyValuePair<string, string>[] BuiltIn =
        {
            Pair("neon", "Neon"), Pair("arcane", "Arcane"), Pair("drop", "Drop"), Pair("classic", "Classic"),
            Pair("cartoon", "Cartoon"), Pair("retro", "Retro"),
        };

        const string PackPrefix = "pack:";

        /// <summary>Style id → name: the built-in ones, the pointer packs found, then Windows' own.</summary>
        public static IReadOnlyList<KeyValuePair<string, string>> Styles =>
            BuiltIn.Concat(Packs().Select(p => Pair(PackPrefix + p.Folder, p.Name))).Concat(new[] { Pair("windows", "Windows default") }).ToList();

        /// <summary>Pointer size as a share of the TV screen height.</summary>
        public static readonly IReadOnlyList<KeyValuePair<string, double>> Sizes = new[]
        {
            new KeyValuePair<string, double>("small", 0.03), new KeyValuePair<string, double>("medium", 0.045),
            new KeyValuePair<string, double>("large", 0.06), new KeyValuePair<string, double>("huge", 0.09),
        };

        /// <summary>Where pointer packs go: one folder per pack, with its .cur/.ani files and (usually) an install.inf.</summary>
        public static string PacksFolder => Path.Combine(Program.DataDir, "Pointers");

        /// <summary>
        /// Every Windows pointer, in the order of a pointer scheme (and of install.inf files): the system
        /// cursor id, our role name, and the registry name packs use.
        /// </summary>
        static readonly (uint Id, string Role, string Registry)[] Roles =
        {
            (32512, "normal", "Arrow"), (32651, "help", "Help"), (32650, "working", "AppStarting"), (32514, "busy", "Wait"),
            (32515, "precision", "Crosshair"), (32513, "text", "IBeam"), (32631, "pen", "NWPen"), (32648, "unavailable", "No"),
            (32645, "ns", "SizeNS"), (32644, "ew", "SizeWE"), (32642, "nwse", "SizeNWSE"), (32643, "nesw", "SizeNESW"),
            (32646, "move", "SizeAll"), (32516, "up", "UpArrow"), (32649, "link", "Hand"), (32671, "location", "Pin"),
            (32672, "person", "Person"),
        };

        static readonly Dictionary<string, Point> Hotspots = LoadHotspots();
        static readonly string AppliedMarker = Path.Combine(Program.DataDir, "pointer-applied");

        /// <summary>Styles that were renamed when they were redrawn.</summary>
        public static string Normalize(string style) => style == "scifi" ? "neon" : style == "fantasy" ? "arcane" : style;

        public static Image Preview(string style)
        {
            style = Normalize(style);
            if (style == "windows") return null;
            if (style.StartsWith(PackPrefix))
            {
                var file = Packs().FirstOrDefault(p => PackPrefix + p.Folder == style)?.Files["normal"];
                return file == null ? null : FileImage(file, 64);
            }
            return Png(style, "normal");
        }

        public static void Apply(string style, string size, int screenHeight)
        {
            style = Normalize(style);
            double share = Sizes.FirstOrDefault(s => s.Key == size).Value;
            int pixels = Math.Max(32, Math.Min(256, (int)Math.Round(screenHeight * (share > 0 ? share : 0.06))));
            // Start from the user's own pointers: a style (or pack) without some pointers must not keep the
            // previous style's ones, e.g. Neon's resize arrows after switching to Drop.
            SystemParametersInfo(SPI_SETCURSORS, 0, IntPtr.Zero, 0);
            try
            {
                if (style.StartsWith(PackPrefix))
                {
                    var pack = Packs().FirstOrDefault(p => PackPrefix + p.Folder == style);
                    if (pack == null)
                    {
                        Restore();
                        return;
                    }
                    File.WriteAllText(AppliedMarker, style);
                    foreach (var role in Roles)
                        if (pack.Files.TryGetValue(role.Role, out var file)) Set(LoadImage(IntPtr.Zero, file, 2 /* IMAGE_CURSOR */, pixels, pixels, 0x10 /* LR_LOADFROMFILE */), role.Id);
                    return;
                }
                if (!Hotspots.ContainsKey(style + "/normal"))
                {
                    Restore();
                    return;
                }
                File.WriteAllText(AppliedMarker, style);
                foreach (var role in Roles) ApplyBuiltIn(style, role.Role, role.Id, pixels);
            }
            catch (Exception e)
            {
                Log.Warn("Can't set the pointer style: " + e.Message);
                Restore();
            }
        }

        /// <summary>Puts the user's own pointers back. Also run at startup in case the app was killed while connected.</summary>
        public static void Restore()
        {
            SystemParametersInfo(SPI_SETCURSORS, 0, IntPtr.Zero, 0);
            try { File.Delete(AppliedMarker); } catch (IOException) { }
        }

        public static bool WasLeftApplied => File.Exists(AppliedMarker);

        // ------------------------------------------------------------------ built-in styles

        /// <summary>How a style's Busy pointer moves: turns of <see cref="Step"/> degrees per frame, plus an effect.</summary>
        sealed class Motion
        {
            public int Frames = 24;
            public float Step = 15;
            public int Jiffies = 4; // 1/60 s per frame
            public bool Glitch, Pulse;
        }

        static Motion MotionOf(string style)
        {
            switch (style)
            {
                case "neon": return new Motion { Step = 30, Jiffies = 6, Glitch = true }; // ticks round, glitches once every two turns
                case "arcane": return new Motion { Step = 15, Jiffies = 8, Pulse = true }; // turns slowly and glows
                case "retro": return new Motion { Frames = 8, Step = 45, Jiffies = 6 };
                default: return new Motion();
            }
        }

        static void ApplyBuiltIn(string style, string role, uint id, int pixels)
        {
            if (role == "busy" || role == "working")
            {
                using (var busy = Png(style, "busy"))
                {
                    if (busy == null) return;
                    var motion = MotionOf(style);
                    var frames = new List<Bitmap>();
                    try
                    {
                        Point hotspot;
                        if (role == "busy")
                        {
                            for (int i = 0; i < motion.Frames; i++) frames.Add(SpinnerFrame(busy, pixels, motion, i));
                            hotspot = Scale(Hotspots[style + "/busy"], pixels);
                        }
                        else
                        {
                            using (var normal = Png(style, "normal"))
                            {
                                int small = (int)(pixels * 0.46), at = (int)(pixels * 0.56);
                                for (int i = 0; i < motion.Frames; i++)
                                {
                                    var frame = new Bitmap(pixels, pixels, PixelFormat.Format32bppArgb);
                                    using (var g = Graphics.FromImage(frame))
                                    using (var spinner = SpinnerFrame(busy, small, motion, i))
                                    {
                                        Smooth(g);
                                        g.DrawImage(normal, new Rectangle(0, 0, pixels, pixels));
                                        g.DrawImage(spinner, new Rectangle(at, at, small, small));
                                    }
                                    frames.Add(frame);
                                }
                            }
                            hotspot = Scale(Hotspots[style + "/normal"], pixels);
                        }
                        var file = Path.Combine(CacheFolder(), style + "-" + role + "-" + pixels + ".ani");
                        File.WriteAllBytes(file, Ani(frames, hotspot, motion.Jiffies));
                        Set(LoadImage(IntPtr.Zero, file, 2 /* IMAGE_CURSOR */, pixels, pixels, 0x10 /* LR_LOADFROMFILE */), id);
                    }
                    finally
                    {
                        foreach (var f in frames) f.Dispose();
                    }
                }
                return;
            }

            var image = Png(style, role) ?? (role == "link" ? Png(style, "normal") : null);
            if (image == null) return; // a simple style without this role: Windows' own pointer stays
            using (image)
            {
                var key = Hotspots.ContainsKey(style + "/" + role) ? style + "/" + role : style + "/normal";
                using (var scaled = new Bitmap(pixels, pixels, PixelFormat.Format32bppArgb))
                {
                    using (var g = Graphics.FromImage(scaled))
                    {
                        Smooth(g);
                        g.DrawImage(image, new Rectangle(0, 0, pixels, pixels));
                    }
                    Set(CreateCursor(scaled, Scale(Hotspots[key], pixels)), id);
                }
            }
        }

        /// <summary>Frame <paramref name="i"/> of the turning Busy pointer, with the style's effect.</summary>
        static Bitmap SpinnerFrame(Image busy, int size, Motion motion, int i)
        {
            var frame = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(frame))
            using (var attributes = new ImageAttributes())
            {
                Smooth(g);
                float c = size / 2f, dx = 0, dy = 0;
                var matrix = new ColorMatrix(); // identity
                if (motion.Glitch && (i == motion.Frames - 4 || i == motion.Frames - 3))
                {
                    // A glitch: knocked sideways, red and blue swapped.
                    bool first = i == motion.Frames - 4;
                    dx = size * (first ? 0.03f : -0.03f);
                    dy = size * (first ? -0.015f : 0.01f);
                    matrix = new ColorMatrix(new[]
                    {
                        new float[] { 0, 0, 1, 0, 0 }, new float[] { 0, 1, 0, 0, 0 }, new float[] { 1, 0, 0, 0, 0 },
                        new float[] { 0, 0, 0, 1, 0 }, new float[] { 0, 0, 0, 0, 1 },
                    });
                }
                if (motion.Pulse)
                {
                    float glow = 1 + 0.22f * (float)Math.Sin(2 * Math.PI * i / 12); // two slow breaths per turn
                    matrix.Matrix00 = matrix.Matrix11 = matrix.Matrix22 = glow;
                }
                attributes.SetColorMatrix(matrix);
                g.TranslateTransform(c + dx, c + dy);
                g.RotateTransform(i * motion.Step);
                g.TranslateTransform(-c, -c);
                g.DrawImage(busy, new Rectangle(0, 0, size, size), 0, 0, busy.Width, busy.Height, GraphicsUnit.Pixel, attributes);
            }
            return frame;
        }

        static void Smooth(Graphics g)
        {
            g.CompositingMode = CompositingMode.SourceOver;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.SmoothingMode = SmoothingMode.HighQuality;
        }

        static Point Scale(Point hotspot, int size) => new Point(hotspot.X * size / 256, hotspot.Y * size / 256);

        static Bitmap Png(string style, string role)
        {
            using (var s = typeof(PointerThemes).Assembly.GetManifestResourceStream("Cursors/" + style + "-" + role + ".png"))
                return s == null ? null : new Bitmap(s);
        }

        static string CacheFolder()
        {
            var folder = Path.Combine(Program.DataDir, "pointer-cache");
            Directory.CreateDirectory(folder);
            return folder;
        }

        /// <summary>An animated pointer file: RIFF "ACON" with one .cur per frame, all at one rate.</summary>
        static byte[] Ani(IList<Bitmap> frames, Point hotspot, int jiffies)
        {
            var icons = frames.Select(f => Cur(f, hotspot)).ToList();
            using (var stream = new MemoryStream())
            using (var w = new BinaryWriter(stream))
            {
                w.Write(Tag("RIFF"));
                w.Write(0); // size, below
                w.Write(Tag("ACON"));
                w.Write(Tag("anih"));
                w.Write(36);
                foreach (var v in new[] { 36, icons.Count, icons.Count, 0, 0, 0, 0, jiffies, 1 /* AF_ICON */ }) w.Write(v);
                w.Write(Tag("LIST"));
                w.Write(4 + icons.Sum(i => 8 + i.Length + (i.Length & 1)));
                w.Write(Tag("fram"));
                foreach (var icon in icons)
                {
                    w.Write(Tag("icon"));
                    w.Write(icon.Length);
                    w.Write(icon);
                    if ((icon.Length & 1) == 1) w.Write((byte)0);
                }
                w.Flush();
                stream.Position = 4;
                w.Write((int)stream.Length - 8);
                return stream.ToArray();
            }
        }

        /// <summary>A one-image .cur file: 32-bit colour with straight alpha, an empty AND mask, the hotspot.</summary>
        static byte[] Cur(Bitmap image, Point hotspot)
        {
            int w = image.Width, h = image.Height, maskRow = (w + 31) / 32 * 4;
            var pixels = new byte[w * h * 4];
            var data = image.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                for (int y = 0; y < h; y++) // bottom-up rows
                    Marshal.Copy(data.Scan0 + y * data.Stride, pixels, (h - 1 - y) * w * 4, w * 4);
            }
            finally
            {
                image.UnlockBits(data);
            }
            int imageSize = 40 + pixels.Length + maskRow * h;
            using (var stream = new MemoryStream())
            using (var o = new BinaryWriter(stream))
            {
                o.Write((ushort)0); o.Write((ushort)2); o.Write((ushort)1); // ICONDIR: a cursor, one image
                o.Write((byte)(w >= 256 ? 0 : w)); o.Write((byte)(h >= 256 ? 0 : h)); o.Write((byte)0); o.Write((byte)0);
                o.Write((ushort)hotspot.X); o.Write((ushort)hotspot.Y); o.Write(imageSize); o.Write(22);
                o.Write(40); o.Write(w); o.Write(h * 2); o.Write((ushort)1); o.Write((ushort)32); // BITMAPINFOHEADER
                o.Write(0); o.Write(pixels.Length + maskRow * h); o.Write(0); o.Write(0); o.Write(0); o.Write(0);
                o.Write(pixels);
                o.Write(new byte[maskRow * h]);
                return stream.ToArray();
            }
        }

        static byte[] Tag(string fourCc) => System.Text.Encoding.ASCII.GetBytes(fourCc);

        static void Set(IntPtr cursor, uint id)
        {
            if (cursor != IntPtr.Zero) SetSystemCursor(cursor, id); // takes ownership of the handle
        }

        static IntPtr CreateCursor(Bitmap bitmap, Point hotspot)
        {
            int size = bitmap.Width;
            var header = new BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf(typeof(BITMAPINFOHEADER)),
                biWidth = size,
                biHeight = -size, // top-down, like the locked bits below
                biPlanes = 1,
                biBitCount = 32,
            };
            var color = CreateDIBSection(IntPtr.Zero, ref header, 0, out var bits, IntPtr.Zero, 0);
            if (color == IntPtr.Zero) return IntPtr.Zero;
            var data = bitmap.LockBits(new Rectangle(0, 0, size, size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var pixels = new byte[size * size * 4];
            Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
            Marshal.Copy(pixels, 0, bits, pixels.Length);
            bitmap.UnlockBits(data);

            var mask = CreateBitmap(size, size, 1, 1, IntPtr.Zero);
            var info = new ICONINFO { fIcon = false, xHotspot = (uint)hotspot.X, yHotspot = (uint)hotspot.Y, hbmMask = mask, hbmColor = color };
            var cursor = CreateIconIndirect(ref info);
            DeleteObject(mask);
            DeleteObject(color);
            return cursor;
        }

        static Dictionary<string, Point> LoadHotspots()
        {
            var result = new Dictionary<string, Point>();
            var assembly = typeof(PointerThemes).Assembly;
            foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith("Cursors/hotspots")))
            {
                using (var s = assembly.GetManifestResourceStream(name))
                using (var reader = new StreamReader(s))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        var parts = line.Split(' ');
                        if (line.StartsWith("#") || parts.Length != 4) continue;
                        result[parts[0] + "/" + parts[1]] = new Point(int.Parse(parts[2]), int.Parse(parts[3]));
                    }
                }
            }
            return result;
        }

        // ------------------------------------------------------------------ pointer packs

        sealed class Pack
        {
            public string Folder, Name;
            public Dictionary<string, string> Files = new Dictionary<string, string>();
        }

        /// <summary>Pointer packs in <see cref="PacksFolder"/> and in a Pointers folder next to the exe.</summary>
        static List<Pack> Packs()
        {
            var packs = new List<Pack>();
            var roots = new[] { PacksFolder, Path.Combine(Path.GetDirectoryName(Application.ExecutablePath) ?? "", "Pointers") };
            foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!Directory.Exists(root)) continue;
                foreach (var dir in Directory.GetDirectories(root))
                {
                    try
                    {
                        var pack = ReadPack(dir);
                        if (pack.Files.ContainsKey("normal") && packs.All(p => p.Folder != pack.Folder)) packs.Add(pack);
                    }
                    catch (Exception e)
                    {
                        Log.Warn("Can't read the pointer pack in " + Path.GetFileName(dir) + ": " + e.Message);
                    }
                }
            }
            return packs.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        /// <summary>A pack's files: from its install.inf when it has one, else guessed from the file names.</summary>
        static Pack ReadPack(string dir)
        {
            var pack = new Pack { Folder = Path.GetFileName(dir), Name = Path.GetFileName(dir) };
            var inf = Directory.GetFiles(dir, "*.inf").FirstOrDefault();
            if (inf != null) ReadInf(inf, dir, pack);
            if (pack.Files.Count == 0) Guess(dir, pack);
            return pack;
        }

        static void ReadInf(string inf, string dir, Pack pack)
        {
            var strings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var lines = File.ReadAllLines(inf);
            string section = "";
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.StartsWith("[")) { section = line.Trim('[', ']').Trim(); continue; }
                if (!section.Equals("Strings", StringComparison.OrdinalIgnoreCase)) continue;
                int eq = line.IndexOf('=');
                if (eq > 0) strings[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim().Trim('"');
            }
            string Expand(string s) => Regex.Replace(s, "%([^%]+)%", m => strings.TryGetValue(m.Groups[1].Value, out var v) ? v : "");
            string FileOf(string path)
            {
                var name = Path.GetFileName(Expand(path).Replace('/', '\\').Split('\\').Last().Trim());
                var file = Path.Combine(dir, name);
                return name.Length > 0 && File.Exists(file) ? file : null;
            }
            if (strings.TryGetValue("SCHEME_NAME", out var scheme) && scheme.Length > 0) pack.Name = scheme;

            foreach (var raw in lines)
            {
                var quoted = Regex.Matches(raw, "\"([^\"]*)\"").Cast<Match>().Select(m => m.Groups[1].Value).ToList();
                if (raw.IndexOf(@"Cursors\Schemes", StringComparison.OrdinalIgnoreCase) >= 0 && quoted.Count > 0)
                {
                    // One line for the whole scheme: the files in scheme order, comma-separated.
                    var files = quoted.Last().Split(',');
                    for (int i = 0; i < files.Length && i < Roles.Length; i++)
                    {
                        var file = FileOf(files[i]);
                        if (file != null) pack.Files[Roles[i].Role] = file;
                    }
                }
                else if (raw.IndexOf(@"Control Panel\Cursors", StringComparison.OrdinalIgnoreCase) >= 0 && quoted.Count > 0)
                {
                    // One line per pointer: HKCU,"Control Panel\Cursors",Arrow,0x00020000,"%10%\...\file.cur"
                    var parts = raw.Split(',').Select(p => p.Trim().Trim('"')).ToList();
                    var role = Roles.FirstOrDefault(r => parts.Count > 2 && r.Registry.Equals(parts[2], StringComparison.OrdinalIgnoreCase));
                    var file = role.Role == null ? null : FileOf(quoted.Last());
                    if (file != null && !pack.Files.ContainsKey(role.Role)) pack.Files[role.Role] = file;
                }
            }
        }

        static void Guess(string dir, Pack pack)
        {
            var words = new (string Role, string[] Words)[]
            {
                ("working", new[] { "working", "appstarting", "background" }), ("busy", new[] { "busy", "wait" }),
                ("help", new[] { "help" }), ("precision", new[] { "precision", "cross" }), ("text", new[] { "text", "ibeam", "beam" }),
                ("pen", new[] { "handwriting", "pen" }), ("unavailable", new[] { "unavailable", "unavail" }),
                ("ns", new[] { "vert", "sizens", "ns." }), ("ew", new[] { "horz", "horiz", "sizewe", "ew." }),
                ("nwse", new[] { "dgn1", "nwse", "diag1" }), ("nesw", new[] { "dgn2", "nesw", "diag2" }), ("move", new[] { "move", "sizeall" }),
                ("up", new[] { "alternate", "uparrow", "up." }), ("link", new[] { "link", "hand" }), ("location", new[] { "location" }),
                ("person", new[] { "person" }), ("normal", new[] { "normal", "arrow", "pointer", "select" }),
            };
            var files = Directory.GetFiles(dir).Where(f => f.EndsWith(".cur", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".ani", StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var file in files)
            {
                var name = Path.GetFileName(file).ToLowerInvariant();
                var match = words.FirstOrDefault(w => !pack.Files.ContainsKey(w.Role) && w.Words.Any(name.Contains));
                if (match.Role != null) pack.Files[match.Role] = file;
            }
        }

        static Image FileImage(string file, int size)
        {
            var handle = LoadImage(IntPtr.Zero, file, 2 /* IMAGE_CURSOR */, size, size, 0x10 /* LR_LOADFROMFILE */);
            if (handle == IntPtr.Zero) return null;
            try
            {
                using (var icon = Icon.FromHandle(handle)) return icon.ToBitmap();
            }
            finally
            {
                DestroyCursor(handle);
            }
        }

        static KeyValuePair<string, string> Pair(string a, string b) => new KeyValuePair<string, string>(a, b);

        const uint SPI_SETCURSORS = 0x0057;

        [StructLayout(LayoutKind.Sequential)]
        struct ICONINFO
        {
            public bool fIcon;
            public uint xHotspot, yHotspot;
            public IntPtr hbmMask, hbmColor;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct BITMAPINFOHEADER
        {
            public uint biSize;
            public int biWidth, biHeight;
            public ushort biPlanes, biBitCount;
            public uint biCompression, biSizeImage;
            public int biXPelsPerMeter, biYPelsPerMeter;
            public uint biClrUsed, biClrImportant;
        }

        [DllImport("user32.dll", SetLastError = true)] static extern bool SetSystemCursor(IntPtr cursor, uint id);
        [DllImport("user32.dll", SetLastError = true)] static extern bool SystemParametersInfo(uint action, uint param, IntPtr data, uint winIni);
        [DllImport("user32.dll", SetLastError = true)] static extern IntPtr CreateIconIndirect(ref ICONINFO info);
        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern IntPtr LoadImage(IntPtr instance, string name, uint type, int cx, int cy, uint load);
        [DllImport("user32.dll")] static extern bool DestroyCursor(IntPtr cursor);
        [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER header, uint usage, out IntPtr bits, IntPtr section, uint offset);
        [DllImport("gdi32.dll")] static extern IntPtr CreateBitmap(int width, int height, uint planes, uint bitCount, IntPtr bits);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr handle);
    }
}
