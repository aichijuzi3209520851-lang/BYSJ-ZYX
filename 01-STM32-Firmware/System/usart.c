/**
  ******************************************************************************
  * @file    System/usart.c
  * @brief   串口驱动实现(USART1 / USART3)
  * @author  LCW
  * @version V1.2
  * @date    2026-10-25
  * @note    ┌── 接收数据流 ──────────────────────────────────────────────┐
  *          │ 字节到达 → RXNE中断 → USARTx_IRQHandler 逐字节组帧         │
  *          │   收到 0x0D 0x0A → USARTx_RX_STA 置 bit15(完成标志)        │
  *          │ 主循环 Process_Serial_Command() 轮询 bit15 → 取帧解析      │
  *          │   → 清标志 → ISR 开始下一帧组包                            │
  *          └───────────────────────────────────────────────────────────┘
  *          状态机 USARTx_RX_STA 位定义:
  *            bit15 : 接收完成标志(一帧就绪, 主循环消费后清0)
  *            bit14 : 已收到0x0D(帧尾第一字节)
  *            bit13~0: 已收到的有效字节数
  *
  *          printf 重定向到 USART1(阻塞逐字节发送, 禁用半主机模式);
  *          协议帧约60字符 @9600bps ≈ 60ms/帧(主循环1.5s周期内可接受)
  *
  *          NVIC说明: 本工程未显式调用 NVIC_PriorityGroupConfig, 依赖
  *          复位默认分组(Group0: 0位抢占/7位子优先级), NVIV_Init传入的
  *          抢占/子优先级最终全部落入子优先级域。本工程仅USART1/3两个
  *          接收中断源, 仲裁无实际影响; 若后续新增中断源, 建议在main()
  *          开头显式配置一次分组(裸机常用Group_2)
  ******************************************************************************
  * @history
  *   V1.0  2026-09-20  首次发布(注释规范化)
  *   V1.2  2026-10-25  交接级注释升级(纯注释零代码变更); 补数据流图/
  *                     状态机位定义/共享变量读写关系
  ******************************************************************************
  */
#include "sys.h"
#include "usart.h"

/* ==================== printf 重定向(USART1) ====================
 * Keil ARMCC 下 printf 走 fputc; #pragma import(__use_no_semihosting)
 * 禁用半主机模式, 避免脱机运行时printf触发BKPT指令卡死 */
#if 1
#pragma import(__use_no_semihosting)

/* 标准库需要的支持结构体(与MicroLIB/标准库对接的最小实现) */
struct __FILE
{
	int handle;
};

FILE __stdout;

/* 避免使用半主机模式下的_sys_exit(程序退出钩子, 嵌入式下永不退出) */
void _sys_exit(int x)
{
	x = x;
}

/**
  * @brief  重定向fputc, 使printf输出到USART1
  * @param  ch: 待发送的字符
  * @param  f: 文件指针(未使用, 标准库签名要求)
  * @retval 发送的字符
  * @note   阻塞发送: 轮询等待TC位(SR bit6, 0x40=发送数据寄存器空)后
  *         写DR。9600bps下单字符约1ms, 一帧协议文本约60ms——主循环
  *         周期1.5s内可接受; 若需更高吞吐可改DMA/中断发送
  */
int fputc(int ch, FILE *f)
{
	while((USART1->SR&0X40)==0);	//循环等待,直到发送数据寄存器为空(TC=1)
	USART1->DR = (u8) ch;           //写入发送数据寄存器(自动清除TC)
	return ch;
}
#endif


#if EN_USART1_RX   /* 若使能串口1接收 */
/* ==================== 串口1 帧缓存与状态机 ====================
 * 读写关系: USART1_IRQHandler(ISR)写, Process_Serial_Command(主循环)读
 * TODO(交接改进): 跨执行流共享, 建议后续补充volatile(见usart.h说明) */
u8 USART_RX_BUF[USART_REC_LEN];     //串口1接收缓存(帧内容, 不含\r\n)

/* 接收状态标志 USART_RX_STA 位定义:
 *   bit15  : 接收完成标志(整帧就绪, 等待主循环消费)
 *   bit14  : 已收到0x0D(帧尾第一字节, 等待0x0A确认)
 *   bit13~0: 已收到的有效字节数(帧长, 最大USART_REC_LEN) */
u16 USART_RX_STA=0;       //串口1接收状态标志

#if EN_USART3_RX   /* 若使能串口3接收 */
u8 USART3_RX_BUF[USART_REC_LEN];     //串口3接收缓存
u16 USART3_RX_STA=0;       //串口3接收状态标志(位定义同串口1)
#endif

