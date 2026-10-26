/**
  ******************************************************************************
  * @file    User/main.c
  * @brief   智能温室环境监测系统主程序(裸机分时隙版)
  * @author  LCW
  * @version V1.2
  * @date    2026-10-25
  * @note    ┌── 系统架构(裸机分时隙主循环) ─────────────────────────────┐
  *          │ 上电: 外设初始化(串口/传感器/OLED/执行器)                  │
  *          │   └→ while(1) 主循环, 周期 = 采集+显示+打印耗时 + 1.5s延时  │
  *          │       1.DHT11温湿度读取(带重试)   2.土壤ADC + 光照ADC       │
  *          │       3.烟雾消抖判定              4.OLED数值区增量刷新      │
  *          │       5.串口指令解析(USART1/3)    6.报警判定→LED/蜂鸣/联动  │
  *          │       7.协议帧printf输出(1.5s/帧) 8.OLED显示保活            │
  *          │ 中断: USART1/3 收指令帧→主循环消费(见 System/usart.c)       │
  *          └───────────────────────────────────────────────────────────┘
  *
  *          RTOS 版本(4任务+队列+互斥锁)完整保留于 rtos-backup 分支,
  *          本裸机版为交接基线: 结构简单、便于答辩演示与现场排障。
  *
  *          ───────────────── 硬件接线总表 ─────────────────
  *          外设               引脚        说明
  *          DHT11温湿度        PA15        单总线
  *          土壤湿度AO         PA0         ADC1_IN0, 越干数值越大
  *          光敏AO             PB1         ADC1_IN9, 仅采集不联动
  *          烟雾DOUT           PA6         低电平=污染
  *          红灯LED(烟雾)      PB10        低电平点亮[占用USART3_TX]
  *          黄灯LED(温湿度)    PB0         低电平点亮
  *          蓝灯LED(过湿)      PA3         低电平点亮
  *          浓烟LED            PB4         低电平点亮
  *          蜂鸣器             PA7         低电平导通
  *          舵机(天窗)         PA1         TIM2_CH2 PWM
  *          电机(风扇)         PA2+PA4/PA5 TIM2_CH3 PWM + 方向IO
  *          OLED               PB8/PB9     软件I2C; VCC/GND接PB7/PB6
  *                                         (固件GPIO供电)或电源排针
  *          USART1             PA9/PA10    上位机协议/指令 9600 8N1
  *          USART3             PB11(RX)    备用指令入口 9600 8N1
  ******************************************************************************
  * @history
  *   V1.0  2026-09-20  注释规范化; 水位传感器替换为D2-5-2K土壤湿度模块
  *   V1.1  2026-10-25  报警阈值宏化统一(三端一致); USART1/USART3指令解析
  *                     合并为共用函数Parse_Command_Frame, DUOJ-1统一开窗180°
  *   V1.2  2026-10-25  交接级注释升级(纯注释零代码变更); 新增光照采集展示;
  *                     OLED显示整改(失败占位"--"/残留修复/行3布局预留光照位);
  *                     OLED显示保活(防供电瞬断灭屏)
  ******************************************************************************
  */

/* ==================== 头文件 ==================== */
#include "sys.h"
#include "Delay.h"    // 裸机延时(SysTick轮询式, 无需初始化, 见System/Delay.c)
#include "DHT11.h"    // 温湿度传感器(单总线时序, PA15)
#include "usart.h"    // 串口驱动(printf重定向/接收帧/指令标志位)
#include "OLED.h"     // OLED驱动(软件I2C, PB8/PB9, 含GPIO供电)
#include "Water.h"    // 土壤湿度驱动(D2-5-2K, ADC1_IN0, 提供通用ADC1_GetValue)
#include "Light.h"    // 光敏驱动(ADC1_IN9, 仅采集不联动蜂鸣器)
#include "Motor.h"    // 电机驱动(TB6612/DRV8833类: 方向IO+PWM调速)
#include "Servo.h"    // 舵机驱动(SG90, TIM2_CH2 PWM, 0.5~2.5ms→0~180°)
#include <string.h>   // strcpy/strcmp(air_status与指令比对)

/* ==================== 硬件引脚定义(位带别名) ====================
 * PAin/PBout等宏来自sys.h的位带映射, 等价于GPIO读改写但为单指令原子操作 */
