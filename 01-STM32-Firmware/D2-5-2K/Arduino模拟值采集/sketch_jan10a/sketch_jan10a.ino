int potPin = A0;  // 定义电位器连接的模拟输入引脚

void setup() {
  Serial.begin(9600);  // 初始化串行通信

}

void loop() {
  int potValue = analogRead(potPin);  // 读取电位器的模拟值
  Serial.println(potValue);           // 在串行监视器中显示读数
  delay(500);                         // 短暂延时
  
}
