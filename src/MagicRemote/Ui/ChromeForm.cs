using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MagicRemote.Ui
{
    /// <summary>
    /// A window without the Windows title bar: its own <see cref="TitleBar"/> instead. It stays a normal
    /// resizable window underneath, so dragging, snapping, double-click to maximize, the shadow, rounded
    /// corners on Windows 11 and Alt+Space keep working.
    /// </summary>
    class ChromeForm : Form
    {
        const int WM_NCCALCSIZE = 0x83, WM_NCHITTEST = 0x84;
        const int HTCLIENT = 1, HTCAPTION = 2, HTLEFT = 10, HTRIGHT = 11, HTTOP = 12, HTTOPLEFT = 13, HTTOPRIGHT = 14,
                  HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17;

        public readonly TitleBar TitleBar;

        protected ChromeForm(string title, string subtitle)
        {
            FormBorderStyle = FormBorderStyle.Sizable;
            DoubleBuffered = true;
            TitleBar = new TitleBar(this, title, subtitle);
            Controls.Add(TitleBar);
        }

        /// <summary>Width of the invisible resize edge inside the window.</summary>
        int Edge => (int)Theme.Px(this, 6);

        bool Maximized => IsHandleCreated && IsZoomed(Handle);

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int round = 2; // DWMWA_WINDOW_CORNER_PREFERENCE = DWMWCP_ROUND (Windows 11)
            DwmSetWindowAttribute(Handle, 33, ref round, 4);
            // Recalculate the frame now that WM_NCCALCSIZE removes it.
            SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, 0x0027 /* NOSIZE | NOMOVE | NOZORDER | FRAMECHANGED */);
        }

        protected override void OnLoad(EventArgs e)
        {
            TitleBar.SendToBack(); // docked first, so it sits above everything else docked to the top
            base.OnLoad(e);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            // The resize edge belongs to the window itself, so nothing may cover it, except when maximized.
            Padding = Maximized ? Padding.Empty : new Padding(Edge);
            TitleBar?.Invalidate(true);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_NCCALCSIZE && m.WParam != IntPtr.Zero)
            {
                // The whole window is client area. A maximized window hangs over the screen edges by the
                // frame width, so pull its client area back in by that much.
                if (IsZoomed(Handle))
                {
                    var p = (NcCalcSizeParams)Marshal.PtrToStructure(m.LParam, typeof(NcCalcSizeParams));
                    int frame = FrameWidth();
                    p.Rect0.Left += frame;
                    p.Rect0.Top += frame;
                    p.Rect0.Right -= frame;
                    p.Rect0.Bottom -= frame;
                    Marshal.StructureToPtr(p, m.LParam, false);
                }
                m.Result = IntPtr.Zero;
                return;
            }
            if (m.Msg == WM_NCHITTEST)
            {
                long lp = m.LParam.ToInt64();
                m.Result = (IntPtr)HitTest(PointToClient(new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF))));
                return;
            }
            base.WndProc(ref m);
        }

        int HitTest(Point p)
        {
            if (!Maximized)
            {
                int e = Edge;
                bool left = p.X < e, right = p.X >= Width - e, top = p.Y < e, bottom = p.Y >= Height - e;
                if (top && left) return HTTOPLEFT;
                if (top && right) return HTTOPRIGHT;
                if (bottom && left) return HTBOTTOMLEFT;
                if (bottom && right) return HTBOTTOMRIGHT;
                if (left) return HTLEFT;
                if (right) return HTRIGHT;
                if (top) return HTTOP;
                if (bottom) return HTBOTTOM;
            }
            // The title bar passes its hits on to the window (see TitleBar.WndProc); its buttons don't.
            return p.Y < TitleBar.Bottom ? HTCAPTION : HTCLIENT;
        }

        int FrameWidth()
        {
            try
            {
                uint dpi = GetDpiForWindow(Handle);
                return GetSystemMetricsForDpi(32 /* SM_CXFRAME */, dpi) + GetSystemMetricsForDpi(92 /* SM_CXPADDEDBORDER */, dpi);
            }
            catch (EntryPointNotFoundException) // before Windows 10 1607
            {
                return GetSystemMetrics(32) + GetSystemMetrics(92);
            }
        }

        [StructLayout(LayoutKind.Sequential)] struct Rect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] struct NcCalcSizeParams { public Rect Rect0, Rect1, Rect2; public IntPtr Pos; }

        [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr hwnd);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] static extern int GetSystemMetrics(int index);
        [DllImport("user32.dll")] static extern int GetSystemMetricsForDpi(int index, uint dpi);
        [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr hwnd);
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    }

    /// <summary>
    /// The top of a <see cref="ChromeForm"/>: logo, name, version and status, plus minimize, maximize and close.
    /// Everything but the buttons is painted, and mouse hits fall through to the window, which treats them as its caption.
    /// </summary>
    sealed class TitleBar : Control
    {
        const int HTTRANSPARENT = -1;
        readonly Form _form;
        readonly string _subtitle;
        readonly Image _logo;
        readonly CaptionButton _min, _max, _close;
        string _status = "";
        bool _good;

        public TitleBar(Form form, string title, string subtitle)
        {
            _form = form;
            Text = title;
            _subtitle = subtitle;
            Dock = DockStyle.Top;
            Height = 72;
            using (var s = typeof(TitleBar).Assembly.GetManifestResourceStream("MagicRemote.ico"))
            using (var icon = new Icon(s, 64, 64))
                _logo = icon.ToBitmap();
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

            _min = new CaptionButton(CaptionGlyph.Minimize) { AccessibleName = "Minimize" };
            _max = new CaptionButton(CaptionGlyph.Maximize) { AccessibleName = "Maximize" };
            _close = new CaptionButton(CaptionGlyph.Close) { AccessibleName = "Close" };
            _min.Click += (s, e) => _form.WindowState = FormWindowState.Minimized;
            _max.Click += (s, e) => _form.WindowState = _form.WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
            _close.Click += (s, e) => _form.Close();
            Controls.AddRange(new Control[] { _min, _max, _close });
        }

        public void SetStatus(string text, bool good)
        {
            _status = text;
            _good = good;
            Invalidate();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x84 /* WM_NCHITTEST */)
            {
                m.Result = (IntPtr)HTTRANSPARENT;
                return;
            }
            base.WndProc(ref m);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int x = Width;
            foreach (var b in new[] { _close, _max, _min })
            {
                x -= b.Width;
                b.Location = new Point(x, 0);
            }
            _max.Glyph = _form.WindowState == FormWindowState.Maximized ? CaptionGlyph.Restore : CaptionGlyph.Maximize;
            _max.AccessibleName = _form.WindowState == FormWindowState.Maximized ? "Restore" : "Maximize";
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Background);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;

            float left = Theme.Px(this, 30), top = Theme.Px(this, 22), logo = Theme.Px(this, 40);
            g.DrawImage(_logo, left, top, logo, logo);

            float textX = left + logo + Theme.Px(this, 14);
            var titleSize = TextRenderer.MeasureText(g, Text, Theme.Title);
            var subSize = TextRenderer.MeasureText(g, _subtitle, Theme.Small);
            int textY = (int)(top + (logo - titleSize.Height - subSize.Height) / 2);
            TextRenderer.DrawText(g, Text, Theme.Title, new Point((int)textX, textY), Theme.Text);
            TextRenderer.DrawText(g, _subtitle, Theme.Small, new Point((int)textX + 1, textY + titleSize.Height), Theme.Muted);

            if (string.IsNullOrEmpty(_status)) return;
            // The status chip, right-aligned before the window buttons, hidden if there's no room.
            var statusSize = TextRenderer.MeasureText(g, _status, Theme.Strong);
            float dot = Theme.Px(this, 8), padX = Theme.Px(this, 14), gap = Theme.Px(this, 8);
            float chipW = padX + dot + gap + statusSize.Width + padX, chipH = Theme.Px(this, 34);
            float chipX = _min.Left - Theme.Px(this, 12) - chipW, chipY = top + (logo - chipH) / 2;
            if (chipX < textX + Math.Max(titleSize.Width, subSize.Width) + Theme.Px(this, 16)) return;
            var accent = _good ? Theme.Accent : Theme.Muted;
            using (var path = Theme.Rounded(new RectangleF(chipX, chipY, chipW, chipH), chipH / 2))
            using (var brush = new SolidBrush(_good ? Color.FromArgb(36, Theme.Accent) : Theme.Surface))
                g.FillPath(brush, path);
            using (var brush = new SolidBrush(accent))
                g.FillEllipse(brush, chipX + padX, chipY + (chipH - dot) / 2, dot, dot);
            TextRenderer.DrawText(g, _status, Theme.Strong,
                new Rectangle((int)(chipX + padX + dot + gap), (int)chipY, statusSize.Width + 2, (int)chipH), _good ? Theme.Accent : Theme.Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _logo.Dispose();
            base.Dispose(disposing);
        }
    }

    enum CaptionGlyph { Minimize, Maximize, Restore, Close }

    /// <summary>A Windows-style window button: flat, grey on hover, red for close.</summary>
    sealed class CaptionButton : Control
    {
        CaptionGlyph _glyph;
        bool _hover;

        public CaptionButton(CaptionGlyph glyph)
        {
            _glyph = glyph;
            Size = new Size(46, 40);
            AccessibleRole = AccessibleRole.PushButton;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            SetStyle(ControlStyles.Selectable, false);
        }

        public CaptionGlyph Glyph
        {
            get => _glyph;
            set
            {
                if (_glyph == value) return;
                _glyph = value;
                Invalidate();
            }
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            bool close = _glyph == CaptionGlyph.Close;
            g.Clear(_hover ? (close ? Theme.CloseHover : Theme.SurfaceHover) : Theme.Background);
            float s = Theme.Px(this, 10), x = (Width - s) / 2f, y = (Height - s) / 2f;
            using (var pen = new Pen(_hover && close ? Color.White : Theme.Text, Math.Max(1f, Theme.Px(this, 1))))
            {
                switch (_glyph)
                {
                    case CaptionGlyph.Minimize:
                        g.DrawLine(pen, x, y + s / 2, x + s, y + s / 2);
                        break;
                    case CaptionGlyph.Maximize:
                        g.DrawRectangle(pen, x, y, s, s);
                        break;
                    case CaptionGlyph.Restore:
                        float o = Theme.Px(this, 2);
                        g.DrawRectangle(pen, x, y + o, s - o, s - o);
                        g.DrawLines(pen, new[] { new PointF(x + o, y + o), new PointF(x + o, y), new PointF(x + s, y), new PointF(x + s, y + s - o), new PointF(x + s - o, y + s - o) });
                        break;
                    default:
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.DrawLine(pen, x, y, x + s, y + s);
                        g.DrawLine(pen, x + s, y, x, y + s);
                        break;
                }
            }
        }
    }
}
