# 智能温室大棚物联网监控系统 · Smart Greenhouse IoT System

![STM32](https://img.shields.io/badge/STM32-F103C8T6-blue?style=for-the-badge&logo=stmicroelectronics&logoColor=white)
![Bare-metal](https://img.shields.io/badge/固件-裸机分时隙主循环-2496ED?style=for-the-badge)
![C#](https://img.shields.io/badge/C%23-WinForms-512BD4?style=for-the-badge&logo=csharp&logoColor=white)
![EMQX](https://img.shields.io/badge/EMQX-5.0.26-FF6A00?style=for-the-badge&logo=eclipsemosquitto&logoColor=white)
![WeChat](https://img.shields.io/badge/微信小程序-Native-07C160?style=for-the-badge&logo=wechat&logoColor=white)
![License](https://img.shields.io/badge/License-MIT-green?style=for-the-badge)

一套完整的**毕业设计级**温室大棚物联网监控系统：STM32 下位机采集温室环境数据，
PC 上位机通过串口解析后经 MQTT 上云，微信小程序订阅数据并可下发控制指令，
实现"**感知 — 传输 — 展示 — 控制**"全链路闭环。

> **固件基线说明**：当前 `main` 分支为**裸机分时隙主循环**版本（结构简单、便于答辩演示与现场排障）。
> 早期 FreeRTOS 四任务版本（采集/报警/通信/显示 + 队列 + 互斥锁）完整保留在固件仓的
> [`rtos-backup`](https://github.com/aichijuzi3209520851-lang/STM32-IoT-Greenhouse/tree/rtos-backup) 分支，作为对照实现，不参与主线运行。

```
┌────────────────────────────────────────────┐
│      微信小程序 · MQTT over WebSocket      │
│       端口 8083 · 数据展示/报警提醒        │
└────────────────────────────────────────────┘
                       │  订阅 green/data ▲
                       │  发布 green/control ▼
┌────────────────────────────────────────────┐
│         EMQX MQTT Broker (5.0.26)          │
│     MQTT:1883 · WS:8083 · 控制台:18083     │
└────────────────────────────────────────────┘
                       │  green/data (JSON,QoS1) ▲
                       │  green/control 透传 ▼
┌────────────────────────────────────────────┐
│     PC 上位机 智能温室.exe (WinForms)      │
│       串口解析 · 曲线展示 · 指令转发       │
└────────────────────────────────────────────┘
                       │  环境数据帧 Temp/Humi/Soil/Light/Air ▲
                       │  mode/DUOJ/MOTOR 指令 ▼
┌────────────────────────────────────────────┐
│          STM32F103C8T6 · 裸机主循环        │
│      采集 → 显示 → 打印 → 指令 → 报警      │
│   DHT11 · 土壤湿度 · 光照 · 烟雾(MQ-2)     │
│     OLED · LED · 蜂鸣器 · 舵机 · 电机      │
└────────────────────────────────────────────┘
```

## 仓库结构

| 目录 | 内容 | 技术栈 |
| --- | --- | --- |
| [`01-STM32-Firmware`](./01-STM32-Firmware) | 下位机固件：裸机分时隙主循环、四路传感采集、OLED 显示、声光报警、执行器联动、双串口指令毫秒级响应 | C / Keil MDK5 / 标准外设库 |
| [`02-PC-UpperComputer`](./02-PC-UpperComputer) | PC 上位机：串口解析、曲线图表、MQTT 发布/订阅转发、视频监控面板 | C# .NET Framework 4.7.2 / WinForms / MQTTnet |
| [`03-EMQX-Server`](./03-EMQX-Server) | EMQX 一键启动器（MQTT.exe）与服务器部署说明 | C# .NET 10 控制台 |
| [`04-WeChat-Miniprogram`](./04-WeChat-Miniprogram) | 微信小程序：温室主题轮播首页、五项环境数据、执行器远程控制、报警提醒、MQTT 配置与历史 | 原生小程序 / MQTT over WebSocket |

## 核心特性

- **三层架构、一份数据**：固件（1.5s/帧）→ 上位机（解析 + 发布 `green/data`）→ 小程序（订阅展示 / 下发控制）
- **下位机裸机主循环**：`while(1)` 分时隙顺序执行 采集 → OLED 刷新 → 协议帧打印 → 串口指令消费 → 报警联动，周期 = 各步耗时 + 1.5s 延时；USART1/USART3 中断收帧、主循环消费，指令响应为**毫秒级**；ISR 共享变量统一 `volatile` 声明，DHT11 µs 级时序以关中断保护
- **控制下行链路**：任意 MQTT 客户端向 `green/control` 发指令 → 上位机原文透传串口 → 固件执行（模式切换 / 天窗舵机 / 风扇电机）；小程序「执行器」页与任意 MQTT 客户端均可下发
- **统一报警阈值（三端一致，固件为权威判定源）**：

  | 指标 | 报警条件 | 联动 |
  | --- | --- | --- |
  | 温度 | > 26 °C | 黄灯 + 天窗开启 180° + 蜂鸣器 |
  | 空气湿度 | > 85 %RH | 黄灯 + 天窗开启 180° + 蜂鸣器 |
  | 土壤湿度 | > 40 %（过湿） | 蓝灯 + 蜂鸣器 |
  | 空气质量 | 烟雾判定 bad | 红灯 + 风扇全速排风 + 蜂鸣器 |
  | 光照强度 | — | **仅采集展示，不联动**（与固件 `Light.h` 约定一致） |

- **稳定的小程序 MQTT 接入**：keep-alive 60s + 30s PINGREQ 心跳 + 指数退避自动重连 + DISCONNECT 优雅退出
- **小程序温室主题轮播首页**：4 张 Pexels 免费商用实拍图（大棚纵览/番茄/草莓/LED 垂直农场）自动轮播 + 胶囊指示点
- **上位机附加能力**：GDI+ 实时三曲线图、CSV 导出、ESP32-CAM MJPEG 视频监控面板

## 数据契约

### 串口帧（STM32 → 上位机，`\r\n` 结尾）

```
Temp:26C   Humi:60%RH   Soil:35%(raw=1200)  Light:72%  Servo:OFF Motor:OFF  Air:nice
```

| 字段 | 含义 | 备注 |
| --- | --- | --- |
| `Temp:` | 温度 °C | DHT11 |
| `Humi:` | 空气湿度 %RH | DHT11 |
| `Soil:` / `raw=` | 土壤湿度 % / ADC 原始值 | D2-5-2K，两点校准用 |
| `Light:` | 光照强度 % | 光敏电阻 ADC1_IN9，仅展示 |
| `Servo:` / `Motor:` | 天窗 / 风扇状态 | ON / OFF |
| `Air:` | 空气质量 | nice / bad |
| `Warning` | 行尾报警标记 | 出现即表示固件判定报警 |

### MQTT JSON（主题 `green/data`，QoS 1，Retain 关闭）

```json
{
  "timestamp": "2026-10-25 15:30:45.123",
  "temperature": 26,
  "humidity": 60,
  "soilMoisture": 35,
  "lightPercent": 72,
  "airQuality": "nice",
  "servoStatus": "OFF",
  "motorStatus": "OFF",
  "alarmType": "正常",
  "hasAlarm": false
}
```

### 控制指令（主题 `green/control` → 串口透传）

| 指令 | 作用 |
| --- | --- |
| `mode-0` / `mode-1` | 自动 / 手动模式 |
| `DUOJ-0` / `DUOJ-1` | 天窗关(0°) / 开(180°)，手动模式生效 |
| `MOTOR-0` / `MOTOR-1` | 风扇停 / 全速，手动模式生效 |

## 快速开始

### 1. 硬件与固件

1. 硬件：STM32F103C8T6 最小系统板 + DHT11 + D2-5-2K 土壤湿度模块 + MQ-2 烟雾模块 + 0.96" OLED + SG90 舵机 + 直流电机 + LED/蜂鸣器（完整引脚表见 [01-STM32-Firmware/README](./01-STM32-Firmware/README.md)）
2. Keil MDK5 打开 `01-STM32-Firmware/Project.uvprojx`，编译并烧录
3. 首次使用需按 `System/Water.h` 中的 `SOIL_AIR_ADC / SOIL_WATER_ADC` 两点校准土壤湿度
4. USB 转串口接 USART1（PA9/PA10），上位机选 9600 波特率连接

### 2. 启动 EMQX 服务器

`03-EMQX-Server` 内的启动器可自动定位并启动 EMQX（默认路径 `D:\EMQX-MQTT\emqx-5.0.26-windows-amd64`）：

- 双击 `MQTT.exe` → 启动并打开 Web 控制台（`http://localhost:18083`，admin/public）
- 命令行 `MQTT.exe stop` → 停止服务器

EMQX 发行包（52MB）已作为仓库 Release 附件提供：
**[⬇️ emqx-5.0.26-windows-amd64.zip 一键下载](https://github.com/aichijuzi3209520851-lang/BYSJ-ZYX/releases/download/emqx-5.0.26/emqx-5.0.26-windows-amd64.zip)**
（也可从 [EMQX 官网](https://www.emqx.io/zh/downloads) 下载 5.0.26 Windows amd64 版本）
解压到 D 盘 1~2 级浅层目录（深路径会导致 Erlang 启动异常）。防火墙需放行 **1883 / 8083 / 18083** 端口。

### 3. 运行 PC 上位机

Visual Studio 2019+ 打开 `02-PC-UpperComputer/DHT11.csproj`（NuGet 会自动还原 MQTTnet 4.3.7），
编译运行 `智能温室.exe`：

1. 选择串口、9600 波特率 → 连接串口
2. MQTT 服务器填 EMQX 主机 IP + `:1883` → 连接 MQTT（发布主题 `green/data` / 订阅主题 `green/control`）
3. 勾选"自动发布数据到 MQTT"即可看到日志持续发布

### 4. 微信小程序

微信开发者工具导入 `04-WeChat-Miniprogram`，勾选"不校验合法域名"，
传感器页 → 配置 → 填入 EMQX 主机 IP、WebSocket 端口 `8083`、订阅主题 `green/data` → 保存并连接。
连接过的配置会自动存入历史，下次一键回填。

## 各端默认配置速查

| 配置项 | PC 上位机 | 微信小程序 |
| --- | --- | --- |
| 服务器 IP | `192.168.36.221` | `192.168.36.221` |
| 端口 | 1883 (TCP) | 8083 (WebSocket) |
| 数据主题 | 发布 `green/data` | 订阅 `green/data`（默认留空，手动输入） |
| 控制主题 | 订阅 `green/control` | 发布 `green/control` |

## 相关文档

- [固件说明与引脚表](./01-STM32-Firmware/README.md)
- [固件 FreeRTOS 版（`rtos-backup` 分支）](https://github.com/aichijuzi3209520851-lang/STM32-IoT-Greenhouse/tree/rtos-backup)
- [上位机 MQTT 使用说明](./02-PC-UpperComputer/MQTT功能使用说明.md)
- [EMQX 部署与一键启动器](./03-EMQX-Server/README.md)
- [小程序说明](./04-WeChat-Miniprogram/README.md)
- [MQTT 连接故障排查](./04-WeChat-Miniprogram/MQTT连接故障排查.md) / [真机调试指南](./04-WeChat-Miniprogram/真机调试MQTT连接指南.md)

## License

[MIT](./LICENSE) © 2026 爱吃橘子