#define SMOKE_DOUT PAin(6)        // 烟雾传感器数字输出, 低电平=污染
#define RED_LED PBout(10)         // 红灯:烟雾报警。注意PB10=USART3_TX,
                                  // 配置为GPIO后串口3仅剩接收(设计取舍:
                                  // 原PA1与舵机TIM2_CH2冲突, 故移至PB10)
#define YELLOW_LED PBout(0)       // 黄灯:温湿度报警
#define BLUE_LED PAout(3)         // 蓝灯:土壤过湿报警
#define SMOKE_ALARM_LED PBout(4)  // 浓烟指示灯:与红灯同源(烟雾), 双灯增强可见性
#define BEEP PAout(7)             // 蜂鸣器:低电平导通(任一报警即响)

/* ==================== 统一报警阈值(三端契约) ====================
 * STM32固件 / PC上位机 / 微信小程序使用同一组数值, 固件为权威判定源:
 * 固件本地毫秒级联动执行器, 上位机/小程序仅做展示与远程手动控制。
 * 修改任一阈值必须三端同步, 否则三端报警状态不一致! */
#define ALARM_TEMP_MAX   26   // 温度报警上限(°C), 依据: 温室作物适宜区间
#define ALARM_HUMI_MAX   85   // 空气湿度报警上限(%RH)
#define ALARM_SOIL_MAX   40   // 土壤湿度报警上限(%, 超过视为过湿)

/* ==================== 全局状态变量 ====================
 * 裸机单线程: 主循环顺序读写, 无并发; 例外是usart.c中断侧只写
 * USARTx_RX_BUF/RX_STA(见该文件), 本文件仅在主循环消费, 无竞态 */
u8 temperature;                 // 温度(°C), DHT11写入, OLED/报警读取
u8 humidity;                    // 空气湿度(%RH), DHT11写入, OLED/报警读取
uint16_t water_level;           // 土壤湿度原始ADC值(0~4095, 越干越大,
                                // 经串口输出raw便于两点法校准)
uint8_t water_percent;          // 土壤湿度百分比(0~100, 越大越湿)
char air_status[10] = "nice";   // 空气质量状态, 仅两值: "nice"/"bad"
                                // (串口协议契约: 上位机正则按此两值匹配)
uint8_t servo_angle = 0;        // 舵机当前角度状态(0=关窗/180=开窗),
                                // 与Servo_SetAngle成对更新, 供协议输出
uint8_t manual_mode = 0;        // 控制模式: 0=自动(报警联动) 1=手动(串口指令)
uint8_t motor_status = 0;       // 电机状态: 0=停止 1=运行, 供协议输出
uint16_t light_raw;             // 光照强度原始ADC值(0~4095, 方向见Light.h)
uint8_t light_percent;          // 光照强度百分比(0~100, 越大越亮)

/**
  * @brief  初始化所有报警相关硬件(传感器输入/报警LED/蜂鸣器)
  * @param  无
  * @retval 无
  * @note   执行耗时: <1ms(纯GPIO寄存器配置)
  *         LED全部低电平点亮(灌电流接法, 单片机IO灌电流能力更强),
  *         初始状态: 全灭(写1), 蜂鸣器关闭(写0)
  *         注意: PB10在此被配为普通推挽输出, 覆盖uart3_init中的
  *         USART3_TX复用配置(调用顺序: uart3_init先于本函数), 即
  *         串口3设计上仅接收不发送
  */
