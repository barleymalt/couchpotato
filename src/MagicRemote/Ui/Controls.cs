using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MagicRemote.Ui
{
    /// <summary>
    /// A rounded panel that stacks its children in one column. Children with Dock = Fill take the
    /// full width (labels wrap); others keep their size.
    /// </summary>
    class Card : TableLayoutPanel
    {
        public Card(bool flat = false)
        {
            ColumnCount = 1;
            ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Dock = DockStyle.Fill;
            Padding = flat ? new Padding(0) : new Padding(26, 22, 26, 22);
            Margin = new Padding(0, 0, 0, 20);
            BackColor = flat ? Theme.Background : Theme.Surface;
            Flat = flat;
            DoubleBuffered = true;
        }

        public bool Flat { get; }

        public T Add<T>(T control) where T : Control
        {
            Controls.Add(control);
            return control;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Theme.Behind(this));
            if (Flat) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Theme.Rounded(new RectangleF(0, 0, Width - 1, Height - 1), Theme.Px(this, 16)))
            using (var brush = new SolidBrush(Theme.Surface))
                e.Graphics.FillPath(brush, path);
        }
    }

    enum PillKind { Primary, Secondary, Ghost, Tab, TabSelected }

    /// <summary>Rounded button: teal primary, quiet secondary, borderless ghost, or a tab.</summary>
    class PillButton : Button
    {
        PillKind _kind;
        bool _hover, _down;

        public PillButton(string text, PillKind kind = PillKind.Secondary)
        {
            Text = text;
            _kind = kind;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
            Font = Theme.Strong;
            Padding = new Padding(20, 9, 20, 9);
            Margin = new Padding(0, 0, 8, 0);
            Cursor = Theme.Hand;
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer, true);
        }

        public PillKind Kind
        {
            get => _kind;
            set
            {
                _kind = value;
                Invalidate();
            }
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = _down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Behind(this));
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Color fill, text, border = Color.Empty;
            switch (_kind)
            {
                case PillKind.Primary:
                case PillKind.TabSelected:
                    fill = _hover ? Theme.AccentHover : Theme.Accent;
                    text = Theme.OnAccent;
                    break;
                case PillKind.Ghost:
                    fill = _hover ? Theme.SurfaceHover : Theme.Behind(this);
                    text = ForeColor == Theme.Danger ? Theme.Danger : Theme.Text;
                    break;
                case PillKind.Tab:
                    fill = _hover ? Theme.SurfaceHover : Theme.Behind(this);
                    text = Theme.Text;
                    border = Theme.Border;
                    break;
                default:
                    fill = _hover ? Theme.SurfaceHover : Theme.Field;
                    text = Theme.Text;
                    border = Theme.Border;
                    break;
            }
            if (!Enabled)
            {
                fill = Theme.Field;
                text = Theme.Muted;
            }

            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            if (_down) r.Inflate(-1, -1);
            using (var path = Theme.Rounded(r, Height / 2f))
            {
                using (var brush = new SolidBrush(fill)) g.FillPath(brush, path);
                var ring = Focused && ShowFocusCues ? Theme.Text : border;
                if (!ring.IsEmpty)
                    using (var pen = new Pen(ring, Focused && ShowFocusCues ? 2 : 1)) g.DrawPath(pen, path);
            }
            TextRenderer.DrawText(g, Text, Font, ClientRectangle, text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
    }

    /// <summary>A switch with its label on the right.</summary>
    class Toggle : CheckBox
    {
        public Toggle(string text)
        {
            Text = text;
            AutoSize = true;
            Cursor = Theme.Hand;
            Margin = new Padding(0, 4, 0, 4);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        float TrackWidth => Theme.Px(this, 46);
        float TrackHeight => Theme.Px(this, 26);
        float Gap => Theme.Px(this, 14);

        public override Size GetPreferredSize(Size proposed)
        {
            var text = TextRenderer.MeasureText(Text, Font);
            return new Size((int)(TrackWidth + Gap) + text.Width + 2, Math.Max((int)TrackHeight + 4, text.Height + 4));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Behind(this));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float y = (Height - TrackHeight) / 2f;
            var track = new RectangleF(1, y, TrackWidth, TrackHeight);
            using (var path = Theme.Rounded(track, TrackHeight / 2f))
            using (var brush = new SolidBrush(Checked ? Theme.Accent : Theme.Border))
            using (var pen = new Pen(Checked ? Theme.Accent : Theme.Border))
            {
                g.FillPath(brush, path);
                g.DrawPath(pen, path);
            }
            float knob = TrackHeight - Theme.Px(this, 6);
            float x = Checked ? track.Right - knob - Theme.Px(this, 3) : track.X + Theme.Px(this, 3);
            using (var brush = new SolidBrush(Checked ? Theme.OnAccent : Theme.Muted))
                g.FillEllipse(brush, x, y + Theme.Px(this, 3), knob, knob);
            if (Focused && ShowFocusCues)
                using (var pen = new Pen(Theme.Text, 1.5f))
                using (var path = Theme.Rounded(RectangleF.Inflate(track, 2, 2), TrackHeight / 2f + 2))
                    g.DrawPath(pen, path);
            var textRect = new Rectangle((int)(TrackWidth + Gap), 0, Width - (int)(TrackWidth + Gap), Height);
            TextRenderer.DrawText(g, Text, Font, textRect, Enabled ? Theme.Text : Theme.Muted,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.WordBreak);
        }
    }

    /// <summary>A borderless text box inside a rounded dark field.</summary>
    class FieldBox : Panel
    {
        public readonly TextBox Box = new TextBox { BorderStyle = BorderStyle.None, BackColor = Theme.Field, ForeColor = Theme.Text };

        public FieldBox(int width)
        {
            Width = width;
            Height = 44;
            Margin = new Padding(0, 0, 10, 0);
            BackColor = Theme.Field;
            DoubleBuffered = true;
            Controls.Add(Box);
            Box.GotFocus += (s, e) => Invalidate();
            Box.LostFocus += (s, e) => Invalidate();
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int inset = (int)Theme.Px(this, 14);
            Box.SetBounds(inset, (Height - Box.Height) / 2, Width - 2 * inset, Box.Height);
        }

        protected override void OnClick(EventArgs e)
        {
            Box.Focus();
            base.OnClick(e);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Theme.Behind(this));
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Theme.Rounded(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Theme.Px(this, 10)))
            using (var brush = new SolidBrush(Theme.Field))
            using (var pen = new Pen(Box.Focused ? Theme.Accent : Theme.Border))
            {
                e.Graphics.FillPath(brush, path);
                e.Graphics.DrawPath(pen, path);
            }
        }
    }

    /// <summary>A numbered circle for setup steps.</summary>
    class StepNumber : Control
    {
        public StepNumber(int number)
        {
            Text = number.ToString();
            Size = new Size(32, 32);
            Margin = new Padding(0, 0, 16, 0);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Behind(this));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var brush = new SolidBrush(Theme.SurfaceHover)) g.FillEllipse(brush, 0, 0, Width - 1, Height - 1);
            TextRenderer.DrawText(g, Text, Theme.Strong, ClientRectangle, Theme.Accent,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
    }

    /// <summary>A small outlined TV, for the paired TV list.</summary>
    class TvIcon : Control
    {
        public TvIcon()
        {
            Size = new Size(36, 36);
            Margin = new Padding(0, 0, 16, 0);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Behind(this));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float k = Width / 36f;
            using (var pen = new Pen(Theme.Muted, 2 * k) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                using (var screen = Theme.Rounded(new RectangleF(3 * k, 7 * k, 30 * k, 19 * k), 3 * k)) g.DrawPath(pen, screen);
                g.DrawLine(pen, 12 * k, 31 * k, 24 * k, 31 * k);
            }
        }
    }

    /// <summary>A selectable picture card (pointer styles).</summary>
    class Tile : Control
    {
        readonly Image _image;
        bool _hover;

        public event EventHandler Chosen;

        public Tile(string text, Image image)
        {
            Text = text;
            _image = image;
            Size = new Size(124, 128);
            Margin = new Padding(0, 0, 10, 10);
            Cursor = Theme.Hand;
            TabStop = true;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.Selectable, true);
        }

        bool _selected;
        public bool Selected
        {
            get => _selected;
            set { _selected = value; Invalidate(); }
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnClick(EventArgs e) { Focus(); Chosen?.Invoke(this, e); base.OnClick(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) Chosen?.Invoke(this, e);
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Behind(this));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            var r = new RectangleF(1, 1, Width - 3, Height - 3);
            using (var path = Theme.Rounded(r, Theme.Px(this, 12)))
            {
                using (var brush = new SolidBrush(_hover ? Theme.SurfaceHover : Theme.Field)) g.FillPath(brush, path);
                var ring = _selected ? Theme.Accent : Focused ? Theme.Text : Theme.Border;
                using (var pen = new Pen(ring, _selected ? 2.5f : 1)) g.DrawPath(pen, path);
            }
            float size = Math.Min(Width, Height) * 0.5f;
            if (_image != null) g.DrawImage(_image, (Width - size) / 2f, Height * 0.12f, size, size);
            TextRenderer.DrawText(g, Text, _selected ? Theme.Strong : Theme.Body,
                new Rectangle(0, (int)(Height * 0.7f), Width, (int)(Height * 0.25f)), _selected ? Theme.Accent : Theme.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
    }

    /// <summary>Small factories so every page uses the same type styles.</summary>
    static class Make
    {
        public static Label Text(string text, bool muted = false, bool small = false) => new Label
        {
            Text = text,
            AutoSize = true,
            Dock = DockStyle.Fill,
            ForeColor = muted ? Theme.Muted : Theme.Text,
            Font = small ? Theme.Small : Theme.Body,
            Margin = new Padding(0, 2, 0, 10),
        };

        /// <summary>Instructions and page introductions, in the larger type.</summary>
        public static Label Instructions(string text, bool muted = false)
        {
            var label = Text(text, muted);
            label.Font = Theme.Instructions;
            return label;
        }

        /// <summary>A numbered setup step: the number on the left, the text and any controls beside it.</summary>
        public static TableLayoutPanel Step(int number, string text, params Control[] controls)
        {
            var step = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 4, 0, 10) };
            step.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            step.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            step.Controls.Add(new StepNumber(number));
            var column = new Card(flat: true) { BackColor = Theme.Surface, Margin = new Padding(0, 3, 0, 0) };
            var label = column.Add(Instructions(text));
            label.Margin = new Padding(0, 0, 0, controls.Length > 0 ? 8 : 0);
            foreach (var c in controls) column.Add(c);
            step.Controls.Add(column);
            return step;
        }

        public static Label Heading(string text) => new Label
        {
            Text = text,
            AutoSize = true,
            Dock = DockStyle.Fill,
            ForeColor = Theme.Text,
            Font = Theme.Heading,
            Margin = new Padding(0, 0, 0, 6),
        };

        /// <summary>A link: teal text, underlined on hover, with Windows' hand (a LinkLabel forces WinForms' own).</summary>
        public static Label Link(string text, Action onClick)
        {
            var link = new Label
            {
                Text = text,
                AutoSize = true,
                ForeColor = Theme.Accent,
                Font = Theme.Strong,
                Cursor = Theme.Hand,
                Margin = new Padding(0, 0, 0, 4),
            };
            link.MouseEnter += (s, e) => link.Font = new Font(Theme.Strong, FontStyle.Underline);
            link.MouseLeave += (s, e) =>
            {
                var underlined = link.Font;
                link.Font = Theme.Strong;
                if (!ReferenceEquals(underlined, Theme.Strong)) underlined.Dispose();
            };
            link.Click += (s, e) => onClick();
            return link;
        }

        public static Flow Row(params Control[] controls)
        {
            // No BackColor: it inherits the card's, which the rounded children paint their corners with.
            var row = new Flow { Margin = new Padding(0, 2, 0, 4) };
            row.Controls.AddRange(controls);
            return row;
        }

        /// <summary>A scrolling page whose content is one column.</summary>
        public static Panel Page(out Card column)
        {
            // With the window's 6 px resize border this makes 36 px from the window edge.
            var page = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(30, 4, 30, 30), BackColor = Theme.Background };
            Theme.DarkScrollbars(page);
            column = new Card(flat: true) { Dock = DockStyle.Top };
            page.Controls.Add(column);
            return page;
        }
    }

    /// <summary>
    /// A row that wraps, as tall as its rows at the width it really gets. In an auto-sized table a
    /// FlowLayoutPanel measures itself at a narrower width, which left empty space under it.
    /// </summary>
    class Flow : FlowLayoutPanel
    {
        // Control's own visibility (STATE_VISIBLE), which unlike Visible ignores hidden parents such as a page in another tab.
        static readonly MethodInfo GetState = typeof(Control).GetMethod("GetState", BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(int) }, null);

        public Flow()
        {
            AutoSize = true;
            WrapContents = true;
            Dock = DockStyle.Fill;
        }

        public override Size GetPreferredSize(Size proposed)
        {
            int width = Parent == null ? 0 : Parent.ClientSize.Width - Parent.Padding.Horizontal - Margin.Horizontal;
            if (width <= 0) return base.GetPreferredSize(proposed);
            width -= Padding.Horizontal;
            int x = 0, y = 0, row = 0, widest = 0;
            foreach (Control c in Controls)
            {
                if (!Shown(c)) continue;
                var size = c.AutoSize ? c.GetPreferredSize(Size.Empty) : c.Size;
                int w = size.Width + c.Margin.Horizontal, h = size.Height + c.Margin.Vertical;
                if (x > 0 && x + w > width)
                {
                    y += row;
                    x = row = 0;
                }
                x += w;
                row = Math.Max(row, h);
                widest = Math.Max(widest, x);
            }
            return new Size(widest + Padding.Horizontal, y + row + Padding.Vertical);
        }

        static bool Shown(Control c) => GetState == null ? c.Visible : (bool)GetState.Invoke(c, new object[] { 0x2 });
    }

    /// <summary>
    /// A drop-down list in the theme: a rounded dark field with a chevron, opened by a click anywhere on
    /// it, and a dark list at most half the screen tall. Only the drawing is ours; Windows runs the list.
    /// Values that aren't in the list are shown through <see cref="SetValue"/>.
    /// </summary>
    class Combo : ComboBox
    {
        const int WM_PAINT = 0x0F;
        bool _listThemed;

        /// <summary>The last item, which <see cref="Picker"/> turns into a text box: "Other keys…".</summary>
        public sealed class OtherItem
        {
            public string Label;
            public override string ToString() => Label;
        }

        /// <summary>A value that isn't one of the items (typed keys, an address), shown at the top.</summary>
        sealed class CustomItem
        {
            public string Text;
            public override string ToString() => Text;
        }

        public Combo()
        {
            DropDownStyle = ComboBoxStyle.DropDownList;
            DrawMode = DrawMode.OwnerDrawFixed;
            FlatStyle = FlatStyle.Flat;
            BackColor = Theme.Field;
            ForeColor = Theme.Text;
            Cursor = Theme.Hand;
            // Off from the start: switching it off later (setting DropDownHeight does) recreates the window,
            // which swallowed the first click on every list.
            IntegralHeight = false;
            // No ItemHeight here: setting it creates the window there and then.
            HandleDestroyed += (s, e) =>
            {
                _list.ReleaseHandle();
                _listThemed = false;
            };
        }

        /// <summary>Watches the open list for the pointer leaving it, so the item under it stops looking hovered.</summary>
        sealed class ListWatcher : NativeWindow
        {
            public bool Outside = true;

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == 0x0200 /* WM_MOUSEMOVE */)
                {
                    // The list captures the mouse while open, so moves outside it arrive here too.
                    int x = (short)(m.LParam.ToInt64() & 0xFFFF), y = (short)((m.LParam.ToInt64() >> 16) & 0xFFFF);
                    GetClientRect(Handle, out var r);
                    bool outside = x < 0 || y < 0 || x >= r.Right || y >= r.Bottom;
                    if (outside != Outside)
                    {
                        Outside = outside;
                        InvalidateRect(Handle, IntPtr.Zero, false);
                    }
                }
                base.WndProc(ref m);
            }
        }

        readonly ListWatcher _list = new ListWatcher();

        /// <summary>Selects the item whose text is <paramref name="text"/>, or shows the text as an extra item.</summary>
        public void SetValue(string text)
        {
            text = text ?? "";
            for (int i = Items.Count - 1; i >= 0; i--)
                if (Items[i] is CustomItem) Items.RemoveAt(i);
            for (int i = 0; i < Items.Count; i++)
            {
                if (Items[i] is OtherItem || GetItemText(Items[i]) != text) continue;
                SelectedIndex = i;
                return;
            }
            if (text.Length == 0)
            {
                SelectedIndex = -1;
                return;
            }
            Items.Insert(0, new CustomItem { Text = text });
            SelectedIndex = 0;
        }

        /// <summary>The value shown through <see cref="SetValue"/> when it isn't an item, else "".</summary>
        public string Custom => Items.Count > 0 && Items[0] is CustomItem c ? c.Text : "";

        int FieldHeight => Font.Height + (int)Theme.Px(this, 18);
        int TextInset => (int)Theme.Px(this, 14);
        int ChevronWidth => (int)Theme.Px(this, 40);

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            if (IsHandleCreated) ItemHeight = FieldHeight;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ItemHeight = FieldHeight;
        }

        protected override void OnEnter(EventArgs e) { Invalidate(); base.OnEnter(e); }
        protected override void OnLeave(EventArgs e) { Invalidate(); base.OnLeave(e); }
        protected override void OnSelectedIndexChanged(EventArgs e) { Invalidate(); base.OnSelectedIndexChanged(e); }
        protected override void OnForeColorChanged(EventArgs e) { Invalidate(); base.OnForeColorChanged(e); }

        protected override void OnDropDownClosed(EventArgs e)
        {
            Invalidate();
            base.OnDropDownClosed(e);
        }

        protected override void OnDropDown(EventArgs e)
        {
            // At most half the screen tall (Windows would otherwise use most of a TV's height).
            int half = Screen.FromControl(this).WorkingArea.Height / 2;
            DropDownHeight = Math.Max(ItemHeight + 2, Math.Min(Items.Count * ItemHeight + 2, half));
            Invalidate();
            if (!_listThemed && Info(out var info))
            {
                // Dark scrollbar in the list.
                try { SetWindowTheme(info.hwndList, "DarkMode_Explorer", null); } catch (Exception) { }
                _list.AssignHandle(info.hwndList);
                _listThemed = true;
            }
            _list.Outside = true; // nothing looks hovered until the pointer is over the list
            base.OnDropDown(e);
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            bool hot = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0 && !_list.Outside;
            using (var brush = new SolidBrush(hot ? Theme.SurfaceHover : Theme.Field)) e.Graphics.FillRectangle(brush, e.Bounds);
            var item = Items[e.Index];
            var color = item is OtherItem ? Theme.Accent : e.Index == SelectedIndex ? Theme.Accent : Theme.Text;
            var text = new Rectangle(e.Bounds.X + TextInset, e.Bounds.Y, e.Bounds.Width - TextInset, e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, GetItemText(item), item is OtherItem ? Theme.Strong : Font, text, color,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            _list.Outside = false; // moving through the list with the keyboard shows where you are
            base.OnKeyDown(e);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x020A /* WM_MOUSEWHEEL */ && !DroppedDown)
            {
                // The wheel scrolls the page, never changes the choice under the pointer.
                var page = Parent;
                while (page != null && !(page is ScrollableControl sc && sc.AutoScroll)) page = page.Parent;
                if (page != null) SendMessage(page.Handle, m.Msg, m.WParam, m.LParam);
                m.Result = IntPtr.Zero;
                return;
            }
            if (m.Msg != WM_PAINT)
            {
                base.WndProc(ref m);
                return;
            }
            // The closed field is all ours: border, background, chosen item and chevron.
            var ps = new PAINTSTRUCT();
            var hdc = BeginPaint(Handle, ref ps);
            try
            {
                using (var buffer = BufferedGraphicsManager.Current.Allocate(hdc, ClientRectangle))
                {
                    PaintField(buffer.Graphics);
                    buffer.Render();
                }
            }
            finally
            {
                EndPaint(Handle, ref ps);
            }
        }

        void PaintField(Graphics g)
        {
            g.Clear(Theme.Behind(this));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            // Teal only while the list is open; a quiet border when it has the keyboard focus.
            var border = DroppedDown ? Theme.Accent : ContainsFocus ? Theme.Muted : Theme.Border;
            using (var path = Theme.Rounded(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Theme.Px(this, 10)))
            {
                using (var brush = new SolidBrush(Theme.Field)) g.FillPath(brush, path);
                using (var pen = new Pen(border, DroppedDown ? Math.Max(1f, Theme.Px(this, 1.5f)) : 1f)) g.DrawPath(pen, path);
            }

            float s = Theme.Px(this, 6), cx = Width - ChevronWidth / 2f, cy = Height / 2f;
            float tip = DroppedDown ? -s / 2 : s / 2;
            using (var pen = new Pen(Enabled ? Theme.Text : Theme.Border, Math.Max(1.5f, Theme.Px(this, 2f))) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                g.DrawLines(pen, new[] { new PointF(cx - s, cy - tip), new PointF(cx, cy + tip), new PointF(cx + s, cy - tip) });

            TextRenderer.DrawText(g, Text, Font, new Rectangle(TextInset, 0, Width - TextInset - ChevronWidth, Height), Enabled ? ForeColor : Theme.Muted,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        bool Info(out COMBOBOXINFO info)
        {
            info = new COMBOBOXINFO { cbSize = Marshal.SizeOf(typeof(COMBOBOXINFO)) };
            return IsHandleCreated && GetComboBoxInfo(Handle, ref info);
        }

        [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        struct COMBOBOXINFO
        {
            public int cbSize;
            public RECT rcItem, rcButton;
            public int stateButton;
            public IntPtr hwndCombo, hwndItem, hwndList;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct PAINTSTRUCT
        {
            public IntPtr hdc;
            public bool fErase;
            public RECT rcPaint;
            public bool fRestore, fIncUpdate;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] rgbReserved;
        }

        [DllImport("user32.dll")] static extern bool GetComboBoxInfo(IntPtr hwnd, ref COMBOBOXINFO info);
        [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr hwnd, out RECT rect);
        [DllImport("user32.dll")] static extern bool InvalidateRect(IntPtr hwnd, IntPtr rect, bool erase);
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] static extern IntPtr BeginPaint(IntPtr hwnd, ref PAINTSTRUCT ps);
        [DllImport("user32.dll")] static extern bool EndPaint(IntPtr hwnd, ref PAINTSTRUCT ps);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);
    }

    /// <summary>
    /// A <see cref="Combo"/> whose last item ("Other keys…", "Type an IP address…") swaps it for a text box
    /// in the same place. Enter or leaving the box keeps the text; Esc goes back to the list.
    /// </summary>
    class Picker : Panel
    {
        public readonly Combo List = new Combo { Dock = DockStyle.Fill, Margin = new Padding(0) };
        readonly FieldBox _typing = new FieldBox(100) { Dock = DockStyle.Fill, Visible = false, Margin = new Padding(0) };
        readonly Combo.OtherItem _other;

        /// <summary>Raised with the chosen item's text, or the typed text.</summary>
        public event Action<string> Chosen;

        public Picker(int width, string otherLabel)
        {
            Width = width;
            Margin = new Padding(0, 3, 8, 3);
            _other = new Combo.OtherItem { Label = otherLabel };
            Controls.Add(_typing);
            Controls.Add(List);
            List.Items.Add(_other);
            Height = List.Height;
            List.SizeChanged += (s, e) => Height = List.Height; // follows the font, e.g. on a screen with other scaling
            List.SelectionChangeCommitted += (s, e) =>
            {
                if (List.SelectedItem == _other) StartTyping();
                else Chosen?.Invoke(List.Text);
            };
            List.SelectedIndexChanged += (s, e) => { if (List.SelectedItem != _other) _shown = List.Text; };
            _typing.Box.KeyDown += (s, e) =>
            {
                if (e.KeyCode != Keys.Enter && e.KeyCode != Keys.Escape) return;
                e.SuppressKeyPress = true;
                StopTyping(e.KeyCode == Keys.Enter);
            };
            _typing.Box.Leave += (s, e) => StopTyping(true);
        }

        /// <summary>The value shown: an item's text, typed text, or "".</summary>
        public string Value
        {
            get => List.SelectedItem == _other ? "" : List.Text;
            set => List.SetValue(value);
        }

        /// <summary>Puts <paramref name="items"/> in the list, before the "Other…" item, keeping the value.</summary>
        public void SetItems(IEnumerable<object> items)
        {
            var value = Value;
            List.BeginUpdate();
            List.Items.Clear();
            foreach (var item in items) List.Items.Add(item);
            List.Items.Add(_other);
            List.EndUpdate();
            Value = value;
        }

        public void ShowError(bool error) => List.ForeColor = error ? Theme.Danger : Theme.Text;

        string _before, _shown = "";

        void StartTyping()
        {
            _before = _shown;
            _typing.Box.Text = List.Custom;
            _typing.Height = List.Height;
            List.Visible = false;
            _typing.Visible = true;
            _typing.Box.Focus();
            _typing.Box.SelectAll();
        }

        void StopTyping(bool keep)
        {
            if (!_typing.Visible) return;
            var text = _typing.Box.Text.Trim();
            _typing.Visible = false;
            List.Visible = true;
            if (keep && text.Length > 0)
            {
                List.SetValue(text);
                Chosen?.Invoke(text);
            }
            else
            {
                List.SetValue(_before);
            }
        }

    }
}
