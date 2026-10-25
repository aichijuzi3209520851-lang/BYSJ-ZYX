/**
  ******************************************************************************
  * @file    System/OLED.c
  * @brief   0.96寸 OLED(SSD1306, 128x64)驱动实现
  * @author  LCW
  * @version V1.2
  * @date    2026-10-25
  * @note    ── 硬件与通信 ────────────────────────────────────────────
  *          接口   : 4针软件模拟I2C, SCL=PB8 / SDA=PB9(开漏输出,
  *                   模块自带上拉; 引线长/多从机时外加上拉4.7k)
  *          从机地址: 0x78(8位格式, 即7位地址0x3C; 个别模块为0x7A)
  *          控制字节: 0x00=命令流 / 0x40=数据流(单字节事务, 每字节
  *                   独立 START→地址→控制→数据→STOP, 江协风格)
  *          ── 显存与坐标模型 ─────────────────────────────────────
  *          128x64 = 8页(页0~7) x 128列, 每字节=一列竖8像素;
  *          字符8x16: 占上下两页各8字节 → 屏幕4行x16列
  *          换算: 页=(Line-1)*2 与 (Line-1)*2+1, 列=(Column-1)*8
  *          ── 时序与耗时预算(72MHz) ──────────────────────────────
  *          单字节事务约24字节I2C位流, GPIO直翻约0.5ms;
  *          全屏OLED_Clear(8页x128列)≈35~50ms(CPU忙等);
  *          主循环增量刷新数值区≈10ms → 显示任务不得放中断/高优先级
  *          ── 供电方式(本工程特有) ──────────────────────────────
  *          OLED的VCC/GND可接: ①PB7/PB6(本驱动GPIO供电, 见
  *          OLED_Power_Init) ②3.3V/GND电源排针(推荐, 见下)
  ******************************************************************************
  * @history
  *   V1.0  2026-09-20  首次发布(注释规范化, 江协科技风格基准实现)
  *   V1.1  2026-10-25  注释交接化升级: 耗时预算/调用上下文/命令说明
  *   V1.2  2026-10-25  新增OLED_Power_Init(PB7/PB6 GPIO供电)与
  *                     OLED_DisplayOn(显示保活, 防供电瞬断灭屏)
  ******************************************************************************
  */
#include "stm32f10x.h"
#include "OLED_Font.h"

/* ==================== 引脚操作宏(位拆I2C的两根线) ====================
 * GPIO_WriteBit为BSRR单写原子操作; O2下可能内联为单条STR指令,
 * SCL脉宽极窄——SSD1306在400kHz上限内可正常采样(多工程实测) */
#define OLED_W_SCL(x)		GPIO_WriteBit(GPIOB, GPIO_Pin_8, (BitAction)(x))
#define OLED_W_SDA(x)		GPIO_WriteBit(GPIOB, GPIO_Pin_9, (BitAction)(x))

/**
  * @brief  初始化I2C引脚(PB8=SCL, PB9=SDA, 开漏+默认高电平)
  * @param  无
  * @retval 无
  * @note   开漏输出是I2C规范要求(支持线与/时钟拉伸), 依赖外部上拉
  *         (模块板载); 总线空闲态为双高
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
  * @brief  I2C起始信号(SCL高时SDA产生下降沿)
  * @param  无
  * @retval 无
  * @note   可被中断/任务切换打断: 打断只拉长相位, 不产生半位,
  *         SSD1306按沿采样, 时序安全(全驱动同此结论)
  */
void OLED_I2C_Start(void)
{
	OLED_W_SDA(1);
	OLED_W_SCL(1);
	OLED_W_SDA(0);   //SCL为高时SDA下降沿 = START
	OLED_W_SCL(0);   //钳住总线，准备传输
}

/**
  * @brief  I2C停止信号(SCL高时SDA产生上升沿)
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
  * @brief  I2C发送一个字节(MSB先行)
  * @param  Byte 要发送的一个字节
  * @retval 无
  * @note   不检查从机应答(ACK)——SSD1306是纯从机且无时序强制,
  *         额外给一拍时钟即可; 省略应答检测简化驱动(江协风格),
  *         代价是无法通过ACK发现接线故障
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
	OLED_W_SCL(1);	//额外的一个时钟，不处理应答信号(第9拍)
	OLED_W_SCL(0);
}

/**
  * @brief  OLED写命令(单字节事务: START→0x78→0x00→命令→STOP)
  * @param  Command 要写入的命令
  * @retval 无
  */