void Alarm_Hardware_Init(void)
{
    GPIO_InitTypeDef GPIO_InitStructure;

    RCC_APB2PeriphClockCmd(RCC_APB2Periph_GPIOA, ENABLE);  // 使能PA端口时钟
    RCC_APB2PeriphClockCmd(RCC_APB2Periph_GPIOB, ENABLE);  // 使能PB端口时钟

    // 烟雾传感器(PA6): 模块DOUT为OC/OD输出型, 上拉输入保证无烟雾时稳定读高
    GPIO_InitStructure.GPIO_Pin = GPIO_Pin_6;
    GPIO_InitStructure.GPIO_Mode = GPIO_Mode_IPU;  // 上拉输入
    GPIO_InitStructure.GPIO_Speed = GPIO_Speed_50MHz;
    GPIO_Init(GPIOA, &GPIO_InitStructure);

    // 红灯LED(PB10)
    GPIO_InitStructure.GPIO_Pin = GPIO_Pin_10;
    GPIO_InitStructure.GPIO_Mode = GPIO_Mode_Out_PP;  // 推挽输出
    GPIO_Init(GPIOB, &GPIO_InitStructure);

    // 黄灯LED(PB0)
    GPIO_InitStructure.GPIO_Pin = GPIO_Pin_0;
    GPIO_InitStructure.GPIO_Mode = GPIO_Mode_Out_PP;
    GPIO_Init(GPIOB, &GPIO_InitStructure);

    // 浓烟指示LED(PB4)
    GPIO_InitStructure.GPIO_Pin = GPIO_Pin_4;
    GPIO_InitStructure.GPIO_Mode = GPIO_Mode_Out_PP;
    GPIO_Init(GPIOB, &GPIO_InitStructure);

    // 蓝灯LED(PA3)
    GPIO_InitStructure.GPIO_Pin = GPIO_Pin_3;
    GPIO_InitStructure.GPIO_Mode = GPIO_Mode_Out_PP;
    GPIO_Init(GPIOA, &GPIO_InitStructure);

    // 蜂鸣器(PA7): 有源蜂鸣器, 低电平导通
    GPIO_InitStructure.GPIO_Pin = GPIO_Pin_7;
    GPIO_InitStructure.GPIO_Mode = GPIO_Mode_Out_PP;
    GPIO_Init(GPIOA, &GPIO_InitStructure);

    // 初始状态: LED全灭(低电平点亮型写1), 蜂鸣器关(写0)
    RED_LED = 1;
    YELLOW_LED = 1;
    BLUE_LED = 1;
    SMOKE_ALARM_LED = 1;
    BEEP = 0;
}

/**
  * @brief  检测空气质量状态(烟雾传感器, 结果写入全局air_status)
  * @param  无
  * @retval 无
  * @note   判定: PA6低电平且50ms后仍为低 → "bad"(烟雾污染), 否则"nice"
  *         50ms消抖依据: 烟雾模块DOUT在浓度临界点会抖动, 双采样+延时
  *         过滤毛刺; 50ms阻塞仅发生在主循环, 无中断时序依赖, 可接受
  *         协议契约: air_status只允许"nice"/"bad"两值(上位机正则匹配)
  */
void Check_Air_Quality(void)
{
    if(SMOKE_DOUT == 0)            // 首次采样为低(疑似污染)
    {
        Delay_ms(50);              // 延时消抖
        if(SMOKE_DOUT == 0)        // 50ms后仍为低 → 确认污染
        {
            strcpy(air_status, "bad");
        }
    }
    else
    {
        strcpy(air_status, "nice");
    }
}

/**
  * @brief  解析一条完整的串口指令帧(USART1/USART3共用同一入口)
  * @param  buf: 指令帧缓冲区(不含帧尾\r\n)
  * @param  len: 指令长度(字节)
  * @retval 无
  * @note   指令集与生效条件(与小程序执行器页/上位机控制面板一致):
  *         ┌──────────┬──────────────────────┬────────────┐
  *         │ mode-0/1 │ 自动/手动模式        │ 任意时刻   │
  *         │ DUOJ-0/1 │ 天窗关0°/开180°      │ 仅手动模式 │
  *         │ MOTOR-0/1│ 风扇停/全速          │ 仅手动模式 │
  *         └──────────┴──────────────────────┴────────────┘
  *         DUOJ-1开窗角度定为180°与自动模式联动角度一致, 保证任意
  *         路径开窗后的天窗姿态唯一; 设备指令仅手动模式生效是为避免
  *         与报警联动互相覆盖(自动模式下执行器归报警逻辑管辖)
  *         执行耗时: 命中指令时含PWM设置 <0.1ms, 无阻塞
  */
