@echo off
chcp 65001 >nul
echo ==========================================
echo MQTT真机调试网络诊断工具
echo ==========================================
echo.

echo [1/4] 检查电脑IP地址
echo ------------------------------------------
echo 请查找"无线局域网适配器 WLAN"部分的IPv4地址：
ipconfig | findstr /C:"无线" /C:"IPv4" /C:"WLAN"
echo.

echo [2/4] 检查EMQX进程状态
echo ------------------------------------------
tasklist | findstr /i "emqx" >nul 2>&1
if %errorlevel%==0 (
    echo [✓] EMQX服务正在运行
) else (
    echo [✗] 警告: EMQX服务未运行！请先启动EMQX
)
echo.

echo [3/4] 检查8083端口监听状态
echo ------------------------------------------
netstat -ano | findstr ":8083.*LISTENING" >nul 2>&1
if %errorlevel%==0 (
    echo [✓] 8083端口正在监听
    netstat -ano | findstr ":8083.*LISTENING"
) else (
    echo [✗] 警告: 8083端口未监听！EMQX WebSocket可能未启动
)
echo.

echo [4/4] 检查防火墙状态
echo ------------------------------------------
netsh advfirewall show allprofiles state | findstr /C:"状态"
echo.

echo ==========================================
echo 诊断完成
echo ==========================================
echo.
echo 【下一步操作】
echo 1. 将上面显示的IPv4地址记录下来（例如：192.168.1.100）
echo 2. 确保手机连接到与电脑相同的WiFi网络
echo 3. 在小程序中点击设置，修改服务器地址为该IP
echo 4. 使用"真机调试"功能扫码测试
echo.
echo 【如果仍无法连接】
echo - 尝试临时关闭防火墙测试
echo - 使用手机浏览器访问: http://你的IP:18083
echo - 查看完整指南: 真机调试MQTT连接指南.md
echo.
pause
