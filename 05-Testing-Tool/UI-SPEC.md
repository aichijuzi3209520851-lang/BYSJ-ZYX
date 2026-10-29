# IotLinkLab UI 规范 v3（顶部工作条 + 可折叠日志）

> 本文件是调试台 UI 的唯一事实来源。**v2 已废止**（v2 的 168px 顶部固定区、
> 常驻 84px 连接卡、常驻日志规则不再适用）。所有尺寸均为 **96 DPI 逻辑像素**，
> 运行时经 `UiFit.S()` / `UiFit.Scale()` 换算到实际 DPI。

## 0. 目标与实测基准

- 设计基准：**1256 × 728**（当前 1280 × 800 / 150% 屏幕的逻辑可用区约 1241 × 691）。
- 最小窗口：**1080 × 640**（紧凑模式）。
- 兼容：1280 × 720 逻辑分辨率（1920 × 1200 @150%）。
- 实测：主工作区 ≥ **560**（日志收起）；顶部 + 状态栏 + 收起日志 ≤ **120**。
- 不改变协议、MQTT、串口、CSV、日志落盘逻辑；仅新增 UI 展示用的只读属性。

## 1. 纵向骨架

```
顶部工作条        56（紧凑 52）
连接设置抽屉      0（默认收起）/ 96（展开，含外边距）
主工作区          Percent 100
运行日志抽屉      34（收起，紧凑 32）/ 168（默认展开）
分隔条            8
全局状态栏        26（紧凑 24）
```

- 设置抽屉与展开日志**互斥**：打开设置自动收起日志；展开日志自动收起设置。
- 日志抽屉可拖拽；拖到 ≤46 自动收起，拖到 ≥64 自动展开；比例/展开状态写入
  `settings.json` 的 `logOpen` / `logRatio`（旧字段保持不变）。
- 收起态日志标题行显示最新一条日志；出现 `LogErr` 时标题前显示红点。
- 快捷键：`F1/F2/F3` 切页 / `F4` 发帧 / `F5/F6` 连接/断开 / `F7/F8` 日志/导出 /
  `F9` 连接设置 / `F10` 运行日志 / `F11` 最大化。

## 2. 横向骨架

| 页面 | 宽屏（≥1280） | 常规/窄屏（<1280） |
|---|---|---|
| 设备模拟器 | 42 / 58 | 40 / 60 |
| 控制台 | 44 / 56 | 42 / 58（<1120） |
| 监控台 | 单列（顶栏 + 60/40 上下分栏） | 同左，列表进入紧凑列宽 |

- 主窗体最小宽度 1080；任何断点不得出现水平滚动条或控件裁字。
- 监控台列宽按 ListView 客户区动态分配；紧凑模式隐藏“长度”列。
- 控制台命令区不再使用绝对宽度；6 个命令按钮为 2 列 `Percent 50` 等宽网格。

## 3. 设计令牌（Theme.cs）

```
Primary #15803D   PrimaryDark #0E592C   PrimaryTint #F0FDF4   PrimarySoft #DCFCE7
Teal #0D9488      Indigo #4F46E5       Danger #C8332C        Warning #B47909
TextPrimary #172A3A  TextSecondary #546878  TextTertiary #7A8C9A
PageBg #F4F7FA    CardBg #FCFDFC       CardStroke #E2E8EC    FieldBg #F8FAFB
Divider #EBF0F3
RadiusCard 12     RadiusButton 8      RadiusInput 8
HeightButton 32   HeightInput 32      HeightSmallButton 28
HeightTopBar 56   HeightTopBarCompact 52
HeightStatusBar 26  HeightStatusBarCompact 24
HeightLogCollapsed 34  HeightLogCollapsedCompact 32
HeightStateBar 52  HeightStateBarCompact 48
GapXs 4  GapSm 8  GapMd 12  GapLg 16  GapXl 20  Pad 16
```

字体只有 5 档：

| 用途 | 字体 |
|---|---|
| 应用标题 | `Theme.Title()` = 微软雅黑 14F Bold |
| 卡片/分组标题 | `Theme.Section()` = 10.5F Bold |
| 正文/控件 | `Theme.Body()` = 9.5F |
| 辅助说明 | `Theme.Meta()` = 8.5F |
| 数字/主题/日志 | `Theme.Mono(9F/9.5F)` |

