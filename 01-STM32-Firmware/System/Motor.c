/**
  ******************************************************************************
  * @file    System/Motor.c
  * @brief   直流电机驱动实现（方向 GPIO + TIM2_CH3 PWM 调速，温室风扇执行器）
  * @author  周颖暄
  * @version V1.1
  * @date    2026-10-25
  * @note    硬件连接（L298N/驱动模块典型接法）：
  *            INA -> PA4、INB -> PA5（方向控制，推挽输出）
  *            ENA -> PA2（TIM2_CH3，PWM调速）
  *          方向真值表：
  *            PA4=1, PA5=0 -> 正转（本工程"正转"即排风方向）
  *            PA4=0, PA5=1 -> 反转
  *            两脚同电平   -> 刹停（本工程未使用）
  *          本工程只用两档：0（停止）与 100（全速排风），
  *          见 main.c 报警控制任务（烟雾联动）与 MOTOR-x 串口指令
  ******************************************************************************
  * @history
  *   V1.0  2026-09-20  首次发布(注释规范化)
  *   V1.1  2026-10-25  注释交接化升级: 方向真值表/调速系数推导/调用上下文
  ******************************************************************************
  */
#include "stm32f10x.h"                  // Device header
#include "PWM.h"

/**
  * @brief  直流电机初始化（方向脚 GPIO + 底层 PWM）
  * @param  无
  * @retval 无
  * @note   调用上下文：main() 初始化阶段；PWM初值CCR=0，上电默认停转
  */
void Motor_Init(void)
{
	/*开启时钟*/
	RCC_APB2PeriphClockCmd(RCC_APB2Periph_GPIOA, ENABLE);		//开启GPIOA的时钟

	/*方向脚 PA4/PA5 配置为推挽输出*/
	GPIO_InitTypeDef GPIO_InitStructure;
	GPIO_InitStructure.GPIO_Mode = GPIO_Mode_Out_PP;
	GPIO_InitStructure.GPIO_Pin = GPIO_Pin_4 | GPIO_Pin_5;
	GPIO_InitStructure.GPIO_Speed = GPIO_Speed_50MHz;
	GPIO_Init(GPIOA, &GPIO_InitStructure);						//初始化PA4(INA)/PA5(INB)为推挽输出

	PWM_Init();													//初始化直流电机的底层PWM(ENA -> TIM2_CH3)
}

/**
  * @brief  直流电机设置速度（带方向）
  * @param  Speed 目标速度，范围：-100~100（符号=方向，绝对值=占空比百分比）
  * @retval 无
  * @note   调用上下文：任务级（报警控制任务自动联动 / 通信任务手动指令），
  *          跨任务调用必须持有 xActuatorMutex（见 main.c）
  *
  *          调速系数推导（时基 1计数=1us，ARR=20000）：
  *            CCR = |Speed| * 200
  *          即把 0~100 的百分比占空比映射到 0~20000 计数：
  *            Speed=100 -> CCR=20000（100%全速）
  *            Speed=50  -> CCR=10000（50%半速）
  */
void Motor_SetSpeed(int8_t Speed)
{
	if (Speed >= 0)							//正转（含0：方向脚保持正转组合，CCR=0自然停转）
	{
		GPIO_SetBits(GPIOA, GPIO_Pin_4);	//INA=1
		GPIO_ResetBits(GPIOA, GPIO_Pin_5);	//INB=0，方向=正转
		PWM_SetCompare3(Speed * 200);			//占空比% x 200 -> CCR（见上方系数推导）
	}
	else									//反转
	{
		GPIO_ResetBits(GPIOA, GPIO_Pin_4);	//INA=0
		GPIO_SetBits(GPIOA, GPIO_Pin_5);	//INB=1，方向=反转
		PWM_SetCompare3(-Speed * 200);		//取绝对值换算CCR
	}
}
