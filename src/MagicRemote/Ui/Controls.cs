using System;
using System.Drawing;
using System.Drawing.Drawing2D;
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
            Cursor = Cursors.Hand;
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
            Cursor = Cursors.Hand;
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
            Cursor = Cursors.Hand;
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

        public static LinkLabel Link(string text, Action onClick)
        {
            var link = new LinkLabel
            {
                Text = text,
                AutoSize = true,
                LinkColor = Theme.Accent,
                ActiveLinkColor = Theme.AccentHover,
                VisitedLinkColor = Theme.Accent,
                LinkBehavior = LinkBehavior.HoverUnderline,
                Font = Theme.Strong,
                Margin = new Padding(0, 0, 0, 4),
            };
            link.LinkClicked += (s, e) => onClick();
            return link;
        }

        public static FlowLayoutPanel Row(params Control[] controls)
        {
            // No BackColor: it inherits the card's, which the rounded children paint their corners with.
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Margin = new Padding(0, 2, 0, 4) };
            row.Controls.AddRange(controls);
            return row;
        }

        public static ComboBox Combo(int width, bool editable) => new ComboBox
        {
            Width = width,
            DropDownStyle = editable ? ComboBoxStyle.DropDown : ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.Field,
            ForeColor = Theme.Text,
            Margin = new Padding(0, 3, 8, 3),
        };

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
}
