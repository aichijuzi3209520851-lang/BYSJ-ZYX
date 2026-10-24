/**
  ******************************************************************************
  * @file    System/usart.c
  * @brief   串口驱动实现(USART1 / USART3)
  * @version V1.0
  * @date    2026-09-20
  * @note    USART1: PA9(TX)/PA10(RX); USART3: PB10(TX)/PB11(RX); 8N1, 波特率9600
  *          printf 重定向到 USART1(禁用半主机模式)
  *          中断接收: 以 0x0D 0x0A(回车换行) 作为一帧结束标志
  ******************************************************************************
  * @history
  *   V1.0  2026-09-20  首次发布(注释规范化)
  ******************************************************************************
  */
#include "sys.h"
#include "usart.h"
#include <string.h>   /* memcpy(ISR命令拷贝) */

#ifdef USE_FREERTOS
/* 命令队列实例(main中创建前ISR可能触发, 判空保护) */
QueueHandle_t xCmdQueue = NULL;
#endif

/* printf重定向支持(禁用半主机) */
#if 1
#pragma import(__use_no_semihosting)

/* 标准库需要的支持结构体 */
struct __FILE
{
	int handle;
};

FILE __stdout;

/* 避免使用半主机模式下的_sys_exit */
void _sys_exit(int x)
{
	x = x;
}

/**
  * @brief  重定向fputc, 使printf输出到USART1
  * @param  ch: 待发送的字符
  * @param  f: 文件指针(未使用)
  * @retval 发送的字符
  * @note   阻塞等待发送数据寄存器为空
  */
int fputc(int ch, FILE *f)
{
	while((USART1->SR&0X40)==0);	//循环等待,直到发送寄存器为空
	USART1->DR = (u8) ch;
	return ch;
}
#endif


#if EN_USART1_RX   /* 若使能串口1接收 */
u8 USART_RX_BUF[USART_REC_LEN];     //串口1接收缓存
/* 接收状态标志 bit 定义
 * bit15:  接收完成标志
 * bit14:  已收到0x0d
 * bit13~0: 收到有效字节数
 */
u16 USART_RX_STA=0;       //串口1接收状态标志

#if EN_USART3_RX   /* 若使能串口3接收 */
u8 USART3_RX_BUF[USART_REC_LEN];     //串口3接收缓存
u16 USART3_RX_STA=0;       //串口3接收状态标志
#endif

/**
  * @brief  USART1初始化(PA9/PA10, 8N1), 并使能接收中断
  * @param  bound: 波特率(如9600)
  * @retval 无
  */
void uart_init(u32 bound){
	GPIO_InitTypeDef GPIO_InitStructure;
	USART_InitTypeDef USART_InitStructure;
	NVIC_InitTypeDef NVIC_InitStructure;

	RCC_APB2PeriphClockCmd(RCC_APB2Periph_USART1|RCC_APB2Periph_GPIOA, ENABLE);	//使能USART1与GPIOA时钟

	//USART1_TX   GPIOA.9
	GPIO_InitStructure.GPIO_Pin = GPIO_Pin_9; //PA.9
	GPIO_InitStructure.GPIO_Speed = GPIO_Speed_50MHz;
	GPIO_InitStructure.GPIO_Mode = GPIO_Mode_AF_PP;	//复用推挽输出
	GPIO_Init(GPIOA, &GPIO_InitStructure);//初始化GPIOA.9

	//USART1_RX	GPIOA.10
	GPIO_InitStructure.GPIO_Pin = GPIO_Pin_10;//PA10
	GPIO_InitStructure.GPIO_Mode = GPIO_Mode_IN_FLOATING;//浮空输入
	GPIO_Init(GPIOA, &GPIO_InitStructure);//初始化GPIOA.10

	//USART1 NVIC配置
	NVIC_InitStructure.NVIC_IRQChannel = USART1_IRQn;
	NVIC_InitStructure.NVIC_IRQChannelPreemptionPriority=6 ;//抢占优先级6(RTOS下须>=MAX_SYSCALL=5)
	NVIC_InitStructure.NVIC_IRQChannelSubPriority = 1;		//子优先级1
	NVIC_InitStructure.NVIC_IRQChannelCmd = ENABLE;			//使能IRQ通道
	NVIC_Init(&NVIC_InitStructure);	//根据指定参数初始化NVIC寄存器

	//USART初始化配置
	USART_InitStructure.USART_BaudRate = bound;//波特率
	USART_InitStructure.USART_WordLength = USART_WordLength_8b;//8位数据格式
	USART_InitStructure.USART_StopBits = USART_StopBits_1;//一个停止位
	USART_InitStructure.USART_Parity = USART_Parity_No;//无奇偶校验位
	USART_InitStructure.USART_HardwareFlowControl = USART_HardwareFlowControl_None;//无硬件流控
	USART_InitStructure.USART_Mode = USART_Mode_Rx | USART_Mode_Tx;	//收发模式

	USART_Init(USART1, &USART_InitStructure); //初始化串口1
	USART_ITConfig(USART1, USART_IT_RXNE, ENABLE);//使能接收中断
	USART_Cmd(USART1, ENABLE);                    //使能串口1

}

