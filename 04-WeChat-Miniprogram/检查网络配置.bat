@echo off
chcp 65001 >nul
echo ==========================================
echo MQTT真机调试网络诊断工具
echo ==========================================
echo.

echo [1/5] 找手机能连的那个电脑地址（有默认网关的网卡）
echo ------------------------------------------
echo 只有一个网卡有"默认网关"，它就是手机要用的地址；
echo 没有网关的都是虚拟网卡（VMware / Hyper-V / Tailscale），手机连不上。
echo.
powershell -NoProfile -Command "Get-NetIPConfiguration | Where-Object { $_.IPv4Address -and $_.IPv4Address.IPAddress -notlike '127.*' -and $_.IPv4Address.IPAddress -notlike '169.254.*' } | ForEach-Object { $nic = $_.InterfaceAlias; $ip = $_.IPv4Address.IPAddress; $gw = $_.IPv4DefaultGateway.NextHop; if ($gw) { Write-Host ('  [手机就用这个] ' + $nic + '  ' + $ip) -ForegroundColor Green } else { Write-Host ('  [虚拟网卡, 勿用] ' + $nic + '  ' + $ip) -ForegroundColor DarkGray } }"
echo.
echo 原始 ipconfig 输出（供核对）：
ipconfig | findstr /C:"适配器" /C:"IPv4" /C:"默认网关"
echo.

echo [2/5] 检查EMQX进程状态
echo ------------------------------------------
tasklist | findstr /i "emqx" >nul 2>&1
if %errorlevel%==0 (
    echo [✓] EMQX服务正在运行
) else (
    echo [✗] 警告: EMQX服务未运行！请先启动EMQX
)
echo.

echo [3/5] 检查端口监听状态
echo ------------------------------------------
netstat -ano | findstr ":8083.*LISTENING" >nul 2>&1
if %errorlevel%==0 (
    echo [✓] 8083 WebSocket 端口正在监听（小程序用这个）
    netstat -ano | findstr ":8083.*LISTENING"
) else (
    echo [✗] 警告: 8083端口未监听！EMQX WebSocket可能未启动
)
netstat -ano | findstr ":1883.*LISTENING" >nul 2>&1
if %errorlevel%==0 (echo [✓] 1883 MQTT 端口正在监听（上位机/调试工具用这个）) else (echo [✗] 警告: 1883端口未监听)
netstat -ano | findstr ":18083.*LISTENING" >nul 2>&1
if %errorlevel%==0 (echo [✓] 18083 Web 控制台端口正在监听) else (echo [✗] 警告: 18083端口未监听)
echo.

echo [4/5] 检查防火墙状态
echo ------------------------------------------
netsh advfirewall show allprofiles state | findstr /C:"状态"
echo.

echo [5/5] 确认小程序连接配置
echo ------------------------------------------
echo 请检查小程序「配置」弹窗里填的内容：
echo   服务器地址 = 上面标 [手机就用这个] 的那个 IPv4
echo   WebSocket端口 = 8083
echo   订阅主题 = green/data   ^(填 green/control 会收到指令原文并报JSON错误^)
echo.

echo ==========================================
echo 诊断完成
echo ==========================================
echo.
echo 【下一步操作】
echo 1. 把上面标 [手机就用这个] 的 IPv4 填进小程序（不要填 127.0.0.1，也不要填虚拟网卡）
echo 2. 确保手机连接到与电脑相同的WiFi/热点
echo 3. 用手机浏览器访问 http://该IP:18083 ，能打开控制台就说明网络通了
echo 4. 使用"真机调试"功能扫码测试
echo.
echo 【如果模拟器能连、手机连不上】
echo - 先查是不是填了虚拟网卡地址（最常见的坑）
echo - 尝试临时关闭防火墙测试
echo - 查看完整指南: 真机调试MQTT连接指南.md
echo.
pause
