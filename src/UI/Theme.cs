using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Lakhfana
{
    // Visual language for every window: colours, fonts, DPI helper, logo and small custom-drawn controls.
    //
    // RTL note: forms use RightToLeft + RightToLeftLayout, so child Locations are measured from the
    // RIGHT edge ("logical x"). Double-buffered custom controls below paint in PHYSICAL coordinates
    // (x = 0 is the physical left), so they handle Arabic alignment themselves.
    public static class Theme
    {
        public static readonly Color Bg = Color.FromArgb(243, 244, 248);
        public static readonly Color Card = Color.White;
        public static readonly Color Border = Color.FromArgb(225, 228, 235);
        public static readonly Color BorderStrong = Color.FromArgb(196, 201, 212);
        public static readonly Color Divider = Color.FromArgb(236, 238, 243);
        public static readonly Color Text = Color.FromArgb(27, 29, 36);
        public static readonly Color TextMuted = Color.FromArgb(98, 104, 119);
        public static readonly Color TextFaint = Color.FromArgb(146, 151, 165);
        public static readonly Color Accent = Color.FromArgb(79, 70, 229);
        public static readonly Color AccentHover = Color.FromArgb(67, 56, 202);
        public static readonly Color AccentPressed = Color.FromArgb(55, 48, 163);
        public static readonly Color AccentSoft = Color.FromArgb(240, 241, 255);
        public static readonly Color AccentSoftBorder = Color.FromArgb(196, 199, 251);
        public static readonly Color Danger = Color.FromArgb(196, 43, 28);
        public static readonly Color Success = Color.FromArgb(15, 123, 69);
        public static readonly Color KeyFill = Color.FromArgb(246, 247, 250);
        public static readonly Color KeyBorder = Color.FromArgb(208, 212, 222);
        public static readonly Color LogoStart = Color.FromArgb(110, 92, 250);
        public static readonly Color LogoEnd = Color.FromArgb(28, 176, 214);

        public static readonly Font Body = MakeFont("Segoe UI", 10f, FontStyle.Regular);
        public static readonly Font BodyStrong = MakeFont("Segoe UI Semibold", 10f, FontStyle.Regular);
        public static readonly Font Title = MakeFont("Segoe UI Semibold", 16f, FontStyle.Regular);
        public static readonly Font Heading = MakeFont("Segoe UI Semibold", 11f, FontStyle.Regular);
        public static readonly Font Small = MakeFont("Segoe UI", 9f, FontStyle.Regular);
        public static readonly Font KeyFont = MakeFont("Segoe UI Semibold", 9.5f, FontStyle.Regular);
        public static readonly Font Mono = MakeFont("Consolas", 10f, FontStyle.Regular);

        static float scale = -1f;
        static Icon appIcon;

        static Font MakeFont(string family, float size, FontStyle style)
        {
            try
            {
                Font f = new Font(family, size, style, GraphicsUnit.Point);
                if (string.Equals(f.Name, family, StringComparison.OrdinalIgnoreCase)) return f;
                f.Dispose();
            }
            catch { }
            return new Font(FontFamily.GenericSansSerif, size, style == FontStyle.Regular && family.EndsWith("Semibold") ? FontStyle.Bold : style, GraphicsUnit.Point);
        }

        // DPI scale factor of the (system-DPI-aware) process: 1.0 at 96 DPI.
        public static float Scale
        {
            get
            {
                if (scale < 0f)
                {
                    float v = 1f;
                    try { using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) v = g.DpiX / 96f; }
                    catch { }
                    scale = v < 1f ? 1f : v;
                }
                return scale;
            }
        }

        public static int S(int px) { return (int)Math.Round(px * Scale); }
        public static float Sf(float px) { return px * Scale; }

        public static bool HasArabic(string s)
        {
            if (s == null) return false;
            foreach (char c in s) if (c >= '؀' && c <= 'ۿ') return true;
            return false;
        }

        public static GraphicsPath RoundRect(RectangleF r, float radius)
        {
            GraphicsPath p = new GraphicsPath();
            float d = Math.Min(radius * 2f, Math.Min(r.Width, r.Height));
            if (d < 1f) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        // RTL placement: physical x of a control that starts `startX` px from the container's RIGHT edge.
        // (Only a form's direct children are mirrored by RightToLeftLayout; nested containers are not,
        // so every window puts its content in one full-size root panel and places children with this.)
        public static int RtlX(int containerWidth, int startX, int width)
        {
            return containerWidth - startX - width;
        }

        public static Panel MakeRoot(Form f)
        {
            Panel root = new Panel();
            root.Dock = DockStyle.Fill;
            root.BackColor = Bg;
            root.Margin = Padding.Empty;
            f.Controls.Add(root);
            return root;
        }

        public static Label MakeLabel(string text, Font font, Color fore, Color back)
        {
            Label l = new Label();
            l.AutoSize = true;
            l.UseMnemonic = false;
            l.Text = text;
            l.Font = font;
            l.ForeColor = fore;
            l.BackColor = back;
            l.Margin = Padding.Empty;
            l.Padding = Padding.Empty;
            return l;
        }

        // Common window chrome: RTL, fonts, colours, no auto-scaling (we scale with S()).
        public static void PrepareForm(Form f)
        {
            f.AutoScaleMode = AutoScaleMode.None;
            f.Font = Body;
            f.BackColor = Bg;
            f.ForeColor = Text;
            f.RightToLeft = RightToLeft.Yes;
            f.RightToLeftLayout = true;
            f.FormBorderStyle = FormBorderStyle.FixedSingle;
            f.MaximizeBox = false;
            f.MinimizeBox = false;
            f.StartPosition = FormStartPosition.CenterScreen;
            try { f.Icon = AppIcon; } catch { }
            f.HandleCreated += delegate { StyleCaption(f.Handle); };
        }

        // Windows 11: paint the title bar in the window background colour (ignored on Windows 10).
        static void StyleCaption(IntPtr hwnd)
        {
            try
            {
                int color = Bg.R | (Bg.G << 8) | (Bg.B << 16);
                ThemeNative.DwmSetWindowAttribute(hwnd, 35 /* DWMWA_CAPTION_COLOR */, ref color, 4);
            }
            catch { }
        }

        public static Icon AppIcon
        {
            get
            {
                if (appIcon == null)
                {
                    byte[] ico = BuildIco(new int[] { 16, 20, 24, 32, 40, 48, 64 });
                    appIcon = new Icon(new MemoryStream(ico));
                }
                return appIcon;
            }
        }

        static byte[] BuildIco(int[] sizes)
        {
            List<byte[]> images = new List<byte[]>();
            foreach (int s in sizes)
            {
                using (Bitmap b = RenderLogo(s))
                using (MemoryStream ms = new MemoryStream())
                {
                    b.Save(ms, ImageFormat.Png);
                    images.Add(ms.ToArray());
                }
            }
            using (MemoryStream o = new MemoryStream())
            using (BinaryWriter w = new BinaryWriter(o))
            {
                w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    byte dim = (byte)(sizes[i] >= 256 ? 0 : sizes[i]);
                    w.Write(dim); w.Write(dim); w.Write((byte)0); w.Write((byte)0);
                    w.Write((short)1); w.Write((short)32);
                    w.Write(images[i].Length); w.Write(offset);
                    offset += images[i].Length;
                }
                foreach (byte[] img in images) w.Write(img);
                w.Flush();
                return o.ToArray();
            }
        }

        // The app logo at an exact pixel size (transparent margin included). Used by tools/make-icon.ps1 too.
        public static Bitmap RenderLogo(int size)
        {
            Bitmap b = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(b))
            {
                g.Clear(Color.Transparent);
                float m = size >= 64 ? size * 0.0625f : (size >= 32 ? 1f : 0f);
                DrawLogo(g, new RectangleF(m, m, size - 2 * m, size - 2 * m));
            }
            return b;
        }

        // Rounded square with a gradient; "A" and "ع" with circular swap arrows (simplified when small).
        public static void DrawLogo(Graphics g, RectangleF r)
        {
            GraphicsState st = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;

            float s = Math.Min(r.Width, r.Height);
            RectangleF sq = new RectangleF(r.X + (r.Width - s) / 2f, r.Y + (r.Height - s) / 2f, s, s);
            using (GraphicsPath body = RoundRect(sq, s * 0.24f))
            {
                using (LinearGradientBrush br = new LinearGradientBrush(new PointF(sq.Left, sq.Top), new PointF(sq.Right, sq.Bottom), LogoStart, LogoEnd))
                    g.FillPath(br, body);
                // soft top sheen fading out by mid-height
                RectangleF top = new RectangleF(sq.X, sq.Y - 1f, s, s * 0.6f + 2f);
                using (LinearGradientBrush sheen = new LinearGradientBrush(top, Color.FromArgb(46, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f))
                {
                    sheen.WrapMode = WrapMode.TileFlipXY;
                    g.SetClip(body, CombineMode.Intersect);
                    g.FillRectangle(sheen, sq.X, sq.Y, s, s * 0.6f - 1f);
                    g.ResetClip();
                }
            }

            Color ink = Color.White;
            if (s < 28f)
            {
                // ⇄ two opposing arrows, pixel-snapped
                float w = Math.Max(2f, (float)Math.Round(s * 0.1f));
                float y1 = sq.Y + (float)Math.Round(s * 0.36f);
                float y2 = sq.Y + (float)Math.Round(s * 0.66f);
                if (((int)w) % 2 == 1) { y1 += 0.5f; y2 += 0.5f; }
                float x1 = sq.X + s * 0.2f, x2 = sq.X + s * 0.8f;
                float head = s * 0.24f, half = s * 0.17f;
                DrawArrow(g, new PointF(x1, y1), new PointF(x2, y1), w, head, half, ink);
                DrawArrow(g, new PointF(x2, y2), new PointF(x1, y2), w, head, half, ink);
            }
            else if (s < 36f)
            {
                // 32 px: the arcs would blur into blobs; bigger letters on the diagonal instead
                using (SolidBrush b = new SolidBrush(ink))
                {
                    DrawGlyph(g, "A", "Segoe UI", FontStyle.Bold, new PointF(sq.X + s * 0.32f, sq.Y + s * 0.33f), s * 0.38f, b);
                    DrawGlyph(g, "ع", "Arial", FontStyle.Bold, new PointF(sq.X + s * 0.67f, sq.Y + s * 0.64f), s * 0.46f, b);
                }
            }
            else
            {
                float cx = sq.X + s / 2f, cy = sq.Y + s / 2f;
                using (SolidBrush b = new SolidBrush(ink))
                {
                    DrawGlyph(g, "A", "Segoe UI", FontStyle.Bold, new PointF(sq.X + s * 0.335f, sq.Y + s * 0.355f), s * 0.29f, b);
                    DrawGlyph(g, "ع", "Arial", FontStyle.Bold, new PointF(sq.X + s * 0.655f, sq.Y + s * 0.63f), s * 0.37f, b);
                }
                bool mid = s < 48f;
                float rad = s * 0.335f, pw = mid ? Math.Max(2f, s * 0.065f) : s * 0.052f;
                float head = mid ? s * 0.16f : s * 0.13f;
                Color arc = Color.FromArgb(235, 255, 255, 255);
                DrawArcArrow(g, cx, cy, rad, 282f, 58f, pw, head, arc);
                DrawArcArrow(g, cx, cy, rad, 102f, 58f, pw, head, arc);
            }
            g.Restore(st);
        }

        static void DrawGlyph(Graphics g, string text, string family, FontStyle style, PointF center, float height, Brush b)
        {
            using (GraphicsPath gp = new GraphicsPath())
            {
                FontFamily ff;
                try { ff = new FontFamily(family); }
                catch { ff = new FontFamily("Segoe UI"); }
                using (ff)
                    gp.AddString(text, ff, (int)(ff.IsStyleAvailable(style) ? style : FontStyle.Regular), 100f, PointF.Empty, StringFormat.GenericTypographic);
                RectangleF bb = gp.GetBounds();
                if (bb.Height <= 0) return;
                float k = height / bb.Height;
                using (Matrix mx = new Matrix())
                {
                    mx.Translate(center.X, center.Y);
                    mx.Scale(k, k);
                    mx.Translate(-(bb.X + bb.Width / 2f), -(bb.Y + bb.Height / 2f));
                    gp.Transform(mx);
                }
                g.FillPath(b, gp);
            }
        }

        // Straight arrow from a to b; the head (length/half-width) sits at b.
        public static void DrawArrow(Graphics g, PointF a, PointF b, float width, float headLen, float headHalf, Color c)
        {
            float dx = b.X - a.X, dy = b.Y - a.Y;
            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len < 0.01f) return;
            float ux = dx / len, uy = dy / len;
            PointF baseP = new PointF(b.X - ux * headLen, b.Y - uy * headLen);
            using (Pen p = new Pen(c, width))
            {
                p.StartCap = LineCap.Round;
                p.EndCap = LineCap.Flat;
                g.DrawLine(p, a, new PointF(baseP.X + ux * 0.5f, baseP.Y + uy * 0.5f));
            }
            PointF[] tri = new PointF[] {
                b,
                new PointF(baseP.X - uy * headHalf, baseP.Y + ux * headHalf),
                new PointF(baseP.X + uy * headHalf, baseP.Y - ux * headHalf) };
            using (SolidBrush br = new SolidBrush(c)) g.FillPolygon(br, tri);
        }

        static void DrawArcArrow(Graphics g, float cx, float cy, float rad, float start, float sweep, float width, float head, Color c)
        {
            // stop the stroke a little early; the head covers the rest
            float headDeg = (float)(head / rad * 180.0 / Math.PI);
            using (Pen p = new Pen(c, width))
            {
                p.StartCap = LineCap.Round;
                g.DrawArc(p, cx - rad, cy - rad, rad * 2, rad * 2, start, sweep - headDeg * 0.6f);
            }
            double end = (start + sweep) * Math.PI / 180.0;
            double baseA = (start + sweep - headDeg) * Math.PI / 180.0;
            PointF tip = new PointF(cx + (float)(Math.Cos(end) * rad), cy + (float)(Math.Sin(end) * rad));
            float bx = cx + (float)(Math.Cos(baseA) * rad), by = cy + (float)(Math.Sin(baseA) * rad);
            float nx = (float)Math.Cos(baseA), ny = (float)Math.Sin(baseA);
            float half = head * 0.62f;
            PointF[] tri = new PointF[] { tip, new PointF(bx + nx * half, by + ny * half), new PointF(bx - nx * half, by - ny * half) };
            using (SolidBrush br = new SolidBrush(c)) g.FillPolygon(br, tri);
        }

        // ⇄ glyph inside a box (top arrow points left, bottom points right).
        public static void DrawSwap(Graphics g, RectangleF r, Color c)
        {
            float w = Math.Max(1.5f, Sf(1.6f));
            float y1 = r.Y + r.Height * 0.36f, y2 = r.Y + r.Height * 0.64f;
            float head = Sf(5f), half = Sf(3.6f);
            DrawArrow(g, new PointF(r.Right - Sf(2), y1), new PointF(r.X + Sf(2), y1), w, head, half, c);
            DrawArrow(g, new PointF(r.X + Sf(2), y2), new PointF(r.Right - Sf(2), y2), w, head, half, c);
        }

        static class ThemeNative
        {
            [DllImport("dwmapi.dll")]
            public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        }
    }

    // White rounded card with a hairline border and a faint shadow.
    public sealed class UiCard : Panel
    {
        public UiCard()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Card;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float rad = Theme.Sf(8f);
            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 2f);
            using (GraphicsPath sh = Theme.RoundRect(new RectangleF(r.X, r.Y + 1f, r.Width, r.Height), rad))
            using (Pen sp = new Pen(Color.FromArgb(14, 0, 0, 0)))
                g.DrawPath(sp, sh);
            using (GraphicsPath p = Theme.RoundRect(r, rad))
            {
                using (SolidBrush b = new SolidBrush(Theme.Card)) g.FillPath(b, p);
                using (Pen pen = new Pen(Theme.Border)) g.DrawPath(pen, p);
            }
        }
    }

    // Draws the app logo.
    public sealed class UiLogo : Control
    {
        public UiLogo()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
            Theme.DrawLogo(e.Graphics, new RectangleF(0, 0, Width, Height));
        }
    }

    // Flat rounded button. Primary = accent fill; secondary = white with a border.
    public sealed class UiButton : Control, IButtonControl
    {
        readonly bool primary;
        bool hover, pressed, isDefault;
        DialogResult dialogResult;

        public UiButton(string text, bool primary)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            SetStyle(ControlStyles.StandardDoubleClick, false);
            this.primary = primary;
            Text = text;
            Font = Theme.BodyStrong;
            Cursor = Cursors.Hand;
            TabStop = true;
            AccessibleRole = AccessibleRole.PushButton;
            AccessibleName = text;
            Size sz = TextRenderer.MeasureText(text, Font);
            Size = new Size(Math.Max(Theme.S(108), sz.Width + Theme.S(44)), Theme.S(38));
        }

        public DialogResult DialogResult { get { return dialogResult; } set { dialogResult = value; } }
        public void NotifyDefault(bool value) { isDefault = value; Invalidate(); }
        public void PerformClick() { if (Enabled && Visible) OnClick(EventArgs.Empty); }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { pressed = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { pressed = false; Invalidate(); base.OnLostFocus(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnTextChanged(EventArgs e) { AccessibleName = Text; Invalidate(); base.OnTextChanged(e); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space) { pressed = true; Invalidate(); e.Handled = true; }
            base.OnKeyDown(e);
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space && pressed) { pressed = false; Invalidate(); PerformClick(); e.Handled = true; }
            base.OnKeyUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color fill, border, fore;
            if (!Enabled)
            {
                fill = Color.FromArgb(232, 233, 238); border = fill; fore = Theme.TextFaint;
            }
            else if (primary)
            {
                fill = pressed ? Theme.AccentPressed : (hover ? Theme.AccentHover : Theme.Accent);
                border = fill; fore = Color.White;
            }
            else
            {
                fill = pressed ? Color.FromArgb(234, 236, 241) : (hover ? Color.FromArgb(246, 247, 250) : Color.White);
                border = Theme.BorderStrong; fore = Theme.Text;
            }
            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            float rad = Theme.Sf(6f);
            using (GraphicsPath p = Theme.RoundRect(r, rad))
            {
                using (SolidBrush b = new SolidBrush(fill)) g.FillPath(b, p);
                using (Pen pen = new Pen(border)) g.DrawPath(pen, p);
            }
            if (Focused && ShowFocusCues)
            {
                float k = Theme.Sf(2.5f);
                using (GraphicsPath p = Theme.RoundRect(new RectangleF(k, k, Width - 1f - 2 * k, Height - 1f - 2 * k), rad - k / 2f))
                using (Pen pen = new Pen(primary ? Color.FromArgb(220, 255, 255, 255) : Theme.Accent, Theme.Sf(1.5f)))
                    g.DrawPath(pen, p);
            }
            TextFormatFlags f = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
            if (Theme.HasArabic(Text)) f |= TextFormatFlags.RightToLeft;
            TextRenderer.DrawText(g, Text, Font, new Rectangle(0, 0, Width, Height - 1), fore, f);
        }
    }

    // Windows 11 style on/off switch. In RTL "on" puts the knob on the left.
    public sealed class UiToggle : Control
    {
        bool isChecked, hover;
        public event EventHandler CheckedChanged;

        public UiToggle()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            SetStyle(ControlStyles.StandardDoubleClick, false);
            Size = new Size(Theme.S(48), Theme.S(28));
            Cursor = Cursors.Hand;
            TabStop = true;
            AccessibleRole = AccessibleRole.CheckButton;
            AccessibleDescription = "مقفول";
        }

        public bool Checked
        {
            get { return isChecked; }
            set
            {
                if (isChecked == value) return;
                isChecked = value;
                Invalidate();
                AccessibleDescription = value ? "شغّال" : "مقفول";
                if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
            }
        }

        protected override void OnClick(EventArgs e) { Focus(); Checked = !Checked; base.OnClick(e); }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space) { Checked = !Checked; e.Handled = true; }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Card);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float tw = Theme.Sf(40f), th = Theme.Sf(20f);
            RectangleF track = new RectangleF((Width - tw) / 2f, (Height - th) / 2f, tw, th);
            using (GraphicsPath p = Theme.RoundRect(track, th / 2f))
            {
                if (isChecked)
                {
                    using (SolidBrush b = new SolidBrush(hover ? Theme.AccentHover : Theme.Accent)) g.FillPath(b, p);
                }
                else
                {
                    using (SolidBrush b = new SolidBrush(hover ? Color.FromArgb(243, 244, 247) : Color.White)) g.FillPath(b, p);
                    using (Pen pen = new Pen(Theme.TextMuted, Theme.Sf(1f))) g.DrawPath(pen, p);
                }
            }
            float kd = Theme.Sf(hover ? 14f : 12f);
            float inset = (th - kd) / 2f + Theme.Sf(hover ? 1f : 2f);
            // RTL: on → physical left, off → physical right
            float kx = isChecked ? track.X + inset : track.Right - inset - kd;
            RectangleF knob = new RectangleF(kx, track.Y + (th - kd) / 2f, kd, kd);
            using (SolidBrush b = new SolidBrush(isChecked ? Color.White : Theme.TextMuted)) g.FillEllipse(b, knob);
            if (Focused && ShowFocusCues)
            {
                float k = Theme.Sf(3f);
                RectangleF fr = RectangleF.Inflate(track, k, k);
                using (GraphicsPath p = Theme.RoundRect(fr, fr.Height / 2f))
                using (Pen pen = new Pen(Theme.Text, Theme.Sf(1.5f))) g.DrawPath(pen, p);
            }
        }

    }

    // Small inline status line: coloured icon + text (Arabic, right-aligned).
    public sealed class UiStatus : Control
    {
        public const int Hint = 0, Ok = 1, Error = 2;
        int kind;

        public UiStatus()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            Font = Theme.Small;
            Height = Theme.S(22);
            AccessibleRole = AccessibleRole.StaticText;
        }

        public int Kind { get { return kind; } }

        public void Set(int kind, string text)
        {
            this.kind = kind;
            Text = text ?? "";
            AccessibleName = Text;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Card);
            if (string.IsNullOrEmpty(Text)) return;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color c = kind == Ok ? Theme.Success : (kind == Error ? Theme.Danger : Theme.TextMuted);
            float d = Theme.Sf(14f);
            RectangleF ic = new RectangleF(Width - d - Theme.Sf(1f), (Height - d) / 2f, d, d);
            if (kind == Hint)
            {
                using (Pen p = new Pen(c, Theme.Sf(1.2f))) g.DrawEllipse(p, ic);
                using (SolidBrush b = new SolidBrush(c))
                {
                    float w = Theme.Sf(1.6f);
                    g.FillRectangle(b, ic.X + d / 2f - w / 2f, ic.Y + d * 0.42f, w, d * 0.36f);
                    g.FillEllipse(b, ic.X + d / 2f - w * 0.6f, ic.Y + d * 0.2f, w * 1.2f, w * 1.2f);
                }
            }
            else
            {
                using (SolidBrush b = new SolidBrush(c)) g.FillEllipse(b, ic);
                using (Pen p = new Pen(Color.White, Theme.Sf(1.6f)))
                {
                    p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                    if (kind == Ok)
                        g.DrawLines(p, new PointF[] {
                            new PointF(ic.X + d * 0.28f, ic.Y + d * 0.52f),
                            new PointF(ic.X + d * 0.44f, ic.Y + d * 0.68f),
                            new PointF(ic.X + d * 0.73f, ic.Y + d * 0.36f) });
                    else
                    {
                        g.DrawLine(p, ic.X + d / 2f, ic.Y + d * 0.27f, ic.X + d / 2f, ic.Y + d * 0.55f);
                        g.DrawLine(p, ic.X + d / 2f, ic.Y + d * 0.74f, ic.X + d / 2f, ic.Y + d * 0.75f);
                    }
                }
            }
            Rectangle tr = new Rectangle(0, 0, (int)(ic.X - Theme.Sf(6f)), Height);
            TextRenderer.DrawText(g, Text, Font, tr, c, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                TextFormatFlags.RightToLeft | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    // A row of chips / words / arrows laid out right-to-left (reading order), e.g. hgsghl ← السلام.
    public sealed class UiChips : Control
    {
        public const int ChipNeutral = 0, ChipAccent = 1, ChipMuted = 2;
        const int KText = 10, KArrow = 11, KSwap = 12;

        sealed class Item { public int Kind; public string Text; }
        readonly List<Item> items = new List<Item>();
        public bool AlignCenter;

        public UiChips()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            Font = Theme.Body;
            Height = Theme.S(32);
            AccessibleRole = AccessibleRole.StaticText;
        }

        public void AddText(string t) { Add(KText, t); }
        public void AddChip(string t, int style) { Add(style, t); }
        public void AddArrow() { Add(KArrow, "←"); }
        public void AddSwap() { Add(KSwap, "⇄"); }

        void Add(int kind, string t)
        {
            Item it = new Item(); it.Kind = kind; it.Text = t; items.Add(it);
            string acc = "";
            foreach (Item i in items) acc += (acc.Length > 0 ? " " : "") + i.Text;
            AccessibleName = acc;
            Invalidate();
        }

        Font FontFor(Item it)
        {
            if (it.Kind == ChipMuted) return Theme.Mono;
            if (it.Kind == ChipAccent) return Theme.BodyStrong;
            if (it.Kind == KText) return Theme.Body;
            return Theme.Body;
        }

        static TextFormatFlags Flags(string t)
        {
            TextFormatFlags f = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix |
                                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter;
            if (Theme.HasArabic(t)) f |= TextFormatFlags.RightToLeft;
            return f;
        }

        int ItemWidth(Item it)
        {
            if (it.Kind == KArrow) return Theme.S(30);
            if (it.Kind == KSwap) return Theme.S(34);
            int w = TextRenderer.MeasureText(it.Text, FontFor(it), new Size(int.MaxValue, int.MaxValue), Flags(it.Text)).Width;
            return it.Kind == KText ? w + Theme.S(8) : w + Theme.S(22);
        }

        public int PreferredWidth
        {
            get { int w = 0; foreach (Item it in items) w += ItemWidth(it); return w + Theme.S(2); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Card);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int total = PreferredWidth;
            float x = AlignCenter ? (Width + total) / 2f : Width - Theme.Sf(1f);
            float ch = Math.Min(Height - Theme.Sf(2f), Theme.Sf(30f));
            float cy = (Height - ch) / 2f;
            foreach (Item it in items)
            {
                int w = ItemWidth(it);
                RectangleF r = new RectangleF(x - w, 0, w, Height);
                x -= w;
                if (it.Kind == KArrow)
                {
                    float y = Height / 2f;
                    Theme.DrawArrow(g, new PointF(r.Right - Theme.Sf(8f), y), new PointF(r.X + Theme.Sf(8f), y),
                        Math.Max(1.5f, Theme.Sf(1.6f)), Theme.Sf(5.5f), Theme.Sf(4f), Theme.Accent);
                    continue;
                }
                if (it.Kind == KSwap)
                {
                    float bw = Theme.Sf(18f), bh = Theme.Sf(16f);
                    Theme.DrawSwap(g, new RectangleF(r.X + (r.Width - bw) / 2f, (Height - bh) / 2f, bw, bh), Theme.Accent);
                    continue;
                }
                Color fore = Theme.Text;
                if (it.Kind != KText)
                {
                    RectangleF cr = new RectangleF(r.X + Theme.Sf(2f), cy + 0.5f, r.Width - Theme.Sf(4f), ch - 1f);
                    Color fill = Theme.KeyFill, border = Theme.Border;
                    if (it.Kind == ChipAccent) { fill = Theme.AccentSoft; border = Theme.AccentSoftBorder; fore = Theme.Accent; }
                    if (it.Kind == ChipMuted) { fill = Color.White; border = Theme.BorderStrong; fore = Theme.TextMuted; }
                    using (GraphicsPath p = Theme.RoundRect(cr, Theme.Sf(6f)))
                    {
                        using (SolidBrush b = new SolidBrush(fill)) g.FillPath(b, p);
                        using (Pen pen = new Pen(border)) g.DrawPath(pen, p);
                    }
                }
                else fore = Theme.TextMuted;
                TextRenderer.DrawText(g, it.Text, FontFor(it), Rectangle.Round(r), fore, Flags(it.Text));
            }
        }
    }
}
