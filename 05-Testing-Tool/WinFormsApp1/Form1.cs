using System.Text;
using System.Text.Json;

namespace IotLinkLab
{
    /// <summary>
    /// 物联网链路调试台主窗体。
    /// 定位: 通用 MQTT 串口链路调试工具 —— 设备模拟器(上行) / 订阅监控台 / 控制台(下行)。
    /// 内置 "STM32-Greenhouse" 协议预设(帧契约与固件 User/main.c 一致), 也可自定义帧模板复用到其他项目。
    /// </summary>
    public class Form1 : Form
    {
        private readonly MqttLink _link = new();
        private readonly SimulatorTab _simTab = new();
        private readonly MonitorTab _monTab = new();
        private readonly ControlTab _ctlTab = new();

        private Panel _banner = null!;
        private TextBox _hostBox = null!, _portBox = null!, _dataTopicBox = null!, _controlTopicBox = null!;
        private ComboBox _protoCombo = null!;
        private RoundedButton _btnConnect = null!, _btnDisconnect = null!;
        private StatusChip _chip = null!;
        private Panel _content = null!;
        private RichTextBox _logBox = null!;
        private SplitContainer _split = null!;
        private Label _statLabel = null!;
        private readonly NavChip[] _navItems = new NavChip[3];
        private int _activeTab;

        private string AppDataDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "IotLinkLab");

