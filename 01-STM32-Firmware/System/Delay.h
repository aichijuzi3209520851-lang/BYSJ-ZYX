/**
  ******************************************************************************
  * @file    System/Delay.h
  * @brief   延时库头文件(FreeRTOS版)
  * @author  周颖暄
  * @version V2.0
  * @date    2026-10-25
  ******************************************************************************
  * @history
  *   V1.0  2026-09-20  首次发布(注释规范化)
  *   V2.0  2026-10-25  RTOS版: 仅保留延时接口, 毫秒时基由FreeRTOS tick承担
  ******************************************************************************
  */
#ifndef __DELAY_H
#define __DELAY_H
#include "stdint.h"

void Delay_Init(void);          /* DWT使能(SysTick归FreeRTOS, 此处不配置) */
void Delay_us(uint32_t us);     /* DWT微秒延时, 不触碰SysTick */
void Delay_ms(uint32_t ms);     /* 短阻塞毫秒延时(任务内长延时请用vTaskDelay) */
void Delay_s(uint32_t s);       /* 仅初始化阶段使用 */

#endif
