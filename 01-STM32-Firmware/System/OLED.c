/**
  ******************************************************************************
  * @file    System/OLED.c
  * @brief   0.96寸 OLED（SSD1306 主控, 128x64）驱动实现 —— 软件模拟 I2C
  * @author  LCW
  * @version V1.1
  * @date    2026-10-25
  * @note    通信方式：GPIO 位拆模拟 I2C（未用硬件 I2C 外设）
  *            SCL -> PB8、SDA -> PB9（均开漏输出，符合 I2C 线与规范）
  *            从机地址 0x78（8位写地址，即 7位地址 0x3C << 1）
  *            时序无插入延时，速率由库函数调用开销决定（约 0.5~1.5us/SCL周期）
  *          显存组织：SSD1306 内置 GDDRAM，128x64 分 8 页(page0~7)，每页 128 列，
  *            本驱动采用 8x16 字库 -> 每字符占 1 列 x 2 页，屏幕 4 行 x 16 列
  *          !!! RTOS 耗时预算（72MHz 实测估算，交接必读）!!!
  *            单次写命令/数据事务（Start+3字节+Stop）约 30~50us
  *            OLED_ShowChar（1字符）约 0.8~1.1ms —— 6条命令事务+16条数据事务
  *            OLED_Clear（整屏）约 35~50ms —— 1024 次数据事务
  *            刷新一行16字符约 12~18ms
  *          以上均为 CPU 忙等（软件I2C无法中断驱动），因此显示任务被设计为
  *          最低优先级(1)且周期 500ms 增量刷新（只刷数值区），绝不放在高优先级
  *          任务或中断中调用。调用上下文：main()初始化阶段 + 显示任务。
  ******************************************************************************
  * @history
  *   V1.0  2026-09-20  首次发布(注释规范化)
  *   V1.1  2026-10-25  注释交接化升级: 耗时预算/调用上下文/SSD1306命令说明
  ******************************************************************************
  */
#include "stm32f10x.h"
#include "OLED_Font.h"

/* ==================== 引脚操作宏（位拆 I2C 的两根线） ==================== */
#define OLED_W_SCL(x)		GPIO_WriteBit(GPIOB, GPIO_Pin_8, (BitAction)(x))
#define OLED_W_SDA(x)		GPIO_WriteBit(GPIOB, GPIO_Pin_9, (BitAction)(x))

/**
  * @brief  初始化 I2C 引脚（PB8=SCL, PB9=SDA，开漏 + 默认高电平）
  * @param  无
  * @retval 无
  * @note   开漏输出是 I2C 标准要求（支持线与/时钟拉伸），需外部上拉电阻
  */
void OLED_I2C_Init(void)
{
    RCC_APB2PeriphClockCmd(RCC_APB2Periph_GPIOB, ENABLE);

	GPIO_InitTypeDef GPIO_InitStructure;
 	GPIO_InitStructure.GPIO_Mode = GPIO_Mode_Out_OD;   //开漏：I2C规范要求
	GPIO_InitStructure.GPIO_Speed = GPIO_Speed_50MHz;
	GPIO_InitStructure.GPIO_Pin = GPIO_Pin_8;
 	GPIO_Init(GPIOB, &GPIO_InitStructure);
	GPIO_InitStructure.GPIO_Pin = GPIO_Pin_9;
	GPIO_Init(GPIOB, &GPIO_InitStructure);

	OLED_W_SCL(1);   //总线空闲态：双线均拉高
	OLED_W_SDA(1);
}

/**
  * @brief  I2C 起始信号（SCL高时 SDA 产生下降沿）
  * @param  无
  * @retval 无
  */
void OLED_I2C_Start(void)
{
	OLED_W_SDA(1);
	OLED_W_SCL(1);
	OLED_W_SDA(0);   //SCL为高时SDA下降沿 = START
	OLED_W_SCL(0);   //钳住总线，准备传输
}

/**
  * @brief  I2C 停止信号（SCL高时 SDA 产生上升沿）
  * @param  无
  * @retval 无
  */
void OLED_I2C_Stop(void)
{
	OLED_W_SDA(0);
	OLED_W_SCL(1);
	OLED_W_SDA(1);   //SCL为高时SDA上升沿 = STOP
}

/**
  * @brief  I2C 发送一个字节（MSB 先行）
  * @param  Byte 要发送的字节
  * @retval 无
  * @note   不检查从机应答(ACK)——SSD1306 只要时序正确必然应答，
  *          省略应答检测可简化驱动（江协科技风格），代价是无法发现接线故障
  */
