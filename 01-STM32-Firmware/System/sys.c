/**
  ******************************************************************************
  * @file    System/sys.c
  * @brief   系统底层函数实现（位带操作支持 / 中断开关 / 休眠 / 栈指针设置）
  * @author  LCW
  * @version V1.1
  * @date    2026-10-25
  * @note    通过内联 ARM 汇编实现底层控制。
  *          !!! FreeRTOS 重要约定 !!!
  *          - INTX_DISABLE/INTX_ENABLE 直接操作 PRIMASK（全局关中断），
  *            在 RTOS 下禁止用于包裹任何内核 API 调用（会破坏内核临界区嵌套
  *            计数，导致解临界区时中断意外开启）；需要临界区请一律使用
  *            taskENTER_CRITICAL()/taskEXIT_CRITICAL()。
  *          - 当前工程无调用者，仅作为工具函数保留。
  *          - MSR_MSP 用于 IAP/ bootloader 切换主栈，应用工程不使用。
  ******************************************************************************
  * @history
  *   V1.0  2026-09-20  首次发布(注释规范化)
  *   V1.1  2026-10-25  注释交接化升级: RTOS 下使用约束警告
  ******************************************************************************
  */
#include "sys.h"

/**
  * @brief  进入低功耗等待状态（WFI 指令）
  * @param  无
  * @retval 无
  * @note   执行后 CPU 挂起直到任意中断到来；RTOS 的空闲任务内部已使用
  *          portSUPPRESS_TICKS_AND_SLEEP 相关机制，应用无需显式调用
  */
void WFI_SET(void)
{
	__ASM volatile("wfi");
}

/**
  * @brief  关闭所有可屏蔽中断（PRIMASK 置 1）
  * @param  无
  * @retval 无
  * @warning FreeRTOS 下禁止与内核 API 混用，详见文件头说明；
  *          如需短临界区请使用 taskENTER_CRITICAL()
  */
void INTX_DISABLE(void)
{
	__ASM volatile("cpsid i");
}

/**
  * @brief  开启所有可屏蔽中断（PRIMASK 清零）
  * @param  无
  * @retval 无
  * @warning 与 INTX_DISABLE 配对使用，FreeRTOS 下同样禁止与内核 API 混用
  */
void INTX_ENABLE(void)
{
	__ASM volatile("cpsie i");
}

/**
  * @brief  设置主栈指针 MSP（用于 IAP/双区固件切换任务栈）
  * @param  addr: 新的栈顶地址（须 8 字节对齐）
  * @retval 无
  * @note   本应用工程不使用；调度器启动后调用会破坏内核状态
  */
__asm void MSR_MSP(u32 addr)
{
    MSR MSP, r0 			//set Main Stack value
    BX r14
}