        public Form1()
        {
            Text = "物联网链路调试台 · IoT Link Lab v1.1";
            // 尺寸按屏幕可用区域收敛: 高分屏 150% 缩放下逻辑分辨率只有 ~1280x720,
            // 写死 1360x880 会超出屏幕被系统裁剪, 造成内容"看起来"重叠/截断。
            Rectangle wa = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
            int w = Math.Min(1360, wa.Width - 24);
            int h = Math.Min(880, wa.Height - 24);
            MinimumSize = new Size(Math.Min(1080, wa.Width - 24), Math.Min(640, wa.Height - 24));
            Size = new Size(w, h);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.PageBg;
            DoubleBuffered = true;
            Font = Theme.Ui(9F);

            BuildUi();
            WireEvents();
            LoadSettings();
            UpdateDataStat();

            // 快捷键: F1/F2/F3 切页 / F5 连接 / F6 断开 / F7 查看日志 / F8 导出数据 / F11 最大化
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
                    SaveSettings();
                }
                else if (e.KeyCode == Keys.F7) { e.Handled = true; ShowLogFile(); }
                else if (e.KeyCode == Keys.F8) { e.Handled = true; ExportData(); }
            };

            Shown += (s, e) =>
            {
                ApplySplitRatio();

                Log("物联网链路调试台就绪。内置协议预设: STM32-Greenhouse(固件 V1.2 契约) + 自定义模板。", Theme.TextSecondary);
                Log($"运行日志: {LogStore.CurrentFile}", Theme.TextTertiary);
            };

            // 窗口尺寸变化时按比例重算分隔位置, 否则缩小时日志区会被整个挤出可视区域
            Resize += (s, e) => ApplySplitRatio();
        }

        // ==================== UI 构建 ====================

        private void BuildUi()
        {
            AppPaths.EnsureAll();

            // banner 高度按「标题+副标题+图标」三者实测计算, 任何 DPI 下都不裁字
            using (var titleFont = Theme.Ui(15F, bold: true))
            using (var subFont = Theme.Ui(8.5F))
            {
                int textH = TextRenderer.MeasureText("物联网链路调试台", titleFont).Height
                          + 3
                          + TextRenderer.MeasureText("串口链路模拟 · MQTT 上行/下行 · 协议可复用", subFont).Height;
                _bannerH = Math.Max(textH + 34, 78);
            }

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1, BackColor = Theme.PageBg,
                Margin = new Padding(0), Padding = new Padding(0)
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, _bannerH));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, _connCardH));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));   // 导航条
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // 内容 + 日志(可拖动)

            // ---------- 1. 顶部横幅 ----------
            _banner = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Primary, Margin = new Padding(0) };
            _banner.Paint += Banner_Paint;
            root.Controls.Add(_banner, 0, 0);

            // ---------- 2. 连接配置卡(两行: 连接参数 / 主题) ----------
            var connCard = new Card { Dock = DockStyle.Fill, Padding = new Padding(14, 10, 14, 10), Margin = new Padding(12, 10, 12, 4) };
            var connGrid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1,
                BackColor = Color.Transparent, Margin = new Padding(0)
            };
            connGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
            connGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
            var connRow = NewRow();
            var topicRow = NewRow();

            // 服务器 / 端口 / 协议 / 按钮 / 状态
            _hostBox = new TextBox { Width = 132, Font = Theme.Mono(9.5F), Text = "127.0.0.1" };
            AddField(connRow, "服务器", _hostBox, first: true);

            _portBox = new TextBox { Width = 66, Font = Theme.Mono(9.5F), Text = "1883" };
            AddField(connRow, "端口", _portBox);

            _protoCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList, Font = Theme.Ui(9F), ItemHeight = 22,
                Margin = new Padding(0, 2, 0, 0)
            };
            _protoCombo.Items.AddRange(new object[] { "TCP", "WebSocket" });
            _protoCombo.SelectedIndex = 0;
            // ★ 关键: 按最长项("WebSocket")实测宽度, 而不是写死 92 —— 根治下拉框裁字
            UiFit.FitCombo(_protoCombo, padTotal: 26);
            _protoCombo.SelectedIndexChanged += (s, e) =>
            {
                if (_portBox.Text == "1883" && _protoCombo.SelectedIndex == 1) _portBox.Text = "8083";
                else if (_portBox.Text == "8083" && _protoCombo.SelectedIndex == 0) _portBox.Text = "1883";
            };
            AddLabel(connRow, "协议");
            connRow.Controls.Add(_protoCombo);

            _btnConnect = new RoundedButton { Text = "连接", FillColor = Theme.Primary, Margin = new Padding(20, 0, 0, 0) };
            UiFit.SizeButton(_btnConnect, padTotal: 40, minWidth: 80);
            _btnDisconnect = new RoundedButton { Text = "断开", FillColor = Theme.Danger, Enabled = false, Margin = new Padding(8, 0, 0, 0) };
            UiFit.SizeButton(_btnDisconnect, padTotal: 40, minWidth: 80);
            connRow.Controls.Add(_btnConnect);
            connRow.Controls.Add(_btnDisconnect);

            _chip = new StatusChip { Margin = new Padding(16, 3, 0, 0) };
            _chip.SetState("未连接", Theme.TextTertiary);
            connRow.Controls.Add(_chip);

            // 主题行
            _dataTopicBox = new TextBox { Width = 210, Font = Theme.Mono(9.5F), Text = "green/data" };
            AddField(topicRow, "上行主题", _dataTopicBox, first: true);
            _controlTopicBox = new TextBox { Width = 210, Font = Theme.Mono(9.5F), Text = "green/control" };
            AddField(topicRow, "下行主题", _controlTopicBox);

            connGrid.Controls.Add(connRow, 0, 0);
            connGrid.Controls.Add(topicRow, 0, 1);
            connCard.Controls.Add(connGrid);
            root.Controls.Add(connCard, 0, 1);

            // ---------- 3. 导航 chips ----------
            var nav = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, BackColor = Color.Transparent, WrapContents = false,
                Padding = new Padding(16, 6, 0, 0), Margin = new Padding(0)
            };
            string[] names = { "① 设备模拟器 · 上行", "② 订阅监控台", "③ 控制台 · 下行" };
            for (int i = 0; i < names.Length; i++)
            {
                var chip = new NavChip { Text = names[i], Margin = new Padding(0, 0, 10, 0) };
                int idx = i;
                chip.Click += (s, e) => SwitchTab(idx);
                _navItems[i] = chip;
                nav.Controls.Add(chip);
            }
            nav.Controls.Add(new Label
            {
                Text = "F1/F2/F3 切换页面   F4 发一帧   F5 连接   F6 断开   F7 日志   F8 导出   F11 最大化",
                AutoSize = true, Font = Theme.Ui(8.5F), ForeColor = Theme.TextTertiary,
                Margin = new Padding(14, 9, 0, 0)
            });
            root.Controls.Add(nav, 0, 2);

            // ---------- 4. 内容 + 日志(SplitContainer, 可拖动分隔) ----------
            _split = new SplitContainer
            {
                Dock = DockStyle.Fill, Orientation = Orientation.Horizontal,
                SplitterWidth = 8, BackColor = Theme.PageBg, Panel1MinSize = 240, Panel2MinSize = 110,
                Margin = new Padding(0)
            };
            _split.Panel1.BackColor = Theme.PageBg;
            _split.Panel2.BackColor = Theme.PageBg;

            _content = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 0, 12, 0) };
            _simTab.Dock = DockStyle.Fill;
            _monTab.Dock = DockStyle.Fill;
            _ctlTab.Dock = DockStyle.Fill;
            _content.Controls.Add(_simTab);
            _content.Controls.Add(_ctlTab);
            _content.Controls.Add(_monTab);
            _split.Panel1.Controls.Add(_content);

            _split.Panel2.Controls.Add(BuildLogCard());
            root.Controls.Add(_split, 0, 3);

            Controls.Add(root);
            SwitchTab(0);
        }

        private int _bannerH = 84;
        private int _connCardH = 108;

        private static FlowLayoutPanel NewRow() => new()
        {
            Dock = DockStyle.Fill, BackColor = Color.Transparent, WrapContents = false, Margin = new Padding(0)
        };

        /// <summary>表单标签: 上下留出呼吸空间, 不再贴脸。</summary>
        private static void AddLabel(FlowLayoutPanel row, string text)
            => row.Controls.Add(new Label
            {
                Text = text, AutoSize = true, Font = Theme.Ui(9F),
                ForeColor = Theme.TextSecondary, Margin = new Padding(22, 9, 7, 0)
            });

        private static void AddField(FlowLayoutPanel row, string label, TextBox box, bool first = false)
        {
            row.Controls.Add(new Label
            {
                Text = label, AutoSize = true, Font = Theme.Ui(9F),
                ForeColor = Theme.TextSecondary,
                Margin = first ? new Padding(0, 9, 7, 0) : new Padding(22, 9, 7, 0)
            });
            row.Controls.Add(box);
        }

        /// <summary>日志卡: 头部三个动作按钮 + 自适应高度的日志正文。</summary>
        private Control BuildLogCard()
        {
            var card = new Card { Dock = DockStyle.Fill, Padding = new Padding(12, 8, 12, 10), Margin = new Padding(12, 6, 12, 12) };

            var grid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1,
                BackColor = Color.Transparent, Margin = new Padding(0)
            };
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var head = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, BackColor = Color.Transparent, WrapContents = false, Margin = new Padding(0)
            };
            head.Controls.Add(new Label
            {
                Text = "运行日志", AutoSize = true, Font = Theme.Ui(9.5F, bold: true),
                ForeColor = Theme.TextSecondary, Margin = new Padding(0, 7, 14, 0)
            });

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
            head.Controls.Add(btnView);
            head.Controls.Add(btnExport);
            head.Controls.Add(btnCopy);
            head.Controls.Add(btnClear);

            _statLabel = new Label
            {
                Text = "已记录 0 条", AutoSize = true, Font = Theme.Ui(8.5F),
                ForeColor = Theme.TextTertiary, Margin = new Padding(14, 8, 0, 0)
            };
            head.Controls.Add(_statLabel);

            _logBox = new RichTextBox
            {
                Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
                ScrollBars = RichTextBoxScrollBars.Vertical,
                BackColor = Theme.FieldBg, Font = Theme.Mono(8.75F),
                BorderStyle = BorderStyle.None, DetectUrls = false, WordWrap = true,
                Margin = new Padding(0)
            };

            grid.Controls.Add(head, 0, 0);
            grid.Controls.Add(_logBox, 0, 1);
            card.Controls.Add(grid);
            return card;
        }

        private static RoundedButton NewSmallButton(string text, Color color, bool outline)
        {
            var b = new RoundedButton
            {
                Text = text,
                Font = Theme.Ui(8.75F, bold: !outline),
                Height = 26,
                FillColor = color,
                TextColor = outline ? color : Color.White,
                Outline = outline,
                Margin = new Padding(0, 4, 8, 0)
            };
            UiFit.SizeButton(b, padTotal: 30, minWidth: 56);
            return b;
        }

        private void Banner_Paint(object? sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var full = _banner.ClientRectangle;

            string path = Path.Combine(AppContext.BaseDirectory, "assets", "banner.jpg");
            if (File.Exists(path))
            {
                try
                {
                    using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                    using var ms = new MemoryStream();
                    fs.CopyTo(ms); ms.Position = 0;
                    using var img = Image.FromStream(ms);
                    // 等比铺满(cover)裁剪: 先在缩放假想画布上算居中偏移, 再除回原图比例.
                    // ★ 偏移量与裁剪宽高必须换算回「原图像素」, 否则当窗口比图宽时
                    //   srcWidth 会超出图宽, GDI+ 只画到边缘 → 横幅右侧露出纯色底。
                    float scale = Math.Max((float)full.Width / img.Width, (float)full.Height / img.Height);
                    var src = new RectangleF(
                        (img.Width - full.Width / scale) / 2f,
                        (img.Height - full.Height / scale) / 2f,
                        full.Width / scale,
                        full.Height / scale);
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.DrawImage(img, full, src.X, src.Y, src.Width, src.Height, GraphicsUnit.Pixel);
                }
                catch { /* 背景缺失走纯色底 */ }
            }

            using (var scrim = new System.Drawing.Drawing2D.LinearGradientBrush(full,
                Color.FromArgb(190, 10, 44, 28), Color.FromArgb(96, 10, 52, 32), 0f))
            {
                g.FillRectangle(scrim, full);
            }

            // 左侧应用标识(与 exe 图标同一素材)
            int pad = 18;
            int markBox = Math.Min(full.Height - 20, 48);
            int textLeft = pad;
            if (markBox >= 28)
            {
                Image? mark = LoadImage("app-mark.png");
                if (mark != null)
                {
                    using (mark)
                    {
                        var dst = new Rectangle(pad, (full.Height - markBox) / 2, markBox, markBox);
                        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        g.DrawImage(mark, dst);
                    }
                    textLeft = pad + markBox + 14;
                }
            }

            using var titleFont = Theme.Ui(15F, bold: true);
            using var subFont = Theme.Ui(8.5F);
            int titleH = TextRenderer.MeasureText("物联网链路调试台", titleFont).Height;
            int subH = TextRenderer.MeasureText("串口链路模拟 · MQTT 上行/下行 · 协议可复用", subFont).Height;
            int blockH = titleH + 3 + subH;
            int top = Math.Max(6, (full.Height - blockH) / 2);
            int textW = Math.Max(10, full.Width - textLeft - 20);

            TextRenderer.DrawText(g, "物联网链路调试台", titleFont,
                new Rectangle(textLeft, top, textW, titleH), Color.White,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, "串口链路模拟 · MQTT 上行/下行 · 协议可复用", subFont,
                new Rectangle(textLeft, top + titleH + 3, textW, subH), Color.FromArgb(210, 255, 255, 255),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
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

        private void SwitchTab(int index)
        {
            _activeTab = index;
            _simTab.Visible = index == 0;
            _monTab.Visible = index == 1;
            _ctlTab.Visible = index == 2;
            foreach (var chip in _navItems) chip.Invalidate();
        }

        /// <summary>
        /// 内容区 : 日志区 = 72% : 28%。窗口改尺寸时也要重算,
        /// 否则缩窗后日志区会被压到不可见(用户原话「浪费空间」的另一面: 该看到的看不到)。
        /// </summary>
        private void ApplySplitRatio()
        {
            try
            {
                int usable = _split.Height - _split.SplitterWidth;
                if (usable < _split.Panel1MinSize + _split.Panel2MinSize) return;
                int target = (int)(usable * 0.72);
                target = Math.Max(_split.Panel1MinSize, Math.Min(usable - _split.Panel2MinSize, target));
                if (_split.SplitterDistance != target) _split.SplitterDistance = target;
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

            _link.StateChanged += (state, detail) => SafeInvoke(() =>
            {
                switch (state)
                {
                    case "connecting": _chip.SetState("连接中", Theme.Warning); _btnConnect.Enabled = false; break;
                    case "connected":
                        _chip.SetState("已连接 " + detail, Theme.Success);
                        _btnConnect.Enabled = false; _btnDisconnect.Enabled = true;
                        Log($"MQTT 已连接: {_hostBox.Text}:{_portBox.Text} ({_protoCombo.Text})", Theme.LogOk);
                        _ = SubscribeDefaultsAsync();
                        break;
                    case "reconnecting": _chip.SetState(detail, Theme.Warning); break;
                    case "disconnected":
                        _chip.SetState("未连接", Theme.TextTertiary);
                        _btnConnect.Enabled = true; _btnDisconnect.Enabled = false;
                        Log("MQTT 已断开", Theme.LogWarn);
                        break;
                    case "failed": _chip.SetState("失败", Theme.Danger); _btnConnect.Enabled = true; _btnDisconnect.Enabled = false; break;
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
                }
                catch (Exception ex) { Log("发布失败: " + ex.Message, Theme.LogErr); }
            });

            _ctlTab.Log += msg => SafeInvoke(() => Log(msg, Theme.TextSecondary));
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
                if (_statLabel == null) return;
                int n = DataStore.Count;
                _statLabel.Text = n == 0 ? "已记录 0 条" : $"已记录 {n} 条数据 · 可导出";
                _statLabel.ForeColor = n == 0 ? Theme.TextTertiary : Theme.Teal;
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
            });
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
                    controlTopic = _controlTopicBox.Text.Trim()
                };
                File.WriteAllText(Path.Combine(AppDataDir, "settings.json"),
                    JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true }),
                    new UTF8Encoding(false));
            }
            catch { /* 设置写失败不阻断 */ }
        }

        protected override async void OnFormClosing(FormClosingEventArgs e)
        {
            try
            {
                _simTab.Shutdown();
                await _link.DisposeAsync();
            }
            catch { }
            base.OnFormClosing(e);
        }
    }
}
