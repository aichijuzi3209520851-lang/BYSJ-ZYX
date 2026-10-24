/**
  ******************************************************************************
  * @file    System/DHT11.c
  * @brief   DHT11 温湿度传感器驱动实现(单总线协议)
  * @author  LCW
  * @version V1.1
  * @date    2026-10-25
  * @note    数据脚: PA15(需先释放JTAG复用); 单总线时序, 带校验和验证
  *
  *          !!! 单次读取耗时预算(交接必读) !!!
  *            主机起始信号      : 20ms 拉低 + 30us 拉高   (DHT11_Rst)
  *            从机应答窗口      : 约 80~160us             (DHT11_Check)
  *            40位数据传输      : 每位110~160us, 共约5ms   (Read_Byte x5)
  *            -------------------------------------------------
  *            单次总计          : 约 25~27ms, 全程CPU忙等
  *          时序容限: '0'高电平26~28us / '1'高电平70us, 上升沿后延时40us采样,
  *          任何>20~30us的任务切换/中断延迟都可能误判位——因此 RTOS 态在
  *          DHT11_Read_Data 外层挂起调度器(见该函数注释), 且器件采样率
  *          上限1Hz(两次读取间隔须>=1s, 本工程周期1.5s满足)
  ******************************************************************************
  * @history
  *   V1.0  2026-09-20  首次发布(注释规范化)
  *   V1.1  2026-10-25  注释交接化升级: 耗时预算/时序容限/RTOS保护说明
  ******************************************************************************
  */
#include "dht11.h"
#include "delay.h"
#ifdef USE_FREERTOS
#include "FreeRTOS.h"
#include "task.h"
#endif

/**
  * @brief  配置DHT11数据脚为输出模式(推挽)
  * @param  无
  * @retval 无
  * @note   同时执行SWJ_JTAGDisable以释放PA15
  */
void DHT11_IO_OUT(void)
{
	GPIO_InitTypeDef  GPIO_InitStructure;
 	
 	RCC_APB2PeriphClockCmd(DHT11_GPIO_CLK|RCC_APB2Periph_AFIO, ENABLE);	 
	GPIO_PinRemapConfig(GPIO_Remap_SWJ_JTAGDisable, ENABLE); 
	
 	GPIO_InitStructure.GPIO_Pin = DHT11_GPIO_PIN;			
 	GPIO_InitStructure.GPIO_Mode = GPIO_Mode_Out_PP; 		 
 	GPIO_InitStructure.GPIO_Speed = GPIO_Speed_50MHz;
 	GPIO_Init(DHT11_GPIO_PORT, &GPIO_InitStructure);				
}

/**
  * @brief  配置DHT11数据脚为输入模式(浮空)
  * @param  无
  * @retval 无
  */
void DHT11_IO_IN(void)
{
	GPIO_InitTypeDef  GPIO_InitStructure;
 	
 	RCC_APB2PeriphClockCmd(DHT11_GPIO_CLK|RCC_APB2Periph_AFIO, ENABLE);	
	GPIO_PinRemapConfig(GPIO_Remap_SWJ_JTAGDisable, ENABLE);    
	
 	GPIO_InitStructure.GPIO_Pin = DHT11_GPIO_PIN;				 
 	GPIO_InitStructure.GPIO_Mode = GPIO_Mode_IN_FLOATING; 		 
 	GPIO_Init(DHT11_GPIO_PORT, &GPIO_InitStructure);				 
}

/**
  * @brief  复位DHT11(主机发起起始信号)
  * @param  无
  * @retval 无
  * @note   拉低DQ至少18ms, 再拉高20~40us
  */
void DHT11_Rst(void)	   
{                 
	DHT11_IO_OUT(); 	//SET OUTPUT
  DHT11_DQ_OUT=0; 	//拉低DQ
  Delay_ms(20);    	//拉低至少18ms
  DHT11_DQ_OUT=1; 	//DQ=1 
	Delay_us(30);     	//主机拉高20~40us
}
/**
  * @brief  等待DHT11应答信号
  * @param  无
  * @retval 0:应答正常; 1:未检测到DHT11
  * @note   DHT11应答: 先拉低40~80us, 再拉高40~80us
  */
u8 DHT11_Check(void) 	   
{   
	u8 retry=0;
	DHT11_IO_IN();//SET INPUT	 
    while (DHT11_DQ_IN&&retry<100)//DHT11会拉低40~80us
	{
		retry++;
		Delay_us(1);
	};	 
	if(retry>=100)return 1;
	else retry=0;
    while (!DHT11_DQ_IN&&retry<100)//DHT11拉低后会再次拉高40~80us
	{
		retry++;
		Delay_us(1);
	};
	if(retry>=100)return 1;	    
	return 0;
}
/**
  * @brief  从DHT11读取一个位
  * @param  无
  * @retval 1或0
  * @note   高电平持续40us后采样: 高电平约26~28us为0, 约70us为1
  */
u8 DHT11_Read_Bit(void) 			 
{
 	u8 retry=0;
	while(DHT11_DQ_IN&&retry<100)//等待变为低电平
	{
		retry++;
		Delay_us(1);
	}
	retry=0;
	while(!DHT11_DQ_IN&&retry<100)//等待变高电平
	{
		retry++;
		Delay_us(1);
	}
	Delay_us(40);//等待40us
	if(DHT11_DQ_IN)return 1;
	else return 0;		   
}
/**
  * @brief  从DHT11读取一个字节
  * @param  无
  * @retval 读到的8位数据
  */
u8 DHT11_Read_Byte(void)    
{        
	u8 i,dat;
	dat=0;
	for (i=0;i<8;i++) 
	{
		dat<<=1; 
		dat|=DHT11_Read_Bit();
  }						    
  return dat;
}
/**
  * @brief  从DHT11读取一次温湿度数据(40位)
  * @param  temp: 温度值输出指针(范围约0~50°C)
  * @param  humi: 湿度值输出指针(范围约20%~90%RH)
  * @retval 0:读取成功; 1:读取失败
  * @note   仅当校验和(buf[0]+buf[1]+buf[2]+buf[3]==buf[4])通过时才更新输出
  *          RTOS态: 读取期间挂起调度器, 防止任务切换打断us级时序;
  *          串口/SysTick中断照常运行(每字节ISR仅2~5us, 对20~30us
  *          采样容限无威胁)。禁止改用taskENTER_CRITICAL包整段读取:
  *          25ms关中断将丢失约24字节串口数据(9600波特)
  */
u8 DHT11_Read_Data(u8 *temp,u8 *humi)
{
 	u8 buf[5];
	u8 i;
	u8 ret = 1;
#ifdef USE_FREERTOS
	vTaskSuspendAll();          /* 禁止任务切换(中断保持运行) */
#endif
	DHT11_Rst();
	if(DHT11_Check()==0)
	{
		for(i=0;i<5;i++)//读取40位数据
		{
			buf[i]=DHT11_Read_Byte();
		}
		if((buf[0]+buf[1]+buf[2]+buf[3])==buf[4])
		{
			*humi=buf[0];
			*temp=buf[2];
			ret = 0;
		}
	}
#ifdef USE_FREERTOS
	xTaskResumeAll();           /* 恢复调度 */
#endif
	return ret;
}
/**
  * @brief  DHT11初始化(复位并检测器件是否存在)
  * @param  无
  * @retval 0:器件存在; 1:器件不存在
  */
u8 DHT11_Init(void)
{	 
 	
			    
	DHT11_Rst();  //复位DHT11
	return DHT11_Check();//等待DHT11的回应
} 







