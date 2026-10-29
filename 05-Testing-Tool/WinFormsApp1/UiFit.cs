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

        /// <summary>把「标签 + 输入框」做成一行, 供流式/网格布局复用。</summary>
        public static Label FieldLabel(string text, float size = 9F) => new()
        {
            Text = text, AutoSize = true, Font = Theme.Ui(size),
            ForeColor = Theme.TextSecondary, Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 0, 6, 0)
        };

        public static Label SectionTitle(string text) => new()
        {
            Text = text, AutoSize = true, Font = Theme.Ui(9.5F, bold: true),
            ForeColor = Theme.TextSecondary, Margin = new Padding(0, 0, 0, 6)
        };
    }
}
