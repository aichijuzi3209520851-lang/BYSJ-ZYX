/**
  ******************************************************************************
  * @file    System/usart.h
  * @brief   串口驱动头文件(USART1/USART3)
  * @version V1.0
  * @date    2026-09-20
  * @note    USART_REC_LEN: 单帧最大字节数; 帧结束标志为0x0D 0x0A
  ******************************************************************************
  * @history
  *   V1.0  2026-09-20  首次发布(注释规范化)
  ******************************************************************************
  */
#ifndef __USART_H
#define __USART_H
#include "stdio.h"
#include "sys.h"

#define USART_REC_LEN  			200  	//接收缓存最大字节数 200
#define EN_USART1_RX 			1		//使能(1)/禁止(0)串口1接收
#define EN_USART3_RX 			1		//使能(1)/禁止(0)串口3接收

extern u8  USART_RX_BUF[USART_REC_LEN]; //串口1接收缓存
extern u16 USART_RX_STA;         		//串口1接收状态标志
extern u8  USART3_RX_BUF[USART_REC_LEN]; //串口3接收缓存
extern u16 USART3_RX_STA;         		//串口3接收状态标志

void uart_init(u32 bound);
void uart3_init(u32 bound);
#endif