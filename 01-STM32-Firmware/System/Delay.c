/**
  ******************************************************************************
  * @file    System/Delay.c
  * @brief   延时库(FreeRTOS版)
  * @author  周颖暄
  * @version V2.0
  * @date    2026-10-25
  * @note    微秒延时: DWT->CYCCNT周期计数器轮询, 完全不触碰SysTick,
  *          SysTick已让给FreeRTOS作系统心跳, 二者互不干扰;
  *          毫秒级任务周期请使用vTaskDelay, 本文件Delay_ms仅用于
  *          时序窗口内的短阻塞(如DHT11起始信号)
  ******************************************************************************
  * @history
  *   V1.0  2026-09-20  首次发布(注释规范化)
  *   V2.0  2026-10-25  RTOS版: Delay_us改DWT实现, 删除SysTick占用
  ******************************************************************************
  */
#include "stm32f10x.h"
#include "Delay.h"

/* ==================== DWT/CoreDebug寄存器定义 ====================
 * 旧版CMSIS(V3.5标准库自带core_cm3.h)未定义DWT/CoreDebug,
 * 此处按Cortex-M3系统地址直接定义, 不依赖CMSIS版本 */
#define DEMCR_REG          (*(volatile unsigned int *)0xE000EDFCU)  /* 调试异常与复用控制 */
#define DEMCR_TRCENA_BIT   (1UL << 24)                              /* DWT使能位 */
#define DWT_CTRL_REG       (*(volatile unsigned int *)0xE0001000U)  /* DWT控制寄存器 */
#define DWT_CYCCNT_REG     (*(volatile unsigned int *)0xE0001004U)  /* 周期计数器 */
#define DWT_CYCCNTENA_BIT  (1UL << 0)                                /* CYCCNT使能位 */

/**
  * @brief  初始化延时库: 使能DWT周期计数器
  * @param  无
  * @retval 无
  * @note   SysTick由FreeRTOS内核接管(vTaskStartScheduler时配置),
  *          本函数只负责DWT, 不配置SysTick
  */
void Delay_Init(void)
{
    DEMCR_REG |= DEMCR_TRCENA_BIT;     // 开放DWT外设
    DWT_CYCCNT_REG = 0;                // 清零周期计数器
    DWT_CTRL_REG |= DWT_CYCCNTENA_BIT; // 启动CYCCNT
}

/**
  * @brief  微秒级延时(DWT实现, 不触碰SysTick, 中断/调度照常)
  * @param  xus 延时时长(72MHz下每微秒72个周期)
  * @retval 无
  * @note   调用前须完成Delay_Init(); 精度±数个周期
  */
void Delay_us(uint32_t xus)
{
    uint32_t start  = DWT_CYCCNT_REG;
    uint32_t cycles = xus * (SystemCoreClock / 1000000U);  // 72 cycles/us
    while ((DWT_CYCCNT_REG - start) < cycles)
    {
        /* 忙等, 不关闭任何中断/定时器 */
    }
}

/**
  * @brief  毫秒级延时(短阻塞, 用于时序窗口内部)
  * @param  xms 延时时长
  * @retval 无
  * @note   任务级长延时(>=1个心跳周期)应使用vTaskDelay让出CPU
  */
void Delay_ms(uint32_t xms)
{
    while (xms--)
    {
        Delay_us(1000);
    }
}

/**
  * @brief  秒级延时(仅初始化阶段使用; 任务内请用vTaskDelay)
  * @param  xs 延时时长
  * @retval 无
  */
void Delay_s(uint32_t xs)
{
    while (xs--)
    {
        Delay_ms(1000);
    }
}
