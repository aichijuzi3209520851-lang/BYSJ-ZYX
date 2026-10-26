using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MQTTnet;
using MQTTnet.Client;

namespace DHT11
{
    public partial class Form1 : Form
    {
        private SerialPort _serialPort;
        private List<double> _tempHistory = new List<double>();
        private List<double> _humHistory = new List<double>();
        private List<double> _waterHistory = new List<double>();  // 土壤湿度历史数据
        private List<double> _lightHistory = new List<double>();  // 光照强度历史数据
        private List<string> _timestampHistory = new List<string>();
        private List<string> _alarmTypeHistory = new List<string>();
        private List<string> _airQualityHistory = new List<string>();  // 空气质量历史
        private List<string> _servoStatusHistory = new List<string>();  // 窗户状态历史
        private List<string> _motorStatusHistory = new List<string>();  // 风扇状态历史
        private const int MaxHistoryPoints = 100;
        private const int MaxLogTextLength = 60000;   // 日志框最大字符数, 超过后砍掉前一半, 防止长时间运行后无限膨胀
        private int _logCharCount = 0;                // 已写入日志的字符数(记账用, 避免每帧读 logTextBox.TextLength 的 O(n))
        private StringBuilder _receivedBuffer = new StringBuilder();

        // 图标缓存: 同一路径只从磁盘解码一次, 避免每帧 Image.FromFile 造成 GDI+ 句柄泄漏与 png 文件长期被锁
        private readonly Dictionary<string, Image> _iconCache = new Dictionary<string, Image>();

        // UI控件
        private ComboBox comPortComboBox;
        private ComboBox baudRateComboBox;
        private Button openButton;
        private Button closeButton;
        private Button refreshButton;
        private Button clearButton;
        private Button exportButton;
        private Label tempLabel;
        private Label humLabel;
        private Panel chartPanel;
        private Label portLabel;
        private Label baudRateLabel;
        private Label lblStatus;
        private TextBox logTextBox;
        private Label logLabel;
        private Label chartLabel;
        private CheckBox autoScrollCheckBox;

        // 新增UI控件
        private Label waterLabel;
        private Label servoLabel;
        private Label motorLabel;
        private Label airQualityLabel;
        private Label lightLabel;
        private PictureBox waterIconPictureBox;
        private PictureBox lightIconPictureBox;
        private PictureBox airQualityIconPictureBox;
        private PictureBox windowIconPictureBox;  // 窗户图标
        private PictureBox fanIconPictureBox;     // 风扇图标
        private PictureBox buzzerIconPictureBox;  // 蜂鸣器图标
        private Button servoOnButton;
        private Button servoOffButton;
        private Button motorOnButton;
        private Button motorOffButton;
        private Button modeAutoButton;
        private Button modeManualButton;
        private Label modeStatusLabel;
        private Label buzzerLabel;   // 蜂鸣器卡片数值(原为 InitializeCustomComponents 内局部变量, 卡片化后提为字段)

        // MQTT相关控件
        private TextBox mqttBrokerTextBox;
        private TextBox mqttTopicTextBox;
        private TextBox mqttSubscribeTopicTextBox;
        private Button mqttConnectButton;
        private Button mqttDisconnectButton;
        private Label mqttBrokerLabel;
        private Label mqttTopicLabel;
        private Label mqttSubscribeTopicLabel;
        private Label mqttStatusLabel;
        private CheckBox mqttAutoPublishCheckBox;

        // 图标显示控件
        private PictureBox tempIconPictureBox;
        private PictureBox humIconPictureBox;
        private PictureBox avatarPictureBox;
        private Label avatarLabel;

        // 布局容器控件(由 InitializeCustomComponents 创建, ApplyResponsiveLayout 统一摆放)
        private Panel headerPanel;
        private CardPanel connectPanel;
        private Panel cardsPanel;
        private CardPanel devicePanel;
        private Button logoutButton;
        private readonly List<CardPanel> _cards = new List<CardPanel>();

        // 统一报警阈值(与STM32固件/微信小程序保持一致, 三端使用同一组数值)
        // 报警条件: 温度>26°C 或 空气湿度>85%RH 或 土壤湿度>40% 或 空气污染
        private double _tempMax = 26.0;
        private double _humMax = 85.0;
        private int _soilMax = 40;

        // 报警状态
        private bool _tempAlarm = false;
        private bool _humAlarm = false;

        // 配色方案
        private Color primaryColor = Color.FromArgb(33, 150, 243);      // 蓝色
        private Color successColor = Color.FromArgb(76, 175, 80);       // 绿色
        private Color dangerColor = Color.FromArgb(244, 67, 54);        // 红色
        private Color warningColor = Color.FromArgb(255, 152, 0);       // 橙色
        private Color darkColor = Color.FromArgb(52, 73, 94);           // 深蓝灰
        private Color lightColor = Color.FromArgb(236, 240, 241);       // 浅灰
        private Color chartBgColor = Color.FromArgb(250, 250, 250);     // 图表背景
        private Color pageBgColor = Color.FromArgb(244, 246, 249);      // 页面底色(略冷灰)
        private Color textSecondaryColor = Color.FromArgb(120, 132, 148); // 次要文字色

        // 数据格式解析配置：固件固定使用 STM32 格式 (Temp:XXC Humi:XX%RH ...), 无需运行时切换

        // 新增数据字段
        private int _waterLevel = 0;
        private int _lightLevel = 0;  // 光照强度百分比(固件 Light:%d%% 字段)
        private string _servoStatus = "OFF";
        private string _motorStatus = "OFF";
        private string _airQuality = "nice";

        // MQTT客户端和配置
        private IMqttClient _mqttClient;
        private string _mqttPublishTopic = "green/data";   // 发布主题：传感器JSON数据发送到这里(与小程序订阅主题一致)
        private string _mqttSubscribeTopic = "green/control";  // 订阅主题：接收控制命令(原文转发串口)
        private bool _mqttAutoPublish = true;               // 是否自动发布数据到MQTT
        private bool _mqttHandlerAttached = false;          // 消息处理器是否已挂载(防止重连后重复挂载)

        // 注: 视频监控模块已整体移除(相关字段/方法/面板/HTTP 客户端全部删除)

        public Form1()
        {
            InitializeComponent();  // 必须首先调用，初始化设计器生成的组件
            InitializeSerialPort();
            InitializeChart();
            InitializeCustomComponents();
            InitializeMqttClient();
            LoadAvailablePorts();
        }

        // ==================== 布局度量 ====================
        // 单一真源: 所有坐标只在 ApplyResponsiveLayout() 里计算。
        // 原实现"构造时写一套坐标、Resize 时再改另一套", 两套数值不一致就错位, 这里彻底改掉。
        private const int PadOuter = 12;            // 窗体四周留白
        private const int GapBlock = 12;            // 区块间距
        private const int HeaderHeight = 62;        // 顶部标题栏高度
        private const int ConnectHeight = 136;      // 连接配置卡片高度(两行)
        private const int CardHeight = 96;          // 传感器/执行器卡片高度
        private const int DeviceHeight = 64;        // 设备控制条高度
        private const int SectionLabelHeight = 24;  // 图表/日志小标题占用高度

        private void InitializeCustomComponents()
        {
            this.SuspendLayout();

            // ========== 窗体基础设置 ==========
            // 标题带版本号: 排查"双击后好像没更新"时, 一眼就能看出跑的到底是哪个版本
            this.Text = "智能温室监控系统 v" + Application.ProductVersion;

            // 初始尺寸按屏幕工作区收敛: 高分屏缩放到 150% 时, DPI 未感知的 WinForms 程序拿到的是缩放后的逻辑像素,
            // 写死 1380x840 会比实际可用区域还大, 窗口右/下边界直接跑出屏幕。这里以工作区为准做一次钳制。
            Rectangle workArea = Screen.PrimaryScreen != null
                ? Screen.PrimaryScreen.WorkingArea
                : new Rectangle(0, 0, 1280, 800);
            int targetWidth = Math.Min(1380, workArea.Width - 24);
            int targetHeight = Math.Min(840, workArea.Height - 48);
            if (targetWidth < 900) targetWidth = Math.Max(320, workArea.Width - 24);
            if (targetHeight < 620) targetHeight = Math.Max(240, workArea.Height - 48);

            // MinimumSize 是含边框的整窗尺寸, 必须同时受工作区约束, 否则一设下去就会被系统撑回屏幕外。
            this.MinimumSize = new Size(
                Math.Min(960, workArea.Width - 24),
                Math.Min(680, workArea.Height - 24));
            this.ClientSize = new Size(targetWidth, targetHeight);
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MaximizeBox = true;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = pageBgColor;
            this.DoubleBuffered = true;
            this.FormClosing += Form1_FormClosing;
            this.Resize += Form1_Resize;

            // 自上而下: 标题栏 → 连接配置 → 传感器卡片 → 设备控制 → (图表 | 日志)
            BuildHeader();
            BuildConnectPanel();
            BuildSensorCards();
            BuildDevicePanel();
            BuildChartAndLog();

            this.ResumeLayout(false);
            this.PerformLayout();

            ApplyResponsiveLayout();
        }

        // ========== 顶部标题栏: 系统名 + 副标题 + 头像 + 昵称 + 注销 ==========
        private void BuildHeader()
        {
            headerPanel = new Panel
            {
                Location = new Point(0, 0),
                Size = new Size(this.ClientSize.Width, HeaderHeight),
                BackColor = Color.White
            };
            headerPanel.Paint += HeaderPanel_Paint;

            Label appTitle = new Label
            {
                Location = new Point(PadOuter + 4, 10),
                AutoSize = true,
                Text = "智能温室监控系统",
                Font = new Font("微软雅黑", 15F, FontStyle.Bold),
                ForeColor = darkColor,
                BackColor = Color.Transparent
            };

            Label appSubTitle = new Label
            {
                Location = new Point(PadOuter + 6, 38),
                AutoSize = true,
                Text = "STM32 环境采集 · 设备联动 · MQTT 远程监控",
                Font = new Font("微软雅黑", 8.5F),
                ForeColor = textSecondaryColor,
                BackColor = Color.Transparent
            };

            avatarPictureBox = new PictureBox
            {
                Location = new Point(0, 0),
                Size = new Size(36, 36),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent
            };

            avatarLabel = new Label
            {
                Location = new Point(0, 0),
                AutoSize = true,
                Text = "爱吃橘子🍊",
                Font = new Font("微软雅黑", 9F),
                ForeColor = darkColor,
                BackColor = Color.Transparent
            };

            logoutButton = CreateStyledButton("注销", new Point(0, 0), new Size(64, 26), textSecondaryColor);
            logoutButton.Click += LogoutButton_Click;

            headerPanel.Controls.AddRange(new Control[] {
                appTitle, appSubTitle, avatarPictureBox, avatarLabel, logoutButton
            });

            this.Controls.Add(headerPanel);

            LoadCircularAvatar();   // 头像按 60px 生成, 这里缩放到 36px 显示
        }

