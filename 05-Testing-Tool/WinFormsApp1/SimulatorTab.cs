using System.IO.Ports;
using System.Text;

namespace IotLinkLab
{
    /// <summary>
    /// 设备模拟器(上行链路): 按固件协议模板生成帧 → 解析预览 → 构建同契约 JSON。
    /// 场景预设/手动编辑/定时轮发(可加抖动)/可选真实串口输出(配合虚拟串口对时,
    /// 让真实上位机走完整"串口→解析→发布"链路)。
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

        private System.Windows.Forms.Timer _timer = null!;
        private readonly Random _rnd = new();

        /// <summary>固件侧状态(被控制台回显指令更新; 自动模式下设备指令被固件忽略)。</summary>
        public string ServoState = "OFF";
        public string MotorState = "OFF";
        public string ModeState = "AUTO";

        /// <summary>(帧文本, 解析结果, JSON)——Form 层负责发布。</summary>
        public event Action<string, FrameData, string>? FrameProduced;
        public event Action<string>? Log;

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

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Color.Transparent };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46f));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 54f));

            // ---------- 左列: 参数与发送 ----------
            var left = new Card { Dock = DockStyle.Fill, Padding = new Padding(Theme.Pad) };
            var leftFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Color.Transparent };

            leftFlow.Controls.Add(MakeSection("场景预设"));
            _sceneCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300, Font = Theme.Ui(9.5F), ItemHeight = 22 };
            foreach (var (key, _, _, _, _, _, _) in FrameCodec.Scenes) _sceneCombo.Items.Add(key);
            _sceneCombo.SelectedIndexChanged += (s, e) => ApplyScene(Math.Max(0, _sceneCombo.SelectedIndex));
            leftFlow.Controls.Add(_sceneCombo);

            leftFlow.Controls.Add(MakeSection("基础数值(场景选定后可微调)"));
            var grid = new TableLayoutPanel { Width = 340, Height = 104, ColumnCount = 4, RowCount = 2, BackColor = Color.Transparent, Margin = new Padding(0, 2, 0, 2) };
            for (int i = 0; i < 4; i++) grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
            (_numTemp, _) = MakeNum("温度°C", 0, 100, 24, 0, grid, 0, 0);
            (_numHumi, _) = MakeNum("湿度%RH", 0, 100, 62, 0, grid, 1, 0);
            (_numSoil, _) = MakeNum("土壤%", 0, 100, 35, 0, grid, 2, 0);
            (_numLight, _) = MakeNum("光照%", 0, 100, 72, 0, grid, 3, 0);
            (_numRaw, _) = MakeNum("raw值", 0, 4095, 2080, 0, grid, 0, 1);
            _numRaw.Width -= 20;
            leftFlow.Controls.Add(grid);

            leftFlow.Controls.Add(MakeSection("帧模板(占位符: {temp} {humi} {soil} {raw} {light} {servo} {motor} {mode} {air} {warning})"));
            _templateBox = new TextBox { Multiline = true, Height = 56, Width = 320, Dock = DockStyle.None, Font = Theme.Mono(8.5F), Text = FrameCodec.DefaultTemplate };
            leftFlow.Controls.Add(_templateBox);

            leftFlow.Controls.Add(MakeSection("发送控制"));
            var sendRow = new FlowLayoutPanel { Width = 320, Height = 40, BackColor = Color.Transparent, Margin = new Padding(0, 2, 0, 0) };
            sendRow.Controls.Add(new Label { Text = "周期", AutoSize = true, Font = Theme.Ui(9F), Margin = new Padding(3, 8, 4, 0) });
            _numInterval = new NumericUpDown { Minimum = 1, Maximum = 60, Value = 1.5m, Increment = 0.5m, Width = 62, DecimalPlaces = 1, Font = Theme.Ui(9F) };
            sendRow.Controls.Add(_numInterval);
            sendRow.Controls.Add(new Label { Text = "s", AutoSize = true, Font = Theme.Ui(9F), Margin = new Padding(0, 8, 10, 0) });
            _chkJitter = new CheckBox { Text = "数值抖动", AutoSize = true, Font = Theme.Ui(9F), Checked = false, Margin = new Padding(0, 8, 8, 0) };
            sendRow.Controls.Add(_chkJitter);
            _numJitter = new NumericUpDown { Minimum = 0, Maximum = 20, Value = 2, Width = 48, Font = Theme.Ui(9F) };
            sendRow.Controls.Add(_numJitter);
            leftFlow.Controls.Add(sendRow);

            var btnRow = new FlowLayoutPanel { Width = 320, Height = 40, BackColor = Color.Transparent, Margin = new Padding(0, 2, 0, 0) };
            _btnOnce = new RoundedButton { Text = "手动发一帧", Width = 110, FillColor = Theme.Teal };
            _btnOnce.Click += (s, e) => EmitFrame("手动");
            _btnStart = new RoundedButton { Text = "开始定时发送", Width = 130, FillColor = Theme.Primary };
            _btnStart.Click += BtnStart_Click;
            btnRow.Controls.Add(_btnOnce); btnRow.Controls.Add(_btnStart);
            leftFlow.Controls.Add(btnRow);

            leftFlow.Controls.Add(MakeSection("串口输出(可选, 配合虚拟串口对让真实上位机走完整链路)"));
            var serRow = new FlowLayoutPanel { Width = 320, Height = 40, BackColor = Color.Transparent, Margin = new Padding(0, 2, 0, 0) };
            _chkSerial = new CheckBox { Text = "同步写串口", AutoSize = true, Font = Theme.Ui(9F), Margin = new Padding(0, 8, 8, 0) };
            _comPort = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90, Font = Theme.Ui(9F) };
            _comBaud = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 80, Font = Theme.Ui(9F) };
            _comBaud.Items.AddRange(new object[] { "9600", "19200", "115200" });
            _comBaud.SelectedIndex = 0;
            serRow.Controls.Add(_chkSerial); serRow.Controls.Add(_comPort); serRow.Controls.Add(_comBaud);
            leftFlow.Controls.Add(serRow);

            left.Controls.Add(leftFlow);

            // ---------- 右列: 三层预览 ----------
            var right = new Card { Dock = DockStyle.Fill, Padding = new Padding(Theme.Pad) };
            var rightLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, BackColor = Color.Transparent };
            rightLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            rightLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 34f));
            rightLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 22f));
            rightLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 44f));

            rightLayout.Controls.Add(MakeSection("① 固件帧文本(串口视角)"), 0, 0);
            _framePreview = MakePreview(Theme.Mono(9.5F));
            rightLayout.Controls.Add(_framePreview, 0, 1);
            rightLayout.Controls.Add(MakeSection("② 解析结果(与上位机同一套正则)"), 0, 2);
            _jsonPreview = MakePreview(Theme.Mono(9F));
            rightLayout.Controls.Add(_jsonPreview, 0, 3);
            right.Controls.Add(rightLayout);

            root.Controls.Add(left, 0, 0);
            root.Controls.Add(right, 1, 0);
            Controls.Add(root);
        }

        private Label MakeSection(string text) => new()
        {
            Text = text, AutoSize = true, Font = Theme.Ui(9.5F, bold: true),
            ForeColor = Theme.TextSecondary, Margin = new Padding(0, 8, 0, 4), BackColor = Color.Transparent
        };

        private TextBox MakePreview(Font font) => new()
        {
            Multiline = true, Dock = DockStyle.Fill, ReadOnly = true, BackColor = Theme.FieldBg,
            Font = font, BorderStyle = BorderStyle.FixedSingle, ScrollBars = ScrollBars.Vertical
        };

        private (NumericUpDown, Label) MakeNum(string label, decimal min, decimal max, decimal val, int pad, TableLayoutPanel grid, int col, int row)
        {
            var wrap = new FlowLayoutPanel { AutoSize = true, BackColor = Color.Transparent, Margin = new Padding(0) };
            wrap.Controls.Add(new Label { Text = label, AutoSize = true, Font = Theme.Ui(8.5F), ForeColor = Theme.TextSecondary, Margin = new Padding(0, 10, 0, 0), AutoEllipsis = true });
            var num = new NumericUpDown { Minimum = min, Maximum = max, Value = val, Width = 64, Font = Theme.Ui(9.5F) };
            num.ValueChanged += (s, e) => UpdatePreviews();
            wrap.Controls.Add(num);
            grid.Controls.Add(wrap, col, row);
            return (num, wrap.Controls[0] as Label)!;
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
        }

        // ==================== 串口输出 ====================

        public void RefreshComPorts()
        {
            try
            {
                _comPort.Items.Clear();
                _comPort.Items.AddRange(SerialPort.GetPortNames());
                if (_comPort.Items.Count > 0) _comPort.SelectedIndex = 0;
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

        public void Shutdown()
        {
            _timer?.Stop();
            try { _serial?.Close(); _serial?.Dispose(); } catch { }
        }
    }
}
