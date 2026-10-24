/**
  ******************************************************************************
  * @file    System/PWM.h
  * @brief   TIM2 PWM 输出驱动头文件
  * @author  LCW
  * @version V1.1
  * @date    2026-10-25
  * @note    TIM2 PWM（舵机CH2/电机CH3共用时基）对外接口
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