static void Parse_Command_Frame(const char *buf, u16 len)
{
    // 模式切换指令: 任意时刻生效(不判manual_mode)
    if(len >= 6 && strncmp(buf, "mode-0", 6) == 0)
    {
        manual_mode = 0;  // 切换到自动模式
    }
    else if(len >= 6 && strncmp(buf, "mode-1", 6) == 0)
    {
        manual_mode = 1;  // 切换到手动模式
    }
    // 设备控制指令: 仅手动模式生效(自动模式由Handle_Alarms联动管辖)
    else if(manual_mode)
    {
        // 舵机指令: DUOJ-1开窗角度与自动模式联动一致(180°)
        if(len >= 6 && strncmp(buf, "DUOJ-0", 6) == 0)
        {
            Servo_SetAngle(0);     // 关窗(0°)
            servo_angle = 0;
        }
        else if(len >= 6 && strncmp(buf, "DUOJ-1", 6) == 0)
        {
            Servo_SetAngle(180);   // 开窗(180°)
            servo_angle = 180;
        }
        // 电机指令
        else if(len >= 7 && strncmp(buf, "MOTOR-0", 7) == 0)
        {
            Motor_SetSpeed(0);     // 停止电机
            motor_status = 0;
        }
        else if(len >= 7 && strncmp(buf, "MOTOR-1", 7) == 0)
        {
            Motor_SetSpeed(100);   // 电机全速
            motor_status = 1;
        }
    }
}

/**
  * @brief  消费USART1/USART3接收完成帧并解析指令(主循环每轮调用)
  * @param  无
  * @retval 无
  * @note   数据流: USARTx ISR组帧(见usart.c) → 置bit15完成标志 →
  *         本函数读走并清标志。读清顺序固定(先取长再清标志)保证
  *         帧内容在清标志前已完整拷入解析函数
  *         执行耗时: 无帧时近乎0; 有帧时<0.1ms
  */
void Process_Serial_Command(void)
{
    // USART1: bit15=1表示收到一帧完整指令(0x0D 0x0A结尾)
    if(USART_RX_STA & 0x8000)
    {
        u16 len = USART_RX_STA & 0x3FFF;  // bit13~0 = 有效字节数
        Parse_Command_Frame((const char*)USART_RX_BUF, len);
        USART_RX_STA = 0;  // 清除接收标志(允许ISR开始下一帧组包)
    }

    // USART3: 与USART1共用同一解析入口, 保证两条串口指令行为一致
    if(USART3_RX_STA & 0x8000)
    {
        u16 len = USART3_RX_STA & 0x3FFF;
        Parse_Command_Frame((const char*)USART3_RX_BUF, len);
        USART3_RX_STA = 0;
    }
}

/**
  * @brief  报警判定与声光输出(主循环每轮调用)
  * @param  无
  * @retval uint8_t: 1=温湿度报警触发(供协议帧附加Warning), 0=未触发
  * @note   统一阈值判定(与上位机/小程序三端一致):
  *         温度>26°C 或 湿度>85%RH → 黄灯 | 土壤>40%(过湿) → 蓝灯
  *         空气=bad → 红灯+浓烟灯 | 任一报警 → 蜂鸣器
  *         自动模式(manual_mode=0)下联动执行器:
  *           烟雾bad → 电机全速排风; 温湿度报警 → 舵机开窗180°
  *         手动模式下只控声光, 不碰执行器(执行器归串口指令管辖)
  *         执行耗时: <0.1ms(纯GPIO+比较); 每轮都全量刷新LED状态,
  *         无需消抖(报警为稳态判定, 抖动由传感器侧消抖兜底)
  */
uint8_t Handle_Alarms(void)
{
    // 1. 温湿度报警(任一越限即触发)
    uint8_t temp_humi_alarm = (temperature > ALARM_TEMP_MAX || humidity > ALARM_HUMI_MAX) ? 1 : 0;

    // 2. 土壤过湿报警(湿度>ALARM_SOIL_MAX; 若想改为"过干报警"把 > 改成 <)
    uint8_t water_alarm = (water_percent > ALARM_SOIL_MAX) ? 1 : 0;

    // 3. 烟雾报警(空气质量为bad)
    uint8_t smoke_alarm = (strcmp(air_status, "bad") == 0) ? 1 : 0;

    // 声光输出: LED低电平点亮(写0亮), 蜂鸣器低电平导通
    YELLOW_LED = temp_humi_alarm ? 0 : 1;
    BLUE_LED = water_alarm ? 0 : 1;
    RED_LED = smoke_alarm ? 0 : 1;
    SMOKE_ALARM_LED = smoke_alarm ? 0 : 1;  // 浓烟灯与红灯同源, 双灯增强可见性

    // 蜂鸣器: 任一报警即触发(经验值: 三类报警同响便于现场快速识别)
    BEEP = (temp_humi_alarm || water_alarm || smoke_alarm) ? 0 : 1;

    // 执行器联动: 仅自动模式(手动模式下执行器归串口指令管辖,
    // 本函数不碰舵机/电机, 避免两个来源互相覆盖状态)
    if (!manual_mode)
    {
        // 电机联动: 烟雾报警 → 全速排风; 解除 → 停止
        if (smoke_alarm)
        {
            Motor_SetSpeed(100);  // 全速正转
            motor_status = 1;
        }
        else
        {
            Motor_SetSpeed(0);    // 停止
            motor_status = 0;
        }

        // 舵机联动: 温湿度报警 → 开窗180°通风; 解除 → 关窗
        if (temp_humi_alarm)
        {
            Servo_SetAngle(180);
            servo_angle = 180;
        }
        else
        {
            Servo_SetAngle(0);
            servo_angle = 0;
        }
    }

    // 返回温湿度报警状态(供协议帧决定是否附加Warning字样)
    return temp_humi_alarm;
}