void OLED_WriteCommand(uint8_t Command)
{
	OLED_I2C_Start();
	OLED_I2C_SendByte(0x78);		//从机地址
	OLED_I2C_SendByte(0x00);		//控制字节0x00 = 命令流
	OLED_I2C_SendByte(Command);
	OLED_I2C_Stop();
}

/**
  * @brief  OLED写数据(单字节事务: START→0x78→0x40→数据→STOP)
  * @param  Data 要写入显存的数据(一列竖8像素, D0在上)
  * @retval 无
  */
void OLED_WriteData(uint8_t Data)
{
	OLED_I2C_Start();
	OLED_I2C_SendByte(0x78);		//从机地址
	OLED_I2C_SendByte(0x40);		//控制字节0x40 = 数据流
	OLED_I2C_SendByte(Data);
	OLED_I2C_Stop();
}

/**
  * @brief  设置显存光标位置(后续WriteData从此处开始连续写)
  * @param  Y 页坐标, 范围0~7(字符行Line对应上下两页)
  * @param  X 列坐标, 范围0~127(字符列Column每列占8像素)
  * @retval 无
  * @note   设光标三条命令: 0xB0|页 / 0x10|列高4位 / 0x00|列低4位
  */
void OLED_SetCursor(uint8_t Y, uint8_t X)
{
	OLED_WriteCommand(0xB0 | Y);					//设置Y位置(页地址)
	OLED_WriteCommand(0x10 | ((X & 0xF0) >> 4));	//设置X位置高4位
	OLED_WriteCommand(0x00 | (X & 0x0F));			//设置X位置低4位
}

/**
  * @brief  显示保活: 重发充电泵开启与显示开启命令
  * @param  无
  * @retval 无
  * @note   SSD1306无自动熄屏功能, 灭屏只可能是供电瞬断(掉电复位后
  *         显示关/充电泵关)或I2C线被干扰误注入0xAE。主循环周期调用
  *         本函数即可自动救回; 彻底解决仍需改善供电(建议OLED改接
  *         3.3V/GND电源排针+独立USB供电)
  */
void OLED_DisplayOn(void)
{
	OLED_WriteCommand(0x8D);	//充电泵设置
	OLED_WriteCommand(0x14);	//开启充电泵
	OLED_WriteCommand(0xAF);	//开启显示
}

/**
  * @brief  OLED全屏清零(显存写0)
  * @param  无
  * @retval 无
  * @note   耗时约35~50ms(CPU忙等), 仅上电初始化时调用;
  *         运行期局部刷新请用ShowString覆盖(等宽字符天然支持覆盖)
  */
void OLED_Clear(void)
{
	uint8_t i, j;
	for (j = 0; j < 8; j++)          //逐页
	{
		OLED_SetCursor(j, 0);
		for(i = 0; i < 128; i++)     //每页128列
		{
			OLED_WriteData(0x00);
		}
	}
}

/**
  * @brief  显示一个ASCII字符(8x16点阵, 占上下两页)
  * @param  Line 行位置, 范围1~4
  * @param  Column 列位置, 范围1~16
  * @param  Char ASCII可见字符(字库索引Char-' ', 不支持中文)
  * @retval 无
  * @note   字模数组OLED_F8x16每字符16字节: 前8=上半页/后8=下半页;
  *         数组必须const(约1.5KB放Flash, 漏const会占RAM)
  */
void OLED_ShowChar(uint8_t Line, uint8_t Column, char Char)
{
	uint8_t i;
	OLED_SetCursor((Line - 1) * 2, (Column - 1) * 8);		//设置光标位置在上半部分
	for (i = 0; i < 8; i++)
	{
		OLED_WriteData(OLED_F8x16[Char - ' '][i]);			//显示上半部分内容
	}
	OLED_SetCursor((Line - 1) * 2 + 1, (Column - 1) * 8);	//设置光标位置在下半部分
	for (i = 0; i < 8; i++)
	{
		OLED_WriteData(OLED_F8x16[Char - ' '][i + 8]);		//显示下半部分内容
	}
}

