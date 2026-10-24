/**
  ******************************************************************************
  * @file    User/main.c
  * @brief   智能温室环境监控系统主程序(FreeRTOS任务化版)
  * @author  LCW
  * @version V2.1
  * @date    2026-10-25
  * @note    输入: DHT11温湿度 / 土壤湿度(D2-5-2K) / 烟雾传感器 / 双串口指令
  *          输出: OLED显示 / 多LED+蜂鸣器报警 / 电机与舵机联动
  *
  *          任务架构(4任务, 抢占式调度, 优先级数值越大越高):
  *          ┌─────────────────────────────────────────────────────┐
  *          │ USART1/3 ISR(prio6) ─帧─▶ xCmdQueue ─▶ 通信任务(2)  │
  *          │                                      指令解析/printf │
  *          │ 传感器任务(3, 周期1.5s)                             │
  *          │   DHT11(挂起调度保护时序)+土壤ADC+烟雾消抖           │
  *          │        │ xSensorQueue                               │
  *          │        ▼                                            │
  *          │ 报警控制任务(4) 统一阈值判定→LED/蜂鸣/执行器联动      │
  *          │        │ xReportQueue                               │
  *          │        ▼ 通信任务按协议打印(文本与V1.x逐字节一致)    │
  *          │ 显示任务(1, 周期500ms) OLED增量刷新                  │
  *          └─────────────────────────────────────────────────────┘
  *
  *          串口命令响应: 轮询版最坏1.5s → RTOS版毫秒级(中断+队列直驱)
  ******************************************************************************
  * @history
  *   V1.0  2026-09-20  注释规范化; 水位传感器替换为D2-5-2K土壤湿度模块
  *   V1.1  2026-10-25  报警阈值宏化统一(三端一致); 双串口指令解析合并
  *   V2.0  2026-10-25  FreeRTOS任务化重构(方案见 docs/RTOS改造方案.md)
  *   V2.1  2026-10-25  注释交接化升级: 四任务函数头增加"任务卡"(优先级/栈/
  *                     周期/阻塞点/输入输出/共享资源)速查信息
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

#include "stm32f10x.h"
#include "Delay.h"
#include "DHT11.h"
#include "usart.h"
#include "OLED.h"
#include "Water.h"      /* 土壤湿度传感器驱动(D2-5-2K) */
#include "Motor.h"
#include "Servo.h"
#include "FreeRTOS.h"
#include "task.h"
#include "queue.h"
#include "semphr.h"
#include <string.h>

/* ==================== 硬件引脚定义 ==================== */
#define SMOKE_DOUT PAin(6)   /* 烟雾传感器输出PA6 */
#define RED_LED PBout(10)    /* 红灯LED(烟雾报警) */
#define YELLOW_LED PBout(0)  /* 黄灯LED(温湿度报警) */
#define BLUE_LED PAout(3)    /* 蓝灯LED(土壤过湿报警) */
#define SMOKE_ALARM_LED PBout(4)  /* 烟雾浓度报警LED(PB4) */
#define BEEP PAout(7)        /* 蜂鸣器PA7 */

/* ==================== 统一报警阈值(三端一致) ==================== */
#define ALARM_TEMP_MAX   26   /* 温度报警上限(°C) */
#define ALARM_HUMI_MAX   85   /* 空气湿度报警上限(%RH) */
#define ALARM_SOIL_MAX   40   /* 土壤湿度报警上限(%, 过湿) */

/* ==================== 任务参数 ==================== */
#define TASK_PRIO_ALARM     4          /* 报警控制(最高应用优先级) */
#define TASK_PRIO_SENSOR    3          /* 传感器采集 */
#define TASK_PRIO_COMM      2          /* 串口通信 */
#define TASK_PRIO_DISPLAY   1          /* OLED显示(最低) */

#define TASK_STACK_SENSOR   512        /* 字(word), 含DHT11/ADC/消抖局部量 */
#define TASK_STACK_ALARM    256
#define TASK_STACK_COMM     384        /* 含printf调用链 */
#define TASK_STACK_DISPLAY  512        /* 含OLED局部缓冲 */

#define SENSOR_PERIOD_MS    1500       /* 采集周期(与旧版一致) */
#define DISPLAY_PERIOD_MS   500        /* 显示刷新周期 */
#define SMOKE_DEBOUNCE_MS   50         /* 烟雾消抖时长 */

