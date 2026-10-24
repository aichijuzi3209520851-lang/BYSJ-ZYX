/**
  ******************************************************************************
  * @file    System/Water.c
  * @brief   D2-5-2K 电容式土壤湿度传感器驱动实现
  * @author  周颖暄
  * @version V1.1
  * @date    2026-09-20
  * @note    硬件接口: AO -> PA0(ADC1通道0), 供电 3.3~5.5V
  *          模块输出与土壤湿度成反比(越干读数越大), 使用前先完成两点校准
  ******************************************************************************
  * @history
  *   V1.0  2026-09-20  首次发布, 替代原水位传感器驱动
  ******************************************************************************
  */
#include "Water.h"

/**
  * @brief  ADC1初始化(用于采集PA0模拟量)
  * @param  无
  * @retval 无
  * @note   PA0配置为模拟输入; 单通道、单次转换、12位右对齐; 使能后执行自校准
  */
void ADC1_Init(void)
{
    ADC_InitTypeDef ADC_InitStructure;
    GPIO_InitTypeDef GPIO_InitStructure;

    // 使能时钟
    RCC_APB2PeriphClockCmd(RCC_APB2Periph_ADC1 | RCC_APB2Periph_GPIOA, ENABLE);

    // PA0 作为 ADC 输入
    GPIO_InitStructure.GPIO_Pin = GPIO_Pin_0;
    GPIO_InitStructure.GPIO_Mode = GPIO_Mode_AIN; // 模拟输入
    GPIO_Init(GPIOA, &GPIO_InitStructure);

    // ADC 初始化
    ADC_DeInit(ADC1);
    ADC_InitStructure.ADC_Mode = ADC_Mode_Independent;
    ADC_InitStructure.ADC_ScanConvMode = DISABLE;          // 单通道
    ADC_InitStructure.ADC_ContinuousConvMode = DISABLE;    // 单次转换
    ADC_InitStructure.ADC_ExternalTrigConv = ADC_ExternalTrigConv_None; // 软件触发
    ADC_InitStructure.ADC_DataAlign = ADC_DataAlign_Right;
    ADC_InitStructure.ADC_NbrOfChannel = 1;
    ADC_Init(ADC1, &ADC_InitStructure);

    // 使能 ADC
    ADC_Cmd(ADC1, ENABLE);

    // ADC 校准
    ADC_ResetCalibration(ADC1);
    while(ADC_GetResetCalibrationStatus(ADC1));
    ADC_StartCalibration(ADC1);
    while(ADC_GetCalibrationStatus(ADC1));
}

/**
  * @brief  读取指定ADC通道的原始转换值
  * @param  channel: ADC通道号(如ADC_Channel_0)
  * @retval 转换结果, 范围0~4095(12位)
  * @note   软件触发方式, 阻塞等待转换完成
  */
uint16_t ADC1_GetValue(uint8_t channel)
{
    // 配置通道
    ADC_RegularChannelConfig(ADC1, channel, 1, ADC_SampleTime_55Cycles5);

    // 软件触发转换
    ADC_SoftwareStartConvCmd(ADC1, ENABLE);

    // 等待转换完成
    while(!ADC_GetFlagStatus(ADC1, ADC_FLAG_EOC));

    // 返回结果
    return ADC_GetConversionValue(ADC1);
}

/**
  * @brief  计算土壤湿度百分比
  * @param  无
  * @retval 土壤湿度百分比, 范围0~100%, 数值越大代表土壤越湿
  * @note   映射公式: 湿度% = (SOIL_AIR_ADC - 当前值) / (SOIL_AIR_ADC - SOIL_WATER_ADC) * 100
  *         读数越大代表越干, 使用前须按实际模块校准 SOIL_AIR_ADC / SOIL_WATER_ADC
  */
uint8_t Soil_GetMoisturePercent(void)
{
    uint16_t raw;
    int32_t  percent;

    raw = ADC1_GetValue(ADC_Channel_0);

    if (raw >= SOIL_AIR_ADC)   return 0;    /* 比空气中还干 -> 0% */
    if (raw <= SOIL_WATER_ADC) return 100;  /* 浸水状态 -> 100% */

    percent = (int32_t)(SOIL_AIR_ADC - raw) * 100L
              / (SOIL_AIR_ADC - SOIL_WATER_ADC);
    return (uint8_t)percent;
}