#ifdef USE_FREERTOS
/**
  * @brief  帧完成后向命令队列投递消息(ISR内调用, 两串口共用)
  * @param  src: 来源串口(1或3)
  * @param  buf: 帧缓冲区(不含\r\n)
  * @param  sta: 对应的接收状态标志(用于取帧长)
  * @retval 无
  * @note   队列满时丢弃本帧(命令为短突发, 不会持续满); 投递后清除帧标志
  */
static void USART_DispatchCommand(uint8_t src, u8 *buf, u16 sta)
{
    BaseType_t xHigherPriorityTaskWoken = pdFALSE;
    CmdMsg_t   msg;
    u16        len = sta & 0x3FFF;

    if (xCmdQueue == NULL)                 /* 队列尚未创建(启动瞬间), 丢弃 */
    {
        return;
    }

    msg.src = src;
    msg.len = (len > CMD_PAYLOAD_MAX) ? CMD_PAYLOAD_MAX : len;
    memcpy(msg.data, buf, msg.len);

    xQueueSendFromISR(xCmdQueue, &msg, &xHigherPriorityTaskWoken);
    portYIELD_FROM_ISR(xHigherPriorityTaskWoken);
}
#endif

/**
  * @brief  USART1中断服务函数(接收帧数据)
  * @param  无
  * @retval 无
  * @note   以0x0d 0x0a作为帧结束标志; RTOS态帧完成即投递命令队列(毫秒级响应)
  */
void USART1_IRQHandler(void)
{
	u8 Res;
	if(USART_GetITStatus(USART1, USART_IT_RXNE) != RESET)  //接收中断(数据以0x0d 0x0a结尾)
	{
		Res =USART_ReceiveData(USART1);	//读取接收到的数据

		if((USART_RX_STA&0x8000)==0)//接收未完成
		{
			if(USART_RX_STA&0x4000)//已收到0x0d
			{
				if(Res!=0x0a)USART_RX_STA=0;//校验错误,重新开始
				else                        //接收完成
				{
				#ifdef USE_FREERTOS
					USART_DispatchCommand(1, USART_RX_BUF, USART_RX_STA);
					USART_RX_STA=0;         //帧已被消费, 立即重置接收
				#else
					USART_RX_STA|=0x8000;   //裸机: 置完成标志, 主循环查询
				#endif
				}
			}
			else //还没收到0X0D
			{
				if(Res==0x0d)USART_RX_STA|=0x4000;
				else
				{
					USART_RX_BUF[USART_RX_STA&0X3FFF]=Res ;
					USART_RX_STA++;
					if(USART_RX_STA>(USART_REC_LEN-1))USART_RX_STA=0;//接收数据溢出,重新开始接收
				}
			}
		}
	}
}

#if EN_USART3_RX   /* 若使能串口3接收 */
/**
  * @brief  USART3初始化(PB10/PB11, 8N1), 并使能接收中断
  * @param  bound: 波特率(如9600)
  * @retval 无
  */
