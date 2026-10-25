/**
  ******************************************************************************
  * @file    User/main.c
  * @brief   智能温室环境监控系统主程序
  * @version V1.0
  * @date    2026-09-20
  * @note    输入: DHT11温湿度 / 土壤湿度(D2-5-2K) / 烟雾传感器 / 双串口指令
  *          输出: OLED显示 / 多LED+蜂鸣器报警 / 电机与舵机联动
  *          工作模式: 自动(报警联动执行器) 与 手动(串口指令) 可切换
  *          主循环周期 1.5s
  ******************************************************************************
 * @history
 *   V1.0  2026-09-20  注释规范化; 水位传感器替换为D2-5-2K土壤湿度模块
 *   V1.1  2026-10-25  报警阈值宏化统一(三端一致); USART1/USART3指令解析
 *                     合并为共用函数Parse_Command_Frame, DUOJ-1统一开窗180°
 ******************************************************************************
 */
//说明:
//	VCC--3.3V/5V
//	GND--GND
//	DATA--PA15(接DHT11)
//	Soil Sensor AO--PA0(ADC通道)  (D2-5-2K电容式,读数与湿度成反比)
//	Smoke DOUT--PA6
//	红灯LED--PB10(烟雾报警) [占用USART3_TX;原PA1与舵机TIM2_CH2冲突]
//  黄灯LED--PB0(温湿度报警)
//  蓝灯LED--PA3(土壤过湿报警)
//  烟雾报警LED--PB4(烟雾浓度报警)
//  蜂鸣器--PA7

#include "sys.h"
#include "Delay.h"
#include "DHT11.h"
#include "usart.h"
#include "OLED.h"
#include "Water.h"  // 土壤湿度传感器驱动头文件(D2-5-2K)
#include "Motor.h"
#include "Servo.h"
#include <string.h>  // 字符串函数库

/* ==================== 硬件引脚定义 ==================== */
#define SMOKE_DOUT PAin(6)   // 烟雾传感器输出PA6
#define RED_LED PBout(10)    // 红灯LED(烟雾报警,改接PB10,舍弃USART3_TX)
#define YELLOW_LED PBout(0)  // 黄灯LED(温湿度报警)
#define BLUE_LED PAout(3)    // 蓝灯LED(土壤过湿报警)
#define SMOKE_ALARM_LED PBout(4)  // 烟雾浓度报警LED(PB4)
#define BEEP PAout(7)        // 蜂鸣器PA7

/* ==================== 统一报警阈值 ====================
 * 三端(STM32固件/PC上位机/微信小程序)使用同一组数值,
 * 固件为权威判定源: 本地毫秒级联动, 上位机/小程序仅做展示 */
#define ALARM_TEMP_MAX   26   // 温度报警上限(°C)
#define ALARM_HUMI_MAX   85   // 空气湿度报警上限(%RH)
#define ALARM_SOIL_MAX   40   // 土壤湿度报警上限(%, 超过视为过湿)

/* ==================== 全局状态变量 ==================== */
u8 temperature;  // 温度变量
u8 humidity;     // 湿度变量
uint16_t water_level;  // 土壤湿度原始ADC值(0~4095, 越干越大)
uint8_t water_percent; // 土壤湿度百分比(0~100, 越大越湿)
char air_status[10] = "nice";  // 空气质量状态
uint8_t servo_angle = 0;  // 舵机当前角度状态(0或180)
uint8_t manual_mode = 0;  // 手动控制模式(1=串口手动控制,0=自动报警控制)
uint8_t motor_status = 0;  // 电机状态(0=停止,1=运行)

/**
  * @brief  初始化所有报警相关硬件(传感器/报警LED/蜂鸣器)
  * @param  无
  * @retval 无
  * @note   LED均为低电平点亮; 初始状态全灭, 蜂鸣器关闭
  */