        // ========== 连接配置卡片: 第一行串口 + 数据操作, 第二行 MQTT ==========
        private void BuildConnectPanel()
        {
            connectPanel = new CardPanel
            {
                Location = new Point(PadOuter, HeaderHeight + GapBlock),
                Size = new Size(this.ClientSize.Width - PadOuter * 2, ConnectHeight),
                Radius = 12,
                ShowAccent = false
            };
            connectPanel.Paint += ConnectPanel_Paint;

            Font fieldFont = new Font("微软雅黑", 9F);
            Font textBoxFont = new Font("微软雅黑", 9.5F);

            // ---------- 第一行: 串口 ----------
            portLabel = new Label
            {
                Location = new Point(16, 12),
                AutoSize = true,
                Text = "串口选择",
                Font = fieldFont,
                ForeColor = textSecondaryColor,
                BackColor = Color.Transparent
            };

            comPortComboBox = new ComboBox
            {
                Location = new Point(16, 35),
                Width = 110,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = fieldFont
            };

            refreshButton = CreateStyledButton("刷新", new Point(134, 34), new Size(56, 26), primaryColor);
            refreshButton.Click += RefreshButton_Click;

            baudRateLabel = new Label
            {
                Location = new Point(204, 12),
                AutoSize = true,
                Text = "波特率",
                Font = fieldFont,
                ForeColor = textSecondaryColor,
                BackColor = Color.Transparent
            };

            baudRateComboBox = new ComboBox
            {
                Location = new Point(204, 35),
                Width = 88,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = fieldFont
            };
            baudRateComboBox.Items.AddRange(new object[] { "9600", "19200", "38400", "57600", "115200" });
            baudRateComboBox.SelectedIndex = 0;
            baudRateComboBox.SelectedIndexChanged += BaudRateComboBox_SelectedIndexChanged;

            openButton = CreateStyledButton("连接串口", new Point(306, 34), new Size(92, 26), successColor);
            openButton.Click += OpenButton_Click;

            closeButton = CreateStyledButton("断开连接", new Point(406, 34), new Size(92, 26), dangerColor);
            closeButton.Click += CloseButton_Click;
            closeButton.Enabled = false;

            lblStatus = new Label
            {
                Location = new Point(508, 40),
                AutoSize = true,
                Text = "● 未连接",
                Font = new Font("微软雅黑", 9F, FontStyle.Bold),
                ForeColor = Color.Gray,
                BackColor = Color.Transparent
            };

            // 清空/导出 在 ApplyResponsiveLayout 里按卡片宽度右对齐
            clearButton = CreateStyledButton("清空数据", new Point(0, 34), new Size(92, 26), warningColor);
            clearButton.Click += ClearButton_Click;

            exportButton = CreateStyledButton("导出CSV", new Point(0, 34), new Size(92, 26), primaryColor);
            exportButton.Click += ExportButton_Click;

            // ---------- 第二行: MQTT ----------
            mqttBrokerLabel = new Label
            {
                Location = new Point(16, 76),
                AutoSize = true,
                Text = "MQTT 服务器",
                Font = fieldFont,
                ForeColor = textSecondaryColor,
                BackColor = Color.Transparent
            };

            mqttBrokerTextBox = new TextBox
            {
                Location = new Point(16, 99),
                Width = 180,
                Text = "192.168.184.221:1883",
                Font = textBoxFont
            };

            mqttTopicLabel = new Label
            {
                Location = new Point(212, 76),
                AutoSize = true,
                Text = "发布主题",
                Font = fieldFont,
                ForeColor = textSecondaryColor,
                BackColor = Color.Transparent
            };

            mqttTopicTextBox = new TextBox
            {
                Location = new Point(212, 99),
                Width = 140,
                Text = "green/data",
                Font = textBoxFont
            };

            mqttSubscribeTopicLabel = new Label
            {
                Location = new Point(368, 76),
                AutoSize = true,
                Text = "订阅主题",
                Font = fieldFont,
                ForeColor = textSecondaryColor,
                BackColor = Color.Transparent
            };

            mqttSubscribeTopicTextBox = new TextBox
            {
                Location = new Point(368, 99),
                Width = 140,
                Text = "green/control",
                Font = textBoxFont
            };

            mqttConnectButton = CreateStyledButton("连接MQTT", new Point(526, 98), new Size(92, 26), successColor);
            mqttConnectButton.Click += MqttConnectButton_Click;

            mqttDisconnectButton = CreateStyledButton("断开MQTT", new Point(626, 98), new Size(92, 26), dangerColor);
            mqttDisconnectButton.Click += MqttDisconnectButton_Click;
            mqttDisconnectButton.Enabled = false;

            mqttStatusLabel = new Label
            {
                Location = new Point(728, 104),
                AutoSize = true,
                Text = "● 未连接",
                Font = new Font("微软雅黑", 9F, FontStyle.Bold),
                ForeColor = Color.Gray,
                BackColor = Color.Transparent
            };

            mqttAutoPublishCheckBox = new CheckBox
            {
                Location = new Point(0, 102),   // 右对齐, 具体位置在 ApplyResponsiveLayout 里算
                AutoSize = true,
                Text = "自动发布数据到MQTT",
                Font = fieldFont,
                Checked = true,
                BackColor = Color.Transparent
            };
            mqttAutoPublishCheckBox.CheckedChanged += MqttAutoPublishCheckBox_CheckedChanged;

            connectPanel.Controls.AddRange(new Control[] {
                portLabel, comPortComboBox, refreshButton,
                baudRateLabel, baudRateComboBox,
                openButton, closeButton, lblStatus,
                clearButton, exportButton,
                mqttBrokerLabel, mqttBrokerTextBox,
                mqttTopicLabel, mqttTopicTextBox,
                mqttSubscribeTopicLabel, mqttSubscribeTopicTextBox,
                mqttConnectButton, mqttDisconnectButton, mqttStatusLabel, mqttAutoPublishCheckBox
            });

            this.Controls.Add(connectPanel);
        }

        // ========== 传感器/执行器卡片行 (8 张, 等分容器宽度) ==========
        private void BuildSensorCards()
        {
            cardsPanel = new Panel
            {
                Location = new Point(PadOuter, HeaderHeight + GapBlock + ConnectHeight + GapBlock),
                Size = new Size(this.ClientSize.Width - PadOuter * 2, CardHeight),
                BackColor = pageBgColor
            };

            _cards.Clear();

            const float numFont = 17F;   // 数值型读数
            const float txtFont = 13F;   // 状态型读数(正常/关闭/开启…)

            _cards.Add(CreateSensorCard("温度", "°C", "--.-", dangerColor, numFont, "ICON\\温度.png",
                out tempLabel, out tempIconPictureBox));
            _cards.Add(CreateSensorCard("湿度", "%RH", "--.-", primaryColor, numFont, "ICON\\湿度.png",
                out humLabel, out humIconPictureBox));
            _cards.Add(CreateSensorCard("土壤湿度", "%", "--", warningColor, numFont, "ICON\\水位.png",
                out waterLabel, out waterIconPictureBox));
            _cards.Add(CreateSensorCard("光照强度", "%", "--", Color.FromArgb(234, 160, 0), numFont, "ICON\\光照.png",
                out lightLabel, out lightIconPictureBox));
            _cards.Add(CreateSensorCard("空气质量", "", "nice", successColor, txtFont, "ICON\\空气质量优状态.png",
                out airQualityLabel, out airQualityIconPictureBox));
            _cards.Add(CreateSensorCard("窗户", "", "关闭", darkColor, txtFont, "ICON\\窗户-关状态.png",
                out servoLabel, out windowIconPictureBox));
            _cards.Add(CreateSensorCard("风扇", "", "关闭", darkColor, txtFont, "ICON\\风扇-关状态.png",
                out motorLabel, out fanIconPictureBox));
            _cards.Add(CreateSensorCard("蜂鸣器", "", "静音", darkColor, txtFont, "ICON\\蜂鸣器-关状态.png",
                out buzzerLabel, out buzzerIconPictureBox));

            this.Controls.Add(cardsPanel);
        }

        /// <summary>
        /// 创建一张读数卡片: 图标 + 名称 + (单位) + 读数。
        /// 单位标签按名称的实际像素宽度紧跟在名称右侧, 避免不同字数下重叠或离太远。
        /// </summary>
        private CardPanel CreateSensorCard(string title, string unit, string initialValue, Color accent,
                                           float valueFontSize, string iconFile,
                                           out Label valueLabel, out PictureBox iconBox)
        {
            CardPanel card = new CardPanel
            {
                Location = new Point(0, 0),
                Size = new Size(160, CardHeight),
                Radius = 10,
                ShowAccent = true,
                AccentColor = accent
            };

            iconBox = new PictureBox
            {
                Location = new Point(12, (CardHeight - 34) / 2),
                Size = new Size(34, 34),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent,
                Image = LoadIconSafely(iconFile)
            };

            Font titleFont = new Font("微软雅黑", 9F);

            Label titleLabel = new Label
            {
                Location = new Point(54, 14),
                AutoSize = true,
                Text = title,
                Font = titleFont,
                ForeColor = textSecondaryColor,
                BackColor = Color.Transparent
            };

            valueLabel = new Label
            {
                Location = new Point(52, 33),
                Size = new Size(100, 34),
                Text = initialValue,
                Font = new Font("Arial", valueFontSize, FontStyle.Bold),
                ForeColor = accent,
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.Transparent
            };

            card.Controls.Add(iconBox);
            card.Controls.Add(titleLabel);
            card.Controls.Add(valueLabel);

            if (!string.IsNullOrEmpty(unit))
            {
                int titlePixelWidth = TextRenderer.MeasureText(title, titleFont).Width;
                Label unitLabel = new Label
                {
                    Location = new Point(54 + titlePixelWidth + 4, 17),
                    AutoSize = true,
                    Text = unit,
                    Font = new Font("Arial", 8.5F),
                    ForeColor = textSecondaryColor,
                    BackColor = Color.Transparent
                };
                card.Controls.Add(unitLabel);
            }

            cardsPanel.Controls.Add(card);
            return card;
        }

