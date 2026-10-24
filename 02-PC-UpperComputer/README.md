# 智能温室大棚监控系统 · PC 上位机

![C#](https://img.shields.io/badge/C%23-WinForms-512BD4?style=for-the-badge&logo=csharp&logoColor=white)
![.NET](https://img.shields.io/badge/.NET%20Framework-4.7.2-512BD4?style=for-the-badge)
![MQTTnet](https://img.shields.io/badge/MQTTnet-4.3.7-660066?style=for-the-badge)

STM32 智能温室物联网系统的**PC 端**：通过串口接收下位机环境数据帧，解析后实时展示并经 MQTT 发布上云，
同时订阅控制主题把远程指令透传到串口，是"手机 → MQTT → 串口 → 单片机"远程控制链路的中枢。

> 系统总览、固件、EMQX、小程序请见仓库根目录 README。

## 功能

- **串口解析**：正则匹配 `Temp:XXC` / `Humi:XX%RH` / `Soil:XX%(raw=XX)` / `Servo` / `Motor` / `Air` / `Warning`（与固件 V1.1+ 输出协议逐字段对应）
- **实时展示**：GDI+ 自绘温度/湿度/土壤湿度三曲线、六组状态图标（双状态随报警切换）、阈值报警日志
- **MQTT 双向**：发布 `green/data`（QoS 1，JSON 含 `soilMoisture` 字段）；订阅 `green/control` 并原文转发串口
- **设备控制**：自动/手动模式切换、天窗舵机、风扇电机按钮（对应 `mode-*` / `DUOJ-*` / `MOTOR-*` 指令）
- **视频监控**：ESP32-CAM MJPEG `/stream` 拉流逐帧解码显示
- **数据导出**：CSV（含时间戳与报警类型）

## 统一报警阈值（三端一致）

温度 > 26 °C / 空气湿度 > 85 %RH / 土壤湿度 > 40 % / 空气 bad——报警展示直接依据固件同一组数值与 `Warning` 标志。

## 编译与运行

Visual Studio 2019+ 打开 `DHT11.csproj`（NuGet 自动还原 MQTTnet 4.3.7，或使用 `packages/` 目录），编译生成 `智能温室.exe`：

1. 选择串口、9600 波特率 → 连接串口
2. MQTT 服务器填 EMQX 主机 `IP:1883` → 连接（发布 `green/data` / 订阅 `green/control`）
3. 勾选"自动发布数据到 MQTT"即可持续发布

详细操作说明见 [MQTT功能使用说明.md](./MQTT功能使用说明.md)。

## 关键源码

| 文件 | 说明 |
| --- | --- |
| `Form1.cs` | 全部 UI 与业务逻辑（串口/解析/图表/MQTT/视频流），约 2400 行 |
| `Program.cs` | 入口 |
| `MQTT功能使用说明.md` | MQTT 配置与三端联调文档（v2.3） |