void Alarm_Hardware_Init(void)
{
    GPIO_InitTypeDef GPIO_InitStructure;
    
    RCC_APB2PeriphClockCmd(RCC_APB2Periph_GPIOA, ENABLE);  // 使能PA端口时钟
    RCC_APB2PeriphClockCmd(RCC_APB2Periph_GPIOB, ENABLE);  // 使能PB端口时钟
    
    // 烟雾传感器(PA6)
    GPIO_InitStructure.GPIO_Pin = GPIO_Pin_6;
    GPIO_InitStructure.GPIO_Mode = GPIO_Mode_IPU;  // 上拉输入
    GPIO_InitStructure.GPIO_Speed = GPIO_Speed_50MHz;
    GPIO_Init(GPIOA, &GPIO_InitStructure);

    // 红灯LED(PB10)——原PA1被舵机TIM2_CH2占用;注意PB10=USART3_TX,串口3将不能发送
    GPIO_InitStructure.GPIO_Pin = GPIO_Pin_10;
    GPIO_InitStructure.GPIO_Mode = GPIO_Mode_Out_PP;  // 推挽输出
    GPIO_Init(GPIOB, &GPIO_InitStructure);

    // 黄灯LED(PB0)
    GPIO_InitStructure.GPIO_Pin = GPIO_Pin_0;
    GPIO_InitStructure.GPIO_Mode = GPIO_Mode_Out_PP;
    GPIO_Init(GPIOB, &GPIO_InitStructure);

    // 烟雾浓度报警LED(PB4)
    GPIO_InitStructure.GPIO_Pin = GPIO_Pin_4;
    GPIO_InitStructure.GPIO_Mode = GPIO_Mode_Out_PP;
    GPIO_Init(GPIOB, &GPIO_InitStructure);

    // 蓝灯LED(PA3)
    GPIO_InitStructure.GPIO_Pin = GPIO_Pin_3;
    GPIO_InitStructure.GPIO_Mode = GPIO_Mode_Out_PP;
    GPIO_Init(GPIOA, &GPIO_InitStructure);

    // 蜂鸣器(PA7)
    GPIO_InitStructure.GPIO_Pin = GPIO_Pin_7;
    GPIO_InitStructure.GPIO_Mode = GPIO_Mode_Out_PP;
    GPIO_Init(GPIOA, &GPIO_InitStructure);

    // 初始状态:LED熄灭,蜂鸣器关闭
    RED_LED = 1;
    YELLOW_LED = 1;
    BLUE_LED = 1;
    SMOKE_ALARM_LED = 1;
    BEEP = 0;
}

/**
  * @brief  检测空气质量状态(烟雾传感器)
  * @param  无
  * @retval 无
  * @note   PA6低电平(消抖50ms后仍为低)判定为"bad", 否则"nice"
  */
