/**
  ******************************************************************************
  * @file    System/DHT11.h
  * @brief   DHT11 温湿度传感器驱动头文件
  * @version V1.0
  * @date    2026-09-20
  * @note    数据脚: PA15(需释放JTAG); 模块供电3.3~5.5V
  ******************************************************************************
  * @history
  *   V1.0  2026-09-20  首次发布(注释规范化)
  ******************************************************************************
  */
#ifndef __DHT11_H
#define __DHT11_H 
#include "sys.h"   

/* IO方向设置(位带操作) */
#define	DHT11_DQ_OUT PAout(15) //数据端口输出 PA15 
#define	DHT11_DQ_IN  PAin(15)  //数据端口输入 PA15

#define DHT11_GPIO_PORT    	GPIOA			              //数据口GPIO端口
#define DHT11_GPIO_CLK 	    RCC_APB2Periph_GPIOA		//GPIO端口时钟
#define DHT11_GPIO_PIN		GPIO_Pin_15			        //数据引脚

u8 DHT11_Init(void);          //初始化DHT11
u8 DHT11_Read_Data(u8 *temp,u8 *humi); //读取温湿度数据
u8 DHT11_Read_Byte(void);     //读取一个字节
u8 DHT11_Read_Bit(void);      //读取一个位
u8 DHT11_Check(void);         //检测DHT11是否应答
void DHT11_Rst(void);         //复位DHT11    
#endif