/* ==================== 消息类型 ==================== */
/* 传感器数据快照(传感器任务→报警任务) */
typedef struct
{
    uint8_t  temperature;
    uint8_t  humidity;
    uint16_t soil_raw;
    uint8_t  soil_percent;
    char     air_status[10];
    uint8_t  dht11_ok;
} SensorMsg_t;

/* 协议输出快照(报警任务→通信任务) */
typedef struct
{
    SensorMsg_t sensor;
    uint8_t     temp_humi_alarm;
} ReportMsg_t;

/* ==================== 全局系统状态(互斥锁保护) ==================== */
typedef struct
{
    uint8_t  temperature;
    uint8_t  humidity;
    uint16_t soil_raw;
    uint8_t  soil_percent;
    char     air_status[10];
    uint8_t  dht11_ok;
    uint8_t  servo_angle;     /* 0或180 */
    uint8_t  motor_status;    /* 0停止/1运行 */
    uint8_t  manual_mode;     /* 0自动/1手动 */
} SysState_t;

static SysState_t g_sys;
static SemaphoreHandle_t xStateMutex;      /* g_sys读写互斥 */
static SemaphoreHandle_t xActuatorMutex;   /* 舵机/电机操作互斥 */

static QueueHandle_t xSensorQueue;         /* 传感器→报警 (深度1, 最新快照) */
static QueueHandle_t xReportQueue;         /* 报警→通信, 协议输出 (深度1) */

/* xCmdQueue 定义于 usart.c(ISR投递), 此处经usart.h extern 引用 */

/* ==================== 状态访问辅助(加锁) ==================== */
static void State_Set(const SysState_t *src)
{
    xSemaphoreTake(xStateMutex, portMAX_DELAY);
    g_sys = *src;
    xSemaphoreGive(xStateMutex);
}

static void State_Get(SysState_t *dst)
{
    xSemaphoreTake(xStateMutex, portMAX_DELAY);
    *dst = g_sys;
    xSemaphoreGive(xStateMutex);
}

/* ==================== 硬件初始化 ==================== */
/**
  * @brief  初始化所有报警相关硬件(传感器输入/报警LED/蜂鸣器)
  */
static void Alarm_Hardware_Init(void)
{
    GPIO_InitTypeDef GPIO_InitStructure;

    RCC_APB2PeriphClockCmd(RCC_APB2Periph_GPIOA, ENABLE);
    RCC_APB2PeriphClockCmd(RCC_APB2Periph_GPIOB, ENABLE);

    /* 烟雾传感器(PA6, 上拉输入) */
    GPIO_InitStructure.GPIO_Pin = GPIO_Pin_6;
    GPIO_InitStructure.GPIO_Mode = GPIO_Mode_IPU;
    GPIO_InitStructure.GPIO_Speed = GPIO_Speed_50MHz;
    GPIO_Init(GPIOA, &GPIO_InitStructure);

    /* 红灯LED(PB10)——注意PB10=USART3_TX,串口3将不能发送 */
    GPIO_InitStructure.GPIO_Pin = GPIO_Pin_10;
    GPIO_InitStructure.GPIO_Mode = GPIO_Mode_Out_PP;
    GPIO_Init(GPIOB, &GPIO_InitStructure);

    /* 黄灯LED(PB0) */
    GPIO_InitStructure.GPIO_Pin = GPIO_Pin_0;
    GPIO_InitStructure.GPIO_Mode = GPIO_Mode_Out_PP;
    GPIO_Init(GPIOB, &GPIO_InitStructure);

    /* 烟雾浓度报警LED(PB4) */
    GPIO_InitStructure.GPIO_Pin = GPIO_Pin_4;
    GPIO_InitStructure.GPIO_Mode = GPIO_Mode_Out_PP;
    GPIO_Init(GPIOB, &GPIO_InitStructure);

    /* 蓝灯LED(PA3) */
    GPIO_InitStructure.GPIO_Pin = GPIO_Pin_3;
    GPIO_InitStructure.GPIO_Mode = GPIO_Mode_Out_PP;
    GPIO_Init(GPIOA, &GPIO_InitStructure);

    /* 蜂鸣器(PA7) */
    GPIO_InitStructure.GPIO_Pin = GPIO_Pin_7;
    GPIO_InitStructure.GPIO_Mode = GPIO_Mode_Out_PP;
    GPIO_Init(GPIOA, &GPIO_InitStructure);

    /* 初始状态:LED熄灭,蜂鸣器关闭(低电平点亮/导通型) */
    RED_LED = 1;
    YELLOW_LED = 1;
    BLUE_LED = 1;
    SMOKE_ALARM_LED = 1;
    BEEP = 0;
}

