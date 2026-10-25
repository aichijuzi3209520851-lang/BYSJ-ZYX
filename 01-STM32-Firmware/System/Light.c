/**
  ******************************************************************************
  * @file    System/Light.c
  * @brief   光敏电阻模块(光照强度)驱动实现
  * @author  LCW
  * @version V1.0
  * @date    2026-10-25
  * @note    硬件连接：光敏模块AO -> PB1(ADC12_IN9), VCC/GND接电源轨
  *          复用Water.c的通用ADC1_GetValue()(单通道软件触发, 阻塞~60us/次)
  *          调用上下文：仅传感器任务(周期1.5s), 8次均值由任务侧完成
  ******************************************************************************
  * @history
  *   V1.0  2026-10-25  首次发布(采集光照强度百分比, 不联动蜂鸣器)
  ******************************************************************************
  */
#include "stm32f10x.h"
#include "Light.h"
#include "Water.h"      /* 复用ADC1外设使能与通用ADC1_GetValue() */

/**
  * @brief  光敏引脚初始化(PB1模拟输入)
  * @param  无
  * @retval 无
  * @note   ADC1外设已在ADC1_Init()(Water.c)中使能并校准, 此处仅配引脚;
  *         ADC通道在每次ADC1_GetValue()里动态切换, 无需在此配置
  */
void Light_Init(void)
{
    GPIO_InitTypeDef GPIO_InitStructure;

    RCC_APB2PeriphClockCmd(RCC_APB2Periph_GPIOB, ENABLE);

    /* PB1 = ADC12_IN9, 模拟输入(无上下拉, 直连模块AO) */
    GPIO_InitStructure.GPIO_Pin  = GPIO_Pin_1;
    GPIO_InitStructure.GPIO_Mode = GPIO_Mode_AIN;
    GPIO_Init(GPIOB, &GPIO_InitStructure);
}

/**
  * @brief  读取光照ADC原始值
  * @param  无
  * @retval 0~4095(12位)
  * @note   软件触发阻塞式转换, 与土壤采样共用ADC1, 采样率极低(1.5s周期)无竞争
  */
uint16_t Light_GetRaw(void)
{
    return ADC1_GetValue(ADC_Channel_9);
}

/**
  * @brief  计算光照强度百分比
  * @param  无
  * @retval 0~100%, 数值越大代表光照越强
  * @note   映射公式: 光照% = (raw - LIGHT_DARK_ADC) /
  *                    (LIGHT_BRIGHT_ADC - LIGHT_DARK_ADC) * 100
  *         默认方向"光越强AO电压越高"; 若模块相反, 交换Light.h两个校准宏即可
  */
uint8_t Light_GetPercent(void)
{
    uint16_t raw;
    int32_t  percent;

    raw = Light_GetRaw();

    if (raw <= LIGHT_DARK_ADC)    return 0;    /* 比校准暗点还暗 -> 0% */
    if (raw >= LIGHT_BRIGHT_ADC)  return 100;  /* 比校准亮点还亮 -> 100% */

    percent = (int32_t)(raw - LIGHT_DARK_ADC) * 100L
              / (LIGHT_BRIGHT_ADC - LIGHT_DARK_ADC);
    return (uint8_t)percent;
}
