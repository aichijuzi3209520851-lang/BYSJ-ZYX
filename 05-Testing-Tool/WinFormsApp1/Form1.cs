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
        private readonly NavChip[] _navItems = new NavChip[3];
        private int _activeTab;

        private string AppDataDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "IotLinkLab");

        public Form1()
        {
            Text = "物联网链路调试台 · IoT Link Lab v1.0";
            MinimumSize = new Size(1180, 800);
            Size = new Size(1360, 880);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.PageBg;
            DoubleBuffered = true;
            Font = Theme.Ui(9F);

            BuildUi();
            WireEvents();
            LoadSettings();
            Shown += (s, e) => Log("物联网链路调试台就绪。内置协议预设: STM32-Greenhouse(固件 V1.2 契约) + 自定义模板。", Theme.TextSecondary);
        }

        // ==================== UI 构建 ====================

        private void BuildUi()
        {
            // banner 高度按字体实测计算(标题+副标题+呼吸空间), 任何 DPI 下都不裁字
            int bannerH = TextRenderer.MeasureText("物联网链路调试台", Theme.Ui(15F, bold: true)).Height
                        + TextRenderer.MeasureText("串口链路模拟 · MQTT 上行/下行 · 协议可复用", Theme.Ui(8.5F)).Height + 30;

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 5, BackColor = Theme.PageBg };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, bannerH));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));    // 连接卡两行
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 120));   // 日志

            // ---------- 1. 顶部横幅 ----------
            _banner = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Primary };
            _banner.Paint += Banner_Paint;
            root.Controls.Add(_banner, 0, 0);

            // ---------- 2. 连接配置卡(两行: 连接参数 / 数据与控制主题), 避免单行超宽被窗缘截断 ----------
            var connCard = new Card { Dock = DockStyle.Fill, Padding = new Padding(10, 6, 10, 6) };
            var connGrid = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, BackColor = Color.Transparent, AutoSize = true };
            connGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
            connGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
            var connRow = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, WrapContents = false, Margin = new Padding(0) };
            var topicRow = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, WrapContents = false, Margin = new Padding(0) };

            AddField(connRow, "服务器", _hostBox = new TextBox { Width = 130, Font = Theme.Mono(9.5F), Text = "127.0.0.1" });
            AddField(connRow, "端口", _portBox = new TextBox { Width = 64, Font = Theme.Mono(9.5F), Text = "1883" });
            _protoCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 92, Font = Theme.Ui(9F), ItemHeight = 20 };
            _protoCombo.Items.AddRange(new object[] { "TCP", "WebSocket" });
            _protoCombo.SelectedIndex = 0;
            _protoCombo.SelectedIndexChanged += (s, e) =>
            {
                if (_portBox.Text == "1883" && _protoCombo.SelectedIndex == 1) _portBox.Text = "8083";
                else if (_portBox.Text == "8083" && _protoCombo.SelectedIndex == 0) _portBox.Text = "1883";
            };
            connRow.Controls.Add(new Label { Text = "协议", AutoSize = true, Font = Theme.Ui(9F), ForeColor = Theme.TextSecondary, Margin = new Padding(8, 10, 4, 0) });
            connRow.Controls.Add(_protoCombo);
            _btnConnect = new RoundedButton { Text = "连接", Width = 76, FillColor = Theme.Primary, Margin = new Padding(8, 3, 0, 0) };
            _btnDisconnect = new RoundedButton { Text = "断开", Width = 76, FillColor = Theme.Danger, Enabled = false, Margin = new Padding(6, 3, 0, 0) };
            connRow.Controls.Add(_btnConnect);
            connRow.Controls.Add(_btnDisconnect);
            _chip = new StatusChip { Margin = new Padding(10, 6, 0, 0) };
            _chip.SetState("● 未连接", Theme.TextTertiary);
            connRow.Controls.Add(_chip);

            AddField(topicRow, "数据主题(上行发布)", _dataTopicBox = new TextBox { Width = 200, Font = Theme.Mono(9.5F), Text = "green/data" });
            AddField(topicRow, "控制主题(下行)", _controlTopicBox = new TextBox { Width = 200, Font = Theme.Mono(9.5F), Text = "green/control" });

            connGrid.Controls.Add(connRow, 0, 0);
            connGrid.Controls.Add(topicRow, 0, 1);
            connCard.Controls.Add(connGrid);
            root.Controls.Add(connCard, 0, 1);

            // ---------- 3. 左侧导航 chips ----------
            var nav = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, WrapContents = false, Padding = new Padding(12, 7, 0, 0) };
            string[] names = { "① 设备模拟器(上行)", "② 订阅监控台", "③ 控制台(下行)" };
            for (int i = 0; i < names.Length; i++)
            {
                var chip = new NavChip
                {
                    Text = names[i],
                    Margin = new Padding(0, 0, 8, 0)
                };
                int idx = i;
                chip.Click += (s, e) => SwitchTab(idx);
                _navItems[i] = chip;
                nav.Controls.Add(chip);
            }
            root.Controls.Add(nav, 0, 2);

            // ---------- 4. 内容区(三页) ----------
            _content = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 4, 10, 4) };
            _simTab.Dock = DockStyle.Fill;
            _monTab.Dock = DockStyle.Fill;
            _ctlTab.Dock = DockStyle.Fill;
            _content.Controls.Add(_simTab);
            _content.Controls.Add(_ctlTab);
            _content.Controls.Add(_monTab);
            root.Controls.Add(_content, 0, 3);

            // ---------- 5. 日志 ----------
            var logCard = new Card { Dock = DockStyle.Fill, Padding = new Padding(8) };
            _logBox = new RichTextBox
            {
                Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = RichTextBoxScrollBars.Vertical,
                BackColor = Theme.FieldBg, Font = Theme.Mono(8.75F), BorderStyle = BorderStyle.None
            };
            logCard.Controls.Add(_logBox);
            root.Controls.Add(logCard, 0, 4);

            Controls.Add(root);
            SwitchTab(0);
        }

        private static void AddField(FlowLayoutPanel row, string label, TextBox box)
        {
            row.Controls.Add(new Label { Text = label, AutoSize = true, Font = Theme.Ui(9F), ForeColor = Theme.TextSecondary, Margin = new Padding(10, 10, 4, 0) });
            row.Controls.Add(box);
        }

        private void Banner_Paint(object? sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            string path = Path.Combine(AppContext.BaseDirectory, "assets", "banner.jpg");
            if (File.Exists(path))
            {
                try
                {
                    using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                    using var ms = new MemoryStream();
                    fs.CopyTo(ms); ms.Position = 0;
                    using var img = System.Drawing.Image.FromStream(ms);
                    var full = _banner.ClientRectangle;
                    float scale = Math.Max((float)full.Width / img.Width, (float)full.Height / img.Height);
                    int w = (int)(img.Width * scale), h = (int)(img.Height * scale);
                    g.DrawImage(img, full, (w - full.Width) / 2f, (h - full.Height) / 2f, full.Width, full.Height, GraphicsUnit.Pixel);
                }
                catch { /* 背景缺失走纯色底 */ }
            }
            using (var scrim = new System.Drawing.Drawing2D.LinearGradientBrush(_banner.ClientRectangle,
                Color.FromArgb(168, 10, 44, 28), Color.FromArgb(90, 10, 52, 32), 0f))
            {
                g.FillRectangle(scrim, _banner.ClientRectangle);
            }

            // 标题+副标题按实测字体高度堆叠并整体垂直居中(DPI 安全, 不用绝对坐标)
            var bannerRect = _banner.ClientRectangle;
            using (var titleFont = Theme.Ui(15F, bold: true))
            using (var subFont = Theme.Ui(8.5F))
            {
                int titleH = TextRenderer.MeasureText("物联网链路调试台", titleFont).Height;
                int subH = TextRenderer.MeasureText("串口链路模拟 · MQTT 上行/下行 · 协议可复用", subFont).Height;
                int blockH = titleH + 2 + subH;
                int top = Math.Max(4, (bannerRect.Height - blockH) / 2);
                TextRenderer.DrawText(g, "物联网链路调试台", titleFont,
                    new Rectangle(18, top, bannerRect.Width - 36, titleH), Color.White,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
                TextRenderer.DrawText(g, "串口链路模拟 · MQTT 上行/下行 · 协议可复用", subFont,
                    new Rectangle(20, top + titleH + 2, bannerRect.Width - 40, subH), Color.FromArgb(205, 255, 255, 255),
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            }
        }

        private void SwitchTab(int index)
        {
            _activeTab = index;
            _simTab.Visible = index == 0;
            _monTab.Visible = index == 1;
            _ctlTab.Visible = index == 2;
            foreach (var chip in _navItems) chip.Invalidate();
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
                    case "connecting": _chip.SetState("● 连接中", Theme.Warning); _btnConnect.Enabled = false; break;
                    case "connected":
                        _chip.SetState("● 已连接 " + detail, Theme.Success);
                        _btnConnect.Enabled = false; _btnDisconnect.Enabled = true;
                        Log($"MQTT 已连接: {_hostBox.Text}:{_portBox.Text} ({_protoCombo.Text})", Theme.LogOk);
                        _ = SubscribeDefaultsAsync();
                        break;
                    case "reconnecting": _chip.SetState("● " + detail, Theme.Warning); break;
                    case "disconnected":
                        _chip.SetState("● 未连接", Theme.TextTertiary);
                        _btnConnect.Enabled = true; _btnDisconnect.Enabled = false;
                        Log("MQTT 已断开", Theme.LogWarn);
                        break;
                    case "failed": _chip.SetState("● 失败", Theme.Danger); _btnConnect.Enabled = true; _btnDisconnect.Enabled = false; break;
                }
            });

            _link.Log += msg => SafeInvoke(() => Log(msg, Theme.TextSecondary));
            _link.PayloadReceived += (topic, payload) => SafeInvoke(() =>
            {
                _monTab.OnMessage(topic, payload);
                if (topic == _controlTopicBox.Text.Trim() && topic.Length > 0)
                {
                    Log($"[下行] {topic} <- {payload.Trim()}", Theme.LogWarn);
                    _simTab.HandleControlCommand(payload.Trim());
                }
            });

            _simTab.FrameProduced += (frame, fd, json) => SafeInvoke(async () =>
            {
                string topic = _dataTopicBox.Text.Trim();
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

            _ctlTab.PublishRequested += (topic, payload, qos1) => SafeInvoke(async () =>
            {
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
                _chip.SetState("● 失败", Theme.Danger);
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

        // ==================== 日志 / 线程 / 设置 ====================

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

        private void Log(string message, Color color)
        {
            SafeInvoke(() =>
            {
                _logBox.SelectionStart = _logBox.TextLength;
                _logBox.SelectionLength = 0;
                _logBox.SelectionColor = Theme.TextTertiary;
                _logBox.AppendText($"[{DateTime.Now:HH:mm:ss.fff}] ");
                _logBox.SelectionColor = color;
                _logBox.AppendText(message + Environment.NewLine);
                if (++_logCount > 800)
                {
                    _logCount = 400;
                    string text = _logBox.Text;
                    int cut = text.Length / 2;
                    int nl = text.IndexOf('\n', cut);
                    _logBox.Text = nl >= 0 ? text.Substring(nl + 1) : text.Substring(cut);
                }
                _logBox.SelectionStart = _logBox.TextLength;
                _logBox.ScrollToCaret();
            });
        }

        private void LoadSettings()
        {
            try
            {
                string file = Path.Combine(AppDataDir, "settings.json");
                if (!File.Exists(file)) return;
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                var r = doc.RootElement;
                if (r.TryGetProperty("host", out var v)) _hostBox.Text = v.GetString() ?? _hostBox.Text;
                if (r.TryGetProperty("port", out var v2)) _portBox.Text = v2.GetString() ?? _portBox.Text;
                if (r.TryGetProperty("useWs", out var v3) && v3.GetBoolean()) _protoCombo.SelectedIndex = 1;
                if (r.TryGetProperty("dataTopic", out var v4)) _dataTopicBox.Text = v4.GetString() ?? _dataTopicBox.Text;
                if (r.TryGetProperty("controlTopic", out var v5)) _controlTopicBox.Text = v5.GetString() ?? _controlTopicBox.Text;
            }
            catch { /* 设置损坏时用默认值 */ }
        }

        private void SaveSettings()
        {
            try
            {
                Directory.CreateDirectory(AppDataDir);
                var obj = new
                {
                    host = _hostBox.Text.Trim(),
                    port = _portBox.Text.Trim(),
                    useWs = _protoCombo.SelectedIndex == 1,
                    dataTopic = _dataTopicBox.Text.Trim(),
                    controlTopic = _controlTopicBox.Text.Trim()
                };
                File.WriteAllText(Path.Combine(AppDataDir, "settings.json"),
                    JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true }));
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