/**
  * @brief  程序主入口: 外设初始化 → OLED静态标签 → 1.5s周期主循环
  * @param  无
  * @retval 无(永不返回)
  * @note   初始化顺序约束:
  *         1. uart_init/uart3_init在最前(printf可在任何初始化后使用)
  *         2. OLED_Init含OLED_Power_Init(PB7/PB6给屏供电), 其内部
  *            上电延时~100ms等待SSD1306就绪——延时时长对编译优化等级
  *            敏感的历史教训见System/OLED.c文件头
  *         3. Light_Init必须在ADC1_Init之后(ADC1外设由后者使能校准)
  *         4. Alarm_Hardware_Init必须在uart3_init之后(覆盖PB10为GPIO)
  *
  *         主循环单轮耗时预算(72MHz实测估算):
  *         DHT11读取(成功)~25ms | 土壤+光照ADC各~1ms | 烟雾消抖50ms |
  *         OLED增量刷新~10ms | printf协议帧~60字符@9600≈60ms |
  *         合计约150ms + Delay_ms(1500) → 实际周期约1.65s
  *
  *         OLED布局(4行×16列, 8x16字库):
  *         ┌────────────────┐
  *         │Temp:NN C       │  数值列6~7, 单位C列8
  *         │Humi:NN %RH     │  数值列6~7, 单位列8~10
  *         │Soil:NNN% L:NNN%│  土壤6~8/%9, 光照标签11~12/数值13~15/%16
  *         │Air:nice        │  状态列5~8(MQ-2判定, 仅nice/bad两值)
  *         └────────────────┘
  *
  *         注意: DHT11读取失败只影响第1/2行数值(显示"--"), 不影响第4行。
  *         第4行Air完全由MQ-2烟雾传感器(PA6)决定, 与DHT11无关。
  */
