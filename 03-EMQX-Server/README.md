# EMQX MQTT 服务器 · 部署与一键启动器

本目录包含 EMQX 本地服务器的**一键启动器**源码（`MQTT.exe`）。
EMQX 完整发行版已作为本仓库 Release 附件提供（52MB，免官网下载）：

> **⬇️ 直接下载**：[emqx-5.0.26-windows-amd64.zip](https://github.com/aichijuzi3209520851-lang/BYSJ-ZYX/releases/download/emqx-5.0.26/emqx-5.0.26-windows-amd64.zip)（Release 页：[emqx-5.0.26](https://github.com/aichijuzi3209520851-lang/BYSJ-ZYX/releases/tag/emqx-5.0.26)）

## 一键启动器

`MQTT.csproj` 为 .NET 10 控制台程序（`Program.cs`），功能：

- **四级定位 EMQX 安装目录**：① 启动器同目录的 `emqx-path.txt` / `emqx-path.cfg`（写 `bin` 路径或发行包根目录都行）
  ② 环境变量 `EMQX_HOME` ③ 常见路径清单（含 `D:\EMQX\emqx-*`、`D:\EMQX-MQTT\emqx-*` 等）
  ④ 扫描 C/D 盘 1~3 级目录找 `emqx*-windows*` / `emqx-<数字>*`
- **自动读取实际配置的 Dashboard 端口**：解析 `etc/emqx.conf` 与 `data/configs/cluster.hocon` 里
  `dashboard.listeners.http.bind`，再依次探测「配置值 → 18083 → 18200」哪个真的在监听。
  **不再硬编码 18083**——若本机把 Dashboard 改成 18200（见下方「端口被 Windows 保留」说明），启动器也能正确判定就绪
- 后台执行 `emqx.cmd start`，轮询就绪状态最长 120 秒；超时会打印「已尝试探测的端口」便于排查
- 就绪后自动打开浏览器，打印局域网访问地址与默认账号（admin / public）
- 命令行子命令：`MQTT.exe status` 查看状态 / `MQTT.exe stop` 停止 EMQX

实现细节：

- EMQX 的输出重定向到临时文件而非管道——Erlang 虚拟机（beam）会继承管道句柄并长期存活，
  用管道捕获会导致启动器永远等不到 EOF 而卡死。
- **逐个目录独立 try/catch**：扫描到 `D:\Config.Msi` 这类无权限目录时只跳过该目录，不会中断整轮扫描
  （旧实现把整个循环包在一个 try 里，被一个无权限目录吞掉后就误报「未找到 EMQX 安装目录」）。
- 无真实控制台时（如被进程管理器启动）`Console.Title` 会抛 `IOException`，已包 try/catch 避免启动即崩。

编译：Visual Studio 2022+（.NET 10 SDK）直接打开 `MQTT.slnx` 生成；或 `dotnet build -c Release`。
单文件发布：`dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o ./dist`
（`-p:` 不能写成 `/p:`，在 Git Bash 里会被当成路径转换）。

### 端口被 Windows 保留导致 Dashboard 起不来

若 EMQX 日志报 `eacces` 无法绑定 18083，先确认该端口是否落在 Windows 的 TCP 排除区间：

```powershell
netsh int ipv4 show excludedportrange protocol=tcp
```

本机实测该命令输出包含 **18027–18126**，18083 / 18084 都在其中，因此 `etc/emqx.conf` 改为：

```
dashboard {
    listeners.http {
        ## 18083/18084 均落在本机 Windows 保留端口区间(18027-18126)内, 绑定报 EACCES, 本机改用 18200
        bind = 18200
    }
}
```

启动器会自动读到这个值并探测 18200，无需改代码。


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
| 18200 | EMQX Dashboard 网页管理（admin / public）。**默认 18083，本机因落在 Windows 保留区间而改为 18200**，以 `etc/emqx.conf` 的 `dashboard.listeners.http.bind` 为准 |

Windows 防火墙放行示例（管理员 PowerShell，端口按本机实际值替换）：

```powershell
New-NetFirewallRule -DisplayName "EMQX-MQTT" -Direction Inbound -Protocol TCP -LocalPort 1883,8083,18200 -Action Allow
```

## 本系统使用的主题

- `green/data` —— 上位机发布传感器 JSON，小程序订阅
- `green/control` —— 任意客户端发布控制指令，上位机订阅后原文透传串口
