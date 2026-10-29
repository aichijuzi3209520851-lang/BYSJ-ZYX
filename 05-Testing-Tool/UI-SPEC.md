# IotLinkLab UI 改造规范（v2）

本文件是 UI 重做的唯一事实来源。所有参与改造的人先读完再动手。

## 0. 用户原话诉求（不可偏离）

> 继续优化调试工具 UI：**下拉框字符显示错误**、**不要浪费空间给莫名其妙的文字**、
> **组件间距太小过于紧凑**、**布局要合理舒适**；可复用工程里微信小程序/PC 上位机的
> **图标资源且大小合适**；新增**数据导出功能**（类似日志导出），要能看到日志记录文件内容。

逐条翻译成验收标准：

| # | 用户原话 | 验收标准 |
|---|---|---|
| U1 | 下拉框字符显示错误 | 任何 ComboBox 的下拉文本与收起态文本都**不得被截断**，必须用 `UiFit.FitCombo()` 按最长项实测宽度 |
| U2 | 数字框显示 `208` 而非 `2080` | 任何 NumericUpDown 必须用 `UiFit.FitNumeric()` 按 `Maximum` 实测宽度 |
| U3 | 不要浪费空间给莫名其妙的文字 | 删掉冗长括注：`数据主题(上行发布)`→`上行主题`、`模式切换(任意时刻生效)`→`工作模式`、`天窗舵机(仅手动模式生效)`→`天窗舵机`、`串口输出(可选)`→`串口输出`。说明性长句不得常驻占版面（改为控件 ToolTip 或一行 8.5F 极简文字） |
| U4 | 组件间距太小过于紧凑 | 所有 `Margin` 不得小于 6；分组之间留白 ≥ 12；卡片内边距 ≥ 14。禁止出现 `Margin(0,2,0,0)` 这类贴脸间距 |
| U5 | 布局要合理舒适 | 页面**不得出现大片空白**（当前模拟器页下方约有 300px 死白）；内容按容器实际高度伸展或分组，纵向有节奏 |
| U6 | 复用工程图标资源且大小合适 | 用 `assets/icons/*.png`，显示尺寸 18~24px（列表/状态）或 28~32px（成组卡片头），保持等比不拉伸 |
| U7 | 新增数据导出 | 入口放主窗体日志卡头部：`查看日志` / `导出数据` / `清空`。对话框用现成 `ExportDialog` |

## 1. 不可修改的文件（Lead 已写好，直接调用）

- `Theme.cs` — 设计令牌
- `Controls.cs` — `RoundedButton` / `Card` / `StatusChip` / `NavChip`
- `UiFit.cs` — **测量工具，治 U1/U2 的唯一正解**
- `FrameCodec.cs` — 帧契约，业务核心
- `MqttLink.cs` — MQTT 封装
- `LogStore.cs` — `AppPaths` / `LogStore`
- `ExportDialog.cs` — 日志/数据查看导出对话框
- `DataStore.cs` — 数据行缓冲 + CSV 导出
- `Form1.cs` — 由 Lead 独占改写

**只允许改自己名下的 tab 文件。**

## 2. 设计令牌（Theme.cs，只用这些值，不要新增颜色）

```
Primary #15803D   PrimaryDark #0E592C   Teal #0D9488   Indigo #4F46E5
Success #15803D   Warning #B47909       Danger #C8332C
TextPrimary #172A3A  TextSecondary #546878  TextTertiary #7A8C9A
PageBg #F4F7FA   CardBg #FFFFFF  CardStroke #E2E8EC  FieldBg #F8FAFB  Divider #EBF0F3
RadiusCard 12   RadiusButton 8   HeightButton 32   HeightInput 30   Gap 8   Pad 16
Theme.Ui(size, bold)   微软雅黑
Theme.Mono(size)       Consolas（数字/主题/载荷用等宽）
```

## 3. UiFit 可用 API（必须用，别自己拍宽度）

```csharp
int  UiFit.Scale(Control c, int v);                 // DPI 换算
int  UiFit.TextWidth(string text, Font font);
int  UiFit.MaxItemWidth(ComboBox cb);
void UiFit.SizeButton(Control b, int padTotal = 40, int minWidth = 0);
void UiFit.FitCombo(ComboBox cb, int padTotal = 24);   // 按最长项自适应宽度
void UiFit.FitNumeric(NumericUpDown n, int padTotal = 12);
Label UiFit.FieldLabel(string text, float size = 9F);  // 表单标签
Label UiFit.SectionTitle(string text);                 // 分组标题
```

## 4. 图标资源（Lead 已备好）

`assets/icons/` 下 64×64 透明 PNG，运行时按需缩放：