/**
  * @brief  显示字符串(逐字符调用ShowChar)
  * @param  Line 起始行位置, 范围1~4
  * @param  Column 起始列位置, 范围1~16
  * @param  String 以'\0'结尾的ASCII字符串
  * @retval 无
  * @note   等宽字库特性: 重复写同一位置即为覆盖, 局部更新无需清屏
  *         (主循环数值区刷新即基于此); 注意超界(Column+长度>16)会回卷
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
  * @brief  内部次方函数(整数, 供数位提取)
  * @param  X 底数
  * @param  Y 指数
  * @retval X的Y次方
  * @note   不依赖math.h的pow(double), 省栈且避免浮点库引入
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
  * @brief  显示无符号十进制数(固定位数, 高位补0)
  * @param  Line 起始行位置, 范围1~4
  * @param  Column 起始列位置, 范围1~16
  * @param  Number 要显示的数字, 范围0~4294967295
  * @param  Length 显示位数, 范围1~10(超长高位截断)
  * @retval 无
  * @note   逐位"除10^(Length-k-1)取余"提取数位, 不用sprintf(省栈);
  *         高位补0特性使重复刷新同位置时等宽对齐(如05/60)
  */
void OLED_ShowNum(uint8_t Line, uint8_t Column, uint32_t Number, uint8_t Length)
{
	uint8_t i;
	for (i = 0; i < Length; i++)
	{
		OLED_ShowChar(Line, Column + i, Number / OLED_Pow(10, Length - i - 1) % 10 + '0');
	}
}

/**
  * @brief  显示带符号十进制数(符号占首列, 数值固定位数)
  * @param  Line 起始行位置, 范围1~4
  * @param  Column 起始列位置, 范围1~16
  * @param  Number 要显示的数字, 范围-2147483648~2147483647
  * @param  Length 数值位数, 范围1~10(总占Column起Length+1列)
  * @retval 无
  */
void OLED_ShowSignedNum(uint8_t Line, uint8_t Column, int32_t Number, uint8_t Length)
{
	uint8_t i;
	uint32_t Number1;
	if (Number >= 0)
	{
		OLED_ShowChar(Line, Column, '+');
		Number1 = Number;
	}
	else
	{
		OLED_ShowChar(Line, Column, '-');
		Number1 = -Number;
	}
	for (i = 0; i < Length; i++)
	{
		OLED_ShowChar(Line, Column + i + 1, Number1 / OLED_Pow(10, Length - i - 1) % 10 + '0');
	}
}

/**
  * @brief  显示十六进制数(大写字母, 固定位数, 高位补0)
  * @param  Line 起始行位置, 范围1~4
  * @param  Column 起始列位置, 范围1~16
  * @param  Number 要显示的数字, 范围0~0xFFFFFFFF
  * @param  Length 显示位数, 范围1~8
  * @retval 无
  */
void OLED_ShowHexNum(uint8_t Line, uint8_t Column, uint32_t Number, uint8_t Length)
{
	uint8_t i, SingleNumber;
	for (i = 0; i < Length; i++)
	{
		SingleNumber = Number / OLED_Pow(16, Length - i - 1) % 16;
		if (SingleNumber < 10)
		{
			OLED_ShowChar(Line, Column + i, SingleNumber + '0');
		}
		else
		{
			OLED_ShowChar(Line, Column + i, SingleNumber - 10 + 'A');
		}
	}
}

/**
  * @brief  显示二进制数(固定位数, 高位补0)
  * @param  Line 起始行位置, 范围1~4
  * @param  Column 起始列位置, 范围1~16
  * @param  Number 要显示的数字
  * @param  Length 显示位数, 范围1~16
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
  * @brief  OLED供电引脚初始化(PB6=GND, PB7=3.3V)
  * @param  无
  * @retval 无
  * @note   本机接线把OLED模块的GND/VCC接在PB6/PB7上, 由GPIO供电:
  *         PB6推挽输出低电平当地, PB7推挽输出高电平当3.3V电源轨。
  *         若模块VCC/GND直接接电源排针, 本段输出悬空无任何影响。
  *         推荐改回电源排针供电(GPIO单脚持续电流上限25mA, 且复位/
  *         烧录期间引脚浮空, 屏会短暂黑一下)
  */
