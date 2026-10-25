/**
  ******************************************************************************
  * @file    System/DHT11.h
  * @brief   DHT11 温湿度传感器驱动头文件(单总线协议)
  * @author  LCW
  * @version V1.2
  * @date    2026-10-25
  * @note    硬件: 数据脚PA15(上电默认JTAG复用脚, 驱动内已做
  *          SWJ_JTAGDisable重映射释放), 模块供电3.3~5.5V
  *          器件特性: 采样率<=1Hz(两次读取须间隔1s以上, 主循环1.5s满足);
  *          读取失败由上层重试兜底(main.c每轮读取, 失败显示"--")
  ******************************************************************************
  * @history
  *   V1.0  2026-09-20  首次发布(注释规范化)
  *   V1.2  2026-10-25  交接级注释升级(纯注释零代码变更)
  ******************************************************************************
  */
#ifndef __DHT11_H
#define __DHT11_H
#include "sys.h"

/* IO方向切换(位带操作, 单指令原子读写) */
#define	DHT11_DQ_OUT PAout(15) //数据端口输出 PA15
#define	DHT11_DQ_IN  PAin(15)  //数据端口输入 PA15

#define DHT11_GPIO_PORT    	GPIOA			              //数据口GPIO端口
#define DHT11_GPIO_CLK 	    RCC_APB2Periph_GPIOA		//GPIO端口时钟
#define DHT11_GPIO_PIN		GPIO_Pin_15			        //数据引脚

u8 DHT11_Init(void);          //初始化DHT11(复位+检测应答)
u8 DHT11_Read_Data(u8 *temp,u8 *humi); //读取温湿度数据(含校验和验证)
u8 DHT11_Read_Byte(void);     //读取一个字节(8位, MSB先行)
u8 DHT11_Read_Bit(void);      //读取一个位(按时序判0/1)
u8 DHT11_Check(void);         //检测DHT11是否应答
void DHT11_Rst(void);         //复位DHT11(主机起始信号)
#endif