| 文件 | 含义 | 建议使用位置 |
|---|---|---|
| `temp.png` | 温度 | 模拟器数值组 / 监控台 |
| `humi.png` | 湿度 | 模拟器数值组 / 监控台 |
| `light.png` | 光照 | 模拟器数值组 / 监控台 |
| `air-ok.png` / `air-bad.png` | 空气质量优/劣 | 模拟器状态条 |
| `win-open.png` / `win-closed.png` | 天窗开/关 | 模拟器状态条 |
| `fan-on.png` / `fan-off.png` | 风扇开/关 | 模拟器状态条 |

`assets/app-mark.png` 是 256×256 应用标识（品牌绿双向箭头），**Form1 banner 使用，tab 里不要用**。

图标加载建议写一个私有 helper，避免重复 `Image.FromFile`：

```csharp
private static Image? LoadIcon(string name)
{
    try
    {
        string p = Path.Combine(AppContext.BaseDirectory, "assets", "icons", name + ".png");
        if (!File.Exists(p)) return null;
        using var fs = new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var ms = new MemoryStream(); fs.CopyTo(ms); ms.Position = 0;
        return Image.FromStream(ms);           // 必须复制到内存, 避免锁文件
    }
    catch { return null; }
}
```

用途是 `PictureBox { SizeMode = Zoom, Size = new Size(22, 22), BackColor = Color.Transparent }`，
**不要**用 BackgroundImage 拉伸。

## 5. 公共 API 契约（**冻结，签名一个字都不能变**，否则 Form1 编译失败）

```csharp
public class SimulatorTab : UserControl
{
    public string ServoState { get; }   // 字段: "ON"/"OFF"
    public string MotorState { get; }   // "ON"/"OFF"
    public string ModeState  { get; }   // "AUTO"/"MAN"
    public event Action<string, FrameData, string>? FrameProduced;  // frame, parsed, json
    public event Action<string>? Log;
    public void HandleControlCommand(string cmd);
    public void Shutdown();
}

public class MonitorTab : UserControl
{
    public event Action<string>? SubscribeRequested;
    public event Action<string>? MessageSelected;
    public event Action<string>? Log;
    public void OnMessage(string topic, string payload);
    public void SetTopics(string[] topics);
    public string[] Topics { get; }
}

public class ControlTab : UserControl
{
    public event Action<string, string, bool>? PublishRequested;  // topic, payload, qos1
    public event Action<string>? Log;
}
```

`ServoState/MotorState/ModeState` 目前是公开**字段**（Form1 不读它们，但保持存在）。可以改成属性，但不能改名或删掉。

## 6. 各 Tab 具体要求

### 6.1 SimulatorTab（设备模拟器 · 上行）

现状问题：下方约 300px 死白；`MakeNum` 默认宽 54 导致 raw 显示 `208`；一堆 `Margin(0,2)` 贴脸；状态条挤。

目标结构（两列，左 42% / 右 58%，**纵向都要吃掉全部高度**）：

```
┌─ 左 Card ─────────────────────┐  ┌─ 右 Card ──────────────────────────┐
│ 场景预设                       │  │ 发送控制                           │
│  [场景下拉 —— 用 UiFit.FitCombo]│  │  周期 [1.5] 秒   [x] 数值抖动       │
│ 基础数值                       │  │  抖动幅度 ± [2]                     │
│  [icon]温度°C [24]  [icon]湿度[62]│ │  [手动发一帧] [定时发送]            │
│  [icon]土壤% [35]  [icon]光照[72]│ │                                    │
│  raw       [2080]              │  │ 串口输出                           │
│                                │  │  [x] 同步写串口                    │
│ 固件 V1.2 帧模板                │  │  COM6 ▾   9600 ▾                    │
│  [多行只读模板框]               │  │                                    │
│                                │  │ 帧预览 / JSON 预览（上下两块）      │
└────────────────────────────────┘  └────────────────────────────────────┘
              └──── 底部状态条：设备状态  天窗 180°   风扇 全速   模式 自动 ────┘
```

硬性要求：
1. 每个 NumericUpDown 调用 `UiFit.FitNumeric`，**raw 必须完整显示 `2080`**。
2. 数值标签前可放 18×18 图标（土壤没有图标，用纯文字，**不要**硬凑）。
3. 状态条用 `win-open/win-closed`、`fan-on/fan-off`、`air-ok/air-bad` 图标 + 文字，
   高度自适应，不要固定成一行高度把文字裁掉。
4. 所有分组标题用 `UiFit.SectionTitle`。
5. 两个按钮用 `UiFit.SizeButton` 自适应（文案会在「定时发送」↔「停止发送」之间切换，
   切换后必须重新 SizeButton）。
