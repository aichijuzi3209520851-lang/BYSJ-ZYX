namespace IotLinkLab
{
    /// <summary>
    /// 控制台(下行链路): 按固件指令集发预设指令 / 自定义报文到控制主题。
    /// 与"固件模拟回显"联动: 指令发出后模拟器状态同步变化, 下一帧即反映。
    /// 指令契约(固件 Parse_Command_Frame): mode-0/1 任意时刻生效; DUOJ/MOTOR 仅手动模式。
    /// </summary>
    public class ControlTab : UserControl
    {
        private TextBox _topicInput = null!, _customInput = null!;
        private CheckBox _chkQos1 = null!;

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

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Color.Transparent };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52f));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48f));

            // ---------- 左: 固件指令集 ----------
            var left = new Card { Dock = DockStyle.Fill, Padding = new Padding(Theme.Pad) };
            var leftFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Color.Transparent };

            leftFlow.Controls.Add(Title("控制主题"));
            var topicRow = new FlowLayoutPanel { Width = 380, Height = 40, BackColor = Color.Transparent, Margin = new Padding(0, 2, 0, 0) };
            _topicInput = new TextBox { Width = 220, Font = Theme.Mono(9.5F), Text = "green/control" };
            _chkQos1 = new CheckBox { Text = "QoS1(可靠送达)", AutoSize = true, Font = Theme.Ui(9F), Checked = true, Margin = new Padding(8, 8, 0, 0) };
            topicRow.Controls.Add(_topicInput);
            topicRow.Controls.Add(_chkQos1);
            leftFlow.Controls.Add(topicRow);

            leftFlow.Controls.Add(Title("模式切换(任意时刻生效)"));
            var modeRow = new FlowLayoutPanel { Width = 380, Height = 44, BackColor = Color.Transparent, Margin = new Padding(0, 2, 0, 0) };
            modeRow.Controls.Add(MakeCmd("mode-0 自动", Theme.Primary));
            modeRow.Controls.Add(MakeCmd("mode-1 手动", Theme.Primary));
            leftFlow.Controls.Add(modeRow);

            leftFlow.Controls.Add(Title("天窗舵机(仅手动模式生效)"));
            var servoRow = new FlowLayoutPanel { Width = 380, Height = 44, BackColor = Color.Transparent, Margin = new Padding(0, 2, 0, 0) };
            servoRow.Controls.Add(MakeCmd("DUOJ-1 开窗180°", Theme.Teal));
            servoRow.Controls.Add(MakeCmd("DUOJ-0 关窗0°", Theme.Teal));
            leftFlow.Controls.Add(servoRow);

            leftFlow.Controls.Add(Title("风扇电机(仅手动模式生效)"));
            var motorRow = new FlowLayoutPanel { Width = 380, Height = 44, BackColor = Color.Transparent, Margin = new Padding(0, 2, 0, 0) };
            motorRow.Controls.Add(MakeCmd("MOTOR-1 全速", Theme.Indigo));
            motorRow.Controls.Add(MakeCmd("MOTOR-0 停止", Theme.Indigo));
            leftFlow.Controls.Add(motorRow);

            leftFlow.Controls.Add(Title("说明"));
            var note = new Label
            {
                Text = "mode-0/1 任意时刻生效; DUOJ/MOTOR 仅手动模式,\n自动模式下由报警联动管辖(发指令无效)。",
                AutoSize = true, MaximumSize = new Size(400, 0), Font = Theme.Ui(9F),
                ForeColor = Theme.TextTertiary, BackColor = Color.Transparent
            };
            leftFlow.Controls.Add(note);
            left.Controls.Add(leftFlow);

            // ---------- 右: 自定义报文 ----------
            var right = new Card { Dock = DockStyle.Fill, Padding = new Padding(Theme.Pad) };
            var rightLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, BackColor = Color.Transparent };
            rightLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            rightLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            rightLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            rightLayout.Controls.Add(Title("自定义报文(原文透传, 换行用 \r\n 或留空自动补帧尾)"), 0, 0);
            _customInput = new TextBox
            {
                Dock = DockStyle.Fill, Multiline = true, Font = Theme.Mono(10F),
                BackColor = Theme.FieldBg, BorderStyle = BorderStyle.FixedSingle
            };
            rightLayout.Controls.Add(_customInput, 0, 1);
            var sendRow = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, BackColor = Color.Transparent, FlowDirection = FlowDirection.RightToLeft, Margin = new Padding(0, 6, 0, 0) };
            var btnSend = new RoundedButton { Text = "发送报文", Width = 110, FillColor = Theme.Danger };
            btnSend.Click += (s, e) => Send(_customInput.Text);
            sendRow.Controls.Add(btnSend);
            rightLayout.Controls.Add(sendRow, 0, 2);
            right.Controls.Add(rightLayout);

            layout.Controls.Add(left, 0, 0);
            layout.Controls.Add(right, 1, 0);
            Controls.Add(layout);
        }

        private Label Title(string text) => new()
        {
            Text = text, AutoSize = true, Font = Theme.Ui(9.5F, bold: true),
            ForeColor = Theme.TextSecondary, Margin = new Padding(0, 10, 0, 4), BackColor = Color.Transparent
        };

        private RoundedButton MakeCmd(string label, Color color)
        {
            var btn = new RoundedButton { Text = label, Width = 158, Height = 34, FillColor = color, Margin = new Padding(0, 0, 8, 0) };
            string cmd = label.Split(' ')[0];
            btn.Click += (s, e) => Send(cmd);
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
