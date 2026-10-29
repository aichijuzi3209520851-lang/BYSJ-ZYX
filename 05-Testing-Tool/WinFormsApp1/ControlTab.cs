namespace IotLinkLab
{
    /// <summary>
    /// 控制台(下行链路): 按固件指令集发预设指令 / 自定义报文到控制主题。
    /// 与"固件模拟回显"联动: 指令发出后模拟器状态同步变化, 下一帧即反映。
    /// 指令契约(固件 Parse_Command_Frame): mode-0/1 任意时刻生效; DUOJ/MOTOR 仅手动模式。
    /// 布局: 左 44% 指令区(2×3 命令网格 + 设备回显) / 右 56% 自定义报文编辑器。
    /// </summary>
    public class ControlTab : UserControl
    {
        private TextBox _topicInput = null!, _customInput = null!;
        private CheckBox _chkQos1 = null!;
        private TableLayoutPanel _layout = null!;
        private Card _leftCard = null!, _rightCard = null!;
        private FieldShell _topicShell = null!, _customShell = null!;
        private Label _targetLabel = null!;
        private Label _echoServo = null!, _echoMotor = null!, _echoMode = null!;
        private readonly ToolTip _tip = new();
        private bool _densityApplying;

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

            _layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
                BackColor = Color.Transparent, Margin = new Padding(0), Padding = new Padding(0)
            };
            _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44f));
            _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 56f));
            _layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            _layout.Controls.Add(BuildLeft(), 0, 0);
            _layout.Controls.Add(BuildRight(), 1, 0);
            Controls.Add(_layout);

            Resize += (s, e) => ApplyResponsive();
            ApplyResponsive();
            UpdateTargetLabel();
        }

        private void ApplyResponsive()
        {
            if (_layout == null || _layout.IsDisposed || _densityApplying) return;
            _densityApplying = true;
            try
            {
                float left = ClientSize.Width < UiFit.S(1120) ? 42f : 44f;
                if (!_layout.ColumnStyles[0].Width.Equals(left)) _layout.ColumnStyles[0].Width = left;

                bool tight = Height < UiFit.S(560);
                int pad = UiFit.S(tight ? 12 : 16);
                if (_leftCard.Padding.Left != pad)
                {
                    _leftCard.Padding = new Padding(pad);
                    _rightCard.Padding = new Padding(pad);
                    _leftCard.Margin = new Padding(0, 0, UiFit.S(tight ? 12 : 16), 0);
                }
                ReFitTopic();
            }
            finally { _densityApplying = false; }
        }

        private void ReFitTopic()
        {
            if (_topicShell == null) return;
            UiFit.FitTextBox(_topicInput, "green/control", padTotal: 32);
            _topicShell.Width = _topicInput.Width + UiFit.S(16);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ReFitTopic();
        }

        // ------------------------------------------------------------------ 左: 固件指令集
        private Control BuildLeft()
        {
            _leftCard = new Card
            {
                Dock = DockStyle.Fill, Padding = new Padding(UiFit.S(Theme.Pad)),
                Margin = new Padding(0, 0, UiFit.S(Theme.GapLg), 0)      // 左右卡片之间只计算一次 16px 间距
            };

            var grid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 10,
                BackColor = Color.Transparent, Margin = new Padding(0), Padding = new Padding(0)
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            for (int i = 0; i < 9; i++) grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.RowStyles[1] = new RowStyle(SizeType.Absolute, UiFit.S(36));   // 控制主题行
            grid.RowStyles[3] = new RowStyle(SizeType.Absolute, UiFit.S(46));   // 命令网格
            grid.RowStyles[5] = new RowStyle(SizeType.Absolute, UiFit.S(46));
            grid.RowStyles[7] = new RowStyle(SizeType.Absolute, UiFit.S(46));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));   // 设备回显吃掉剩余高度

            // ---- 控制主题 ----
            grid.Controls.Add(SectionTitle("控制主题", first: true), 0, 0);

            var topicRow = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1,
                AutoSize = false, Height = UiFit.S(32),
                BackColor = Color.Transparent, Margin = new Padding(0, 0, 0, UiFit.S(Theme.GapXs))
            };
            topicRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            topicRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            topicRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            topicRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            _topicInput = new TextBox
            {
                Font = Theme.Mono(9.5F), Text = "green/control", BorderStyle = BorderStyle.None
            };
            UiFit.FitTextBox(_topicInput, "green/control", padTotal: 32);
            _topicShell = new FieldShell(_topicInput) { Width = _topicInput.Width + UiFit.S(16), Anchor = AnchorStyles.Left };
            _chkQos1 = new CheckBox
            {
                Text = "QoS1", AutoSize = true, Font = Theme.Body(), Checked = true,
                ForeColor = Theme.TextPrimary, BackColor = Color.Transparent, Anchor = AnchorStyles.None,
                Margin = new Padding(UiFit.S(Theme.GapSm), 0, 0, 0)
            };
            topicRow.Controls.Add(_topicShell, 0, 0);
            topicRow.Controls.Add(_chkQos1, 1, 0);
            grid.Controls.Add(topicRow, 0, 1);

            // ---- 工作模式(括注已删除, 说明改 ToolTip)----
            Label tMode = SectionTitle("工作模式");
            grid.Controls.Add(tMode, 0, 2);
            grid.Controls.Add(CommandGrid(
                MakeCmd("mode-0 自动"),
                MakeCmd("mode-1 手动")), 0, 3);

            // ---- 天窗舵机 ----
            Label tServo = SectionTitle("天窗舵机");
            grid.Controls.Add(tServo, 0, 4);
            grid.Controls.Add(CommandGrid(
                MakeCmd("DUOJ-1 开窗180°"),
                MakeCmd("DUOJ-0 关窗0°")), 0, 5);

            // ---- 风扇电机 ----
            Label tMotor = SectionTitle("风扇电机");
            grid.Controls.Add(tMotor, 0, 6);
            grid.Controls.Add(CommandGrid(
                MakeCmd("MOTOR-1 全速"),
                MakeCmd("MOTOR-0 停止")), 0, 7);

            // ---- 设备回显(吃掉原先的底部空白)----
            grid.Controls.Add(SectionTitle("设备回显"), 0, 8);
            grid.Controls.Add(BuildEchoPanel(), 0, 9);

            _tip.SetToolTip(tMode, "工作模式切换任意时刻生效");
            _tip.SetToolTip(tServo, "模式切换任意时刻生效，舵机/电机仅手动模式生效");
            _tip.SetToolTip(tMotor, "模式切换任意时刻生效，舵机/电机仅手动模式生效");
            _tip.SetToolTip(_topicInput, "下行控制主题, 与主窗体「下行主题」保持一致");
            _tip.SetToolTip(_chkQos1, "勾选后以 QoS1 发布(可靠送达); 取消则用 QoS0");
            _chkQos1.CheckedChanged += (s, e) => UpdateTargetLabel();
            _topicInput.TextChanged += (s, e) => UpdateTargetLabel();

            _leftCard.Controls.Add(grid);
            return _leftCard;
        }

        private Control BuildEchoPanel()
        {
            var card = new Card
            {
                Dock = DockStyle.Fill, Radius = 8, BackColor = Theme.FieldBg,
                Padding = new Padding(UiFit.S(Theme.GapMd), UiFit.S(Theme.GapSm), UiFit.S(Theme.GapMd), UiFit.S(Theme.GapSm)),
                Margin = new Padding(0)
            };
            var cells = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1,
                BackColor = Color.Transparent, Margin = new Padding(0)
            };
            for (int i = 0; i < 3; i++) cells.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3f));
            cells.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            (_echoServo, _) = EchoCell(cells, 0, "天窗");
            (_echoMotor, _) = EchoCell(cells, 1, "风扇");
            (_echoMode, _) = EchoCell(cells, 2, "模式");
            card.Controls.Add(cells);
            return card;
        }

        private static (Label Value, Label Caption) EchoCell(TableLayoutPanel cells, int col, string caption)
        {
            var cell = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2,
                BackColor = Color.Transparent, Margin = new Padding(0)
            };
            cell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            cell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            cell.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            var cap = new Label
            {
                Text = caption, AutoSize = true, Font = Theme.Meta(), ForeColor = Theme.TextTertiary,
                BackColor = Color.Transparent, Anchor = AnchorStyles.Left, Margin = new Padding(0)
            };
            var val = new Label
            {
                Text = "—", AutoSize = true, Font = Theme.Ui(10.5F, bold: true), ForeColor = Theme.TextSecondary,
                BackColor = Color.Transparent, Anchor = AnchorStyles.Left, Margin = new Padding(0, UiFit.S(2), 0, 0)
            };
            cell.Controls.Add(cap, 0, 0);
            cell.Controls.Add(val, 0, 1);
            cells.Controls.Add(cell, col, 0);
            return (val, cap);
        }

        /// <summary>两列等宽命令网格: 按钮 Dock=Fill, 文案不会被截。</summary>
        private static TableLayoutPanel CommandGrid(params RoundedButton[] buttons)
        {
            var grid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = Math.Max(1, buttons.Length), RowCount = 1,
                AutoSize = false, Height = UiFit.S(40),
                BackColor = Color.Transparent, Margin = new Padding(0, 0, 0, UiFit.S(Theme.GapXs)),
                Padding = new Padding(0)
            };
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, UiFit.S(40)));
            for (int i = 0; i < buttons.Length; i++)
            {
                grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / buttons.Length));
                buttons[i].Dock = DockStyle.Fill;
                buttons[i].Margin = new Padding(0, 0, i == buttons.Length - 1 ? 0 : UiFit.S(Theme.GapSm), 0);
                grid.Controls.Add(buttons[i], i, 0);
            }
            return grid;
        }

        // ------------------------------------------------------------------ 右: 自定义报文
        private Control BuildRight()
        {
            _rightCard = new Card
            {
                Dock = DockStyle.Fill, Padding = new Padding(UiFit.S(Theme.Pad)),
                Margin = new Padding(UiFit.S(Theme.GapMd), 0, 0, 0)
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

            Label tCustom = SectionTitle("自定义报文", first: true);
            grid.Controls.Add(tCustom, 0, 0);

            _customInput = new TextBox
            {
                Dock = DockStyle.Fill, Multiline = true, Font = Theme.Mono(10F),
                BackColor = Theme.FieldBg, BorderStyle = BorderStyle.None,
                ScrollBars = ScrollBars.Vertical, AcceptsReturn = true, Margin = new Padding(0)
            };
            _customShell = new FieldShell(_customInput, multiline: true) { Dock = DockStyle.Fill, Margin = new Padding(0) };
            grid.Controls.Add(_customShell, 0, 1);

            var footer = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
                BackColor = Color.Transparent, Margin = new Padding(0, UiFit.S(Theme.GapSm), 0, 0)
            };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            _targetLabel = new Label
            {
                Text = "目标 green/control · QoS1", AutoSize = true, Font = Theme.Meta(),
                ForeColor = Theme.TextTertiary, BackColor = Color.Transparent,
                Anchor = AnchorStyles.Left, Margin = new Padding(0)
            };

            var btnSend = new RoundedButton
            {
                Text = "发送报文", FillColor = Theme.Primary,
                Height = UiFit.S(Theme.HeightButton), Anchor = AnchorStyles.Right, Margin = new Padding(0)
            };
            UiFit.SizeButton(btnSend, padTotal: 36, minWidth: 108);
            btnSend.Click += (s, e) => Send(_customInput.Text);
            footer.Controls.Add(_targetLabel, 0, 0);
            footer.Controls.Add(btnSend, 1, 0);
            grid.Controls.Add(footer, 0, 2);

            // 说明性长句不占版面, 改 ToolTip(原文: 换行用 \r\n 或留空自动补帧尾)
            _tip.SetToolTip(tCustom, "原文透传；换行用 \\r\\n，留空则自动补帧尾");
            _tip.SetToolTip(_customInput, "原文透传；换行用 \\r\\n，留空则自动补帧尾");
            _tip.SetToolTip(btnSend, "把上面的报文发布到控制主题");

            _rightCard.Controls.Add(grid);
            return _rightCard;
        }

        // ------------------------------------------------------------------ 组件工厂
        private static Label SectionTitle(string text, bool first = false)
        {
            Label lbl = UiFit.SectionTitle(text);
            lbl.Margin = new Padding(0, first ? 0 : UiFit.S(Theme.GapSm), 0, UiFit.S(6));
            return lbl;
        }

        /// <summary>
        /// 命令按钮: 文案完整显示(Dock=Fill 等宽网格), 不再贴边/截字。
        /// ⚠️ 指令解析依赖 label.Split(' ')[0], 第一个空格之前必须始终是命令字。
        /// </summary>
        private RoundedButton MakeCmd(string label)
        {
            var btn = new RoundedButton
            {
                Text = label, Height = UiFit.S(40),
                FillColor = Theme.Indigo, TextColor = Theme.Indigo, Outline = true,
                Font = Theme.Ui(9.5F, bold: true), Margin = new Padding(0)
            };
            string cmd = label.Split(' ')[0];
            btn.Click += (s, e) => Send(cmd);
            _tip.SetToolTip(btn, $"发送 {cmd}");
            return btn;
        }

        /// <summary>下发后的设备状态回显(仅展示, 来自模拟器的固件侧状态)。</summary>
        public void SetDeviceState(string servo, string motor, string mode)
        {
            if (_echoServo == null) return;
            _echoServo.Text = servo == "ON" ? "开 180°" : "关 0°";
            _echoServo.ForeColor = servo == "ON" ? Theme.Primary : Theme.TextSecondary;
            _echoMotor.Text = motor == "ON" ? "全速" : "停止";
            _echoMotor.ForeColor = motor == "ON" ? Theme.Primary : Theme.TextSecondary;
            _echoMode.Text = mode == "MAN" ? "手动" : "自动";
            _echoMode.ForeColor = mode == "MAN" ? Theme.Warning : Theme.Teal;
        }

        private void UpdateTargetLabel()
        {
            if (_targetLabel == null) return;
            string topic = _topicInput.Text.Trim();
            _targetLabel.Text = $"目标 {topic} · {(_chkQos1.Checked ? "QoS1" : "QoS0")}";
        }

        private void Send(string payload)
        {
            payload = payload.Trim();
            if (payload.Length == 0) { Log?.Invoke("报文为空, 已忽略"); return; }
            PublishRequested?.Invoke(ControlTopic, payload, _chkQos1.Checked);
        }
    }
}
