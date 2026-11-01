using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Couchpotato.Ui
{
    /// <summary>
    /// Cross-fades an area of a window when its content changes (switching tabs): a picture of the old
    /// content, in a click-through window on top, fades out while the new content draws underneath, so
    /// the drawing itself is never seen. Off when Windows' animation effects are off.
    /// The cover gets its picture through UpdateLayeredWindow before it is shown, so its first frame is
    /// already the picture (a painted window could show a blank frame first).
    /// </summary>
    sealed class PageFade : IDisposable
    {
        const int DurationMs = 180;
        readonly Form _owner;
        readonly Control _area;
        readonly Cover _cover = new Cover();
        readonly Timer _timer = new Timer { Interval = 15 };
        readonly Stopwatch _clock = new Stopwatch();
        Picture _picture;

        public PageFade(Form owner, Control area)
        {
            _owner = owner;
            _area = area;
            _cover.Owner = owner;
            _timer.Tick += (s, e) => Step();
            owner.VisibleChanged += (s, e) => Stop();
        }

        /// <summary>Runs <paramref name="change"/> under a picture of the area, then fades the picture away.</summary>
        public void Run(Action change)
        {
            Stop();
            var bounds = _area.RectangleToScreen(_area.ClientRectangle);
            _picture = Animations() && _owner.Visible && _owner.WindowState != FormWindowState.Minimized ? Picture.Of(bounds) : null;
            if (_picture == null)
            {
                change();
                return;
            }
            _picture.Show(_cover.Handle, bounds.Location, 255);
            ShowWindow(_cover.Handle, 4 /* SW_SHOWNOACTIVATE */);
            change();
            // Draw the new content now, under the cover, children included.
            RedrawWindow(_area.Handle, IntPtr.Zero, IntPtr.Zero, 0x0100 /* UPDATENOW */ | 0x0080 /* ALLCHILDREN */);
            _clock.Restart();
            _timer.Start();
        }

        void Step()
        {
            double t = _clock.ElapsedMilliseconds / (double)DurationMs;
            if (t >= 1 || _picture == null)
            {
                Stop();
                return;
            }
            _picture.Show(_cover.Handle, null, (byte)(255 * Math.Pow(1 - t, 2))); // fast, then settling
        }

        void Stop()
        {
            _timer.Stop();
            if (_cover.IsHandleCreated) ShowWindow(_cover.Handle, 0 /* SW_HIDE */);
            _picture?.Dispose();
            _picture = null;
        }

        static bool Animations()
        {
            return !SystemParametersInfo(0x1042 /* SPI_GETCLIENTAREAANIMATION */, 0, out bool on, 0) || on;
        }

        public void Dispose()
        {
            Stop();
            _timer.Dispose();
            _cover.Dispose();
        }

        /// <summary>What's on screen in an area, as a GDI bitmap ready for UpdateLayeredWindow.</summary>
        sealed class Picture : IDisposable
        {
            IntPtr _dc, _bitmap, _old;
            Size _size;

            public static Picture Of(Rectangle screen)
            {
                if (screen.Width <= 0 || screen.Height <= 0) return null;
                using (var copy = new Bitmap(screen.Width, screen.Height))
                {
                    // The window was just clicked, so it's in front: the screen shows exactly what the user sees.
                    using (var g = Graphics.FromImage(copy)) g.CopyFromScreen(screen.Location, Point.Empty, screen.Size);
                    var p = new Picture { _size = screen.Size, _dc = CreateCompatibleDC(IntPtr.Zero), _bitmap = copy.GetHbitmap() };
                    p._old = SelectObject(p._dc, p._bitmap);
                    return p;
                }
            }

            public void Show(IntPtr window, Point? at, byte alpha)
            {
                var blend = new BLENDFUNCTION { BlendOp = 0 /* AC_SRC_OVER */, SourceConstantAlpha = alpha };
                var size = new SIZE { cx = _size.Width, cy = _size.Height };
                var source = new POINT();
                if (at.HasValue)
                {
                    var position = new POINT { x = at.Value.X, y = at.Value.Y };
                    UpdateLayeredWindow(window, IntPtr.Zero, ref position, ref size, _dc, ref source, 0, ref blend, 2 /* ULW_ALPHA */);
                }
                else
                {
                    UpdateLayeredWindow(window, IntPtr.Zero, IntPtr.Zero, ref size, _dc, ref source, 0, ref blend, 2);
                }
            }

            public void Dispose()
            {
                if (_dc == IntPtr.Zero) return;
                SelectObject(_dc, _old);
                DeleteObject(_bitmap);
                DeleteDC(_dc);
                _dc = IntPtr.Zero;
            }
        }

        /// <summary>A layered window that never takes focus or clicks; its content comes only from UpdateLayeredWindow.</summary>
        sealed class Cover : Form
        {
            public Cover()
            {
                AutoScaleMode = AutoScaleMode.None;
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.Manual;
            }

            protected override bool ShowWithoutActivation => true;

            protected override CreateParams CreateParams
            {
                get
                {
                    var cp = base.CreateParams;
                    cp.ExStyle |= 0x80000 /* LAYERED */ | 0x20 /* TRANSPARENT */ | 0x80 /* TOOLWINDOW */ | 0x08000000 /* NOACTIVATE */;
                    return cp;
                }
            }
        }

        [StructLayout(LayoutKind.Sequential)] struct POINT { public int x, y; }
        [StructLayout(LayoutKind.Sequential)] struct SIZE { public int cx, cy; }
        [StructLayout(LayoutKind.Sequential)] struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

        [DllImport("user32.dll")] static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr dst, ref POINT position, ref SIZE size, IntPtr src, ref POINT source, int key, ref BLENDFUNCTION blend, int flags);
        [DllImport("user32.dll")] static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr dst, IntPtr position, ref SIZE size, IntPtr src, ref POINT source, int key, ref BLENDFUNCTION blend, int flags);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd, int command);
        [DllImport("user32.dll")] static extern bool RedrawWindow(IntPtr hwnd, IntPtr rect, IntPtr region, uint flags);
        [DllImport("user32.dll")] static extern bool SystemParametersInfo(uint action, uint param, out bool value, uint winIni);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
    }
}
