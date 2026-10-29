using System.Text;
using System.Text.Json;

namespace IotLinkLab
{
    /// <summary>
    /// 物联网链路调试台主窗体。
    /// 定位: 通用 MQTT 串口链路调试工具 —— 设备模拟器(上行) / 订阅监控台 / 控制台(下行)。
    /// 骨架: 56px 顶部工作条(品牌+页签+连接操作) + 可展开连接设置抽屉 + 主工作区
    ///       + 可折叠运行日志抽屉 + 26px 全局状态栏。所有高度/边距按 DPI 换算。
    /// </summary>
    public class Form1 : Form
    {
        private readonly MqttLink _link = new();
        private readonly SimulatorTab _simTab;
        private readonly MonitorTab _monTab;
        private readonly ControlTab _ctlTab;

        private TableLayoutPanel _root = null!;
        private Panel _topBar = null!;
        private Card _settingsCard = null!;
        private SplitContainer _split = null!;
        private Panel _content = null!;
        private Panel _statusBar = null!;

        private TextBox _hostBox = null!, _portBox = null!, _dataTopicBox = null!, _controlTopicBox = null!;
        private ComboBox _protoCombo = null!;
        private FieldShell _hostShell = null!, _portShell = null!, _protoShell = null!;
        private FieldShell _dataTopicShell = null!, _controlTopicShell = null!;
        private RoundedButton _btnConnect = null!, _btnDisconnect = null!, _btnSettings = null!;
        private RoundedButton _btnLog = null!, _btnHelp = null!;
        private StatusChip _chip = null!;
        private Label _subtitle = null!;
        private FlowLayoutPanel _navChips = null!;
        private readonly NavChip[] _navItems = new NavChip[3];

        private Card _logCard = null!;
        private TableLayoutPanel _logGrid = null!;
        private FieldShell _logShell = null!;
        private RichTextBox _logBox = null!;
        private Label _logLatest = null!, _logErrorDot = null!;
        private Label _statusDot = null!, _statusLink = null!, _statusData = null!;
        private Label _statusSerial = null!, _statusLogBtn = null!;

        private ToolTip _formTips = null!;
        private bool _uiBuilt;
        private bool _settingsOpen;
        private bool _logExpanded;
        private bool _logHasError;

        // 分隔条状态: 默认按可用高度分配, 用户拖过后按用户比例保持
        private bool _userSplit;
        private double _userSplitRatio = 0.72;
        private bool _suppressSplitEvent;
        private bool _applyingMode;
        private bool _compact;

        private string AppDataDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "IotLinkLab");

        public Form1()
        {
            _simTab = new SimulatorTab();
            _monTab = new MonitorTab();
            _ctlTab = new ControlTab();

            Text = "物联网链路调试台 · IoT Link Lab v1.1";
            // 尺寸按屏幕可用区域收敛: 先把设计尺寸(96 DPI 逻辑像素)换算到当前 DPI,
            // 再与屏幕可用区域取小。150% 缩放下若直接写 1360x880, 逻辑可用区只剩 ~907x587,
            // 内容必然被挤压/裁切。
            Rectangle wa = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, UiFit.S(1280), UiFit.S(720));
            int w = Math.Min(UiFit.S(1360), wa.Width - UiFit.S(24));
            int h = Math.Min(UiFit.S(880), wa.Height - UiFit.S(24));
            MinimumSize = new Size(
                Math.Min(UiFit.S(1080), wa.Width - UiFit.S(24)),
                Math.Min(UiFit.S(640), wa.Height - UiFit.S(24)));
            Size = new Size(w, h);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.PageBg;
            AutoScaleMode = AutoScaleMode.None;   // 所有尺寸由 UiFit.S / Scale 统一换算, 避免二次缩放
            DoubleBuffered = true;
            Font = Theme.Body();

            BuildUi();
            WireEvents();
            LoadSettings();
            _statusSerial.Text = "串口 " + _simTab.SerialState;
            UpdateDataStat();
            SyncDeviceEcho();
            ApplyLayoutMode();
            ApplySplitRatio();

            // 快捷键: F1/F2/F3 切页 / F4 立发一帧 / F5 连接 / F6 断开 / F7 查看日志 / F8 导出 / F11 最大化
            KeyPreview = true;
            KeyDown += async (s, e) =>
            {
                if (e.KeyCode == Keys.F1) { e.Handled = true; SwitchTab(0); }
                else if (e.KeyCode == Keys.F2) { e.Handled = true; SwitchTab(1); }
                else if (e.KeyCode == Keys.F3) { e.Handled = true; SwitchTab(2); }
                else if (e.KeyCode == Keys.F11)
                {
                    e.Handled = true;
                    WindowState = WindowState == FormWindowState.Maximized
                        ? FormWindowState.Normal : FormWindowState.Maximized;
                }
                else if (e.KeyCode == Keys.F4) { e.Handled = true; _simTab.EmitOnce(); }
                else if (e.KeyCode == Keys.F5 && _btnConnect.Enabled) { e.Handled = true; await ConnectAsync(); }
                else if (e.KeyCode == Keys.F6 && _btnDisconnect.Enabled)
                {
                    e.Handled = true;
                    await _link.DisconnectAsync();
                }
                else if (e.KeyCode == Keys.F7) { e.Handled = true; ShowLogFile(); }
                else if (e.KeyCode == Keys.F8) { e.Handled = true; ExportData(); }
                else if (e.KeyCode == Keys.F9) { e.Handled = true; ToggleSettings(); }
                else if (e.KeyCode == Keys.F10) { e.Handled = true; SetLogExpanded(!_logExpanded, applyLayout: true); }
            };

            Shown += (s, e) =>
            {
                ApplyLayoutMode();
                ApplySplitRatio();

                Log("物联网链路调试台就绪。内置协议预设: STM32-Greenhouse(固件 V1.2 契约) + 自定义模板。", Theme.TextSecondary);
                Log($"运行日志: {LogStore.CurrentFile}", Theme.TextTertiary);
            };

            // 窗口尺寸变化时按比例重算分隔位置, 否则缩小时日志区会被整个挤出可视区域
            Resize += (s, e) =>
            {
                ApplyLayoutMode();
                ApplySplitRatio();
            };

