using System;
using System.Drawing;

namespace DHT11
{
    /// <summary>
    /// 全局设计令牌(单一真源): 温室绿 + 暖木 + 奶油白浅色系(Organic Biophilic)。
    /// 所有颜色/尺寸常量只在此定义, 控件一律消费本类, 避免散落的魔法数。
    /// 间距/尺寸按 8px 网格; 圆角: 窗口级卡片 12px / 按钮 8px / 徽章胶囊 999px。
    /// </summary>
    internal static class AppTheme
    {
        // ---------- 品牌色 ----------
        public static readonly Color Primary = Color.FromArgb(21, 128, 61);      // 土绿 #15803D
        public static readonly Color PrimaryDark = Color.FromArgb(14, 89, 44);    // 按压态
        public static readonly Color PrimarySoft = Color.FromArgb(240, 253, 244); // 薄荷白底 #F0FDF4
        public static readonly Color AccentGold = Color.FromArgb(161, 98, 7);     // 丰收金 #A16207(点缀)

        // ---------- 语义色 ----------
        public static readonly Color Success = Color.FromArgb(21, 128, 61);
        public static readonly Color Warning = Color.FromArgb(180, 121, 9);
        public static readonly Color Danger = Color.FromArgb(200, 51, 44);
        public static readonly Color Info = Color.FromArgb(47, 111, 158);

        // ---------- 中性色 ----------
        public static readonly Color TextPrimary = Color.FromArgb(20, 60, 38);    // 深绿炭黑 #143C26
        public static readonly Color TextSecondary = Color.FromArgb(93, 111, 100);
        public static readonly Color TextTertiary = Color.FromArgb(122, 140, 129);
        public static readonly Color PageBg = Color.FromArgb(243, 248, 244);     // 奶油绿白
        public static readonly Color CardBg = Color.White;
        public static readonly Color CardStroke = Color.FromArgb(226, 235, 229);
        public static readonly Color Divider = Color.FromArgb(236, 242, 238);

        // ---------- 布局尺度(8px 网格) ----------
        public const int RadiusCard = 12;      // 卡片圆角
        public const int RadiusButton = 8;     // 按钮圆角
        public const int RadiusInput = 8;      // 输入框圆角
        public const int HeightButton = 32;   // 统一按钮高度
        public const int HeightInput = 34;    // 统一输入框高度
        public const int GapBlock = 12;      // 区块间距
        public const int GapInner = 8;       // 组件内间距
        public const int PadCard = 16;      // 卡片内边距

        /// <summary>按钮/标签字体(微软雅黑保持与全应用一致)。</summary>
        public static Font FontUi(float size, bool bold = false)
        {
            return new Font("微软雅黑", size, bold ? FontStyle.Bold : FontStyle.Regular);
        }

        /// <summary>数值读数字体(等宽数字更整齐)。</summary>
        public static Font FontNumeric(float size)
        {
            return new Font("Consolas", size, FontStyle.Bold);
        }

        /// <summary>在底色上派生浅色 tint(用于卡片浅底/悬停态)。</summary>
        public static Color Tint(Color baseColor, int alpha)
        {
            return Color.FromArgb(alpha, baseColor);
        }
    }
}
