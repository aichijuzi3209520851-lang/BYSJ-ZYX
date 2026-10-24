/**
  ******************************************************************************
  * @file    System/PWM.c
  * @brief   TIM2 PWM 输出驱动实现（舵机 / 电机共用同一时基）
  * @author  LCW
  * @version V1.1
  * @date    2026-10-25
  * @note    时基设计（50Hz 舵机标准周期）：
  *            TIM2 时钟 = 72MHz(APB1倍频后)
  *            PSC  = 72-1   -> 计数频率 72MHz/72   = 1MHz（1个计数 = 1us）
  *            ARR  = 20000-1 -> PWM周期 20000us    = 20ms（50Hz，SG90舵机标准）
  *            占空比 = CCR / ARR，即 CCR=1500 表示 1.5ms 脉宽
  *          通道分配：
  *            TIM2_CH2 -> PA1 -> 舵机(天窗)，见 Servo.c
  *            TIM2_CH3 -> PA2 -> 电机调速(风扇)，见 Motor.c
  *          RTOS 上下文：纯寄存器操作、天然线程安全，任务/中断均可调用；
  *          无定时器中断，不占用任何 NVIC 通道
  ******************************************************************************
  * @history
  *   V1.0  2026-09-20  首次发布(注释规范化)
  *   V1.1  2026-10-25  注释交接化升级: 时基推导/通道分配/线程安全说明
  ******************************************************************************
  */
#include "stm32f10x.h"                  // Device header

/**
  * @brief  初始化 TIM2 PWM（舵机 CH2 + 电机 CH3 共用）
  * @param  无
  * @retval 无
  * @note   调用上下文：main() 初始化阶段（调度器启动前），
  *          由 Servo_Init()/Motor_Init() 间接调用（内部有重复初始化保护，多次调用无害）
  */
void PWM_Init(void)
{
	/* 开启时钟：TIM2 挂 APB1，GPIOA 挂 APB2 */
	RCC_APB1PeriphClockCmd(RCC_APB1Periph_TIM2, ENABLE);			//开启TIM2的时钟
	RCC_APB2PeriphClockCmd(RCC_APB2Periph_GPIOA, ENABLE);			//开启GPIOA的时钟

	/*GPIO初始化：PA1/PA2 配置为复用推挽，交由定时器控制电平*/
	GPIO_InitTypeDef GPIO_InitStructure;
	GPIO_InitStructure.GPIO_Mode = GPIO_Mode_AF_PP;
	GPIO_InitStructure.GPIO_Pin = GPIO_Pin_1 | GPIO_Pin_2;
	GPIO_InitStructure.GPIO_Speed = GPIO_Speed_50MHz;
	GPIO_Init(GPIOA, &GPIO_InitStructure);							//受外设控制的引脚，必须配置为复用模式

	/*配置时钟源：内部时钟（不调用此函数时 TIM 默认也是内部时钟，显式调用便于阅读）*/
	TIM_InternalClockConfig(TIM2);		//选择TIM2为内部时钟

	/*时基单元初始化：1MHz计数 x 20000 = 20ms周期(50Hz)*/
	TIM_TimeBaseInitTypeDef TIM_TimeBaseInitStructure;				//定义结构体变量
	TIM_TimeBaseInitStructure.TIM_ClockDivision = TIM_CKD_DIV1;     //时钟分频，选择不分频，此参数用于配置滤波器时钟，不影响时基单元功能
	TIM_TimeBaseInitStructure.TIM_CounterMode = TIM_CounterMode_Up; //计数器模式，选择向上计数
	TIM_TimeBaseInitStructure.TIM_Period = 20000 - 1;               //计数周期(ARR)：20000计数=20ms，舵机标准刷新周期
	TIM_TimeBaseInitStructure.TIM_Prescaler = 72 - 1;               //预分频器(PSC)：72分频后1MHz，1个计数=1us，便于脉宽换算
	TIM_TimeBaseInitStructure.TIM_RepetitionCounter = 0;            //重复计数器，仅高级定时器(TIM1/TIM8)使用
	TIM_TimeBaseInit(TIM2, &TIM_TimeBaseInitStructure);             //将结构体变量交给TIM_TimeBaseInit，配置TIM2的时基单元

	/*输出比较初始化：PWM模式1，有效电平为高*/
	TIM_OCInitTypeDef TIM_OCInitStructure;							//定义结构体变量
	TIM_OCStructInit(&TIM_OCInitStructure);                         //结构体初始化，若结构体没有完整赋值
	                                                                //则最好执行此函数，给结构体所有成员都赋一个默认值
	                                                                //避免结构体初值不确定的问题
	TIM_OCInitStructure.TIM_OCMode = TIM_OCMode_PWM1;               //输出比较模式，选择PWM模式1（CNT<CCR时输出有效电平）
	TIM_OCInitStructure.TIM_OCPolarity = TIM_OCPolarity_High;       //输出极性，选择为高，若选择极性为低，则输出高低电平取反
	TIM_OCInitStructure.TIM_OutputState = TIM_OutputState_Enable;   //输出使能
	TIM_OCInitStructure.TIM_Pulse = 0;								//初始CCR=0：上电脉宽为0，舵机不通电、电机不转
	TIM_OC2Init(TIM2, &TIM_OCInitStructure);                        //配置TIM2的输出比较通道2（PA1 -> 舵机）
	TIM_OC3Init(TIM2, &TIM_OCInitStructure);                        //配置TIM2的输出比较通道3（PA2 -> 电机）

	/*TIM使能：定时器开始运行，PWM开始输出*/
	TIM_Cmd(TIM2, ENABLE);			//使能TIM2，定时器开始运行
}

/**
  * @brief  设置电机通道(CH3)的CCR值
  * @param  Compare 要写入的CCR值（计数=微秒）
  * @retval 无
  * @note   调用上下文：任务级（报警控制任务/通信任务），经 xActuatorMutex 保护后调用；
  *          占空比 = Compare / 20000，如 Compare=10000 表示 50% 占空比
  */
void PWM_SetCompare3(uint16_t Compare)
{
	TIM_SetCompare3(TIM2, Compare);		//设置CCR3的值
}

/**
  * @brief  设置舵机通道(CH2)的CCR值
  * @param  Compare 要写入的CCR2值，有效范围 500~2500（对应0.5~2.5ms脉宽）
  * @retval 无
  * @note   调用上下文：任务级（报警控制任务/通信任务），经 xActuatorMutex 保护后调用；
  *          本驱动只写寄存器不做范围检查，角度限幅由上层 Servo_SetAngle() 的线性映射保证
  */
void PWM_SetCompare2(uint16_t Compare)
{
	TIM_SetCompare2(TIM2, Compare);		//设置CCR2的值
}
