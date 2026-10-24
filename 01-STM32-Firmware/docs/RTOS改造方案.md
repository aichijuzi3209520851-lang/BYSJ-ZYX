# STM32F103C8T6 固件 FreeRTOS 改造方案

> 版本：v1.0 · 日期：2026-10-25 · 状态：已批准执行
> 主线：main 分支（FreeRTOS）；备份：bare-metal-opt 分支（裸机优化版）

## 1. 可行性结论

**本系统可以使用 FreeRTOS，且资源余量宽裕。**

| 指标 | 改造前 | 加 FreeRTOS 后（预估） |
| --- | --- | --- |
| Flash | 11.3KB / 64KB（17.3%） | 约 17~21KB（26~33%） |
| RAM | 2.1KB / 20KB（10.2%，含栈堆） | 约 5~7KB（25~35%） |
| CPU 占空比 | 单周期最坏 ~180ms / 1.5s（~12%） | 进一步下降 |

**真实收益**：
- 串口命令响应：最坏 1.5 秒（命令每 1.5s 才被轮询一次）→ **毫秒级**（中断 + 队列事件驱动）
- 报警联动：轮询触发 → **即时事件触发**
- 架构：超级循环 → 4 任务 + 队列/互斥锁，模块边界清晰（毕业设计技术亮点）

**不变项**：DHT11 器件限 1Hz 采样、OLED 软件 I2C 刷屏速度——RTOS 提速的是"响应"，不是传感器本身。

## 2. 必须解决的三个硬冲突

| # | 冲突 | 解决 |
| --- | --- | --- |
| 1 | `System/Delay.c` 的 `Delay_us` 重编程 SysTick 并关停它，与 FreeRTOS 心跳冲突 | 重写为 **DWT->CYCCNT** 轮询方案，SysTick 完全让给内核 |
| 2 | `stm32f10x_it.c` 空的 `SysTick/PendSV/SVC_Handler` 与内核端口符号冲突 | 删除三个空函数，`FreeRTOSConfig.h` 宏映射接管 |
| 3 | USART1/3 中断优先级 3 高于 `configMAX_SYSCALL_INTERRUPT_PRIORITY=5`，ISR 不能调用 FromISR API | 优先级改为 **6** |

另修复现存隐患：工程 Define 缺 `STM32F10X_MD` 宏（重新编译直接报错）。

## 3. 硬约束（改造中不可破坏）

1. **串口输出协议逐字段不变**：`Temp:%dC   Humi:%d%%RH   Soil:%d%%(raw=%d)  Servo:xx Motor:xx  Air:nice[ Warning]\r\n` → 上位机/小程序零改动
2. **命令行为不变**：`mode-0/1` 任意时刻生效；`DUOJ-0/1`、`MOTOR-0/1` 仅手动模式；双串口共用 `Parse_Command_Frame`
3. **统一阈值不动**：`ALARM_TEMP_MAX=26 / ALARM_HUMI_MAX=85 / ALARM_SOIL_MAX=40`（三端一致）
4. 引脚分配与外设接口（TIM2 PWM、软件 I2C OLED、ADC PA0）不变

## 4. 任务架构（阶段 4 落地）

```
USART1/3 ISR (prio 6) ──帧──▶ xCmdQueue(8) ──▶ 通信任务 prio 2
                                                ├ 解析指令 / 模式切换 / 手动设备控制
                                                └ printf 输出(阻塞式,9600)

传感器任务 prio 3 (周期1.5s)
  ├ DHT11(vTaskSuspendAll 保护时序, 失败重试2次/间隔200ms)
  ├ 土壤ADC均值采样
  └ 烟雾双采样消抖(50ms, 非阻塞)
        │ xSensorQueue(4)
        ▼
报警控制任务 prio 4
  ├ 统一阈值判定 → LED/蜂鸣器/舵机/电机联动
  └ 更新 g_sys_state(持 xStateMutex)

显示任务 prio 1 (周期500ms)：读 g_sys_state → OLED 增量刷新
```

- 任务栈：传感器 512 字（含 printf）/ 报警 256 / 通信 384 / 显示 512；`configMINIMAL_STACK_SIZE=128`
- 互斥锁：`xStateMutex`（状态结构体）、`xActuatorMutex`（舵机/电机跨任务操作）
- 栈溢出钩子：全 LED 点亮 + 蜂鸣器长鸣指示

## 5. 关键技术决策与依据

