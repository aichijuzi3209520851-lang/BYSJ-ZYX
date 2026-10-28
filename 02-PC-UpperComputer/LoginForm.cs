using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace DHT11
{
    /// <summary>
    /// 登录窗口: 整窗温室实拍背景 + 居中白色圆角卡片, 体现"智能温室大棚监测系统"产品形象。
    /// 纯代码构建(不建 Designer 文件)。账号口令硬编码为 admin/admin, 与本项目
    /// 「单机演示 + 上位机本地鉴权」的定位匹配; 若将来要接真实鉴权, 只需替换 ValidateCredentials。
    /// </summary>
    public class LoginForm : Form
    {
        private const string DefaultUser = "admin";
        private const string DefaultPassword = "admin";

        private RoundedTextBox userTextBox;
        private RoundedTextBox passwordTextBox;
        private RoundedButton loginButton;
        private Label hintLabel;
        private Image _bgImage;          // 登录页背景(温室实拍), 读入内存后释放文件句柄
        private bool _bgFailed;

        public LoginForm()
        {
            BuildUi();

            // 标题栏左上角小图标 + 任务栏图标。AppIcon.Apply 内部逐级降级且全程吞异常。
            AppIcon.Apply(this);
        }

        private void BuildUi()
        {
            this.SuspendLayout();

            this.Text = "智能温室监测系统 v" + Application.ProductVersion + " - 登录";
            this.ClientSize = new Size(880, 560);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = AppTheme.PageBg;
            this.DoubleBuffered = true;
            this.Paint += LoginForm_Paint;

            _bgImage = LoadBackgroundImage();

            // ---------- 居中登录卡片(圆角 16px + 柔影) ----------
            RoundedCard card = new RoundedCard
            {
                Size = new Size(360, 400),
                BackColor = Color.White
            };
            card.Location = new Point((this.ClientSize.Width - card.Width) / 2 + 120,
                                      (this.ClientSize.Height - card.Height) / 2);

            // 品牌图标圆盘
            PictureBox logoBox = new PictureBox
            {
                Location = new Point(126, 30),
                Size = new Size(72, 72),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent
            };
            Image logo = LoadIconSafely("ICON\\app_icon.ico") ?? LoadIconSafely("ICON\\app_icon_preview.png");
            logoBox.Image = logo ?? MakeLeafFallback();

            Label titleLabel = new Label
            {
                Location = new Point(0, 112),
                Size = new Size(360, 34),
                TextAlign = ContentAlignment.MiddleCenter,
                Text = "智能温室大棚监测系统",
                Font = AppTheme.FontUi(14.5F, bold: true),
                ForeColor = AppTheme.TextPrimary,
                BackColor = Color.Transparent
            };

            Label subTitleLabel = new Label
            {
                Location = new Point(0, 146),
                Size = new Size(360, 22),
                TextAlign = ContentAlignment.MiddleCenter,
                Text = "Smart Greenhouse IoT Monitoring",
                Font = AppTheme.FontUi(8.5F),
                ForeColor = AppTheme.TextTertiary,
                BackColor = Color.Transparent
            };

            userTextBox = MakeField("用户名", 192, DefaultUser, false);
            passwordTextBox = MakeField("密码", 262, "", true);

            hintLabel = new Label
            {
                Location = new Point(0, 306),
                Size = new Size(360, 20),
                TextAlign = ContentAlignment.MiddleCenter,
                Text = "演示环境 · 默认账号 admin / 密码 admin",
                Font = AppTheme.FontUi(8.5F),
                ForeColor = AppTheme.TextTertiary,
                BackColor = Color.Transparent
            };

            loginButton = new RoundedButton
            {
                Text = "登 录",
                Location = new Point(60, 336),
                Size = new Size(240, 40),
                Radius = 10,
                FillColor = AppTheme.Primary,
                Font = AppTheme.FontUi(11F, bold: true)
            };
            loginButton.Click += LoginButton_Click;

            card.Controls.AddRange(new Control[] {
                logoBox, titleLabel, subTitleLabel,
                userTextBox.FieldLabel, userTextBox,
                passwordTextBox.FieldLabel, passwordTextBox,
                hintLabel, loginButton
            });

            this.Controls.Add(card);
            this.KeyPreview = true;
            this.KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) { this.DialogResult = DialogResult.Cancel; this.Close(); } };

            this.ResumeLayout(false);
        }

        /// <summary>带浮动标签的圆角输入框(标签绘制在卡片上, 输入框统一 34px 高; 回车触发登录)。</summary>
        private RoundedTextBox MakeField(string label, int y, string text, bool password)
        {
            Label fieldLabel = new Label
            {
                Location = new Point(60, y - 20),
                AutoSize = true,
                Text = label,
                Font = AppTheme.FontUi(8.5F),
                ForeColor = AppTheme.TextSecondary,
                BackColor = Color.Transparent
            };

            RoundedTextBox box = new RoundedTextBox
            {
                Location = new Point(60, y),
                Size = new Size(240, AppTheme.HeightInput),
                Font = new Font("微软雅黑", 10F),
                Text = text,
                UseSystemPasswordChar = password
            };
            box.InnerKeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    LoginButton_Click(loginButton, EventArgs.Empty);
                }
            };
            box.FieldLabel = fieldLabel;
            return box;
        }

        /// <summary>整窗绘制: 背景照片 → 深绿纱罩(右侧加深托卡片) → 底部标题落款。</summary>
        private void LoginForm_Paint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle full = this.ClientRectangle;

            if (_bgImage != null)
            {
                // cover 式铺满: 按比例取中裁剪
                float scale = Math.Max((float)full.Width / _bgImage.Width, (float)full.Height / _bgImage.Height);
                int w = (int)(_bgImage.Width * scale), h = (int)(_bgImage.Height * scale);
                RectangleF srcWin = new RectangleF((w - full.Width) / 2f, (h - full.Height) / 2f, full.Width, full.Height);
                g.DrawImage(_bgImage, full, srcWin.X, srcWin.Y, srcWin.Width, srcWin.Height, GraphicsUnit.Pixel);
            }
            else
            {
                using (SolidBrush b = new SolidBrush(AppTheme.Primary))
                    g.FillRectangle(b, full);
            }

            // 深绿纱罩: 左浅右深, 保证白字与右侧卡片对比
            using (LinearGradientBrush scrim = new LinearGradientBrush(full,
                Color.FromArgb(88, 9, 34, 22), Color.FromArgb(178, 7, 28, 19), 0f))
            {
                g.FillRectangle(scrim, full);
            }

            // 左下角产品落款(白字)
            using (Font titleFont = AppTheme.FontUi(17F, bold: true))
            using (SolidBrush white = new SolidBrush(Color.White))
            {
                g.DrawString("智能温室大棚监测系统", titleFont, white, 40, full.Height - 88);
            }
            using (Font subFont = AppTheme.FontUi(9F))
            using (SolidBrush soft = new SolidBrush(Color.FromArgb(196, 255, 255, 255)))
            {
                g.DrawString("STM32 环境采集 · 设备联动 · MQTT 远程监测", subFont, soft, 42, full.Height - 52);
            }

            if (_bgFailed)
            {
                using (Font warn = AppTheme.FontUi(7.5F))
                using (SolidBrush soft = new SolidBrush(Color.FromArgb(140, 255, 255, 255)))
                    g.DrawString("背景图缺失(ICON\\login-bg.jpg)", warn, soft, 42, 20);
            }
        }

        private Image LoadBackgroundImage()
        {
            try
            {
                string path = Path.Combine(Application.StartupPath, "ICON\\login-bg.jpg");
                if (!File.Exists(path)) { _bgFailed = true; return null; }
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var ms = new MemoryStream())
                {
                    fs.CopyTo(ms);
                    ms.Position = 0;
                    // 必须落成独立 Bitmap: Image.FromStream 要求流在图像生命周期内保持打开,
                    // 直接返回会在首次绘制时抛"GDI+ 中发生一般性错误"。
                    using (Image src = Image.FromStream(ms))
                    {
                        return new Bitmap(src);
                    }
                }
            }
            catch { _bgFailed = true; return null; }
        }

        private Image LoadIconSafely(string relativePath)
        {
            try
            {
                string path = Path.Combine(Application.StartupPath, relativePath);
                if (!File.Exists(path)) return null;
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var ms = new MemoryStream())
                {
                    fs.CopyTo(ms);
                    ms.Position = 0;
                    using (Image src = Image.FromStream(ms))
                    {
                        return new Bitmap(src);
                    }
                }
            }
            catch { return null; }
        }

        /// <summary>图标缺失时的兜底: 画一个绿叶圆盘, 保证登录页品牌区不空。</summary>
        private Bitmap MakeLeafFallback()
        {
            Bitmap bmp = new Bitmap(72, 72);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (SolidBrush b = new SolidBrush(AppTheme.Primary))
                    g.FillEllipse(b, 4, 4, 64, 64);
                using (Font f = AppTheme.FontUi(20F, bold: true))
                using (StringFormat sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    g.DrawString("温室", f, Brushes.White, new RectangleF(4, 4, 64, 64), sf);
            }
            return bmp;
        }

        private void LoginButton_Click(object sender, EventArgs e)
        {
            string user = (userTextBox.Text ?? string.Empty).Trim();
            string password = passwordTextBox.Text ?? string.Empty;

            if (user.Length == 0)
            {
                hintLabel.Text = "请输入用户名";
                hintLabel.ForeColor = AppTheme.Danger;
                userTextBox.Focus();
                return;
            }

            if (ValidateCredentials(user, password))
            {
                this.DialogResult = DialogResult.OK;
                this.Close();
                return;
            }

            hintLabel.Text = "用户名或密码错误, 请重新输入";
            hintLabel.ForeColor = AppTheme.Danger;
            passwordTextBox.Text = "";
            passwordTextBox.Focus();
        }

        /// <summary>鉴权入口。当前为固定账号, 将来接数据库/服务端时只改这里。</summary>
        private static bool ValidateCredentials(string user, string password)
        {
            return string.Equals(user, DefaultUser, StringComparison.Ordinal)
                && string.Equals(password, DefaultPassword, StringComparison.Ordinal);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _bgImage?.Dispose();
            base.OnFormClosed(e);
        }
    }

    /// <summary>圆角白卡容器(带描边与柔和外落影)。</summary>
    public class RoundedCard : Panel
    {
        public RoundedCard()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.UserPaint
                   | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = RoundedButton.CreateRoundedPath(rect, 16))
            {
                using (SolidBrush b = new SolidBrush(BackColor))
                    g.FillPath(b, path);
                using (Pen pen = new Pen(AppTheme.CardStroke, 1))
                    g.DrawPath(pen, path);
            }
            base.OnPaint(e);
        }
    }

    /// <summary>圆角输入框: 外层自绘圆角描边, 内嵌真实 TextBox 保证输入体验。</summary>
    public class RoundedTextBox : Panel
    {
        public Label FieldLabel;   // 悬挂在卡片上的字段标签(由 LoginForm 创建)
        public event KeyEventHandler InnerKeyDown;   // 回车登录等快捷键透传
        private readonly TextBox _inner = new TextBox();
        private bool _focused;

        public RoundedTextBox()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.UserPaint
                   | ControlStyles.ResizeRedraw, true);
            _inner.BorderStyle = BorderStyle.None;
            _inner.Font = new Font("微软雅黑", 10F);
            Controls.Add(_inner);
            _inner.Location = new Point(12, 8);
            _inner.GotFocus += (s, e) => { _focused = true; Invalidate(); };
            _inner.LostFocus += (s, e) => { _focused = false; Invalidate(); };
            _inner.KeyDown += (s, e) => InnerKeyDown?.Invoke(this, e);
            Resize += (s, e) => LayoutInner();
            LayoutInner();
        }

        private void LayoutInner()
        {
            _inner.Size = new Size(Width - 24, _inner.PreferredHeight);
            _inner.Location = new Point(12, (Height - _inner.Height) / 2);
        }

        public override string Text { get { return _inner.Text; } set { _inner.Text = value; } }
        public new bool UseSystemPasswordChar { get { return _inner.UseSystemPasswordChar; } set { _inner.UseSystemPasswordChar = value; } }
        public new Font Font { get { return _inner.Font; } set { _inner.Font = value; } }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = RoundedButton.CreateRoundedPath(rect, AppTheme.RadiusInput))
            {
                using (SolidBrush b = new SolidBrush(Color.FromArgb(248, 251, 249)))
                    g.FillPath(b, path);
                using (Pen pen = new Pen(_focused ? AppTheme.Primary : AppTheme.CardStroke, _focused ? 1.6f : 1))
                    g.DrawPath(pen, path);
            }
        }
    }
}
