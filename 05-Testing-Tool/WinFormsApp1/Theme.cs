namespace IotLinkLab
{
    /// <summary>
    /// 设计令牌(单一真源): 浅色科技风 —— 奶油白底 + 青绿主色 + 圆角卡片。
    /// 与主仓 02-PC-UpperComputer 的 AppTheme 保持同一套温室绿语言(#15803D)。
    /// 间距按 8px 网格; 圆角: 卡片 12 / 按钮 8 / 徽章胶囊。
    /// 禁: 全黑/大深色底/纯色大面(用户明确要求浅色科技感)。
    /// </summary>
    internal static class Theme
    {
        // ---------- 品牌色 ----------
        public static readonly Color Primary = Color.FromArgb(21, 128, 61);       // 土绿
        public static readonly Color PrimaryDark = Color.FromArgb(14, 89, 44);
        public static readonly Color PrimaryTint = Color.FromArgb(240, 253, 244); // 薄荷白底 #F0FDF4
        public static readonly Color PrimarySoft = Color.FromArgb(220, 252, 231); // 浅薄荷 #DCFCE7
        public static readonly Color OnPrimary = Color.FromArgb(252, 253, 252);   // 主色上的近白文字
        public static readonly Color Teal = Color.FromArgb(13, 148, 136);        // 链路青(数据方向)
        public static readonly Color Indigo = Color.FromArgb(79, 70, 229);       // 指令靛(下行)

        // ---------- 语义色 ----------
        public static readonly Color Success = Color.FromArgb(21, 128, 61);
        public static readonly Color Warning = Color.FromArgb(180, 121, 9);
        public static readonly Color Danger = Color.FromArgb(200, 51, 44);

        // ---------- 中性色 ----------
        public static readonly Color TextPrimary = Color.FromArgb(23, 42, 58);
        public static readonly Color TextSecondary = Color.FromArgb(84, 104, 120);
        public static readonly Color TextTertiary = Color.FromArgb(122, 140, 154);
        public static readonly Color PageBg = Color.FromArgb(244, 247, 250);      // 冷白
        public static readonly Color CardBg = Color.FromArgb(252, 253, 252);      // 近白, 带轻微品牌倾向
        public static readonly Color CardStroke = Color.FromArgb(226, 232, 236);
        public static readonly Color FieldBg = Color.FromArgb(248, 250, 251);
        public static readonly Color Divider = Color.FromArgb(235, 240, 243);
        public static readonly Color LogInfo = Color.FromArgb(60, 80, 96);
        public static readonly Color LogOk = Color.FromArgb(21, 128, 61);
        public static readonly Color LogWarn = Color.FromArgb(180, 121, 9);
        public static readonly Color LogErr = Color.FromArgb(200, 51, 44);

        // ---------- 布局尺度(96 DPI 逻辑像素) ----------
        public const int RadiusCard = 12;
        public const int RadiusButton = 8;
        public const int RadiusInput = 8;
        public const int HeightButton = 32;
        public const int HeightInput = 32;
        public const int HeightNav = 32;
        public const int HeightSmallButton = 28;
        public const int HeightTopBar = 56;
        public const int HeightTopBarCompact = 52;
        public const int HeightStatusBar = 26;
        public const int HeightStatusBarCompact = 24;
        public const int HeightLogCollapsed = 34;
        public const int HeightLogCollapsedCompact = 32;
        public const int HeightStateBar = 52;
        public const int HeightStateBarCompact = 48;
        public const int GapXs = 4;
        public const int GapSm = 8;
        public const int GapMd = 12;
        public const int GapLg = 16;
        public const int GapXl = 20;
        public const int Pad = 16;

        public static Font Ui(float size, bool bold = false)
            => new Font("微软雅黑", size, bold ? FontStyle.Bold : FontStyle.Regular);

        public static Font Mono(float size, bool bold = false)
            => new Font("Consolas", size, bold ? FontStyle.Bold : FontStyle.Regular);

        // ---------- 字体层级(只保留 5 档) ----------
        public static Font Title() => Ui(14F, bold: true);
        public static Font Section() => Ui(10.5F, bold: true);
        public static Font Body() => Ui(9.5F);
        public static Font Meta() => Ui(8.5F);
    }
}
