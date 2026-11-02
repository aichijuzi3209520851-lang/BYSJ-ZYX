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
        private TextBox _saveDirBox = null!;
        private ComboBox _protoCombo = null!;
        private FieldShell _hostShell = null!, _portShell = null!, _protoShell = null!;
        private FieldShell _dataTopicShell = null!, _controlTopicShell = null!, _saveDirShell = null!;
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
        private bool _resizing;
        // 拖拽期间挂起根布局: WM_SIZE 洪流里每帧都会让 TableLayoutPanel 同步重排整棵子树
        // (实测 48~143ms/帧, 必然掉帧)。挂起后拖拽只做窗口位移动画, 停手时一次收敛。
        private bool _layoutSuspendedByDrag;
        private Size _lastClientSize = Size.Empty;
        // 尺寸稳定后再重排: 拖拽窗口时每一帧都会触发跨档位整树重排(实测单次 ~300ms),
        // 用防抖定时器把这次重排推迟到用户停手之后, 拖拽过程只跟手不重排。
        private readonly System.Windows.Forms.Timer _resizeSettle = new() { Interval = 120 };
        // 记录上次真正写入 SplitContainer 的形态, 用它做"要不要再赋一次"的判定,
        // 避免每次 Resize 都重算并写 SplitterDistance(单次 77~1000ms)。
        private int _appliedP2Fixed = -1;
        private FixedPanel _appliedFixedPanel = FixedPanel.None;
        private double _appliedRatio = -1;

        /// <summary>
        /// SplitContainer 的「够准就不动」容差(像素)。
        /// 容器尺寸每变一次, Panel2 实际高度与目标值天然差 2~3px(SplitContainer 内部
        /// 按像素取整 + 分隔条宽度分摊), 而修正这点差值要重建两侧子窗口(实测 107ms)。
        /// 3px 的日志栏高度差不可见, 不值得付这个代价。
        /// </summary>
        private const int SplitterTolerancePx = 3;
        // 布局档位快照: 只有档位真的翻转时才重做文字/按钮宽度重算, 平常的 Resize 全部早退
        private bool _narrow;
        private bool _showSubtitle = true;
        private bool _showHelp = true;
        private int _activeTab = -1;

        private string AppDataDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "IotLinkLab");

        public Form1()
        {
            _simTab = new SimulatorTab();
            _monTab = new MonitorTab();
            _ctlTab = new ControlTab();

            Text = "物联网链路调试台 · IoT Link Lab v1.1";
            // 窗口图标: 显式从 assets/app.ico 读取, 保证标题栏/任务栏图标与 exe 内嵌图标(ApplicationIcon)
            // 以及界面左上角 app-mark.png 同源。若素材缺失则回退到 exe 自带图标。
            Icon = LoadAppIcon();
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
            // 把 Color.Transparent 换成等效实色, 这是最关键的单项性能修复(实测跨断点重排 3496ms → 505ms)。
            // WinForms 的 Transparent 并非 alpha 合成, 而是"绘制时向父控件索要背景" ——
            // 本界面有 4~5 层 TableLayoutPanel 嵌套 + 90 多处 Transparent, 每次重绘都要沿祖先链逐层回溯,
            // 且让每个子控件都无法走双缓冲的快速路径。改成从最近的非透明祖先解析出的等效实色后,
            // 视觉完全一致(截图逐区比对无差异), 但整棵树的绘制与布局成本降到原来的 1/7。
            MakeOpaque(this);
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

            // 窗口尺寸变化时按比例重算分隔位置, 否则缩小时日志区会被整个挤出可视区域。
            //
            // 性能要点(实测数据): 我们自己写的布局方法全部在 0.15ms 内, 但"跨过档位阈值"的那一次
            // 一定会触发一整棵控件树的重排 —— ApplyLayoutMode 跨断点 65ms、Sim 87ms、分隔条赋值 60ms,
            // 单次合计 ~300ms。拖拽时 WM_SIZE 每帧涌入, 只要在拖拽过程中就重排, 就会掉帧。
            //
            // 因此改成"尺寸稳定后再重排": 拖拽期间只记录尺寸, 用 120ms 防抖定时器在用户停手后
            // 统一做一次。拖拽过程本身由 TableLayoutPanel 的默认按比例布局跟随(不需要我们介入),
            // 而档位切换 / 分隔条比例这类"非连续"调整本来也不需要逐帧生效。
            Resize += (s, e) =>
            {
                if (_resizing) return;
                _resizing = true;
                try
                {
                    Size client = ClientSize;
                    if (client != _lastClientSize)
                    {
                        _lastClientSize = client;
                        // 拖拽期间挂起根布局: 让各层 TableLayoutPanel 不要在 WM_SIZE 洪流里
                        // 逐帧重排(实测每帧 48~143ms, 必然掉帧)。停手后 ResumeLayout 一次收敛。
                        if (!_layoutSuspendedByDrag)
                        {
                            _layoutSuspendedByDrag = true;
                            _root.SuspendLayout();
                        }
                        _resizeSettle.Stop();
                        _resizeSettle.Start();
                    }
                }
                finally { _resizing = false; }
            };

            // 停手后统一做一次真正的布局收敛。
            _resizeSettle.Tick += (s, e) =>
            {
                _resizeSettle.Stop();
                if (IsDisposed || !IsHandleCreated) return;
                using var _d = Diag.T("Form.Settle " + ClientSize.Width + "x" + ClientSize.Height);

                // 关键: 拖拽期间挂起的布局不要在这里先解除。
                // 早先的写法是「先 ResumeLayout 收敛一次(52ms), 再 ApplyLayoutMode 自己
                // 又 SuspendLayout/ResumeLayout 收敛一次(65ms)」—— 同一棵树的整树重排
                // 白做了两遍。现在让挂起状态一直保持到本方法末尾, ApplyLayoutMode 里的
                // 挂起/解除只是在计数上嵌套一层(WinForms 的 SuspendLayout 是引用计数的),
                // 最后在这里只解除一次, 全树只按最终尺寸重排一遍。
                ApplyLayoutMode();
                ApplySplitRatio();

                if (_layoutSuspendedByDrag)
                {
                    _layoutSuspendedByDrag = false;
                    using (Diag.T("Form.DragResume"))
                    {
                        _root.ResumeLayout(true);
                        PerformLayout();
                    }
                }

                // 上面两步会改内容区尺寸, 从而再次触发当前页的 Resize 并重启它自己的
                // 防抖定时器 —— 若不在这里直接收尾, 120ms 后同一页会把同一件事再做一遍
                // (实测重复付 56ms)。这里同步收敛并取消那次排队。
                SettleActiveTab();
                if (Diag.On) BeginInvoke(new Action(() => Diag.Mark("counters " + Diag.TakeCounters())));
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
                BackColor = Color.Transparent, Image = LoadBrandMark(UiFit.S(28)),
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
                Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5,
                BackColor = Color.Transparent, Margin = new Padding(0), Padding = new Padding(0)
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, UiFit.S(Theme.HeightInput)));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, UiFit.S(Theme.GapXs * 2)));
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

            // 第三行: 保存位置(日志 + 导出 CSV 的根目录)
            // 默认是 %APPDATA%\IotLinkLab, 但演示/交付时经常要把文件放到 U 盘或项目目录,
            // 所以给一个可编辑路径 + 「浏览」「使用默认」。空着即用默认目录。
            var row3 = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1,
                BackColor = Color.Transparent, Margin = new Padding(0)
            };
            row3.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            row3.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            row3.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            row3.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            row3.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            _saveDirBox = new TextBox { Font = Theme.Mono(9F), BorderStyle = BorderStyle.None, Text = "" };
            _saveDirShell = new FieldShell(_saveDirBox) { Dock = DockStyle.Fill, Margin = new Padding(0, 0, UiFit.S(Theme.GapSm), 0) };

            // 手输路径后按回车即生效: 路径框旁边虽有「浏览…」, 但把路径直接粘进来的场景更常见,
            // 必须回车就能用。留空回车 = 恢复默认(与「使用默认」按钮等价)。
            _saveDirBox.KeyDown += (s, e) =>
            {
                if (e.KeyCode != Keys.Enter) return;
                e.SuppressKeyPress = true;
                ApplySaveDir();
            };
            // 离开输入框时也顺手生效一次, 避免用户改了路径却忘了点任何按钮。
            _saveDirBox.Leave += (s, e) =>
            {
                string typed = _saveDirBox.Text.Trim();
                bool sameAsShown = AppPaths.IsCustom
                    ? string.Equals(AppPaths.NormalizeRoot(typed), AppPaths.Root, StringComparison.OrdinalIgnoreCase)
                    : typed.Length == 0;
                if (!sameAsShown) ApplySaveDir();
            };

            var btnBrowse = new RoundedButton { Text = "浏览…", Outline = true, FillColor = Theme.Primary, TextColor = Theme.Primary, Margin = new Padding(0, 0, UiFit.S(Theme.GapSm), 0) };
            btnBrowse.Click += (s, e) => BrowseSaveDir();
            UiFit.SizeButton(btnBrowse, 36, 68);

            var btnDefault = new RoundedButton { Text = "使用默认", Outline = true, FillColor = Theme.TextSecondary, TextColor = Theme.TextSecondary, Margin = new Padding(0) };
            btnDefault.Click += (s, e) =>
            {
                _saveDirBox.Text = "";
                ApplySaveDir();
            };
            UiFit.SizeButton(btnDefault, 36, 76);

            row3.Controls.Add(FormLabel("保存位置", right: UiFit.S(Theme.GapSm)), 0, 0);
            row3.Controls.Add(_saveDirShell, 1, 0);
            row3.Controls.Add(btnBrowse, 2, 0);
            row3.Controls.Add(btnDefault, 3, 0);
            grid.Controls.Add(row3, 0, 4);

            _settingsCard.Controls.Add(grid);
        }

        /// <summary>选一个保存根目录(日志与导出 CSV 都会落到它下面)。</summary>
        private void BrowseSaveDir()
        {
            using var dlg = new FolderBrowserDialog
            {
                Description = "选择日志与导出数据的保存位置",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = true
            };
            string current = AppPaths.NormalizeRoot(_saveDirBox.Text) ?? AppPaths.Root;
            try { if (Directory.Exists(current)) dlg.SelectedPath = current; } catch { }
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            _saveDirBox.Text = dlg.SelectedPath;
            ApplySaveDir();
        }

        /// <summary>
        /// 立即生效: 改根目录 → 重建 logs/exports 子目录 → 丢弃旧会话日志句柄(下次写入在新目录建文件)
        /// → 存盘。目录不可写时回滚并提示, 不能静默把用户的日志写丢。
        /// </summary>
        private void ApplySaveDir()
        {
            string want = _saveDirBox.Text.Trim();
            string? normalized = AppPaths.NormalizeRoot(want);

            if (normalized == null && want.Length > 0)
            {
                Log($"保存位置无效: {want}", Theme.LogErr);
                return;
            }

            // 探测目标目录是否真的可写(排除只读盘、无权限目录)
            if (normalized != null)
            {
                try
                {
                    Directory.CreateDirectory(normalized);
                    string probe = Path.Combine(normalized, $".write-test-{Guid.NewGuid():N}.tmp");
                    File.WriteAllText(probe, "ok");
                    File.Delete(probe);
                }
                catch (Exception ex)
                {
                    Log($"保存位置不可写, 已保留原设置: {ex.Message}", Theme.LogErr);
                    return;
                }
            }

            if (!AppPaths.SetRoot(normalized))
            {
                Log($"保存位置: {AppPaths.Root}", Theme.TextTertiary);
                return;
            }

            AppPaths.EnsureAll();
            LogStore.ResetFile();
            Log($"保存位置已切换到: {AppPaths.Root}", Theme.LogOk);
            Log($"后续日志与导出都会写到这里; 当前会话日志: {LogStore.CurrentFile}", Theme.TextTertiary);
            _saveDirBox.Text = AppPaths.IsCustom ? AppPaths.Root : "";
            SaveSettings();
            if (!_logExpanded) UpdateLogLatest("保存位置已切换", Theme.LogOk);
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

        /// <summary>
        /// 取窗口图标: 依次尝试 assets/app.ico → exe 内嵌图标 → 无。
        /// exe 内嵌图标由 csproj 的 ApplicationIcon 决定, 素材目录里的 app.ico 与它同源,
        /// 这样标题栏、任务栏、Alt+Tab 三处图标保持一致, 不会出现"换了 ico 但窗口还是旧图"。
        /// </summary>
        internal static Icon? LoadAppIcon()
        {
            try
            {
                string p = Path.Combine(AppContext.BaseDirectory, "assets", "app.ico");
                if (File.Exists(p))
                {
                    using var fs = new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.Read);
                    using var ms = new MemoryStream();
                    fs.CopyTo(ms); ms.Position = 0;
                    // 必须 new 一个副本: Icon 会持有流, 流释放后原对象不可用
                    using var loaded = new Icon(ms);
                    return (Icon)loaded.Clone();
                }
            }
            catch { /* 落到下面的 exe 回退 */ }
            try
            {
                string exe = Application.ExecutablePath;
                if (!string.IsNullOrEmpty(exe) && File.Exists(exe))
                {
                    var ico = Icon.ExtractAssociatedIcon(exe);
                    if (ico != null) return ico;
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// 界面左上角品牌标识。优先用 assets/app-mark.png(与 app.ico 同源导出),
        /// 素材缺失时直接从 app.ico 的最大帧解码, 避免"标题栏换了新图标、左上角还是旧图"。
        /// </summary>
        internal static Image? LoadBrandMark()
        {
            var img = LoadImage("app-mark.png");
            if (img != null) return img;
            var ico = LoadAppIcon();
            if (ico == null) return null;
            using (ico) return ico.ToBitmap();
        }

        /// <summary>
        /// 左上角标识按目标像素尺寸预先高质量缩放。
        /// 原实现是把 256×256 位图塞进 28px 的 PictureBox 交给 Zoom 缩放,
        /// GDI+ 默认插值会把它糊成一团; 预缩放后直接 1:1 贴图, 边缘干净且绘制更快。
        /// </summary>
        internal static Image? LoadBrandMark(int pixelSize)
        {
            var src = LoadBrandMark();
            if (src == null) return null;
            if (pixelSize <= 0 || src.Width <= pixelSize) return src;
            try
            {
                var bmp = new Bitmap(pixelSize, pixelSize);
                using var g = Graphics.FromImage(bmp);
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                using var attrs = new System.Drawing.Imaging.ImageAttributes();
                attrs.SetWrapMode(System.Drawing.Drawing2D.WrapMode.TileFlipXY);   // 抑制缩放边缘溢色
                g.DrawImage(src, new Rectangle(0, 0, pixelSize, pixelSize),
                    0, 0, src.Width, src.Height, GraphicsUnit.Pixel, attrs);
                src.Dispose();
                return bmp;
            }
            catch { return src; }
        }


        // ==================== 布局模式 ====================

        /// <summary>
        /// 按 DPI/窗口尺寸切换紧凑布局。
        /// 性能要点: 原实现每次 Resize 都无条件写 RowStyles.Height / Visible / NavChip.Text 并调用 FitTopBar,
        /// 而 TableLayoutPanel 的每一个样式写入、每一个 Visible 翻转都会触发整棵子树的重新布局 + 重绘,
        /// 拖拽窗口时 WM_SIZE 每帧涌入数次 → 布局风暴 → 严重掉帧。
        /// 现在所有写入都先比对当前值, 且只有跨过宽度档位时才重算顶栏按钮宽度。
        /// </summary>
        private void ApplyLayoutMode(bool force = false)
        {
            if (!_uiBuilt || _applyingMode || _root == null || _root.IsDisposed) return;
            using var _d = Diag.T("Form.ApplyLayoutMode");
            _applyingMode = true;
            try
            {
                int w = ClientSize.Width;
                int h = ClientSize.Height;
                bool compact = h < UiFit.S(700);
                bool compactChanged = compact != _compact;

                bool wideSubtitle = w >= UiFit.S(1200);
                bool wideHelp = w >= UiFit.S(1120);
                bool narrow = w < UiFit.S(1120);
                bool widthTierChanged = narrow != _narrow || wideSubtitle != _showSubtitle || wideHelp != _showHelp;
                _narrow = narrow;
                _showSubtitle = wideSubtitle;
                _showHelp = wideHelp;

                // 批量合并布局: RowStyle/Visible 的每个赋值都会立刻触发一次整棵子树重排,
                // 跨断点时这里有 8~9 处写入 = 8~9 次全量重排(实测 113ms)。
                // 全部写入都必须在挂起区内完成, 漏一处在外面就白挂起一次。
                SuspendLayout();
                _root.SuspendLayout();
                try
                {
                    using (Diag.T("ALM.Compact"))
                    {
                        if (compactChanged)
                        {
                            _compact = compact;
                            SetRowHeight(_root, 0, UiFit.S(compact ? Theme.HeightTopBarCompact : Theme.HeightTopBar));
                            SetRowHeight(_root, 3, UiFit.S(compact ? Theme.HeightStatusBarCompact : Theme.HeightStatusBar));
                            SetRowHeight(_logGrid, 0, UiFit.S(_logExpanded ? 36 : (compact ? Theme.HeightLogCollapsedCompact : Theme.HeightLogCollapsed)));
                        }
                    }

                    using (Diag.T("ALM.Visible"))
                    {
                        SetVisible(_subtitle, wideSubtitle);
                        SetVisible(_btnHelp, wideHelp);
                    }

                    if (widthTierChanged || force)
                    {
                        string[] full = { "① 设备模拟器 · 上行", "② 订阅监控台", "③ 控制台 · 下行" };
                        string[] shortNames = { "① 模拟器", "② 监控台", "③ 控制台" };
                        using (Diag.T("ALM.NavText"))
                        {
                            for (int i = 0; i < _navItems.Length; i++)
                            {
                                string want = narrow ? shortNames[i] : full[i];
                                if (_navItems[i].Text != want) _navItems[i].Text = want;
                            }
                        }
                    }

                    using (Diag.T("ALM.Rows"))
                    {
                        SetVisible(_settingsCard, _settingsOpen);
                        // 抽屉里 3 行输入(32+8+32+8+32=112) + 卡片上下 Padding(GapXs×2=8) + 上下 Margin(GapSm×2=16)
                        SetRowHeight(_root, 1, _settingsOpen ? UiFit.S(136) : 0);
                    }
                }
                finally
                {
                    using (Diag.T("ALM.ResumeLayout"))
                    {
                        _root.ResumeLayout(true);
                        ResumeLayout(true);
                    }
                }

                // 顶栏按钮宽度与「文字 / 字体 / DPI」有关, 与窗口尺寸无关。
                // 必须每次都调: 设置抽屉开合会改按钮文案(「设置」↔「收起设置」), 漏算会截字。
                // 之所以不再昂贵: UiFit.TextWidth 已按 (文本,字体,DPI) 缓存测量结果,
                // 且 FitButton/SetWidth 都只在结果真的变化时才写 Width。
                using (Diag.T("ALM.FitTopBar")) FitTopBar();
            }
            finally { _applyingMode = false; }
        }

        /// <summary>只在值真的变化时写 Visible —— Visible 翻转会触发父容器的重新布局。</summary>
        private static void SetVisible(Control? c, bool visible)
        {
            if (c == null || c.IsDisposed) return;
            if (c.Visible != visible) c.Visible = visible;
        }

        /// <summary>只在高度真的变化时写 RowStyle —— 每个非等值写入都会让 TableLayoutPanel 全量重排。</summary>
        private static void SetRowHeight(TableLayoutPanel? t, int index, float height)
        {
            if (t == null || t.IsDisposed) return;
            if (index < 0 || index >= t.RowStyles.Count) return;
            if (Math.Abs(t.RowStyles[index].Height - height) > 0.5f) t.RowStyles[index].Height = height;
        }

        private static void SetColumnWidth(TableLayoutPanel? t, int index, float width)
        {
            if (t == null || t.IsDisposed) return;
            if (index < 0 || index >= t.ColumnStyles.Count) return;
            if (Math.Abs(t.ColumnStyles[index].Width - width) > 0.001f) t.ColumnStyles[index].Width = width;
        }

        /// <summary>宽度只在真的变化时写入, 避免 Layout 事件白跑。</summary>
        private static void SetWidth(Control? c, int width)
        {
            if (c == null || c.IsDisposed) return;
            width = Math.Max(0, width);
            if (c.Width != width) c.Width = width;
        }

        private static void SetHeight(Control? c, int height)
        {
            if (c == null || c.IsDisposed) return;
            height = Math.Max(0, height);
            if (c.Height != height) c.Height = height;
        }

        private static void SetPadding(Control? c, Padding padding)
        {
            if (c == null || c.IsDisposed) return;
            if (c.Padding != padding) c.Padding = padding;
        }

        private void FitTopBar()
        {
            if (!_uiBuilt) return;
            FitButton(_btnConnect, 36, 64);
            FitButton(_btnDisconnect, 36, 64);
            FitButton(_btnSettings, 32, 60);
            FitButton(_btnLog, 32, 60);
            int btnH = UiFit.S(Theme.HeightButton);
            SetHeight(_btnConnect, btnH);
            SetHeight(_btnDisconnect, btnH);
            SetHeight(_btnSettings, btnH);
            SetHeight(_btnLog, btnH);
            int help = UiFit.S(28);
            if (_btnHelp.Width != help || _btnHelp.Height != help) _btnHelp.Size = new Size(help, help);
            FitSettingsFields();
        }

        /// <summary>按钮按文字实测宽度, 但只在结果不同时才写 Width。</summary>
        private static void FitButton(Control? b, int padTotal, int minWidth)
        {
            if (b == null || b.IsDisposed) return;
            int w = UiFit.TextWidth(b.Text, b.Font) + UiFit.Scale(b, padTotal);
            if (minWidth > 0) w = Math.Max(w, UiFit.Scale(b, minWidth));
            SetWidth(b, w);
        }

        private void FitSettingsFields()
        {
            if (_portShell == null) return;
            FitField(_portBox, "65535", 16);
            SetWidth(_portShell, _portBox.Width + UiFit.S(16));
            if (_protoShell != null && _protoCombo != null)
            {
                FitComboWidth(_protoCombo, 26);
                SetWidth(_protoShell, _protoCombo.Width + UiFit.S(16));
            }
        }

        private static void FitField(TextBox? t, string sample, int padTotal)
        {
            if (t == null || t.IsDisposed) return;
            int textW = UiFit.TextWidth(sample, t.Font);
            SetWidth(t, Math.Max(UiFit.Scale(t, 72), textW + UiFit.Scale(t, 8) + UiFit.Scale(t, padTotal)));
        }

        private static void FitComboWidth(ComboBox? cb, int padTotal)
        {
            if (cb == null || cb.IsDisposed) return;
            int max = 0;
            foreach (var item in cb.Items)
            {
                int iw = UiFit.TextWidth(cb.GetItemText(item), cb.Font);
                if (iw > max) max = iw;
            }
            if (cb.Text.Length > 0) max = Math.Max(max, UiFit.TextWidth(cb.Text, cb.Font));
            int arrow = SystemInformation.VerticalScrollBarWidth + UiFit.Scale(cb, 10);
            SetWidth(cb, Math.Max(UiFit.Scale(cb, 54), max + arrow + UiFit.Scale(cb, padTotal)));
        }

        // ==================== 日志抽屉状态 ====================

        private void ApplyLogStateUi()
        {
            if (_logShell == null) return;
            SetVisible(_logShell, _logExpanded);
            SetPadding(_logCard, _logExpanded
                ? new Padding(UiFit.S(Theme.GapMd), UiFit.S(Theme.GapSm), UiFit.S(Theme.GapMd), UiFit.S(Theme.GapSm))
                : new Padding(UiFit.S(Theme.GapMd), 0, UiFit.S(Theme.GapMd), 0));
            SetRowHeight(_logGrid, 0, UiFit.S(_logExpanded ? 36 : (_compact ? Theme.HeightLogCollapsedCompact : Theme.HeightLogCollapsed)));
            string btnText = _logExpanded ? "收起日志" : "日志";
            if (_btnLog.Text != btnText)
            {
                _btnLog.Text = btnText;
                if (_uiBuilt)
                {
                    FitButton(_btnLog, 32, 60);
                    SetHeight(_btnLog, UiFit.S(Theme.HeightButton));
                }
            }
            string statusText = _logExpanded ? "收起日志" : "展开日志";
            if (_statusLogBtn.Text != statusText) _statusLogBtn.Text = statusText;
            if (_logExpanded)
            {
                _logHasError = false;
                SetVisible(_logErrorDot, false);
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
            using var _d = Diag.T("Form.ApplySplitRatio");
            try
            {
                if (_split == null || _split.IsDisposed || _split.Height <= 0) return;
                int usable = _split.Height - _split.SplitterWidth;
                int p1min = UiFit.S(300);

                // 日志区高度在"收起"和"默认展开"下都是固定像素, 只有用户手动拖过分隔条才按比例。
                bool fixedLog = !_logExpanded || !_userSplit;
                int p2fixed = _logExpanded
                    ? UiFit.S(168)
                    : UiFit.S(_compact ? Theme.HeightLogCollapsedCompact : Theme.HeightLogCollapsed);
                int p2min = _logExpanded ? UiFit.S(120) : p2fixed;

                if (usable < p1min + p2min)
                {
                    // 极端窗口: 展开态优先保证主区, 自动回到收起态
                    if (_logExpanded)
                    {
                        _logExpanded = false;
                        ApplyLogStateUi();
                        fixedLog = true;
                        p2fixed = UiFit.S(Theme.HeightLogCollapsedCompact);
                        p2min = p2fixed;
                    }
                    if (usable < p1min + p2min) return;
                }

                if (fixedLog)
                {
                    // 收起态/默认展开态: 日志区高度是死数。
                    // 声明 Panel2 为固定面板后, 容器高度变化完全由 Panel1 吸收,
                    // SplitContainer 自己维护, 我们只在"固定高度本身变了"时赋一次值。
                    // (每帧重算并赋 SplitterDistance 会各自重建两侧子窗口, 实测 77~1000ms)
                    int clamped = Math.Max(p2min, Math.Min(usable - p1min, p2fixed));
                    if (_split.FixedPanel != FixedPanel.Panel2) _split.FixedPanel = FixedPanel.Panel2;

                    // 关键: 判据要取「Panel2 的实际高度」, 而不是「上次设过的常量」。
                    // FixedPanel = Panel2 时, SplitContainer 自己在尺寸变化中维持 Panel2 高度、
                    // 自动移动分隔条 —— 也就是说绝大多数 Resize 根本不需要我们赋值。
                    // 之前用 _appliedP2Fixed 比对, 只要容器高度变了就误判成「需要重设」,
                    // 结果每次 Settle 都白付 86ms(单次 SplitterDistance 赋值要重建两侧子窗口)。
                    int actualP2 = usable - _split.SplitterDistance;

                    // 容差: 实测容器尺寸每变一次, Panel2 实际高度会与目标值差 2~3px
                    // (SplitContainer 内部按像素取整 + 分隔条宽度分摊)。
                    // 3px 的日志栏高度差肉眼不可见, 但为它赋一次 SplitterDistance 要重建两侧
                    // 子窗口(实测 107ms)。加容差后绝大多数 Settle 直接跳过。
                    if (Math.Abs(actualP2 - clamped) > SplitterTolerancePx)
                    {
                        _appliedP2Fixed = clamped;
                        _appliedFixedPanel = FixedPanel.Panel2;
                        int target = usable - clamped;
                        if (_split.SplitterDistance != target) SetSplitterDistance(target);
                    }
                }
                else
                {
                    // 用户拖过: 两侧按比例分配, 同样交回原生处理(比例变了才赋值)
                    if (_split.FixedPanel != FixedPanel.None) _split.FixedPanel = FixedPanel.None;
                    if (_appliedFixedPanel != FixedPanel.None)
                    {
                        _appliedFixedPanel = FixedPanel.None;
                        _appliedP2Fixed = -1;
                    }
                    double ratio = _userSplitRatio;
                    if (Math.Abs(ratio - _appliedRatio) > 0.001)
                    {
                        _appliedRatio = ratio;
                        int targetP2 = (int)Math.Round(usable * (1 - ratio));
                        targetP2 = Math.Max(p2min, Math.Min(usable - p1min, targetP2));
                        int target = usable - targetP2;
                        if (Math.Abs(_split.SplitterDistance - target) > SplitterTolerancePx)
                            SetSplitterDistance(target);
                    }
                }
            }
            catch { /* 极端窗口尺寸下忽略 */ }
        }

        private void SetSplitterDistance(int target)
        {
            _suppressSplitEvent = true;
            try
            {
                using var _t = Diag.T("Form.SetSplitterDistance " + _split.SplitterDistance + "->" + target);
                _split.SplitterDistance = target;
            }
            finally { _suppressSplitEvent = false; }
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
            // 只切「当前页」与「上一页」两个控件的 Visible: 三个 tab 都常驻内容区,
            // 隐藏中的 tab 写 Visible=false 是空操作但依然会走进 Control 的可见性变更流程。
            // 同时不再为每个 NavChip 额外调 Invalidate —— NavChip.Selected 的 setter 内部已按需重绘,
            // 原来的做法等于每次切页把整条导航栏强制重画两遍。
            if (_activeTab == index) { SyncDeviceEchoFor(index); return; }
            _activeTab = index;
            using var _sw = Diag.T("Form.SwitchTab " + index);

            // 三页常驻内容区, 切页只翻 Visible。
            //
            // 为什么不再用 Controls.Clear() 重挂: 重挂会重建整棵控件树, 实测 20~49ms(切页的全部开销),
            // 而且会丢掉焦点/滚动位置。改回常驻 + 下面一条"隐藏页挂起布局"就同时解决了两个问题:
            //   - 隐藏页不参与绘制(Visible=false 时不派发 WM_PAINT);
            //   - 隐藏页不参与布局(在它自己的 Resize 里, 见 SetTabLayoutSuspended)。
            // 原来"隐藏 tab 仍走布局"的根因是 SetBounds → Resize → TableLayoutPanel 重排,
            // 只要隐藏页在 Resize 时直接早退, 这条链就断了, 不需要把控件移出控件树。
            Control? want = index switch { 0 => _simTab, 1 => _monTab, 2 => _ctlTab, _ => null };
            if (want != null)
            {
                using (Diag.T("ST.Visible"))
                {
                    SuspendLayout();
                    _content.SuspendLayout();
                    try
                    {
                        if (_simTab != null) SetTabLayout(_simTab, index == 0, want);
                        if (_monTab != null) SetTabLayout(_monTab, index == 1, want);
                        if (_ctlTab != null) SetTabLayout(_ctlTab, index == 2, want);
                    }
                    finally
                    {
                        _content.ResumeLayout(true);
                        ResumeLayout(true);
                    }
                }
                using (Diag.T("ST.RelayoutTab")) RelayoutActiveTab(want);
            }
            using (Diag.T("ST.Nav"))
            {
                for (int i = 0; i < _navItems.Length; i++) _navItems[i].Selected = i == index;
            }
            SyncDeviceEchoFor(index);
        }

        /// <summary>
        /// 切页: 目标页恢复布局并显示, 其它页挂起布局并隐藏。
        /// 顺序很重要 —— 先挂起旧页再显示新页, 否则中途会各重排一次。
        /// </summary>
        private static void SetTabLayout(Control tab, bool active, Control wanted)
        {
            if (tab == wanted)
            {
                if (!tab.Visible) tab.Visible = true;
            }
            else
            {
                if (tab.Visible) tab.Visible = false;
                SuspendTabLayout(tab);
            }
        }

        /// <summary>隐藏页挂起布局: 让它在尺寸变化时不再做整树重排。</summary>
        private static void SuspendTabLayout(Control tab)
        {
            switch (tab)
            {
                case SimulatorTab sim: sim.LayoutSuspended = true; break;
                case MonitorTab mon: mon.LayoutSuspended = true; break;
                case ControlTab ctl: ctl.LayoutSuspended = true; break;
            }
        }

        /// <summary>
        /// 刚成为当前页的页需要按当前尺寸重算一次响应式档位(它在隐藏期间尺寸可能是旧的)。
        /// 只排入防抖队列, 不在这里同步重排 —— 整树重排要 60~100ms, 同步做会让切页卡这么一下。
        /// </summary>
        private static void RelayoutActiveTab(Control tab)
        {
            switch (tab)
            {
                case SimulatorTab sim: sim.LayoutSuspended = false; sim.RequestSettle(); break;
                case MonitorTab mon: mon.LayoutSuspended = false; mon.RequestSettle(); break;
                case ControlTab ctl: ctl.LayoutSuspended = false; ctl.RequestSettle(); break;
            }
        }

        /// <summary>
        /// 让当前页立刻收敛, 并取消它排队中的防抖重排。
        /// 仅用于 Form 自己刚改完布局(已导致当前页尺寸变化)之后 —— 此时页面已经收到了
        /// Resize 并重启了定时器, 再等 120ms 只是把同一件事重做一遍。
        /// </summary>
        private void SettleActiveTab()
        {
            switch (_activeTab)
            {
                case 0: _simTab.SettleNow(); break;
                case 1: _monTab.SettleNow(); break;
                case 2: _ctlTab.SettleNow(); break;
            }
        }

        private void SyncDeviceEchoFor(int index)
        {
            if (index == 2) SyncDeviceEcho();
        }

        /// <summary>
        /// 诊断/优化用: 把控件树里所有 BackColor=Transparent 换成最近的非透明祖先色。
        /// Transparent 在 WinForms 里不是 alpha 合成, 而是"绘制时向父控件要背景",
        /// 多层嵌套下每次重绘都会沿祖先链逐层回溯, 是改变尺寸时的主要开销来源。
        /// </summary>
        internal static void MakeOpaque(Control root)
        {
            foreach (Control child in root.Controls)
            {
                if (child.BackColor == Color.Transparent)
                {
                    // 先递归处理子级(它们要参考本控件处理后的颜色), 再决定自己的颜色
                    MakeOpaque(child);
                    Color inherited = ResolveOpaqueBackColor(child.Parent);
                    if (inherited != Color.Empty && child.BackColor != inherited) child.BackColor = inherited;
                }
                else
                {
                    MakeOpaque(child);
                }
            }
        }

        private static Color ResolveOpaqueBackColor(Control? c)
        {
            while (c != null)
            {
                if (c.BackColor != Color.Transparent && c.BackColor != Color.Empty) return c.BackColor;
                c = c.Parent;
            }
            return Theme.PageBg;
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
                // 保存位置: settings.json 本身始终留在 %APPDATA%\IotLinkLab(不随保存位置搬家),
                // 否则改一次路径就再也读不回自己的配置。
                if (r.TryGetProperty("saveRoot", out var v8) && v8.ValueKind == JsonValueKind.String)
                {
                    AppPaths.SetRoot(v8.GetString());
                    AppPaths.EnsureAll();   // 自定义目录可能还没建过/盘符换了, 这里立刻补齐
                    _saveDirBox.Text = AppPaths.IsCustom ? AppPaths.Root : "";
                }
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
                    logRatio = _userSplit ? _userSplitRatio : 0,
                    saveRoot = AppPaths.IsCustom ? AppPaths.Root : ""
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
            // 句柄创建后 DPI 才确定, 这里强制重算一次字体宽度相关的顶栏按钮尺寸
            FitTopBar();
            ApplyLayoutMode(force: true);
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
