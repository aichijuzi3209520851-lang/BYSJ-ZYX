# 智能温室大棚监测小程序 · Smart Greenhouse Monitoring

![WeChat Mini Program](https://img.shields.io/badge/WeChat-MiniProgram-07C160?style=for-the-badge&logo=wechat&logoColor=white)
![MQTT](https://img.shields.io/badge/MQTT-over%20WebSocket-660066?style=for-the-badge&logo=eclipsemosquitto&logoColor=white)
![License](https://img.shields.io/badge/License-MIT-blue?style=for-the-badge)

STM32 智能温室物联网系统的**移动端**：通过 MQTT over WebSocket 订阅 PC 上位机发布的环境数据，
实时展示温度 / 空气湿度 / 土壤湿度 / 空气质量 / 天窗 / 风扇六项状态，报警时高亮提示并震动提醒。

> 系统总览、固件烧录、EMQX 部署请看仓库根目录的 README。

## 功能特性

- **MQTT 实时监测**：`wx.connectSocket` 连接 EMQX（默认 `8083` WebSocket 端口），手动构造 CONNECT / SUBSCRIBE 报文完成订阅，解析上位机发布的 JSON 数据帧（1.5 秒/帧）
- **连接保活与自动重连**：CONNECT 携带 keep-alive 60s，每 30s 发送 PINGREQ 心跳；异常断开按 2s/4s/8s 指数退避自动重连（最多 3 次），关闭前发送 DISCONNECT 优雅退出
- **强制手填订阅主题**：订阅主题默认留空，必须由用户输入（与上位机发布主题保持一致）；连接成功后自动写入历史记录（最多 5 条），下次在配置弹窗中一键回填
- **统一阈值告警**：与固件 / 上位机使用同一组阈值（温度>26°C、湿度>85%RH、土壤湿度>40%、空气 bad），卡片红色高亮、报警横幅、状态跳变时震动一次
- **本地持久化**：连接配置与历史记录通过 `wx.setStorageSync` 保存

## 目录结构

```
├── app.js               # 全局配置: 默认服务器 192.168.36.221:8083, 订阅主题默认留空
├── app.json             # 页面与 tabBar(首页/传感器)
├── app.wxss             # 全局设计令牌与通用样式
├── pages/
│   ├── home/            # 首页: 品牌区 / 功能介绍 / 系统信息 / 开发者
│   └── sensor/          # 传感器页: 连接状态栏 / 报警横幅 / 六宫格数据 / 配置弹窗(含历史)
├── ICON/ images/        # 图标资源(报警/正常双状态)
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

## MQTT 数据契约

上位机发布到 `green/data`（QoS 1）的 JSON：

```json
{
  "timestamp": "2026-10-25 15:30:45.123",
  "temperature": 26,
  "humidity": 60,
  "soilMoisture": 35,
  "airQuality": "nice",
  "servoStatus": "OFF",
  "motorStatus": "OFF",
  "alarmType": "正常",
  "hasAlarm": false
}
```

## License

MIT