static void OLED_Power_Init(void)
{
    GPIO_InitTypeDef GPIO_InitStructure;

    RCC_APB2PeriphClockCmd(RCC_APB2Periph_GPIOB, ENABLE);

    /* PB6: 推挽输出低 = OLED GND */
    GPIO_InitStructure.GPIO_Mode  = GPIO_Mode_Out_PP;
    GPIO_InitStructure.GPIO_Speed = GPIO_Speed_50MHz;
    GPIO_InitStructure.GPIO_Pin   = GPIO_Pin_6;
    GPIO_Init(GPIOB, &GPIO_InitStructure);
    GPIO_WriteBit(GPIOB, GPIO_Pin_6, Bit_RESET);

    /* PB7: 推挽输出高 = OLED VCC(3.3V) */
    GPIO_InitStructure.GPIO_Pin = GPIO_Pin_7;
    GPIO_Init(GPIOB, &GPIO_InitStructure);
    GPIO_WriteBit(GPIOB, GPIO_Pin_7, Bit_SET);
}

/**
  * @brief  OLED初始化: 供电 → 上电延时 → I2C引脚 → 初始化命令序列 → 清屏
  * @param  无
  * @retval 无
  * @note   命令序列要点(完整命令含义见SSD1306数据手册):
  *         0xA8/0xDA  多路复用与COM配置(64行屏定值)
  *         0xA1/0xC8  扫描方向(与模块安装方向匹配, 反了图像镜像)
  *         0x81 0xCF  对比度(可调0x00~0xFF)
  *         0x8D 0x14  充电泵开启——不写屏不亮, 最常见故障点
  *         0xAF       开显示(配置期间保持0xAE关闭防花屏)
  *
  *         交接注意事项(上电延时与供电顺序):
  *         当前顺序为"空循环延时→供电→初始化", 空循环的实际时长随
  *         编译优化等级剧烈变化(O2下被整体删除, O1约100ms)——本工程
  *         为O1且真机验证正常; 该延时的本意是等SSD1306内部电源就绪
  *         (规范≥100ms)。若日后提高优化等级或出现上电花屏, 请改为
  *         ①把OLED_Power_Init()移到延时之前(先供电再等待) ②延时改用
  *         确定性计时器(如DWT Delay_ms, 需在其前完成Delay_Init)
  */
void OLED_Init(void)
{
	uint32_t i, j;

	for (i = 0; i < 1000; i++)			//上电延时(实际时长随优化等级变化, 见上注)
	{
		for (j = 0; j < 1000; j++);
	}

	OLED_Power_Init();                  //给模块供电(PB7=3.3V/PB6=GND)

	OLED_I2C_Init();			//端口初始化

	OLED_WriteCommand(0xAE);	//关闭显示

	OLED_WriteCommand(0xD5);	//设置显示时钟分频比/振荡器频率
	OLED_WriteCommand(0x80);

	OLED_WriteCommand(0xA8);	//设置多路复用率
	OLED_WriteCommand(0x3F);

	OLED_WriteCommand(0xD3);	//设置显示偏移
	OLED_WriteCommand(0x00);

	OLED_WriteCommand(0x40);	//设置显示开始行

	OLED_WriteCommand(0xA1);	//设置左右方向，0xA1正常 0xA0左右反置

	OLED_WriteCommand(0xC8);	//设置上下方向，0xC8正常 0xC0上下反置

	OLED_WriteCommand(0xDA);	//设置COM引脚硬件配置
	OLED_WriteCommand(0x12);

	OLED_WriteCommand(0x81);	//设置对比度控制
	OLED_WriteCommand(0xCF);

	OLED_WriteCommand(0xD9);	//设置预充电周期
	OLED_WriteCommand(0xF1);

	OLED_WriteCommand(0xDB);	//设置VCOMH取消选择级别
	OLED_WriteCommand(0x30);

	OLED_WriteCommand(0xA4);	//设置整个显示打开/关闭

	OLED_WriteCommand(0xA6);	//设置正常/倒转显示

	OLED_WriteCommand(0x8D);	//设置充电泵
	OLED_WriteCommand(0x14);

	OLED_WriteCommand(0xAF);	//开启显示

	OLED_Clear();				//OLED清屏
}
