/**
  ******************************************************************************
  * @file    Project/STM32F10x_StdPeriph_Template/stm32f10x_it.c 
  * @author  MCD Application Team
  * @version V3.5.0
  * @date    08-April-2011
  * @brief   Main Interrupt Service Routines.
  *          This file provides template for all exceptions handler and 
  *          peripherals interrupt service routine.
  ******************************************************************************
  * @attention
  *
  * THE PRESENT FIRMWARE WHICH IS FOR GUIDANCE ONLY AIMS AT PROVIDING CUSTOMERS
  * WITH CODING INFORMATION REGARDING THEIR PRODUCTS IN ORDER FOR THEM TO SAVE
  * TIME. AS A RESULT, STMICROELECTRONICS SHALL NOT BE HELD LIABLE FOR ANY
  * DIRECT, INDIRECT OR CONSEQUENTIAL DAMAGES WITH RESPECT TO ANY CLAIMS ARISING
  * FROM THE CONTENT OF SUCH FIRMWARE AND/OR THE USE MADE BY CUSTOMERS OF THE
  * CODING INFORMATION CONTAINED HEREIN IN CONNECTION WITH THEIR PRODUCTS.
  *
  * <h2><center>&copy; COPYRIGHT 2011 STMicroelectronics</center></h2>
  ******************************************************************************
  */

/* Includes ------------------------------------------------------------------*/
#include "stm32f10x_it.h"

/** @addtogroup STM32F10x_StdPeriph_Template
  * @{
  */

/*
 * ============================================================================
 *                    本工程中断使用说明（交接必读） 2026-10-25
 * ============================================================================
 * 一、本文件只保留 Cortex-M3 内核异常的兜底 handler（下方）。
 *    外设中断服务函数不在本文件，分布如下：
 *      - USART1_IRQHandler / USART3_IRQHandler  -> System/usart.c（帧接收状态机）
 *      - SysTick / PendSV / SVC                 -> FreeRTOS 内核端口接管
 *        （见 User/FreeRTOSConfig.h 末尾的宏映射；本文件原有三个空 handler
 *          已删除，否则与内核端口符号冲突导致调度器失效）
 *
 * 二、当前中断优先级规划（NVIC 分组4，4位全抢占，数值越小优先级越高）：
 *      优先级 6  : USART1 / USART3   ——可安全调用 FromISR API
 *      阈值  5  : configMAX_SYSCALL_INTERRUPT_PRIORITY
 *      优先级 15: SysTick / PendSV  ——FreeRTOS 内核固定（最低）
 *
 * 三、HardFault 死机排查指引：
 *      1) Keil 调试模式下暂停，查看 Call Stack Window 定位死机调用链
 *      2) 本工程最常见原因：任务栈溢出（已配 configCHECK_FOR_STACK_OVERFLOW=2，
 *         溢出会先触发 vApplicationStackOverflowHook 点亮全部LED+蜂鸣器，
 *         若直接 HardFault 则多为野指针/未对齐访问）
 * ============================================================================
 */

/* Private typedef -----------------------------------------------------------*/
/* Private define ------------------------------------------------------------*/
/* Private macro -------------------------------------------------------------*/
/* Private variables ---------------------------------------------------------*/
/* Private function prototypes -----------------------------------------------*/
/* Private functions ---------------------------------------------------------*/

/******************************************************************************/
/*            Cortex-M3 Processor Exceptions Handlers                         */
/******************************************************************************/

/**
  * @brief  This function handles NMI exception.
  * @param  None
  * @retval None
  */
void NMI_Handler(void)
{
}

/**
  * @brief  硬件错误异常（最严重的运行时错误，正常程序不应到达）
  * @param  None
  * @retval None
  * @note   常见诱因：野指针/未对齐访问/栈被写穿。调试方法见文件头"排查指引"。
  *          量产处置建议：此处点亮报警LED并软件复位(NVIC_SystemReset)
  */
void HardFault_Handler(void)
{
  /* Go to infinite loop when Hard Fault exception occurs */
  while (1)
  {
  }
}

/**
  * @brief  This function handles Memory Manage exception.
  * @param  None
  * @retval None
  */
void MemManage_Handler(void)
{
  /* Go to infinite loop when Memory Manage exception occurs */
  while (1)
  {
  }
}

/**
  * @brief  This function handles Bus Fault exception.
  * @param  None
  * @retval None
  */
void BusFault_Handler(void)
{
  /* Go to infinite loop when Bus Fault exception occurs */
  while (1)
  {
  }
}

/**
  * @brief  This function handles Usage Fault exception.
  * @param  None
  * @retval None
  */
void UsageFault_Handler(void)
{
  /* Go to infinite loop when Usage Fault exception occurs */
  while (1)
  {
  }
}

/**
  * @brief  SVCall / PendSV / SysTick 三个内核异常入口已由 FreeRTOS 端口接管:
  *         见 User/FreeRTOSConfig.h 中的宏映射(vPortSVCHandler/xPortPendSVHandler/
  *         xPortSysTickHandler), 此处不再定义, 否则符号重复。
  */

/**
  * @brief  This function handles Debug Monitor exception.
  * @param  None
  * @retval None
  */
void DebugMon_Handler(void)
{
}

/******************************************************************************/
/*                 STM32F10x Peripherals Interrupt Handlers                   */
/*  Add here the Interrupt Handler for the used peripheral(s) (PPP), for the  */
/*  available peripheral interrupt handler's name please refer to the startup */
/*  file (startup_stm32f10x_xx.s).                                            */
/******************************************************************************/

/**
  * @brief  This function handles PPP interrupt request.
  * @param  None
  * @retval None
  */
/*void PPP_IRQHandler(void)
{
}*/

/**
  * @}
  */ 


/******************* (C) COPYRIGHT 2011 STMicroelectronics *****END OF FILE****/
