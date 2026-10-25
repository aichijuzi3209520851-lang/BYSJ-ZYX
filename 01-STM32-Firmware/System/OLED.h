/**
  ******************************************************************************
  * @file    System/OLED.h
  * @brief   OLED(SSD1306, 128x64)驱动头文件
  * @version V1.0
  * @date    2026-09-20
  * @note    Line范围1~4, Column范围1~16(8x16字库)
  ******************************************************************************
  * @history
  *   V1.0  2026-09-20  首次发布(注释规范化)
  ******************************************************************************
  */
#ifndef __OLED_H
#include "stdint.h"
#define __OLED_H

void OLED_Init(void);
void OLED_DisplayOn(void);   /* 显示保活: 重发充电泵+显示开(防供电瞬断/干扰误关后灭屏) */
void OLED_Clear(void);
void OLED_ShowChar(uint8_t Line, uint8_t Column, char Char);
void OLED_ShowString(uint8_t Line, uint8_t Column, char *String);
void OLED_ShowNum(uint8_t Line, uint8_t Column, uint32_t Number, uint8_t Length);
void OLED_ShowSignedNum(uint8_t Line, uint8_t Column, int32_t Number, uint8_t Length);
void OLED_ShowHexNum(uint8_t Line, uint8_t Column, uint32_t Number, uint8_t Length);
void OLED_ShowBinNum(uint8_t Line, uint8_t Column, uint32_t Number, uint8_t Length);

#endif