6. 帧模板框 / 预览框用 `Theme.Mono`，只读，`Dock=Fill` 吃掉剩余高度，**不要留死白**。
7. 保留全部现有行为：场景切换 `ApplyScene`、抖动、`BuildFrame`、`EmitFrame`、
   `HandleControlCommand`（`mode-0/mode-1` 任意时刻生效；`DUOJ-*`/`MOTOR-*` 仅 MAN）、
   `RefreshComPorts`、`WriteSerial`、定时器 1500ms、`Shutdown()`。

### 6.2 MonitorTab（订阅监控台）

现状问题：顶栏 Height=44 塞 6 个控件太挤；列表列宽写死（「载荷预览」560 在 1040 宽窗口下超出）。

目标结构：
```
┌ 顶部一行（AutoSize，Margin ≥ 8） ─────────────────────────────────────┐
│ 订阅主题 [green/data          ] [添加订阅] [清空记录] [暂停接收]  已接收 0 条 · 0/min │
├ 列表（Percent 60）────────────────────────────────────────────────┤
│ 时间 | 主题 | 长度 | 载荷预览                                        │
├ 详情 Card（Percent 40，Dock=Fill）──────────────────────────────────┤
│ [只读等宽载荷全文]                                                   │
└──────────────────────────────────────────────────────────────────┘
```

硬性要求：
1. 主题输入框宽度用 `UiFit.TextWidth("green/data", Theme.Mono(9.5F)) + 40` 之类的实测值，别写死 200。
2. 「载荷预览」列宽**必须按 ListView 实际客户区宽度动态分配**（`Resize` 里重算：
   时间 110 / 主题 `Math.Max(140, w*0.18)` / 长度 60 / 预览 = 剩余），不允许出现横向滚动条吃掉内容。
3. 按钮全部 `UiFit.SizeButton`。
4. 列表与详情之间 Margin ≥ 10。
5. 保留全部行为：暂停、上限 400 条、`_pending` 200、`UpdateStat`、`MessageSelected`、`SetTopics`、`Topics`。

### 6.3 ControlTab（控制台 · 下行）

现状问题：`MakeCmd` 宽度写死 158 导致「DUOJ-1 开窗180°」贴边；说明区长句占版面。

目标结构（左 46% / 右 54%）：
```
┌ 左 Card ──────────────────────────┐  ┌ 右 Card ────────────────────┐
│ 控制主题                          │  │ 自定义报文                    │
│  [green/control      ] [x] QoS1   │  │  [多行等宽输入, 吃掉全部高度]   │
│ 工作模式                          │  │                             │
│  [mode-0 自动] [mode-1 手动]      │  │                             │
│ 天窗舵机                          │  │                             │
│  [DUOJ-1 开窗180°] [DUOJ-0 关窗0°]│  │                             │
│ 风扇电机                          │  │                             │
│  [MOTOR-1 全速] [MOTOR-0 停止]    │  │  [发送报文]                  │
└───────────────────────────────────┘  └─────────────────────────────┘
```

硬性要求：
1. `MakeCmd` 宽度用 `UiFit.SizeButton(btn, padTotal: 34)` 按文案自适应，**不得贴边/截字**。
   ⚠️ 指令解析依赖 `label.Split(' ')[0]`，所以按钮文案里的**第一个空格之前必须是命令字**
   （`mode-0 自动` → `mode-0`）。可以改措辞，但这个前提不能破。
2. 三处括注删掉（见 U3），改为 `ToolTip`：「模式切换任意时刻生效，舵机/电机仅手动模式生效」。
3. 右边输入框 `Dock=Fill` 吃掉高度，底部按钮右对齐。
4. 保留 `PublishRequested` / `Log` 事件与 `Send()` 的空报文提示。

## 7. 通用禁令

- 不要 `dotnet build`（Lead 统一编译验证，避免并发锁 obj/bin）
- 不要改 csproj
- 不要新增 NuGet 包
- 不要引入新的顶层类文件（helper 写成 private static 方法放在自己的 tab 文件内）
- 不要用 `System.Drawing` 之外的新依赖
- 注释用中文，与现有代码风格一致
- 写完自查：`grep -n "Width = [0-9]" 自己的文件`，凡是给 ComboBox / NumericUpDown / Button
  写死宽度的，必须改成 UiFit 调用

## 8. 交付

各自改完，回报：
1. 改了哪个文件、行数变化
2. 每条硬性要求怎么落实的（逐条对应编号）
3. 自查 grep 结果
4. 有没有为了满足布局而改动任何行为逻辑（必须为「无」）
