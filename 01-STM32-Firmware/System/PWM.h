/**
  ******************************************************************************
  * @file    System/PWM.h
  * @brief   TIM2 PWM 输出驱动头文件(舵机/电机共用同一时基)
  * @author  LCW
  * @version V1.2
  * @date    2026-10-25
  * @note    CH2(PA1)->舵机(SetCompare2), CH3(PA2)->电机(SetCompare3)
  ******************************************************************************
  * @history
  *   V1.0  2026-09-20  首次发布(注释规范化)
  *   V1.2  2026-10-25  交接级注释升级(纯注释零代码变更)
  ******************************************************************************
  */
#ifndef __PWM_H
#define __PWM_H

void PWM_Init(void);
void PWM_SetCompare2(uint16_t Compare);
void PWM_SetCompare3(uint16_t Compare);

#endif