int main(void)
{
    uint8_t temp_humi_alarm_status;  // 温湿度报警状态(供协议帧附加Warning)
    uint8_t dht11_ok;                // DHT11本轮读取成功标志(第1/2行显示"--"用)

    uart_init(9600);     // 串口1: 上位机协议输出+指令接收(PA9/PA10)
    uart3_init(9600);    // 串口3: 备用指令入口(PB11 RX)
    DHT11_Init();        // DHT11单总线引脚(PA15)
    OLED_Init();         // OLED(含PB7/PB6供电初始化+上电延时+I2C配置)
    ADC1_Init();         // ADC1外设使能与校准(PA0土壤通道)
    Light_Init();        // 光敏引脚(PB1=IN9, 复用ADC1, 必须在ADC1_Init后)
    Alarm_Hardware_Init();// 报警硬件(传感器输入/LED/蜂鸣器)
    Motor_Init();        // 电机(方向IO+TIM2_CH3 PWM)
    Servo_Init();        // 舵机(TIM2_CH2 PWM)
    Servo_SetAngle(0);   // 舵机初始0°(关窗, 与固件状态变量一致)
    OLED_Clear();        // 清屏(上电花屏残留兜底)

    // OLED静态标签: 只在初始化画一次, 主循环仅增量刷新数值区(省刷屏时间)
    OLED_ShowString(1, 1, "Temp:  C");  // 行1: 数值占列6~7(2位)
    OLED_ShowString(2, 1, "Humi:  %RH");// 行2: 数值占列6~7, 单位列8~10
    OLED_ShowString(3, 1, "Soil:");     // 行3左: 数值占列6~8(3位)
    OLED_ShowChar(3, 9, '%');           // 行3左: 百分号固定列9
    OLED_ShowString(3, 11, "L:");       // 行3右: 光照标签列11~12
    OLED_ShowChar(3, 16, '%');          // 行3右: 百分号列16(数值占13~15)
    OLED_ShowString(4, 1, "Air: ");     // 行4: 状态列5~8

    while(1)
    {
        /* ---- 1. DHT11温湿度: 失败时数值区显示"--"(等宽覆盖无残留) ---- */
        dht11_ok = (DHT11_Read_Data(&temperature, &humidity) == 0);  // 本轮读取结果
        if(dht11_ok)  // 成功: 数值区写实际值(2位, 高位补0)
        {
            // 串口打印
            printf("Temp:%dC   Humi:%d%%RH   ", temperature, humidity);
            OLED_ShowNum(1, 6, temperature, 2);  // OLED显示温度
            OLED_ShowNum(2, 6, humidity, 2);     // OLED显示湿度
        }
        else
        {
            printf("DHT11 Read Failed!   ");            // 读取失败时提示
            OLED_ShowString(1, 6, "--");  // 数值区显示占位符(等宽覆盖无残留)
            OLED_ShowString(2, 6, "--");
        }

        /* ---- 2. 土壤湿度: 单次采样 + 反比映射 ---- */
        water_level = ADC1_GetValue(ADC_Channel_0);        // 读取原始ADC值(越干越大)
        water_percent = Soil_GetMoisturePercent();         // 反比映射为湿度百分比
        printf("Soil:%d%%(raw=%d)  ", water_percent, water_level);  // 打印百分比+raw(便于校准)
        OLED_ShowNum(3, 6, water_percent, 3);         // OLED显示湿度百分比(占第6~8列)

        /* ---- 3. 光照强度: 单次采样, 仅展示不联动(见Light.h校准说明) ---- */
        light_raw = Light_GetRaw();
        light_percent = Light_GetPercent();
        printf("Light:%d%%  ", light_percent);
        OLED_ShowNum(3, 13, light_percent, 3);   // OLED显示光照百分比(占第13~15列)

        /* ---- 4. 空气质量: MQ-2烟雾判定, 与DHT11无关 → 先清后写(消除nice/bad切换残留) ---- */
        Check_Air_Quality();
        OLED_ShowString(4, 5, "      ");          // 先清后写, 消除nice/bad切换残留
        OLED_ShowString(4, 5, air_status);        // 只显示MQ-2判定结果nice/bad

        /* ---- 5. 串口指令消费与解析(mode-x/DUOJ-x/MOTOR-x) ---- */
        Process_Serial_Command();

        /* ---- 6. 报警判定与声光/执行器联动 ---- */
        temp_humi_alarm_status = Handle_Alarms();

        /* ---- 7. 协议帧输出(上位机按字段正则解析, 1.5s/帧) ----
         * 帧格式: Temp:%dC Humi:%d%%RH Soil:%d%%(raw=%d) Light:%d%%
         *         Servo:ON|OFF Motor:ON|OFF Mode:AUTO|MAN Air:nice|bad [Warning]\r\n
         * Warning后缀由固件报警判定附加(上位机同源阈值展示)
         * Mode字段: manual_mode=0输出AUTO, =1输出MAN, 供上位机/小程序
         * 回显当前控制模式(避免小程序本地模式状态与固件失步) */
        printf("Servo:%s Motor:%s Mode:%s ",
               (servo_angle == 180) ? "ON " : "OFF",
               (motor_status == 1) ? "ON " : "OFF",
               manual_mode ? "MAN" : "AUTO");

        if (temp_humi_alarm_status || (water_percent > ALARM_SOIL_MAX) || (strcmp(air_status, "bad") == 0))
        {
            printf("Air:%s Warning\r\n", air_status);  // 有报警时附加Warning
        }
        else
        {
            printf("Air:%s\r\n", air_status);  // 无报警时正常打印
        }

        /* ---- 8. OLED显示保活: 每1.5s重发充电泵+显示开, 自动救回灭屏 ---- */
        OLED_DisplayOn();

        Delay_ms(1500);  // 1.5秒读取一次
    }
}
