using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace IotLinkLab
{
    /// <summary>统一圆角按钮(与上位机 RoundedButton 同一设计语言)。</summary>
    public class RoundedButton : Control
    {
        private bool _hover, _pressed;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int Radius { get; set; } = Theme.RadiusButton;
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color FillColor { get; set; } = Theme.Primary;
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color TextColor { get; set; } = Color.White;
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool Outline { get; set; }

        public RoundedButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.UserPaint | ControlStyles.ResizeRedraw
                   | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            Font = Theme.Ui(9F, bold: true);
            Size = new Size(96, Theme.HeightButton);
        }

        private Color CurrentFill()
        {
            if (!Enabled) return Outline ? Color.FromArgb(60, Theme.TextTertiary) : Color.FromArgb(130, FillColor);
            if (_pressed) return Outline ? Color.FromArgb(40, FillColor) : ControlPaint.Dark(FillColor, 0.08f);
            if (_hover) return Outline ? Color.FromArgb(24, FillColor) : ControlPaint.Light(FillColor, 0.12f);
            return Outline ? Color.Transparent : FillColor;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using var path = CreateRoundedPath(rect, Radius);
            var fill = CurrentFill();
            if (fill.A > 0) using (var b = new SolidBrush(fill)) g.FillPath(b, path);
            if (Outline)
            {
                using var pen = new Pen(Enabled ? FillColor : Color.FromArgb(110, Theme.TextTertiary), 1.6f);
                g.DrawPath(pen, path);
            }
            var text = !Enabled ? Color.FromArgb(150, Theme.TextTertiary) : TextColor;
            TextRenderer.DrawText(g, Text, Font, rect, text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = _pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { _pressed = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

        internal static GraphicsPath CreateRoundedPath(Rectangle rect, int radius)
        {
            int d = Math.Max(1, radius * 2);
            if (d > rect.Width) d = rect.Width;
            if (d > rect.Height) d = rect.Height;
            var path = new GraphicsPath();
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseAllFigures();
            return path;
        }
    }

    /// <summary>白底圆角卡片容器。</summary>
    public class Card : Panel
    {
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int Radius { get; set; } = Theme.RadiusCard;
        public Card()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.CardBg;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using var path = RoundedButton.CreateRoundedPath(rect, Radius);
            using (var b = new SolidBrush(BackColor)) g.FillPath(b, path);
            using (var pen = new Pen(Theme.CardStroke, 1)) g.DrawPath(pen, path);
            base.OnPaint(e);
        }
    }

    /// <summary>连接状态徽章(圆点 + 文本胶囊)。</summary>
    public class StatusChip : Control
    {
        private string _text = "未连接";
        private Color _dot = Theme.TextTertiary;

        public StatusChip()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Font = Theme.Ui(9F, bold: true);
            Size = new Size(110, 26);
        }

        public void SetState(string text, Color dot) { _text = text; _dot = dot; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = RoundedButton.CreateRoundedPath(rect, Height / 2))
            {
                using (var b = new SolidBrush(Color.FromArgb(18, _dot)))
                    g.FillPath(b, path);
                using (var pen = new Pen(Color.FromArgb(40, _dot), 1))
                    g.DrawPath(pen, path);
            }
            g.FillEllipse(new SolidBrush(_dot), 12, Height / 2 - 4, 8, 8);
            TextRenderer.DrawText(g, _text, Font, new Rectangle(24, 0, Width - 26, Height), Theme.TextPrimary,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
    }
}