/**
  * @brief  USART1初始化(PA9/PA10, 9600 8N1), 并使能RXNE接收中断
  * @param  bound: 波特率(当前9600; 协议帧/上位机均按此波特率)
  * @retval 无
  * @note   引脚: PA9=TX(复用推挽) PA10=RX(浮空输入, USART外设接管)
  *         执行耗时: <0.1ms(纯寄存器配置)
  *         NVIC: 抢占3/子3(工程未显式分组, 实际落子优先级域, 见文件头)
  */
void uart_init(u32 bound){
	GPIO_InitTypeDef GPIO_InitStructure;
	USART_InitTypeDef USART_InitStructure;
	NVIC_InitTypeDef NVIC_InitStructure;

	RCC_APB2PeriphClockCmd(RCC_APB2Periph_USART1|RCC_APB2Periph_GPIOA, ENABLE);	//使能USART1与GPIOA时钟

	//USART1_TX   GPIOA.9: 复用推挽输出(引脚控制权交给USART外设)
	GPIO_InitStructure.GPIO_Pin = GPIO_Pin_9; //PA.9
	GPIO_InitStructure.GPIO_Speed = GPIO_Speed_50MHz;
	GPIO_InitStructure.GPIO_Mode = GPIO_Mode_AF_PP;	//复用推挽输出
	GPIO_Init(GPIOA, &GPIO_InitStructure);//初始化GPIOA.9

	//USART1_RX	GPIOA.10: 浮空输入(由USART外设内部处理电平)
	GPIO_InitStructure.GPIO_Pin = GPIO_Pin_10;//PA10
	GPIO_InitStructure.GPIO_Mode = GPIO_Mode_IN_FLOATING;//浮空输入
	GPIO_Init(GPIOA, &GPIO_InitStructure);//初始化GPIOA.10

	//USART1 NVIC配置(接收中断优先级)
	NVIC_InitStructure.NVIC_IRQChannel = USART1_IRQn;
	NVIC_InitStructure.NVIC_IRQChannelPreemptionPriority=3 ;//抢占优先级3
	NVIC_InitStructure.NVIC_IRQChannelSubPriority = 3;		//子优先级3
	NVIC_InitStructure.NVIC_IRQChannelCmd = ENABLE;			//使能IRQ通道
	NVIC_Init(&NVIC_InitStructure);	//根据指定参数初始化NVIC寄存器

	//USART初始化配置: 9600 8N1 收发模式
	USART_InitStructure.USART_BaudRate = bound;//波特率
	USART_InitStructure.USART_WordLength = USART_WordLength_8b;//8位数据格式
	USART_InitStructure.USART_StopBits = USART_StopBits_1;//一个停止位
	USART_InitStructure.USART_Parity = USART_Parity_No;//无奇偶校验位
	USART_InitStructure.USART_HardwareFlowControl = USART_HardwareFlowControl_None;//无硬件流控
	USART_InitStructure.USART_Mode = USART_Mode_Rx | USART_Mode_Tx;	//收发模式

	USART_Init(USART1, &USART_InitStructure); //初始化串口1
	USART_ITConfig(USART1, USART_IT_RXNE, ENABLE);//使能接收中断(每字节触发一次)
	USART_Cmd(USART1, ENABLE);                    //使能串口1

}

/**
  * @brief  USART1中断服务函数: 逐字节组帧, 以0x0D 0x0A判定帧尾
  * @param  无
  * @retval 无
  * @note   状态机流程(单字节粒度):
  *           未收到0x0D: 普通字节入BUF并计数; 收到0x0D → 置bit14
  *           已收0x0D  : 下一字节==0x0A → 置bit15完成; 否则整帧作废重收
  *           计数溢出(>USART_REC_LEN-1) → 整帧作废重新开始
  *         进中断后GetITStatus读SR + ReceiveData读DR, 顺带清除RXNE/ORE
  *         标志(读SR再读DR的标准清除序列), 溢出不会积压
  *         执行耗时: ~1µs/字节, 9600bps字节间隔~1ms, 无溢出风险
  *         共享变量: USART_RX_BUF/RX_STA(主循环读, 见usart.h的TODO)
  */
void USART1_IRQHandler(void)
{
	u8 Res;
	if(USART_GetITStatus(USART1, USART_IT_RXNE) != RESET)  //接收中断(数据以0x0d 0x0a结尾)
	{
		Res =USART_ReceiveData(USART1);	//读取接收到的数据(读DR同时清RXNE)

		if((USART_RX_STA&0x8000)==0)//接收未完成(bit15=0时才继续组帧)
		{
			if(USART_RX_STA&0x4000)//已收到0x0d(等待帧尾第二字节0x0A)
			{
				if(Res!=0x0a)USART_RX_STA=0;//帧尾校验错误,整帧作废重新开始
				else USART_RX_STA|=0x8000;	//0x0D 0x0A齐 → 置bit15接收完成
			}
			else //还没收到0X0D: 普通数据字节
			{
				if(Res==0x0d)USART_RX_STA|=0x4000;  //收到帧尾第一字节
				else
				{
					USART_RX_BUF[USART_RX_STA&0X3FFF]=Res ;  //字节入缓存(bit13~0为写索引)
					USART_RX_STA++;
					if(USART_RX_STA>(USART_REC_LEN-1))USART_RX_STA=0;//超长整帧作废,重新开始
				}
			}
		}
	}
}

