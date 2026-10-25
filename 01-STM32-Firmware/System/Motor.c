/**
  ******************************************************************************
  * @file    System/Motor.c
  * @brief   直流电机驱动实现(方向IO + TIM2_CH3 PWM调速)
  * @author  LCW
  * @version V1.2
  * @date    2026-10-25
  * @note    方向脚: PA4/PA5(推挽输出); 调速: PWM通道3(PA2)
  *          正转: Speed>=0 -> PA4=1,PA5=0; 反转: Speed<0 -> PA4=0,PA5=1
  *          温室场景: 风扇仅需正转排风, 负速度为驱动通用性保留
  ******************************************************************************
  * @history
  *   V1.0  2026-09-20  首次发布(注释规范化)
  *   V1.2  2026-10-25  交接级注释升级(纯注释零代码变更)
  ******************************************************************************
  */
#include "stm32f10x.h"                  // Device header
#include "PWM.h"

/**
  * 函    数：直流电机初始化
  * 参    数：无
  * 返 回 值：无
  * @note    方向脚PA4/PA5推挽输出; 调速依赖PWM_Init(TIM2_CH3, PA2);
  *          执行耗时<0.1ms
  */
void Motor_Init(void)
{
	/*开启时钟*/
	RCC_APB2PeriphClockCmd(RCC_APB2Periph_GPIOA, ENABLE);		//开启GPIOA的时钟

	GPIO_InitTypeDef GPIO_InitStructure;
	GPIO_InitStructure.GPIO_Mode = GPIO_Mode_Out_PP;
	GPIO_InitStructure.GPIO_Pin = GPIO_Pin_4 | GPIO_Pin_5;
	GPIO_InitStructure.GPIO_Speed = GPIO_Speed_50MHz;
	GPIO_Init(GPIOA, &GPIO_InitStructure);						//将PA4和PA5引脚初始化为推挽输出

	PWM_Init();													//初始化直流电机的底层PWM
}

/**
  * 函    数：直流电机设置速度
  * 参    数：Speed 要设置的速度，范围：-100~100
  * 返 回 值：无
  * @note    速度映射: |Speed|(0~100) * 200 = CCR(0~20000),
  *          对应占空比0~100%(ARR=20000, 见PWM.c);
  *          方向脚先于占空比设置(先定向再给能量);
  *          SetBits/ResetBits为BSRR单写原子操作
  */
void Motor_SetSpeed(int8_t Speed)
{
	if (Speed >= 0)							//如果设置正转的速度值
	{
		GPIO_SetBits(GPIOA, GPIO_Pin_4);	//PA4置高电平
		GPIO_ResetBits(GPIOA, GPIO_Pin_5);	//PA5置低电平，设置方向为正转
		PWM_SetCompare3(Speed * 200);			//PWM设置为速度值*200 (0-100映射到0-20000)
	}
	else									//否则，即设置反转的速度值
	{
		GPIO_ResetBits(GPIOA, GPIO_Pin_4);	//PA4置低电平
		GPIO_SetBits(GPIOA, GPIO_Pin_5);	//PA5置高电平，设置方向为反转
		PWM_SetCompare3(-Speed * 200);		//PWM设置为负的速度值*200
	}
}