/* ==================== 传感器任务 ==================== */
/**
  * @brief  传感器采集任务（周期 1.5s）
  *
  * 任务卡（交接速查）：
  *   优先级     : TASK_PRIO_SENSOR(3)，低于报警任务——采集可被报警判定打断
  *   栈         : 512字（DHT11时序局部量 + ADC累加 + 消息组装）
  *   周期/触发  : vTaskDelay 周期 1500ms
  *   阻塞点     : ① DHT11读取(~25ms, 挂起调度器保护) ② 消抖vTaskDelay(50ms)
  *                ③ 重试间隔vTaskDelay(200ms) ④ 周期等待vTaskDelay(1500ms)
  *   输入       : DHT11(PA15) / 土壤ADC(PA0) / 烟雾DOUT(PA6)
  *   输出       : xSensorQueue(深度1, xQueueOverwrite最新快照语义)
  *   共享资源   : 无直接共享——经消息队列解耦, 不持任何锁
  *
  * @note   DHT11失败重试2次(间隔200ms, 器件采样率<=1Hz约束下的补救窗口);
  *         土壤ADC取8次均值抑制抖动; 烟雾50ms双采样消抖(vTaskDelay不阻塞他任务)
  */
static void Sensor_Task(void *pvParameters)
{
    SensorMsg_t msg;
    u8 temp = 0, humi = 0;
    u8 retry;
    uint8_t smoke_first;
    uint32_t adc_sum;
    uint8_t i;

    (void)pvParameters;

    for (;;)
    {
        memset(&msg, 0, sizeof(msg));
        strcpy(msg.air_status, "nice");

        /* ---- DHT11: 最多3次尝试 ---- */
        msg.dht11_ok = 0;
        for (retry = 0; retry < 3; retry++)
        {
            if (DHT11_Read_Data(&temp, &humi) == 0)
            {
                msg.temperature = temp;
                msg.humidity    = humi;
                msg.dht11_ok    = 1;
                break;
            }
            vTaskDelay(pdMS_TO_TICKS(200));   /* 重试间隔(仅阻塞本任务) */
        }

        /* ---- 土壤湿度: 8次均值(soil_percent独立采样换算) ---- */
        adc_sum = 0;
        for (i = 0; i < 8; i++)
        {
            adc_sum += ADC1_GetValue(ADC_Channel_0);
        }
        msg.soil_raw     = (uint16_t)(adc_sum / 8);
        msg.soil_percent = Soil_GetMoisturePercent();

        /* ---- 烟雾: 双采样消抖(非阻塞) ---- */
        smoke_first = SMOKE_DOUT;
        vTaskDelay(pdMS_TO_TICKS(SMOKE_DEBOUNCE_MS));
        if ((smoke_first == 0) && (SMOKE_DOUT == 0))
        {
            strcpy(msg.air_status, "bad");
        }

        /* ---- 投递给报警任务(深度1: 只保留最新快照) ---- */
        xQueueOverwrite(xSensorQueue, &msg);

        vTaskDelay(pdMS_TO_TICKS(SENSOR_PERIOD_MS));
    }
}

/* ==================== 报警控制任务 ==================== */
/**
  * @brief  报警控制任务（事件驱动：收到传感器快照即判定）
  *
  * 任务卡（交接速查）：
  *   优先级     : TASK_PRIO_ALARM(4)，应用层最高——报警判定不被任何任务延迟
  *   栈         : 256字（判定逻辑无大局部量）
  *   周期/触发  : xQueueReceive(xSensorQueue) 阻塞等待，快照到达即运行
  *   阻塞点     : ① 等队列(portMAX_DELAY) ② xStateMutex/xActuatorMutex短临界区
  *   输入       : xSensorQueue 的 SensorMsg_t 快照
  *   输出       : LED/蜂鸣器 GPIO、舵机/电机(xActuatorMutex保护)、
  *                g_sys(xStateMutex保护)、xReportQueue(通知通信任务打印)
  *   共享资源   : g_sys 与执行器——全部经互斥锁访问
  *
  * @note   统一阈值判定→声光输出→自动模式执行器联动→更新系统状态→
  *         通知通信任务按协议打印。手动模式下执行器归通信任务管辖,
  *         本任务只更新声光与状态, 不碰舵机/电机
  */
