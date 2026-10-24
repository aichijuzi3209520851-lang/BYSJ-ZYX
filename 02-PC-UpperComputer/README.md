# 🌡️ 基于STM32的智能温室数据监控系统

[![C#](https://img.shields.io/badge/C%23-.NET%20Framework%204.7.2-blue.svg)](https://docs.microsoft.com/en-us/dotnet/framework/)
[![License](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows-lightgrey.svg)](https://www.microsoft.com/windows/)
[![MQTT](https://img.shields.io/badge/MQTT-v3.1.1-orange.svg)](https://mqtt.org/)

一个功能强大的Windows桌面应用程序，用于实时监控STM32单片机传感器数据，支持MQTT物联网协议、视频监控、设备控制和数据可视化分析。

## 📋 目录

- [🌡️ 基于STM32的智能温室数据监控系统](#️-基于stm32的智能温室数据监控系统)
  - [📋 目录](#-目录)
  - [✨ 核心功能](#-核心功能)
  - [🏗️ 技术架构](#️-技术架构)
  - [📦 依赖组件](#-依赖组件)
  - [🚀 快速开始](#-快速开始)
  - [🔧 安装配置](#-安装配置)
  - [📖 使用教程](#-使用教程)
  - [🔌 API文档](#-api文档)
  - [📊 数据格式](#-数据格式)
  - [🤝 贡献指南](#-贡献指南)
  - [📄 许可证](#-许可证)
  - [🙏 致谢](#-致谢)

## ✨ 核心功能

### 🌡️ 环境监测
- **温湿度监控**: 实时采集STM32单片机传感器数据
- **水位监测**: 支持水位传感器数据采集
- **空气质量**: 空气质量状态检测（优/劣）
- **数据可视化**: 历史数据图表展示（支持100组数据）

### 📡 物联网集成
- **MQTT协议**: 支持MQTT v3.1.1标准协议
- **数据发布**: 自动将传感器数据发布到MQTT服务器
- **远程控制**: 通过MQTT接收控制指令
- **主题订阅**: 支持自定义发布/订阅主题

### 📹 视频监控
- **ESP32-CAM集成**: 支持ESP32摄像头视频流
- **MJPEG格式**: 实时视频流显示
- **网络配置**: 可配置摄像头IP地址
- **状态监控**: 实时连接状态显示

### 🎛️ 设备控制
- **窗户控制**: 舵机驱动窗户开关
- **风扇控制**: 电机控制风扇启停
- **模式切换**: 自动/手动模式切换
- **蜂鸣器**: 报警状态指示

### 📊 数据管理
- **CSV导出**: 支持数据导出到CSV文件
- **历史记录**: 自动保存100组历史数据
- **报警记录**: 温湿度异常报警记录
- **日志系统**: 详细的操作和错误日志

### 🎨 用户界面
- **现代化设计**: 扁平化UI设计风格
- **实时状态**: 设备状态图标显示
- **响应式布局**: 支持窗口大小调整
- **个性化头像**: 支持自定义用户头像

## 🏗️ 技术架构

### 系统架构图

```
┌─────────────────────────────────────────────────────────────┐
│                    基于STM32的智能温室数据监控系统                        │
├─────────────────────────────────────────────────────────────┤
│  用户界面层 (Windows Forms)                                   │
│  ┌─────────────┬─────────────┬─────────────┬─────────────┐ │
│  │  串口通信    │  MQTT客户端  │  视频监控    │  数据可视化   │ │
│  └─────────────┴─────────────┴─────────────┴─────────────┘ │
├─────────────────────────────────────────────────────────────┤
│  业务逻辑层                                                  │
│  ┌─────────────┬─────────────┬─────────────┬─────────────┐ │
│  │  数据解析    │  协议处理    │  设备控制    │  异常处理    │ │
│  └─────────────┴─────────────┴─────────────┴─────────────┘ │
├─────────────────────────────────────────────────────────────┤
│  数据访问层                                                  │
│  ┌─────────────┬─────────────┬─────────────┬─────────────┐ │
│  │  串口驱动    │  MQTT协议   │  HTTP客户端  │  文件系统    │ │
│  └─────────────┴─────────────┴─────────────┴─────────────┘ │
├─────────────────────────────────────────────────────────────┤
│  硬件接口层                                                  │
│  ┌─────────────┬─────────────┬─────────────┬─────────────┐ │
│  │  STM32传感器 │  STM32单片机 │  ESP32-CAM  │  舵机/电机   │ │
│  └─────────────┴─────────────┴─────────────┴─────────────┘ │
└─────────────────────────────────────────────────────────────┘
```

### 技术栈

| 技术类别 | 具体技术 | 版本 | 用途 |
|---------|---------|------|------|
| **编程语言** | C# | 7.3+ | 主要开发语言 |
| **框架平台** | .NET Framework | 4.7.2 | 应用程序框架 |
| **UI框架** | Windows Forms | - | 桌面用户界面 |
| **通信协议** | System.IO.Ports | - | 串口通信 |
| **MQTT协议** | MQTTnet | 4.3.7.1207 | MQTT客户端库 |
| **HTTP客户端** | System.Net.Http | - | 视频流获取 |
| **图形处理** | System.Drawing | - | 图表绘制和图像处理 |
| **数据格式** | System.Text.Json | - | JSON数据序列化 |

## 📦 依赖组件

### NuGet包依赖
```xml
<package id="MQTTnet" version="4.3.7.1207" targetFramework="net472" />
```

### 系统依赖
- **操作系统**: Windows 7 SP1 或更高版本
- **.NET Framework**: 4.7.2 或更高版本
- **Visual C++ Redistributable**: 2015-2019 版本

### 硬件要求
- **处理器**: Intel Core i3 或同等性能处理器
- **内存**: 最低 2GB RAM，推荐 4GB+
- **存储**: 至少 100MB 可用磁盘空间
- **串口**: 至少 1 个可用串口（USB转串口适配器支持）

## 🚀 快速开始

### 1. 环境准备

确保您的系统满足以下要求：

```powershell
# 检查.NET Framework版本（需要4.7.2或更高）
Get-ItemProperty "HKLM:SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full\" | Select-Object Release
```

### 2. 下载程序

从[Releases](../../releases)页面下载最新版本的程序：

```bash
# 下载并解压
curl -L -o STM32_Greenhouse.zip https://github.com/your-repo/STM32_Greenhouse/releases/latest/download/STM32_Greenhouse.zip
unzip STM32_Greenhouse.zip -d STM32_Greenhouse
```

### 3. 硬件连接

按照以下步骤连接硬件设备：

```
STM32传感器 → STM32单片机 → USB转串口 → 电脑
ESP32-CAM → 网络路由器 → 同一局域网
舵机/电机 → STM32单片机 → 控制设备
```

### 4. 启动程序

双击运行 `STM32_Greenhouse.exe` 启动监控系统：

```bash
# 程序位置
STM32_Greenhouse\bin\Release\STM32_Greenhouse.exe
```

## 🔧 安装配置

### 开发环境搭建

#### 1. 安装Visual Studio

推荐使用 Visual Studio 2019 或更高版本：

```bash
# 下载Visual Studio Community
https://visualstudio.microsoft.com/downloads/

# 工作负载选择
- .NET桌面开发
- Windows应用程序开发
```

#### 2. 克隆项目

```bash
# 克隆仓库
git clone https://github.com/your-repo/STM32_Greenhouse.git
cd STM32_Greenhouse

# 打开解决方案
start STM32_Greenhouse.sln
```

#### 3. 安装依赖

```bash
# 还原NuGet包
nuget restore STM32_Greenhouse.sln

# 或者使用Visual Studio的包管理器
Update-Package -reinstall
```

#### 4. 编译项目

```bash
# 使用MSBuild编译
msbuild STM32_Greenhouse.sln /p:Configuration=Release

# 或者使用Visual Studio
# Build -> Build Solution (Ctrl+Shift+B)
```

### 配置文件说明

#### App.config
```xml
<?xml version="1.0" encoding="utf-8" ?>
<configuration>
    <startup> 
        <supportedRuntime version="v4.0" sku=".NETFramework,Version=v4.7.2" />
    </startup>
</configuration>
```

#### 程序设置
程序的主要配置通过用户界面进行：

- **串口配置**: COM端口、波特率设置
- **MQTT配置**: 服务器地址、主题设置
- **视频配置**: ESP32-CAM IP地址
- **报警阈值**: 温湿度上下限设置

## 📖 使用教程

### 串口连接配置

#### 1. 选择串口
```
1. 打开程序
2. 在"串口选择"下拉框中选择可用串口
3. 点击"刷新"按钮可重新检测串口
```

#### 2. 设置波特率
```
1. 在"波特率"下拉框中选择合适的波特率
2. 常用波特率: 9600, 19200, 38400, 57600, 115200
3. 确保与下位机设置一致
```

#### 3. 连接串口
```
1. 点击"连接串口"按钮
2. 状态指示器变为绿色"已连接"
3. 开始接收传感器数据
```

### MQTT功能使用

#### 1. 启动MQTT服务器

```bash
# 进入EMQX目录
cd emqx-5.0.26-windows-amd64\bin

# 启动服务器
.\emqx.cmd start

# 验证服务器状态
# 浏览器访问: http://localhost:18083
# 用户名: admin, 密码: public
```

#### 2. 配置MQTT连接

```
1. 在"MQTT服务器"输入框中输入服务器地址
2. 格式: IP地址:端口 (如: 192.168.1.100:1883)
3. 本地服务器: localhost:1883
```

#### 3. 连接MQTT

```
1. 点击"连接MQTT"按钮
2. 状态指示器显示"已连接"（绿色）
3. 勾选"自动发布数据到MQTT"
```

#### 4. 数据发布格式

发布的JSON数据格式：
```json
{
    "timestamp": "2025-12-18 15:30:45.123",
    "temperature": 25.5,
    "humidity": 60.2,
    "waterLevel": 45,
    "airQuality": "nice",
    "servoStatus": "ON",
    "motorStatus": "OFF",
    "alarmType": "正常",
    "hasAlarm": false
}
```

### 视频监控配置

#### 1. ESP32-CAM设置

确保ESP32-CAM已正确配置：
```cpp
// ESP32-CAM代码示例
#include "esp_camera.h"

// 配置摄像头引脚
#define PWDN_GPIO_NUM     32
#define RESET_GPIO_NUM    -1
#define XCLK_GPIO_NUM      0
// ... 其他引脚配置

// 启动Web服务器
void startCameraServer(){
    httpd_config_t config = HTTPD_DEFAULT_CONFIG();
    httpd_uri_t stream_uri = {
        .uri       = "/stream",
        .method    = HTTP_GET,
        .handler   = stream_handler,
        .user_ctx  = NULL
    };
    // ... 服务器配置
}
```

#### 2. 连接视频监控

```
1. 在"ESP32地址"输入框中输入摄像头IP
2. 格式: http://192.168.1.100
3. 点击"连接"按钮
4. 视频画面将显示在右侧区域
```

### 设备控制操作

#### 模式切换
```
自动模式: 系统自动根据传感器数据控制设备
手动模式: 用户手动控制窗户和风扇
```

#### 窗户控制
```
窗户开: 发送指令"DUOJ-1"
窗户关: 发送指令"DUOJ-0"
```

#### 风扇控制
```
风扇开: 发送指令"MOTOR-1"
风扇关: 发送指令"MOTOR-0"
```

### 数据导出

#### CSV导出功能
```
1. 点击"导出CSV"按钮
2. 选择保存位置和文件名
3. 数据将保存为CSV格式
4. 包含所有历史数据和报警记录
```

#### CSV文件格式
```csv
序号,温度(°C),湿度(%),水位(%),空气质量,窗户状态,风扇状态,报警类型,时间戳
1,25.5,60.2,45,nice,ON,OFF,正常,2025-12-18 15:30:45
2,26.1,58.7,45,nice,ON,OFF,正常,2025-12-18 15:31:00
```

## 🔌 API文档

### 串口通信协议

#### 数据接收格式

程序支持以下数据格式：

**STM32格式**（默认）：
```
Temp:25C Humi:60%RH Water:45% Servo:ON Motor:OFF Air:nice
```

#### 控制指令格式

**设备控制指令**：
```
模式控制:
- mode-0: 切换到自动模式
- mode-1: 切换到手动模式

窗户控制:
- DUOJ-0: 关闭窗户
- DUOJ-1: 打开窗户

风扇控制:
- MOTOR-0: 关闭风扇
- MOTOR-1: 打开风扇
```

### MQTT API

#### 连接参数

```csharp
// MQTT连接配置
var options = new MqttClientOptionsBuilder()
    .WithTcpServer(server, port)
    .WithClientId("STM32_Greenhouse_Client_" + Guid.NewGuid().ToString())
    .WithCleanSession()
    .Build();
```

#### 发布主题

**默认主题**: `STM32_Greenhouse/data`

**消息格式**:
```json
{
    "timestamp": "2025-12-18 15:30:45.123",
    "temperature": 25.5,
    "humidity": 60.2,
    "waterLevel": 45,
    "airQuality": "nice",
    "servoStatus": "ON",
    "motorStatus": "OFF",
    "alarmType": "正常",
    "hasAlarm": false
}
```

#### 订阅主题

**默认主题**: `STM32_Greenhouse/control`

**控制消息格式**:
```
mode-0      // 自动模式
mode-1      // 手动模式
DUOJ-0      // 窗户关
DUOJ-1      // 窗户开
MOTOR-0     // 风扇关
MOTOR-1     // 风扇开
```

### HTTP API (视频监控)

#### ESP32-CAM视频流

**URL格式**:
```
http://[ESP32_IP]/stream
```

**请求示例**:
```http
GET http://192.168.1.100/stream HTTP/1.1
Host: 192.168.1.100
```

**响应格式**:
```http
HTTP/1.1 200 OK
Content-Type: multipart/x-mixed-replace; boundary=frame

--frame
Content-Type: image/jpeg
Content-Length: [图片大小]

[JPEG图片数据]
--frame
Content-Type: image/jpeg
Content-Length: [图片大小]

[JPEG图片数据]
```

## 📊 数据格式

### 传感器数据格式

#### STM32数据解析

**原始数据格式**:
```
Temp:25C Humi:60%RH Water:45% Servo:ON Motor:OFF Air:nice
```

**解析后的数据结构**:
```csharp
public class SensorData
{
    public double Temperature { get; set; }     // 温度 (°C)
    public double Humidity { get; set; }        // 湿度 (%)
    public int WaterLevel { get; set; }         // 水位 (%)
    public string AirQuality { get; set; }      // 空气质量 (nice/bad)
    public string ServoStatus { get; set; }     // 窗户状态 (ON/OFF)
    public string MotorStatus { get; set; }     // 风扇状态 (ON/OFF)
    public DateTime Timestamp { get; set; }     // 时间戳
}
```

### 历史数据存储

#### 内存数据结构

```csharp
// 历史数据列表
private List<double> _tempHistory = new List<double>();      // 温度历史
private List<double> _humHistory = new List<double>();       // 湿度历史
private List<double> _waterHistory = new List<double>();     // 水位历史
private List<string> _timestampHistory = new List<string>(); // 时间戳历史
private List<string> _alarmTypeHistory = new List<string>();// 报警类型历史
```

#### 数据限制
- **最大历史记录**: 100组数据
- **自动清理**: 超过限制时自动删除最早数据
- **内存管理**: 防止内存泄漏和过度占用

### 报警机制

#### 报警阈值设置
```csharp
private double _tempMin = 15.0;   // 温度下限 (°C)
private double _tempMax = 30.0;   // 温度上限 (°C)
private double _humMin = 30.0;    // 湿度下限 (%)
private double _humMax = 70.0;    // 湿度上限 (%)
```

#### 报警类型
- **温度报警**: 温度超出设定范围
- **湿度报警**: 湿度超出设定范围
- **水位报警**: 水位超过50%
- **空气质量报警**: 空气质量为"bad"
- **复合报警**: 多个参数同时异常

## 🤝 贡献指南

我们欢迎所有形式的贡献，包括bug修复、功能增强、文档改进等。

### 开发环境设置

#### 1. 克隆仓库
```bash
git clone https://github.com/your-repo/STM32_Greenhouse.git
cd STM32_Greenhouse
```

#### 2. 创建开发分支
```bash
git checkout -b feature/your-feature-name
```

#### 3. 开发规范

**代码风格**:
```csharp
// 使用有意义的变量名
private SerialPort _serialPort;
private List<double> _tempHistory;

// 使用PascalCase命名公共成员
public void ConnectSerialPort()
{
    // 方法实现
}

// 使用camelCase命名私有字段
private bool _isConnected;
```

**注释规范**:
```csharp
/// <summary>
/// 初始化串口通信参数
/// </summary>
/// <param name="portName">串口名称</param>
/// <param name="baudRate">波特率</param>
private void InitializeSerialPort(string portName, int baudRate)
{
    // 实现代码
}
```

#### 4. 提交代码
```bash
# 添加修改的文件
git add .

# 提交代码（遵循提交信息规范）
git commit -m "feat: 添加MQTT自动重连功能"

# 推送到远程分支
git push origin feature/your-feature-name
```

#### 5. 创建Pull Request

1. 在GitHub上创建Pull Request
2. 描述清楚修改的内容和原因
3. 关联相关的Issue
4. 等待代码审查

### 提交信息规范

我们遵循[Conventional Commits](https://www.conventionalcommits.org/)规范：

```
<type>[optional scope]: <description>

[optional body]

[optional footer(s)]
```

**类型说明**:
- `feat`: 新功能
- `fix`: Bug修复
- `docs`: 文档更新
- `style`: 代码格式调整
- `refactor`: 代码重构
- `test`: 测试相关
- `chore`: 构建过程或辅助工具的变动

**示例**:
```
feat: 添加视频监控功能

- 支持ESP32-CAM视频流显示
- 添加MJPEG格式解析
- 实现视频连接状态监控

Closes #123
```

### 测试指南

#### 单元测试
```csharp
[TestClass]
public class DataParserTests
{
    [TestMethod]
    public void ParseData_ValidInput_ReturnsCorrectValues()
    {
        // Arrange
        string input = "Temp:25C Humi:60%RH";
        
        // Act
        var result = DataParser.Parse(input);
        
        // Assert
        Assert.AreEqual(25.0, result.Temperature);
        Assert.AreEqual(60.0, result.Humidity);
    }
}
```

#### 集成测试
```bash
# 测试串口通信
# 1. 连接硬件设备
# 2. 启动程序
# 3. 验证数据接收

# 测试MQTT功能
# 1. 启动MQTT服务器
# 2. 连接客户端
# 3. 验证数据发布和订阅
```

### 文档贡献

我们欢迎文档改进，包括：
- 修复拼写错误和语法问题
- 添加更多使用示例
- 改进API文档
- 添加多语言支持

## 📄 许可证

本项目采用MIT许可证 - 查看[LICENSE](LICENSE)文件了解详情。

```
MIT License

Copyright (c) 2025 基于STM32的智能温室数据监控系统

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## 🙏 致谢

### 开源项目
- **[MQTTnet](https://github.com/dotnet/MQTTnet)** - 优秀的.NET MQTT客户端库
- **[System.IO.Ports](https://github.com/dotnet/runtime)** - .NET串口通信支持
- **[Windows Forms](https://github.com/dotnet/winforms)** - Windows桌面UI框架

### 硬件厂商
- **[STM32单片机](https://www.st.com/en/microcontrollers-microprocessors/stm32-32-bit-arm-cortex-mcus.html)** - 意法半导体ARM Cortex-M微控制器
- **[ESP32-CAM](https://www.espressif.com/en/products/socs/esp32)** - 乐鑫科技WiFi摄像头模组

### 开发工具
- **Visual Studio** - 强大的集成开发环境
- **Git** - 版本控制系统
- **GitHub** - 代码托管平台
---

**⭐ 如果这个项目对您有帮助，请给我一个Star！**