        // ========== 设备控制条: 模式切换 + 窗户/风扇手动控制 ==========
        private void BuildDevicePanel()
        {
            devicePanel = new CardPanel
            {
                Location = new Point(PadOuter, 0),
                Size = new Size(this.ClientSize.Width - PadOuter * 2, DeviceHeight),
                Radius = 12,
                ShowAccent = true,
                AccentColor = Color.FromArgb(0, 172, 193)
            };

            Label deviceTitle = new Label
            {
                Location = new Point(20, 22),
                AutoSize = true,
                Text = "设备控制",
                Font = new Font("微软雅黑", 10F, FontStyle.Bold),
                ForeColor = darkColor,
                BackColor = Color.Transparent
            };

            modeStatusLabel = new Label
            {
                Location = new Point(96, 24),
                AutoSize = true,
                Text = "● 自动模式",
                Font = new Font("微软雅黑", 9F),
                ForeColor = successColor,
                BackColor = Color.Transparent
            };

            modeAutoButton = CreateStyledButton("自动模式", new Point(212, 17), new Size(90, 30), successColor);
            modeAutoButton.Click += ModeAutoButton_Click;

            modeManualButton = CreateStyledButton("手动模式", new Point(310, 17), new Size(90, 30), warningColor);
            modeManualButton.Click += ModeManualButton_Click;

            servoOffButton = CreateStyledButton("窗户关", new Point(430, 17), new Size(84, 30), primaryColor);
            servoOffButton.Click += ServoOffButton_Click;
            servoOffButton.Enabled = false;

            servoOnButton = CreateStyledButton("窗户开", new Point(522, 17), new Size(84, 30), primaryColor);
            servoOnButton.Click += ServoOnButton_Click;
            servoOnButton.Enabled = false;

            motorOffButton = CreateStyledButton("风扇关", new Point(640, 17), new Size(84, 30), primaryColor);
            motorOffButton.Click += MotorOffButton_Click;
            motorOffButton.Enabled = false;

            motorOnButton = CreateStyledButton("风扇开", new Point(732, 17), new Size(84, 30), primaryColor);
            motorOnButton.Click += MotorOnButton_Click;
            motorOnButton.Enabled = false;

            Label deviceHint = new Label
            {
                Name = "deviceHintLabel",
                Location = new Point(0, 22),
                AutoSize = false,
                Size = new Size(340, 20),
                Text = "提示: 切换到手动模式后, 窗户/风扇按钮才可操作",
                Font = new Font("微软雅黑", 8.5F),
                ForeColor = textSecondaryColor,
                TextAlign = ContentAlignment.MiddleRight,
                BackColor = Color.Transparent
            };

            devicePanel.Controls.AddRange(new Control[] {
                deviceTitle, modeStatusLabel,
                modeAutoButton, modeManualButton,
                servoOffButton, servoOnButton,
                motorOffButton, motorOnButton, deviceHint
            });

            this.Controls.Add(devicePanel);
        }

        // ========== 图表 + 日志 (并排, 宽度按 58 : 42 分配) ==========
        private void BuildChartAndLog()
        {
            chartLabel = new Label
            {
                Location = new Point(PadOuter + 4, 0),
                AutoSize = true,
                Text = "历史数据趋势 (最近 100 组)",
                Font = new Font("微软雅黑", 10F, FontStyle.Bold),
                ForeColor = darkColor,
                BackColor = Color.Transparent
            };

            logLabel = new Label
            {
                Location = new Point(0, 0),
                AutoSize = true,
                Text = "运行日志",
                Font = new Font("微软雅黑", 10F, FontStyle.Bold),
                ForeColor = darkColor,
                BackColor = Color.Transparent
            };

            autoScrollCheckBox = new CheckBox
            {
                Location = new Point(0, 2),
                AutoSize = true,
                Text = "自动滚动",
                Font = new Font("微软雅黑", 9F),
                Checked = true,
                BackColor = Color.Transparent
            };

            chartPanel = new Panel
            {
                Location = new Point(PadOuter, 0),
                Size = new Size(700, 380),
                BackColor = chartBgColor,
                BorderStyle = BorderStyle.None
            };
            chartPanel.Paint += ChartPanel_Paint;

            logTextBox = new TextBox
            {
                Location = new Point(0, 0),
                Size = new Size(520, 380),
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Color.White,
                Font = new Font("Consolas", 9F),
                BorderStyle = BorderStyle.FixedSingle
            };

            this.Controls.AddRange(new Control[] {
                chartLabel, chartPanel, logLabel, autoScrollCheckBox, logTextBox
            });
        }

        /// <summary>
        /// 按当前客户区尺寸重算所有区块的位置。构造完成后调用一次, 之后每次 Resize 调用。
        /// </summary>
        private void ApplyResponsiveLayout()
        {
            if (headerPanel == null || connectPanel == null || cardsPanel == null || devicePanel == null) return;

            int windowWidth = this.ClientSize.Width;
            int windowHeight = this.ClientSize.Height;
            int innerWidth = windowWidth - PadOuter * 2;

            // ---------- 1. 标题栏 (右对齐: 头像 + 昵称 + 注销) ----------
            headerPanel.SetBounds(0, 0, windowWidth, HeaderHeight);

            if (logoutButton != null)
            {
                logoutButton.Location = new Point(windowWidth - PadOuter - logoutButton.Width,
                                                  (HeaderHeight - logoutButton.Height) / 2);
            }
            if (avatarPictureBox != null && logoutButton != null)
            {
                avatarPictureBox.Location = new Point(logoutButton.Left - 10 - avatarPictureBox.Width,
                                                      (HeaderHeight - avatarPictureBox.Height) / 2);
            }
            if (avatarLabel != null && avatarPictureBox != null)
            {
                avatarLabel.Location = new Point(avatarPictureBox.Left - 8 - avatarLabel.Width,
                                                 (HeaderHeight - avatarLabel.Height) / 2);
            }

            // ---------- 2. 连接配置卡片 ----------
            int connectTop = HeaderHeight + GapBlock;
            connectPanel.SetBounds(PadOuter, connectTop, innerWidth, ConnectHeight);

            int cardWidth = connectPanel.Width;
            if (exportButton != null && clearButton != null)
            {
                exportButton.Location = new Point(cardWidth - 16 - exportButton.Width, 34);
                clearButton.Location = new Point(exportButton.Left - 8 - clearButton.Width, 34);
            }
            if (mqttAutoPublishCheckBox != null)
            {
                mqttAutoPublishCheckBox.Location = new Point(cardWidth - 16 - mqttAutoPublishCheckBox.Width, 102);
            }

            // ---------- 3. 卡片行 (等分) ----------
            int cardsTop = connectTop + ConnectHeight + GapBlock;
            cardsPanel.SetBounds(PadOuter, cardsTop, innerWidth, CardHeight);

            const int cardGap = 6;
            int cardCount = _cards.Count;
            if (cardCount > 0)
            {
                int eachWidth = (innerWidth - cardGap * (cardCount - 1)) / cardCount;
                if (eachWidth < 110) eachWidth = 110;
                for (int i = 0; i < cardCount; i++)
                {
                    _cards[i].SetBounds(i * (eachWidth + cardGap), 0, eachWidth, CardHeight);
                }
            }

            // ---------- 4. 设备控制条 ----------
            int deviceTop = cardsTop + CardHeight + GapBlock;
            devicePanel.SetBounds(PadOuter, deviceTop, innerWidth, DeviceHeight);

            Control deviceHint = devicePanel.Controls["deviceHintLabel"];
            if (deviceHint != null)
            {
                deviceHint.Location = new Point(devicePanel.Width - 16 - deviceHint.Width, 22);
            }

            // ---------- 5. 图表 | 日志 ----------
            int contentTop = deviceTop + DeviceHeight + GapBlock;
            int labelTop = contentTop;
            int panelTop = contentTop + SectionLabelHeight;
            int panelHeight = windowHeight - panelTop - PadOuter;
            if (panelHeight < 200) panelHeight = 200;

            int available = innerWidth - GapBlock;
            int chartWidth = (int)(available * 0.58);
            int logLeft = PadOuter + chartWidth + GapBlock;
            int logWidth = available - chartWidth;

            if (chartLabel != null) chartLabel.Location = new Point(PadOuter + 4, labelTop + 2);
            if (logLabel != null) logLabel.Location = new Point(logLeft + 4, labelTop + 2);
            if (autoScrollCheckBox != null)
            {
                autoScrollCheckBox.Location = new Point(logLeft + logWidth - autoScrollCheckBox.Width - 4, labelTop + 2);
            }
            if (chartPanel != null) chartPanel.SetBounds(PadOuter, panelTop, chartWidth, panelHeight);
            if (logTextBox != null) logTextBox.SetBounds(logLeft, panelTop, logWidth, panelHeight);

            if (chartPanel != null) chartPanel.Invalidate();
        }

        // 标题栏底部 1px 分隔线
        private void HeaderPanel_Paint(object sender, PaintEventArgs e)
        {
            using (Pen pen = new Pen(Color.FromArgb(230, 235, 241), 1))
            {
                e.Graphics.DrawLine(pen, 0, headerPanel.Height - 1, headerPanel.Width, headerPanel.Height - 1);
            }
        }

        // 连接卡片里两行之间的分隔线
        private void ConnectPanel_Paint(object sender, PaintEventArgs e)
        {
            using (Pen pen = new Pen(Color.FromArgb(238, 241, 246), 1))
            {
                e.Graphics.DrawLine(pen, 16, 69, connectPanel.Width - 16, 69);
            }
        }

        /// <summary>圆角矩形路径 (卡片绘制用)。</summary>
        private static GraphicsPath CreateRoundedPath(Rectangle rect, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int diameter = Math.Max(1, radius * 2);
            if (diameter > rect.Width) diameter = rect.Width;
            if (diameter > rect.Height) diameter = rect.Height;

            path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
            path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseAllFigures();
            return path;
        }

        /// <summary>
        /// 统一卡片外观: 白底 + 圆角 + 1px 描边 + 可选左侧主题色竖条。
        /// 做成 Panel 子类(而不是给每个 Panel 挂 Paint 事件)是为了打开双缓冲, 消除频繁刷新时的闪烁。
        /// </summary>
        private sealed class CardPanel : Panel
        {
            private static readonly Color StrokeColor = Color.FromArgb(228, 232, 238);

            public Color AccentColor { get; set; } = Color.Transparent;
            public int Radius { get; set; } = 10;
            public bool ShowAccent { get; set; }

            public CardPanel()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint
                       | ControlStyles.OptimizedDoubleBuffer
                       | ControlStyles.UserPaint
                       | ControlStyles.ResizeRedraw, true);
                BackColor = Color.White;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;

                // 圆角以外的区域先填父容器底色, 否则四角会留下白色直角
                g.Clear(Parent != null ? Parent.BackColor : Color.White);

                Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
                using (GraphicsPath path = CreateRoundedPath(rect, Radius))
                using (SolidBrush fill = new SolidBrush(Color.White))
                using (Pen border = new Pen(StrokeColor, 1))
                {
                    g.FillPath(fill, path);
                    g.DrawPath(border, path);
                }

                if (ShowAccent && AccentColor.A != 0)
                {
                    using (SolidBrush accent = new SolidBrush(AccentColor))
                    {
                        g.FillRectangle(accent, new Rectangle(2, 16, 3, Math.Max(4, Height - 32)));
                    }
                }

