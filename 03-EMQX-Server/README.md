# EMQX MQTT 服务器 · 部署与一键启动器

本目录包含 EMQX 本地服务器的**一键启动器**源码（`MQTT.exe`）。
EMQX 完整发行版已作为本仓库 Release 附件提供（52MB，免官网下载）：

> **⬇️ 直接下载**：[emqx-5.0.26-windows-amd64.zip](https://github.com/aichijuzi3209520851-lang/BYSJ-ZYX/releases/download/emqx-5.0.26/emqx-5.0.26-windows-amd64.zip)（Release 页：[emqx-5.0.26](https://github.com/aichijuzi3209520851-lang/BYSJ-ZYX/releases/tag/emqx-5.0.26)）

## 一键启动器

`MQTT.csproj` 为 .NET 10 控制台程序（`Program.cs`），功能：

- 自动在 D 盘 1~2 级目录定位 `emqx-*-windows-amd64`
- 后台执行 `emqx.cmd start`，轮询 Web 控制台端口（18083）最长 90 秒
- 就绪后自动打开浏览器，打印局域网访问地址与默认账号（admin / public）
- 命令行 `MQTT.exe stop` 停止 EMQX

实现细节：EMQX 的输出重定向到临时文件而非管道——Erlang 虚拟机（beam）会继承管道句柄并长期存活，
用管道捕获会导致启动器永远等不到 EOF 而卡死。

编译：Visual Studio 2022+（.NET 10 SDK）直接打开 `MQTT.slnx` 生成；或 `dotnet build -c Release`。

## 部署 EMQX

1. 下载 EMQX 发行包（两种方式任选）：
   - **本仓库 Release 附件（推荐，免翻墙）**：[emqx-5.0.26-windows-amd64.zip](https://github.com/aichijuzi3209520851-lang/BYSJ-ZYX/releases/download/emqx-5.0.26/emqx-5.0.26-windows-amd64.zip)
   - [EMQX 官网](https://www.emqx.io/zh/downloads) 下载 **v5.0.26 · Windows amd64**
2. 解压到 **D 盘 1~2 级浅层目录**，推荐：`D:\EMQX-MQTT\emqx-5.0.26-windows-amd64`
   （EMQX 放在过深的路径会导致 Erlang 虚拟机启动异常，启动器按此约定扫描）
3. 双击 `MQTT.exe` 启动，或手动 `cd emqx-5.0.26-windows-amd64\bin && .\emqx.cmd start`

## 需要放行的端口

| 端口 | 用途 |
| --- | --- |
| 1883 | MQTT over TCP —— PC 上位机连接 |
| 8083 | MQTT over WebSocket —— 微信小程序连接 |
| 18083 | EMQX Dashboard 网页管理（admin / public） |

Windows 防火墙放行示例（管理员 PowerShell）：

```powershell
New-NetFirewallRule -DisplayName "EMQX-MQTT" -Direction Inbound -Protocol TCP -LocalPort 1883,8083,18083 -Action Allow
```

## 本系统使用的主题

- `green/data` —— 上位机发布传感器 JSON，小程序订阅
- `green/control` —— 任意客户端发布控制指令，上位机订阅后原文透传串口
