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
 * 并清标志。四组变量均为跨执行流共享, 一律 volatile:
 * 主循环每轮轮询 bit15 完成标志, 若编译器把它缓存进寄存器, 优化后
 * (-O1/-O2)会陷入"ISR已组好帧但主循环看不见"的死等。
 * 主循环把缓冲交给解析函数(只读拷贝)时可显式去限定:
 *   Parse_Command_Frame((const char *)USART_RX_BUF, len); */
extern volatile u8  USART_RX_BUF[USART_REC_LEN];  /* 串口1帧内容缓冲(不含帧尾\r\n) */
extern volatile u16 USART_RX_STA;                 /* 串口1接收状态机(位定义见usart.c) */
extern volatile u8  USART3_RX_BUF[USART_REC_LEN]; /* 串口3帧内容缓冲 */
extern volatile u16 USART3_RX_STA;                /* 串口3接收状态机 */

/* ==================== 初始化接口 ==================== */
void uart_init(u32 bound);    /* USART1初始化(PA9/PA10, 9600 8N1, RXNE中断) */
void uart3_init(u32 bound);   /* USART3初始化(PB10/PB11, 9600 8N1, RXNE中断) */

#endif