            _split.SplitterMoved += (s, e) =>
            {
                if (_suppressSplitEvent || _split.IsDisposed) return;
                int usable = _split.Height - _split.SplitterWidth;
                if (usable <= UiFit.S(300)) return;

                int panel2 = usable - _split.SplitterDistance;
                if (_logExpanded && panel2 <= UiFit.S(46))
                {
                    SetLogExpanded(false, applyLayout: true);
                    return;
                }
                if (!_logExpanded && panel2 >= UiFit.S(64))
                {
                    _userSplitRatio = (double)_split.SplitterDistance / usable;
                    _userSplit = true;
                    SetLogExpanded(true, applyLayout: true);
                    return;
                }
                if (_logExpanded)
                {
                    _userSplitRatio = (double)_split.SplitterDistance / usable;
                    _userSplit = true;
                }
            };
        }

        // ==================== UI 构建 ====================

        private void BuildUi()
        {
            AppPaths.EnsureAll();
            _formTips = new ToolTip { AutoPopDelay = 15000, InitialDelay = 350, ReshowDelay = 150 };

            _root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1, BackColor = Theme.PageBg,
                Margin = new Padding(0), Padding = new Padding(0)
            };
            _root.RowStyles.Add(new RowStyle(SizeType.Absolute, UiFit.S(Theme.HeightTopBar)));
            _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));                       // 连接设置抽屉
            _root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));                      // 主区 + 日志
            _root.RowStyles.Add(new RowStyle(SizeType.Absolute, UiFit.S(Theme.HeightStatusBar)));

            BuildTopBar();
            BuildSettingsDrawer();
            BuildSplitArea();
            BuildStatusBar();

            _root.Controls.Add(_topBar, 0, 0);
            _root.Controls.Add(_settingsCard, 0, 1);
            _root.Controls.Add(_split, 0, 2);
            _root.Controls.Add(_statusBar, 0, 3);

            Controls.Add(_root);
            SwitchTab(0);
            _uiBuilt = true;
            ApplyLogStateUi();
        }

        // ---------- 1. 顶部工作条 ----------
        private void BuildTopBar()
        {
            _topBar = new Panel
            {
                Dock = DockStyle.Fill, BackColor = Theme.CardBg, Margin = new Padding(0),
                Padding = new Padding(UiFit.S(Theme.GapLg), 0, UiFit.S(Theme.GapLg), 0)
            };
            _topBar.Paint += (s, e) =>
            {
                using var pen = new Pen(Theme.Divider, 1);
                e.Graphics.DrawLine(pen, 0, _topBar.ClientSize.Height - 1, _topBar.ClientSize.Width, _topBar.ClientSize.Height - 1);
            };

            var bar = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1,
                BackColor = Color.Transparent, Margin = new Padding(0), Padding = new Padding(0)
            };
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bar.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            // 品牌: 28px 标识 + 标题/副标题
            var brand = new TableLayoutPanel
            {
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, RowCount = 1,
                BackColor = Color.Transparent, Margin = new Padding(0), Anchor = AnchorStyles.Left
            };
            brand.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            brand.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            brand.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            var mark = new PictureBox
            {
                Size = new Size(UiFit.S(28), UiFit.S(28)), SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent, Image = LoadImage("app-mark.png"),
                Anchor = AnchorStyles.None, Margin = new Padding(0)
            };
            var titleStack = new TableLayoutPanel
            {
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, RowCount = 2,
                BackColor = Color.Transparent, Margin = new Padding(UiFit.S(10), 0, 0, 0),
                Anchor = AnchorStyles.Left
            };
            titleStack.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            titleStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            titleStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var title = new Label
            {
                Text = "物联网链路调试台", AutoSize = true, Font = Theme.Title(),
                ForeColor = Theme.TextPrimary, BackColor = Color.Transparent, Margin = new Padding(0)
            };
            _subtitle = new Label
            {
                Text = "IoT Link Lab · 串口 / MQTT 链路联调", AutoSize = true, Font = Theme.Meta(),
                ForeColor = Theme.TextTertiary, BackColor = Color.Transparent, Margin = new Padding(0, UiFit.S(2), 0, 0)
            };
            titleStack.Controls.Add(title, 0, 0);
            titleStack.Controls.Add(_subtitle, 0, 1);
            brand.Controls.Add(mark, 0, 0);
            brand.Controls.Add(titleStack, 1, 0);
            bar.Controls.Add(brand, 0, 0);

            // 导航胶囊
            _navChips = new FlowLayoutPanel
            {
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false,
                BackColor = Color.Transparent, Margin = new Padding(UiFit.S(24), 0, 0, 0),
                Anchor = AnchorStyles.Left, Padding = new Padding(0)
            };
            string[] names = { "① 设备模拟器 · 上行", "② 订阅监控台", "③ 控制台 · 下行" };
            for (int i = 0; i < names.Length; i++)
            {
                var chip = new NavChip { Text = names[i], Margin = new Padding(0, 0, UiFit.S(Theme.GapSm), 0) };
                int idx = i;
                chip.Click += (s, e) => SwitchTab(idx);
                _navItems[i] = chip;
                _navChips.Controls.Add(chip);
            }
            bar.Controls.Add(_navChips, 1, 0);

            // 右侧操作
            var actions = new TableLayoutPanel
            {
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 7, RowCount = 1,
                BackColor = Color.Transparent, Margin = new Padding(0), Anchor = AnchorStyles.Right
            };
            for (int i = 0; i < 7; i++) actions.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            actions.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            _chip = new StatusChip { Anchor = AnchorStyles.None, Margin = new Padding(0, 0, UiFit.S(Theme.GapSm), 0) };
            _chip.SetState("未连接", Theme.TextTertiary);

            _btnConnect = new RoundedButton
            {
                Text = "连接", FillColor = Theme.Primary, Anchor = AnchorStyles.None,
                Height = UiFit.S(Theme.HeightButton), Margin = new Padding(0, 0, UiFit.S(Theme.GapSm), 0)
            };
            UiFit.SizeButton(_btnConnect, padTotal: 36, minWidth: 64);
            _btnDisconnect = new RoundedButton
            {
                Text = "断开", FillColor = Theme.Danger, TextColor = Theme.Danger, Outline = true,
                Enabled = false, Anchor = AnchorStyles.None, Height = UiFit.S(Theme.HeightButton),
                Margin = new Padding(0, 0, UiFit.S(Theme.GapSm), 0)
            };
            UiFit.SizeButton(_btnDisconnect, padTotal: 36, minWidth: 64);
            _btnSettings = new RoundedButton
            {
                Text = "设置", FillColor = Theme.TextSecondary, TextColor = Theme.TextSecondary, Outline = true,
                Anchor = AnchorStyles.None, Height = UiFit.S(Theme.HeightButton),
                Margin = new Padding(0, 0, UiFit.S(Theme.GapSm), 0)
            };
            UiFit.SizeButton(_btnSettings, padTotal: 32, minWidth: 60);
            _btnLog = new RoundedButton
            {
                Text = "日志", FillColor = Theme.Teal, TextColor = Theme.Teal, Outline = true,
                Anchor = AnchorStyles.None, Height = UiFit.S(Theme.HeightButton),
                Margin = new Padding(0, 0, UiFit.S(Theme.GapSm), 0)
            };
            UiFit.SizeButton(_btnLog, padTotal: 32, minWidth: 60);
            _btnHelp = new RoundedButton
            {
                Text = "?", FillColor = Theme.TextSecondary, TextColor = Theme.TextSecondary, Outline = true,
                Radius = 14, Anchor = AnchorStyles.None,
                Size = new Size(UiFit.S(28), UiFit.S(28)), Margin = new Padding(0)
            };
            _formTips.SetToolTip(_btnHelp,
                "快捷键\nF1/F2/F3 切换页面\nF4 发一帧\nF5 连接 / F6 断开\nF7 查看日志 / F8 导出数据\nF9 连接设置 / F10 运行日志\nF11 最大化");

            actions.Controls.Add(_chip, 0, 0);
            actions.Controls.Add(_btnConnect, 1, 0);
            actions.Controls.Add(_btnDisconnect, 2, 0);
            actions.Controls.Add(_btnSettings, 3, 0);
            actions.Controls.Add(_btnLog, 4, 0);
            actions.Controls.Add(_btnHelp, 5, 0);
            bar.Controls.Add(actions, 2, 0);

            _topBar.Controls.Add(bar);
        }

        // ---------- 2. 连接设置抽屉 ----------
        private void BuildSettingsDrawer()
        {
            _settingsCard = new Card
            {
                Dock = DockStyle.Fill, Margin = new Padding(UiFit.S(Theme.GapMd), UiFit.S(Theme.GapSm), UiFit.S(Theme.GapMd), UiFit.S(Theme.GapSm)),
                Padding = new Padding(UiFit.S(Theme.GapMd), UiFit.S(Theme.GapXs), UiFit.S(Theme.GapMd), UiFit.S(Theme.GapXs)),
                Visible = false
            };

            var grid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3,
                BackColor = Color.Transparent, Margin = new Padding(0), Padding = new Padding(0)
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, UiFit.S(Theme.HeightInput)));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, UiFit.S(Theme.GapXs * 2)));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, UiFit.S(Theme.HeightInput)));

            // 第一行: 服务器 / 端口 / 协议
            var row1 = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 8, RowCount = 1,
                BackColor = Color.Transparent, Margin = new Padding(0)
            };
            row1.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            row1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            row1.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            row1.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            row1.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            row1.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            row1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            row1.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            row1.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            _hostBox = new TextBox { Font = Theme.Mono(9.5F), Text = "127.0.0.1", BorderStyle = BorderStyle.None };
            _hostShell = new FieldShell(_hostBox) { Dock = DockStyle.Fill };
            _portBox = new TextBox { Font = Theme.Mono(9.5F), Text = "1883", BorderStyle = BorderStyle.None };
            UiFit.FitTextBox(_portBox, "65535", padTotal: 16);
            _portShell = new FieldShell(_portBox) { Width = _portBox.Width + UiFit.S(16), Anchor = AnchorStyles.Left };
            _protoCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList, Font = Theme.Ui(9.5F), ItemHeight = 22, FlatStyle = FlatStyle.Flat
            };
            _protoCombo.Items.AddRange(new object[] { "TCP", "WebSocket" });
            _protoCombo.SelectedIndex = 0;
            UiFit.FitCombo(_protoCombo, padTotal: 26);
            _protoCombo.SelectedIndexChanged += (s, e) =>
            {
                if (_portBox.Text == "1883" && _protoCombo.SelectedIndex == 1) _portBox.Text = "8083";
                else if (_portBox.Text == "8083" && _protoCombo.SelectedIndex == 0) _portBox.Text = "1883";
            };
            _protoShell = new FieldShell(_protoCombo) { Width = _protoCombo.Width + UiFit.S(16), Anchor = AnchorStyles.Left };

            row1.Controls.Add(FormLabel("服务器", right: UiFit.S(Theme.GapSm)), 0, 0);
            row1.Controls.Add(_hostShell, 1, 0);
            row1.Controls.Add(FormLabel(":", right: UiFit.S(Theme.GapSm)), 2, 0);
            row1.Controls.Add(_portShell, 3, 0);
            row1.Controls.Add(FormLabel("协议", left: UiFit.S(Theme.GapLg), right: UiFit.S(Theme.GapSm)), 4, 0);
            row1.Controls.Add(_protoShell, 5, 0);
            row1.Controls.Add(new Label
            {
                Text = "连接后自动订阅上行 / 下行主题", AutoSize = true, Font = Theme.Meta(),
                ForeColor = Theme.TextTertiary, BackColor = Color.Transparent,
                Anchor = AnchorStyles.Right, Margin = new Padding(0)
            }, 7, 0);
            grid.Controls.Add(row1, 0, 0);

            // 第二行: 上行 / 下行主题
            var row2 = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1,
                BackColor = Color.Transparent, Margin = new Padding(0)
            };
            row2.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            row2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            row2.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            row2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            row2.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            _dataTopicBox = new TextBox { Font = Theme.Mono(9.5F), Text = "green/data", BorderStyle = BorderStyle.None };
            _controlTopicBox = new TextBox { Font = Theme.Mono(9.5F), Text = "green/control", BorderStyle = BorderStyle.None };
            _dataTopicShell = new FieldShell(_dataTopicBox) { Dock = DockStyle.Fill, Margin = new Padding(0, 0, UiFit.S(Theme.GapLg), 0) };
            _controlTopicShell = new FieldShell(_controlTopicBox) { Dock = DockStyle.Fill };
            row2.Controls.Add(FormLabel("上行主题", right: UiFit.S(Theme.GapSm)), 0, 0);
            row2.Controls.Add(_dataTopicShell, 1, 0);
            row2.Controls.Add(FormLabel("下行主题", left: UiFit.S(Theme.GapLg), right: UiFit.S(Theme.GapSm)), 2, 0);
            row2.Controls.Add(_controlTopicShell, 3, 0);
            grid.Controls.Add(row2, 0, 2);

            _settingsCard.Controls.Add(grid);
        }

        private static Label FormLabel(string text, int left = 0, int right = 0) => new()
        {
            Text = text, AutoSize = true, Font = Theme.Body(), ForeColor = Theme.TextSecondary,
            BackColor = Color.Transparent, Anchor = AnchorStyles.Left,
            Margin = new Padding(left, 0, right, 0)
        };

        // ---------- 3. 主区 + 日志 ----------
        private void BuildSplitArea()
        {
            _split = new SplitContainer
            {
                Dock = DockStyle.Fill, Orientation = Orientation.Horizontal,
                SplitterWidth = UiFit.S(8), BackColor = Theme.PageBg,
                Margin = new Padding(0)
            };
            // 构造期不设置 MinSize: 控件尚未 Dock 出真实高度时设置会触发
            // “SplitterDistance 必须在 Panel1MinSize 和 Width - Panel2MinSize 之间”的异常。
            // 主区最小高度由 ApplySplitRatio 自行夹取。
            _split.Panel1MinSize = 0;
            _split.Panel2MinSize = 0;
            _split.Panel1.BackColor = Theme.PageBg;
            _split.Panel2.BackColor = Theme.PageBg;

            _content = new Panel
            {
                Dock = DockStyle.Fill, BackColor = Theme.PageBg,
                Padding = new Padding(UiFit.S(Theme.GapMd), UiFit.S(Theme.GapSm), UiFit.S(Theme.GapMd), UiFit.S(Theme.GapSm))
            };
            _simTab.Dock = DockStyle.Fill;
            _monTab.Dock = DockStyle.Fill;
            _ctlTab.Dock = DockStyle.Fill;
            _content.Controls.Add(_simTab);
            _content.Controls.Add(_ctlTab);
            _content.Controls.Add(_monTab);
            _split.Panel1.Controls.Add(_content);

            _split.Panel2.Controls.Add(BuildLogCard());
        }

        private Control BuildLogCard()
        {
            _logCard = new Card
            {
                Dock = DockStyle.Fill, Padding = new Padding(UiFit.S(Theme.GapMd), 0, UiFit.S(Theme.GapMd), 0),
                Margin = new Padding(UiFit.S(Theme.GapMd), 0, UiFit.S(Theme.GapMd), 0)
            };

            _logGrid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2,
                BackColor = Color.Transparent, Margin = new Padding(0), Padding = new Padding(0)
            };
            _logGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            _logGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, UiFit.S(Theme.HeightLogCollapsedCompact)));
            _logGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            var head = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1,
                BackColor = Color.Transparent, Margin = new Padding(0)
            };
            head.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            head.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            head.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            head.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            var titleFlow = new FlowLayoutPanel
            {
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false,
                BackColor = Color.Transparent, Margin = new Padding(0), Anchor = AnchorStyles.Left,
                Padding = new Padding(0)
            };
            _logErrorDot = new Label
            {
                Text = "●", AutoSize = true, Font = Theme.Meta(), ForeColor = Theme.Danger,
                BackColor = Color.Transparent, Visible = false, Anchor = AnchorStyles.None,
                Margin = new Padding(0, 0, UiFit.S(Theme.GapXs), 0)
            };
            var title = new Label
            {
                Text = "运行日志", AutoSize = true, Font = Theme.Section(),
                ForeColor = Theme.TextPrimary, BackColor = Color.Transparent,
                Anchor = AnchorStyles.None, Margin = new Padding(0)
            };
            titleFlow.Controls.Add(_logErrorDot);
            titleFlow.Controls.Add(title);

            _logLatest = new Label
            {
                Text = "等待日志…", AutoSize = false, Dock = DockStyle.Fill, AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft, Font = Theme.Meta(), ForeColor = Theme.TextTertiary,
                BackColor = Color.Transparent, Margin = new Padding(UiFit.S(Theme.GapMd), 0, UiFit.S(Theme.GapMd), 0)
            };

            var actions = new FlowLayoutPanel
            {
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false,
                BackColor = Color.Transparent, Margin = new Padding(0), Anchor = AnchorStyles.Right,
                Padding = new Padding(0)
            };
            var btnView = NewSmallButton("查看日志", Theme.Teal, outline: true);
            btnView.Click += (s, e) => ShowLogFile();
            var btnExport = NewSmallButton("导出数据", Theme.Primary, outline: false);
            btnExport.Click += (s, e) => ExportData();
            var btnCopy = NewSmallButton("复制", Theme.TextSecondary, outline: true);
            btnCopy.Click += (s, e) => CopyLog();
            var btnClear = NewSmallButton("清空", Theme.TextSecondary, outline: true);
            btnClear.Click += (s, e) =>
            {
                _logBox.Clear();
                _logCount = 0;
                Log("日志显示区已清空(磁盘日志文件保留)。", Theme.TextTertiary);
            };
            actions.Controls.Add(btnView);
            actions.Controls.Add(btnExport);
            actions.Controls.Add(btnCopy);
            actions.Controls.Add(btnClear);

            head.Controls.Add(titleFlow, 0, 0);
            head.Controls.Add(_logLatest, 1, 0);
            head.Controls.Add(actions, 2, 0);

            _logBox = new RichTextBox
            {
                Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
                ScrollBars = RichTextBoxScrollBars.Vertical,
                BackColor = Theme.FieldBg, Font = Theme.Mono(9F),
                BorderStyle = BorderStyle.None, DetectUrls = false, WordWrap = true
            };
            _logShell = new FieldShell(_logBox, multiline: true)
            {
                Dock = DockStyle.Fill, Margin = new Padding(0, UiFit.S(Theme.GapXs), 0, 0), Visible = false
            };

            _logGrid.Controls.Add(head, 0, 0);
            _logGrid.Controls.Add(_logShell, 0, 1);
            _logCard.Controls.Add(_logGrid);
            return _logCard;
        }

        private static RoundedButton NewSmallButton(string text, Color color, bool outline)
        {
            var b = new RoundedButton
            {
                Text = text,
                Font = Theme.Ui(8.5F, bold: !outline),
                Height = UiFit.S(Theme.HeightSmallButton),
                FillColor = color,
                TextColor = outline ? color : Theme.OnPrimary,
                Outline = outline,
                Margin = new Padding(0, 0, UiFit.S(Theme.GapSm), 0)
            };
            UiFit.SizeButton(b, padTotal: 28, minWidth: 52);
            return b;
        }

        // ---------- 4. 全局状态栏 ----------
        private void BuildStatusBar()
        {
            _statusBar = new Panel
            {
                Dock = DockStyle.Fill, BackColor = Theme.CardBg, Margin = new Padding(0),
                Padding = new Padding(UiFit.S(Theme.GapMd), 0, UiFit.S(Theme.GapMd), 0)
            };
            _statusBar.Paint += (s, e) =>
            {
                using var pen = new Pen(Theme.Divider, 1);
                e.Graphics.DrawLine(pen, 0, 0, _statusBar.ClientSize.Width, 0);
            };

            var grid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1,
                BackColor = Color.Transparent, Margin = new Padding(0)
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            var left = new FlowLayoutPanel
            {
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false,
                BackColor = Color.Transparent, Margin = new Padding(0), Anchor = AnchorStyles.Left
            };
            _statusDot = new Label
            {
                Text = "●", AutoSize = true, Font = Theme.Meta(), ForeColor = Theme.TextTertiary,
                BackColor = Color.Transparent, Anchor = AnchorStyles.None,
                Margin = new Padding(0, 0, UiFit.S(Theme.GapXs), 0)
            };
            _statusLink = new Label
            {
                Text = "未连接", AutoSize = true, Font = Theme.Meta(), ForeColor = Theme.TextSecondary,
                BackColor = Color.Transparent, Anchor = AnchorStyles.None, Margin = new Padding(0)
            };
            left.Controls.Add(_statusDot);
            left.Controls.Add(_statusLink);

            _statusData = new Label
            {
                Text = "已记录 0 条", AutoSize = true, Font = Theme.Meta(), ForeColor = Theme.TextTertiary,
                BackColor = Color.Transparent, Anchor = AnchorStyles.Left,
                Margin = new Padding(UiFit.S(Theme.GapLg), 0, 0, 0)
            };

            var right = new FlowLayoutPanel
            {
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false,
                BackColor = Color.Transparent, Margin = new Padding(0), Anchor = AnchorStyles.Right
            };
            _statusSerial = new Label
            {
                Text = "串口 未启用", AutoSize = true, Font = Theme.Meta(), ForeColor = Theme.TextTertiary,
                BackColor = Color.Transparent, Anchor = AnchorStyles.None,
                Margin = new Padding(0, 0, UiFit.S(Theme.GapLg), 0)
            };
            _statusLogBtn = new Label
            {
                Text = "展开日志", AutoSize = true, Font = Theme.Meta(), ForeColor = Theme.TextSecondary,
                BackColor = Color.Transparent, Anchor = AnchorStyles.None, Cursor = Cursors.Hand,
                Margin = new Padding(0)
            };
            _statusLogBtn.Click += (s, e) => SetLogExpanded(!_logExpanded, applyLayout: true);
            right.Controls.Add(_statusSerial);
            right.Controls.Add(_statusLogBtn);

            grid.Controls.Add(left, 0, 0);
            grid.Controls.Add(_statusData, 1, 0);
            grid.Controls.Add(right, 2, 0);
            _statusBar.Controls.Add(grid);
        }

        /// <summary>从内存流读图, 避免 Image.FromFile 锁住程序目录里的素材文件。</summary>
        internal static Image? LoadImage(params string[] segments)
        {
            try
            {
                string p = Path.Combine(AppContext.BaseDirectory, "assets", Path.Combine(segments));
                if (!File.Exists(p)) return null;
                using var fs = new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var ms = new MemoryStream();
                fs.CopyTo(ms); ms.Position = 0;
                return Image.FromStream(ms);
            }
            catch { return null; }
        }

        // ==================== 布局模式 ====================

        private void ApplyLayoutMode()
        {
            if (!_uiBuilt || _applyingMode || _root == null || _root.IsDisposed) return;
            _applyingMode = true;
            try
            {
                int w = ClientSize.Width;
                int h = ClientSize.Height;
                bool compact = h < UiFit.S(700);
                if (compact != _compact)
                {
                    _compact = compact;
                    _root.RowStyles[0].Height = UiFit.S(compact ? Theme.HeightTopBarCompact : Theme.HeightTopBar);
                    _root.RowStyles[3].Height = UiFit.S(compact ? Theme.HeightStatusBarCompact : Theme.HeightStatusBar);
                    _logGrid.RowStyles[0].Height = UiFit.S(_logExpanded ? 36 : (compact ? Theme.HeightLogCollapsedCompact : Theme.HeightLogCollapsed));
                }

                _subtitle.Visible = w >= UiFit.S(1200);
                _btnHelp.Visible = w >= UiFit.S(1120);

                bool narrow = w < UiFit.S(1120);
                string[] full = { "① 设备模拟器 · 上行", "② 订阅监控台", "③ 控制台 · 下行" };
                string[] shortNames = { "① 模拟器", "② 监控台", "③ 控制台" };
                for (int i = 0; i < _navItems.Length; i++)
                {
                    string want = narrow ? shortNames[i] : full[i];
                    if (_navItems[i].Text != want) _navItems[i].Text = want;
                }

                _settingsCard.Visible = _settingsOpen;
                _root.RowStyles[1].Height = _settingsOpen ? UiFit.S(96) : 0;
                FitTopBar();
            }
            finally { _applyingMode = false; }
        }

        private void FitTopBar()
        {
            if (!_uiBuilt) return;
            UiFit.SizeButton(_btnConnect, padTotal: 36, minWidth: 64);
            UiFit.SizeButton(_btnDisconnect, padTotal: 36, minWidth: 64);
            UiFit.SizeButton(_btnSettings, padTotal: 32, minWidth: 60);
            UiFit.SizeButton(_btnLog, padTotal: 32, minWidth: 60);
            _btnConnect.Height = _btnDisconnect.Height = _btnSettings.Height = _btnLog.Height = UiFit.S(Theme.HeightButton);
            _btnHelp.Size = new Size(UiFit.S(28), UiFit.S(28));
            FitSettingsFields();
        }

        private void FitSettingsFields()
        {
            if (_portShell == null) return;
            UiFit.FitTextBox(_portBox, "65535", padTotal: 16);
            _portShell.Width = _portBox.Width + UiFit.S(16);
            UiFit.FitCombo(_protoCombo, padTotal: 26);
            _protoShell.Width = _protoCombo.Width + UiFit.S(16);
        }

        // ==================== 日志抽屉状态 ====================

        private void ApplyLogStateUi()
        {
            if (_logShell == null) return;
            _logShell.Visible = _logExpanded;
            _logCard.Padding = _logExpanded
                ? new Padding(UiFit.S(Theme.GapMd), UiFit.S(Theme.GapSm), UiFit.S(Theme.GapMd), UiFit.S(Theme.GapSm))
                : new Padding(UiFit.S(Theme.GapMd), 0, UiFit.S(Theme.GapMd), 0);
            _logGrid.RowStyles[0].Height = UiFit.S(_logExpanded ? 36 : (_compact ? Theme.HeightLogCollapsedCompact : Theme.HeightLogCollapsed));
            _btnLog.Text = _logExpanded ? "收起日志" : "日志";
            if (_uiBuilt)
            {
                UiFit.SizeButton(_btnLog, padTotal: 32, minWidth: 60);
                _btnLog.Height = UiFit.S(Theme.HeightButton);
            }
            _statusLogBtn.Text = _logExpanded ? "收起日志" : "展开日志";
            if (_logExpanded)
            {
                _logHasError = false;
                _logErrorDot.Visible = false;
            }
        }

        private void ToggleSettings()
        {
            // 设置抽屉与展开日志互斥: 两者同时展开会把主区压到无法阅读
            if (!_settingsOpen && _logExpanded)
            {
                _logExpanded = false;
                ApplyLogStateUi();
            }
            _settingsOpen = !_settingsOpen;
            _btnSettings.Text = _settingsOpen ? "收起设置" : "设置";
            ApplyLayoutMode();
            ApplySplitRatio();
        }

        private void SetLogExpanded(bool expand, bool applyLayout)
        {
            if (_logExpanded == expand)
            {
                if (applyLayout) ApplySplitRatio();
                return;
            }
            // 展开日志时自动收起连接设置, 保证主工作区仍可读
            if (expand && _settingsOpen)
            {
                _settingsOpen = false;
                _btnSettings.Text = "设置";
            }
            _logExpanded = expand;
            ApplyLogStateUi();
            if (applyLayout) ApplySplitRatio();
            if (_uiBuilt && !_applyingMode) ApplyLayoutMode();
        }

        /// <summary>
        /// 日志区高度: 收起固定 32/34; 展开默认 168, 用户拖过后按用户比例。
        /// 主区始终保留至少 300px, 不会被日志挤出可视区。
        /// </summary>
        private void ApplySplitRatio()
        {
            try
            {
                if (_split == null || _split.IsDisposed || _split.Height <= 0) return;
                int usable = _split.Height - _split.SplitterWidth;
                int p1min = UiFit.S(300);
                int p2min = _logExpanded ? UiFit.S(120) : UiFit.S(_compact ? Theme.HeightLogCollapsedCompact : Theme.HeightLogCollapsed);
                if (usable < p1min + p2min)
                {
                    // 极端窗口: 展开态优先保证主区, 自动回到收起态
                    if (_logExpanded)
                    {
                        _logExpanded = false;
                        ApplyLogStateUi();
                        p2min = UiFit.S(Theme.HeightLogCollapsedCompact);
                    }
                    if (usable < p1min + p2min) return;
                }

                int targetP2 = _logExpanded
                    ? (_userSplit ? (int)Math.Round(usable * (1 - _userSplitRatio)) : UiFit.S(168))
                    : p2min;
                targetP2 = Math.Max(p2min, Math.Min(usable - p1min, targetP2));
                int target = usable - targetP2;

                if (_split.SplitterDistance == target) return;

                _suppressSplitEvent = true;
                try { _split.SplitterDistance = target; }
                finally { _suppressSplitEvent = false; }
            }
            catch { /* 极端窗口尺寸下忽略 */ }
        }

        // ==================== 事件接线 ====================

        private void WireEvents()
        {
            _btnConnect.Click += async (s, e) => await ConnectAsync();
            _btnDisconnect.Click += async (s, e) =>
            {
                await _link.DisconnectAsync();
                SaveSettings();
            };
            _btnSettings.Click += (s, e) => ToggleSettings();
            _btnLog.Click += (s, e) => SetLogExpanded(!_logExpanded, applyLayout: true);

            _link.StateChanged += (state, detail) => SafeInvoke(() =>
            {
                switch (state)
                {
                    case "connecting":
                        _chip.SetState("连接中", Theme.Warning);
                        UpdateStatusLink("连接中…", Theme.Warning);
                        _btnConnect.Enabled = false;
                        break;
                    case "connected":
                        _chip.SetState(ClientSize.Width >= UiFit.S(1200) ? "已连接 " + detail : "已连接", Theme.Success);
                        UpdateStatusLink($"已连接 {_hostBox.Text.Trim()}:{_portBox.Text.Trim()} ({_protoCombo.Text})", Theme.Success);
                        _btnConnect.Enabled = false; _btnDisconnect.Enabled = true;
                        Log($"MQTT 已连接: {_hostBox.Text}:{_portBox.Text} ({_protoCombo.Text})", Theme.LogOk);
                        _ = SubscribeDefaultsAsync();
                        break;
                    case "reconnecting":
                        _chip.SetState("重连中", Theme.Warning);
                        UpdateStatusLink(detail, Theme.Warning);
                        break;
                    case "disconnected":
                        _chip.SetState("未连接", Theme.TextTertiary);
                        UpdateStatusLink("未连接", Theme.TextTertiary);
                        _btnConnect.Enabled = true; _btnDisconnect.Enabled = false;
                        Log("MQTT 已断开", Theme.LogWarn);
                        break;
                    case "failed":
                        _chip.SetState("失败", Theme.Danger);
                        UpdateStatusLink("连接失败", Theme.Danger);
                        _btnConnect.Enabled = true; _btnDisconnect.Enabled = false;
                        break;
                }
            });

            _link.Log += msg => SafeInvoke(() => Log(msg, Theme.TextSecondary));

            _link.PayloadReceived += (topic, payload) => SafeInvoke(() =>
            {
                _monTab.OnMessage(topic, payload);
                bool isControl = topic == _controlTopicBox.Text.Trim() && topic.Length > 0;
                var received = new DataRow
                {
                    Time = DateTime.Now,
                    Direction = isControl ? "下行" : "订阅",
                    Topic = topic,
                    Payload = payload
                };
                Enrich(received, payload);
                DataStore.Add(received);
                if (isControl)
                {
                    Log($"[下行] {topic} <- {payload.Trim()}", Theme.LogWarn);
                    _simTab.HandleControlCommand(payload.Trim());
                    SyncDeviceEcho();
                }
                UpdateDataStat();
            });

            _simTab.FrameProduced += (frame, fd, json) => SafeInvoke(async () =>
            {
                string topic = _dataTopicBox.Text.Trim();

                // 无论是否连上服务器, 本地上行帧都记录下来, 保证导出数据完整
                DataStore.Add(new DataRow
                {
                    Time = DateTime.Now,
                    Direction = "上行",
                    Topic = topic,
                    Frame = frame,
                    Payload = json,
                    Temp = fd.HasTempHum ? fd.Temp : null,
                    Hum = fd.HasTempHum ? fd.Hum : null,
                    Soil = fd.Soil, Light = fd.Light, Raw = fd.Raw,
                    Servo = fd.Servo, Motor = fd.Motor, Mode = fd.Mode,
                    Air = fd.Air, Alarm = fd.Warning ? "Warning" : ""
                });
                UpdateDataStat();

                if (_link.IsConnected && topic.Length > 0)
                {
                    try
                    {
                        await _link.PublishAsync(topic, json, qos1: true);
                    }
                    catch (Exception ex) { Log("发布失败: " + ex.Message, Theme.LogErr); }
                }
                else
                {
                    Log("MQTT 未连接, 帧未发布(仅本地预览)", Theme.LogWarn);
                }
            });

            _simTab.Log += msg => SafeInvoke(() => Log(msg, Theme.TextSecondary));
            _simTab.SerialStateChanged += state => SafeInvoke(() => _statusSerial.Text = "串口 " + state);

            _monTab.SubscribeRequested += topic => SafeInvoke(async () =>
            {
                if (!_link.IsConnected) { Log("MQTT 未连接, 稍后连接时将自动订阅", Theme.LogWarn); return; }
                try { await _link.SubscribeAsync(topic); }
                catch (Exception ex) { Log("订阅失败: " + ex.Message, Theme.LogErr); }
            });

            _monTab.Log += msg => SafeInvoke(() => Log(msg, Theme.TextSecondary));

            _ctlTab.PublishRequested += (topic, payload, qos1) => SafeInvoke(async () =>
            {
                DataStore.Add(new DataRow
                {
                    Time = DateTime.Now,
                    Direction = "上行",
                    Topic = topic,
                    Payload = payload
                });
                UpdateDataStat();

                if (!_link.IsConnected) { Log("MQTT 未连接, 指令未发出", Theme.LogErr); return; }
                try
                {
                    await _link.PublishAsync(topic, payload, qos1);
                    Log($"[上行指令] {topic} -> {payload}{(qos1 ? " (QoS1)" : "")}", Theme.LogOk);
                    // 固件模拟回显: 下一帧即反映设备状态变化
                    _simTab.HandleControlCommand(payload);
                    SyncDeviceEcho();
                }
                catch (Exception ex) { Log("发布失败: " + ex.Message, Theme.LogErr); }
            });

            _ctlTab.Log += msg => SafeInvoke(() => Log(msg, Theme.TextSecondary));
        }

        private void UpdateStatusLink(string text, Color color)
        {
            if (_statusLink == null) return;
            _statusDot.ForeColor = color;
            _statusLink.Text = text;
            _statusLink.ForeColor = color == Theme.TextTertiary ? Theme.TextSecondary : color;
        }

        private void SyncDeviceEcho()
        {
            _ctlTab?.SetDeviceState(_simTab.ServoState, _simTab.MotorState, _simTab.ModeState);
        }

        private void SwitchTab(int index)
        {
            _simTab.Visible = index == 0;
            _monTab.Visible = index == 1;
            _ctlTab.Visible = index == 2;
            for (int i = 0; i < _navItems.Length; i++)
            {
                _navItems[i].Selected = i == index;
                _navItems[i].Invalidate();
            }
            if (index == 2) SyncDeviceEcho();
        }

        private async Task ConnectAsync()
        {
            string host = _hostBox.Text.Trim();
            if (host.Length == 0) { Log("请填写服务器地址", Theme.LogErr); return; }
            if (!int.TryParse(_portBox.Text.Trim(), out int port) || port <= 0 || port > 65535)
            { Log("端口不合法", Theme.LogErr); return; }
            bool ws = _protoCombo.SelectedIndex == 1;
            SaveSettings();
            try
            {
                await _link.ConnectAsync(host, port, ws);
            }
            catch (Exception ex)
            {
                _chip.SetState("失败", Theme.Danger);
                UpdateStatusLink("连接失败", Theme.Danger);
                _btnConnect.Enabled = true;
                Log("连接失败: " + ex.Message, Theme.LogErr);
            }
        }

        /// <summary>连接成功后自动订阅: 数据主题(监控) + 控制主题(回显)。</summary>
        private async Task SubscribeDefaultsAsync()
        {
            foreach (var topic in new[] { _dataTopicBox.Text.Trim(), _controlTopicBox.Text.Trim() })
            {
                if (topic.Length == 0) continue;
                try { await _link.SubscribeAsync(topic); }
                catch (Exception ex) { Log($"订阅 {topic} 失败: {ex.Message}", Theme.LogWarn); }
            }
        }

        // ==================== 日志 / 数据导出 ====================

        /// <summary>查看本次会话的完整磁盘日志(界面只显示最近若干行, 文件里是全量)。</summary>
        private void ShowLogFile()
        {
            try
            {
                string file = LogStore.CurrentFile;
                using var dlg = new ExportDialog("运行日志", file,
                    "完整会话日志(含界面未显示的历史行)。可另存为，或用「打开所在文件夹」定位。");
                dlg.ShowDialog(this);
            }
            catch (Exception ex) { Log("打开日志失败: " + ex.Message, Theme.LogErr); }
        }

        /// <summary>把收集到的链路数据导出成 CSV, 并立刻显示文件内容。</summary>
        private void ExportData()
        {
            try
            {
                int n = DataStore.Count;
                if (n == 0)
                {
                    Log("还没有可导出的数据。先在「① 设备模拟器」发一帧, 或订阅到消息后再导出。", Theme.LogWarn);
                    return;
                }
                string path = DataStore.ExportCsv();
                Log($"已导出 {n} 条数据 -> {path}", Theme.LogOk);
                using var dlg = new ExportDialog("数据导出", path,
                    $"本次导出 {n} 条记录(上行帧 / 下行指令 / 订阅消息)。CSV 带 BOM, Excel 可直接打开不乱码。");
                dlg.ShowDialog(this);
            }
            catch (Exception ex) { Log("导出失败: " + ex.Message, Theme.LogErr); }
        }

        private void CopyLog()
        {
            try
            {
                if (_logBox.TextLength == 0) return;
                Clipboard.SetText(_logBox.Text);
                Log("日志已复制到剪贴板。", Theme.TextTertiary);
            }
            catch (Exception ex) { Log("复制失败: " + ex.Message, Theme.LogErr); }
        }

        /// <summary>
        /// 把订阅到的载荷解析成结构化字段填进 DataRow, 让导出的 CSV 里订阅行也有温湿度等列。
        /// 载荷可能是 JSON(本工具自己发的)或原始串口帧文本(别的设备发的), 两种都要认。
        /// </summary>
        private static void Enrich(DataRow row, string payload)
        {
            string text = payload.Trim();
            if (text.Length == 0) return;

            if (text.StartsWith("{"))
            {
                try
                {
                    using var doc = JsonDocument.Parse(text);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("temperature", out var t) && t.ValueKind == JsonValueKind.Number)
                    { row.Temp = t.GetDouble(); }
                    if (root.TryGetProperty("humidity", out var h) && h.ValueKind == JsonValueKind.Number)
                    { row.Hum = h.GetDouble(); }
                    if (root.TryGetProperty("soilMoisture", out var s) && s.ValueKind == JsonValueKind.Number)
                    { row.Soil = s.GetInt32(); }
                    if (root.TryGetProperty("lightPercent", out var l) && l.ValueKind == JsonValueKind.Number)
                    { row.Light = l.GetInt32(); }
                    if (root.TryGetProperty("airQuality", out var a) && a.ValueKind == JsonValueKind.String)
                    { row.Air = a.GetString() ?? ""; }
                    if (root.TryGetProperty("servoStatus", out var sv) && sv.ValueKind == JsonValueKind.String)
                    { row.Servo = sv.GetString() ?? ""; }
                    if (root.TryGetProperty("motorStatus", out var mo) && mo.ValueKind == JsonValueKind.String)
                    { row.Motor = mo.GetString() ?? ""; }
                    if (root.TryGetProperty("mode", out var md) && md.ValueKind == JsonValueKind.String)
                    { string m = md.GetString() ?? ""; row.Mode = m == "manual" ? "MAN" : "AUTO"; }
                    if (root.TryGetProperty("alarmType", out var al) && al.ValueKind == JsonValueKind.String)
                    { string at = al.GetString() ?? ""; row.Alarm = at == "正常" ? "" : at; }
                    if (root.TryGetProperty("hasAlarm", out var ha) && ha.ValueKind == JsonValueKind.True)
                    { if (row.Alarm.Length == 0) row.Alarm = "Warning"; }
                    row.Frame = $"JSON ({text.Length} 字节)";
                    return;
                }
                catch (JsonException) { /* 不是合法 JSON, 落到下面的帧文本解析 */ }
            }

            var fd = FrameCodec.Parse(text);
            if (fd == null) return;

            row.Frame = text;
            row.Temp = fd.HasTempHum ? fd.Temp : null;
            row.Hum = fd.HasTempHum ? fd.Hum : null;
            row.Soil = fd.Soil;
            row.Light = fd.Light;
            row.Raw = fd.Raw;
            row.Servo = fd.Servo;
            row.Motor = fd.Motor;
            row.Mode = fd.Mode;
            row.Air = fd.Air;
            row.Alarm = fd.Warning ? "Warning" : "";
        }

        private void UpdateDataStat()
        {
            SafeInvoke(() =>
            {
                if (_statusData == null) return;
                int n = DataStore.Count;
                _statusData.Text = n == 0 ? "已记录 0 条" : $"已记录 {n} 条 · 可导出";
                _statusData.ForeColor = n == 0 ? Theme.TextTertiary : Theme.Teal;
            });
        }

        private void SafeInvoke(Action action)
        {
            try
            {
                if (IsDisposed || Disposing || !IsHandleCreated) return;
                if (InvokeRequired) BeginInvoke(action);
                else action();
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }

        // SafeInvoke 的 async 重载(避免 lambda async void 裸奔)
        private void SafeInvoke(Func<Task> action) => SafeInvoke(() => { try { _ = action(); } catch { } });

        private int _logCount;
        private const int LogTrimAt = 900;    // 超过这么多行开始裁剪
        private const int LogKeepLines = 500; // 裁剪后保留的末尾行数

        private void Log(string message, Color color)
        {
            // 完整记录始终落盘(F7 / 「查看日志」可看到界面裁掉的历史)
            LogStore.Append($"[{DateTime.Now:HH:mm:ss.fff}] {message}");

            SafeInvoke(() =>
            {
                _logBox.SelectionStart = _logBox.TextLength;
                _logBox.SelectionLength = 0;
                _logBox.SelectionColor = Theme.TextTertiary;
                _logBox.AppendText($"[{DateTime.Now:HH:mm:ss.fff}] ");
                _logBox.SelectionColor = color;
                _logBox.AppendText(message + Environment.NewLine);

                if (++_logCount > LogTrimAt)
                {
                    _logCount = LogKeepLines;
                    TrimLogBox(LogKeepLines);
                }

                _logBox.SelectionStart = _logBox.TextLength;
                _logBox.ScrollToCaret();

                UpdateLogLatest(message, color);
            });
        }

        /// <summary>收起态在日志标题行显示最新一条, 并用红点提示尚未查看的错误。</summary>
        private void UpdateLogLatest(string message, Color color)
        {
            if (_logLatest == null) return;
            string one = message.Replace("\r", " ").Replace("\n", " ");
            if (one.Length > 96) one = one[..96] + "…";
            _logLatest.Text = $"{DateTime.Now:HH:mm:ss}  {one}";
            _logLatest.ForeColor = color;
            if (color == Theme.LogErr) _logHasError = true;
            _logErrorDot.Visible = _logHasError && !_logExpanded;
        }

        /// <summary>
        /// 只保留末尾 keep 行。原实现用 text.IndexOf('\n') 按字符位置切, 中文与颜色分段的
        /// RichTextBox 容易切在段落中间; 这里改为按行切, 逻辑更直白也不会切坏。
        /// </summary>
        private void TrimLogBox(int keep)
        {
            var lines = _logBox.Lines;
            if (lines.Length <= keep) return;
            var tail = new string[keep];
            Array.Copy(lines, lines.Length - keep, tail, 0, keep);
            _logBox.Lines = tail;
        }

        private void LoadSettings()
        {
            try
            {
                AppPaths.EnsureAll();
                string file = Path.Combine(AppDataDir, "settings.json");
                if (!File.Exists(file)) return;
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                var r = doc.RootElement;
                if (r.TryGetProperty("host", out var v)) _hostBox.Text = v.GetString() ?? _hostBox.Text;
                // 端口兼容数字与字符串两种写法: 本程序存的是字符串, 但手改过 settings.json 的人
                // 很自然写成 "port": 1883 (数字)。原实现直接 GetString() 会在数字上抛
                // InvalidOperationException, 被外层 catch 吞掉后 host/主题/协议全部静默丢失。
                if (r.TryGetProperty("port", out var v2))
                {
                    _portBox.Text = v2.ValueKind == JsonValueKind.Number
                        ? v2.GetInt32().ToString()
                        : (v2.GetString() ?? _portBox.Text);
                }
                if (r.TryGetProperty("dataTopic", out var v4)) _dataTopicBox.Text = v4.GetString() ?? _dataTopicBox.Text;
                if (r.TryGetProperty("controlTopic", out var v5)) _controlTopicBox.Text = v5.GetString() ?? _controlTopicBox.Text;
                // useWs 放最后: 它会触发端口自动切换, 不能覆盖已恢复的端口
                if (r.TryGetProperty("useWs", out var v3) && v3.ValueKind == JsonValueKind.True) _protoCombo.SelectedIndex = 1;
                if (r.TryGetProperty("logOpen", out var v6) && v6.ValueKind == JsonValueKind.True) _logExpanded = true;
                if (r.TryGetProperty("logRatio", out var v7) && v7.ValueKind == JsonValueKind.Number)
                {
                    double ratio = v7.GetDouble();
                    if (ratio > 0.35 && ratio < 0.9) { _userSplitRatio = ratio; _userSplit = true; }
                }
                ApplyLogStateUi();
            }
            catch { /* 设置损坏时用默认值 */ }
        }

        private void SaveSettings()
        {
            try
            {
                AppPaths.EnsureAll();
                var obj = new
                {
                    host = _hostBox.Text.Trim(),
                    port = _portBox.Text.Trim(),
                    useWs = _protoCombo.SelectedIndex == 1,
                    dataTopic = _dataTopicBox.Text.Trim(),
                    controlTopic = _controlTopicBox.Text.Trim(),
                    logOpen = _logExpanded,
                    logRatio = _userSplit ? _userSplitRatio : 0
                };
                File.WriteAllText(Path.Combine(AppDataDir, "settings.json"),
                    JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true }),
                    new UTF8Encoding(false));
            }
            catch { /* 设置写失败不阻断 */ }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (!_uiBuilt) return;
            FitTopBar();
            ApplyLayoutMode();
        }

        protected override async void OnFormClosing(FormClosingEventArgs e)
        {
            try
            {
                SaveSettings();
                _simTab.Shutdown();
                await _link.DisposeAsync();
            }
            catch { }
            base.OnFormClosing(e);
        }
    }
}