void Check_Air_Quality(void)
{
    if(SMOKE_DOUT == 0)
    {
        Delay_ms(50);  // 延时消抖
        if(SMOKE_DOUT == 0)
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
  * @param  len: 指令长度
  * @note   指令集: mode-0/mode-1 切换自动/手动模式;
  *         DUOJ-0/DUOJ-1 舵机关窗(0°)/开窗(180°);
  *         MOTOR-0/MOTOR-1 电机停止/全速(设备指令仅手动模式生效)
  *         两条串口走同一解析函数, 保证指令行为完全一致
  */
static void Parse_Command_Frame(const char *buf, u16 len)
{
    // 检测模式切换指令
    if(len >= 6 && strncmp(buf, "mode-0", 6) == 0)
    {
        manual_mode = 0;  // 切换到自动模式
    }
    else if(len >= 6 && strncmp(buf, "mode-1", 6) == 0)
    {
        manual_mode = 1;  // 切换到手动模式
    }
    // 只有在手动模式下才处理设备控制指令
    else if(manual_mode)
    {
        // 检测舵机控制指令(开窗角度与自动模式报警联动一致, 均为180°)
        if(len >= 6 && strncmp(buf, "DUOJ-0", 6) == 0)
        {
            Servo_SetAngle(0);
            servo_angle = 0;
        }
        else if(len >= 6 && strncmp(buf, "DUOJ-1", 6) == 0)
        {
            Servo_SetAngle(180);
            servo_angle = 180;
        }
        // 检测电机控制指令
        else if(len >= 7 && strncmp(buf, "MOTOR-0", 7) == 0)
        {
            Motor_SetSpeed(0);  // 停止电机
            motor_status = 0;
        }
        else if(len >= 7 && strncmp(buf, "MOTOR-1", 7) == 0)
        {
            Motor_SetSpeed(100);  // 启动电机全速
            motor_status = 1;
        }
    }
}

/**
  * @brief  处理USART1/USART3的串口指令
  * @param  无
  * @retval 无
  * @note   两条串口的接收帧统一交给 Parse_Command_Frame 解析,
  *         避免两处独立实现导致指令行为不一致
  */
void Process_Serial_Command(void)
{
    // 处理USART1的指令
    if(USART_RX_STA & 0x8000)  // 接收完成
    {
        u16 len = USART_RX_STA & 0x3FFF;  // 获取接收长度
        Parse_Command_Frame((const char*)USART_RX_BUF, len);
        USART_RX_STA = 0;  // 清除接收标志
    }

    // 处理USART3的指令(与USART1共用同一解析入口)
    if(USART3_RX_STA & 0x8000)  // 接收完成
    {
        u16 len = USART3_RX_STA & 0x3FFF;  // 获取接收长度
        Parse_Command_Frame((const char*)USART3_RX_BUF, len);
        USART3_RX_STA = 0;  // 清除接收标志
    }
}

/**
  * @brief  处理报警逻辑
  * @param  无
  * @retval 1:温湿度报警触发; 0:未触发
  * @note   统一阈值(与上位机/小程序一致):
  *         温度>ALARM_TEMP_MAX或湿度>ALARM_HUMI_MAX -> 黄灯;
  *         土壤湿度>ALARM_SOIL_MAX -> 蓝灯;
  *         空气为bad -> 红灯+浓烟灯; 任一报警 -> 蜂鸣器
  *         自动模式下由报警联动执行器(烟雾->电机全速, 温湿度->舵机开窗180°)
  */
uint8_t Handle_Alarms(void)
{
    // 1. 温湿度报警判断
    uint8_t temp_humi_alarm = (temperature > ALARM_TEMP_MAX || humidity > ALARM_HUMI_MAX) ? 1 : 0;

    // 2. 土壤湿度过湿报警判断(湿度>ALARM_SOIL_MAX视为过湿; 若想改为"过干报警"把 > 改成 <)
    uint8_t water_alarm = (water_percent > ALARM_SOIL_MAX) ? 1 : 0;

    // 3. 烟雾报警判断(空气质量为bad)
    uint8_t smoke_alarm = (strcmp(air_status, "bad") == 0) ? 1 : 0;

    // 控制对应LED
    YELLOW_LED = temp_humi_alarm ? 0 : 1;  // 低电平点亮
    BLUE_LED = water_alarm ? 0 : 1;
    RED_LED = smoke_alarm ? 0 : 1;
    SMOKE_ALARM_LED = smoke_alarm ? 0 : 1;  // 烟雾浓度报警LED

    // 控制蜂鸣器(任一报警即触发)
    BEEP = (temp_humi_alarm || water_alarm || smoke_alarm) ? 0 : 1;

    // 只有在自动模式下才由报警状态控制执行器
    if (!manual_mode)
    {
        // 控制电机(烟雾报警时全速转动)
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

        // 控制舵机(温湿度报警时转动180度)
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

    // 返回温湿度报警状态
    return temp_humi_alarm;
}

/**
  * @brief  程序主入口
  * @param  无
  * @retval 无
  * @note   初始化全部外设后, 进入1.5s周期的数据采集/显示/报警主循环
  */
int main(void)
{
    uint8_t temp_humi_alarm_status;  // 温湿度报警状态
    uint8_t dht11_ok;                // DHT11本轮读取成功标志(OLED显示用)

    uart_init(9600);     // 初始化串口1,波特率9600
    uart3_init(9600);    // 初始化串口3,波特率9600
    DHT11_Init();        // 初始化DHT11温湿度传感器
    OLED_Init();         // 初始化OLED显示屏
    ADC1_Init();         // 初始化ADC(土壤湿度传感器)
    Alarm_Hardware_Init();// 初始化所有报警硬件
    Motor_Init();        // 初始化电机
    Servo_Init();        // 初始化舵机
    Servo_SetAngle(0);   // 舵机初始位置0度
    OLED_Clear();        // 清屏

    // 固定显示标识
    OLED_ShowString(1, 1, "Temp:  C");  // 第一行显示温度标识
    OLED_ShowString(2, 1, "Humi:  %RH");// 第二行显示湿度标识
    OLED_ShowString(3, 1, "Soil:");        // 第三行显示土壤湿度标签(右侧留白, 预留光照显示)
    OLED_ShowChar(3, 9, '%');              // 百分号固定在第9列(数值占第6~8列)
    OLED_ShowString(4, 1, "Air: ");      // 第四行显示空气质量标识

    while(1)
    {
        // 读取温湿度数据
        dht11_ok = (DHT11_Read_Data(&temperature, &humidity) == 0);  // 本轮读取结果
        if(dht11_ok)  // 成功读取数据
        {
            // 串口打印
            printf("Temp:%dC   Humi:%d%%RH   ", temperature, humidity);
            OLED_ShowNum(1, 6, temperature, 2);  // OLED显示温度
            OLED_ShowNum(2, 6, humidity, 2);    // OLED显示湿度
        }
        else
        {
            printf("DHT11 Read Failed!   ");            // 读取失败时提示
            OLED_ShowString(1, 6, "--");  // 数值区显示占位符(等宽覆盖无残留)
            OLED_ShowString(2, 6, "--");
        }

        // 读取土壤湿度数据(D2-5-2K电容式,读数与湿度成反比)
        water_level = ADC1_GetValue(ADC_Channel_0);        // 读取原始ADC值(越干越大)
        water_percent = Soil_GetMoisturePercent();         // 反比映射为湿度百分比
        printf("Soil:%d%%(raw=%d)  ", water_percent, water_level);  // 打印百分比+raw(便于校准)
        OLED_ShowNum(3, 6, water_percent, 3);         // OLED显示湿度百分比(占第6~8列)

        // 检测空气质量
        Check_Air_Quality();
        OLED_ShowString(4, 5, "      ");          // 先清后写, 消除Error!/nice切换残留
        OLED_ShowString(4, 5, dht11_ok ? air_status : "Error!");  // DHT11失败时提示

        // 处理串口指令
        Process_Serial_Command();

        // 处理报警逻辑
        temp_humi_alarm_status = Handle_Alarms();

        // 打印舵机和电机状态(根据实际状态显示)
        printf("Servo:%s Motor:%s ",
               (servo_angle == 180) ? "ON " : "OFF",
               (motor_status == 1) ? "ON " : "OFF");

        // 打印Air状态,根据报警状态决定是否附加Warning(在输出Air状态后,不换行)
        if (temp_humi_alarm_status || (water_percent > ALARM_SOIL_MAX) || (strcmp(air_status, "bad") == 0))
        {
            printf("Air:%s Warning\r\n", air_status);  // 有报警时附加Warning
        }
        else
        {
            printf("Air:%s\r\n", air_status);  // 无报警时正常打印
        }

        Delay_ms(1500);  // 1.5秒读取一次
    }
}
