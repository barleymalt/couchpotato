using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MagicRemote.Win32
{
    /// <summary>
    /// Big, TV-friendly pointer styles, shown while a TV is connected. They replace Windows' pointers
    /// for the session; restoring reloads the user's own pointer scheme, so nothing is left changed.
    /// </summary>
    static class PointerThemes
    {
        public static readonly IReadOnlyList<KeyValuePair<string, string>> Styles = new[]
        {
            Pair("drop", "Drop"), Pair("classic", "Classic"), Pair("cartoon", "Cartoon"),
            Pair("fantasy", "Fantasy"), Pair("scifi", "Sci-fi"), Pair("retro", "Retro"), Pair("windows", "Windows default"),
        };

        /// <summary>Pointer size as a share of the TV screen height.</summary>
        public static readonly IReadOnlyList<KeyValuePair<string, double>> Sizes = new[]
        {
            new KeyValuePair<string, double>("small", 0.03), new KeyValuePair<string, double>("medium", 0.045),
            new KeyValuePair<string, double>("large", 0.06), new KeyValuePair<string, double>("huge", 0.09),
        };

        // Windows pointer → our image. Others (resize arrows, "not allowed"…) stay as they are.
        static readonly (uint Id, string Shape)[] Replaced =
        {
            (32512, "pointer"),  // OCR_NORMAL
            (32649, "pointer"),  // OCR_HAND
            (32513, "text"),     // OCR_IBEAM
            (32514, "busy"),     // OCR_WAIT
            (32650, "working"),  // OCR_APPSTARTING
        };

        static readonly Dictionary<string, Point> Hotspots = LoadHotspots();
        static readonly string AppliedMarker = Path.Combine(Program.DataDir, "pointer-applied");

        public static Image Preview(string style)
        {
            if (style == "windows") return null;
            using (var s = typeof(PointerThemes).Assembly.GetManifestResourceStream("Cursors/" + style + "-pointer.png"))
                return s == null ? null : new Bitmap(s);
        }

        public static void Apply(string style, string size, int screenHeight)
        {
            if (style == "windows" || !Hotspots.ContainsKey(style + "/pointer"))
            {
                Restore();
                return;
            }
            double share = Sizes.FirstOrDefault(s => s.Key == size).Value;
            int pixels = Math.Max(32, Math.Min(256, (int)Math.Round(screenHeight * (share > 0 ? share : 0.06))));
            try
            {
                File.WriteAllText(AppliedMarker, style);
                foreach (var (id, shape) in Replaced)
                {
                    using (var s = typeof(PointerThemes).Assembly.GetManifestResourceStream("Cursors/" + style + "-" + shape + ".png"))
                    using (var image = new Bitmap(s))
                    {
                        var cursor = CreateCursor(image, Hotspots[style + "/" + shape], pixels);
                        if (cursor != IntPtr.Zero) SetSystemCursor(cursor, id); // takes ownership of the handle
                    }
                }
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

        static IntPtr CreateCursor(Bitmap source, Point hotspot, int size)
        {
            using (var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bitmap))
                {
                    g.CompositingMode = CompositingMode.SourceCopy;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(source, new Rectangle(0, 0, size, size));
                }

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
                var info = new ICONINFO
                {
                    fIcon = false,
                    xHotspot = (uint)(hotspot.X * size / 256),
                    yHotspot = (uint)(hotspot.Y * size / 256),
                    hbmMask = mask,
                    hbmColor = color,
                };
                var cursor = CreateIconIndirect(ref info);
                DeleteObject(mask);
                DeleteObject(color);
                return cursor;
            }
        }

        static Dictionary<string, Point> LoadHotspots()
        {
            var result = new Dictionary<string, Point>();
            using (var s = typeof(PointerThemes).Assembly.GetManifestResourceStream("Cursors/hotspots.txt"))
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
            return result;
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
        [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER header, uint usage, out IntPtr bits, IntPtr section, uint offset);
        [DllImport("gdi32.dll")] static extern IntPtr CreateBitmap(int width, int height, uint planes, uint bitCount, IntPtr bits);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr handle);
    }
}