| 决策 | 依据 |
| --- | --- |
| DHT11 用 `vTaskSuspendAll()` 而非关中断 | 整次读取 25~27ms 中仅 ~5ms 是时序敏感窗口；关中断 25ms 会丢 ~24 字节串口数据（9600 波特）。挂起调度器挡住任务切换，串口/SysTick 中断照常运行（每字节 ISR 仅 2~5µs，对 20~30µs 采样容限无威胁） |
| `Delay_us` 改 DWT | 与任务/中断完全解耦，两种版本（裸机/RTOS）通用 |
| printf 保持阻塞 | 每周期 ~77ms 阻塞仅发生在低优先级通信任务，传感器/报警任务照常抢占；9600 波特下无瓶颈 |
| 编译 -O0 → -O1 | RTOS 任务切换频繁需要时序余量；-O1 省 10~25% 代码，调试信息仍可读 |
| FreeRTOS V10.5.1 + RVDS/ARM_CM3 端口 + heap_4 | V10.x 生态成熟文档多；ARMCC v5 必须用 RVDS 端口；heap_4 支持碎片合并 |
| 工程宏 `USE_FREERTOS` 双态开关 | 同一套驱动源码同时服务裸机分支与 RTOS 主线 |

## 6. FreeRTOSConfig.h 关键参数

```c
#define configCPU_CLOCK_HZ               72000000
#define configTICK_RATE_HZ               1000        // 1ms 心跳
#define configMAX_PRIORITIES             7
#define configMINIMAL_STACK_SIZE         128
#define configTOTAL_HEAP_SIZE            (10 * 1024)
#define configMAX_SYSCALL_INTERRUPT_PRIORITY 5       // 0x50
#define configUSE_PREEMPTION             1
#define configUSE_TIME_SLICING           1
#define configUSE_MUTEXES                1
#define configCHECK_FOR_STACK_OVERFLOW   2
#define configPRIO_BITS                  4
// handler 宏映射(配合删除 it.c 空函数)
#define vPortSVCHandler     SVC_Handler
#define xPortPendSVHandler  PendSV_Handler
#define xPortSysTickHandler SysTick_Handler
```

## 7. 中断优先级规划（改造后）

| 中断 | 优先级 | 说明 |
| --- | --- | --- |
| SVCall / PendSV | 最低（内核固定） | 上下文切换 |
| SysTick | 最低（内核固定） | 系统心跳 |
| USART1 / USART3 | 6 | ≥ MAX_SYSCALL(5)，可安全调用 FromISR |
| （无其他中断） | — | TIM2 纯 PWM 无中断、ADC 轮询 |

## 8. 阶段划分与实时交付

| 阶段 | 内容 | 交付 |
| --- | --- | --- |
| 0 | 补 STM32F10X_MD 宏、-O1、方案文档、基线编译 | git 提交推送 |
| 1 | 裸机优化备份版（bare-metal-opt 分支）：SysTick 1ms 时基、分时隙主循环、非阻塞消抖 | 分支提交推送 |
| 2 | FreeRTOS 内核引入 + 工程配置 + FreeRTOSConfig.h | 提交推送 |
| 3 | 底层改造：Delay DWT / it.c 删空 handler / 串口优先级+队列 / DHT11 挂起调度 / NVIC 分组 | 提交推送 |
| 4 | main.c 任务化重构（4 任务 + 队列 + 互斥锁） | 提交推送 |
| 5 | UV4 CLI 编译验证 + 前后资源对比表 + 静态自查 | 提交推送 |
| 6 | README 重写 + tag v2.0-rtos / v1.1-baremetal + 同步 BYSJ-ZYX | 推送 GitHub |

## 9. 风险与回退

| 风险 | 缓解 |
| --- | --- |
| DHT11 在 RTOS 下偶发校验失败 | 已配挂起调度器 + 2 次重试；仍频繁失败可提升传感器任务至最高优先级（预留） |
| FreeRTOS 源码下载失败 | 依次：代理直下 → gh api → 手动放置（附目录结构说明） |
| 裸机分支与主线分叉 | 裸机分支仅做时序优化，不再演进，仅作回退保险 |
| Keil CLI 编译异常 | 代码按 ARMCC5 语法编写，注明"未经编译器验证"部分由用户 Rebuild 确认 |

## 10. 真机验收清单（交付后由用户执行）

1. 上电：OLED 四行标签正常显示，舵机归 0°
2. 串口：1.5s 周期输出，文本与旧版**逐字节一致**（上位机可直接解析）
3. 命令响应：连接上位机发送 `mode-1` + `DUOJ-1`，舵机应在 **<100ms** 内动作（旧版最坏 1.5s+）
4. 报警联动：手捂 DHT11 升温 >26°C → 黄灯亮 + 舵机开窗；对烟雾传感器哈气 → 红灯 + 电机全速
5. 稳定性：连续运行 ≥30 分钟无死机、无串口乱码、无 OLED 花屏
