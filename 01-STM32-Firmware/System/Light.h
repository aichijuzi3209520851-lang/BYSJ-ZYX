/**
  ******************************************************************************
  * @file    System/Light.h
  * @brief   光敏电阻模块(光照强度)驱动头文件
  * @author  LCW
  * @version V1.0
  * @date    2026-10-25
  * @note    仅采集光照数据, 不参与蜂鸣器等报警联动(用户约定);
  *          模块AO输出模拟量, 接口: AO->PB1, VCC->3.3~5V, GND->GND
  *          后续联动预留: 白色LED建议接PB12(空闲推挽脚), 光照低亮灯
  ******************************************************************************
  * @history
  *   V1.0  2026-10-25  首次发布(采集光照强度百分比)
  ******************************************************************************
  */
#ifndef _Light_H
#define _Light_H

#include "stm32f10x.h"

/* 校准值（12 位 ADC, 0~4095）
 * 默认假设"光越强 AO 电压越高": 全暗约 1000, 强光约 3000
 * !!! 请按模块实测校准: 遮光记下 raw 填 DARK, 手电/日光下记 raw 填 BRIGHT !!!
 * 若你的模块是"光越亮读数越低", 直接交换下面两个值即可反转方向 */
#define LIGHT_DARK_ADC    1000    /* 全暗(遮光)ADC读数 */
#define LIGHT_BRIGHT_ADC  3000    /* 强光(日光/手电)ADC读数 */

/**
  * @brief  光敏引脚初始化(PB1模拟输入; ADC1外设由Water.c的ADC1_Init使能)
  * @param  无
  * @retval 无
  * @note   调用顺序: 须在 ADC1_Init() 之后调用(main.c 已保证)
  */
void     Light_Init(void);

/**
  * @brief  读取光照ADC原始值
  * @param  无
  * @retval 0~4095, 具体方向取决于模块分压设计(见校准宏注释)
  */
uint16_t Light_GetRaw(void);

/**
  * @brief  获取光照强度百分比
  * @param  无
  * @retval 0~100%, 数值越大代表光照越强
  * @note   映射公式: 光照% = (raw - LIGHT_DARK_ADC) /
  *                    (LIGHT_BRIGHT_ADC - LIGHT_DARK_ADC) * 100, 限幅0~100
  */
uint8_t  Light_GetPercent(void);

#endif