void OLED_I2C_SendByte(uint8_t Byte)
{
	uint8_t i;
	for (i = 0; i < 8; i++)
	{
		OLED_W_SDA(!!(Byte & (0x80 >> i)));   //高位在前逐位送上SDA
		OLED_W_SCL(1);                        //SCL高电平期间从机采样
		OLED_W_SCL(0);                        //SCL低电平期间切换下一位
	}
	OLED_W_SCL(1);	//第9个时钟：应答位时隙，不读取（见函数头说明）
	OLED_W_SCL(0);
}

/**
  * @brief  向 OLED 写一条命令（控制字 0x00）
  * @param  Command 命令字节（见 SSD1306 数据手册命令表）
  * @retval 无
  */
void OLED_WriteCommand(uint8_t Command)
{
	OLED_I2C_Start();
	OLED_I2C_SendByte(0x78);		//从机地址(写)
	OLED_I2C_SendByte(0x00);		//控制字节0x00 = 后续为命令
	OLED_I2C_SendByte(Command);
	OLED_I2C_Stop();
}

/**
  * @brief  向 OLED 显存写一个字节数据（控制字 0x40）
  * @param  Data 显示数据字节（8位纵向点阵）
  * @retval 无
  */
void OLED_WriteData(uint8_t Data)
{
	OLED_I2C_Start();
	OLED_I2C_SendByte(0x78);		//从机地址(写)
	OLED_I2C_SendByte(0x40);		//控制字节0x40 = 后续为显示数据
	OLED_I2C_SendByte(Data);
	OLED_I2C_Stop();
}

/**
  * @brief  设置显存写入光标（页地址 + 列地址）
  * @param  Y 页地址，范围：0~7（每页8行像素，纵向）
  * @param  X 列地址，范围：0~127（横向像素列）
  * @retval 无
  * @note   SSD1306 地址自动增长方向为"仅列自增"——写满一行后页不会自动进位，
  *          所以 ShowChar 的上下半部分需要分别 SetCursor（见 OLED_ShowChar）
  */
void OLED_SetCursor(uint8_t Y, uint8_t X)
{
	OLED_WriteCommand(0xB0 | Y);					//设置页地址(Y)
	OLED_WriteCommand(0x10 | ((X & 0xF0) >> 4));	//设置列地址高4位
	OLED_WriteCommand(0x00 | (X & 0x0F));			//设置列地址低4位
}

/**
  * @brief  清屏（整屏 GDDRAM 写 0）
  * @param  无
  * @retval 无
  * @warning 耗时约 35~50ms（1024次数据事务），仅初始化阶段/显示任务可用，
  *          禁止在中断或高优先级任务中调用
  */
void OLED_Clear(void)
{
	uint8_t i, j;
	for (j = 0; j < 8; j++)        //逐页清零
	{
		OLED_SetCursor(j, 0);
		for(i = 0; i < 128; i++)   //每页128列
		{
			OLED_WriteData(0x00);  //0x00 = 该列8行像素全灭
		}
	}
}

/**
  * @brief  显示一个 ASCII 字符（8x16 点阵）
  * @param  Line   行位置，范围：1~4（对应页 0~7，每字符占2页）
  * @param  Column 列位置，范围：1~16
  * @param  Char   要显示的字符（ASCII 可见字符，空格起）
  * @retval 无
  * @note   耗时约 0.8~1.1ms；字库索引 Char-' ' 使空格(0x20)对应数组首元素。
  *          8x16字符拆成上下两个8x8半区分别写入（页地址不自动进位，见SetCursor）
  */
void OLED_ShowChar(uint8_t Line, uint8_t Column, char Char)
{
	uint8_t i;
	OLED_SetCursor((Line - 1) * 2, (Column - 1) * 8);		//光标定位到字符上半区
	for (i = 0; i < 8; i++)
	{
		OLED_WriteData(OLED_F8x16[Char - ' '][i]);			//写入上半部分字模
	}
	OLED_SetCursor((Line - 1) * 2 + 1, (Column - 1) * 8);	//光标定位到字符下半区
	for (i = 0; i < 8; i++)
	{
		OLED_WriteData(OLED_F8x16[Char - ' '][i + 8]);		//写入下半部分字模
	}
}