## 4. 控件规范

### 4.1 FieldShell（圆角输入容器）

- `FieldShell(Control inner, bool multiline = false)`：圆角 8、`FieldBg` 底、
  1px `CardStroke`；焦点时 `Primary` 1.6px；悬停时 150 alpha 绿边。
- 内部 `TextBox/RichTextBox/ListView` 设 `BorderStyle=None`；`ComboBox` 设 `FlatStyle.Flat`；
  `NumericUpDown` 设 `BorderStyle=None`；单行控件由容器垂直居中，多行控件 `Dock=Fill`。
- **禁止**任何 UI 控件使用 `BorderStyle.FixedSingle`。

### 4.2 Card

- 圆角 12，`Region` 跟随圆角路径（四角不露方底）。
- 内边距 16（紧凑 12）；卡片间距 16；组内间距 8；分组间距 ≥8。

### 4.3 按钮 / 导航 / 徽章

- 主操作：`Primary` 填充；数据/上行：`Teal` 描边；下行：`Indigo` 描边；
  危险：`Danger`；中性动作：`TextSecondary` 描边。
- 导航胶囊：高 32，选中 `PrimaryTint` 底 + `Primary` 字 + 1.4px 描边。
- 状态徽章：8px 圆点 + 文本，高 26，底色为状态色 alpha 18。
- 图标：18 / 22 / 28 三种逻辑尺寸，全部 `UiFit.S()` 换算；`PictureBox.Zoom`，不拉伸。

## 5. 三页布局

### 5.1 设备模拟器

```
左卡 40%: 场景预设(下拉) → 传感器数值(2×3) → 固件 V1.2 帧模板(Fill)
右卡 60%: 发送控制 / 串口输出(并排) → 帧预览(固定 64/紧凑 56) → JSON 预览(Fill)
底部整宽: 设备状态卡 52（紧凑 48，根行 64/60）：天窗 / 风扇 / 模式 + 固件节奏
```

- 所有下拉/数字框宽度由 `UiFit.FitCombo` / `FitNumeric` 实测；`raw` 必须完整显示 `4095`。
- 帧预览与 JSON 预览保留“帧 → 解析字段 → JSON”同屏可见。
- 状态图标 22，数值图标 18；不硬凑缺失图标（土壤用文字）。

### 5.2 订阅监控台

```
订阅控制 Card 48: 主题 FieldShell + 添加订阅 + 暂停接收 + 清空记录 + 统计
消息流 Card 60%: 标题/上限 + ListView（行高 24，无横向滚动）
载荷详情 Card 40%: 标题 + 字符数 + 等宽只读全文
```

- 空列表中央显示“暂无消息 · 连接后自动订阅 green/data”。
- 选中消息后详情标题右侧显示 `${payload.Length} 字符`。
- 保留 400 条上限、`_pending` 200、暂停、`MessageSelected`、`SetTopics`。

### 5.3 控制台

```
左卡 44%: 控制主题 + QoS1 → 工作模式(2) → 天窗舵机(2) → 风扇电机(2) → 设备回显(Fill)
右卡 56%: 自定义报文(Fill) → 目标主题/QoS + 发送报文
```

- 命令按钮 `Dock=Fill`、高 40，文案第一段仍是命令字（`label.Split(' ')[0]`）。
- 设备回显由 `ControlTab.SetDeviceState(servo, motor, mode)` 刷新，仅展示。
- 长说明一律 ToolTip，不常驻版面。

## 6. 响应式与 DPI

- `UiFit.S(int)`：逻辑像素 → 物理像素；所有高度/边距/图标必须使用它。
- 项目启用 `ApplicationHighDpiMode=PerMonitorV2`；`Form1.AutoScaleMode=None`，避免二次缩放。
- 窗体初始尺寸：`min(S(1360), 工作区宽-S(24)) × min(S(880), 工作区高-S(24))`；
  最小尺寸 `S(1080) × S(640)`。
- 宽度断点：≥1280 完整导航；<1200 隐藏副标题；<1120 短导航 + 隐藏“?”帮助；
  监控台 <1200 紧凑列宽；控制台 <1120 改 42/58。
