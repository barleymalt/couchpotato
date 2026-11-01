using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MagicRemote.Ui
{
    /// <summary>
    /// Cross-fades an area of a window when its content changes (switching tabs): a picture of the old
    /// content, in a click-through window on top, fades out while the new content draws underneath, so
    /// the drawing itself is never seen. Off when Windows' animation effects are off.
    /// </summary>
    sealed class PageFade : IDisposable
    {
        const int DurationMs = 180;
        readonly Form _owner;
        readonly Control _area;
        readonly Cover _cover = new Cover();
        readonly Timer _timer = new Timer { Interval = 15 };
        readonly Stopwatch _clock = new Stopwatch();

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
            var picture = Animations() ? Snapshot() : null;
            if (picture == null)
            {
                change();
                return;
            }
            _cover.BackgroundImage?.Dispose();
            _cover.BackgroundImage = picture;
            _cover.Bounds = _area.RectangleToScreen(_area.ClientRectangle);
            _cover.Opacity = 0.99; // layered from the start, so showing it doesn't flash
            _cover.Show();
            _cover.Update();
            change();
            // Draw the new content now, under the cover, children included.
            RedrawWindow(_area.Handle, IntPtr.Zero, IntPtr.Zero, 0x0100 /* UPDATENOW */ | 0x0080 /* ALLCHILDREN */);
            _clock.Restart();
            _timer.Start();
        }

        void Step()
        {
            double t = Math.Min(1, _clock.ElapsedMilliseconds / (double)DurationMs);
            if (t >= 1)
            {
                Stop();
                return;
            }
            _cover.Opacity = 0.99 * Math.Pow(1 - t, 2); // fast, then settling
        }

        void Stop()
        {
            _timer.Stop();
            if (_cover.Visible) _cover.Hide();
        }

        /// <summary>The area as it looks now, from Windows' copy of the window (right even if it's partly covered).</summary>
        Bitmap Snapshot()
        {
            if (!_owner.Visible || _owner.WindowState == FormWindowState.Minimized) return null;
            var window = _owner.Bounds;
            var area = _area.RectangleToScreen(_area.ClientRectangle);
            if (window.Width <= 0 || window.Height <= 0 || area.Width <= 0 || area.Height <= 0) return null;
            using (var whole = new Bitmap(window.Width, window.Height))
            {
                using (var g = Graphics.FromImage(whole))
                {
                    var hdc = g.GetHdc();
                    bool ok;
                    try { ok = PrintWindow(_owner.Handle, hdc, 2 /* PW_RENDERFULLCONTENT */); }
                    finally { g.ReleaseHdc(hdc); }
                    if (!ok) return null;
                }
                var picture = new Bitmap(area.Width, area.Height);
                using (var g = Graphics.FromImage(picture))
                    g.DrawImage(whole, new Rectangle(0, 0, area.Width, area.Height), new Rectangle(area.X - window.X, area.Y - window.Y, area.Width, area.Height), GraphicsUnit.Pixel);
                return picture;
            }
        }

        static bool Animations()
        {
            return !SystemParametersInfo(0x1042 /* SPI_GETCLIENTAREAANIMATION */, 0, out bool on, 0) || on;
        }

        public void Dispose()
        {
            Stop();
            _timer.Dispose();
            _cover.BackgroundImage?.Dispose();
            _cover.Dispose();
        }

        /// <summary>A borderless window that never takes focus or clicks.</summary>
        sealed class Cover : Form
        {
            public Cover()
            {
                AutoScaleMode = AutoScaleMode.None;
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.Manual;
                BackgroundImageLayout = ImageLayout.None;
                BackColor = Theme.Background;
            }

            protected override bool ShowWithoutActivation => true;

            protected override CreateParams CreateParams
            {
                get
                {
                    var cp = base.CreateParams;
                    cp.ExStyle |= 0x20 /* TRANSPARENT */ | 0x80 /* TOOLWINDOW */ | 0x08000000 /* NOACTIVATE */;
                    return cp;
                }
            }
        }

        [DllImport("user32.dll")] static extern bool RedrawWindow(IntPtr hwnd, IntPtr rect, IntPtr region, uint flags);
        [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
        [DllImport("user32.dll")] static extern bool SystemParametersInfo(uint action, uint param, out bool value, uint winIni);
    }
}