/**
  * @brief  显示一个字符串（自动连续排布，不换行）
  * @param  Line   起始行位置，范围：1~4
  * @param  Column 起始列位置，范围：1~16
  * @param  String 以'\0'结尾的 ASCII 字符串（越界部分不显示保护由调用方负责）
  * @retval 无
  * @note   耗时 = 字符数 x 0.8~1.1ms；调用方须保证 Line/Column+长度 不超过 4/16
  */
void OLED_ShowString(uint8_t Line, uint8_t Column, char *String)
{
	uint8_t i;
	for (i = 0; String[i] != '\0'; i++)
	{
		OLED_ShowChar(Line, Column + i, String[i]);
	}
}

/**
  * @brief  整数次幂（内部工具函数，供数字显示提取各位）
  * @param  X 底数
  * @param  Y 指数
  * @retval X 的 Y 次方
  */
uint32_t OLED_Pow(uint32_t X, uint32_t Y)
{
	uint32_t Result = 1;
	while (Y--)
	{
		Result *= X;
	}
	return Result;
}

/**
  * @brief  显示无符号十进制整数（固定位数，前导补零）
  * @param  Line   行位置，范围：1~4
  * @param  Column 起始列位置，范围：1~16
  * @param  Number 要显示的数字，范围：0~4294967295
  * @param  Length 数字位数，范围：1~10（不足位补零，如 5 显示为 "05"）
  * @retval 无
  * @note   固定位数设计便于界面布局：数值刷新时不会因位数变化留下残影
  */
void OLED_ShowNum(uint8_t Line, uint8_t Column, uint32_t Number, uint8_t Length)
{
	uint8_t i;
	for (i = 0; i < Length; i++)							//从最高位到最低位逐位提取
	{
		OLED_ShowChar(Line, Column + i, Number / OLED_Pow(10, Length - i - 1) % 10 + '0');
	}
}

/**
  * @brief  显示带符号十进制整数（首位显示 +/- 符号）
  * @param  Line   行位置，范围：1~4
  * @param  Column 起始列位置，范围：1~16
  * @param  Number 要显示的数字，范围：-2147483648~2147483647
  * @param  Length 数字位数（不含符号位），范围：1~10
  * @retval 无
  */
void OLED_ShowSignedNum(uint8_t Line, uint8_t Column, int32_t Number, uint8_t Length)
{
	uint8_t i;
	uint32_t Number1;
	if (Number >= 0)                      //非负数：显示'+'，原值输出
	{
		OLED_ShowChar(Line, Column, '+');
		Number1 = Number;
	}
	else                                  //负数：显示'-'，取绝对值输出
	{
		OLED_ShowChar(Line, Column, '-');
		Number1 = -Number;
	}
	for (i = 0; i < Length; i++)							//数字位右移1列（让出符号位）
	{
		OLED_ShowChar(Line, Column + i + 1, Number1 / OLED_Pow(10, Length - i - 1) % 10 + '0');
	}
}

/**
  * @brief  显示十六进制整数（大写，固定位数）
  * @param  Line   行位置，范围：1~4
  * @param  Column 起始列位置，范围：1~16
  * @param  Number 要显示的数字，范围：0~0xFFFFFFFF
  * @param  Length 十六进制位数，范围：1~8
  * @retval 无
  */
void OLED_ShowHexNum(uint8_t Line, uint8_t Column, uint32_t Number, uint8_t Length)
{
	uint8_t i, SingleNumber;
	for (i = 0; i < Length; i++)
	{
		SingleNumber = Number / OLED_Pow(16, Length - i - 1) % 16;   //提取一位16进制数
		if (SingleNumber < 10)
		{
			OLED_ShowChar(Line, Column + i, SingleNumber + '0');     //0~9 -> '0'~'9'
		}
		else
		{
			OLED_ShowChar(Line, Column + i, SingleNumber - 10 + 'A');//10~15 -> 'A'~'F'
		}
	}
}

/**
  * @brief  显示二进制整数（固定位数）
  * @param  Line   行位置，范围：1~4
  * @param  Column 起始列位置，范围：1~16
  * @param  Number 要显示的数字，范围：0~0xFFFF（1111 1111 1111 1111）
  * @param  Length 二进制位数，范围：1~16
  * @retval 无
  */
void OLED_ShowBinNum(uint8_t Line, uint8_t Column, uint32_t Number, uint8_t Length)
{
	uint8_t i;
	for (i = 0; i < Length; i++)
	{
		OLED_ShowChar(Line, Column + i, Number / OLED_Pow(2, Length - i - 1) % 2 + '0');
	}
}