static void Alarm_Task(void *pvParameters)
{
    SensorMsg_t s;
    SysState_t  st;
    ReportMsg_t rep;
    uint8_t temp_humi_alarm, soil_alarm, smoke_alarm;

    (void)pvParameters;

    for (;;)
    {
        xQueueReceive(xSensorQueue, &s, portMAX_DELAY);

        /* ---- 统一阈值判定(DHT11读取失败时不触发温湿度报警) ---- */
        temp_humi_alarm = (s.dht11_ok &&
                          (s.temperature > ALARM_TEMP_MAX || s.humidity > ALARM_HUMI_MAX)) ? 1 : 0;
        soil_alarm   = (s.soil_percent > ALARM_SOIL_MAX) ? 1 : 0;
        smoke_alarm  = (strcmp(s.air_status, "bad") == 0) ? 1 : 0;

        /* ---- 声光输出(低电平点亮/导通) ---- */
        YELLOW_LED      = temp_humi_alarm ? 0 : 1;
        BLUE_LED        = soil_alarm      ? 0 : 1;
        RED_LED         = smoke_alarm     ? 0 : 1;
        SMOKE_ALARM_LED = smoke_alarm     ? 0 : 1;
        BEEP = (temp_humi_alarm || soil_alarm || smoke_alarm) ? 0 : 1;

        /* ---- 状态组装(保留模式/执行器字段) ---- */
        State_Get(&st);
        st.temperature  = s.temperature;
        st.humidity     = s.humidity;
        st.soil_raw     = s.soil_raw;
        st.soil_percent = s.soil_percent;
        strcpy(st.air_status, s.air_status);
        st.dht11_ok     = s.dht11_ok;

        /* ---- 自动模式执行器联动(手动模式不干预) ---- */
        if (!st.manual_mode)
        {
            xSemaphoreTake(xActuatorMutex, portMAX_DELAY);
            if (smoke_alarm)
            {
                Motor_SetSpeed(100);
                st.motor_status = 1;
            }
            else
            {
                Motor_SetSpeed(0);
                st.motor_status = 0;
            }

            if (temp_humi_alarm)
            {
                Servo_SetAngle(180);
                st.servo_angle = 180;
            }
            else
            {
                Servo_SetAngle(0);
                st.servo_angle = 0;
            }
            xSemaphoreGive(xActuatorMutex);
        }

        State_Set(&st);

        /* ---- 通知通信任务输出协议帧 ---- */
        rep.sensor = s;
        rep.temp_humi_alarm = temp_humi_alarm;
        xQueueOverwrite(xReportQueue, &rep);
    }
}

/* ==================== 通信任务 ==================== */
/**
  * @brief  串口通信任务：指令解析执行 + 协议数据打印
  *
  * 任务卡（交接速查）：
  *   优先级     : TASK_PRIO_COMM(2)，高于显示——命令响应不被刷屏拖延
  *   栈         : 384字（含printf调用链，printf库需要一定栈深）
  *   周期/触发  : xQueueReceive(xCmdQueue) 50ms超时轮转（兼顾命令与打印两源）
  *   阻塞点     : ① 等命令队列(50ms超时) ② printf阻塞发送(~77ms/帧)
  *                ③ xStateMutex/xActuatorMutex短临界区
  *   输入       : xCmdQueue(ISR直投的命令帧) + xReportQueue(报警任务的打印请求)
  *   输出       : printf协议文本(USART1) + g_sys状态更新 + 手动模式执行器
  *   共享资源   : g_sys(xStateMutex)、舵机/电机(xActuatorMutex)
  *
  * @note   指令来自xCmdQueue(ISR直投, 毫秒级响应);
  *         协议文本与V1.x逐字节一致, 上位机/小程序零改动——修改打印格式
  *         前务必同步 02-PC-UpperComputer 的解析正则
  */
