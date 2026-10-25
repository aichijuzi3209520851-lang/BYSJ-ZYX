/**
  ******************************************************************************
  * @file    System/Delay.c
  * @brief   软件延时库实现(SysTick轮询式)
  * @author  LCW
  * @version V1.2
  * @date    2026-10-25
  * @note    实现: 每次调用把SysTick临时配置为"HCLK计数→计数到0停表"
  *          (CTRL=0x05启动, 0x04停止), 用完即关——无需任何初始化函数,
  *          与FreeRTOS版(SysTick归内核, 改DWT)方案的区别即在此
  *          阻塞特性: 全部为忙等, 仅用于主循环/驱动时序, 中断中慎用
  ******************************************************************************
  * @history
  *   V1.0  2026-09-20  首次发布(注释规范化)
  *   V1.2  2026-10-25  交接级注释升级(纯注释零代码变更)
  ******************************************************************************
  */
#include "stm32f10x.h"

/**
  * @brief  微秒级延时(阻塞)
  * @param  xus 延时时长(µs), 72MHz下每µs=72个计数
  * @retval 无
  * @note   范围: 单次最大约59.6ms(SysTick 24位计数器, 2^24/72µs);
  *         Delay_ms内部按1ms分段调用规避此限制
  */
void Delay_us(uint32_t xus)
{
	SysTick->LOAD = 72 * xus;				//设置定时器重装值
	SysTick->VAL = 0x00;					//清空当前计数值
	SysTick->CTRL = 0x00000005;				//设置时钟源为HCLK，启动定时器
	while(!(SysTick->CTRL & 0x00010000));	//等待计数到0(COUNTFLAG置位)
	SysTick->CTRL = 0x00000004;				//关闭定时器
}

/**
  * @brief  毫秒级延时(阻塞, 按1ms分段调用Delay_us)
  * @param  xms 延时时长(ms)
  * @retval 无
  */
void Delay_ms(uint32_t xms)
{
	while(xms--)
	{
		Delay_us(1000);
	}
}

/**
  * @brief  秒级延时(阻塞, 按1s分段调用Delay_ms)
  * @param  xs 延时时长(s)
  * @retval 无
  */
void Delay_s(uint32_t xs)
{
	while(xs--)
	{
		Delay_ms(1000);
	}
}
