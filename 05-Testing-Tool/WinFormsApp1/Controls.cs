using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace IotLinkLab
{
    /// <summary>
    /// 自绘资源池: 复用 SolidBrush / Pen。
    /// 原实现每个 OnPaint 都 new SolidBrush + new Pen(含 StatusChip 里连 new 都不 Dispose 的圆点刷),
    /// 拖动窗口时每秒数百次重绘 → GDI 对象猛烈抖动并持续泄漏, 是掉帧的直接来源之一。
    /// </summary>
    internal static class PaintPool
    {
        private const int MaxEntries = 512;

        private static readonly Dictionary<int, SolidBrush> BrushPool = new();
        private static readonly Dictionary<long, Pen> PenPool = new();

        /// <summary>焦点虚线圈专用画笔(带 DashStyle, 不可与其他画笔共享)。</summary>
        public static readonly Pen FocusPen = CreateFocusPen();

        private static Pen CreateFocusPen()
        {
            var p = new Pen(Color.FromArgb(150, Theme.Primary), 2f) { DashStyle = DashStyle.Dot };
            return p;
        }

        public static SolidBrush Brush(Color color)
        {
            int key = color.ToArgb();
            if (BrushPool.TryGetValue(key, out var brush)) return brush;
            if (BrushPool.Count >= MaxEntries) ClearBrushes();
            brush = new SolidBrush(color);
            BrushPool[key] = brush;
            return brush;
        }

        public static Pen Pen(Color color, float width)
        {
            long key = ((long)color.ToArgb() << 8) | (long)Math.Round(width * 10f);
            if (PenPool.TryGetValue(key, out var pen)) return pen;
            if (PenPool.Count >= MaxEntries) ClearPens();
            pen = new Pen(color, width);
            PenPool[key] = pen;
            return pen;
        }

        private static void ClearBrushes()
        {
            foreach (var b in BrushPool.Values) b.Dispose();
            BrushPool.Clear();
        }

        private static void ClearPens()
        {
            foreach (var p in PenPool.Values) p.Dispose();
            PenPool.Clear();
        }
    }

    /// <summary>
    /// 圆角路径缓存: 控件尺寸未变时复用同一 GraphicsPath。
    /// 拖动窗口时 Resize 频率极高, 每次重算 4 段圆弧既耗 CPU 又持续分配非托管 GDI+ 句柄。
    /// </summary>
    internal sealed class PathCache
    {
        private GraphicsPath? _path;
        private int _w = -1, _h = -1, _r = -1;

        public GraphicsPath Get(int width, int height, int radius)
        {
            if (_path != null && _w == width && _h == height && _r == radius) return _path;
            _path?.Dispose();
            _path = RoundedButton.CreateRoundedPath(new Rectangle(0, 0, width, height), radius);
            _w = width; _h = height; _r = radius;
            return _path;
        }
    }

    /// <summary>统一圆角按钮(与上位机 RoundedButton 同一设计语言)。</summary>
    public class RoundedButton : Control
    {
        private bool _hover, _pressed;
        private readonly PathCache _paths = new();

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int Radius { get; set; } = Theme.RadiusButton;
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color FillColor { get; set; } = Theme.Primary;
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color TextColor { get; set; } = Theme.OnPrimary;
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool Outline { get; set; }

        public RoundedButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.UserPaint | ControlStyles.ResizeRedraw
                   | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            Font = Theme.Ui(9.5F, bold: true);
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
            var path = _paths.Get(Math.Max(1, rect.Width), Math.Max(1, rect.Height), Radius);

            var fill = CurrentFill();
            if (fill.A > 0) g.FillPath(PaintPool.Brush(fill), path);
            if (Outline)
            {
                var outlineColor = Enabled ? FillColor : Color.FromArgb(110, Theme.TextTertiary);
                g.DrawPath(PaintPool.Pen(outlineColor, 1.6f), path);
            }
            if (Focused && ShowFocusCues)
            {
                var focusPath = RoundedButton.CreateRoundedPath(
                    new Rectangle(2, 2, Width - 5, Height - 5), Math.Max(2, Radius - 2));
                using (focusPath) g.DrawPath(PaintPool.FocusPen, focusPath);
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
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

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

    /// <summary>白底圆角卡片容器。Region 与圆角路径一致, 避免四角露出方形底色。</summary>
    public class Card : Panel
    {
        private readonly PathCache _paths = new();
        private int _regionW = -1, _regionH = -1, _regionR = -1;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int Radius { get; set; } = Theme.RadiusCard;

        public Card()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.CardBg;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UpdateRegion();
        }

        /// <summary>
        /// 只在尺寸/圆角真变化时重建 Region。
        /// Region 赋值会走 SetWindowRgn, 是 GDI 侧的重操作; 原实现连一次等尺寸的重复 Resize
        /// 也照样重建并 Dispose 一次 Region, 拖拽窗口时每个 Card 每帧都要付这笔钱。
        /// </summary>
        private void UpdateRegion()
        {
            if (Width <= 0 || Height <= 0) return;
            if (Width == _regionW && Height == _regionH && Radius == _regionR) return;
            try
            {
                using var path = RoundedButton.CreateRoundedPath(new Rectangle(0, 0, Width, Height), Radius);
                var old = Region;
                Region = new Region(path);
                old?.Dispose();
                _regionW = Width; _regionH = Height; _regionR = Radius;
            }
            catch { /* 极端尺寸下保持方形, 不影响布局 */ }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            var path = _paths.Get(Math.Max(1, rect.Width), Math.Max(1, rect.Height), Radius);
            g.FillPath(PaintPool.Brush(BackColor), path);
            g.DrawPath(PaintPool.Pen(Theme.CardStroke, 1f), path);
            base.OnPaint(e);
        }
    }

    /// <summary>
    /// 圆角输入容器: 统一承载 TextBox / ComboBox / NumericUpDown / RichTextBox。
    /// 内部控件去掉系统边框并居中, 焦点时容器描边转品牌绿, 解决“直角输入框嵌在圆角卡里”的割裂感。
    /// </summary>
    public class FieldShell : Panel
    {
        private readonly Control _inner;
        private readonly bool _multiline;
        private readonly PathCache _paths = new();
        private bool _hover;
        private int _innerHeight = -1;
        private bool _laidOutOnce;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int Radius { get; set; } = Theme.RadiusInput;

        public Control Inner => _inner;

        public FieldShell(Control inner, bool multiline = false, int height = 0)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.UserPaint | ControlStyles.ResizeRedraw
                   | ControlStyles.SupportsTransparentBackColor, true);
            _inner = inner;
            _multiline = multiline;
            BackColor = Color.Transparent;
            Padding = new Padding(0);
            Height = height > 0 ? height : UiFit.S(Theme.HeightInput);
            Margin = new Padding(0);

            if (inner is TextBox tb)
            {
                tb.BorderStyle = BorderStyle.None;
                tb.BackColor = Theme.FieldBg;
            }
            if (inner is RichTextBox rtb)
            {
                rtb.BorderStyle = BorderStyle.None;
                rtb.BackColor = Theme.FieldBg;
            }
            if (inner is ComboBox cb)
            {
                cb.FlatStyle = FlatStyle.Flat;
                cb.BackColor = Theme.FieldBg;
            }
            if (inner is NumericUpDown nu)
            {
                nu.BorderStyle = BorderStyle.None;
                nu.BackColor = Theme.FieldBg;
            }
            if (inner is ListView lv)
            {
                lv.BorderStyle = BorderStyle.None;
                lv.BackColor = Theme.FieldBg;
            }

            Controls.Add(inner);
            if (multiline)
            {
                Padding = new Padding(UiFit.S(8), UiFit.S(6), UiFit.S(8), UiFit.S(6));
                inner.Dock = DockStyle.Fill;
            }

            inner.Enter += (s, e) => Invalidate();
            inner.Leave += (s, e) => Invalidate();
            inner.GotFocus += (s, e) => Invalidate();
            inner.LostFocus += (s, e) => Invalidate();

            MouseEnter += (s, e) => { _hover = true; Invalidate(); };
            MouseLeave += (s, e) => { _hover = false; Invalidate(); };
            MouseDown += (s, e) => { _inner.Focus(); };
            Cursor = Cursors.IBeam;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutInner();
        }

        // 原实现同时 override OnLayout 也调 LayoutInner(), 一次尺寸变化要跑两遍完整测量 + SetBounds,
        // 并在 SetBounds 里再次触发 OnLayout → 自我递归放大。这里只在首次布局时补一次兜底。
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (!_laidOutOnce) LayoutInner();
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            _innerHeight = -1;   // 字体变化后内部控件理想高度可能改变, 失效缓存
        }

        private void LayoutInner()
        {
            if (_inner == null || _inner.IsDisposed) return;
            if (_multiline) { _laidOutOnce = true; return; }   // Dock=Fill 已由 Padding 控制内缩
            if (ClientSize.Width <= 0 || ClientSize.Height <= 0) return;

            int padX = UiFit.S(8);
            int w = Math.Max(0, ClientSize.Width - padX * 2);
            if (_innerHeight <= 0) _innerHeight = _inner.PreferredSize.Height;   // 结果缓存, 不再每次布局测量
            int h = _innerHeight;
            if (h <= 0 || h > ClientSize.Height) h = Math.Min(_inner.Height, ClientSize.Height);
            h = Math.Max(1, h);
            int y = Math.Max(0, (ClientSize.Height - h) / 2);
            if (_inner.Left == padX && _inner.Top == y && _inner.Width == w && _inner.Height == h)
            {
                _laidOutOnce = true;
                return;   // 已经就位就不再 SetBounds, 避免无谓的重绘风暴
            }
            _inner.SetBounds(padX, y, w, h);
            _laidOutOnce = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            var path = _paths.Get(Math.Max(1, rect.Width), Math.Max(1, rect.Height), Radius);
            g.FillPath(PaintPool.Brush(Theme.FieldBg), path);
            Color stroke = _inner.Focused
                ? Theme.Primary
                : _hover ? Color.FromArgb(150, Theme.Primary) : Theme.CardStroke;
            g.DrawPath(PaintPool.Pen(stroke, _inner.Focused ? 1.6f : 1f), path);
            base.OnPaint(e);
        }
    }

    /// <summary>连接状态徽章(圆点 + 文本胶囊)。宽度按文字实测自适应, 圆点由控件绘制, 文本不得自带 "● " 前缀。</summary>
    public class StatusChip : Control
    {
        private string _text = "未连接";
        private Color _dot = Theme.TextTertiary;
        private readonly PathCache _paths = new();
        private string _measuredText = "";
        private Font? _measuredFont;
        private int _measuredWidth = -1;

        public StatusChip()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Font = Theme.Ui(9.5F, bold: true);
            ResizeText();
        }

        public void SetState(string text, Color dot)
        {
            _text = text.TrimStart('●', ' ').Trim();   // 兼容旧调用: 圆点统一由控件绘制
            _dot = dot;
            ResizeText();
            Invalidate();
        }

        private void ResizeText()
        {
            if (_measuredWidth < 0 || _measuredFont != Font || _measuredText != _text)
            {
                _measuredWidth = TextRenderer.MeasureText(_text, Font).Width;   // 文字/字体不变则不重测
                _measuredText = _text;
                _measuredFont = Font;
            }
            // 12 左留白 + 8 圆点 + 8 间距 + 文本 + 14 右留白
            var size = new Size(12 + 8 + 8 + _measuredWidth + 14, Math.Max(26, Font.Height + 8));
            if (Size != size) Size = size;
        }

        protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); _measuredWidth = -1; ResizeText(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            var path = _paths.Get(Math.Max(1, rect.Width), Math.Max(1, rect.Height), Height / 2);
            g.FillPath(PaintPool.Brush(Color.FromArgb(18, _dot)), path);
            g.DrawPath(PaintPool.Pen(Color.FromArgb(40, _dot), 1f), path);

            int dotY = Height / 2 - 4;
            // 原代码此处 new SolidBrush(_dot) 后从不 Dispose —— 每次重绘泄漏一个 GDI 画刷。
            g.FillEllipse(PaintPool.Brush(_dot), 12, dotY, 8, 8);
            TextRenderer.DrawText(g, _text, Font, new Rectangle(28, 0, Width - 30, Height), Theme.TextPrimary,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
    }

    /// <summary>
    /// 导航胶囊(单绘制源): 继承 Control 而非 Label, 避免基类默认文本绘制与
    /// OnPaint 自绘文本叠加造成重影; 宽度按文字实测自适应(DPI 安全)。
    /// </summary>
    public class NavChip : Control
    {
        private bool _selected;
        private readonly PathCache _paths = new();
        private string _measuredText = "";
        private Font? _measuredFont;
        private int _measuredWidth = -1;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool Selected
        {
            get { return _selected; }
            set
            {
                if (_selected == value) return;   // 同值不再触发重绘(SwitchTab 里本就调了 Invalidate)
                _selected = value;
                Invalidate();
            }
        }

        public NavChip()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.UserPaint | ControlStyles.ResizeRedraw
                   | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            Font = Theme.Ui(9.5F, bold: true);
            ResizeForText();
        }

        private void ResizeForText()
        {
            if (_measuredWidth < 0 || _measuredFont != Font || _measuredText != Text)
            {
                _measuredWidth = TextRenderer.MeasureText(Text, Font).Width;
                _measuredText = Text;
                _measuredFont = Font;
            }
            var size = new Size(_measuredWidth + 36, Math.Max(30, Font.Height + 8));
            if (Size == size) return;   // 尺寸未变 → 不写 Size, 不触发父级重新布局
            Size = size;
            Invalidate();
        }

        protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); _measuredWidth = -1; ResizeForText(); }
        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); ResizeForText(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            var path = _paths.Get(Math.Max(1, rect.Width), Math.Max(1, rect.Height), Height / 2);
            g.FillPath(PaintPool.Brush(_selected ? Color.FromArgb(20, Theme.Primary) : Theme.CardBg), path);
            g.DrawPath(PaintPool.Pen(_selected ? Theme.Primary : Theme.CardStroke, 1.4f), path);
            TextRenderer.DrawText(g, Text, Font, rect,
                _selected ? Theme.Primary : Theme.TextSecondary,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
    }
}
