/**
  ******************************************************************************
  * @file    System/usart.h
  * @brief   串口驱动头文件(USART1/USART3)
  * @author  LCW
  * @version V2.0
  * @date    2026-10-25
  * @note    USART_REC_LEN: 单帧最大字节数; 帧结束标志为0x0D 0x0A
  *          FreeRTOS版: 帧完成时ISR直接投递命令消息到xCmdQueue(毫秒级响应),
  *          中断优先级为6(>= configMAX_SYSCALL_INTERRUPT_PRIORITY=5, 可安全
  *          调用FromISR系列API)
  ******************************************************************************
  * @history
  *   V1.0  2026-09-20  首次发布(注释规范化)
  *   V2.0  2026-10-25  RTOS版: 中断优先级3→6; 新增命令消息类型与命令队列
  ******************************************************************************
  */
#ifndef __USART_H
#define __USART_H
#include "stdio.h"
#include "sys.h"

#ifdef USE_FREERTOS
#include "FreeRTOS.h"
#include "queue.h"
#endif

#define USART_REC_LEN  			200  	//接收缓存最大字节数 200
#define EN_USART1_RX 			1		//使能(1)/禁止(0)串口1接收
#define EN_USART3_RX 			1		//使能(1)/禁止(0)串口3接收

/* 命令帧载荷最大长度(实际指令<=7字节, 预留裕量) */
#define CMD_PAYLOAD_MAX         24

/* 命令消息: ISR→通信任务(标记来源串口与帧内容) */
typedef struct
{
    uint8_t src;                        /* 1=USART1, 3=USART3 */
    uint8_t len;                        /* 有效字节数 */
    char    data[CMD_PAYLOAD_MAX];      /* 帧内容(不含\r\n) */
} CmdMsg_t;

extern u8  USART_RX_BUF[USART_REC_LEN]; //串口1接收缓存
extern u16 USART_RX_STA;         		//串口1接收状态标志
extern u8  USART3_RX_BUF[USART_REC_LEN]; //串口3接收缓存
extern u16 USART3_RX_STA;         		//串口3接收状态标志

#ifdef USE_FREERTOS
extern QueueHandle_t xCmdQueue;         //命令队列(ISR投递, 通信任务消费)
#endif

void uart_init(u32 bound);
void uart3_init(u32 bound);
#endif
