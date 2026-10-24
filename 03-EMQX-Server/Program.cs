using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

// ============================================================
//  EMQX 本地 MQTT 服务器 · 一键启动器
//  功能:自动定位 EMQX → 后台启动 → 等待 Web 控制台就绪 → 打开浏览器
//  用法:直接双击运行  = 启动 EMQX 并打开 Web 控制台
//        命令行参数 stop = 停止 EMQX(如:MQTT.exe stop)
// ============================================================

const int DashboardPort = 18083;

Console.Title = "EMQX 一键启动器";
Console.OutputEncoding = Encoding.UTF8;

Banner();

// ---------- 分支:命令行带 stop 参数,停止 EMQX ----------
if (args.Length > 0 && args[0].Equals("stop", StringComparison.OrdinalIgnoreCase))
{
    string? stopDir = FindEmqxBinDir();
    if (stopDir is null)
    {
        ShowNotFound();
    }
    else
    {
        Msg("▶ 正在停止 EMQX ...", ConsoleColor.Cyan);
        RunEmqxCommand(stopDir, "stop");
        Msg("✔ 已执行停止命令,EMQX 正在关闭。", ConsoleColor.Green);
    }
    Exit();
    return;
}

// ---------- 步骤 1:定位 EMQX 安装目录 ----------
// 只在 D 盘 1~2 级浅层目录中寻找:路径太深会导致 Erlang 启动异常
string? binDir = FindEmqxBinDir();
if (binDir is null)
{
    ShowNotFound();
    Exit();
    return;
}
Msg($"✔ 已定位 EMQX 目录:{binDir}", ConsoleColor.Green);

// ---------- 步骤 2:若 18083 端口已通,说明 EMQX 正在运行 ----------
if (IsDashboardUp())
{
    Msg("✔ 检测到 EMQX 已在运行,无需重复启动。", ConsoleColor.Green);
    OpenDashboard();
    Exit();
    return;
}

// ---------- 步骤 3:执行 emqx start ----------
Msg("▶ 正在启动 EMQX(冷启动可能稍慢)...", ConsoleColor.Cyan);
bool ok = RunEmqxCommand(binDir, "start");
if (!ok)
{
    Msg("✘ 启动命令执行失败,请检查上方输出,或 EMQX 目录下 log 文件夹中的日志。", ConsoleColor.Red);
    Exit();
    return;
}

// ---------- 步骤 4:轮询等待 Web 控制台(18083 端口)就绪,最长 90 秒 ----------
Console.Write("… 等待 Web 控制台就绪 ");
DateTime deadline = DateTime.UtcNow.AddSeconds(90);
while (DateTime.UtcNow < deadline && !IsDashboardUp())
{
    Console.Write("·");
    Thread.Sleep(1000);
}
Console.WriteLine();

if (!IsDashboardUp())
{
    Msg("✘ 等待超时:EMQX 未能启动,请查看 EMQX 目录下 log 文件夹中的日志。", ConsoleColor.Red);
    Exit();
    return;
}

// ---------- 步骤 5:就绪,打开浏览器并展示访问信息 ----------
OpenDashboard();
Exit();
return;

// ============================ 辅助函数 ============================

void Banner()
{
    Msg("══════════════════════════════════════════════", ConsoleColor.Cyan);
    Msg("      EMQX 本地 MQTT 服务器 · 一键启动器", ConsoleColor.Cyan);
    Msg("══════════════════════════════════════════════", ConsoleColor.Cyan);
}

void ShowNotFound()
{
    Msg("✘ 未能在 D 盘浅层目录(1~2 级)中找到 EMQX 安装目录!", ConsoleColor.Red);
    Msg("  请确保 emqx-5.0.26-windows-amd64 文件夹放在 D 盘下 1~2 级路径,", ConsoleColor.Yellow);
    Msg("  例如:D:\\EMQX-MQTT\\emqx-5.0.26-windows-amd64", ConsoleColor.Yellow);
    Msg("  提示:EMQX 不能放在很深的路径里,否则 Erlang 虚拟机会启动异常。", ConsoleColor.DarkGray);
}

void Exit()
{
    Console.WriteLine();
    Msg("按回车键关闭本窗口(EMQX 会继续在后台运行)...", ConsoleColor.DarkGray);
    Console.ReadLine();
}

void Msg(string text, ConsoleColor color)
{
    Console.ForegroundColor = color;
    Console.WriteLine(text);
    Console.ResetColor();
}

