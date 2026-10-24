/**
  ******************************************************************************
  * @file    System/sys.c
  * @brief   系统底层函数实现(中断开关/休眠/栈指针设置)
  * @version V1.0
  * @date    2026-09-20
  * @note    通过ARM汇编指令实现
  ******************************************************************************
  * @history
  *   V1.0  2026-09-20  首次发布(注释规范化)
  ******************************************************************************
  */
#include "sys.h"
 
/**
  * @brief  进入低功耗等待状态(WFI指令)
  * @param  无
  * @retval 无
  */
void WFI_SET(void)
{
	__ASM volatile("wfi");		  
}

/**
  * @brief  关闭所有中断(PRIMASK置1)
  * @param  无
  * @retval 无
  */
void INTX_DISABLE(void)
{		  
	__ASM volatile("cpsid i");
}

/**
  * @brief  开启所有中断(PRIMASK清零)
  * @param  无
  * @retval 无
  */
void INTX_ENABLE(void)
{
	__ASM volatile("cpsie i");		  
}

/**
  * @brief  设置主栈指针(用于切换任务栈等)
  * @param  addr: 栈顶地址
  * @retval 无
  */
__asm void MSR_MSP(u32 addr) 
{
    MSR MSP, r0 			//set Main Stack value
    BX r14
}
