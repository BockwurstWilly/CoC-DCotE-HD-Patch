using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CoCHD
{
    /// <summary>
    /// Innsmouth, 1922: ink-black and sea-green panels, parchment text, typewriter accents.
    /// Shared by launcher and setup.
    /// </summary>
    static class Theme
    {
        public static readonly Color Back = Color.FromArgb(11, 14, 13);
        public static readonly Color Panel = Color.FromArgb(21, 26, 24);
        public static readonly Color PanelLight = Color.FromArgb(33, 40, 37);
        public static readonly Color Border = Color.FromArgb(70, 84, 72);
        public static readonly Color Text = Color.FromArgb(224, 210, 178);
        public static readonly Color TextDim = Color.FromArgb(142, 134, 112);
        public static readonly Color Accent = Color.FromArgb(38, 84, 70);
        public static readonly Color AccentDark = Color.FromArgb(16, 40, 33);
        public static readonly Color AccentLight = Color.FromArgb(112, 176, 146);
        public static readonly Color Brass = Color.FromArgb(186, 150, 88);
        public static readonly Color Good = Color.FromArgb(128, 184, 120);
        public static readonly Color Warn = Color.FromArgb(214, 160, 60);

        public const string Credits = "Made by BockwurstWilly using Claude";

        public static readonly Font Body = new Font("Segoe UI", 9.5f);
        public static readonly Font Small = new Font("Segoe UI", 8.5f);
        public static readonly Font Bold = new Font("Segoe UI Semibold", 9.5f);
        public static readonly Font Type = new Font("Courier New", 9f, FontStyle.Bold);

        /// <summary>Serif display face for titles and navigation (the period look of the game's journal).</summary>
        public static Font Display(float size, FontStyle style = FontStyle.Bold)
        {
            foreach (var name in new[] { "Palatino Linotype", "Book Antiqua", "Georgia" })
            {
                using (var probe = new Font(name, size, style))
                    if (probe.Name == name)
                        return new Font(name, size, style);
            }
            return new Font(FontFamily.GenericSerif, size, style);
        }

        public static Label Label(string text, Font font = null, Color? color = null)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                Font = font ?? Body,
                ForeColor = color ?? Text,
                BackColor = Color.Transparent,
                UseMnemonic = false,
            };
        }

        public static ComboBox Combo(int width)
        {
            var cb = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                BackColor = PanelLight,
                ForeColor = Text,
                Font = Body,
                Width = width,
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = Body.Height + 6,
            };
            // with visual styles a DropDownList ignores BackColor and stays white: draw the items ourselves
            cb.DrawItem += (o, e) =>
            {
                bool face = (e.State & DrawItemState.ComboBoxEdit) != 0;
                bool sel = (e.State & DrawItemState.Selected) != 0 && !face;
                using (var b = new SolidBrush(sel ? Accent : PanelLight))
                    e.Graphics.FillRectangle(b, e.Bounds);
                if (e.Index >= 0)
                    TextRenderer.DrawText(e.Graphics, cb.Items[e.Index].ToString(), cb.Font,
                        new Rectangle(e.Bounds.X + 4, e.Bounds.Y, e.Bounds.Width - 4, e.Bounds.Height),
                        cb.Enabled ? Text : TextDim, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            };
            return cb;
        }

        /// <summary>Screen DPI / 96: layouts are written for 96 DPI and scaled once after building.</summary>
        public static readonly float Scale = GetScale();

        static float GetScale()
        {
            using (var g = Graphics.FromHwnd(IntPtr.Zero))
                return g.DpiX / 96f;
        }

        /// <summary>A size measured on screen (fonts are already DPI-sized) in 96-DPI layout units.</summary>
        public static int Unscaled(int px) { return (int)Math.Ceiling(px / Scale); }

        /// <summary>A 96-DPI layout value in screen pixels, for painting.</summary>
        public static int S(float v) { return (int)Math.Round(v * Scale); }

        public static CheckBox Check(string text)
        {
            return new RetroCheck { Text = text, Font = Body, ForeColor = Text, BackColor = Panel };
        }

        public static RetroButton Button(string text, bool primary = false)
        {
            return new RetroButton { Text = text, Primary = primary, Font = primary ? Display(10.5f) : Body };
        }

        /// <summary>
        /// Layouts are written in 96-DPI pixels. Fonts (in points) already follow the system DPI, so only
        /// bounds need scaling; done once, explicitly, after the form is built.
        /// </summary>
        public static void ScaleToDpi(Form form)
        {
            if (Math.Abs(Scale - 1f) > 0.005f)
                form.Scale(new SizeF(Scale, Scale));
        }

        public static Label CreditsLabel(Point location)
        {
            var l = Label(Credits, Small, TextDim);
            l.Location = location;
            return l;
        }

        /// <summary>A thin double rule with a small diamond in the middle - the journal's section divider.</summary>
        public static void Rule(Graphics g, int x0, int x1, int y)
        {
            using (var p = new Pen(Color.FromArgb(120, Brass)))
            {
                int mid = (x0 + x1) / 2, d = S(4);
                g.DrawLine(p, x0, y, mid - 2 * d, y);
                g.DrawLine(p, mid + 2 * d, y, x1, y);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.DrawPolygon(p, new[] { new Point(mid, y - d), new Point(mid + d, y), new Point(mid, y + d), new Point(mid - d, y) });
                g.SmoothingMode = SmoothingMode.Default;
            }
        }
    }

    /// <summary>Flat button with a brass-edged bevel, drawn by hand so it looks the same everywhere.</summary>
    class RetroButton : Control
    {
        bool hover, down;
        public bool Primary;

        public RetroButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.SupportsTransparentBackColor | ControlStyles.Selectable, true);
            Size = new Size(120, 32);
            ForeColor = Theme.Text;
            Cursor = Cursors.Hand;
            TabStop = true;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space)
                OnClick(EventArgs.Empty);
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            Color top, bottom;
            if (!Enabled)
            {
                top = bottom = Theme.Panel;
            }
            else if (Primary)
            {
                top = down ? Theme.AccentDark : hover ? Color.FromArgb(54, 112, 94) : Theme.Accent;
                bottom = Theme.AccentDark;
            }
            else
            {
                top = hover ? Theme.PanelLight : Theme.Panel;
                bottom = Theme.Back;
            }
            using (var b = new LinearGradientBrush(r, top, bottom, LinearGradientMode.Vertical))
                g.FillRectangle(b, r);
            using (var p = new Pen(Focused || hover ? Theme.Brass : Primary ? Color.FromArgb(150, Theme.Brass) : Theme.Border))
                g.DrawRectangle(p, r);
            if (Primary && Enabled)
                using (var p = new Pen(Color.FromArgb(40, 255, 255, 255)))
                    g.DrawLine(p, 1, 1, Width - 2, 1);
            TextRenderer.DrawText(g, Text, Font, r, Enabled ? Theme.Text : Theme.TextDim,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    /// <summary>Image scaled with high-quality bicubic filtering (PictureBox scales crudely).</summary>
    class ImageBox : Control
    {
        readonly Image image;

        public ImageBox(Image image)
        {
            this.image = image;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            using (var b = new SolidBrush(BackColor))
                g.FillRectangle(b, ClientRectangle);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            float scale = Math.Min((float)Width / image.Width, (float)Height / image.Height);
            float w = image.Width * scale, h = image.Height * scale;
            g.DrawImage(image, (Width - w) / 2, (Height - h) / 2, w, h);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                image.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>Check box drawn in the theme colours.</summary>
    class RetroCheck : CheckBox
    {
        public RetroCheck()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            AutoSize = false;
            Cursor = Cursors.Hand;
        }

        int Box { get { return Font.Height; } }

        protected override void OnParentChanged(EventArgs e) { if (Parent != null) BackColor = Parent.BackColor; base.OnParentChanged(e); }

        // the size follows the (already DPI-sized) font; DPI scaling must only move the box
        protected override void ScaleControl(SizeF factor, BoundsSpecified specified)
        {
            base.ScaleControl(factor, specified & BoundsSpecified.Location);
        }

        protected override void OnTextChanged(EventArgs e) { FitSize(); base.OnTextChanged(e); }
        protected override void OnFontChanged(EventArgs e) { FitSize(); base.OnFontChanged(e); }

        void FitSize()
        {
            var t = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPrefix);
            Size = new Size(Box + 8 + t.Width + 4, Math.Max(Box, t.Height) + 4);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            using (var bg = new SolidBrush(BackColor))
                g.FillRectangle(bg, ClientRectangle);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int y = (Height - Box) / 2;
            var r = new Rectangle(1, y, Box - 2, Box - 2);
            using (var b = new SolidBrush(Checked ? Theme.Accent : Theme.PanelLight))
                g.FillRectangle(b, r);
            using (var p = new Pen(Focused ? Theme.Brass : Theme.Border))
                g.DrawRectangle(p, r);
            if (Checked)
            {
                using (var p = new Pen(Theme.Text, Math.Max(2f, Box / 7f)))
                {
                    p.StartCap = p.EndCap = LineCap.Round;
                    g.DrawLines(p, new[]
                    {
                        new PointF(r.Left + r.Width * 0.22f, r.Top + r.Height * 0.52f),
                        new PointF(r.Left + r.Width * 0.42f, r.Top + r.Height * 0.74f),
                        new PointF(r.Left + r.Width * 0.80f, r.Top + r.Height * 0.28f),
                    });
                }
            }
            TextRenderer.DrawText(g, Text, Font, new Point(Box + 8, (Height - TextRenderer.MeasureText(Text, Font).Height) / 2),
                Enabled ? ForeColor : Theme.TextDim, TextFormatFlags.NoPrefix);
        }
    }

    /// <summary>Horizontal value slider in the theme colours (the stock TrackBar ignores dark colours).</summary>
    class RetroSlider : Control
    {
        int value, minimum, maximum = 100;
        bool dragging;
        public event EventHandler ValueChanged;

        public RetroSlider()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.SupportsTransparentBackColor | ControlStyles.Selectable, true);
            Size = new Size(260, 26);
            BackColor = Theme.Panel;
            TabStop = true;
        }

        public int Minimum { get { return minimum; } set { minimum = value; Value = this.value; } }
        public int Maximum { get { return maximum; } set { maximum = value; Value = this.value; } }

        public int Value
        {
            get { return value; }
            set
            {
                int v = Math.Max(minimum, Math.Min(maximum, value));
                if (v == this.value)
                    return;
                this.value = v;
                Invalidate();
                if (ValueChanged != null)
                    ValueChanged(this, EventArgs.Empty);
            }
        }

        protected override void OnParentChanged(EventArgs e) { if (Parent != null) BackColor = Parent.BackColor; base.OnParentChanged(e); }

        int Track0 { get { return 8; } }
        int Track1 { get { return Width - 9; } }

        void SetFromX(int x)
        {
            float t = (float)(x - Track0) / (Track1 - Track0);
            Value = minimum + (int)Math.Round(t * (maximum - minimum));
        }

        protected override void OnMouseDown(MouseEventArgs e) { Focus(); dragging = true; SetFromX(e.X); base.OnMouseDown(e); }
        protected override void OnMouseMove(MouseEventArgs e) { if (dragging) SetFromX(e.X); base.OnMouseMove(e); }
        protected override void OnMouseUp(MouseEventArgs e) { dragging = false; base.OnMouseUp(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override bool IsInputKey(Keys k)
        {
            return k == Keys.Left || k == Keys.Right || base.IsInputKey(k);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Left) Value--;
            if (e.KeyCode == Keys.Right) Value++;
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            using (var bg = new SolidBrush(BackColor))
                g.FillRectangle(bg, ClientRectangle);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int cy = Height / 2;
            float t = maximum == minimum ? 0 : (float)(value - minimum) / (maximum - minimum);
            int x = Track0 + (int)(t * (Track1 - Track0));
            using (var p = new Pen(Theme.Border, 4))
                g.DrawLine(p, Track0, cy, Track1, cy);
            using (var p = new Pen(Theme.AccentLight, 4))
                g.DrawLine(p, Track0, cy, x, cy);
            var knob = new Rectangle(x - 7, cy - 9, 14, 18);
            using (var b = new LinearGradientBrush(knob, Theme.Brass, Color.FromArgb(110, 86, 48), LinearGradientMode.Vertical))
                g.FillRectangle(b, knob);
            using (var p = new Pen(Focused ? Theme.Text : Theme.Back))
                g.DrawRectangle(p, knob);
        }
    }
}