static void Comm_Task(void *pvParameters)
{
    CmdMsg_t    cmd;
    ReportMsg_t rep;
    SysState_t  st;

    (void)pvParameters;

    for (;;)
    {
        /* ---- 指令处理(50ms超时轮转, 保证协议输出同样及时) ---- */
        if (xQueueReceive(xCmdQueue, &cmd, pdMS_TO_TICKS(50)) == pdTRUE)
        {
            State_Get(&st);

            /* 模式切换: 任意时刻生效 */
            if (cmd.len >= 6 && strncmp(cmd.data, "mode-0", 6) == 0)
            {
                st.manual_mode = 0;
            }
            else if (cmd.len >= 6 && strncmp(cmd.data, "mode-1", 6) == 0)
            {
                st.manual_mode = 1;
            }
            /* 设备指令: 仅手动模式生效 */
            else if (st.manual_mode)
            {
                if (cmd.len >= 6 && strncmp(cmd.data, "DUOJ-0", 6) == 0)
                {
                    xSemaphoreTake(xActuatorMutex, portMAX_DELAY);
                    Servo_SetAngle(0);
                    xSemaphoreGive(xActuatorMutex);
                    st.servo_angle = 0;
                }
                else if (cmd.len >= 6 && strncmp(cmd.data, "DUOJ-1", 6) == 0)
                {
                    xSemaphoreTake(xActuatorMutex, portMAX_DELAY);
                    Servo_SetAngle(180);
                    xSemaphoreGive(xActuatorMutex);
                    st.servo_angle = 180;
                }
                else if (cmd.len >= 7 && strncmp(cmd.data, "MOTOR-0", 7) == 0)
                {
                    xSemaphoreTake(xActuatorMutex, portMAX_DELAY);
                    Motor_SetSpeed(0);
                    xSemaphoreGive(xActuatorMutex);
                    st.motor_status = 0;
                }
                else if (cmd.len >= 7 && strncmp(cmd.data, "MOTOR-1", 7) == 0)
                {
                    xSemaphoreTake(xActuatorMutex, portMAX_DELAY);
                    Motor_SetSpeed(100);
                    xSemaphoreGive(xActuatorMutex);
                    st.motor_status = 1;
                }
            }

            State_Set(&st);
        }

        /* ---- 协议输出(非阻塞读取报告队列) ---- */
        if (xQueueReceive(xReportQueue, &rep, 0) == pdTRUE)
        {
            State_Get(&st);

            if (rep.sensor.dht11_ok)
            {
                printf("Temp:%dC   Humi:%d%%RH   ", rep.sensor.temperature, rep.sensor.humidity);
            }
            else
            {
                printf("DHT11 Read Failed!   ");
            }

            printf("Soil:%d%%(raw=%d)  ", rep.sensor.soil_percent, rep.sensor.soil_raw);

            printf("Servo:%s Motor:%s ",
                   (st.servo_angle == 180) ? "ON " : "OFF",
                   (st.motor_status == 1)  ? "ON " : "OFF");

            if (rep.temp_humi_alarm ||
                (rep.sensor.soil_percent > ALARM_SOIL_MAX) ||
                (strcmp(rep.sensor.air_status, "bad") == 0))
            {
                printf("Air:%s Warning\r\n", rep.sensor.air_status);
            }
            else
            {
                printf("Air:%s\r\n", rep.sensor.air_status);
            }
        }
    }
}

/* ==================== 显示任务 ==================== */
/**
  * @brief  OLED显示任务（周期500ms，增量刷新数值区）
  *
  * 任务卡（交接速查）：
  *   优先级     : TASK_PRIO_DISPLAY(1)，全系统最低——刷屏忙等期间让出CPU给所有人
  *   栈         : 512字（OLED驱动调用链使用局部缓冲）
  *   周期/触发  : vTaskDelay 周期 500ms
  *   阻塞点     : ① xStateMutex短临界区 ② 软件I2C刷屏忙等(每字符~1ms, 见OLED.c)
  *   输入       : g_sys(xStateMutex保护下读取快照)
  *   输出       : OLED数值区(标签已在main初始化时画好, 本任务只刷数值)
  *   共享资源   : g_sys(只读)——绝不在本任务调用任何阻塞>10ms的非显示API
  *
  * @note   设计依据: OLED软件I2C整屏刷新35~50ms且CPU忙等(耗时预算见OLED.c
  *         文件头), 因此放最低优先级+增量刷新, 报警爆发时刷屏自动让路
  */