/// <summary>在 D 盘 1~2 级目录中搜索 emqx-*-windows-amd64\bin\emqx.cmd</summary>
string? FindEmqxBinDir()
{
    // 优先使用约定俗成的安装路径
    string[] preferred =
    {
        @"D:\EMQX-MQTT\emqx-5.0.26-windows-amd64\bin",
    };
    foreach (string p in preferred)
    {
        if (File.Exists(Path.Combine(p, "emqx.cmd")))
            return p;
    }

    // 兜底:扫描 D 盘下一级与两级目录
    try
    {
        if (!Directory.Exists(@"D:\"))
            return null;

        foreach (string l1 in Directory.GetDirectories(@"D:\"))
        {
            // 情况 A:EMQX 直接放在 D:\xxx\emqx-...(两级)
            string name1 = Path.GetFileName(l1);
            if (name1.StartsWith("emqx-", StringComparison.OrdinalIgnoreCase) &&
                name1.Contains("windows-amd64") &&
                File.Exists(Path.Combine(l1, "bin", "emqx.cmd")))
            {
                return Path.Combine(l1, "bin");
            }

            // 情况 B:EMQX 放在 D:\xxx\yyy\emqx-...(三级中的第二级容器)
            foreach (string l2 in Directory.GetDirectories(l1, "emqx-*windows-amd64"))
            {
                if (File.Exists(Path.Combine(l2, "bin", "emqx.cmd")))
                    return Path.Combine(l2, "bin");
            }
        }
    }
    catch
    {
        // 忽略无权限访问的目录
    }
    return null;
}

/// <summary>检测 EMQX Web 控制台端口(18083)是否已监听</summary>
bool IsDashboardUp()
{
    try
    {
        using var client = new TcpClient();
        IAsyncResult ar = client.BeginConnect(IPAddress.Loopback, DashboardPort, null, null);
        if (!ar.AsyncWaitHandle.WaitOne(800))
            return false;
        client.EndConnect(ar);
        return true;
    }
    catch
    {
        return false;
    }
}

/// <summary>调用 emqx.cmd 执行 start / stop 命令,并回显其输出</summary>
bool RunEmqxCommand(string binDirPath, string command)
{
    // 关键点:把输出重定向到临时文件,而不是用管道捕获。
    // 因为 emqx start 拉起的 Erlang 虚拟机(beam)会继承管道句柄并长期存活,
    // 管道永远等不到 EOF,程序会卡死;临时文件则没有这个问题。
    string outFile = Path.Combine(Path.GetTempPath(), $"emqx_{command}_{Guid.NewGuid():N}.log");

    var psi = new ProcessStartInfo
    {
        FileName = "cmd.exe",
        // /d /s /c ""D:\...\emqx.cmd" start > "临时文件" 2>&1"
        Arguments = $"/d /s /c \"\"{Path.Combine(binDirPath, "emqx.cmd")}\" {command} > \"{outFile}\" 2>&1\"",
        WorkingDirectory = binDirPath,
        UseShellExecute = false,
        CreateNoWindow = true,
    };

    using Process? p = Process.Start(psi);
    if (p is null)
    {
        Msg("✘ 无法创建进程!", ConsoleColor.Red);
        return false;
    }

    bool exited = p.WaitForExit(30000);

    string output = ReadFileShared(outFile).TrimEnd();
    if (output.Length > 0)
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine(output);
        Console.ResetColor();
    }
    try { File.Delete(outFile); } catch { /* beam 可能仍持有文件句柄,忽略 */ }

    // 若 30 秒内没退出,不判定失败,交给后续端口轮询做最终裁决
    if (!exited)
        return true;

    return p.ExitCode == 0;
}

/// <summary>以共享读方式读取文件(其他进程可能仍在写入/持有句柄)</summary>
static string ReadFileShared(string path)
{
    try
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var sr = new StreamReader(fs);
        return sr.ReadToEnd();
    }
    catch
    {
        return "";
    }
}

/// <summary>打开默认浏览器访问控制台,并打印局域网访问地址与默认账号</summary>
void OpenDashboard()
{
    Msg($"✔ EMQX Web 控制台已就绪,正在打开浏览器 ...", ConsoleColor.Green);
    try
    {
        Process.Start(new ProcessStartInfo($"http://127.0.0.1:{DashboardPort}") { UseShellExecute = true });
    }
    catch
    {
        Msg($"  浏览器打开失败,请手动访问:http://127.0.0.1:{DashboardPort}", ConsoleColor.Yellow);
    }

    List<IPAddress> ips = GetLanIPv4Addresses();
    if (ips.Count > 0)
    {
        Msg("  局域网访问地址(供手机 / STM32 开发板等设备使用):", ConsoleColor.Cyan);
        foreach (IPAddress ip in ips)
            Msg($"    http://{ip}:{DashboardPort}", ConsoleColor.Cyan);
    }
    Msg("  默认账号:admin    默认密码:public", ConsoleColor.Cyan);
    Msg("  小技巧:在命令行运行 MQTT.exe stop 可停止服务器。", ConsoleColor.DarkGray);
}

/// <summary>枚举本机所有在线网卡上的 IPv4 地址(用于局域网访问)</summary>
List<IPAddress> GetLanIPv4Addresses()
{
    var result = new List<IPAddress>();
    foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
    {
        if (nic.OperationalStatus != OperationalStatus.Up)
            continue;
        if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            continue;
        foreach (UnicastIPAddressInformation addr in nic.GetIPProperties().UnicastAddresses)
        {
            if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
                result.Add(addr.Address);
        }
    }
    return result;
}
