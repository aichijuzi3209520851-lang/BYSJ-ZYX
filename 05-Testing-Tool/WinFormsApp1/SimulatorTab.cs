using System.IO;
using System.IO.Ports;

namespace IotLinkLab
{
    /// <summary>
    /// 设备模拟器(上行链路): 按固件协议模板生成帧 → 解析预览 → 构建同契约 JSON。
    /// 场景预设/手动编辑/定时轮发(可加抖动)/可选真实串口输出(配合虚拟串口对时,
    /// 让真实上位机走完整"串口→解析→发布"链路)。
    /// 布局: 左卡(场景 / 基础数值 / 帧模板) + 右卡(发送控制 / 串口输出 / 帧预览·JSON 预览)
    ///       + 底部整宽设备状态条; 帧预览固定 64px, JSON 吃满剩余高度。
    /// 宽度全部由 UiFit 实测(下拉框不裁字, 数字框完整显示 2080), 说明性长句统一走 ToolTip。
    /// </summary>
    public class SimulatorTab : UserControl
    {
        private ComboBox _sceneCombo = null!;
        private NumericUpDown _numTemp = null!, _numHumi = null!, _numSoil = null!, _numLight = null!, _numRaw = null!;
        private TextBox _templateBox = null!;
        private NumericUpDown _numInterval = null!, _numJitter = null!;
        private CheckBox _chkJitter = null!;
        private RoundedButton _btnOnce = null!, _btnStart = null!;
        private TextBox _framePreview = null!, _jsonPreview = null!;
        private CheckBox _chkSerial = null!;
        private ComboBox _comPort = null!, _comBaud = null!;
        private SerialPort? _serial;
        private Label _stateServo = null!, _stateMotor = null!, _stateMode = null!;   // 模拟设备实时状态
        private PictureBox _icoServo = null!, _icoMotor = null!;                      // 状态条图标(22×22), 模式用文字徽章
        private ToolTip _tips = null!;                                                // 说明性长句的唯一去处

        private TableLayoutPanel _root = null!;
        private TableLayoutPanel _rightLayout = null!;
        private Card _leftCard = null!, _rightCard = null!;
        private FieldShell _sceneShell = null!, _templateShell = null!;
        private FieldShell _frameShell = null!, _jsonShell = null!;
        private FieldShell _comPortShell = null!, _comBaudShell = null!;
        private readonly List<(NumericUpDown Num, FieldShell Shell)> _numShells = new();

        private System.Windows.Forms.Timer _timer = null!;
        private readonly Random _rnd = new();
        private bool _uiBuilt;                                                        // BuildUi 完成后才允许按 DPI 复测宽度
        private bool _densityApplying;

        /// <summary>图标位图缓存: 同一张 PNG 只解码一次, 且已复制到内存(不锁文件)。</summary>
        private static readonly Dictionary<string, Image?> IconCache = new();

        /// <summary>固件侧状态(被控制台回显指令更新; 自动模式下设备指令被固件忽略)。</summary>
        public string ServoState = "OFF";
        public string MotorState = "OFF";
        public string ModeState = "AUTO";

        /// <summary>(帧文本, 解析结果, JSON)——Form 层负责发布。</summary>
        public event Action<string, FrameData, string>? FrameProduced;
        public event Action<string>? Log;
        /// <summary>串口输出状态变化(未启用 / COM6@9600), 供全局状态栏展示。</summary>
        public event Action<string>? SerialStateChanged;

        /// <summary>串口输出状态文本(未勾选时为“未启用”)。</summary>
        public string SerialState
        {
            get
            {
                if (_chkSerial == null || !_chkSerial.Checked) return "未启用";
                string port = _comPort?.SelectedItem as string ?? "未选端口";
                return $"{port}@{_comBaud?.Text ?? "9600"}";
            }
        }

        public SimulatorTab()
        {
            BuildUi();
            _timer = new System.Windows.Forms.Timer { Interval = 1500 };
            _timer.Tick += (s, e) => EmitFrame("定时");
            _sceneCombo.SelectedIndex = 0;
            ApplyScene(0);
            RefreshComPorts();
        }

        // ==================== UI ====================

