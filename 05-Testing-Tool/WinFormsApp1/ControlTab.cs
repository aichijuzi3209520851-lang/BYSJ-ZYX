namespace IotLinkLab
{
    /// <summary>
    /// 控制台(下行链路): 按固件指令集发预设指令 / 自定义报文到控制主题。
    /// 与"固件模拟回显"联动: 指令发出后模拟器状态同步变化, 下一帧即反映。
    /// 指令契约(固件 Parse_Command_Frame): mode-0/1 任意时刻生效; DUOJ/MOTOR 仅手动模式。
    /// 布局按 UI-SPEC 6.3: 左 46% / 右 54%; 命令按钮按文案实测宽度(不再写死 158 截字);
    /// 括注一律删掉改 ToolTip; 右侧输入框 Dock=Fill 吃满高度, 发送按钮右对齐。
    /// </summary>
    public class ControlTab : UserControl
    {
        private TextBox _topicInput = null!, _customInput = null!;
        private CheckBox _chkQos1 = null!;
        private readonly ToolTip _tip = new();

        /// <summary>(topic, payload, qos1)——Form 层发布。</summary>
        public event Action<string, string, bool>? PublishRequested;
        public event Action<string>? Log;

        public string ControlTopic => _topicInput.Text.Trim();

        public ControlTab()
        {
            BuildUi();
        }

        private void BuildUi()
        {
            Dock = DockStyle.Fill;
            BackColor = Color.Transparent;
            DoubleBuffered = true;

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
                BackColor = Color.Transparent, Margin = new Padding(0), Padding = new Padding(0)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46f));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 54f));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            layout.Controls.Add(BuildLeft(), 0, 0);
            layout.Controls.Add(BuildRight(), 1, 0);
            Controls.Add(layout);
        }

        // ------------------------------------------------------------------ 左: 固件指令集
        private Control BuildLeft()
        {
            var card = new Card
            {
                Dock = DockStyle.Fill, Padding = new Padding(Theme.Pad),
                Margin = new Padding(0, 0, 12, 0)               // 左右卡片之间留白 12
            };

            var grid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 9,
                BackColor = Color.Transparent, Margin = new Padding(0), Padding = new Padding(0)
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            for (int i = 0; i < 8; i++) grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));   // 末行吃掉多余高度, 内容靠上排列

            // ---- 控制主题 ----
            grid.Controls.Add(SectionTitle("控制主题"), 0, 0);

            var topicRow = new FlowLayoutPanel
            {
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false, FlowDirection = FlowDirection.LeftToRight,
                BackColor = Color.Transparent, Margin = new Padding(0, 0, 0, 4)
            };
            _topicInput = new TextBox
            {
                Font = Theme.Mono(9.5F), Text = "green/control", BorderStyle = BorderStyle.FixedSingle,
                Margin = new Padding(0, 5, 10, 0)
            };
            // 宽度按实测文案算, 不写死 220(U1 / U3)
            _topicInput.Width = UiFit.TextWidth("green/control", _topicInput.Font) + UiFit.Scale(_topicInput, 48);
            _chkQos1 = new CheckBox
            {
                Text = "QoS1", AutoSize = true, Font = Theme.Ui(9F), Checked = true,
                ForeColor = Theme.TextPrimary, BackColor = Color.Transparent,
                Margin = new Padding(8, 8, 0, 0)
            };
            topicRow.Controls.Add(_topicInput);
            topicRow.Controls.Add(_chkQos1);
            grid.Controls.Add(topicRow, 0, 1);

            // ---- 工作模式(括注已删除, 说明改 ToolTip)----
            Label tMode = SectionTitle("工作模式");
            grid.Controls.Add(tMode, 0, 2);
            grid.Controls.Add(CommandRow(
                MakeCmd("mode-0 自动", Theme.Primary),
                MakeCmd("mode-1 手动", Theme.Primary)), 0, 3);

            // ---- 天窗舵机 ----
            Label tServo = SectionTitle("天窗舵机");
            grid.Controls.Add(tServo, 0, 4);
            grid.Controls.Add(CommandRow(
                MakeCmd("DUOJ-1 开窗180°", Theme.Teal),
                MakeCmd("DUOJ-0 关窗0°", Theme.Teal)), 0, 5);

            // ---- 风扇电机 ----
            Label tMotor = SectionTitle("风扇电机");
            grid.Controls.Add(tMotor, 0, 6);
            grid.Controls.Add(CommandRow(
                MakeCmd("MOTOR-1 全速", Theme.Indigo),
                MakeCmd("MOTOR-0 停止", Theme.Indigo)), 0, 7);

            _tip.SetToolTip(tMode, "工作模式切换任意时刻生效");
            _tip.SetToolTip(tServo, "模式切换任意时刻生效，舵机/电机仅手动模式生效");
            _tip.SetToolTip(tMotor, "模式切换任意时刻生效，舵机/电机仅手动模式生效");
            _tip.SetToolTip(_topicInput, "下行控制主题, 与主窗体「控制主题」保持一致");
            _tip.SetToolTip(_chkQos1, "勾选后以 QoS1 发布(可靠送达); 取消则用 QoS0");

            card.Controls.Add(grid);
            return card;
        }

        /// <summary>一行两个命令按钮: 靠左排列, 不拉伸。</summary>
        private static FlowLayoutPanel CommandRow(params Control[] buttons)
        {
            var row = new FlowLayoutPanel
            {
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false, FlowDirection = FlowDirection.LeftToRight,
                BackColor = Color.Transparent, Margin = new Padding(0, 0, 0, 2)
            };
            row.Controls.AddRange(buttons);
            return row;
        }

        // ------------------------------------------------------------------ 右: 自定义报文
        private Control BuildRight()
        {
            var card = new Card
            {
                Dock = DockStyle.Fill, Padding = new Padding(Theme.Pad),
                Margin = new Padding(12, 0, 0, 0)
            };

            var grid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3,
                BackColor = Color.Transparent, Margin = new Padding(0), Padding = new Padding(0)
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));   // 输入框 Dock=Fill 吃满剩余高度
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            Label tCustom = SectionTitle("自定义报文");
            grid.Controls.Add(tCustom, 0, 0);

            _customInput = new TextBox
            {
                Dock = DockStyle.Fill, Multiline = true, Font = Theme.Mono(10F),
                BackColor = Theme.FieldBg, BorderStyle = BorderStyle.FixedSingle,
                ScrollBars = ScrollBars.Vertical, AcceptsReturn = true,
                Margin = new Padding(0, 0, 0, 12)
            };
            grid.Controls.Add(_customInput, 0, 1);

            var btnSend = new RoundedButton
            {
                Text = "发送报文", FillColor = Theme.Danger,
                Anchor = AnchorStyles.Right,                   // 6.3-3: 底部按钮贴右对齐
                Margin = new Padding(0, 6, 0, 0)
            };
            UiFit.SizeButton(btnSend, padTotal: 40, minWidth: 110);
            btnSend.Click += (s, e) => Send(_customInput.Text);
            grid.Controls.Add(btnSend, 0, 2);

            // 说明性长句不占版面, 改 ToolTip(原文: 换行用 \r\n 或留空自动补帧尾)
            _tip.SetToolTip(tCustom,
                "原文透传；换行用 \\r\\n，留空则自动补帧尾");
            _tip.SetToolTip(_customInput, "原文透传；换行用 \\r\\n，留空则自动补帧尾");
            _tip.SetToolTip(btnSend, "把上面的报文发布到控制主题");

            card.Controls.Add(grid);
            return card;
        }

        // ------------------------------------------------------------------ 组件工厂
        private static Label SectionTitle(string text)
        {
            Label lbl = UiFit.SectionTitle(text);
            lbl.Margin = new Padding(0, 10, 0, 6);   // 组与组之间留白 ≥ 12(上一行 8 + 这里 10)
            return lbl;
        }

        /// <summary>
        /// 命令按钮: 宽度按文案实测 + 34 留白(6.3-1), 杜绝「DUOJ-1 开窗180°」贴边截字。
        /// ⚠️ 指令解析依赖 label.Split(' ')[0], 第一个空格之前必须始终是命令字。
        /// </summary>
        private RoundedButton MakeCmd(string label, Color color)
        {
            var btn = new RoundedButton
            {
                Text = label, Height = Theme.HeightButton, FillColor = color,
                Margin = new Padding(0, 0, 10, 8)
            };
            UiFit.SizeButton(btn, padTotal: 34);
            string cmd = label.Split(' ')[0];
            btn.Click += (s, e) => Send(cmd);
            _tip.SetToolTip(btn, $"发送 {cmd}");
            return btn;
        }

        private void Send(string payload)
        {
            payload = payload.Trim();
            if (payload.Length == 0) { Log?.Invoke("报文为空, 已忽略"); return; }
            PublishRequested?.Invoke(ControlTopic, payload, _chkQos1.Checked);
        }
    }
}
