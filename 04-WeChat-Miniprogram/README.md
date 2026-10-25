# 智能温室大棚监测小程序 · Smart Greenhouse Monitoring

![WeChat Mini Program](https://img.shields.io/badge/WeChat-MiniProgram-07C160?style=for-the-badge&logo=wechat&logoColor=white)
![MQTT](https://img.shields.io/badge/MQTT-over%20WebSocket-660066?style=for-the-badge&logo=eclipsemosquitto&logoColor=white)
![License](https://img.shields.io/badge/License-MIT-blue?style=for-the-badge)

STM32 智能温室物联网系统的**移动端**：通过 MQTT over WebSocket 订阅 PC 上位机发布的环境数据，
实时展示温度 / 空气湿度 / 土壤湿度 / 光照强度 / 空气质量五项环境状态，并可远程控制天窗与风扇。

> 系统总览、固件烧录、EMQX 部署请看仓库根目录的 README。

## 功能特性

- **温室主题轮播首页**：Hero 区 4 张 Pexels 免费商用实拍图（拱形大棚纵览 / 番茄行栽 / 草莓大棚 / LED 垂直农场）自动轮播（4s/帧、无缝循环），底部渐变遮罩承载白字标题，胶囊式指示点随滑动激活
- **MQTT 实时监测**：`wx.connectSocket` 连接 EMQX（默认 `8083` WebSocket 端口），手动构造 CONNECT / SUBSCRIBE 报文完成订阅，解析上位机发布的 JSON 数据帧（1.5 秒/帧）
- **执行器远程控制**：「执行器」页支持自动/手动模式切换（`mode-0/1`），手动模式下远程开关天窗（`DUOJ-0/1`）与风扇（`MOTOR-0/1`），指令经 `green/control` 主题 → 上位机原文转发串口 → STM32 执行；设备真实状态由 `green/data` 回显
- **连接保活与自动重连**：CONNECT 携带 keep-alive 60s，每 30s 发送 PINGREQ 心跳；异常断开按 2s/4s/8s 指数退避自动重连（最多 3 次），关闭前发送 DISCONNECT 优雅退出
- **强制手填订阅主题**：订阅主题默认留空，必须由用户输入（与上位机发布主题保持一致）；连接成功后自动写入历史记录（最多 5 条，传感器页与执行器页共用），下次在配置弹窗中一键回填
- **统一阈值告警**：与固件 / 上位机使用同一组阈值（温度>26°C、湿度>85%RH、土壤湿度>40%、空气 bad），卡片红色高亮、报警横幅、状态跳变时震动一次；**光照强度仅展示不参与报警**（与固件 `Light.h` 约定一致）
- **本地持久化**：连接配置与历史记录通过 `wx.setStorageSync` 保存

## 目录结构

```
├── app.js               # 全局配置: 默认服务器 192.168.36.221:8083, 订阅主题默认留空
├── app.json             # 页面与 tabBar(首页/传感器/执行器), 浅色导航栏
├── app.wxss             # 全局设计令牌与通用样式
├── utils/
│   └── mqtt.js          # 共享 MQTT 3.1.1 客户端: 传感器页与执行器页共用(订阅+发布)
├── pages/
│   ├── home/            # 首页: 4图轮播Hero / 功能介绍 / 系统信息 / 开发者
│   ├── sensor/          # 传感器页: 连接状态栏 / 报警横幅 / 五项环境数据(光照整行空气质量) / 配置弹窗(含历史)
│   └── actuator/        # 执行器页: 模式切换 / 天窗·风扇控制卡片 / 最近指令 / 配置弹窗
├── ICON/ images/        # 图标资源(报警/正常/开关双状态) + hero-*.jpg 轮播背景图
├── MQTT连接故障排查.md   # EMQX/防火墙/域名校验排障指南
└── 真机调试MQTT连接指南.md # 真机调试网络配置指南(配套 检查网络配置.bat)
```

## 快速上手

1. 微信开发者工具导入本目录，AppID 使用自己的测试号或已有 AppID
2. 「详情 → 本地设置」勾选 **不校验合法域名、web-view（业务域名）、TLS 版本以及 HTTPS 证书**
3. 先启动 EMQX 服务器并让上位机连上（见仓库根目录 README），然后在"传感器"页点击 **配置**：
   - 服务器地址：EMQX 所在电脑的局域网 IP（默认示例 `192.168.36.221`）
   - WebSocket 端口：`8083`
   - 订阅主题：与上位机发布主题一致（默认 `green/data`）
4. 点击"保存并连接"，状态栏变绿、卡片出现数据即成功
5. 切到「执行器」页：先切换 **手动控制** 模式，再用卡片上的按钮开关天窗 / 风扇
   （自动模式下设备按统一阈值自主联动，按钮呈禁用态；配置与连接历史与传感器页共用）

## MQTT 数据契约

上位机发布到 `green/data`（QoS 1）的 JSON：

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

小程序发布到 `green/control` 的**纯文本指令**（上位机订阅后原文经串口转发固件）：

| 指令 | 含义 | 生效条件 |
|------|------|----------|
| `mode-0` / `mode-1` | 自动 / 手动模式 | 任意时刻 |
| `DUOJ-0` / `DUOJ-1` | 天窗关(0°) / 天窗开(180°) | 仅手动模式 |
| `MOTOR-0` / `MOTOR-1` | 风扇停 / 风扇全速 | 仅手动模式 |

## License

MIT
