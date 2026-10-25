/**
  ******************************************************************************
  * @file    System/usart.h
  * @brief   串口驱动头文件(USART1/USART3)
  * @author  LCW
  * @version V1.2
  * @date    2026-10-25
  * @note    本头文件对外暴露:
  *          1. 初始化接口  uart_init/uart3_init(9600 8N1 + RXNE接收中断)
  *          2. 接收帧缓存  USARTx_RX_BUF + 状态机标志 USARTx_RX_STA
  *          帧协议: 以 0x0D 0x0A(\r\n) 作为一帧结束标志, 单帧最长
  *          USART_REC_LEN 字节; 主循环轮询 bit15 消费帧(见 main.c 的
  *          Process_Serial_Command), 本文件只负责"组帧+置完成标志"
  ******************************************************************************
  * @history
  *   V1.0  2026-09-20  首次发布(注释规范化)
  *   V1.2  2026-10-25  交接级注释升级(纯注释零代码变更); 补共享变量
  *                     读写关系与volatile改进项说明
  ******************************************************************************
  */
#ifndef __USART_H
#define __USART_H
#include "stdio.h"
#include "sys.h"

/* ==================== 配置宏 ==================== */
#define USART_REC_LEN  			200  	/* 单帧接收缓存最大字节数(指令帧仅6~7字节,
                                           200为冗余上限; 溢出时ISR侧丢弃整帧重收) */
#define EN_USART1_RX 			1		/* 使能(1)/禁止(0)串口1接收: 上位机指令入口 */
#define EN_USART3_RX 			1		/* 使能(1)/禁止(0)串口3接收: 备用指令入口
                                           (PB10 TX被红灯占用, 仅RX有效) */

/* ==================== 接收帧缓存与状态标志(对外导出) ====================
 * 读写关系: USARTx_IRQHandler(ISR侧)写, Process_Serial_Command(主循环)读
 * 并清标志。TODO(交接改进): 两者为跨执行流共享, 建议后续为本组变量补充
 * volatile(裸机单帧消费场景下ARMCC跨函数调用会保守重载, 实际风险低,
 * 但按ISR共享变量纪律应显式声明) */
extern u8  USART_RX_BUF[USART_REC_LEN];  /* 串口1帧内容缓冲(不含帧尾\r\n) */
extern u16 USART_RX_STA;                 /* 串口1接收状态机(位定义见usart.c) */
extern u8  USART3_RX_BUF[USART_REC_LEN]; /* 串口3帧内容缓冲 */
extern u16 USART3_RX_STA;                /* 串口3接收状态机 */

/* ==================== 初始化接口 ==================== */
void uart_init(u32 bound);    /* USART1初始化(PA9/PA10, 9600 8N1, RXNE中断) */
void uart3_init(u32 bound);   /* USART3初始化(PB10/PB11, 9600 8N1, RXNE中断) */

#endif
