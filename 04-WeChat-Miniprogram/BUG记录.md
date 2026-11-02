# BUG 记录

本文件记录小程序在实际联调中暴露并已修复的缺陷。每条包含：现象、根因、证据、修复、验证方法。

---

## BUG-001 执行器页/传感器页把指令主题当数据主题订阅

**发现时间**：2026-11-02
**影响范围**：`pages/actuator/actuator.js`、`pages/sensor/sensor.js`、`utils/mqtt.js`
**严重程度**：高（功能可用性 + 控制台刷错）

### 现象

微信开发者工具控制台成片输出：

```
actuator.js? [sm]:223 ✗ JSON解析失败: SyntaxError: Unexpected token 'm', "mode-1" is not valid JSON
actuator.js? [sm]:223 ✗ JSON解析失败: SyntaxError: Unexpected token 'D', "DUOJ-1" is not valid JSON
actuator.js? [sm]:223 ✗ JSON解析失败: SyntaxError: Unexpected token 'M', "MOTOR-1" is not valid JSON
```

每点一次控制按钮就刷一组。同时传感器页显示不出来数据。

### 根因

`green/control` 是**下行纯文本指令通道**：小程序往这里发布 `mode-1` / `DUOJ-1` / `MOTOR-1` 这类字面量指令，PC 上位机订阅后原文转发到串口。它不承载任何 JSON。

但两页都把它填进了「数据主题」并订阅了。MQTT 的语义是**发布者自己也会收到自己发布的消息**（broker 不区分发布/订阅来源），于是：

```
小程序 publish('green/control', 'mode-1')
  → EMQX 原样回送给同一个小程序（因为它订阅了这个主题）
  → handleSensorJson('mode-1')
  → JSON.parse('mode-1') 抛 SyntaxError
```

即"自己发出的指令绕一圈打回自己脸上"。

### 证据（端到端复现，裸 MQTT 3.1.1 over TCP）

| 场景 | 订阅主题 | 发布目标 | 收到报文数 |
| --- | --- | --- | --- |
| A（旧行为） | `green/control` | `green/control` | **1 条** `topic=green/control payload="mode-1"` |
| B（修复后） | `green/data` | `green/control` | **0 条** |

### 修复

1. `utils/mqtt.js` 新增主题常量与判定：
   - `DEFAULT_DATA_TOPIC = 'green/data'`
   - `CONTROL_TOPIC = 'green/control'`
   - `isControlTopic(topic)`：`trim()` 后严格相等比较（不做前缀匹配，`green/control/x` 返回 false）
2. `utils/mqtt.js` 的 CONNACK 订阅循环里跳过指令主题，并打印 warn 日志。
3. 两页的 `loadConfig()` 读到存储里的指令主题时直接丢弃，并 `wx.removeStorageSync('mqtt_topic')` 清掉，避免下次启动又被读回来。
4. 两页的 `saveConfigAndConnect()` 在 `isControlTopic` 命中时 `wx.showModal` 拦截，明确告知应填 `green/data`。
5. 两页的 `handleSensorJson()` 开头加 `if (!raw.startsWith('{')) return` —— 纵深防御，任何非 JSON 报文静默忽略而不是抛错刷屏。
6. 两页的配置弹窗新增 `.field-hint` 提示行，占位符改用 `{{dataTopicDefault}}`。

### 验证方法

1. `node --check` 三个 js 文件通过。
2. 修复后在执行器页点控制按钮，控制台**不应**再出现 `Unexpected token`。
3. 连接日志里应显示订阅的是 `green/data`。

---

## BUG-002 真机调试填了虚拟网卡地址导致连不上（模拟器却正常）

**发现时间**：2026-11-02
**影响范围**：`03-EMQX-Server/Program.cs`（错误地址的**来源**）、小程序配置流程（**受害者**）
**严重程度**：高（真机完全不可用，且极具迷惑性）

### 现象

- 微信开发者工具的**模拟器**里：连得上，状态栏变绿，数据正常刷新。
- 同一份代码跑到**手机真机调试**：一直"正在连接…"直到超时，永远连不上。
- 用户日志里的连接地址：`ws://192.168.40.1:8083/mqtt`

### 根因（两层）

**第一层：`192.168.40.1` 是 VMware 的虚拟网卡（VMnet8），不是电脑的局域网地址。**

它只存在于这台电脑内部。模拟器恰好也跑在这台电脑上，所以填任何本机网卡地址（包括虚拟网卡、甚至 `127.0.0.1`）都能连通；手机是独立设备，它所在的热点网段里根本没有这个地址。

**这正好解释了「模拟器能连、真机连不上」这个最令人困惑的现象。**

**第二层：EMQX 启动器主动把这个错误地址推荐给了用户。**