void uart3_init(u32 bound){
	GPIO_InitTypeDef GPIO_InitStructure;
	USART_InitTypeDef USART_InitStructure;
	NVIC_InitTypeDef NVIC_InitStructure;

	RCC_APB2PeriphClockCmd(RCC_APB2Periph_GPIOB, ENABLE);	//使能GPIOB时钟
	RCC_APB1PeriphClockCmd(RCC_APB1Periph_USART3, ENABLE);	//使能USART3时钟

	//USART3_TX   GPIOB.10
	GPIO_InitStructure.GPIO_Pin = GPIO_Pin_10; //PB.10
	GPIO_InitStructure.GPIO_Speed = GPIO_Speed_50MHz;
	GPIO_InitStructure.GPIO_Mode = GPIO_Mode_AF_PP;	//复用推挽输出
	GPIO_Init(GPIOB, &GPIO_InitStructure);//初始化GPIOB.10

	//USART3_RX	GPIOB.11
	GPIO_InitStructure.GPIO_Pin = GPIO_Pin_11;//PB11
	GPIO_InitStructure.GPIO_Mode = GPIO_Mode_IN_FLOATING;//浮空输入
	GPIO_Init(GPIOB, &GPIO_InitStructure);//初始化GPIOB.11

	//USART3 NVIC配置
	NVIC_InitStructure.NVIC_IRQChannel = USART3_IRQn;
	NVIC_InitStructure.NVIC_IRQChannelPreemptionPriority=6 ;//抢占优先级6(RTOS下须>=MAX_SYSCALL=5)
	NVIC_InitStructure.NVIC_IRQChannelSubPriority = 2;		//子优先级2
	NVIC_InitStructure.NVIC_IRQChannelCmd = ENABLE;			//使能IRQ通道
	NVIC_Init(&NVIC_InitStructure);	//根据指定参数初始化NVIC寄存器

	//USART初始化配置
	USART_InitStructure.USART_BaudRate = bound;//波特率
	USART_InitStructure.USART_WordLength = USART_WordLength_8b;//8位数据格式
	USART_InitStructure.USART_StopBits = USART_StopBits_1;//一个停止位
	USART_InitStructure.USART_Parity = USART_Parity_No;//无奇偶校验位
	USART_InitStructure.USART_HardwareFlowControl = USART_HardwareFlowControl_None;//无硬件流控
	USART_InitStructure.USART_Mode = USART_Mode_Rx | USART_Mode_Tx;	//收发模式

	USART_Init(USART3, &USART_InitStructure); //初始化串口3
	USART_ITConfig(USART3, USART_IT_RXNE, ENABLE);//使能接收中断
	USART_Cmd(USART3, ENABLE);                    //使能串口3

}

/**
  * @brief  USART3中断服务函数(接收帧数据)
  * @param  无
  * @retval 无
  * @note   与USART1共用同一指令解析入口; RTOS态帧完成即投递命令队列
  */
void USART3_IRQHandler(void)
{
	u8 Res;
	if(USART_GetITStatus(USART3, USART_IT_RXNE) != RESET)  //接收中断
	{
		Res =USART_ReceiveData(USART3);	//读取接收到的数据

		if((USART3_RX_STA&0x8000)==0)//接收未完成
		{
			if(USART3_RX_STA&0x4000)//已收到0x0d
			{
				if(Res!=0x0a)USART3_RX_STA=0;//校验错误,重新开始
				else                          //接收完成
				{
				#ifdef USE_FREERTOS
					USART_DispatchCommand(3, USART3_RX_BUF, USART3_RX_STA);
					USART3_RX_STA=0;         //帧已被消费, 立即重置接收
				#else
					USART3_RX_STA|=0x8000;   //裸机: 置完成标志, 主循环查询
				#endif
				}
			}
			else //还没收到0X0D
			{
				if(Res==0x0d)USART3_RX_STA|=0x4000;
				else
				{
					USART3_RX_BUF[USART3_RX_STA&0X3FFF]=Res ;
					USART3_RX_STA++;
					if(USART3_RX_STA>(USART_REC_LEN-1))USART3_RX_STA=0;//接收数据溢出,重新开始接收
				}
			}
		}
	}
}
#endif
#endif