- 高度断点：<700 紧凑顶栏/状态栏；<560 紧凑卡片内边距/状态条/帧预览。
- 已知限制：运行中跨显示器改变 DPI 时不重建布局，重启后按新 DPI 正确。

## 7. 不可改契约

```csharp
SimulatorTab: ServoState / MotorState / ModeState / FrameProduced / Log /
              HandleControlCommand / Shutdown / SerialState(新增) / SerialStateChanged(新增)
MonitorTab:   SubscribeRequested / MessageSelected / Log / OnMessage / SetTopics / Topics
ControlTab:   PublishRequested / Log / SetDeviceState(新增)
```

## 8. 完成自查

- `grep -n "BorderStyle.FixedSingle"` → 0。
- `grep -n "Color.FromArgb"` → 仅 `Theme.cs` / `Controls.cs`。
- 布局 `Margin`/`Padding` 以 0/4/8/12/16/20/24 为主；6px 仅作标题与字段的光学微调。
- 字号只使用 5 档（8.5 / 9.5 / 10.5 / 14 与等宽 9/9.5）。
- 1256×728 与 1080×640 实拍无裁切/重叠；所有按钮文案完整显示。
- 日志收起/展开、设置抽屉开合、拖拽分隔条、F1–F10/F11 全部可用。

## 9. 使用指南（改 UI 的标准流程）

1. **先读规范**：本文件第 3 节取颜色/字号/圆角/间距，第 4 节取控件规则。
2. **只消费令牌**：颜色、字号、圆角、高度一律从 `Theme` 取；不得在页面里新增
   `Color.FromArgb` 或裸写高度/字号。
3. **宽度实测**：`ComboBox` 用 `UiFit.FitCombo`，`NumericUpDown` 用 `UiFit.FitNumeric`，
   按钮用 `UiFit.SizeButton`；不要写死 54/158/220 这类宽度。
4. **高度/边距/图标按 DPI**：统一 `UiFit.S()`；新增行高必须显式设置，不依赖控件默认值。
5. **输入控件统一外观**：TextBox/ComboBox/NumericUpDown/ListView 必须放进 `FieldShell`。
6. **三尺寸验收**：1256×728、1080×640、最大化各截一张；检查裁切、重叠、长文案、滚动条。
7. **记录变更**：修改布局或令牌时，同时更新本文件第 11 节与 `README.md` 版本记录。

## 10. 实现注意事项（WinForms 特有坑）

- `TableLayoutPanel` 的默认首选高度是 **100px**。把它 `Dock=Fill` 放进 `AutoSize` 行时，
  父行会按 100px 取高，第三行按钮/输入会被裁成一条色带。控制区必须显式行高：
  模拟器右侧控制区 148（内部 136）、控制台命令网格 40（父行 46）。
- `FieldShell` **不要设置 `Region`**：用透明背景 + 自绘圆角填充/描边，内部控件左右保留
  8px、上下保留 6px 内边距；否则圆角边框会被 Region 裁掉，只剩一条底边。
- 日志抽屉与连接设置抽屉**互斥**：打开其一时自动关闭另一个；日志展开高度不足时
  自动回到收起态，保证主工作区 ≥460。
- `settings.json` 新增 `logOpen`/`logRatio`；旧版文件缺少字段时回退默认值，不得抛异常。
- 跨显示器改变 DPI 时不重建布局，重启后按新 DPI 生效；属于已知限制。
- 监控台列宽按 ListView 客户区动态分配，禁止出现横向滚动条；紧凑模式隐藏“长度”列。

## 11. 版本变更记录

| 版本 | 日期 | 主要变更 |
| --- | --- | --- |
| v1 | 2026-10-29 | 初版三页签调试台：横幅 + 连接卡 + 模拟器/监控台/控制台 + 数据导出 |
| v2 | 2026-10-29 | 修复下拉/数字框裁字，收紧间距与日志区，补充 `UiFit` 实测宽度 |
| v3 | 2026-10-29 | 顶部工作条 + 连接设置抽屉 + 可折叠日志 + 全局状态栏；`FieldShell`；DPI 尺寸换算；`F9/F10` |
