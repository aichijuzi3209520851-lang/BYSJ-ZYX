using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DHT11
{
    /// <summary>
    /// 登录窗口。纯代码构建(不建 Designer 文件), 与 Form1 的手写布局风格保持一致。
    /// 账号口令硬编码为 admin/admin, 与本项目「单机演示 + 上位机本地鉴权」的定位匹配;
    /// 若将来要接真实鉴权, 只需替换 ValidateCredentials 的实现。
    /// </summary>
    public class LoginForm : Form
    {
        private const string DefaultUser = "admin";
        private const string DefaultPassword = "admin";

        private TextBox userTextBox;
        private TextBox passwordTextBox;
        private Button loginButton;
        private Button cancelButton;
        private Label hintLabel;

        private readonly Color primaryColor = Color.FromArgb(33, 150, 243);
        private readonly Color darkColor = Color.FromArgb(52, 73, 94);
        private readonly Color dangerColor = Color.FromArgb(244, 67, 54);
        private readonly Color textSecondaryColor = Color.FromArgb(120, 132, 148);
        private readonly Color pageBgColor = Color.FromArgb(244, 246, 249);

        public LoginForm()
        {
            BuildUi();
        }

        private void BuildUi()
        {
            this.SuspendLayout();

            this.Text = "智能温室监控系统 v" + Application.ProductVersion + " - 登录";
            this.ClientSize = new Size(400, 320);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = pageBgColor;
            this.DoubleBuffered = true;
            // 不挂 ApplicationIcon 之外的额外资源; 图标由 csproj 的 ApplicationIcon 决定。

            Panel card = new Panel
            {
                Location = new Point(28, 28),
                Size = new Size(344, 264),
                BackColor = Color.White
            };
            card.Paint += Card_Paint;

            Label titleLabel = new Label
            {
                Location = new Point(28, 22),
                AutoSize = true,
                Text = "智能温室监控系统",
                Font = new Font("微软雅黑", 15F, FontStyle.Bold),
                ForeColor = darkColor,
                BackColor = Color.Transparent
            };

            Label subTitleLabel = new Label
            {
                Location = new Point(30, 58),
                AutoSize = true,
                Text = "请登录后使用",
                Font = new Font("微软雅黑", 9F),
                ForeColor = textSecondaryColor,
                BackColor = Color.Transparent
            };

            Label userLabel = new Label
            {
                Location = new Point(28, 92),
                AutoSize = true,
                Text = "用户名",
                Font = new Font("微软雅黑", 9F),
                ForeColor = textSecondaryColor,
                BackColor = Color.Transparent
            };

            userTextBox = new TextBox
            {
                Location = new Point(28, 114),
                Width = 288,
                Font = new Font("微软雅黑", 10F),
                Text = DefaultUser        // 演示项目: 预填, 省一次输入
            };

            Label passwordLabel = new Label
            {
                Location = new Point(28, 152),
                AutoSize = true,
                Text = "密码",
                Font = new Font("微软雅黑", 9F),
                ForeColor = textSecondaryColor,
                BackColor = Color.Transparent
            };

            passwordTextBox = new TextBox
            {
                Location = new Point(28, 174),
                Width = 288,
                Font = new Font("微软雅黑", 10F),
                UseSystemPasswordChar = true
            };

            hintLabel = new Label
            {
                Location = new Point(28, 200),
                AutoSize = true,
                Text = "默认账号 admin / 密码 admin",
                Font = new Font("微软雅黑", 8.5F),
                ForeColor = textSecondaryColor,
                BackColor = Color.Transparent
            };

            loginButton = CreateStyledButton("登 录", new Point(28, 222), new Size(140, 32), primaryColor);
            loginButton.Click += LoginButton_Click;

            cancelButton = CreateStyledButton("退 出", new Point(176, 222), new Size(140, 32), dangerColor);
            cancelButton.Click += CancelButton_Click;

            card.Controls.AddRange(new Control[] {
                titleLabel, subTitleLabel,
                userLabel, userTextBox,
                passwordLabel, passwordTextBox,
                hintLabel, loginButton, cancelButton
            });

            this.Controls.Add(card);
            this.AcceptButton = loginButton;
            this.CancelButton = cancelButton;

            this.ResumeLayout(false);
        }

        private Button CreateStyledButton(string text, Point location, Size size, Color backColor)
        {
            Button btn = new Button
            {
                Text = text,
                Location = location,
                Size = size,
                BackColor = backColor,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("微软雅黑", 10F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderSize = 0;
            btn.FlatAppearance.MouseOverBackColor = ControlPaint.Light(backColor, 0.2f);
            btn.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(backColor, 0.1f);
            return btn;
        }

        /// <summary>卡片圆角描边, 与主界面的卡片外观保持一致。</summary>
        private void Card_Paint(object sender, PaintEventArgs e)
        {
            Panel panel = sender as Panel;
            if (panel == null) return;

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle rect = new Rectangle(0, 0, panel.Width - 1, panel.Height - 1);
            const int radius = 10;
            int diameter = radius * 2;

            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
                path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
                path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
                path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
                path.CloseAllFigures();

                using (Pen pen = new Pen(Color.FromArgb(228, 232, 238), 1))
                {
                    e.Graphics.DrawPath(pen, path);
                }
            }
        }

        private void LoginButton_Click(object sender, EventArgs e)
        {
            string user = (userTextBox.Text ?? string.Empty).Trim();
            string password = passwordTextBox.Text ?? string.Empty;

            if (user.Length == 0)
            {
                MessageBox.Show("请输入用户名!", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
            hintLabel.ForeColor = dangerColor;
            passwordTextBox.Clear();
            passwordTextBox.Focus();
        }

        private void CancelButton_Click(object sender, EventArgs e)
        {
            this.DialogResult = DialogResult.Cancel;
            this.Close();
        }

        /// <summary>鉴权入口。当前为固定账号, 将来接数据库/服务端时只改这里。</summary>
        private static bool ValidateCredentials(string user, string password)
        {
            return string.Equals(user, DefaultUser, StringComparison.Ordinal)
                && string.Equals(password, DefaultPassword, StringComparison.Ordinal);
        }
    }
}