#if EN_USART3_RX   /* 若使能串口3接收 */
/**
  * @brief  USART3初始化(PB10/PB11, 9600 8N1), 并使能RXNE接收中断
  * @param  bound: 波特率(当前9600)
  * @retval 无
  * @note   引脚: PB10=TX(复用推挽)/PB11=RX(浮空输入)
  *         注意: PB10随后在Alarm_Hardware_Init中被改配为红灯LED的
  *         普通推挽输出 → 串口3设计上仅接收不发送(取舍见main.c)
  *         USART3挂APB1总线(与USART1的APB2不同), 时钟使能行不同
  */
void uart3_init(u32 bound){
	GPIO_InitTypeDef GPIO_InitStructure;
	USART_InitTypeDef USART_InitStructure;
	NVIC_InitTypeDef NVIC_InitStructure;

	RCC_APB2PeriphClockCmd(RCC_APB2Periph_GPIOB, ENABLE);	//使能GPIOB时钟
	RCC_APB1PeriphClockCmd(RCC_APB1Periph_USART3, ENABLE);	//使能USART3时钟(APB1, 与USART1不同)

	//USART3_TX   GPIOB.10: 复用推挽输出(后被红灯LED配置覆盖, 仅RX有效)
	GPIO_InitStructure.GPIO_Pin = GPIO_Pin_10; //PB.10
	GPIO_InitStructure.GPIO_Speed = GPIO_Speed_50MHz;
	GPIO_InitStructure.GPIO_Mode = GPIO_Mode_AF_PP;	//复用推挽输出
	GPIO_Init(GPIOB, &GPIO_InitStructure);//初始化GPIOB.10

	//USART3_RX	GPIOB.11: 浮空输入
	GPIO_InitStructure.GPIO_Pin = GPIO_Pin_11;//PB11
	GPIO_InitStructure.GPIO_Mode = GPIO_Mode_IN_FLOATING;//浮空输入
	GPIO_Init(GPIOB, &GPIO_InitStructure);//初始化GPIOB.11

	//USART3 NVIC配置(与USART1同为抢占3/子3, 两中断源互不嵌套)
	NVIC_InitStructure.NVIC_IRQChannel = USART3_IRQn;
	NVIC_InitStructure.NVIC_IRQChannelPreemptionPriority=3 ;//抢占优先级3
	NVIC_InitStructure.NVIC_IRQChannelSubPriority = 3;		//子优先级3
	NVIC_InitStructure.NVIC_IRQChannelCmd = ENABLE;			//使能IRQ通道
	NVIC_Init(&NVIC_InitStructure);	//根据指定参数初始化NVIC寄存器

	//USART初始化配置: 9600 8N1 收发模式
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
  * @brief  USART3中断服务函数: 逐字节组帧(逻辑与USART1完全一致)
  * @param  无
  * @retval 无
  * @note   状态机与USART1_IRQHandler逐句对应, 仅操作USART3_RX_BUF/
  *         USART3_RX_STA。组帧完成后由主循环 Process_Serial_Command
  *         与USART1共用 Parse_Command_Frame 解析, 保证两条串口对
  *         mode-x/DUOJ-x/MOTOR-x 指令行为完全一致
  */
void USART3_IRQHandler(void)
{
	u8 Res;
	if(USART_GetITStatus(USART3, USART_IT_RXNE) != RESET)  //接收中断
	{
		Res =USART_ReceiveData(USART3);	//读取接收到的数据(读DR同时清RXNE)

		if((USART3_RX_STA&0x8000)==0)//接收未完成
		{
			if(USART3_RX_STA&0x4000)//已收到0x0d(等待0x0A)
			{
				if(Res!=0x0a)USART3_RX_STA=0;//帧尾校验错误,重新开始
				else USART3_RX_STA|=0x8000;	//接收完成
			}
			else //还没收到0X0D
			{
				if(Res==0x0d)USART3_RX_STA|=0x4000;
				else
				{
					USART3_RX_BUF[USART3_RX_STA&0X3FFF]=Res ;
					USART3_RX_STA++;
					if(USART3_RX_STA>(USART_REC_LEN-1))USART3_RX_STA=0;//超长整帧作废,重新开始
				}
			}
		}
	}
}
#endif
#endif