static void Display_Task(void *pvParameters)
{
    SysState_t st;

    (void)pvParameters;

    for (;;)
    {
        State_Get(&st);

        if (st.dht11_ok)
        {
            OLED_ShowNum(1, 6, st.temperature, 2);
            OLED_ShowNum(2, 6, st.humidity, 2);
        }
        else
        {
            OLED_ShowString(4, 5, "Error!");
        }

        OLED_ShowNum(3, 12, st.soil_percent, 3);
        OLED_ShowString(4, 5, st.air_status);

        vTaskDelay(pdMS_TO_TICKS(DISPLAY_PERIOD_MS));
    }
}

/* ==================== 内核钩子 ==================== */
/**
  * @brief  栈溢出钩子: 全LED点亮+蜂鸣器长鸣指示(检测方式2)
  */
void vApplicationStackOverflowHook(TaskHandle_t xTask, char *pcTaskName)
{
    (void)xTask;
    (void)pcTaskName;
    taskDISABLE_INTERRUPTS();
    RED_LED = 0; YELLOW_LED = 0; BLUE_LED = 0; SMOKE_ALARM_LED = 0;
    BEEP = 0;
    for (;;);
}

/* ==================== 主函数 ==================== */
/**
  * @brief  程序入口: 外设初始化→创建队列/互斥锁/任务→启动调度器
  * @note   调度器启动后main不再返回; 空闲任务兜底
  */
int main(void)
{
    SysState_t init_state;

    /* ---- NVIC分组显式固化(4位全抢占, 与FreeRTOS优先级模型配套) ---- */
    NVIC_PriorityGroupConfig(NVIC_PriorityGroup_4);

    /* ---- 外设初始化 ---- */
    Delay_Init();        /* DWT使能(须在任何Delay_us前; SysTick归内核) */
    uart_init(9600);     /* USART1, 优先级6 */
    uart3_init(9600);    /* USART3, 优先级6 */
    DHT11_Init();
    OLED_Init();
    ADC1_Init();
    Alarm_Hardware_Init();
    Motor_Init();
    Servo_Init();
    Servo_SetAngle(0);
    OLED_Clear();

    /* ---- OLED固定标签 ---- */
    OLED_ShowString(1, 1, "Temp:  C");
    OLED_ShowString(2, 1, "Humi:  %RH");
    OLED_ShowString(3, 1, "Soil Water:");
    OLED_ShowChar(3, 15, '%');
    OLED_ShowString(4, 1, "Air: ");

    /* ---- 初始状态 ---- */
    memset(&init_state, 0, sizeof(init_state));
    strcpy(init_state.air_status, "nice");
    g_sys = init_state;

    /* ---- 同步原语 ---- */
    xStateMutex    = xSemaphoreCreateMutex();
    xActuatorMutex = xSemaphoreCreateMutex();
    xSensorQueue   = xQueueCreate(1, sizeof(SensorMsg_t));   /* 深度1: 最新快照语义 */
    xReportQueue   = xQueueCreate(1, sizeof(ReportMsg_t));
    xCmdQueue      = xQueueCreate(8, sizeof(CmdMsg_t));      /* usart.h声明, ISR消费 */

    configASSERT(xStateMutex && xActuatorMutex && xSensorQueue && xReportQueue && xCmdQueue);

    /* ---- 创建任务 ---- */
    xTaskCreate(Alarm_Task,   "alarm",   TASK_STACK_ALARM,   NULL, TASK_PRIO_ALARM,   NULL);
    xTaskCreate(Sensor_Task,  "sensor",  TASK_STACK_SENSOR,  NULL, TASK_PRIO_SENSOR,  NULL);
    xTaskCreate(Comm_Task,    "comm",    TASK_STACK_COMM,    NULL, TASK_PRIO_COMM,    NULL);
    xTaskCreate(Display_Task, "oled",    TASK_STACK_DISPLAY, NULL, TASK_PRIO_DISPLAY, NULL);

    /* ---- 启动调度器(不返回) ---- */
    vTaskStartScheduler();

    /* 理论不可达; 到此说明堆内存不足以创建空闲任务 */
    for (;;);
}