/**
  * @brief  OLED 上电初始化（SSD1306 复位命令序列 + 清屏）
  * @param  无
  * @retval 无
  * @note   调用上下文：仅 main() 初始化阶段。
  *          开头约几十ms的上电等待循环：SSD1306 断电重启后需要时间建立
  *          内部电荷泵，立即发命令可能被忽略（无复位引脚模块的保守做法）
  */
/**
  * @brief  OLED 供电引脚初始化（PB6=GND, PB7=3.3V）
  * @param  无
  * @retval 无
  * @note   本机接线把 OLED 模块的 GND/VCC 接在 PB6/PB7 上, 由 GPIO 供电:
  *         PB6 推挽输出低电平当地, PB7 推挽输出高电平当 3.3V 电源轨。
  *         若模块 VCC/GND 直接接电源排针, 本段输出悬空无任何影响,
  *         但仍推荐改回电源排针供电(GPIO 单脚持续电流上限25mA,
  *         且复位/烧录期间引脚浮空, 屏会短暂黑一下)
  */
static void OLED_Power_Init(void)
{
    GPIO_InitTypeDef GPIO_InitStructure;

    RCC_APB2PeriphClockCmd(RCC_APB2Periph_GPIOB, ENABLE);

    /* PB6: 推挽输出低 = OLED GND */
    GPIO_InitStructure.GPIO_Pin   = GPIO_Pin_6;
    GPIO_InitStructure.GPIO_Mode  = GPIO_Mode_Out_PP;
    GPIO_InitStructure.GPIO_Speed = GPIO_Speed_50MHz;
    GPIO_Init(GPIOB, &GPIO_InitStructure);
    GPIO_WriteBit(GPIOB, GPIO_Pin_6, Bit_RESET);

    /* PB7: 推挽输出高 = OLED VCC(3.3V) */
    GPIO_InitStructure.GPIO_Pin = GPIO_Pin_7;
    GPIO_Init(GPIOB, &GPIO_InitStructure);
    GPIO_WriteBit(GPIOB, GPIO_Pin_7, Bit_SET);
}

void OLED_Init(void)
{
	uint32_t i, j;

	OLED_Power_Init();					//先给模块上电(PB7高/PB6低), 再等内部电源就绪

	for (i = 0; i < 1000; i++)			//上电延时（保守等待SSD1306内部电源就绪）
	{
		for (j = 0; j < 1000; j++);
	}

	OLED_I2C_Init();			//I2C引脚初始化

	OLED_WriteCommand(0xAE);	//关闭显示（配置期间不亮屏，防花屏）

	OLED_WriteCommand(0xD5);	//设置显示时钟分频比/振荡器频率
	OLED_WriteCommand(0x80);	//默认分频比

	OLED_WriteCommand(0xA8);	//设置多路复用率（驱动行数）
	OLED_WriteCommand(0x3F);	//64行复用（128x64屏标准值）

	OLED_WriteCommand(0xD3);	//设置显示偏移
	OLED_WriteCommand(0x00);	//无偏移

	OLED_WriteCommand(0x40);	//设置显示开始行 = 第0行

	OLED_WriteCommand(0xA1);	//设置段重映射：0xA1正常 / 0xA0左右反置

	OLED_WriteCommand(0xC8);	//设置COM输出扫描方向：0xC8正常 / 0xC0上下反置

	OLED_WriteCommand(0xDA);	//设置COM引脚硬件配置
	OLED_WriteCommand(0x12);	//128x64屏标准配置（交替COM模式）

	OLED_WriteCommand(0x81);	//设置对比度
	OLED_WriteCommand(0xCF);	//对比度值（0x00~0xFF，越大越亮，0xCF为常用平衡值）

	OLED_WriteCommand(0xD9);	//设置预充电周期
	OLED_WriteCommand(0xF1);	//Phase1=1/Phase2=15（影响像素充放电时序）

	OLED_WriteCommand(0xDB);	//设置VCOMH取消选择级别
	OLED_WriteCommand(0x30);	//约0.83xVcc

	OLED_WriteCommand(0xA4);	//设置整个显示打开/关闭：0xA4正常显存输出 / 0xA5全亮

	OLED_WriteCommand(0xA6);	//设置正常/反色显示：0xA6正常 / 0xA7反色

	OLED_WriteCommand(0x8D);	//设置电荷泵
	OLED_WriteCommand(0x14);	//开启电荷泵（模块无外部升压时必须开启）

	OLED_WriteCommand(0xAF);	//开启显示

	OLED_Clear();				//清屏（防止显存随机数据导致花屏）
}
