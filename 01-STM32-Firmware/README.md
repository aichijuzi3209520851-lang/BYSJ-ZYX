# STM32F103C8T6 智能温室大棚监控系统（下位机固件）

![MCU](https://img.shields.io/badge/MCU-STM32F103C8T6-blue.svg)
![Keil](https://img.shields.io/badge/IDE-Keil%20MDK5-red.svg)
![FreeRTOS](https://img.shields.io/badge/FreeRTOS-V10.5.1-ff6600.svg)
![License](https://img.shields.io/badge/License-MIT-green.svg)

> 本仓库是"智能温室大棚物联网系统"的**下位机固件**部分。
> 系统总览、PC 上位机、EMQX 服务器与微信小程序请见仓库根目录 README。

基于 STM32F103C8T6（ARM Cortex-M3 @72MHz）+ 标准外设库 + **FreeRTOS 实时操作系统**的环境监测与报警固件：
1.5 秒周期采集 **DHT11 温湿度 / D2-5-2K 电容式土壤湿度 / 烟雾传感器**，
OLED 实时显示，多 LED + 蜂鸣器声光报警，自动模式下报警联动**舵机（天窗）**与**电机（风扇）**；
支持 USART1 / USART3 双串口指令手动控制，两路串口共用同一指令解析函数，行为完全一致。

## 软件架构（FreeRTOS 任务化，V2.0）

```
USART1/3 ISR(prio6) ──帧──▶ xCmdQueue ──▶ 通信任务(prio2): 指令解析/协议打印
传感器任务(prio3, 1.5s): DHT11(挂起调度保护时序)+土壤ADC均值+烟雾消抖
      │ xSensorQueue(最新快照)
      ▼
报警控制任务(prio4): 统一阈值判定 → LED/蜂鸣器/舵机/电机联动 → 更新系统状态
      │ xReportQueue
      ▼
显示任务(prio1, 500ms): OLED 增量刷新（标签初始化时绘制）
```

| 任务 | 优先级 | 栈(字) | 职责 |
| --- | --- | --- | --- |
| alarm | 4 | 256 | 阈值判定 + 声光报警 + 执行器联动 |
| sensor | 3 | 512 | DHT11/土壤 ADC(8次均值)/烟雾双采样消抖 |
| comm | 2 | 384 | 指令执行（毫秒级响应）+ 协议 printf |
| oled | 1 | 512 | OLED 周期刷新 |

**关键设计**（详见 [docs/RTOS改造方案.md](./docs/RTOS改造方案.md)）：
- `Delay_us` 改用 **DWT 周期计数器**，SysTick 完全让给 FreeRTOS 心跳
- DHT11 读取（约 25ms µs 级时序）用 `vTaskSuspendAll()` 挂起调度器保护——串口中断照常运行，不丢数据
- 串口 ISR 帧完成即 `xQueueSendFromISR` 直投通信任务，**命令响应从旧版最坏 1.5s 降到毫秒级**
- 中断优先级规划：USART=6（可调用 FromISR），`configMAX_SYSCALL_INTERRUPT_PRIORITY=5`，NVIC 分组 4
- 栈溢出钩子：全 LED 点亮 + 蜂鸣器长鸣指示
- 串口协议文本与 V1.x **逐字节一致**，上位机/小程序零改动

**资源占用**（-O1，实测 0 Error / 0 Warning）：Flash 17.4KB/64KB（27.2%），RAM 12.4KB/20KB（62.1%，含 10KB 内核堆）。

**分支说明**：
- `main`：FreeRTOS 任务化版本（主线，tag `v2.0-rtos`）
- `bare-metal-opt`：裸机优化备份版（SysTick 时基 + 分时隙调度，无 RTOS 依赖，tag `v1.1-baremetal`）

## 硬件引脚分配


| 外设 | 引脚 | 说明 |
| --- | --- | --- |
| DHT11 温湿度 | PA15 | 单总线 |
| 土壤湿度 D2-5-2K AO | PA0 | ADC1_IN0，模拟量（读数与湿度成反比） |
| 光敏传感器 AO | PB1 | ADC1_IN9，模拟量（仅采集光照，不联动蜂鸣器） |
| 烟雾传感器 DOUT | PA6 | 数字量，低电平=污染 |
| 红灯 LED（烟雾报警） | PB10 | 低电平点亮（占用后 USART3 仅可接收） |
| 黄灯 LED（温湿度报警） | PB0 | 低电平点亮 |
| 蓝灯 LED（土壤过湿报警） | PA3 | 低电平点亮 |
| 浓烟报警 LED | PB4 | 低电平点亮 |
| 蜂鸣器 | PA7 | 低电平导通 |
| 舵机（天窗） | PA1 | TIM2_CH2 PWM，0.5~2.5ms → 0~180° |
| 电机（风扇） | PA2 + PA4/PA5 | TIM2_CH3 PWM 调速 + 方向 IO |
| USART1 | PA9(TX) / PA10(RX) | 9600 8N1，printf 调试输出 + 指令 |
| USART3 | PB11(RX) | 9600 8N1，指令接收（PB10 已被红灯占用） |
| OLED (SSD1306) | PB8(SCL) / PB9(SDA) | 软件 I2C 开漏；VCC/GND 可接 PB7/PB6（固件 GPIO 供电）或电源排针（推荐） |
| 白色 LED（光照联动预留） | PB12 | 预留脚，暂未启用；后续光弱亮灯联动用 |

## 统一报警阈值

固件通过宏定义阈值，并与 PC 上位机、微信小程序保持**三端一致**（固件为权威判定源）：

```c
#define ALARM_TEMP_MAX   26   // 温度报警上限(°C)
#define ALARM_HUMI_MAX   85   // 空气湿度报警上限(%RH)
#define ALARM_SOIL_MAX   40   // 土壤湿度报警上限(%, 过湿)
```

| 条件 | 声光提示 | 自动模式联动 |
| --- | --- | --- |
| 温度 > 26°C 或 湿度 > 85%RH | 黄灯 + 蜂鸣器 | 舵机开窗 180° |
| 土壤湿度 > 40%（过湿） | 蓝灯 + 蜂鸣器 | — |
| 空气质量 = bad | 红灯 + 浓烟灯 + 蜂鸣器 | 电机全速排风 |

## 串口指令集（帧以 `\r\n` 结尾）

USART1 与 USART3 收到指令后统一交给 `Parse_Command_Frame()` 解析：

| 指令 | 作用 | 生效条件 |
| --- | --- | --- |
| `mode-0` / `mode-1` | 切换自动 / 手动模式 | 任意模式 |
| `DUOJ-0` / `DUOJ-1` | 天窗舵机关(0°) / 开(180°) | 仅手动模式 |
| `MOTOR-0` / `MOTOR-1` | 风扇停 / 全速 | 仅手动模式 |

## 串口数据输出（每 1.5s 一帧）

```
Temp:26C   Humi:60%RH   Soil:35%(raw=1200)  Light:72%(raw=2200)  Servo:OFF Motor:OFF  Air:nice
```

- `Soil:` 土壤湿度百分比（`raw` 为 ADC 原始值，便于两点校准）
- `Light:` 光照强度百分比（`raw` 为 ADC 原始值；校准见 System/Light.h，仅采集不联动报警）
- 行尾出现 `Warning` 表示固件判定处于报警状态
- PC 上位机按此格式解析并通过 MQTT 发布（详见根目录 README）

## 土壤湿度校准

D2-5-2K 为电容式传感器，ADC 读数与湿度**成反比**。首次使用需两点校准
（修改 `System/Water.h`）：

```c
#define SOIL_AIR_ADC      2080    /* 干燥(空气中)ADC读数, 按实测修改 */
#define SOIL_WATER_ADC    1040    /* 100%湿度(水中)ADC读数, 按实测修改 */
```

串口输出的 `raw=` 值即可用于读取实测值。模块资料见 `D2-5-2K/` 目录。

## 工程结构与编译

```
├── Project.uvprojx     # Keil MDK5 工程文件
├── User/               # main.c(主逻辑) + 中断/配置
├── System/             # 外设驱动: DHT11/Water/OLED/Motor/Servo/PWM/usart/Delay/sys
├── Start/ Library/     # CMSIS 内核与 STM32F10x 标准外设库
├── D2-5-2K/            # 土壤湿度传感器模块资料(说明书/原理图)
└── keilkill.bat        # 清理编译产物脚本
```

使用 **Keil MDK5（ARMCC v5）+ STM32F10x Pack** 打开 `Project.uvprojx` 直接编译（Objects/Listings 为本地编译产物，不入库），通过 ST-Link 下载到开发板即可。

## License

MIT
