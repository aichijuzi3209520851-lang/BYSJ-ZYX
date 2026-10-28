using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DHT11
{
    /// <summary>
    /// 统一圆角按钮: GDI+ 抗锯齿绘制, 圆角 8px, 统一高度 32px。
    /// 修复旧版 FlatStyle.Flat 直角按钮"颜色艳/直角/大小不一"的观感问题。
    /// 悬停/按压态在控件内自绘, 不依赖系统按钮 chrome; 禁用态整体降透明。
    /// </summary>
    public class RoundedButton : Control
    {
        private bool _hover;
        private bool _pressed;

        public int Radius { get; set; } = AppTheme.RadiusButton;
        public Color FillColor { get; set; } = AppTheme.Primary;
        public Color TextColor { get; set; } = Color.White;
        /// <summary>空心样式: 透明底 + 主题色描边与文字(次级操作)。</summary>
        public bool Outline { get; set; }

        public RoundedButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.UserPaint
                   | ControlStyles.ResizeRedraw
                   | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            Font = AppTheme.FontUi(9F, bold: true);
            Size = new Size(92, AppTheme.HeightButton);
        }

        private Color CurrentFill()
        {
            if (!Enabled)
                return Outline ? Color.FromArgb(70, AppTheme.TextTertiary) : Color.FromArgb(140, FillColor);
            if (_pressed)
                return Outline ? AppTheme.Tint(FillColor, 40) : ControlPaint.Dark(FillColor, 0.08f);
            if (_hover)
                return Outline ? AppTheme.Tint(FillColor, 28) : ControlPaint.Light(FillColor, 0.12f);
            return Outline ? Color.Transparent : FillColor;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = CreateRoundedPath(rect, Radius))
            {
                Color fill = CurrentFill();
                if (fill.A > 0)
                {
                    using (SolidBrush b = new SolidBrush(fill))
                        g.FillPath(b, path);
                }

                using (Pen pen = new Pen(
                    Outline && Enabled ? FillColor : (Outline ? Color.FromArgb(120, AppTheme.TextTertiary) : fill),
                    Outline ? 1.6f : 0f))
                {
                    if (Outline)
                        g.DrawPath(pen, path);
                }

                // 顶缘 1px 内受光线(玻璃质感, α<1 避免白纸感)
                if (!Outline && Enabled && !_pressed)
                {
                    using (Pen rim = new Pen(Color.FromArgb(52, Color.White), 1))
                        g.DrawLine(rim, rect.Left + Radius, rect.Top + 1, rect.Right - Radius, rect.Top + 1);
                }
            }

            Color text = !Enabled ? Color.FromArgb(150, AppTheme.TextTertiary)
                       : _pressed && !Outline ? Color.FromArgb(235, TextColor)
                       : TextColor;
            TextRenderer.DrawText(g, Text, Font, rect, text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { _pressed = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

        internal static GraphicsPath CreateRoundedPath(Rectangle rect, int radius)
        {
            int d = Math.Max(1, radius * 2);
            if (d > rect.Width) d = rect.Width;
            if (d > rect.Height) d = rect.Height;
            GraphicsPath path = new GraphicsPath();
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseAllFigures();
            return path;
        }
    }

    /// <summary>
    /// 分段控件(自动/手动模式切换): 玻璃轨道 + tint 玻璃选中态,
    /// 对应小程序端 mode-seg 的 P15 规范(tint 而非实色块)。
    /// </summary>
    public class SegmentedControl : Control
    {
        private int _selectedIndex;

        public string[] Items { get; set; } = { "自动模式", "手动模式" };
        public int SelectedIndex
        {
            get { return _selectedIndex; }
            set { _selectedIndex = Math.Max(0, Math.Min(Items.Length - 1, value)); Invalidate(); }
        }

        public event EventHandler SelectedChanged;

        public SegmentedControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.UserPaint
                   | ControlStyles.ResizeRedraw, true);
            BackColor = AppTheme.PrimarySoft;
            Cursor = Cursors.Hand;
            Font = AppTheme.FontUi(9F);
            Size = new Size(190, AppTheme.HeightButton + 8);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle full = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath track = RoundedButton.CreateRoundedPath(full, Height / 2))
            using (SolidBrush trackBrush = new SolidBrush(Color.FromArgb(210, 233, 242, 235)))
            {
                g.FillPath(trackBrush, track);
            }

            int segWidth = Width / Items.Length;
            Rectangle selRect = new Rectangle(_selectedIndex * segWidth + 3, 3, segWidth - 6, Height - 7);
            using (GraphicsPath sel = RoundedButton.CreateRoundedPath(selRect, selRect.Height / 2))
            using (SolidBrush selBrush = new SolidBrush(Color.White))
            {
                g.FillPath(selBrush, sel);
                using (Pen rim = new Pen(Color.FromArgb(150, 21, 128, 61), 1.4f))
                    g.DrawPath(rim, sel);
            }

            for (int i = 0; i < Items.Length; i++)
            {
                Rectangle r = new Rectangle(i * segWidth, 0, segWidth, Height);
                bool on = i == _selectedIndex;
                using (Font f = AppTheme.FontUi(9F, on))
                {
                    TextRenderer.DrawText(g, Items[i], f, r,
                        on ? AppTheme.Primary : AppTheme.TextSecondary,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                }
            }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left) return;
            int idx = Math.Min(Items.Length - 1, Math.Max(0, e.X * Items.Length / Width));
            if (idx != _selectedIndex)
            {
                SelectedIndex = idx;
                SelectedChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }
}