`BYSJ-ZYX/03-EMQX-Server/Program.cs` 的 `GetLanIPv4Addresses()` 原实现只排除 Loopback，把所有 Up 状态的网卡全部列出来当"局域网访问地址"。用户照着控制台抄了 `192.168.40.1` 填进手机。

### 证据

本机实测的全部 IPv4 网卡（`NetworkInterface.GetAllNetworkInterfaces()` 与 `Get-NetIPConfiguration` 交叉确认）：

| 网卡 | 类型 | IPv4 | 默认网关 | 手机能用 |
| --- | --- | --- | --- | --- |
| WLAN (MediaTek Wi-Fi 6 MT7921) | 物理 | `192.168.46.46` | `192.168.46.240` | ✅ |
| VMware Network Adapter VMnet1 | 虚拟 | `192.168.198.1` | 空 | ❌ |
| VMware Network Adapter VMnet8 | 虚拟 | `192.168.40.1` | 空 | ❌ |
| vEthernet (Default Switch) | Hyper-V | `172.20.144.1` | 空 | ❌ |
| Tailscale | 隧道 | `100.86.134.42` | 空 | ❌ |

其它已排除项（都不是原因）：

- 防火墙：`Domain/Private/Public` 三档 `Enabled` 全为 **False**。
- EMQX 绑定：`0.0.0.0` 监听 `1883` / `8083` / `18083`，不是只绑 `127.0.0.1`。
- 本机自连：`127.0.0.1` / `192.168.46.46` / `192.168.40.1` / `100.86.134.42` 的 8083 全部连通。

### 修复

**1. 启动器不再误导（根因侧）** —— `03-EMQX-Server/Program.cs`

- `GetLanIPv4Addresses()` 返回类型由 `List<IPAddress>` 改为 `List<LanAddress>`，新增 `record LanAddress(string Ip, string Nic, bool Preferred)`。
- **判据：是否有默认网关。** `IPv4DefaultGateway` 为空的网卡标记为非首选；同时跳过 `169.254.*`（APIPA，即"没连上网"时的自分配地址）。
- 首选网卡优先输出，打印成：
  ```
  ★ WLAN  —— 手机就用这个
      控制台   http://192.168.46.46:18083
      小程序   服务器地址填 192.168.46.46    端口 8083
      上位机   192.168.46.46:1883
  ```
  非首选输出为 `虚拟网卡, 手机连不上, 勿用`，末尾附说明。
- 无任何可用地址时打印「⚠ 没找到可用的局域网地址: 本机似乎没有连上 Wi-Fi / 网线」。

> 「有默认网关」这个判据已被实测证明准确：本机 5 块网卡里只有 WLAN 有网关。

**2. 小程序侧加校验（用户侧）** —— `utils/mqtt.js`

新增 `checkHostReachability(host)` 与 `describeVirtualHint(host)`：

- `127.0.0.1` / `localhost` → 提示"手机会连到手机自己"
- `100.64~100.127.*` → Tailscale / CGNAT 段
- `172.16~172.31.*` → Hyper-V / WSL2 段
- `192.168.x.1`（末位是 1）→ 提示"常见于 VMware / Hyper-V 宿主机侧地址，请核对"
- 空值 → 提示填写局域网 IP

**设计决定：只提示不拦截。** 用户完全可能真把 EMQX 装在某个虚拟网段上（例如用 VPN 组网），硬拦会误伤。

接线位置：
- `saveConfigAndConnect()` → 命中则 `wx.showModal`「这个地址手机连不上」并中止。
- `onHostInput()` → 边输入边提示，写入 `hostHint`。
- `loadConfig()` → 恢复历史地址时同样计算提示。

**3. 移除死配置与错误示例**

- `app.js` 的 `globalData.mqttConfig.host = '192.168.36.221'` 是**全项目无人读取**的死配置（grep 只命中它自己），但会被人照抄 → 已删除，改为注释说明。
- 两页 `mqttHost` 默认值由 `'192.168.36.221'` 改为 `''`，强制用户填写真实地址。
- 两页 WXML 占位符由「例如 192.168.36.221」改为「电脑 Wi-Fi 的 IPv4 地址, 如 192.168.46.46」，并新增 `hostHint` 提示行。

**4. 打开局域网真机调试开关**

`project.private.config.json` 的 `"useLanDebug": false` → `true`。

**5. 文档与工具同步**

