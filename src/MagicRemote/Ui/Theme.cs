using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MagicRemote.Ui
{
    /// <summary>The "living-room dark" look shared with the TV app: dark slate, teal accent, big type.</summary>
    static class Theme
    {
        public static readonly Color Background = Color.FromArgb(0x14, 0x18, 0x1F);
        public static readonly Color Surface = Color.FromArgb(0x1F, 0x26, 0x30);
        public static readonly Color SurfaceHover = Color.FromArgb(0x2A, 0x33, 0x40);
        public static readonly Color Field = Color.FromArgb(0x15, 0x1A, 0x21);
        public static readonly Color Border = Color.FromArgb(0x36, 0x40, 0x4D);
        public static readonly Color Text = Color.FromArgb(0xE8, 0xED, 0xF2);
        public static readonly Color Muted = Color.FromArgb(0x9A, 0xA5, 0xB1);
        public static readonly Color Accent = Color.FromArgb(0x52, 0xC4, 0xA0);
        public static readonly Color AccentHover = Color.FromArgb(0x6E, 0xD4, 0xB4);
        public static readonly Color OnAccent = Color.FromArgb(0x0B, 0x26, 0x1E);
        public static readonly Color Danger = Color.FromArgb(0xF0, 0x7E, 0x7E);
        public static readonly Color Warning = Color.FromArgb(0xF2, 0xC1, 0x4E);

        public static readonly Color CloseHover = Color.FromArgb(0xC4, 0x2B, 0x1C);

        // Manrope (SIL Open Font License, Fonts/OFL.txt) is embedded; Segoe UI if it can't be loaded.
        static readonly PrivateFontCollection Embedded = new PrivateFontCollection();
        static readonly FontFamily Regular = LoadFont("Manrope", "Segoe UI", "Manrope-Regular.ttf", "Manrope-Bold.ttf");
        static readonly FontFamily SemiBold = LoadFont("Manrope SemiBold", "Segoe UI Semibold", "Manrope-SemiBold.ttf");

        // Sizes in points: 11.25 pt is 15 px at 100 % scaling.
        public static readonly Font Body = new Font(Regular, 11.25f);
        /// <summary>Instructions and page introductions: a step larger than the rest.</summary>
        public static readonly Font Instructions = new Font(Regular, 12f);
        public static readonly Font Small = new Font(Regular, 10.5f);
        public static readonly Font Strong = new Font(SemiBold, 11.25f);
        public static readonly Font Heading = new Font(Regular, 14.25f, FontStyle.Bold);
        public static readonly Font Title = new Font(Regular, 15.75f, FontStyle.Bold);

        /// <summary>
        /// Loads embedded font files for both drawing paths: GDI+ (Font objects) through a private collection,
        /// and GDI (TextRenderer, text boxes, lists) through AddFontMemResourceEx, which finds them by name.
        /// </summary>
        static FontFamily LoadFont(string family, string fallback, params string[] files)
        {
            try
            {
                foreach (var file in files)
                {
                    byte[] data;
                    using (var s = typeof(Theme).Assembly.GetManifestResourceStream("Fonts/" + file))
                    using (var m = new MemoryStream())
                    {
                        s.CopyTo(m);
                        data = m.ToArray();
                    }
                    // Both keep using this memory for the life of the process, so it's never freed.
                    var memory = Marshal.AllocCoTaskMem(data.Length);
                    Marshal.Copy(data, 0, memory, data.Length);
                    Embedded.AddMemoryFont(memory, data.Length);
                    AddFontMemResourceEx(memory, (uint)data.Length, IntPtr.Zero, out _);
                }
                var loaded = Embedded.Families.FirstOrDefault(f => f.Name == family);
                if (loaded != null) return loaded;
            }
            catch (Exception e)
            {
                Log.Warn("Can't load the " + family + " font: " + e.Message);
            }
            return new FontFamily(fallback);
        }

        /// <summary>Dark window: background, fonts, and a dark title bar on Windows 10 1809+ and 11.</summary>
        public static void Apply(Form form)
        {
            form.BackColor = Background;
            form.ForeColor = Text;
            form.Font = Body;
            form.HandleCreated += (s, e) =>
            {
                int on = 1;
                // 20 = DWMWA_USE_IMMERSIVE_DARK_MODE (19 on Windows 10 before 20H1).
                if (DwmSetWindowAttribute(form.Handle, 20, ref on, 4) != 0) DwmSetWindowAttribute(form.Handle, 19, ref on, 4);
            };
        }

        /// <summary>Dark scrollbars for a scrolling panel (ignored on old Windows).</summary>
        public static void DarkScrollbars(Control control)
        {
            control.HandleCreated += (s, e) =>
            {
                try { SetWindowTheme(control.Handle, "DarkMode_Explorer", null); } catch (Exception) { }
            };
        }

        public static GraphicsPath Rounded(RectangleF r, float radius)
        {
            var path = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d <= 0)
            {
                path.AddRectangle(r);
                return path;
            }
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        /// <summary>Scales a 96-DPI length to the control's DPI.</summary>
        public static float Px(Control c, float value) => value * c.DeviceDpi / 96f;

        /// <summary>The colour behind a control, for painting its rounded corners.</summary>
        public static Color Behind(Control c) => c.Parent?.BackColor ?? Background;

        [DllImport("gdi32.dll")] static extern IntPtr AddFontMemResourceEx(IntPtr font, uint length, IntPtr reserved, out uint count);
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);
    }

    /// <summary>Dark colours for the tray menu.</summary>
    sealed class DarkMenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Theme.Surface;
        public override Color MenuBorder => Theme.Border;
        public override Color MenuItemBorder => Theme.SurfaceHover;
        public override Color MenuItemSelected => Theme.SurfaceHover;
        public override Color MenuItemSelectedGradientBegin => Theme.SurfaceHover;
        public override Color MenuItemSelectedGradientEnd => Theme.SurfaceHover;
        public override Color ImageMarginGradientBegin => Theme.Surface;
        public override Color ImageMarginGradientMiddle => Theme.Surface;
        public override Color ImageMarginGradientEnd => Theme.Surface;
        public override Color SeparatorDark => Theme.Border;
        public override Color SeparatorLight => Theme.Surface;
        public override Color CheckBackground => Theme.SurfaceHover;
        public override Color CheckSelectedBackground => Theme.SurfaceHover;
        public override Color CheckPressedBackground => Theme.SurfaceHover;
    }
}
