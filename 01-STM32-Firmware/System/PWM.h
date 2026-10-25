/**
  ******************************************************************************
  * @file    System/PWM.h
  * @brief   TIM2 PWM 输出驱动头文件
  * @version V1.0
  * @date    2026-09-20
  ******************************************************************************
  * @history
  *   V1.0  2026-09-20  首次发布(注释规范化)
  ******************************************************************************
  */
#ifndef __PWM_H
#define __PWM_H

void PWM_Init(void);
void PWM_SetCompare2(uint16_t Compare);
void PWM_SetCompare3(uint16_t Compare);

#endif
