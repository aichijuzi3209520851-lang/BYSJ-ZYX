namespace IotLinkLab
{
    /// <summary>
    /// 控件尺寸自适应(DPI 安全): 宽度由「实测文字宽度 + 留白」算出, 而不是拍脑袋写死。
    /// 目的: 杜绝 "WebSocke"(下拉框裁字) 与 "208"(2080 被数字框裁掉) 这类显示错误。
    /// </summary>
    public static class UiFit
    {
        public const TextFormatFlags MeasureFlags =
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;

        // 当前 DPI(未显式设置时取屏幕 DPI), 供行高/边距等“没有控件引用”的场景换算
        private static int _dpi;

        public static void SetDpi(int dpi)
        {
            if (dpi > 0) _dpi = dpi;
        }

        private static int Dpi()
        {
            if (_dpi > 0) return _dpi;
            try
            {
                using var g = Graphics.FromHwnd(IntPtr.Zero);
                _dpi = Math.Max(96, (int)Math.Round(g.DpiX));
            }
            catch { _dpi = 96; }
            return _dpi;
        }

        /// <summary>逻辑像素 → 物理像素(按当前 DPI), 用于高度/边距/图标尺寸。</summary>
        public static int S(int logical) => (int)Math.Round(logical * (Dpi() / 96.0));

        public static int Scale(Control c, int v)
            => (int)Math.Round(v * (c.DeviceDpi / 96.0));

        public static int Scale(Font f, int v) => v;   // 占位重载, 便于语义阅读

        public static int TextWidth(string? text, Font font)
            => string.IsNullOrEmpty(text)
                ? 0
                : TextRenderer.MeasureText(text, font, new Size(int.MaxValue, int.MaxValue), MeasureFlags).Width;

        public static int MaxItemWidth(ComboBox cb)
        {
            int max = 0;
            foreach (var item in cb.Items)
            {
                int w = TextWidth(cb.GetItemText(item), cb.Font);
                if (w > max) max = w;
            }
            return max;
        }

        /// <summary>按钮宽度 = 文字宽 + 左右留白; minWidth 给个下限避免小按钮过窄。</summary>
        public static void SizeButton(Control b, int padTotal = 40, int minWidth = 0)
        {
            int w = TextWidth(b.Text, b.Font) + Scale(b, padTotal);
            if (minWidth > 0) w = Math.Max(w, Scale(b, minWidth));
            b.Width = w;
        }

        /// <summary>下拉框: 取最长选项 + 箭头区, 保证任何一项都完整显示。</summary>
        public static void FitCombo(ComboBox cb, int padTotal = 24)
        {
            int max = MaxItemWidth(cb);
            if (cb.Text.Length > 0) max = Math.Max(max, TextWidth(cb.Text, cb.Font));
            int arrow = SystemInformation.VerticalScrollBarWidth + Scale(cb, 10);
            int w = max + arrow + Scale(cb, padTotal);
            cb.Width = Math.Max(Scale(cb, 54), w);
        }

        /// <summary>数字框: 按最大值的字符宽度 + 上下箭头区, 避免高位被裁。</summary>
        public static void FitNumeric(NumericUpDown n, int padTotal = 12)
        {
            string longest = n.DecimalPlaces > 0
                ? n.Maximum.ToString("0." + new string('0', n.DecimalPlaces))
                : ((long)n.Maximum).ToString();
            int textW = TextWidth(longest, n.Font);
            int arrows = SystemInformation.VerticalScrollBarWidth + Scale(n, 4);
            n.Width = textW + arrows + Scale(n, padTotal);
        }

        /// <summary>单行输入框: 按样本文案实测宽度 + 左右留白。</summary>
        public static void FitTextBox(TextBox t, string sample, int padTotal = 26)
        {
            int textW = TextWidth(sample, t.Font);
            t.Width = Math.Max(Scale(t, 72), textW + Scale(t, 8) + Scale(t, padTotal));
        }

        /// <summary>把控件放在网格单元的正中(同时垂直/水平居中, 不再用手写 Margin.Top 找基线)。</summary>
        public static void Center(Control c) => c.Anchor = AnchorStyles.None;

        /// <summary>把「标签 + 输入框」做成一行, 供流式/网格布局复用。</summary>
        public static Label FieldLabel(string text, float size = 9.5F) => new()
        {
            Text = text, AutoSize = true, Font = Theme.Ui(size),
            ForeColor = Theme.TextSecondary, Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 0, 6, 0)
        };

        public static Label SectionTitle(string text) => new()
        {
            Text = text, AutoSize = true, Font = Theme.Section(),
            ForeColor = Theme.TextPrimary, Margin = new Padding(0, 0, 0, 8)
        };
    }
}
