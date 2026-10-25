/**
  ******************************************************************************
  * @file    System/DHT11.c
  * @brief   DHT11 温湿度传感器驱动实现(单总线协议)
  * @author  LCW
  * @version V1.2
  * @date    2026-10-25
  * @note    ── 单总线时序概要 ────────────────────────────────────────
  *          起始  : 主机拉低>=18ms → 拉高20~40us
  *          应答  : DHT11拉低40~80us → 拉高40~80us
  *          数据  : 每位以50us低电平起始, 高电平宽26~28us=0 / 70us=1
  *          帧格式: 40位 = 湿度整数+湿度小数+温度整数+温度小数+校验和
  *          ── 实现要点 ──────────────────────────────────────────────
  *          1. PA15为JTAG复用脚, IO_OUT/IO_IN中均执行SWJ_JTAGDisable
  *             (开AFIO时钟)释放之
  *          2. 所有等待循环带retry<100上限(约100us), 器件不在位时
  *             返回错误而非死等——拔线不会卡死主循环
  *          3. DHT11_Read_Data含校验和验证, 校验失败不更新输出
  *          4. 器件采样率<=1Hz: main.c每1.5s读一次满足间隔要求
  ******************************************************************************
  * @history
  *   V1.0  2026-09-20  首次发布(注释规范化)
  *   V1.2  2026-10-25  交接级注释升级(纯注释零代码变更)
  ******************************************************************************
  */
#include "dht11.h"
#include "delay.h"

/**
  * @brief  配置DHT11数据脚为输出模式(推挽)
  * @param  无
  * @retval 无
  * @note   同时执行SWJ_JTAGDisable以释放PA15(JTAG复用→普通GPIO),
  *         AFIO时钟必须先开(重映射寄存器挂在AFIO上)
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
  * @note   读取阶段主机释放总线由DHT11驱动, 浮空输入采样;
  *         SWJ_JTAGDisable重复执行无副作用(幂等)
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
  * @note   时序: 拉低>=18ms(取20ms) → 释放拉高20~40us(取30us)
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
  * @retval 0:应答正常; 1:未检测到DHT11(超时100us)
  * @note   应答时序: DHT11先拉低40~80us, 再拉高40~80us;
  *         两段等待均带retry上限, 器件不在位时快速返回1
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
  * @note   判位方法: 等待50us低电平起始段结束后延时40us采样数据线——
  *         此时"0"的高电平(26~28us)已结束读得0, "1"的高电平(~70us)
  *         仍在读得1; 所有等待带retry<100超时上限
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
  * @brief  从DHT11读取一个字节(MSB先行)
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
  * @brief  从DHT11读取一次温湿度数据(40位帧)
  * @param  temp: 温度值输出指针(整数部分, 范围约0~50°C)
  * @param  humi: 湿度值输出指针(整数部分, 范围约20%~90%RH)
  * @retval 0:读取成功; 1:读取失败(无应答)
  * @note   校验和 = 湿度整数+湿度小数+温度整数+温度小数(即buf[0..3]
  *         之和)==buf[4], 仅校验通过才更新输出——失败时上层保留旧值
  *         并显示"--"; 执行耗时约25ms(阻塞, 裸机主循环可接受)
  */
u8 DHT11_Read_Data(u8 *temp,u8 *humi)
{
 	u8 buf[5];
	u8 i;
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
		}
	}else return 1;
	return 0;
}
/**
  * @brief  DHT11初始化(复位并检测器件是否存在)
  * @param  无
  * @retval 0:器件存在; 1:器件不存在(仅提示, 不阻塞后续流程)
  */
u8 DHT11_Init(void)
{


	DHT11_Rst();  //复位DHT11
	return DHT11_Check();//等待DHT11的回应
}