- `真机调试MQTT连接指南.md` 开头新增「⚠ 最常见的坑：填了虚拟网卡地址」整节（含网卡对照表、一秒判断法、地址铁律表），第 2 步的「重要提示」补上虚拟网卡禁令，问题 1 补上该排查分支。
- `MQTT连接故障排查.md` 第 4 节补充虚拟网卡段落与 PowerShell 一行命令。
- `检查网络配置.bat` 从 4 步扩到 5 步：第 1 步改为**按网关自动标注** `[手机就用这个]` / `[虚拟网卡, 勿用]`，并同时检查 8083 / 1883 / 18083 三个端口，末尾给出"模拟器能连真机连不上"的专属提示。
- `README.md` 的目录结构说明与快速上手步骤同步更新。

> **修正记录（开发中被单元测试抓出来的漏洞）**：最初的实现把风险判定写成两个互不相干的函数 ——
> `checkHostReachability`（硬拦）和 `describeVirtualHint`（软提示），但 `saveConfigAndConnect()`
> 只接了硬拦那一个。结果本机真正出事的那两个地址 `192.168.40.1` / `192.168.198.1` 恰好都落在
> "软提示"档，**保存时什么都不弹，等同于没修**。
>
> 现统一为 `describeHostRisk(host)` → `{ level, message }`，`level` 取 `'block' | 'warn' | ''`，
> 保存流程对两级都做了处理：
> - `block` → `wx.showModal` 单按钮「知道了」，中止保存
> - `warn`  → `wx.showModal` 双按钮「仍然使用 / 改一下」，用户确认后才继续
>
> 之所以不把 `192.168.x.1` 也硬拦：用户完全可能给电脑配了静态 `.1` 结尾的地址，硬拦会误伤。
>
> 另有两处被后续检查抓出的问题一并修正：删重复代码块时误删了主题校验（已放回并移入
> `_saveAndConnect()`，这样"确认后继续"的路径也会走到它）；`describeHostRisk` 被调用但漏了
> `require` 解构（`node --check` 只验语法，查不出未导入的符号）。

### 验证方法

1. **启动器**：`dotnet build -c Release`（03-EMQX-Server）0 警告 0 错误；实际跑一遍，确认输出里
   `★` 落在 WLAN（`192.168.46.46`）上，其余 4 块虚拟网卡标注为"虚拟网卡, 手机连不上, 勿用"。
   已实测通过。
2. **网关判据**：`Get-NetIPConfiguration` 交叉确认 —— 5 块网卡里只有 WLAN 有默认网关
   （`192.168.46.240`），其余全空。判据成立。
3. **语法**：`node --check` 四个 js 文件（`app.js`、`utils/mqtt.js`、两页）全部通过。
4. **地址判定单测**（11 个用例，覆盖本机真实网卡、回环、空值、正常地址）：

   | 输入 | 期望 level | 实测 |
   | --- | --- | --- |
   | `192.168.40.1`（VMnet8） | warn | ✅ |
   | `192.168.198.1`（VMnet1） | warn | ✅ |
   | `172.20.144.1`（Hyper-V） | block | ✅ |
   | `100.86.134.42`（Tailscale） | block | ✅ |
   | `127.0.0.1` / `localhost` | block | ✅ |
   | 空串 | block | ✅ |
   | `192.168.46.46`（真实 WLAN） | 放行 | ✅ |
   | `192.168.1.100` / `192.168.46.240` / `10.0.0.5` | 放行 | ✅ |

   **11/11 通过。**
5. **端到端 MQTT over WebSocket 验证**（与小程序完全相同的接入路径：`ws://192.168.46.46:8083/mqtt`，
   手写 MQTT 3.1.1 报文 + WebSocket 握手）：

   ```
   A 旧行为(订阅 green/control): 收到 1 条 ["green/control=DUOJ-1"]
   B 修复后(订阅 green/data)   : 收到 0 条
   ✔ PASS: 复现了旧问题的成因, 并验证修复有效
   ```

   这一步同时证明了"模拟器为什么能连"—— 用电脑自己的 WLAN 地址连 8083 是通的，
   说明服务器、端口、EMQX 配置全都正常，唯一的变量就是**手机到不了那块虚拟网卡**。
6. **手机侧人工确认**：真机调试填 `192.168.46.46:8083` 应能连通；填 `192.168.40.1` 应弹
   「确认这个地址吗」。用手机浏览器访问 `http://192.168.46.46:18083` 能打开 EMQX Dashboard，
   即说明网段连通。


---

## 附：相关但未处理的观察

- `03-EMQX-Server/Program.cs` 的局域网地址列表只解决"列得对"，不解决"IP 会变"。真机调试建议把电脑 Wi-Fi 设为静态 IP，或每次从启动器输出里抄当前地址。
- `02-PC-UpperComputer/Form1.cs:384` 的连接地址默认值仍是 `"192.168.184.221:1883"`，该网段在本机不存在。属于同类问题（写死了一个不存在的地址），但用户尚未要求修改。
