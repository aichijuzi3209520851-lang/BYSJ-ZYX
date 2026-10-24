# MQTT连接故障排查指南

## 问题症状
微信小程序中"传感器"界面无法连接到本地MQTT服务器

## 已完成的优化
1. ✓ 添加详细的连接日志
2. ✓ 改进错误处理和提示
3. ✓ 添加MQTT数据包发送/接收日志
4. ✓ 显示连接状态和加载动画

## 故障排查步骤

### 1. 检查微信开发者工具设置 ⭐ 最重要

在微信开发者工具中，必须勾选以下选项：

**位置**: 右上角 "详情" -> "本地设置"

请勾选:
- ✓ **不校验合法域名、web-view（业务域名）、TLS 版本以及 HTTPS 证书**

如果不勾选此选项，微信小程序会拒绝连接到本地服务器！

### 2. 检查EMQX服务器配置

确认EMQX服务器已启动并正确配置WebSocket支持。

#### 2.1 检查EMQX是否运行

Windows命令行:
```cmd
# 检查EMQX服务状态
emqx_ctl status

# 或者检查8083端口是否被监听
netstat -ano | findstr "8083"
```

#### 2.2 检查EMQX WebSocket配置

编辑EMQX配置文件 `etc/emqx.conf`:

```conf
# 确保WebSocket监听器已启用
listeners.ws.default {
  bind = "0.0.0.0:8083"
  websocket.mqtt_path = "/mqtt"
  max_connections = 1024000
}
```

重启EMQX使配置生效:
```cmd
emqx stop
emqx start
```

### 3. 检查防火墙设置

#### Windows防火墙

打开 Windows PowerShell（管理员权限）:

```powershell
# 添加入站规则，允许8083端口
New-NetFirewallRule -DisplayName "EMQX WebSocket" -Direction Inbound -Protocol TCP -LocalPort 8083 -Action Allow
```

或者在 Windows 防火墙高级设置中手动添加规则。

### 4. 确认IP地址

#### 获取本机IP地址

```cmd
ipconfig
```

查找 "无线局域网适配器 WLAN" 或 "以太网适配器" 下的 IPv4 地址。

**注意**: 不要使用 `127.0.0.1` 或 `localhost`，必须使用实际的局域网IP地址（如 192.168.x.x）

### 5. 测试MQTT连接

#### 5.1 使用MQTTX客户端测试

下载并安装 [MQTTX](https://mqttx.app/)

连接配置:
- 协议: `ws://`
- 主机: `192.168.36.221`（你的IP）
- 端口: `8083`
- 路径: `/mqtt`
- 完整URL: `ws://192.168.36.221:8083/mqtt`

如果MQTTX能成功连接，说明EMQX服务器配置正确。

#### 5.2 使用浏览器测试

打开浏览器控制台（F12），执行:

```javascript
const ws = new WebSocket('ws://192.168.36.221:8083/mqtt', ['mqtt']);

ws.onopen = () => console.log('✓ WebSocket已连接');
ws.onerror = (err) => console.error('✗ 连接失败:', err);
ws.onclose = () => console.log('连接已关闭');
```

### 6. 在小程序中查看调试日志

运行小程序后，打开微信开发者工具的控制台（Console标签页），查看详细日志:

成功连接时应看到:
```
===== MQTT连接开始 =====
连接URL: ws://192.168.36.221:8083/mqtt
✓ WebSocket连接请求已发送
✓ WebSocket连接已成功建立
客户端ID: wx_sensor_xxxxxxxx
正在发送MQTT CONNECT包...
✓ MQTT CONNECT包发送成功
✓ 收到CONNACK确认包
正在订阅主题: LCW2
✓ MQTT SUBSCRIBE包发送成功
✓ 收到SUBACK确认包，订阅成功
```

失败时会显示详细的错误信息。

### 7. 修改连接配置

如果IP地址变化了，可以在小程序中修改:

1. 进入"传感器"页面
2. 点击右上角的设置按钮
3. 修改MQTT服务器地址、端口和主题
4. 点击"保存并连接"

### 8. 常见错误及解决方法

#### 错误: "connection refused" 或 "连接失败"
- 原因: EMQX服务器未启动或端口错误
- 解决: 启动EMQX，检查端口配置

#### 错误: "timeout" 或超时
- 原因: 防火墙阻止连接
- 解决: 开放8083端口

#### 错误: "invalid url" 或 URL无效
- 原因: IP地址格式错误
- 解决: 检查IP地址格式（如 192.168.1.100）

#### 错误: "request:fail"
- 原因: 微信开发者工具未勾选"不校验合法域名"
- 解决: 在"详情"->"本地设置"中勾选该选项

### 9. 测试MQTT消息发布

使用MQTTX或命令行工具发布测试消息:

```bash
# 使用mosquitto_pub（需安装mosquitto客户端）
mosquitto_pub -h 192.168.36.221 -p 1883 -t LCW2 -m '{"temperature":25.5,"humidity":60,"waterLevel":30,"airQuality":"nice","warning":false}'
```

如果小程序收到消息，控制台会显示:
```
收到MQTT消息，数据包大小: xxx bytes
消息类型: 0x3x
这是一个PUBLISH消息
消息负载: {"temperature":25.5,...}
✓ 解析成功，传感器数据: {...}
```

## EMQX快速配置参考

### Windows环境下EMQX配置文件位置
```
C:\Program Files\emqx\etc\emqx.conf
```

### 关键配置项

```conf
# WebSocket监听器
listeners.ws.default {
  bind = "0.0.0.0:8083"
  websocket.mqtt_path = "/mqtt"
  max_connections = 1024000
  websocket.compress = true
}

# TCP监听器（用于标准MQTT客户端）
listeners.tcp.default {
  bind = "0.0.0.0:1883"
  max_connections = 1024000
}
```

## 联系支持

如果按照以上步骤仍无法解决问题，请提供以下信息:
1. 微信开发者工具控制台的完整日志
2. EMQX服务器日志（位于 `log/emqx.log`）
3. 使用MQTTX测试的结果截图
4. 本机IP地址和EMQX版本

## 参考资料

- [EMQX官方文档](https://www.emqx.io/docs/)
- [微信小程序WebSocket API](https://developers.weixin.qq.com/miniprogram/dev/api/network/websocket/wx.connectSocket.html)
- [MQTT协议规范](http://mqtt.org/)