                base.OnPaint(e);   // 必须调用, 否则外层挂的 Paint 事件(如 ConnectPanel_Paint)不会触发
            }
        }

        // 创建样式化按钮
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
                Font = new Font("微软雅黑", 9F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderSize = 0;
            btn.FlatAppearance.MouseOverBackColor = ControlPaint.Light(backColor, 0.2f);
            btn.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(backColor, 0.1f);
            return btn;
        }

        // 安全加载图标
        // 修复: 原实现每次都 Image.FromFile 返回全新 Image 且从不释放, 固件 1.5s 一帧、每帧最多换 10 个图标,
        //       约 2.4 万张/小时, 会耗尽 GDI+ 句柄并长期锁定 png 文件。改为进程内缓存, 同一路径只解码一次。
        //       返回的对象由缓存持有, 调用方不得 Dispose。
        private Image LoadIconSafely(string relativePath)
        {
            try
            {
                string iconPath = Path.Combine(Application.StartupPath, relativePath);

                Image cached;
                if (_iconCache.TryGetValue(iconPath, out cached))
                {
                    return cached;
                }

                if (!File.Exists(iconPath))
                {
                    LogMessage($"警告: 图标文件不存在: {iconPath}");
                    _iconCache[iconPath] = null;   // 记住"不存在", 避免每帧重复查盘与刷日志
                    return null;
                }

                // 先用 FileStream 读进内存再解码, 解码后立即关闭文件句柄, 这样 png 不会被进程长期占用
                using (var fs = new FileStream(iconPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var ms = new MemoryStream())
                {
                    fs.CopyTo(ms);
                    ms.Position = 0;
                    cached = Image.FromStream(ms);
                }

                _iconCache[iconPath] = cached;
                return cached;
            }
            catch (Exception ex)
            {
                LogMessage($"错误: 加载图标失败 - {ex.Message}");
                return null;
            }
        }

        // 释放图标缓存(关闭窗体时调用)
        private void DisposeIconCache()
        {
            foreach (var kv in _iconCache)
            {
                if (kv.Value != null)
                {
                    kv.Value.Dispose();
                }
            }
            _iconCache.Clear();
        }

        // 设置图标; 取不到(文件缺失/已释放)时保持原样, 避免把 PictureBox 刷成空白
        private void TrySetIcon(PictureBox box, string relativePath)
        {
            if (box == null) return;
            Image icon = LoadIconSafely(relativePath);
            if (icon != null)
            {
                box.Image = icon;
            }
        }

        // 加载圆形头像
        private void LoadCircularAvatar()
        {
            try
            {
                string avatarPath = Path.Combine(Application.StartupPath, "ICON\\爱吃橘子.jpg");
                if (File.Exists(avatarPath))
                {
                    // 修复: 原实现 Image.FromFile 后既不解码副本也不释放, 头像文件被进程长期锁定。
                    // 改为读进内存解码, 解码完成即关闭文件, 并 Dispose 掉源图。
                    using (var fs = new FileStream(avatarPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    using (var ms = new MemoryStream())
                    {
                        fs.CopyTo(ms);
                        ms.Position = 0;
                        using (Image originalImage = Image.FromStream(ms))
                        {
                            Image circularImage = CreateCircularImage(originalImage, 60);
                            var oldImage = avatarPictureBox.Image;
                            avatarPictureBox.Image = circularImage;
                            oldImage?.Dispose();
                        }
                    }
                }
                else
                {
                    LogMessage($"警告: 头像文件不存在: {avatarPath}");
                }
            }
            catch (Exception ex)
            {
                LogMessage($"错误: 加载头像失败 - {ex.Message}");
            }
        }

        // 创建圆形图片
        private Image CreateCircularImage(Image sourceImage, int size)
        {
            Bitmap bitmap = new Bitmap(size, size);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;

                // 创建圆形路径
                using (GraphicsPath path = new GraphicsPath())
                {
                    path.AddEllipse(0, 0, size, size);
                    g.SetClip(path);

                    // 绘制图片
                    g.DrawImage(sourceImage, 0, 0, size, size);
                }

                // 绘制圆形边框
                using (Pen pen = new Pen(Color.FromArgb(220, 220, 220), 2))
                {
                    g.ResetClip();
                    g.DrawEllipse(pen, 1, 1, size - 2, size - 2);
                }
            }
            return bitmap;
        }

        // 更新报警状态
        private void UpdateAlarmStatus(double temp, double hum)
        {
            // 检查温度报警(统一阈值: >26°C, 与固件/小程序一致)
            bool tempAlarmNew = (temp > _tempMax);
            if (tempAlarmNew != _tempAlarm)
            {
                _tempAlarm = tempAlarmNew;
                if (_tempAlarm)
                {
                    tempIconPictureBox.Image = LoadIconSafely("ICON\\温度_报警状态.png");
                    LogMessage($"⚠ 温度报警！当前: {temp:F1}°C, 阈值: >{_tempMax:F1}°C", warningColor);
                }
                else
                {
                    tempIconPictureBox.Image = LoadIconSafely("ICON\\温度.png");
                    LogMessage($"✓ 温度恢复正常: {temp:F1}°C", successColor);
                }
            }

            // 检查湿度报警(统一阈值: >85%RH, 与固件/小程序一致)
            bool humAlarmNew = (hum > _humMax);
            if (humAlarmNew != _humAlarm)
            {
                _humAlarm = humAlarmNew;
                if (_humAlarm)
                {
                    humIconPictureBox.Image = LoadIconSafely("ICON\\湿度_报警状态.png");
                    LogMessage($"⚠ 湿度报警！当前: {hum:F1}%, 阈值: >{_humMax:F1}%", warningColor);
                }
                else
                {
                    humIconPictureBox.Image = LoadIconSafely("ICON\\湿度.png");
                    LogMessage($"✓ 湿度恢复正常: {hum:F1}%", successColor);
                }
            }
        }

        // 获取当前报警类型(统一阈值方案, 与STM32固件/微信小程序保持一致)
        // 报警条件: 温度>26°C / 湿度>85%RH / 土壤湿度>40% / 空气污染
        private string GetAlarmType(double temp, double hum)
        {
            bool tempAlarm = (temp > _tempMax);
            bool humAlarm = (hum > _humMax);
            bool soilAlarm = (_waterLevel > _soilMax);
            bool airAlarm = (_airQuality == "bad");

            if (tempAlarm && humAlarm)
                return "温度湿度报警";
            else if (tempAlarm)
                return "温度报警";
            else if (humAlarm)
                return "湿度报警";
            else if (soilAlarm)
                return "土壤过湿报警";
            else if (airAlarm)
                return "空气污染报警";
            else
                return "正常";
        }

        private void InitializeSerialPort()
        {
            _serialPort = new SerialPort
            {
                Parity = Parity.None,
                StopBits = StopBits.One,
                DataBits = 8,
                Handshake = Handshake.None,
                RtsEnable = true,      // 启用RTS信号
                DtrEnable = true,      // 启用DTR信号
                ReadTimeout = 500,
                WriteTimeout = 500,
                Encoding = Encoding.UTF8,  // 使用UTF8编码
                NewLine = "\r\n"  // 设置换行符为\r\n
            };
            _serialPort.DataReceived += SerialPort_DataReceived;
        }

        private void InitializeChart()
        {
            _tempHistory.Clear();
            _humHistory.Clear();
            _waterHistory.Clear();
            _lightHistory.Clear();
        }

        private void LoadAvailablePorts()
        {
            try
            {
                comPortComboBox.Items.Clear();
                var ports = SerialPort.GetPortNames();
                if (ports.Length > 0)
                {
                    comPortComboBox.Items.AddRange(ports);
                    comPortComboBox.SelectedIndex = 0;
                    LogMessage($"检测到 {ports.Length} 个串口: {string.Join(", ", ports)}");
                }
                else
                {
                    comPortComboBox.Items.Add("无可用串口");
                    comPortComboBox.SelectedIndex = 0;
                    LogMessage("警告: 未检测到可用串口！");
                }
            }
            catch (Exception ex)
            {
                comPortComboBox.Items.Add("无可用串口");
                comPortComboBox.SelectedIndex = 0;
                LogMessage($"错误: 枚举串口失败 - {ex.Message}");
            }
        }

        private void RefreshButton_Click(object sender, EventArgs e)
        {
            LoadAvailablePorts();
        }

        private void BaudRateComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (baudRateComboBox.SelectedItem != null && _serialPort != null && !_serialPort.IsOpen)
            {
                if (int.TryParse(baudRateComboBox.SelectedItem.ToString(), out int br))
                {
                    _serialPort.BaudRate = br;
                    LogMessage($"波特率设置为: {br}");
                }
            }
        }

        private void OpenButton_Click(object sender, EventArgs e)
        {
            if (_serialPort == null) InitializeSerialPort();

            if (_serialPort.IsOpen) return;

            if (comPortComboBox.SelectedIndex == -1 || comPortComboBox.SelectedItem.ToString() == "无可用串口")
            {
                MessageBox.Show("请选择有效的串口！", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            try
            {
                _serialPort.PortName = comPortComboBox.SelectedItem.ToString();
                if (int.TryParse(baudRateComboBox.SelectedItem.ToString(), out int br))
                    _serialPort.BaudRate = br;

                _serialPort.Open();
                _receivedBuffer.Clear();

                openButton.Enabled = false;
                closeButton.Enabled = true;
                comPortComboBox.Enabled = false;
                baudRateComboBox.Enabled = false;
                refreshButton.Enabled = false;

                lblStatus.Text = "● 已连接";
                lblStatus.ForeColor = successColor;

                LogMessage($"成功连接到 {_serialPort.PortName}，波特率: {_serialPort.BaudRate}");
                LogMessage("等待接收数据...");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"串口打开失败:\n{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                LogMessage($"错误: 打开串口失败 - {ex.Message}");
            }
        }

        private void CloseButton_Click(object sender, EventArgs e)
        {
            CloseSerialPort();
        }

        private void CloseSerialPort()
        {
            try
            {
                if (_serialPort != null && _serialPort.IsOpen)
                {
                    _serialPort.Close();
                    LogMessage($"已断开串口 {_serialPort.PortName}");
                }
            }
            catch (Exception ex)
            {
                LogMessage($"错误: 关闭串口失败 - {ex.Message}");
            }

            openButton.Enabled = true;
            closeButton.Enabled = false;
            comPortComboBox.Enabled = true;
            baudRateComboBox.Enabled = true;
            refreshButton.Enabled = true;

            lblStatus.Text = "● 未连接";
            lblStatus.ForeColor = Color.Gray;
        }

        private void ClearButton_Click(object sender, EventArgs e)
        {
            _tempHistory.Clear();
            _humHistory.Clear();
            _waterHistory.Clear();
            _lightHistory.Clear();
            _timestampHistory.Clear();
            _alarmTypeHistory.Clear();
            _airQualityHistory.Clear();
            _servoStatusHistory.Clear();
            _motorStatusHistory.Clear();

            // 修复: 原来只清历史 list, 不清日志框 —— 按钮叫"清空"但日志一直留着且无限膨胀(UpdateLogTextBox
            //       每次 ScrollToCaret 都是 O(n), 跑几小时会明显卡顿)。这里把日志框一并清掉并重置长度计数。
            logTextBox.Clear();
            _logCharCount = 0;

            // 修复: 原来也不重置"最新状态", 清空后界面仍显示上一次的土壤/光照/空气质量/舵机/风扇,
            //       图标还停在报警状态。统一复位到初始值, 让界面与"无数据"一致。
            _waterLevel = 0;
            _lightLevel = 0;
            _airQuality = "nice";
            _servoStatus = "OFF";
            _motorStatus = "OFF";
            _tempAlarm = false;
            _humAlarm = false;

            tempLabel.Text = "--.-";
            humLabel.Text = "--.-";
            waterLabel.Text = "--";
            lightLabel.Text = "--";
            airQualityLabel.Text = "nice";
            airQualityLabel.ForeColor = successColor;
            servoLabel.Text = "关闭";
            motorLabel.Text = "关闭";

            // 图标全部复位到正常态(直接取缓存, 已解码过的不会重复读盘)
            TrySetIcon(tempIconPictureBox, "ICON\\温度.png");
            TrySetIcon(humIconPictureBox, "ICON\\湿度.png");
            TrySetIcon(waterIconPictureBox, "ICON\\水位.png");
            TrySetIcon(airQualityIconPictureBox, "ICON\\空气质量优状态.png");
            TrySetIcon(windowIconPictureBox, "ICON\\窗户-关状态.png");
            TrySetIcon(fanIconPictureBox, "ICON\\风扇-关状态.png");
            TrySetIcon(buzzerIconPictureBox, "ICON\\蜂鸣器-关状态.png");

            chartPanel.Invalidate();
            LogMessage("已清空历史数据");
        }

        private void ExportButton_Click(object sender, EventArgs e)
        {
            if (_tempHistory.Count == 0)
            {
                MessageBox.Show("没有数据可导出！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (SaveFileDialog sfd = new SaveFileDialog())
            {
                sfd.Filter = "CSV文件|*.csv|所有文件|*.*";
                sfd.FileName = $"DHT11数据_{DateTime.Now:yyyyMMdd_HHmmss}.csv";

                if (sfd.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        using (StreamWriter sw = new StreamWriter(sfd.FileName, false, Encoding.UTF8))
                        {
                            // 写入CSV标题行
                            sw.WriteLine("序号,温度(°C),湿度(%),土壤湿度(%),光照(%),空气质量,窗户状态,风扇状态,报警类型,时间戳");

                            // 写入数据行
                            for (int i = 0; i < _tempHistory.Count; i++)
                            {
                                string timestamp = i < _timestampHistory.Count ? _timestampHistory[i] : "";
                                string alarmType = i < _alarmTypeHistory.Count ? _alarmTypeHistory[i] : "正常";
                                string airQuality = i < _airQualityHistory.Count ? _airQualityHistory[i] : "";
                                string servoStatus = i < _servoStatusHistory.Count ? _servoStatusHistory[i] : "";
                                string motorStatus = i < _motorStatusHistory.Count ? _motorStatusHistory[i] : "";
                                double waterLevel = i < _waterHistory.Count ? _waterHistory[i] : 0;
                                double lightLevel = i < _lightHistory.Count ? _lightHistory[i] : 0;

                                // 修复: 原来直接用 {x:F1} 插值, 受区域设置影响(德语区会写成 25,5, 把 CSV 列拆坏)。
                                //       导出格式统一钉死为 InvariantCulture。
                                sw.WriteLine(string.Format(CultureInfo.InvariantCulture,
                                    "{0},{1:F1},{2:F1},{3:F0},{4:F0},{5},{6},{7},{8},{9}",
                                    i + 1, _tempHistory[i], _humHistory[i], waterLevel, lightLevel,
                                    airQuality, servoStatus, motorStatus, alarmType, timestamp));
                            }
                        }
                        LogMessage($"数据已导出到: {sfd.FileName}");
                        MessageBox.Show("数据导出成功！", "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"导出失败:\n{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        LogMessage($"错误: 导出数据失败 - {ex.Message}");
                    }
                }
            }
        }

        // 关键修复：改进的串口数据接收处理
        private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                // 读取所有可用数据到缓冲区
                int bytesToRead = _serialPort.BytesToRead;
                byte[] buffer = new byte[bytesToRead];
                _serialPort.Read(buffer, 0, bytesToRead);
                string data = Encoding.UTF8.GetString(buffer);

                _receivedBuffer.Append(data);

                // 处理缓冲区中的完整行（支持多种换行符）
                string bufferContent = _receivedBuffer.ToString();
                string[] lines = bufferContent.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

                // 处理除最后一个元素外的所有行（最后一个可能不完整）
                for (int i = 0; i < lines.Length - 1; i++)
                {
                    string line = lines[i].Trim();
                    if (!string.IsNullOrEmpty(line))
                    {
                        ProcessReceivedLine(line);
                    }
                }

                // 保留最后一个不完整的部分
                _receivedBuffer.Clear();
                _receivedBuffer.Append(lines[lines.Length - 1]);

                // 防止缓冲区无限增长
                if (_receivedBuffer.Length > 1000)
                {
                    _receivedBuffer.Clear();
                }
            }
            catch (Exception ex)
            {
                LogMessage($"数据接收错误: {ex.Message}");
            }
        }

        private void ProcessReceivedLine(string line)
        {
            try
            {
                // 记录原始数据
                SafeInvoke(() => LogMessage($"接收: {line}"));

                // 解析数据
                var parsed = ParseData(line);
                if (!parsed.HasValue)
                {
                    SafeInvoke(() => LogMessage($"解析失败: 数据格式不正确", Color.Red));
                    return;
                }

                var p = parsed.Value;
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                SafeInvoke(delegate
                {
                    // 温湿度可能缺失(DHT11 读失败), 此时其余字段照常刷新, 不再整帧丢弃
                    if (p.HasTempHum)
                    {
                        tempLabel.Text = p.Temp.ToString("F1", CultureInfo.InvariantCulture);
                        humLabel.Text = p.Hum.ToString("F1", CultureInfo.InvariantCulture);
                    }

                    waterLabel.Text = _waterLevel.ToString(CultureInfo.InvariantCulture);
                    lightLabel.Text = _lightLevel.ToString(CultureInfo.InvariantCulture);
                    airQualityLabel.Text = _airQuality;
                    servoLabel.Text = _servoStatus == "ON" ? "开启" : "关闭";
                    motorLabel.Text = _motorStatus == "ON" ? "开启" : "关闭";

                    // 更新空气质量图标和颜色
                    if (_airQuality == "bad")
                    {
                        airQualityIconPictureBox.Image = LoadIconSafely("ICON\\空气质量劣状态.png");
                        airQualityLabel.ForeColor = dangerColor;
                    }
                    else
                    {
                        airQualityIconPictureBox.Image = LoadIconSafely("ICON\\空气质量优状态.png");
                        airQualityLabel.ForeColor = successColor;
                    }

                    // 更新土壤湿度图标(统一阈值: >40%过湿报警)
                    if (_waterLevel > _soilMax)
                    {
                        waterIconPictureBox.Image = LoadIconSafely("ICON\\水位_报警状态.png");
                    }
                    else
                    {
                        waterIconPictureBox.Image = LoadIconSafely("ICON\\水位.png");
                    }

                    // 更新窗户图标
                    if (_servoStatus == "ON")
                    {
                        windowIconPictureBox.Image = LoadIconSafely("ICON\\窗户-开状态.png");
                    }
                    else
                    {
                        windowIconPictureBox.Image = LoadIconSafely("ICON\\窗户-关状态.png");
                    }

                    // 更新风扇图标
                    if (_motorStatus == "ON")
                    {
                        fanIconPictureBox.Image = LoadIconSafely("ICON\\风扇-开状态.png");
                    }
                    else
                    {
                        fanIconPictureBox.Image = LoadIconSafely("ICON\\风扇-关状态.png");
                    }

                    // 更新蜂鸣器图标（根据报警状态）
                    bool hasAlarm = (p.HasTempHum && p.Temp > _tempMax) ||
                                   (p.HasTempHum && p.Hum > _humMax) ||
                                   (_waterLevel > _soilMax) ||
                                   (_airQuality == "bad");
                    if (hasAlarm)
                    {
                        buzzerIconPictureBox.Image = LoadIconSafely("ICON\\蜂鸣器-开状态.png");
                    }
                    else
                    {
                        buzzerIconPictureBox.Image = LoadIconSafely("ICON\\蜂鸣器-关状态.png");
                    }

                    if (!p.HasTempHum)
                    {
                        LogMessage($"DHT11 无温湿度数据, 已跳过温度/湿度/图表更新(土壤={_waterLevel}%, 光照={_lightLevel}%)", warningColor);
                        return;
                    }

                    // 获取报警类型
                    string alarmType = GetAlarmType(p.Temp, p.Hum);

                    // 添加到图表，包含时间戳和报警类型
                    AddToChart(p.Temp, p.Hum, _waterLevel, timestamp, alarmType);
                    chartPanel.Invalidate();
                    LogMessage($"解析成功: 温度={p.Temp:F1}°C, 湿度={p.Hum:F1}%, 土壤湿度={_waterLevel}%, 光照={_lightLevel}%", Color.Green);

                    // 检查报警状态
                    UpdateAlarmStatus(p.Temp, p.Hum);

                    // 发布数据到MQTT
                    PublishDataToMqtt(p.Temp, p.Hum);
                });
            }
            catch (Exception ex)
            {
                // 修复: 原 catch 里再次 this.Invoke, 关窗瞬间会二次抛 ObjectDisposedException 并逃逸到线程池导致进程终止。
                //       改成 SafeInvoke(内部吞掉 ObjectDisposedException/InvalidOperationException) + 兜底 catch。
                SafeInvoke(() => LogMessage($"处理数据异常: {ex.Message}", Color.Red));
            }
        }

        /// <summary>
        /// 一帧解析结果。温湿度可能缺失(固件 DHT11 读取失败时只打印 DHT11 Read Failed!，
        /// 但 Soil/Light/Servo/Motor/Air 仍会正常输出)，此时 HasTempHum=false，其余字段照常可用。
        /// </summary>
        private struct FrameData
        {
            public bool HasTempHum;
            public double Temp;
            public double Hum;
        }

        private FrameData? ParseData(string raw)
        {
            try
            {
                string s = raw.Trim();
                if (string.IsNullOrEmpty(s)) return null;

                // 固定使用STM32格式解析: Temp:XXC   Humi:XX%RH
                // 修复: 原正则 Temp:(\d+)C 吃不到负号, 固件 printf("Temp:%dC") 在零下会输出 Temp:-5C,
                //       导致整帧被判"格式不正确"(连 Humi 一起白解析)。改为允许可选的负号与小数。
                var tempMatch = Regex.Match(s, @"Temp:(-?\d+(?:\.\d+)?)C");
                var humMatch = Regex.Match(s, @"Humi:(\d+(?:\.\d+)?)%RH");

                double temp = 0, hum = 0;
                bool hasTempHum = false;

                if (tempMatch.Success && humMatch.Success
                    && double.TryParse(tempMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out temp)
                    && double.TryParse(humMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out hum))
                {
                    // 合理性检查放在写状态之前
                    // 修复: 原实现先 ParseSTM32ExtendedData 写全局字段、后做范围检查,
                    //       非法帧虽然返回 null, 但 _waterLevel/_servoStatus 等已经被污染了。
                    if (temp >= -50 && temp <= 100 && hum >= 0 && hum <= 100)
                    {
                        hasTempHum = true;
                    }
                }

                // 扩展数据(土壤/光照/舵机/电机/空气)在这帧里是否存在, 与温湿度是否有效相互独立
                bool hasExtended = ParseSTM32ExtendedData(s);

                // 温湿度无效且没有任何扩展字段 → 这行根本不是数据帧(比如启动横幅)
                if (!hasTempHum && !hasExtended) return null;

                var frame = new FrameData { HasTempHum = hasTempHum, Temp = temp, Hum = hum };
                return frame;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ParseData异常: {ex.Message}");
                return null;
            }
        }

        // 解析STM32扩展数据
        /// <summary>
        /// 解析固件帧里的扩展字段。返回 true 表示这行里至少出现了一个扩展字段。
        /// 修复: 原实现返回 void 且用 int.Parse, 一旦字段异常就整帧丢弃; 现改为 TryParse, 单个字段坏掉不影响其它字段。
        /// </summary>
        private bool ParseSTM32ExtendedData(string data)
        {
            bool parsedAny = false;
            try
            {
                // 解析土壤湿度: Soil:XX%(STM32固件输出格式, raw值用于校准)
                var waterMatch = Regex.Match(data, @"Soil:(\d+)%");
                if (waterMatch.Success)
                {
                    if (int.TryParse(waterMatch.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int water))
                    {
                        _waterLevel = water;
                    }
                    parsedAny = true;
                }

                // 解析光照强度: Light:XX%(固件 Light_GetPercent 输出)
                var lightMatch = Regex.Match(data, @"Light:(\d+)%");
                if (lightMatch.Success)
                {
                    if (int.TryParse(lightMatch.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int light))
                    {
                        _lightLevel = light;
                    }
                    parsedAny = true;
                }

                // 解析舵机状态: Servo:ON/OFF
                var servoMatch = Regex.Match(data, @"Servo:(ON|OFF)");
                if (servoMatch.Success)
                {
                    _servoStatus = servoMatch.Groups[1].Value;
                    parsedAny = true;
                }

                // 解析电机状态: Motor:ON/OFF
                var motorMatch = Regex.Match(data, @"Motor:(ON|OFF)");
                if (motorMatch.Success)
                {
                    _motorStatus = motorMatch.Groups[1].Value;
                    parsedAny = true;
                }

                // 解析空气质量: Air:nice/bad
                var airMatch = Regex.Match(data, @"Air:(nice|bad)");
                if (airMatch.Success)
                {
                    _airQuality = airMatch.Groups[1].Value;
                    parsedAny = true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ParseSTM32ExtendedData异常: {ex.Message}");
            }
            return parsedAny;
        }

        private void AddToChart(double temp, double hum, double water, string timestamp, string alarmType)
        {
            _tempHistory.Add(temp);
            _humHistory.Add(hum);
            _waterHistory.Add(water);
            _lightHistory.Add(_lightLevel);
            _timestampHistory.Add(timestamp);
            _alarmTypeHistory.Add(alarmType);
            _airQualityHistory.Add(_airQuality);
            _servoStatusHistory.Add(_servoStatus);
            _motorStatusHistory.Add(_motorStatus);
            if (_tempHistory.Count > MaxHistoryPoints)
            {
                _tempHistory.RemoveAt(0);
                _humHistory.RemoveAt(0);
                _waterHistory.RemoveAt(0);
                _lightHistory.RemoveAt(0);
                _timestampHistory.RemoveAt(0);
                _alarmTypeHistory.RemoveAt(0);
                _airQualityHistory.RemoveAt(0);
                _servoStatusHistory.RemoveAt(0);
                _motorStatusHistory.RemoveAt(0);
            }
        }

        /// <summary>
        /// 安全地把动作投递到 UI 线程。
        /// 修复: 原代码在后台线程里裸调 this.Invoke, 关窗瞬间句柄已销毁会抛 ObjectDisposedException,
        ///       而 catch 块里再 Invoke 一次会二次抛出, 异常逃出 SerialPort.DataReceived 落到线程池 →
        ///       .NET Framework 直接终止进程。所有跨线程 UI 更新统一走这里。
        /// </summary>
        private bool SafeInvoke(Action action)
        {
            try
            {
                if (IsDisposed || Disposing || !IsHandleCreated) return false;
                if (InvokeRequired)
                {
                    Invoke((MethodInvoker)delegate { action(); });
                }
                else
                {
                    action();
                }
                return true;
            }
            catch (ObjectDisposedException) { return false; }
            catch (InvalidOperationException) { return false; }
        }

        private void LogMessage(string message, Color? color = null)
        {
            try
            {
                string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
                string logEntry = $"[{timestamp}] {message}\r\n";

                SafeInvoke(() => UpdateLogTextBox(logEntry));
            }
            catch { }
        }

        private void UpdateLogTextBox(string logEntry)
        {
            logTextBox.AppendText(logEntry);
            _logCharCount += logEntry.Length;

            // 修复: 原实现只追加不回收, 4 行/秒 → 每小时 1MB+, AppendText/ScrollToCaret 都是 O(n), 跑半天界面明显卡。
            //       超过上限时砍掉前半部分, 只保留最近的 MaxLogTextLength/2 个字符。
            //       用 _logCharCount 记账而不是每帧读 logTextBox.TextLength (后者要跨进程取长度, 且日志长了会拖慢整体刷新)。
            if (_logCharCount > MaxLogTextLength)
            {
                int keepLength = MaxLogTextLength / 2;
                string current = logTextBox.Text;
                int cut = current.Length > keepLength ? current.Length - keepLength : 0;
                logTextBox.Text = current.Substring(cut);
                _logCharCount = logTextBox.TextLength;
                logTextBox.SelectionStart = logTextBox.TextLength;
                logTextBox.SelectionLength = 0;
            }

            if (autoScrollCheckBox.Checked)
            {
                logTextBox.SelectionStart = logTextBox.TextLength;
                logTextBox.ScrollToCaret();
            }
        }

        private void ChartPanel_Paint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(chartBgColor);

            var rect = chartPanel.ClientRectangle;
            int leftMargin = 60;
            int rightMargin = 60;
            int topMargin = 30;
            int bottomMargin = 50;

            int left = rect.Left + leftMargin;
            int top = rect.Top + topMargin;
            int right = rect.Right - rightMargin;
            int bottom = rect.Bottom - bottomMargin;

            if (right <= left || bottom <= top) return;

            // 绘制背景网格
            DrawGrid(g, left, top, right, bottom);

            // 绘制坐标轴
            using (var axisPen = new Pen(darkColor, 2))
            {
                g.DrawLine(axisPen, left, top, left, bottom);           // Y轴
                g.DrawLine(axisPen, left, bottom, right, bottom);       // X轴
            }

            // 绘制Y轴刻度 (左侧：温度 0-40°C)
            DrawTemperatureYAxis(g, left, top, bottom);

            // 绘制Y轴刻度 (右侧：湿度 0-100%)
            DrawHumidityYAxis(g, right, top, bottom);

            // 绘制X轴刻度
            DrawXAxis(g, left, right, bottom);

            // 绘制数据曲线
            if (_tempHistory.Count > 1)
                DrawTemperatureSeries(g, _tempHistory, left, top, right, bottom);
            if (_humHistory.Count > 1)
                DrawHumiditySeries(g, _humHistory, left, top, right, bottom);
            if (_waterHistory.Count > 1)
                DrawWaterSeries(g, _waterHistory, left, top, right, bottom);
            if (_lightHistory.Count > 1)
                DrawLightSeries(g, _lightHistory, left, top, right, bottom);

            // 绘制图例
            DrawLegend(g, right - 150, top);

            // 绘制标题
            using (var titleFont = new Font("微软雅黑", 11F, FontStyle.Bold))
            using (var titleBrush = new SolidBrush(darkColor))
            {
                string title = $"数据点数: {_tempHistory.Count}/{MaxHistoryPoints}";
                g.DrawString(title, titleFont, titleBrush, left, top - 25);
            }
        }

        // 水平网格分段数。温度轴与湿度轴都用这个值, 保证三者的刻度线重合。
        private const int ChartHSteps = 10;
        // 垂直网格分段数
        private const int ChartVSteps = 10;

        private void DrawGrid(Graphics g, int left, int top, int right, int bottom)
        {
            using (var gridPen = new Pen(Color.FromArgb(230, 230, 230), 1))
            {
                gridPen.DashStyle = DashStyle.Dot;

                // 水平网格线
                // 修复: 原来 hSteps=8, 而 DrawHumidityYAxis 用 steps=10 → 右侧 0/10/…/100 的刻度全落在网格线之间。
                //       统一为 ChartHSteps=10: 温度 0/4/…/40、湿度 0/10/…/100 都正好落在网格线上。
                int hSteps = ChartHSteps;
                for (int i = 0; i <= hSteps; i++)
                {
                    int y = top + (bottom - top) * i / hSteps;
                    g.DrawLine(gridPen, left, y, right, y);
                }

                // 垂直网格线
                int vSteps = ChartVSteps;
                for (int i = 0; i <= vSteps; i++)
                {
                    int x = left + (right - left) * i / vSteps;
                    g.DrawLine(gridPen, x, top, x, bottom);
                }
            }
        }

        private void DrawTemperatureYAxis(Graphics g, int x, int top, int bottom)
        {
            using (var font = new Font("Arial", 9F))
            using (var brush = new SolidBrush(dangerColor))
            using (var tickPen = new Pen(darkColor, 1))   // 修复: 原来在循环里 9 次 new Pen 且从不释放
            {
                int steps = ChartHSteps;
                for (int i = 0; i <= steps; i++)
                {
                    int y = bottom - (bottom - top) * i / steps;
                    double value = 40.0 * i / steps;  // 0-40度

                    // 刻度线
                    g.DrawLine(tickPen, x - 5, y, x, y);

                    // 刻度值
                    string label = value.ToString("F0", CultureInfo.InvariantCulture);
                    SizeF size = g.MeasureString(label, font);
                    g.DrawString(label, font, brush, x - size.Width - 10, y - size.Height / 2);
                }

                // Y轴标签
                using (var labelFont = new Font("微软雅黑", 10F, FontStyle.Bold))
                {
                    string axisLabel = "温度(°C)";
                    SizeF labelSize = g.MeasureString(axisLabel, labelFont);
                    g.DrawString(axisLabel, labelFont, brush, x - labelSize.Width + 2, top - 25);
                }
            }
        }

        private void DrawHumidityYAxis(Graphics g, int x, int top, int bottom)
        {
            using (var font = new Font("Arial", 9F))
            using (var brush = new SolidBrush(primaryColor))
            using (var tickPen = new Pen(darkColor, 1))   // 修复: 原来在循环里 11 次 new Pen 且从不释放
            {
                int steps = ChartHSteps;
                for (int i = 0; i <= steps; i++)
                {
                    int y = bottom - (bottom - top) * i / steps;
                    double value = 100.0 * i / steps;  // 0-100%

                    // 刻度线
                    g.DrawLine(tickPen, x, y, x + 5, y);

                    // 刻度值
                    string label = value.ToString("F0", CultureInfo.InvariantCulture);
                    g.DrawString(label, font, brush, x + 10, y - font.Height / 2);
                }

                // Y轴标签
                using (var labelFont = new Font("微软雅黑", 10F, FontStyle.Bold))
                {
                    string axisLabel = "湿度(%)";
                    g.DrawString(axisLabel, labelFont, brush, x + 10, top - 25);
                }
            }
        }

        private void DrawXAxis(Graphics g, int left, int right, int bottom)
        {
            using (var font = new Font("Arial", 9F))
            using (var brush = new SolidBrush(darkColor))
            {
                // X轴标签
                string label = "数据序号 →";
                SizeF size = g.MeasureString(label, font);
                g.DrawString(label, font, brush, (left + right) / 2 - size.Width / 2, bottom + 25);
            }
        }

        private void DrawTemperatureSeries(Graphics g, List<double> data, int left, int top, int right, int bottom)
        {
            int count = data.Count;
            if (count < 1) return;

            float width = right - left;
            float height = bottom - top;
            float xStep = count > 1 ? width / (count - 1) : 0;

            using (var pen = new Pen(dangerColor, 3))
            using (var shadowPen = new Pen(Color.FromArgb(50, dangerColor), 5))
            using (var pointBrush = new SolidBrush(dangerColor))   // 修复: 原来循环内每点 new SolidBrush 从不释放
            {
                pen.LineJoin = LineJoin.Round;

                PointF? prev = null;
                for (int i = 0; i < count; i++)
                {
                    float x = left + i * xStep;
                    double v = data[i];

                    // 映射到 0-40 度范围
                    float ratio = (float)(v / 40.0);
                    if (ratio < 0) ratio = 0;
                    if (ratio > 1) ratio = 1;

                    float y = bottom - ratio * height;
                    var cur = new PointF(x, y);

                    if (prev.HasValue)
                    {
                        g.DrawLine(shadowPen, prev.Value, cur);  // 阴影
                        g.DrawLine(pen, prev.Value, cur);         // 主线
                    }

                    // 绘制数据点
                    g.FillEllipse(pointBrush, x - 3, y - 3, 6, 6);

                    prev = cur;
                }
            }
        }

        private void DrawHumiditySeries(Graphics g, List<double> data, int left, int top, int right, int bottom)
        {
            int count = data.Count;
            if (count < 1) return;

            float width = right - left;
            float height = bottom - top;
            float xStep = count > 1 ? width / (count - 1) : 0;

            using (var pen = new Pen(primaryColor, 3))
            using (var shadowPen = new Pen(Color.FromArgb(50, primaryColor), 5))
            using (var pointBrush = new SolidBrush(primaryColor))   // 修复: 原来循环内每点 new SolidBrush 从不释放
            {
                pen.LineJoin = LineJoin.Round;

                PointF? prev = null;
                for (int i = 0; i < count; i++)
                {
                    float x = left + i * xStep;
                    double v = data[i];

                    // 映射到 0-100% 范围
                    float ratio = (float)(v / 100.0);
                    if (ratio < 0) ratio = 0;
                    if (ratio > 1) ratio = 1;

                    float y = bottom - ratio * height;
                    var cur = new PointF(x, y);

                    if (prev.HasValue)
                    {
                        g.DrawLine(shadowPen, prev.Value, cur);  // 阴影
                        g.DrawLine(pen, prev.Value, cur);         // 主线
                    }

                    // 绘制数据点
                    g.FillEllipse(pointBrush, x - 3, y - 3, 6, 6);

                    prev = cur;
                }
            }
        }

        private void DrawWaterSeries(Graphics g, List<double> data, int left, int top, int right, int bottom)
        {
            int count = data.Count;
            if (count < 1) return;

            float width = right - left;
            float height = bottom - top;
            float xStep = count > 1 ? width / (count - 1) : 0;

            Color waterColor = Color.FromArgb(255, 193, 7);  // 黄色
            using (var pen = new Pen(waterColor, 3))
            using (var shadowPen = new Pen(Color.FromArgb(50, waterColor), 5))
            using (var pointBrush = new SolidBrush(waterColor))   // 修复: 原来循环内每点 new SolidBrush 从不释放
            {
                pen.LineJoin = LineJoin.Round;

                PointF? prev = null;
                for (int i = 0; i < count; i++)
                {
                    float x = left + i * xStep;
                    double v = data[i];

                    // 映射到 0-100% 范围（与湿度共用Y轴）
                    float ratio = (float)(v / 100.0);
                    if (ratio < 0) ratio = 0;
                    if (ratio > 1) ratio = 1;

                    float y = bottom - ratio * height;
                    var cur = new PointF(x, y);

                    if (prev.HasValue)
                    {
                        g.DrawLine(shadowPen, prev.Value, cur);  // 阴影
                        g.DrawLine(pen, prev.Value, cur);         // 主线
                    }

                    // 绘制数据点
                    g.FillEllipse(pointBrush, x - 3, y - 3, 6, 6);

                    prev = cur;
                }
            }
        }

        private void DrawLightSeries(Graphics g, List<double> data, int left, int top, int right, int bottom)
        {
            int count = data.Count;
            if (count < 1) return;

            float width = right - left;
            float height = bottom - top;
            float xStep = count > 1 ? width / (count - 1) : 0;

            Color lightColor = Color.FromArgb(255, 152, 0);  // 橙色(与土壤湿度黄色区分)
            using (var pen = new Pen(lightColor, 3))
            using (var shadowPen = new Pen(Color.FromArgb(50, lightColor), 5))
            using (var pointBrush = new SolidBrush(lightColor))   // 修复: 原来循环内每点 new SolidBrush 从不释放
            {
                pen.LineJoin = LineJoin.Round;

                PointF? prev = null;
                for (int i = 0; i < count; i++)
                {
                    float x = left + i * xStep;
                    double v = data[i];

                    // 映射到 0-100% 范围（与湿度共用Y轴）
                    float ratio = (float)(v / 100.0);
                    if (ratio < 0) ratio = 0;
                    if (ratio > 1) ratio = 1;

                    float y = bottom - ratio * height;
                    var cur = new PointF(x, y);

                    if (prev.HasValue)
                    {
                        g.DrawLine(shadowPen, prev.Value, cur);  // 阴影
                        g.DrawLine(pen, prev.Value, cur);         // 主线
                    }

                    // 绘制数据点
                    g.FillEllipse(pointBrush, x - 3, y - 3, 6, 6);

                    prev = cur;
                }
            }
        }

        private void DrawLegend(Graphics g, int x, int y)
        {
            // 修复: 原来 4 组图例各自 new Pen + new SolidBrush 且全部不释放(每次重绘漏 8 个 GDI 句柄),
            //       改成按颜色复用 4 个 Pen + 4 个 Brush, 统一 using 释放。
            Color waterColor = Color.FromArgb(255, 193, 7);
            Color lightColor = Color.FromArgb(255, 152, 0);

            using (var font = new Font("微软雅黑", 9F))
            using (var tempPen = new Pen(dangerColor, 3))
            using (var humPen = new Pen(primaryColor, 3))
            using (var waterPen = new Pen(waterColor, 3))
            using (var lightPen = new Pen(lightColor, 3))
            using (var tempBrush = new SolidBrush(dangerColor))
            using (var humBrush = new SolidBrush(primaryColor))
            using (var waterBrush = new SolidBrush(waterColor))
            using (var lightBrush = new SolidBrush(lightColor))
            {
                // 温度图例
                g.DrawLine(tempPen, x, y + 5, x + 30, y + 5);
                g.FillEllipse(tempBrush, x + 12, y + 2, 6, 6);
                g.DrawString("温度", font, tempBrush, x + 35, y);

                // 湿度图例
                g.DrawLine(humPen, x, y + 25, x + 30, y + 25);
                g.FillEllipse(humBrush, x + 12, y + 22, 6, 6);
                g.DrawString("湿度", font, humBrush, x + 35, y + 20);

                // 土壤湿度图例
                g.DrawLine(waterPen, x, y + 45, x + 30, y + 45);
                g.FillEllipse(waterBrush, x + 12, y + 42, 6, 6);
                g.DrawString("土壤湿度", font, waterBrush, x + 35, y + 40);

                // 光照图例
                g.DrawLine(lightPen, x, y + 65, x + 30, y + 65);
                g.FillEllipse(lightBrush, x + 12, y + 62, 6, 6);
                g.DrawString("光照", font, lightBrush, x + 35, y + 60);
            }
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            try
            {
                // 断开串口
                if (_serialPort != null && _serialPort.IsOpen)
                {
                    _serialPort.DataReceived -= SerialPort_DataReceived;
                    _serialPort.Close();
                    _serialPort.Dispose();
                }

                // 断开MQTT
                // 修复: 原来在 UI 线程上 _mqttClient.DisconnectAsync().Wait(1000) 同步等异步,
                //       最轻是每次关窗固定僵 1 秒, 最重是 SynchronizationContext 捕获导致死锁。
                //       改为把断开动作丢到线程池执行, UI 线程只用 Task.WhenAny 做有超时的等待,
                //       超时后照样继续关窗(不再无限期阻塞), 最后无论成功与否都释放客户端。
                if (_mqttClient != null)
                {
                    try
                    {
                        var disconnectTask = Task.Run(async () =>
                        {
                            if (_mqttClient.IsConnected)
                            {
                                await _mqttClient.DisconnectAsync();
                            }
                        });

                        if (!disconnectTask.Wait(1500))
                        {
                            System.Diagnostics.Debug.WriteLine("MQTT 断开超时, 继续关闭窗口");
                        }
                    }
                    catch { }
                    finally
                    {
                        try { _mqttClient.Dispose(); } catch { }
                    }
                }


                // 释放图标缓存(必须在窗体销毁前, 且此时不再有后台线程取用)
                DisposeIconCache();
            }
            catch { }
        }

        // 窗口尺寸变化: 统一交给 ApplyResponsiveLayout 重算全部坐标,
        // 不再像原实现那样「构造写一套、Resize 改另一套」, 两套数值不一致就会错位。
        private void Form1_Resize(object sender, EventArgs e)
        {
            if (this.WindowState == FormWindowState.Minimized) return;

            try
            {
                ApplyResponsiveLayout();
            }
            catch { }
        }

        /// <summary>用户点了顶栏「注销」: 置位后关闭本窗体, Program.cs 会据此回到登录页。</summary>
        public bool LogoutRequested { get; private set; }

        private void LogoutButton_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show("确定要注销当前账号并返回登录页吗?", "注销",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }

            LogoutRequested = true;
            this.Close();
        }

#region MQTT相关方法

        // 初始化MQTT客户端
        private void InitializeMqttClient()
        {
            try
            {
                var factory = new MqttFactory();
                _mqttClient = factory.CreateMqttClient();

                // 修复: 处理器在这里挂载一次即可, 客户端对象全生命周期只有这一个,
                //       不能放在 SubscribeToTopic 里(每重连一次就多挂一份, 命令会被重复转发到串口)。
                AttachMqttMessageHandler();

                LogMessage("MQTT客户端已初始化");
            }
            catch (Exception ex)
            {
                LogMessage($"MQTT客户端初始化失败: {ex.Message}", Color.Red);
            }
        }

        /// <summary>
        /// 解析 MQTT Broker 地址。支持 host / host:port / mqtt://host:port 三种写法, 端口非法时返回 false。
        /// </summary>
        private static bool TryParseBrokerAddress(string text, out string server, out int port)
        {
            server = null;
            port = 1883;

            if (string.IsNullOrWhiteSpace(text)) return false;

            string s = text.Trim();

            // 容忍带 scheme 的写法 (mqtt:// / tcp://)
            int schemeIndex = s.IndexOf("://", StringComparison.Ordinal);
            if (schemeIndex >= 0)
            {
                s = s.Substring(schemeIndex + 3);
            }

            // 去掉可能存在的路径部分
            int slashIndex = s.IndexOf('/');
            if (slashIndex >= 0)
            {
                s = s.Substring(0, slashIndex);
            }

            if (s.Length == 0) return false;

            string[] parts = s.Split(':');
            if (parts.Length > 2) return false;   // IPv6 未做支持, 明确拒绝而不是静默截断

            server = parts[0].Trim();
            if (server.Length == 0) return false;

            if (parts.Length == 2)
            {
                if (!int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out port)) return false;
                if (port <= 0 || port > 65535) return false;
            }

            return true;
        }

        // MQTT连接按钮点击事件
        private async void MqttConnectButton_Click(object sender, EventArgs e)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(mqttBrokerTextBox.Text))
                {
                    MessageBox.Show("请输入MQTT服务器地址!", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (string.IsNullOrWhiteSpace(mqttTopicTextBox.Text))
                {
                    MessageBox.Show("请输入MQTT发布主题!", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // 解析服务器地址和端口
                // 修复: 原来裸用 int.Parse, 填 "192.168.1.5:abc" 会抛 FormatException;
                //       且 Split(':') 不支持 mqtt://host:1883 这类带 scheme 的写法。改用 TryParse 给出明确提示。
                string brokerText = mqttBrokerTextBox.Text.Trim();
                string server;
                int port;

                if (!TryParseBrokerAddress(brokerText, out server, out port))
                {
                    MessageBox.Show("MQTT服务器地址格式不正确, 正确示例: 192.168.1.5:1883", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // 读取发布主题和订阅主题
                _mqttPublishTopic = mqttTopicTextBox.Text.Trim();
                _mqttSubscribeTopic = mqttSubscribeTopicTextBox.Text.Trim();

                UpdateMqttStatus("连接中...", Color.Orange);
                LogMessage($"正在连接MQTT服务器: {mqttBrokerTextBox.Text}");

                // 创建连接选项
                var options = new MqttClientOptionsBuilder()
                    .WithTcpServer(server, port)
                    .WithClientId("DHT11_Client_" + Guid.NewGuid().ToString())
                    .WithCleanSession()
                    .Build();

                // 连接到MQTT服务器
                var result = await _mqttClient.ConnectAsync(options);

                if (result.ResultCode == MqttClientConnectResultCode.Success)
                {
                    UpdateMqttStatus("已连接", Color.Green);
                    mqttConnectButton.Enabled = false;
                    mqttDisconnectButton.Enabled = true;
                    mqttBrokerTextBox.Enabled = false;
                    mqttTopicTextBox.Enabled = false;
                    mqttSubscribeTopicTextBox.Enabled = false;

                    LogMessage($"MQTT连接成功: {mqttBrokerTextBox.Text}", Color.Green);
                    LogMessage($"发布主题: {_mqttPublishTopic}");

                    // 订阅主题
                    if (!string.IsNullOrWhiteSpace(_mqttSubscribeTopic))
                    {
                        await SubscribeToTopic(_mqttSubscribeTopic);
                    }
                }
                else
                {
                    UpdateMqttStatus("连接失败", Color.Red);
                    LogMessage($"MQTT连接失败: {result.ResultCode}", Color.Red);
                    MessageBox.Show($"MQTT连接失败: {result.ResultCode}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                UpdateMqttStatus("连接失败", Color.Red);
                LogMessage($"MQTT连接异常: {ex.Message}", Color.Red);
                MessageBox.Show($"连接MQTT服务器失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // MQTT断开按钮点击事件
        private async void MqttDisconnectButton_Click(object sender, EventArgs e)
        {
            try
            {
                if (_mqttClient != null && _mqttClient.IsConnected)
                {
                    await _mqttClient.DisconnectAsync();
                    UpdateMqttStatus("未连接", Color.Gray);
                    mqttConnectButton.Enabled = true;
                    mqttDisconnectButton.Enabled = false;
                    mqttBrokerTextBox.Enabled = true;
                    mqttTopicTextBox.Enabled = true;
                    mqttSubscribeTopicTextBox.Enabled = true;

                    LogMessage("MQTT已断开连接");
                }
            }
            catch (Exception ex)
            {
                LogMessage($"MQTT断开连接失败: {ex.Message}", Color.Red);
                MessageBox.Show($"断开MQTT连接失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // 自动发布复选框状态改变事件
        private void MqttAutoPublishCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            _mqttAutoPublish = mqttAutoPublishCheckBox.Checked;
            LogMessage($"自动发布MQTT数据: {(_mqttAutoPublish ? "已开启" : "已关闭")}");
        }

        // 发布温湿度数据到MQTT
        private async void PublishDataToMqtt(double temperature, double humidity)
        {
            try
            {
                if (_mqttClient == null || !_mqttClient.IsConnected)
                {
                    return; // 如果未连接，不发送
                }

                if (!_mqttAutoPublish)
                {
                    return; // 如果未开启自动发布，不发送
                }

                // 获取报警状态(统一阈值方案, 与固件/小程序一致)
                string alarmType = GetAlarmType(temperature, humidity);
                bool hasAlarm = (alarmType != "正常");

                // 构建JSON格式的数据(soilMoisture=土壤湿度%, 与小程序字段对应)
                // 修复: 原来直接用 {temperature} 插值, 受当前区域设置影响 —— 德语/法语等小数点是逗号,
                //       会拼出 "temperature": 25,5 这种非法 JSON, 小程序端 JSON.parse 直接失败。
                //       统一用 InvariantCulture 格式化, 保证小数点永远是 '.'。
                string jsonData = $@"{{
    ""timestamp"": ""{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)}"",
    ""temperature"": {temperature.ToString("F1", CultureInfo.InvariantCulture)},
    ""humidity"": {humidity.ToString("F1", CultureInfo.InvariantCulture)},
    ""soilMoisture"": {_waterLevel.ToString(CultureInfo.InvariantCulture)},
    ""lightPercent"": {_lightLevel.ToString(CultureInfo.InvariantCulture)},
    ""airQuality"": ""{_airQuality}"",
    ""servoStatus"": ""{_servoStatus}"",
    ""motorStatus"": ""{_motorStatus}"",
    ""alarmType"": ""{alarmType}"",
    ""hasAlarm"": {hasAlarm.ToString().ToLowerInvariant()}
}}";

                var message = new MqttApplicationMessageBuilder()
                    .WithTopic(_mqttPublishTopic)
                    .WithPayload(jsonData)
                    .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce)
                    .WithRetainFlag(false)
                    .Build();

                await _mqttClient.PublishAsync(message);
                LogMessage($"[MQTT发送] 温度:{temperature}°C 湿度:{humidity}% 土壤湿度:{_waterLevel}% 光照:{_lightLevel}% 空气:{_airQuality} 报警:{alarmType}", Color.Blue);
            }
            catch (Exception ex)
            {
                LogMessage($"MQTT发布数据失败: {ex.Message}", Color.Red);
            }
        }

        // 更新MQTT连接状态标签
        private void UpdateMqttStatus(string status, Color color)
        {
            // 修复: 原来直接 Invoke, 关窗瞬间会抛 ObjectDisposedException 到 MQTT 回调线程; 统一走 SafeInvoke。
            SafeInvoke(delegate
            {
                mqttStatusLabel.Text = "● " + status;
                mqttStatusLabel.ForeColor = color;
            });
        }

        // 订阅MQTT主题
        private async Task SubscribeToTopic(string topic)
        {
            try
            {
                var subscribeOptions = new MqttClientSubscribeOptionsBuilder()
                    .WithTopicFilter(topic)
                    .Build();

                await _mqttClient.SubscribeAsync(subscribeOptions);
                LogMessage($"已订阅主题: {topic}", Color.Green);

                // 修复: 原来在这里 += OnMqttMessageReceived。_mqttClient 只在 InitializeMqttClient 创建一次,
                //       用户"断开→重连"一次就多挂一个处理器 → 一条 MQTT 命令被转发到串口 N 次(小程序按一次开窗,
                //       STM32 收到多次 DUOJ-1)。而且该 += 被"订阅主题非空"门控, 订阅主题留空时处理器压根不挂载,
                //       小程序控制彻底失灵。现在改为在 InitializeMqttClient 里只挂一次, 并用 _mqttHandlerAttached 防重。
                AttachMqttMessageHandler();
            }
            catch (Exception ex)
            {
                LogMessage($"订阅主题失败: {ex.Message}", Color.Red);
            }
        }

        // 挂载 MQTT 消息处理器(全生命周期只挂一次)
        private void AttachMqttMessageHandler()
        {
            if (_mqttClient == null || _mqttHandlerAttached) return;
            _mqttClient.ApplicationMessageReceivedAsync += OnMqttMessageReceived;
            _mqttHandlerAttached = true;
        }

        // MQTT消息接收处理
        private Task OnMqttMessageReceived(MqttApplicationMessageReceivedEventArgs e)
        {
            try
            {
                string topic = e.ApplicationMessage.Topic;
                string payload = Encoding.UTF8.GetString(e.ApplicationMessage.PayloadSegment.Array,
                    e.ApplicationMessage.PayloadSegment.Offset,
                    e.ApplicationMessage.PayloadSegment.Count);

                // 在UI线程中处理
                SafeInvoke(delegate
                {
                    LogMessage($"[MQTT接收] 主题:{topic} 消息:{payload}", Color.Purple);

                    // 将MQTT消息通过串口发送到单片机
                    if (_serialPort != null && _serialPort.IsOpen)
                    {
                        _serialPort.WriteLine(payload);
                        LogMessage($"[转发到串口] {payload}", Color.Blue);
                    }
                    else
                    {
                        LogMessage("串口未连接，无法转发消息", Color.Orange);
                    }
                });
            }
            catch (Exception ex)
            {
                LogMessage($"处理MQTT消息失败: {ex.Message}", Color.Red);
            }

            return Task.CompletedTask;
        }

        #endregion

        #region 设备控制方法

        // 发送串口命令
        private void SendSerialCommand(string command)
        {
            try
            {
                if (_serialPort != null && _serialPort.IsOpen)
                {
                    _serialPort.WriteLine(command);
                    LogMessage($"发送命令: {command}", Color.Blue);
                }
                else
                {
                    MessageBox.Show("串口未连接!", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                LogMessage($"发送命令失败: {ex.Message}", Color.Red);
                MessageBox.Show($"发送命令失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // 自动模式按钮
        private void ModeAutoButton_Click(object sender, EventArgs e)
        {
            SendSerialCommand("mode-0");
            modeStatusLabel.Text = "● 自动模式";
            modeStatusLabel.ForeColor = successColor;
            servoOnButton.Enabled = false;
            servoOffButton.Enabled = false;
            motorOnButton.Enabled = false;
            motorOffButton.Enabled = false;
        }

        // 手动模式按钮
        private void ModeManualButton_Click(object sender, EventArgs e)
        {
            SendSerialCommand("mode-1");
            modeStatusLabel.Text = "● 手动模式";
            modeStatusLabel.ForeColor = warningColor;
            servoOnButton.Enabled = true;
            servoOffButton.Enabled = true;
            motorOnButton.Enabled = true;
            motorOffButton.Enabled = true;
        }

        // 舵机关按钮
        private void ServoOffButton_Click(object sender, EventArgs e)
        {
            SendSerialCommand("DUOJ-0");
        }

        // 舵机开按钮
        private void ServoOnButton_Click(object sender, EventArgs e)
        {
            SendSerialCommand("DUOJ-1");
        }

        // 电机关按钮
        private void MotorOffButton_Click(object sender, EventArgs e)
        {
            SendSerialCommand("MOTOR-0");
        }

        // 电机开按钮
        private void MotorOnButton_Click(object sender, EventArgs e)
        {
            SendSerialCommand("MOTOR-1");
        }

        #endregion
    }
}