        private void BuildUi()
        {
            Dock = DockStyle.Fill;
            BackColor = Color.Transparent;
            DoubleBuffered = true;

            _tips = new ToolTip { AutoPopDelay = 15000, InitialDelay = 350, ReshowDelay = 150 };

            // 两列(宽屏 42/58, 常规 40/60) + 底部整宽状态条
            _root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2,
                BackColor = Color.Transparent, Margin = new Padding(0), Padding = new Padding(0)
            };
            _root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40f));
            _root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60f));
            _root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            _root.RowStyles.Add(new RowStyle(SizeType.Absolute, UiFit.S(56)));

            _root.Controls.Add(BuildLeftCard(), 0, 0);
            _root.Controls.Add(BuildRightCard(), 1, 0);

            var stateBar = BuildStateBar();
            _root.Controls.Add(stateBar, 0, 1);
            _root.SetColumnSpan(stateBar, 2);
            RefreshStateLabels();

            Controls.Add(_root);
            _uiBuilt = true;
            FitAllWidths();
            ApplyResponsive();
            Resize += (s, e) => ApplyResponsive();
        }

        /// <summary>左卡: 场景预设 → 传感器数值 → 帧模板(吃掉卡片剩余高度)。</summary>
        private Card BuildLeftCard()
        {
            _leftCard = new Card
            {
                Dock = DockStyle.Fill, Padding = new Padding(UiFit.S(Theme.Pad)),
                Margin = new Padding(0, 0, UiFit.S(Theme.GapLg), 0)
            };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6,
                BackColor = Color.Transparent, Margin = new Padding(0), Padding = new Padding(0)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));        // 场景预设 标题
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));        // 场景下拉
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));        // 传感器数值 标题
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));        // 数值网格
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));        // 帧模板 标题
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));   // 帧模板(Fill)

            layout.Controls.Add(MakeTitle("场景预设", first: true), 0, 0);

            _sceneCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList, Font = Theme.Ui(9.5F), ItemHeight = 22,
                Anchor = AnchorStyles.Left, Margin = new Padding(0)
            };
            foreach (var (key, _, _, _, _, _, _) in FrameCodec.Scenes) _sceneCombo.Items.Add(key);
            _sceneCombo.SelectedIndexChanged += (s, e) => ApplyScene(Math.Max(0, _sceneCombo.SelectedIndex));
            _tips.SetToolTip(_sceneCombo, "一键套用预设场景(正常 / 高温 / 烟雾污染 / DHT11 读取失败等),套用后仍可手改下面的数值。");
            _sceneShell = new FieldShell(_sceneCombo) { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, UiFit.S(Theme.GapSm)) };
            layout.Controls.Add(_sceneShell, 0, 1);

            layout.Controls.Add(MakeTitle("传感器数值"), 0, 2);

            // 2 列 × 3 行: [图标] 标签 [数字框]; 每个数字框宽度由 UiFit.FitNumeric 实测
            var grid = new TableLayoutPanel
            {
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, RowCount = 3,
                BackColor = Color.Transparent, Anchor = AnchorStyles.Left | AnchorStyles.Top,
                Margin = new Padding(0, 0, 0, 0), Padding = new Padding(0)
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            for (int i = 0; i < 3; i++) grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            (_numTemp, _) = MakeNum("温度°C", "temp", 0, 100, 24, grid, 0, 0);
            (_numHumi, _) = MakeNum("湿度%", "humi", 0, 100, 62, grid, 1, 0);
            (_numSoil, _) = MakeNum("土壤%", null, 0, 100, 35, grid, 0, 1);      // 工程里没有土壤图标, 不硬凑
            (_numLight, _) = MakeNum("光照%", "light", 0, 100, 72, grid, 1, 1);
            (_numRaw, _) = MakeNum("raw", null, 0, 4095, 2080, grid, 0, 2);
            layout.Controls.Add(grid, 0, 3);

            layout.Controls.Add(MakeTitle("固件 V1.2 帧模板"), 0, 4);
            _templateBox = new TextBox
            {
                Multiline = true, ReadOnly = true, Dock = DockStyle.Fill,
                Font = Theme.Mono(9F), Text = FrameCodec.DefaultTemplate,
                ScrollBars = ScrollBars.Vertical, BackColor = Theme.FieldBg,
                BorderStyle = BorderStyle.None, WordWrap = true
            };
            _tips.SetToolTip(_templateBox, "固件 V1.2 实际下发的帧格式(占位符说明见 README);只读展示,上方的数值改动会实时反映到右侧预览。");
            _templateShell = new FieldShell(_templateBox, multiline: true) { Dock = DockStyle.Fill };
            layout.Controls.Add(_templateShell, 0, 5);

            _leftCard.Controls.Add(layout);
            return _leftCard;
        }

        /// <summary>右卡: 顶部「发送控制 + 串口输出」并排, 下方帧预览固定高度、JSON 预览吃满剩余高度。</summary>
        private Card BuildRightCard()
        {
            _rightCard = new Card
            {
                Dock = DockStyle.Fill, Padding = new Padding(UiFit.S(Theme.Pad)),
                Margin = new Padding(0)
            };

            _rightLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5,
                BackColor = Color.Transparent, Margin = new Padding(0), Padding = new Padding(0)
            };
            _rightLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            _rightLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, UiFit.S(148)));   // 控制区(含底边距)
            _rightLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));        // 1 帧预览 标题
            _rightLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, UiFit.S(64))); // 2 帧预览
            _rightLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));        // 3 JSON 预览 标题
            _rightLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));   // 4 JSON 预览

            // ---------- 控制区 ----------
            var topGrid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
                BackColor = Color.Transparent, Margin = new Padding(0, 0, 0, UiFit.S(Theme.GapMd))
            };
            topGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55f));
            topGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45f));
            // 控制区高度必须显式给定: 两个子面板都是 Dock=Fill, 若父行用 AutoSize,
            // 第三行(发送按钮/串口参数)会被裁掉, 只剩按钮顶部一条色带。
            topGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, UiFit.S(136)));

            var sendPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3,
                BackColor = Color.Transparent, Margin = new Padding(0, 0, UiFit.S(Theme.GapLg), 0)
            };
            sendPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            sendPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            sendPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            sendPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _numInterval = new NumericUpDown
            {
                Minimum = 1, Maximum = 60, Value = 1.5m, Increment = 0.5m, DecimalPlaces = 1,
                Font = Theme.Body(), BorderStyle = BorderStyle.None
            };
            UiFit.FitNumeric(_numInterval);
            var intervalShell = new FieldShell(_numInterval) { Width = _numInterval.Width + UiFit.S(16) };
            _numShells.Add((_numInterval, intervalShell));

            _chkJitter = new CheckBox
            {
                Text = "数值抖动", AutoSize = true, Font = Theme.Body(), ForeColor = Theme.TextPrimary,
                BackColor = Color.Transparent, Checked = false, Anchor = AnchorStyles.None,
                Margin = new Padding(0, 0, UiFit.S(Theme.GapSm), 0)
            };
            _tips.SetToolTip(_chkJitter, "勾选后每帧在基础值上叠加 ±抖动幅度 的随机波动,模拟真实传感器读数。");

            _numJitter = new NumericUpDown
            {
                Minimum = 0, Maximum = 20, Value = 2, Font = Theme.Body(),
                Enabled = false, BorderStyle = BorderStyle.None
            };
            UiFit.FitNumeric(_numJitter);
            var jitterShell = new FieldShell(_numJitter) { Width = _numJitter.Width + UiFit.S(16), Enabled = false };
            _numShells.Add((_numJitter, jitterShell));
            _chkJitter.CheckedChanged += (s, e) =>
            {
                _numJitter.Enabled = _chkJitter.Checked;
                jitterShell.Enabled = _chkJitter.Checked;
            };

            var sendRow = SingleRow(
                FieldLabel("周期", UiFit.S(Theme.GapSm)),
                intervalShell,
                FieldLabel("秒", UiFit.S(Theme.GapMd)),
                _chkJitter,
                FieldLabel("±", UiFit.S(Theme.GapXs)),
                jitterShell);
            sendRow.Margin = new Padding(0, 0, 0, UiFit.S(Theme.GapSm));
            sendPanel.Controls.Add(MakeTitle("发送控制", first: true), 0, 0);
            sendPanel.Controls.Add(sendRow, 0, 1);

            _btnOnce = new RoundedButton
            {
                Text = "手动发一帧", FillColor = Theme.Teal, Height = UiFit.S(Theme.HeightButton),
                Margin = new Padding(0, 0, UiFit.S(Theme.GapMd), 0), Anchor = AnchorStyles.None
            };
            _btnOnce.Click += (s, e) => EmitFrame("手动");
            _btnStart = new RoundedButton
            {
                Text = "定时发送", FillColor = Theme.Primary, Height = UiFit.S(Theme.HeightButton),
                Margin = new Padding(0), Anchor = AnchorStyles.None
            };
            _btnStart.Click += BtnStart_Click;
            _tips.SetToolTip(_btnOnce, "立即按当前场景与数值生成一帧并发布。");
            _tips.SetToolTip(_btnStart, "按上面的周期自动轮发;再次点击停止。");
            var btnRow = SingleRow(_btnOnce, _btnStart);
            sendPanel.Controls.Add(btnRow, 0, 2);

            var serialPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3,
                BackColor = Color.Transparent, Margin = new Padding(0), Padding = new Padding(0)
            };
            serialPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            serialPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            serialPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            serialPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _chkSerial = new CheckBox
            {
                Text = "同步写串口", AutoSize = true, Font = Theme.Body(), ForeColor = Theme.TextPrimary,
                BackColor = Color.Transparent, Anchor = AnchorStyles.None,
                Margin = new Padding(0, 0, 0, UiFit.S(Theme.GapSm))
            };
            _tips.SetToolTip(_chkSerial, "除 MQTT 发布外,把同一份帧文本写入所选串口(配合虚拟串口对,可让真实上位机走完整链路)。");

            _comPort = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList, Font = Theme.Body(),
                Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, UiFit.S(Theme.GapSm), 0)
            };
            _tips.SetToolTip(_comPort, "可选的真实/虚拟串口;没有选择时串口输出自动跳过(不影响 MQTT 发布)。");
            _comPort.Enabled = false;
            _comPortShell = new FieldShell(_comPort) { Anchor = AnchorStyles.Left, Enabled = false };

            _comBaud = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList, Font = Theme.Body(),
                Anchor = AnchorStyles.Left, Margin = new Padding(0)
            };
            _comBaud.Items.AddRange(new object[] { "9600", "19200", "115200" });
            _comBaud.SelectedIndex = 0;
            _comBaud.Enabled = false;
            _comBaudShell = new FieldShell(_comBaud) { Anchor = AnchorStyles.Left, Enabled = false };
            _chkSerial.CheckedChanged += (s, e) =>
            {
                _comPort.Enabled = _chkSerial.Checked;
                _comBaud.Enabled = _chkSerial.Checked;
                _comPortShell.Enabled = _chkSerial.Checked;
                _comBaudShell.Enabled = _chkSerial.Checked;
                NotifySerialState();
            };
            _comPort.SelectedIndexChanged += (s, e) => NotifySerialState();
            _comBaud.SelectedIndexChanged += (s, e) => NotifySerialState();

            serialPanel.Controls.Add(MakeTitle("串口输出", first: true), 0, 0);
            serialPanel.Controls.Add(_chkSerial, 0, 1);
            serialPanel.Controls.Add(SingleRow(_comPortShell, _comBaudShell), 0, 2);

            topGrid.Controls.Add(sendPanel, 0, 0);
            topGrid.Controls.Add(serialPanel, 1, 0);
            _rightLayout.Controls.Add(topGrid, 0, 0);

            // ---------- 预览区 ----------
            _rightLayout.Controls.Add(MakeTitle("帧预览", first: true), 0, 1);
            _framePreview = MakePreview(Theme.Mono(9.5F), wordWrap: true);
            _framePreview.MinimumSize = new Size(0, UiFit.S(40));
            _tips.SetToolTip(_framePreview, "与真实串口输出逐字节一致的帧文本(固件 printf 视角)。");
            _frameShell = new FieldShell(_framePreview, multiline: true) { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 0) };
            _rightLayout.Controls.Add(_frameShell, 0, 2);

            _rightLayout.Controls.Add(MakeTitle("JSON 预览"), 0, 3);
            _jsonPreview = MakePreview(Theme.Mono(9F), wordWrap: true);
            _jsonPreview.MinimumSize = new Size(0, UiFit.S(72));
            _tips.SetToolTip(_jsonPreview, "按与上位机同一套正则解析出的字段,以及最终发布的 green/data JSON。");
            _jsonShell = new FieldShell(_jsonPreview, multiline: true) { Dock = DockStyle.Fill, Margin = new Padding(0) };
            _rightLayout.Controls.Add(_jsonShell, 0, 4);

            _rightCard.Controls.Add(_rightLayout);
            return _rightCard;
        }

        /// <summary>底部设备状态条(整宽): 22×22 图标 + 文字, 状态项按三等分网格展开。</summary>
        private Control BuildStateBar()
        {
            var strip = new Card
            {
                Dock = DockStyle.Fill, Radius = 10,
                Padding = new Padding(UiFit.S(Theme.GapMd), UiFit.S(Theme.GapSm), UiFit.S(Theme.GapMd), UiFit.S(Theme.GapSm)),
                Margin = new Padding(0, UiFit.S(Theme.GapMd), 0, 0)
            };
            var cells = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 1,
                BackColor = Color.Transparent, Margin = new Padding(0)
            };
            cells.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            cells.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33f));
            cells.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33f));
            cells.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33f));
            cells.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            cells.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            var caption = new Label
            {
                Text = "设备状态", AutoSize = true, Font = Theme.Meta(), ForeColor = Theme.TextTertiary,
                BackColor = Color.Transparent, Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 0, UiFit.S(Theme.GapLg), 0)
            };
            _tips.SetToolTip(caption, "模拟设备固件侧的实时状态,随控制台回显指令变化(自动模式下设备指令被固件忽略)。");
            cells.Controls.Add(caption, 0, 0);

            var servo = MakeStateCell("天窗", "win-closed");
            var motor = MakeStateCell("风扇", "fan-off");
            var mode = MakeStateCell("模式", null);
            _stateServo = servo.Value; _icoServo = servo.Icon!;
            _stateMotor = motor.Value; _icoMotor = motor.Icon!;
            _stateMode = mode.Value;
            cells.Controls.Add(servo.Cell, 1, 0);
            cells.Controls.Add(motor.Cell, 2, 0);
            cells.Controls.Add(mode.Cell, 3, 0);

            var cadence = new Label
            {
                Text = "固件 V1.2 · 1.5s/帧", AutoSize = true, Font = Theme.Mono(8.5F),
                ForeColor = Theme.TextTertiary, BackColor = Color.Transparent,
                Anchor = AnchorStyles.Right, Margin = new Padding(0)
            };
            cells.Controls.Add(cadence, 4, 0);

            strip.Controls.Add(cells);
            return strip;
        }

        /// <summary>分组标题: 样式单源走 UiFit.SectionTitle, 只补间距(分组之间 ≥ 8)。</summary>
        private static Label MakeTitle(string text, bool first = false)
        {
            var title = UiFit.SectionTitle(text);
            title.Margin = new Padding(0, first ? 0 : UiFit.S(Theme.GapSm), 0, UiFit.S(6));
            return title;
        }

        private static Label FieldLabel(string text, int right = 8) => new()
        {
            Text = text, AutoSize = true, Font = Theme.Body(), ForeColor = Theme.TextSecondary,
            BackColor = Color.Transparent, Anchor = AnchorStyles.None, Margin = new Padding(0, 0, right, 0)
        };

        /// <summary>把若干控件横向排成一行, 并让每个控件在该行内居中。</summary>
        private static TableLayoutPanel SingleRow(params Control[] items)
        {
            var row = new TableLayoutPanel
            {
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = Math.Max(1, items.Length), RowCount = 1,
                BackColor = Color.Transparent, Margin = new Padding(0), Padding = new Padding(0)
            };
            int maxH = 1;
            for (int i = 0; i < items.Length; i++)
            {
                var c = items[i];
                c.Anchor = AnchorStyles.None;
                maxH = Math.Max(maxH, Math.Max(c.Height, c.PreferredSize.Height));
                row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                row.Controls.Add(c, i, 0);
            }
            // 行高必须取控件实测高度: FieldShell 的 PreferredSize 可能小于其实际高度,
            // 若用 Percent 行会把圆角输入框上下裁掉, 只剩一条底边。
            row.RowStyles.Add(new RowStyle(SizeType.Absolute, maxH));
            return row;
        }

        /// <summary>数值单元格: [图标] 标签 [数字框]; 宽度按 Maximum 实测, 2080 不会被裁成 208。</summary>
        private (NumericUpDown, Label) MakeNum(string label, string? icon, decimal min, decimal max, decimal val, TableLayoutPanel grid, int col, int row)
        {
            var items = new List<Control>();
            if (icon != null)
            {
                items.Add(new PictureBox
                {
                    SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(UiFit.S(18), UiFit.S(18)),
                    BackColor = Color.Transparent, Image = LoadIcon(icon), Anchor = AnchorStyles.None,
                    Margin = new Padding(0, 0, UiFit.S(6), 0)
                });
            }
            var cap = FieldLabel(label, UiFit.S(6));
            items.Add(cap);

            var num = new NumericUpDown
            {
                Minimum = min, Maximum = max, Value = val, Font = Theme.Body(), BorderStyle = BorderStyle.None
            };
            num.ValueChanged += (s, e) => UpdatePreviews();
            UiFit.FitNumeric(num);                          // 必须在 Maximum / Font 之后实测
            var shell = new FieldShell(num) { Width = num.Width + UiFit.S(16) };
            _numShells.Add((num, shell));
            items.Add(shell);

            var cell = SingleRow(items.ToArray());
            cell.Margin = new Padding(0, 0, UiFit.S(Theme.GapLg), UiFit.S(6));
            grid.Controls.Add(cell, col, row);
            return (num, cap);
        }

        /// <summary>状态单元格: 22×22 图标 + 名称 + 值; 图标可为空(模式用文字, 不借空气质量图标)。</summary>
        private (Label Value, PictureBox? Icon, TableLayoutPanel Cell) MakeStateCell(string title, string? iconName)
        {
            PictureBox? pic = null;
            var items = new List<Control>();
            if (iconName != null)
            {
                pic = new PictureBox
                {
                    SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(UiFit.S(22), UiFit.S(22)),
                    BackColor = Color.Transparent, Image = LoadIcon(iconName), Anchor = AnchorStyles.None,
                    Margin = new Padding(0, 0, UiFit.S(Theme.GapSm), 0)
                };
                items.Add(pic);
            }
            var cap = new Label
            {
                Text = title, AutoSize = true, Font = Theme.Meta(), ForeColor = Theme.TextTertiary,
                BackColor = Color.Transparent, Anchor = AnchorStyles.None,
                Margin = new Padding(0, 0, UiFit.S(6), 0)
            };
            var val = new Label
            {
                Text = "--", AutoSize = true, Font = Theme.Ui(9.5F, bold: true),
                ForeColor = Theme.TextSecondary, BackColor = Color.Transparent, Anchor = AnchorStyles.None,
                Margin = new Padding(0)
            };
            items.Add(cap);
            items.Add(val);
            var cell = SingleRow(items.ToArray());
            cell.Margin = new Padding(0, 0, UiFit.S(Theme.GapLg), 0);
            return (val, pic, cell);
        }

        private static TextBox MakePreview(Font font, bool wordWrap = false) => new()
        {
            Multiline = true, ReadOnly = true, BackColor = Theme.FieldBg,
            Font = font, BorderStyle = BorderStyle.None, ScrollBars = ScrollBars.Vertical,
            WordWrap = wordWrap, Margin = new Padding(0)
        };

        /// <summary>所有下拉/数字框/按钮的宽度统一由 UiFit 实测(本文件不写死任何控件宽度)。</summary>
        private void FitAllWidths()
        {
            UiFit.FitCombo(_sceneCombo);
            UiFit.FitCombo(_comPort);
            UiFit.FitCombo(_comBaud);

            foreach (var pair in _numShells)
            {
                UiFit.FitNumeric(pair.Num);
                pair.Shell.Width = pair.Num.Width + UiFit.S(16);
            }
            _comPortShell.Width = _comPort.Width + UiFit.S(16);
            _comBaudShell.Width = _comBaud.Width + UiFit.S(16);

            UiFit.SizeButton(_btnOnce, padTotal: 34, minWidth: 92);
            UiFit.SizeButton(_btnStart, padTotal: 34, minWidth: 92);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (_uiBuilt) { FitAllWidths(); ApplyResponsive(); }   // 句柄就绪(真实 DPI)后再实测一次
        }

        /// <summary>宽度断点(40/60 ↔ 42/58)与高度断点(状态条/帧预览/卡片内边距)。</summary>
        private void ApplyResponsive()
        {
            if (!_uiBuilt || _densityApplying || _root == null || _root.IsDisposed) return;
            _densityApplying = true;
            try
            {
                int w = ClientSize.Width;
                float left = w >= UiFit.S(1280) ? 42f : 40f;
                if (!_root.ColumnStyles[0].Width.Equals(left)) _root.ColumnStyles[0].Width = left;

                bool tight = Height < UiFit.S(560);
                _root.RowStyles[1].Height = UiFit.S((tight ? Theme.HeightStateBarCompact : Theme.HeightStateBar) + Theme.GapMd);
                _rightLayout.RowStyles[2].Height = UiFit.S(tight ? 56 : 64);

                int pad = UiFit.S(tight ? 12 : 16);
                if (_leftCard.Padding.Left != pad)
                {
                    _leftCard.Padding = new Padding(pad);
                    _rightCard.Padding = new Padding(pad);
                    _leftCard.Margin = new Padding(0, 0, UiFit.S(tight ? 12 : 16), 0);
                }
            }
            finally { _densityApplying = false; }
        }

        /// <summary>图标加载: 从 assets/icons 读 PNG 并复制到内存位图, 避免锁文件; 失败返回 null(界面降级为纯文字)。</summary>
        private static Image? LoadIcon(string name)
        {
            if (IconCache.TryGetValue(name, out var cached)) return cached;
            Image? img = null;
            try
            {
                foreach (string path in IconCandidates(name))
                {
                    if (!File.Exists(path)) continue;
                    using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                    using var ms = new MemoryStream();
                    fs.CopyTo(ms);
                    ms.Position = 0;
                    using var raw = Image.FromStream(ms);   // 必须复制到内存, 避免锁文件
                    img = new Bitmap(raw);                  // 再拷一份独立位图, 解除对流对象的依赖
                    break;
                }
            }
            catch { img = null; }
            IconCache[name] = img;
            return img;
        }

        /// <summary>候选路径: 输出目录优先, 未随 csproj 复制时沿上回退到源码树的 assets/icons(调试运行兜底)。</summary>
        private static IEnumerable<string> IconCandidates(string name)
        {
            string leaf = Path.Combine("assets", "icons", name + ".png");
            yield return Path.Combine(AppContext.BaseDirectory, leaf);

            DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 6 && dir != null; i++, dir = dir.Parent)
                yield return Path.Combine(dir.FullName, leaf);
        }

        /// <summary>刷新三段状态文字、配色与图标(手动/自动、开/关各有专属色)。</summary>
        private void RefreshStateLabels()
        {
            if (_stateServo == null) return;

            _stateServo.Text = ServoState == "ON" ? "开180°" : "关0°";
            _stateServo.ForeColor = ServoState == "ON" ? Theme.Primary : Theme.TextSecondary;
            _icoServo.Image = LoadIcon(ServoState == "ON" ? "win-open" : "win-closed");

            _stateMotor.Text = MotorState == "ON" ? "全速" : "停";
            _stateMotor.ForeColor = MotorState == "ON" ? Theme.Primary : Theme.TextSecondary;
            _icoMotor.Image = LoadIcon(MotorState == "ON" ? "fan-on" : "fan-off");

            _stateMode.Text = ModeState == "MAN" ? "手动" : "自动";
            _stateMode.ForeColor = ModeState == "MAN" ? Theme.Warning : Theme.Teal;
        }

        // ==================== 逻辑 ====================

        private void ApplyScene(int index)
        {
            if (index < 0 || index >= FrameCodec.Scenes.Length) return;
            var sc = FrameCodec.Scenes[index];
            bool dht11Fail = sc.Key.Contains("DHT11");
            _numTemp.Value = dht11Fail ? 0 : (decimal)sc.Temp;
            _numHumi.Value = dht11Fail ? 0 : (decimal)sc.Hum;
            _numSoil.Value = sc.Soil;
            _numLight.Value = sc.Light;
            _numRaw.Value = 2080;
            UpdatePreviews();
        }

        private double Jitter(double baseV, decimal amp)
        {
            if (!_chkJitter.Checked || amp <= 0) return baseV;
            double a = (double)amp;
            return Math.Round(baseV + (_rnd.NextDouble() * 2 - 1) * a, 1);
        }

        private (string frame, FrameData fd) BuildFrame()
        {
            bool dht11Fail = _sceneCombo.SelectedIndex == FrameCodec.Scenes.Length - 1;
            double temp = Clamp(Jitter((double)_numTemp.Value, _numJitter.Value), -50, 100);
            double hum = Clamp(Jitter((double)_numHumi.Value, _numJitter.Value), 0, 100);
            int soil = (int)Clamp(_numSoil.Value + (_chkJitter.Checked ? (decimal)_rnd.Next(-(int)_numJitter.Value, (int)_numJitter.Value + 1) : 0), 0, 100);
            int light = (int)Clamp(_numLight.Value, 0, 100);
            int raw = (int)_numRaw.Value;

            string servo = ServoState == "ON" ? "ON " : "OFF";   // 忠实还原固件 printf("ON ")
            string warn = Alarm.Evaluate(dht11Fail ? 0 : temp, dht11Fail ? 0 : hum, soil, ModeState == "AUTO" ? AirState() : AirState()).Has ? " Warning" : "";
            // air 状态由场景决定(正常/烟雾); 控制台回显不影响 air
            string air = dht11Fail ? AirState() : AirState();
            if (dht11Fail)
            {
                // DHT11 失败帧: 固件此时只打印失败提示 + 扩展字段(无 Temp/Humi 段)
                string frame = $"{FrameCodec.Dht11FailPrefix}Soil:{soil}%(raw={raw})  Light:{light}%  Servo:{servo} Motor:{MotorState} Mode:{ModeState} Air:{air}{warn}";
                var fdFail = FrameCodec.Parse(frame)!;
                return (frame, fdFail);
            }

            string normal = FrameCodec.Render(_templateBox.Text, temp, hum, soil, raw, light, servo, MotorState, ModeState, air, warn);
            var fd = FrameCodec.Parse(normal) ?? new FrameData { HasTempHum = true, Temp = temp, Hum = hum, Soil = soil, Raw = raw, Light = light, Servo = servo.Trim(), Motor = MotorState, Mode = ModeState, Air = air, Warning = warn.Length > 0 };
            return (normal, fd);
        }

        private double Clamp(double v, double min, double max) => Math.Max(min, Math.Min(max, v));
        private decimal Clamp(decimal v, decimal min, decimal max) => Math.Max(min, Math.Min(max, v));

        private string AirState() => _sceneCombo.SelectedIndex == 4 ? "bad" : "nice";

        private void EmitFrame(string source)
        {
            try
            {
                var (frame, fd) = BuildFrame();
                string json = FrameCodec.ToJson(fd, DateTime.Now);
                _framePreview.Text = frame + "\r\n";
                _jsonPreview.Text = "── 解析字段 ──\r\n" + Describe(fd) + "\r\n\r\n── 发布 JSON (green/data 契约) ──\r\n" + json;
                FrameProduced?.Invoke(frame, fd, json);

                if (_chkSerial.Checked) WriteSerial(frame + "\r\n");
                Log?.Invoke($"[{source}] 帧已生成: Temp={ (fd.HasTempHum ? fd.Temp.ToString("0.0") : "--") } Soil={fd.Soil} Air={fd.Air}{(fd.Warning ? " [报警]" : "")}");
            }
            catch (Exception ex)
            {
                Log?.Invoke("生成帧失败: " + ex.Message);
            }
        }

        private static string Describe(FrameData fd)
        {
            string t = fd.HasTempHum ? fd.Temp.ToString("0.0") : "null";
            string h = fd.HasTempHum ? fd.Hum.ToString("0.0") : "null";
            var (alarm, has) = Alarm.Evaluate(fd.HasTempHum ? fd.Temp : 0, fd.HasTempHum ? fd.Hum : 0, fd.Soil, fd.Air);
            return $"温度={t}  湿度={h}  土壤={fd.Soil}  光照={fd.Light}  Servo={fd.Servo.Trim()}  Motor={fd.Motor}  Mode={fd.Mode}  Air={fd.Air}  Warning={fd.Warning}  报警={alarm}{(has ? " ★" : "")}";
        }

        private void BtnStart_Click(object? sender, EventArgs e)
        {
            if (_timer.Enabled)
            {
                _timer.Stop();
                _btnStart.Text = "开始定时发送";
                _btnStart.FillColor = Theme.Primary;
                Log?.Invoke("定时发送已停止");
            }
            else
            {
                _timer.Interval = Math.Max(200, (int)(_numInterval.Value * 1000));
                _timer.Start();
                _btnStart.Text = "停止发送";
                _btnStart.FillColor = Theme.Danger;
                Log?.Invoke($"定时发送已启动(周期 {_timer.Interval}ms)");
            }
            UiFit.SizeButton(_btnStart);   // 文案变了必须重新实测宽度, 否则「开始定时发送」被截
        }

        private void NotifySerialState() => SerialStateChanged?.Invoke(SerialState);

        // ==================== 串口输出 ====================

        public void RefreshComPorts()
        {
            try
            {
                _comPort.Items.Clear();
                _comPort.Items.AddRange(SerialPort.GetPortNames());
                if (_comPort.Items.Count > 0) _comPort.SelectedIndex = 0;
                UiFit.FitCombo(_comPort);   // 端口名(COM10)长度不定, 按实际项实测宽度
                if (_uiBuilt) _comPortShell.Width = _comPort.Width + UiFit.S(16);
            }
            catch { }
        }

        private void WriteSerial(string text)
        {
            try
            {
                if (_comPort.SelectedItem is not string port) { Log?.Invoke("串口未选择, 已跳过串口输出"); return; }
                if (_serial == null || _serial.PortName != port || !_serial.IsOpen)
                {
                    _serial?.Dispose();
                    _serial = new SerialPort(port, int.Parse(_comBaud.Text!)) { NewLine = "\r\n" };
                    _serial.Open();
                    Log?.Invoke($"串口已打开: {port}@{_comBaud.Text}");
                    NotifySerialState();
                }
                _serial.Write(text);
            }
            catch (Exception ex)
            {
                Log?.Invoke("串口输出失败: " + ex.Message);
            }
        }

        /// <summary>固件模拟回显: 收到控制指令时更新状态(自动模式下设备指令被固件忽略)。</summary>
        public void HandleControlCommand(string cmd)
        {
            cmd = cmd.Trim();
            if (cmd == "mode-0") { ModeState = "AUTO"; Log?.Invoke("固件回显: mode-0 → 自动模式"); }
            else if (cmd == "mode-1") { ModeState = "MAN"; Log?.Invoke("固件回显: mode-1 → 手动模式"); }
            else if (ModeState == "MAN")
            {
                if (cmd == "DUOJ-1") { ServoState = "ON"; Log?.Invoke("固件回显: DUOJ-1 → 天窗开 180°"); }
                else if (cmd == "DUOJ-0") { ServoState = "OFF"; Log?.Invoke("固件回显: DUOJ-0 → 天窗关 0°"); }
                else if (cmd == "MOTOR-1") { MotorState = "ON"; Log?.Invoke("固件回显: MOTOR-1 → 风扇全速"); }
                else if (cmd == "MOTOR-0") { MotorState = "OFF"; Log?.Invoke("固件回显: MOTOR-0 → 风扇停"); }
            }
            else if (cmd.StartsWith("DUOJ") || cmd.StartsWith("MOTOR"))
            {
                Log?.Invoke($"固件忽略 {cmd}(自动模式下设备归报警联动管辖)");
            }
            RefreshStateLabels();
            UpdatePreviews();
        }

        private void UpdatePreviews()
        {
            try
            {
                var (frame, fd) = BuildFrame();
                string json = FrameCodec.ToJson(fd, DateTime.Now);
                _framePreview.Text = frame + "\r\n";
                _jsonPreview.Text = "── 解析字段 ──\r\n" + Describe(fd) + "\r\n\r\n── 发布 JSON (green/data 契约) ──\r\n" + json;
            }
            catch { /* 初始化期间控件未就绪, 忽略 */ }
        }

        /// <summary>按当前数值立即发一帧(等同点击「手动发一帧」), 供 F4 快捷键调用。</summary>
        public void EmitOnce() => EmitFrame("手动");

        public void Shutdown()
        {
            _timer?.Stop();
            try { _serial?.Close(); _serial?.Dispose(); } catch { }
        }
    }
}
