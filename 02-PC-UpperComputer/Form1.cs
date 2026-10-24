using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Net.Http;
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
        private List<string> _timestampHistory = new List<string>();
        private List<string> _alarmTypeHistory = new List<string>();
        private List<string> _airQualityHistory = new List<string>();  // 空气质量历史
        private List<string> _servoStatusHistory = new List<string>();  // 窗户状态历史
        private List<string> _motorStatusHistory = new List<string>();  // 风扇状态历史
        private const int MaxHistoryPoints = 100;
        private StringBuilder _receivedBuffer = new StringBuilder();
        private StringBuilder _logBuffer = new StringBuilder();

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
        private CheckBox autoScrollCheckBox;
        private Label tempUnitLabel;
        private Label humUnitLabel;

        // 新增UI控件
        private Label waterLabel;
        private Label servoLabel;
        private Label motorLabel;
        private Label airQualityLabel;
        private PictureBox waterIconPictureBox;
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

        // 视频监控控件
        private Label videoLabel;
        private TextBox videoIpTextBox;
        private Button videoConnectButton;
        private Button videoDisconnectButton;
        private PictureBox videoPictureBox;
        private Label videoStatusLabel;

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

        // 数据格式解析配置（固定使用STM32格式）
        private string _currentDataFormat = "STM32格式";

        // 新增数据字段
        private int _waterLevel = 0;
        private string _servoStatus = "OFF";
        private string _motorStatus = "OFF";
        private string _airQuality = "nice";
        private bool _hasWarning = false;

        // MQTT客户端和配置
        private IMqttClient _mqttClient;
        private string _mqttPublishTopic = "green/data";   // 发布主题：传感器JSON数据发送到这里(与小程序订阅主题一致)
        private string _mqttSubscribeTopic = "green/control";  // 订阅主题：接收控制命令(原文转发串口)
        private bool _mqttAutoPublish = true;               // 是否自动发布数据到MQTT

        // 视频流相关
        private HttpClient _videoHttpClient;
        private CancellationTokenSource _videoCancellationToken;
        private bool _videoStreaming = false;

        public Form1()
        {
            InitializeComponent();  // 必须首先调用，初始化设计器生成的组件
            InitializeSerialPort();
            InitializeChart();
            InitializeCustomComponents();
            InitializeMqttClient();
            LoadAvailablePorts();
        }

        private void InitializeCustomComponents()
        {
            this.SuspendLayout();

            // 窗体设置
            this.Text = "智能温室";
            this.ClientSize = new Size(1350, 750);
            this.MinimumSize = new Size(1350, 750);  // 设置最小窗口尺寸
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MaximizeBox = true;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = lightColor;
            this.FormClosing += Form1_FormClosing;
            this.Resize += Form1_Resize;

            // ========== 控制面板 ==========
            Panel controlPanel = new Panel
            {
                Location = new Point(0, 0),
                Size = new Size(1350, 110),
                BackColor = Color.White
            };
            controlPanel.Paint += ControlPanel_Paint;

            // 串口选择
            portLabel = new Label
            {
                Location = new Point(20, 15),
                AutoSize = true,
                Text = "串口选择:",
                Font = new Font("微软雅黑", 10F),
                ForeColor = darkColor
            };

            comPortComboBox = new ComboBox
            {
                Location = new Point(20, 40),
                Width = 120,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("微软雅黑", 9F)
            };

            // 刷新按钮
            refreshButton = CreateStyledButton("刷新", new Point(145, 40), new Size(60, 27), primaryColor);
            refreshButton.Click += RefreshButton_Click;

            // 波特率选择
            baudRateLabel = new Label
            {
                Location = new Point(220, 15),
                AutoSize = true,
                Text = "波特率:",
                Font = new Font("微软雅黑", 10F),
                ForeColor = darkColor
            };

            baudRateComboBox = new ComboBox
            {
                Location = new Point(220, 40),
                Width = 100,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("微软雅黑", 9F)
            };
            baudRateComboBox.Items.AddRange(new object[] { "9600", "19200", "38400", "57600", "115200" });
            baudRateComboBox.SelectedIndex = 0;
            baudRateComboBox.SelectedIndexChanged += BaudRateComboBox_SelectedIndexChanged;

            // 连接按钮
            openButton = CreateStyledButton("连接串口", new Point(20, 75), new Size(90, 27), successColor);
            openButton.Click += OpenButton_Click;

            closeButton = CreateStyledButton("断开连接", new Point(120, 75), new Size(90, 27), dangerColor);
            closeButton.Click += CloseButton_Click;
            closeButton.Enabled = false;

            // 状态标签
            lblStatus = new Label
            {
                Location = new Point(230, 80),
                AutoSize = true,
                Text = "● 未连接",
                Font = new Font("微软雅黑", 10F, FontStyle.Bold),
                ForeColor = Color.Gray
            };

            // 清空数据按钮
            clearButton = CreateStyledButton("清空数据", new Point(360, 75), new Size(90, 27), warningColor);
            clearButton.Click += ClearButton_Click;

            // 导出数据按钮
            exportButton = CreateStyledButton("导出CSV", new Point(460, 75), new Size(90, 27), primaryColor);
            exportButton.Click += ExportButton_Click;

            // ========== MQTT配置区域 ==========
            // MQTT服务器标签
            mqttBrokerLabel = new Label
            {
                Location = new Point(700, 15),
                AutoSize = true,
                Text = "MQTT服务器:",
                Font = new Font("微软雅黑", 10F),
                ForeColor = darkColor
            };

            // MQTT服务器地址输入框
            mqttBrokerTextBox = new TextBox
            {
                Location = new Point(700, 40),
                Width = 160,
                Text = "192.168.184.221:1883",
                Font = new Font("微软雅黑", 9F)
            };

            // MQTT发布主题标签
            mqttTopicLabel = new Label
            {
                Location = new Point(870, 15),
                AutoSize = true,
                Text = "发布主题:",
                Font = new Font("微软雅黑", 10F),
                ForeColor = darkColor
            };

            // MQTT发布主题输入框
            mqttTopicTextBox = new TextBox
            {
                Location = new Point(870, 40),
                Width = 150,
                Text = "green/data",
                Font = new Font("微软雅黑", 9F)
            };

            // MQTT订阅主题标签
            mqttSubscribeTopicLabel = new Label
            {
                Location = new Point(1030, 15),
                AutoSize = true,
                Text = "订阅主题:",
                Font = new Font("微软雅黑", 10F),
                ForeColor = darkColor
            };

            // MQTT订阅主题输入框
            mqttSubscribeTopicTextBox = new TextBox
            {
                Location = new Point(1030, 40),
                Width = 100,
                Text = "green/control",
                Font = new Font("微软雅黑", 9F)
            };

            // 自动发布数据复选框（放在第二行左侧）
            mqttAutoPublishCheckBox = new CheckBox
            {
                Location = new Point(700, 80),
                AutoSize = true,
                Text = "自动发布数据到MQTT",
                Font = new Font("微软雅黑", 9F),
                Checked = true
            };
            mqttAutoPublishCheckBox.CheckedChanged += MqttAutoPublishCheckBox_CheckedChanged;

            // MQTT连接按钮（在发布主题输入框正下方）
            mqttConnectButton = CreateStyledButton("连接MQTT", new Point(870, 75), new Size(90, 27), successColor);
            mqttConnectButton.Click += MqttConnectButton_Click;

            // MQTT断开按钮（在连接按钮右边）
            mqttDisconnectButton = CreateStyledButton("断开MQTT", new Point(970, 75), new Size(90, 27), dangerColor);
            mqttDisconnectButton.Click += MqttDisconnectButton_Click;
            mqttDisconnectButton.Enabled = false;

            // MQTT状态标签（在断开按钮右边）
            mqttStatusLabel = new Label
            {
                Location = new Point(1070, 80),
                AutoSize = true,
                Text = "● 未连接",
                Font = new Font("微软雅黑", 9F, FontStyle.Bold),
                ForeColor = Color.Gray
            };

            // ========== 头像和昵称 ==========
            // 头像图片框
            avatarPictureBox = new PictureBox
            {
                Location = new Point(1150, 15),
                Size = new Size(60, 60),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.White
            };
            // 加载并设置圆形头像
            LoadCircularAvatar();

            // 昵称标签
            avatarLabel = new Label
            {
                Location = new Point(1130, 78),
                AutoSize = false,
                Width = 100,
                Height = 20,
                Text = "爱吃橘子🍊",
                Font = new Font("微软雅黑", 9F),
                ForeColor = darkColor,
                TextAlign = ContentAlignment.MiddleCenter
            };

            // 添加到控制面板
            controlPanel.Controls.AddRange(new Control[] {
                portLabel, comPortComboBox, refreshButton,
                baudRateLabel, baudRateComboBox,
                openButton, closeButton, lblStatus,
                clearButton, exportButton,
                mqttBrokerLabel, mqttBrokerTextBox, mqttTopicLabel, mqttTopicTextBox,
                mqttSubscribeTopicLabel, mqttSubscribeTopicTextBox,
                mqttConnectButton, mqttDisconnectButton,
                mqttStatusLabel, mqttAutoPublishCheckBox,
                avatarPictureBox, avatarLabel
            });

            // ========== 数据显示面板 ==========
            Panel dataPanel = new Panel
            {
                Location = new Point(10, 130),
                Size = new Size(850, 110),
                BackColor = Color.White,
                BorderStyle = BorderStyle.None
            };
            dataPanel.Paint += DataPanel_Paint;

            // 温度图标
            tempIconPictureBox = new PictureBox
            {
                Location = new Point(10, 30),
                Size = new Size(35, 35),
                SizeMode = PictureBoxSizeMode.Zoom,
                Image = LoadIconSafely("ICON\\温度.png")
            };

            // 温度显示
            Label tempTitleLabel = new Label
            {
                Location = new Point(50, 10),
                AutoSize = true,
                Text = "温度",
                Font = new Font("微软雅黑", 9F),
                ForeColor = darkColor
            };

            tempLabel = new Label
            {
                Location = new Point(50, 30),
                Size = new Size(70, 30),
                Text = "--.-",
                Font = new Font("Arial", 18F, FontStyle.Bold),
                ForeColor = dangerColor,
                TextAlign = ContentAlignment.MiddleLeft
            };

            tempUnitLabel = new Label
            {
                Location = new Point(50, 60),
                AutoSize = true,
                Text = "°C",
                Font = new Font("Arial", 10F),
                ForeColor = dangerColor
            };

            // 湿度图标
            humIconPictureBox = new PictureBox
            {
                Location = new Point(120, 30),
                Size = new Size(35, 35),
                SizeMode = PictureBoxSizeMode.Zoom,
                Image = LoadIconSafely("ICON\\湿度.png")
            };

            // 湿度显示
            Label humTitleLabel = new Label
            {
                Location = new Point(160, 10),
                AutoSize = true,
                Text = "湿度",
                Font = new Font("微软雅黑", 9F),
                ForeColor = darkColor
            };

            humLabel = new Label
            {
                Location = new Point(160, 30),
                Size = new Size(70, 30),
                Text = "--.-",
                Font = new Font("Arial", 18F, FontStyle.Bold),
                ForeColor = primaryColor,
                TextAlign = ContentAlignment.MiddleLeft
            };

            humUnitLabel = new Label
            {
                Location = new Point(160, 60),
                AutoSize = true,
                Text = "%",
                Font = new Font("Arial", 10F),
                ForeColor = primaryColor
            };

            // 土壤湿度图标
            waterIconPictureBox = new PictureBox
            {
                Location = new Point(230, 30),
                Size = new Size(35, 35),
                SizeMode = PictureBoxSizeMode.Zoom,
                Image = LoadIconSafely("ICON\\水位.png")
            };

            // 土壤湿度显示
            Label waterTitleLabel = new Label
            {
                Location = new Point(270, 10),
                AutoSize = true,
                Text = "土壤湿度",
                Font = new Font("微软雅黑", 9F),
                ForeColor = darkColor
            };

            waterLabel = new Label
            {
                Location = new Point(270, 30),
                Size = new Size(70, 30),
                Text = "--",
                Font = new Font("Arial", 18F, FontStyle.Bold),
                ForeColor = Color.FromArgb(255, 152, 0),
                TextAlign = ContentAlignment.MiddleLeft
            };

            Label waterUnitLabel = new Label
            {
                Location = new Point(270, 60),
                AutoSize = true,
                Text = "%",
                Font = new Font("Arial", 10F),
                ForeColor = Color.FromArgb(255, 152, 0)
            };

            // 空气质量图标
            airQualityIconPictureBox = new PictureBox
            {
                Location = new Point(340, 30),
                Size = new Size(35, 35),
                SizeMode = PictureBoxSizeMode.Zoom,
                Image = LoadIconSafely("ICON\\空气质量优状态.png")
            };

            // 空气质量显示
            Label airQualityTitleLabel = new Label
            {
                Location = new Point(380, 10),
                AutoSize = true,
                Text = "空气",
                Font = new Font("微软雅黑", 9F),
                ForeColor = darkColor
            };

            airQualityLabel = new Label
            {
                Location = new Point(380, 30),
                Size = new Size(70, 30),
                Text = "nice",
                Font = new Font("Arial", 16F, FontStyle.Bold),
                ForeColor = successColor,
                TextAlign = ContentAlignment.MiddleLeft
            };

            // 窗户图标（原舵机）
            windowIconPictureBox = new PictureBox
            {
                Location = new Point(450, 30),
                Size = new Size(35, 35),
                SizeMode = PictureBoxSizeMode.Zoom,
                Image = LoadIconSafely("ICON\\窗户-关状态.png")
            };

            // 窗户状态显示
            Label windowTitleLabel = new Label
            {
                Location = new Point(490, 10),
                AutoSize = true,
                Text = "窗户",
                Font = new Font("微软雅黑", 9F),
                ForeColor = darkColor
            };

            servoLabel = new Label
            {
                Location = new Point(490, 35),
                Size = new Size(70, 25),
                Text = "关闭",
                Font = new Font("微软雅黑", 12F, FontStyle.Bold),
                ForeColor = darkColor,
                TextAlign = ContentAlignment.MiddleLeft
            };

            // 风扇图标（原电机）
            fanIconPictureBox = new PictureBox
            {
                Location = new Point(560, 30),
                Size = new Size(35, 35),
                SizeMode = PictureBoxSizeMode.Zoom,
                Image = LoadIconSafely("ICON\\风扇-关状态.png")
            };

            // 风扇状态显示
            Label fanTitleLabel = new Label
            {
                Location = new Point(600, 10),
                AutoSize = true,
                Text = "风扇",
                Font = new Font("微软雅黑", 9F),
                ForeColor = darkColor
            };

            motorLabel = new Label
            {
                Location = new Point(600, 35),
                Size = new Size(70, 25),
                Text = "关闭",
                Font = new Font("微软雅黑", 12F, FontStyle.Bold),
                ForeColor = darkColor,
                TextAlign = ContentAlignment.MiddleLeft
            };

            // 蜂鸣器图标
            buzzerIconPictureBox = new PictureBox
            {
                Location = new Point(670, 30),
                Size = new Size(35, 35),
                SizeMode = PictureBoxSizeMode.Zoom,
                Image = LoadIconSafely("ICON\\蜂鸣器-关状态.png")
            };

            // 蜂鸣器状态显示
            Label buzzerTitleLabel = new Label
            {
                Location = new Point(710, 10),
                AutoSize = true,
                Text = "蜂鸣器",
                Font = new Font("微软雅黑", 9F),
                ForeColor = darkColor
            };

            Label buzzerLabel = new Label
            {
                Location = new Point(710, 35),
                Size = new Size(60, 25),
                Text = "关闭",
                Font = new Font("微软雅黑", 12F, FontStyle.Bold),
                ForeColor = darkColor,
                TextAlign = ContentAlignment.MiddleLeft
            };

            dataPanel.Controls.AddRange(new Control[] {
                tempIconPictureBox, tempTitleLabel, tempLabel, tempUnitLabel,
                humIconPictureBox, humTitleLabel, humLabel, humUnitLabel,
                waterIconPictureBox, waterTitleLabel, waterLabel, waterUnitLabel,
                airQualityIconPictureBox, airQualityTitleLabel, airQualityLabel,
                windowIconPictureBox, windowTitleLabel, servoLabel,
                fanIconPictureBox, fanTitleLabel, motorLabel,
                buzzerIconPictureBox, buzzerTitleLabel, buzzerLabel
            });

            // ========== 控制面板 ==========
            Panel controlDevicePanel = new Panel
            {
                Location = new Point(10, 250),
                Size = new Size(850, 20),
                BackColor = lightColor,
                BorderStyle = BorderStyle.None
            };

            Label controlTitleLabel = new Label
            {
                Location = new Point(0, 0),
                AutoSize = true,
                Text = "设备控制",
                Font = new Font("微软雅黑", 10F, FontStyle.Bold),
                ForeColor = darkColor
            };

            modeStatusLabel = new Label
            {
                Location = new Point(100, 2),
                AutoSize = true,
                Text = "● 自动模式",
                Font = new Font("微软雅黑", 9F),
                ForeColor = successColor
            };

            modeAutoButton = CreateStyledButton("自动模式", new Point(200, -2), new Size(80, 24), successColor);
            modeAutoButton.Click += ModeAutoButton_Click;

            modeManualButton = CreateStyledButton("手动模式", new Point(290, -2), new Size(80, 24), warningColor);
            modeManualButton.Click += ModeManualButton_Click;

            servoOffButton = CreateStyledButton("窗户关", new Point(400, -2), new Size(70, 24), primaryColor);
            servoOffButton.Click += ServoOffButton_Click;
            servoOffButton.Enabled = false;

            servoOnButton = CreateStyledButton("窗户开", new Point(480, -2), new Size(70, 24), primaryColor);
            servoOnButton.Click += ServoOnButton_Click;
            servoOnButton.Enabled = false;

            motorOffButton = CreateStyledButton("风扇关", new Point(580, -2), new Size(70, 24), primaryColor);
            motorOffButton.Click += MotorOffButton_Click;
            motorOffButton.Enabled = false;

            motorOnButton = CreateStyledButton("风扇开", new Point(660, -2), new Size(70, 24), primaryColor);
            motorOnButton.Click += MotorOnButton_Click;
            motorOnButton.Enabled = false;

            controlDevicePanel.Controls.AddRange(new Control[] {
                controlTitleLabel, modeStatusLabel,
                modeAutoButton, modeManualButton,
                servoOffButton, servoOnButton,
                motorOffButton, motorOnButton
            });

            // ========== 图表面板 ==========
            Label chartLabel = new Label
            {
                Location = new Point(10, 280),
                AutoSize = true,
                Text = "历史数据图表 (最近100组数据)",
                Font = new Font("微软雅黑", 10F, FontStyle.Bold),
                ForeColor = darkColor
            };

            chartPanel = new Panel
            {
                Location = new Point(10, 305),
                Size = new Size(440, 440),
                BackColor = chartBgColor,
                BorderStyle = BorderStyle.None
            };
            chartPanel.Paint += ChartPanel_Paint;

            // ========== 视频监控面板 ==========
            videoLabel = new Label
            {
                Location = new Point(470, 280),
                AutoSize = true,
                Text = "视频监控",
                Font = new Font("微软雅黑", 10F, FontStyle.Bold),
                ForeColor = darkColor
            };

            videoPictureBox = new PictureBox
            {
                Location = new Point(470, 305),
                Size = new Size(320, 240),
                BackColor = Color.Black,
                BorderStyle = BorderStyle.FixedSingle,
                SizeMode = PictureBoxSizeMode.Zoom
            };

            Label videoIpLabel = new Label
            {
                Location = new Point(470, 555),
                AutoSize = true,
                Text = "ESP32地址:",
                Font = new Font("微软雅黑", 9F),
                ForeColor = darkColor
            };

            videoIpTextBox = new TextBox
            {
                Location = new Point(545, 553),
                Width = 180,
                Text = "http://192.168.1.100",
                Font = new Font("微软雅黑", 9F)
            };

            videoConnectButton = CreateStyledButton("连接", new Point(470, 585), new Size(70, 27), successColor);
            videoConnectButton.Click += VideoConnectButton_Click;

            videoDisconnectButton = CreateStyledButton("断开", new Point(550, 585), new Size(70, 27), dangerColor);
            videoDisconnectButton.Click += VideoDisconnectButton_Click;
            videoDisconnectButton.Enabled = false;

            videoStatusLabel = new Label
            {
                Location = new Point(630, 590),
                AutoSize = true,
                Text = "● 未连接",
                Font = new Font("微软雅黑", 9F, FontStyle.Bold),
                ForeColor = Color.Gray
            };

            // ========== 日志面板 ==========
            logLabel = new Label
            {
                Location = new Point(940, 130),
                AutoSize = true,
                Text = "接收日志",
                Font = new Font("微软雅黑", 10F, FontStyle.Bold),
                ForeColor = darkColor
            };

            autoScrollCheckBox = new CheckBox
            {
                Location = new Point(1240, 130),
                AutoSize = true,
                Text = "自动滚动",
                Font = new Font("微软雅黑", 9F),
                Checked = true
            };

            logTextBox = new TextBox
            {
                Location = new Point(940, 155),
                Size = new Size(370, 520),
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Color.White,
                Font = new Font("Consolas", 9F),
                BorderStyle = BorderStyle.FixedSingle
            };

            // 添加所有控件到窗体
            this.Controls.AddRange(new Control[] {
                controlPanel, dataPanel,
                controlDevicePanel, chartLabel, chartPanel,
                videoLabel, videoIpLabel, videoIpTextBox, videoConnectButton, videoDisconnectButton,
                videoStatusLabel, videoPictureBox,
                logLabel, autoScrollCheckBox, logTextBox
            });

            this.ResumeLayout(false);
            this.PerformLayout();
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
        private Image LoadIconSafely(string relativePath)
        {
            try
            {
                string iconPath = Path.Combine(Application.StartupPath, relativePath);
                if (File.Exists(iconPath))
                {
                    return Image.FromFile(iconPath);
                }
                else
                {
                    LogMessage($"警告: 图标文件不存在: {iconPath}");
                    return null;
                }
            }
            catch (Exception ex)
            {
                LogMessage($"错误: 加载图标失败 - {ex.Message}");
                return null;
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
                    Image originalImage = Image.FromFile(avatarPath);
                    Image circularImage = CreateCircularImage(originalImage, 60);
                    avatarPictureBox.Image = circularImage;
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

        // 控制面板绘制边框
        private void ControlPanel_Paint(object sender, PaintEventArgs e)
        {
            Panel panel = sender as Panel;
            using (Pen pen = new Pen(Color.FromArgb(220, 220, 220), 1))
            {
                e.Graphics.DrawLine(pen, 0, panel.Height - 1, panel.Width, panel.Height - 1);
            }
        }

        // 数据面板绘制圆角边框
        private void DataPanel_Paint(object sender, PaintEventArgs e)
        {
            Panel panel = sender as Panel;
            using (Pen pen = new Pen(Color.FromArgb(220, 220, 220), 2))
            {
                Rectangle rect = new Rectangle(0, 0, panel.Width - 1, panel.Height - 1);
                DrawRoundedRectangle(e.Graphics, rect, 8, pen);
            }
        }

        // 绘制圆角矩形
        private void DrawRoundedRectangle(Graphics graphics, Rectangle bounds, int cornerRadius, Pen drawPen)
        {
            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddArc(bounds.X, bounds.Y, cornerRadius, cornerRadius, 180, 90);
                path.AddArc(bounds.X + bounds.Width - cornerRadius, bounds.Y, cornerRadius, cornerRadius, 270, 90);
                path.AddArc(bounds.X + bounds.Width - cornerRadius, bounds.Y + bounds.Height - cornerRadius, cornerRadius, cornerRadius, 0, 90);
                path.AddArc(bounds.X, bounds.Y + bounds.Height - cornerRadius, cornerRadius, cornerRadius, 90, 90);
                path.CloseAllFigures();
                graphics.DrawPath(drawPen, path);
            }
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
            _timestampHistory.Clear();
            _alarmTypeHistory.Clear();
            _airQualityHistory.Clear();
            _servoStatusHistory.Clear();
            _motorStatusHistory.Clear();
            tempLabel.Text = "--.-";
            humLabel.Text = "--.-";
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
                            sw.WriteLine("序号,温度(°C),湿度(%),土壤湿度(%),空气质量,窗户状态,风扇状态,报警类型,时间戳");

                            // 写入数据行
                            for (int i = 0; i < _tempHistory.Count; i++)
                            {
                                string timestamp = i < _timestampHistory.Count ? _timestampHistory[i] : "";
                                string alarmType = i < _alarmTypeHistory.Count ? _alarmTypeHistory[i] : "正常";
                                string airQuality = i < _airQualityHistory.Count ? _airQualityHistory[i] : "";
                                string servoStatus = i < _servoStatusHistory.Count ? _servoStatusHistory[i] : "";
                                string motorStatus = i < _motorStatusHistory.Count ? _motorStatusHistory[i] : "";
                                double waterLevel = i < _waterHistory.Count ? _waterHistory[i] : 0;

                                sw.WriteLine($"{i + 1},{_tempHistory[i]:F1},{_humHistory[i]:F1},{waterLevel:F0},{airQuality},{servoStatus},{motorStatus},{alarmType},{timestamp}");
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
                this.Invoke((MethodInvoker)delegate
                {
                    LogMessage($"接收: {line}");
                });

                // 解析数据
                var parsed = ParseData(line);
                if (parsed.HasValue)
                {
                    var p = parsed.Value;
                    string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    this.Invoke((MethodInvoker)delegate
                    {
                        tempLabel.Text = $"{p.Temp:F1}";
                        humLabel.Text = $"{p.Hum:F1}";
                        waterLabel.Text = $"{_waterLevel}";
                        airQualityLabel.Text = $"{_airQuality}";
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
                        bool hasAlarm = (p.Temp > _tempMax) ||
                                       (p.Hum > _humMax) ||
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

                        // 获取报警类型
                        string alarmType = GetAlarmType(p.Temp, p.Hum);

                        // 添加到图表，包含时间戳和报警类型
                        AddToChart(p.Temp, p.Hum, _waterLevel, timestamp, alarmType);
                        chartPanel.Invalidate();
                        LogMessage($"解析成功: 温度={p.Temp:F1}°C, 湿度={p.Hum:F1}%, 土壤湿度={_waterLevel}%", Color.Green);

                        // 检查报警状态
                        UpdateAlarmStatus(p.Temp, p.Hum);

                        // 发布数据到MQTT
                        PublishDataToMqtt(p.Temp, p.Hum);
                    });
                }
                else
                {
                    this.Invoke((MethodInvoker)delegate
                    {
                        LogMessage($"解析失败: 数据格式不正确", Color.Red);
                    });
                }
            }
            catch (Exception ex)
            {
                this.Invoke((MethodInvoker)delegate
                {
                    LogMessage($"处理数据异常: {ex.Message}", Color.Red);
                });
            }
        }

        private (double Temp, double Hum)? ParseData(string raw)
        {
            try
            {
                string s = raw.Trim();
                if (string.IsNullOrEmpty(s)) return null;

                // 固定使用STM32格式解析: Temp:XXC   Humi:XX%RH
                var tempMatch = Regex.Match(s, @"Temp:(\d+)C");
                var humMatch = Regex.Match(s, @"Humi:(\d+)%RH");

                if (!tempMatch.Success || !humMatch.Success) return null;
                if (tempMatch.Groups.Count < 2 || humMatch.Groups.Count < 2) return null;

                if (!double.TryParse(tempMatch.Groups[1].Value, out double temp)) return null;
                if (!double.TryParse(humMatch.Groups[1].Value, out double hum)) return null;

                // 解析STM32扩展数据
                ParseSTM32ExtendedData(s);

                // 合理性检查
                if (temp < -50 || temp > 100) return null;
                if (hum < 0 || hum > 100) return null;

                return (temp, hum);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ParseData异常: {ex.Message}");
                return null;
            }
        }

        // 解析STM32扩展数据
        private void ParseSTM32ExtendedData(string data)
        {
            try
            {
                // 解析土壤湿度: Soil:XX%(STM32固件输出格式, raw值用于校准)
                var waterMatch = Regex.Match(data, @"Soil:(\d+)%");
                if (waterMatch.Success)
                {
                    _waterLevel = int.Parse(waterMatch.Groups[1].Value);
                }

                // 解析舵机状态: Servo:ON/OFF
                var servoMatch = Regex.Match(data, @"Servo:(ON|OFF)");
                if (servoMatch.Success)
                {
                    _servoStatus = servoMatch.Groups[1].Value;
                }

                // 解析电机状态: Motor:ON/OFF
                var motorMatch = Regex.Match(data, @"Motor:(ON|OFF)");
                if (motorMatch.Success)
                {
                    _motorStatus = motorMatch.Groups[1].Value;
                }

                // 解析空气质量: Air:nice/bad
                var airMatch = Regex.Match(data, @"Air:(nice|bad)");
                if (airMatch.Success)
                {
                    _airQuality = airMatch.Groups[1].Value;
                }

                // 检查是否有Warning
                _hasWarning = data.Contains("Warning");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ParseSTM32ExtendedData异常: {ex.Message}");
            }
        }

        private void AddToChart(double temp, double hum, double water, string timestamp, string alarmType)
        {
            _tempHistory.Add(temp);
            _humHistory.Add(hum);
            _waterHistory.Add(water);
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
                _timestampHistory.RemoveAt(0);
                _alarmTypeHistory.RemoveAt(0);
                _airQualityHistory.RemoveAt(0);
                _servoStatusHistory.RemoveAt(0);
                _motorStatusHistory.RemoveAt(0);
            }
        }

        private void LogMessage(string message, Color? color = null)
        {
            try
            {
                string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
                string logEntry = $"[{timestamp}] {message}\r\n";

                _logBuffer.Append(logEntry);

                // 限制日志缓冲区大小
                if (_logBuffer.Length > 50000)
                {
                    _logBuffer.Remove(0, 10000);
                }

                if (logTextBox.InvokeRequired)
                {
                    logTextBox.Invoke((MethodInvoker)delegate
                    {
                        UpdateLogTextBox(logEntry);
                    });
                }
                else
                {
                    UpdateLogTextBox(logEntry);
                }
            }
            catch { }
        }

        private void UpdateLogTextBox(string logEntry)
        {
            logTextBox.AppendText(logEntry);
            if (autoScrollCheckBox.Checked)
            {
                logTextBox.SelectionStart = logTextBox.Text.Length;
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

            // 绘制图例
            DrawLegend(g, right - 150, top);

            // 绘制标题
            using (var titleFont = new Font("微软雅黑", 11F, FontStyle.Bold))
            {
                string title = $"数据点数: {_tempHistory.Count}/{MaxHistoryPoints}";
                g.DrawString(title, titleFont, new SolidBrush(darkColor), left, top - 25);
            }
        }

        private void DrawGrid(Graphics g, int left, int top, int right, int bottom)
        {
            using (var gridPen = new Pen(Color.FromArgb(230, 230, 230), 1))
            {
                gridPen.DashStyle = DashStyle.Dot;

                // 水平网格线
                int hSteps = 8;
                for (int i = 0; i <= hSteps; i++)
                {
                    int y = top + (bottom - top) * i / hSteps;
                    g.DrawLine(gridPen, left, y, right, y);
                }

                // 垂直网格线
                int vSteps = 10;
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
            {
                int steps = 8;
                for (int i = 0; i <= steps; i++)
                {
                    int y = bottom - (bottom - top) * i / steps;
                    double value = 40.0 * i / steps;  // 0-40度

                    // 刻度线
                    g.DrawLine(new Pen(darkColor, 1), x - 5, y, x, y);

                    // 刻度值
                    string label = value.ToString("F0");
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
            {
                int steps = 10;
                for (int i = 0; i <= steps; i++)
                {
                    int y = bottom - (bottom - top) * i / steps;
                    double value = 100.0 * i / steps;  // 0-100%

                    // 刻度线
                    g.DrawLine(new Pen(darkColor, 1), x, y, x + 5, y);

                    // 刻度值
                    string label = value.ToString("F0");
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
                    g.FillEllipse(new SolidBrush(dangerColor), x - 3, y - 3, 6, 6);

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
                    g.FillEllipse(new SolidBrush(primaryColor), x - 3, y - 3, 6, 6);

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
                    g.FillEllipse(new SolidBrush(waterColor), x - 3, y - 3, 6, 6);

                    prev = cur;
                }
            }
        }

        private void DrawLegend(Graphics g, int x, int y)
        {
            using (var font = new Font("微软雅黑", 9F))
            {
                // 温度图例
                g.DrawLine(new Pen(dangerColor, 3), x, y + 5, x + 30, y + 5);
                g.FillEllipse(new SolidBrush(dangerColor), x + 12, y + 2, 6, 6);
                g.DrawString("温度", font, new SolidBrush(dangerColor), x + 35, y);

                // 湿度图例
                g.DrawLine(new Pen(primaryColor, 3), x, y + 25, x + 30, y + 25);
                g.FillEllipse(new SolidBrush(primaryColor), x + 12, y + 22, 6, 6);
                g.DrawString("湿度", font, new SolidBrush(primaryColor), x + 35, y + 20);

                // 土壤湿度图例
                Color waterColor = Color.FromArgb(255, 193, 7);
                g.DrawLine(new Pen(waterColor, 3), x, y + 45, x + 30, y + 45);
                g.FillEllipse(new SolidBrush(waterColor), x + 12, y + 42, 6, 6);
                g.DrawString("土壤湿度", font, new SolidBrush(waterColor), x + 35, y + 40);
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
                if (_mqttClient != null && _mqttClient.IsConnected)
                {
                    _mqttClient.DisconnectAsync().Wait(1000);
                }

                // 断开视频流
                _videoCancellationToken?.Cancel();
                _videoHttpClient?.Dispose();
            }
            catch { }
        }

        private void Form1_Resize(object sender, EventArgs e)
        {
            if (this.WindowState == FormWindowState.Minimized) return;

            try
            {
                // 获取当前窗体的客户区大小
                int clientWidth = this.ClientSize.Width;
                int clientHeight = this.ClientSize.Height;

                // 调整图表面板的大小和位置
                if (chartPanel != null)
                {
                    // 保持图表宽度和高度固定
                    // chartPanel.Width = 440;
                    // chartPanel.Height = 195;
                }

                // 调整日志文本框的位置和大小
                if (logTextBox != null)
                {
                    logTextBox.Location = new Point(clientWidth - 410, 155);
                    logTextBox.Width = 370;
                    logTextBox.Height = clientHeight - 165;
                }

                // 调整日志标签位置
                if (logLabel != null)
                {
                    logLabel.Location = new Point(clientWidth - 410, 130);
                }

                // 调整自动滚动复选框位置
                if (autoScrollCheckBox != null)
                {
                    autoScrollCheckBox.Location = new Point(clientWidth - 110, 130);
                }

                // 重绘图表
                if (chartPanel != null)
                {
                    chartPanel.Invalidate();
                }
            }
            catch { }
        }

        #region MQTT相关方法

        // 初始化MQTT客户端
        private void InitializeMqttClient()
        {
            try
            {
                var factory = new MqttFactory();
                _mqttClient = factory.CreateMqttClient();
                LogMessage("MQTT客户端已初始化");
            }
            catch (Exception ex)
            {
                LogMessage($"MQTT客户端初始化失败: {ex.Message}", Color.Red);
            }
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
                string[] parts = mqttBrokerTextBox.Text.Split(':');
                string server = parts[0];
                int port = parts.Length > 1 ? int.Parse(parts[1]) : 1883;

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
                string jsonData = $@"{{
    ""timestamp"": ""{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}"",
    ""temperature"": {temperature},
    ""humidity"": {humidity},
    ""soilMoisture"": {_waterLevel},
    ""airQuality"": ""{_airQuality}"",
    ""servoStatus"": ""{_servoStatus}"",
    ""motorStatus"": ""{_motorStatus}"",
    ""alarmType"": ""{alarmType}"",
    ""hasAlarm"": {hasAlarm.ToString().ToLower()}
}}";

                var message = new MqttApplicationMessageBuilder()
                    .WithTopic(_mqttPublishTopic)
                    .WithPayload(jsonData)
                    .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce)
                    .WithRetainFlag(false)
                    .Build();

                await _mqttClient.PublishAsync(message);
                LogMessage($"[MQTT发送] 温度:{temperature}°C 湿度:{humidity}% 土壤湿度:{_waterLevel}% 空气:{_airQuality} 报警:{alarmType}", Color.Blue);
            }
            catch (Exception ex)
            {
                LogMessage($"MQTT发布数据失败: {ex.Message}", Color.Red);
            }
        }

        // 更新MQTT连接状态标签
        private void UpdateMqttStatus(string status, Color color)
        {
            if (mqttStatusLabel.InvokeRequired)
            {
                mqttStatusLabel.Invoke(new Action(() => UpdateMqttStatus(status, color)));
                return;
            }

            mqttStatusLabel.Text = "● " + status;
            mqttStatusLabel.ForeColor = color;
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

                // 设置消息接收处理器
                _mqttClient.ApplicationMessageReceivedAsync += OnMqttMessageReceived;
            }
            catch (Exception ex)
            {
                LogMessage($"订阅主题失败: {ex.Message}", Color.Red);
            }
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
                this.Invoke((MethodInvoker)delegate
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

        #region 视频监控方法

        // 连接视频流按钮
        private async void VideoConnectButton_Click(object sender, EventArgs e)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(videoIpTextBox.Text))
                {
                    MessageBox.Show("请输入ESP32摄像头地址!", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                string baseUrl = videoIpTextBox.Text.Trim();
                if (!baseUrl.StartsWith("http://") && !baseUrl.StartsWith("https://"))
                {
                    baseUrl = "http://" + baseUrl;
                }

                // ESP32-CAM的MJPEG流地址通常是 /stream 或 /:81/stream
                string streamUrl = baseUrl.TrimEnd('/') + "/stream";

                UpdateVideoStatus("连接中...", Color.Orange);
                LogMessage($"正在连接视频流: {streamUrl}");

                _videoHttpClient = new HttpClient();
                _videoHttpClient.Timeout = TimeSpan.FromSeconds(10);
                _videoCancellationToken = new CancellationTokenSource();

                videoConnectButton.Enabled = false;
                videoDisconnectButton.Enabled = true;
                videoIpTextBox.Enabled = false;

                // 启动视频流接收任务
                _ = Task.Run(() => ReceiveVideoStream(streamUrl, _videoCancellationToken.Token));
            }
            catch (Exception ex)
            {
                UpdateVideoStatus("连接失败", Color.Red);
                LogMessage($"视频流连接失败: {ex.Message}", Color.Red);
                MessageBox.Show($"连接视频流失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);

                videoConnectButton.Enabled = true;
                videoDisconnectButton.Enabled = false;
                videoIpTextBox.Enabled = true;
            }
        }

        // 断开视频流按钮
        private void VideoDisconnectButton_Click(object sender, EventArgs e)
        {
            try
            {
                _videoCancellationToken?.Cancel();
                _videoHttpClient?.Dispose();
                _videoStreaming = false;

                UpdateVideoStatus("未连接", Color.Gray);
                videoConnectButton.Enabled = true;
                videoDisconnectButton.Enabled = false;
                videoIpTextBox.Enabled = true;

                if (videoPictureBox.InvokeRequired)
                {
                    videoPictureBox.Invoke(new Action(() => videoPictureBox.Image = null));
                }
                else
                {
                    videoPictureBox.Image = null;
                }

                LogMessage("视频流已断开");
            }
            catch (Exception ex)
            {
                LogMessage($"断开视频流失败: {ex.Message}", Color.Red);
            }
        }

        // 接收MJPEG视频流
        private async Task ReceiveVideoStream(string streamUrl, CancellationToken cancellationToken)
        {
            try
            {
                _videoStreaming = true;
                using (var response = await _videoHttpClient.GetAsync(streamUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
                {
                    response.EnsureSuccessStatusCode();

                    // 连接成功，更新状态
                    this.Invoke(new Action(() =>
                    {
                        UpdateVideoStatus("已连接", Color.Green);
                        LogMessage("视频流连接成功", Color.Green);
                    }));

                    using (var stream = await response.Content.ReadAsStreamAsync())
                    {
                        byte[] buffer = new byte[1024 * 1024]; // 1MB buffer
                        List<byte> frameBuffer = new List<byte>();
                        int bytesRead;

                        while (!cancellationToken.IsCancellationRequested && (bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                        {
                            for (int i = 0; i < bytesRead; i++)
                            {
                                frameBuffer.Add(buffer[i]);

                                // 检测JPEG结束标记 (0xFF 0xD9)
                                if (frameBuffer.Count >= 2 && frameBuffer[frameBuffer.Count - 2] == 0xFF && frameBuffer[frameBuffer.Count - 1] == 0xD9)
                                {
                                    // 找到JPEG起始标记 (0xFF 0xD8)
                                    int startIndex = -1;
                                    for (int j = 0; j < frameBuffer.Count - 1; j++)
                                    {
                                        if (frameBuffer[j] == 0xFF && frameBuffer[j + 1] == 0xD8)
                                        {
                                            startIndex = j;
                                            break;
                                        }
                                    }

                                    if (startIndex >= 0)
                                    {
                                        byte[] frameData = frameBuffer.Skip(startIndex).ToArray();
                                        DisplayFrame(frameData);
                                        frameBuffer.Clear();
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                LogMessage("视频流接收已取消");
            }
            catch (HttpRequestException ex)
            {
                if (_videoStreaming)
                {
                    string errorMsg = ex.Message;
                    string suggestion = "";

                    if (ex.Message.Contains("404"))
                    {
                        suggestion = "\n\n建议尝试以下地址格式：\n" +
                                   "1. http://IP地址:81/stream\n" +
                                   "2. http://IP地址\n" +
                                   "3. http://IP地址/cam-hi.jpg\n" +
                                   "4. http://IP地址:81";
                    }

                    LogMessage($"视频流接收错误: {errorMsg}", Color.Red);
                    this.Invoke(new Action(() =>
                    {
                        UpdateVideoStatus("连接失败", Color.Red);
                        videoConnectButton.Enabled = true;
                        videoDisconnectButton.Enabled = false;
                        videoIpTextBox.Enabled = true;
                        MessageBox.Show($"视频流连接失败: {errorMsg}{suggestion}", "连接错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }));
                }
            }
            catch (Exception ex)
            {
                if (_videoStreaming)
                {
                    LogMessage($"视频流接收错误: {ex.Message}", Color.Red);
                    this.Invoke(new Action(() =>
                    {
                        UpdateVideoStatus("连接断开", Color.Red);
                        videoConnectButton.Enabled = true;
                        videoDisconnectButton.Enabled = false;
                        videoIpTextBox.Enabled = true;
                    }));
                }
            }
            finally
            {
                _videoStreaming = false;
            }
        }

        // 显示视频帧
        private void DisplayFrame(byte[] frameData)
        {
            try
            {
                using (var ms = new MemoryStream(frameData))
                {
                    var image = Image.FromStream(ms);

                    if (videoPictureBox.InvokeRequired)
                    {
                        videoPictureBox.Invoke(new Action(() =>
                        {
                            var oldImage = videoPictureBox.Image;
                            videoPictureBox.Image = image;
                            oldImage?.Dispose();
                        }));
                    }
                    else
                    {
                        var oldImage = videoPictureBox.Image;
                        videoPictureBox.Image = image;
                        oldImage?.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"显示帧错误: {ex.Message}");
            }
        }

        // 更新视频连接状态标签
        private void UpdateVideoStatus(string status, Color color)
        {
            if (videoStatusLabel.InvokeRequired)
            {
                videoStatusLabel.Invoke(new Action(() => UpdateVideoStatus(status, color)));
                return;
            }

            videoStatusLabel.Text = "● " + status;
            videoStatusLabel.ForeColor = color;
        }

        #endregion
    }
}
