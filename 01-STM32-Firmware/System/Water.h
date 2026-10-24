/**
  ******************************************************************************
  * @file    System/Water.h
  * @brief   D2-5-2K 电容式土壤湿度传感器驱动头文件
  * @author  周颖暄
  * @version V1.1
  * @date    2026-09-20
  * @note    替代原水位传感器; 模块AO输出0~3.0V模拟量, 读数与湿度成反比
  *          接口: AO->PA0, VCC->3.3~5.5V, GND->GND
  ******************************************************************************
  * @history
  *   V1.0  2026-09-20  首次发布, 替代原水位传感器驱动
  ******************************************************************************
  */
#ifndef _Water_H
#define _Water_H

#include "stm32f10x.h"

/* 校准值（12 位 ADC）
 * 说明书示例（10 位 ADC）：空气 520 / 水中 260
 * 换算到 12 位约 520*4=2080 / 260*4=1040
 * !!! 请按你的模块串口打印的 raw 值实际校准后修改 !!! */
#define SOIL_AIR_ADC      2080    /* 干燥(空气中)ADC读数 */
#define SOIL_WATER_ADC    1040    /* 100%湿度(水中)ADC读数 */

/**
  * @brief  ADC1初始化(PA0模拟输入, 单通道单次转换)
  * @param  无
  * @retval 无
  */
void     ADC1_Init(void);

/**
  * @brief  读取指定通道ADC原始值
  * @param  channel: ADC通道号
  * @retval 0~4095
  */
uint16_t ADC1_GetValue(uint8_t channel);

/**
  * @brief  获取土壤湿度百分比
  * @param  无
  * @retval 0~100%, 越大越湿
  */
uint8_t  Soil_GetMoisturePercent(void);

#endif
