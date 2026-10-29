using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

// ============================================================
//  EMQX 本地 MQTT 服务器 · 一键启动器
//  功能:自动定位 EMQX → 后台启动 → 等待 Web 控制台就绪 → 打开浏览器
//  用法:直接双击运行  = 启动 EMQX 并打开 Web 控制台
//        命令行参数 stop   = 停止 EMQX(如:MQTT.exe stop)
//        命令行参数 status = 只查询当前状态,不做任何变更
// ============================================================

// EMQX 默认 Web 控制台端口是 18083,但它落在 Windows 的"保留/排除端口区间"
// (本机实测为 18027-18126)内,绑定会抛 eacces,所以实际端口必须从
// etc/emqx.conf 里读,读不到才退回默认候选。
const int DefaultDashboardPort = 18083;
const int MqttPort = 1883;
const int DashboardFallbackPort = 18200;

// 运行期确定的实际值(定位到安装目录后会重新赋值)
int DashboardPort = DefaultDashboardPort;

// 没有真实控制台时(例如输出被重定向、由服务或计划任务拉起),设置标题会抛
// IOException,不能让这一行把整个启动流程打断。
try { Console.Title = "EMQX 一键启动器"; } catch { }
try { Console.OutputEncoding = Encoding.UTF8; } catch { }

Banner();

string mode = args.Length > 0 ? args[0].Trim().ToLowerInvariant() : "start";

// ---------- 分支:status 只查询 ----------
if (mode is "status")
{
    // 先定位目录,好把配置里的真实 dashboard 端口读出来,再判端口
    string? whereBin0 = FindEmqxBinDir();
    AdoptConfiguredPort(whereBin0);

    bool webUp = ResolveLiveDashboardPort();
    bool mqttUp = IsPortOpen(MqttPort);

    if (webUp || mqttUp)
    {
        Msg("✔ EMQX 正在运行。", ConsoleColor.Green);
        Msg($"  MQTT  接入 :{(mqttUp ? "已监听" : "未监听")}  {MqttPort}", ConsoleColor.DarkGray);
        Msg($"  Web 控制台:{(webUp ? "已监听" : "未监听")}  {DashboardPort}", ConsoleColor.DarkGray);
    }
    else
    {
        Msg($"… EMQX 当前未运行({MqttPort} 与 {DashboardPort} 端口均无响应)。", ConsoleColor.Yellow);
    }

    Msg(whereBin0 is null ? "  (未定位到 EMQX 安装目录)" : $"  安装目录:{whereBin0}", ConsoleColor.DarkGray);
    Exit();
    return;
}

// ---------- 分支:stop 停止 ----------
if (mode is "stop")
{
    string? stopDir = FindEmqxBinDir();
    AdoptConfiguredPort(stopDir);

    if (!IsEmqxUp())
    {
        Msg("… EMQX 当前未运行,无需停止。", ConsoleColor.Yellow);
        Exit();
        return;
    }
    if (stopDir is null)
    {
        Msg("✘ 检测到 EMQX 正在运行,但没找到它的安装目录,无法执行停止命令。", ConsoleColor.Red);
        Msg("  请手动结束 emqx 相关进程,或在 EMQX 的 bin 目录执行 emqx.cmd stop。", ConsoleColor.Yellow);
        Exit(pause: true);
        return;
    }
    Msg("▶ 正在停止 EMQX ...", ConsoleColor.Cyan);
    RunEmqxCommand(stopDir, "stop");
    Msg("✔ 已执行停止命令,EMQX 正在关闭。", ConsoleColor.Green);
    Exit();
    return;
}

// ---------- 步骤 1:先定位目录,读出真实端口;再判端口,判断是否已在运行 ----------
// 顺序很重要:以前是先找目录再判端口,一旦目录定位失败(例如安装路径变了),
// 即便 EMQX 明明在运行也会报"找不到安装目录",体验很糟。
// 另:必须先读配置拿到真实 dashboard 端口,否则 18200 在听也会被判成未启动。
string? binDir = FindEmqxBinDir();
AdoptConfiguredPort(binDir);

if (ResolveLiveDashboardPort() || IsPortOpen(MqttPort))
{
    Msg("✔ 检测到 EMQX 已在运行,无需重复启动。", ConsoleColor.Green);
    OpenDashboard();
    Exit();
    return;
}

// ---------- 步骤 2:校验安装目录 ----------
if (binDir is null)
{
    ShowNotFound();
    Exit(pause: true);
    return;
}
Msg($"✔ 已定位 EMQX 目录:{binDir}", ConsoleColor.Green);

// ---------- 步骤 3:执行 emqx start ----------
Msg("▶ 正在启动 EMQX(冷启动可能稍慢,首次约 20~60 秒)...", ConsoleColor.Cyan);
bool ok = RunEmqxCommand(binDir, "start");
if (!ok)
{
    Msg("✘ 启动命令执行失败,请检查上方输出,或 EMQX 目录下 log 文件夹中的日志。", ConsoleColor.Red);
    Exit(pause: true);
    return;
}

// ---------- 步骤 4:轮询等待就绪(Web 控制台或 MQTT 1883),最长 120 秒 ----------
Console.Write("… 等待 EMQX 就绪 ");
DateTime deadline = DateTime.UtcNow.AddSeconds(120);
while (DateTime.UtcNow < deadline && !(ResolveLiveDashboardPort() || IsPortOpen(MqttPort, 300)))
{
    Console.Write("·");
    Thread.Sleep(1000);
}
Console.WriteLine();

bool webReady = ResolveLiveDashboardPort();
bool mqttReady = IsPortOpen(MqttPort);

if (!webReady && !mqttReady)
{
    Msg("✘ 等待超时:EMQX 未能启动,请查看 EMQX 目录下 log 文件夹中的日志。", ConsoleColor.Red);
    Msg($"  已尝试探测的端口:{MqttPort}(MQTT) / {DashboardPort}(Web)", ConsoleColor.DarkGray);
    Exit(pause: true);
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
    Msg("✘ 未找到 EMQX 安装目录!", ConsoleColor.Red);
    Msg("", ConsoleColor.Gray);
    Msg("  启动器会按下面的顺序查找:", ConsoleColor.Yellow);
    Msg("    1) 本程序旁边的 emqx-path.txt(里面写 EMQX 的 bin 目录,最省事);", ConsoleColor.Yellow);
    Msg("    2) 环境变量 EMQX_HOME;", ConsoleColor.Yellow);
    Msg("    3) 常见路径 C:\\/D:\\ 下的 EMQX\\emqx-*-windows-amd64;", ConsoleColor.Yellow);
    Msg("    4) C/D 盘 1~3 级目录中形如 emqx-*-windows-amd64 的文件夹。", ConsoleColor.Yellow);
    Msg("", ConsoleColor.Gray);
    Msg("  最快解决办法:把下面这行写进 emqx-path.txt,与 MQTT.exe 放在同一目录 ——", ConsoleColor.Cyan);
    Msg("    D:\\EMQX\\emqx-5.0.26-windows-amd64\\bin", ConsoleColor.White);
    Msg("  提示:EMQX 不能放在很深的路径里,否则 Erlang 虚拟机会启动异常。", ConsoleColor.DarkGray);
}

/// <summary>
/// 结束程序。成功路径不阻塞(一键启动就该自己关掉窗口),
/// 失败路径才停下来等回车,好让用户看清报错。
/// </summary>
void Exit(bool pause = false)
{
    Console.WriteLine();
    if (!pause || Console.IsInputRedirected)
    {
        if (!pause) Msg("(本窗口将在 3 秒后自动关闭,EMQX 会继续在后台运行)", ConsoleColor.DarkGray);
        for (int i = 3; i > 0 && !Console.IsInputRedirected; i--) { Thread.Sleep(1000); }
        return;
    }
    Msg("按回车键关闭本窗口(EMQX 会继续在后台运行)...", ConsoleColor.DarkGray);
    try { Console.ReadLine(); } catch { }
}

void Msg(string text, ConsoleColor color)
{
    Console.ForegroundColor = color;
    Console.WriteLine(text);
    Console.ResetColor();
}

/// <summary>判断某个目录是否真的是 EMQX 的 bin 目录(必须含 emqx.cmd)。</summary>
static bool IsEmqxBinDir(string dir)
{
    try
    {
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return false;
        return File.Exists(Path.Combine(dir, "emqx.cmd"));
    }
    catch { return false; }
}

/// <summary>目录名是否像 EMQX 发行包(emqx-5.0.26-windows-amd64 / emqx 等)。</summary>
static bool LooksLikeEmqxDir(string name)
{
    string n = name.ToLowerInvariant();
    if (!n.StartsWith("emqx")) return false;
    return n.Contains("windows") || System.Text.RegularExpressions.Regex.IsMatch(n, @"^emqx-\d");
}

/// <summary>
/// 定位 EMQX 的 bin 目录。多级候选,逐项独立容错。
/// 【关键修复】原实现把整个扫描循环包在一个 try/catch 里,遇到 D:\Config.Msi
/// 这类拒绝访问的系统目录(会抛 UnauthorizedAccessException)就直接跳出,
/// 导致按字母序排在它后面的 D:\EMQX 永远扫不到,程序误报"找不到安装目录"。
/// 现在每个目录独立 try/catch,单个目录不可读只跳过它,不影响后续目录。
/// </summary>
string? FindEmqxBinDir()
{
    string exeDir = AppContext.BaseDirectory;

    // ---------- 候选 1:外置配置文件(用户可手工指定,最可靠) ----------
    foreach (string cfgName in new[] { "emqx-path.txt", "emqx-path.cfg" })
    {
        try
        {
            string cfg = Path.Combine(exeDir, cfgName);
            if (!File.Exists(cfg)) continue;
            foreach (string rawLine in File.ReadAllLines(cfg))
            {
                string line = rawLine.Trim().Trim('"');
                if (line.Length == 0 || line.StartsWith("#")) continue;
                if (IsEmqxBinDir(line)) return line;
                // 允许用户写发行包根目录,自动补 bin
                string bin = Path.Combine(line, "bin");
                if (IsEmqxBinDir(bin)) return bin;
            }
        }
        catch { /* 配置文件读不了就跳过 */ }
    }

    // ---------- 候选 2:环境变量 ----------
    try
    {
        string? env = Environment.GetEnvironmentVariable("EMQX_HOME");
        if (!string.IsNullOrWhiteSpace(env))
        {
            string e = env.Trim().Trim('"');
            if (IsEmqxBinDir(e)) return e;
            string ebin = Path.Combine(e, "bin");
            if (IsEmqxBinDir(ebin)) return ebin;
        }
    }
    catch { }

    // ---------- 候选 3:常见安装位置(含本机实际的 D:\EMQX 布局) ----------
    string[] preferred =
    {
        @"D:\EMQX-MQTT\emqx-5.0.26-windows-amd64\bin",
        @"D:\EMQX\emqx-5.0.26-windows-amd64\bin",
        @"D:\emqx\emqx-5.0.26-windows-amd64\bin",
        @"D:\emqx-5.0.26-windows-amd64\bin",
        @"C:\EMQX\emqx-5.0.26-windows-amd64\bin",
        @"C:\emqx-5.0.26-windows-amd64\bin",
    };
    foreach (string p in preferred)
        if (IsEmqxBinDir(p)) return p;

    // ---------- 候选 4:扫描 C/D 盘 1~3 级目录,逐目录容错 ----------
    foreach (string root in new[] { @"D:\", @"C:\" })
    {
        try { if (!Directory.Exists(root)) continue; }
        catch { continue; }

        string[] level1;
        try { level1 = Directory.GetDirectories(root); }
        catch { continue; }   // 连根目录都读不了,换下一个盘

        foreach (string l1 in level1)
        {
            // 情况 A:EMQX 直接放在 D:\emqx-5.0.26-windows-amd64\bin
            try
            {
                if (LooksLikeEmqxDir(Path.GetFileName(l1)))
                {
                    string b = Path.Combine(l1, "bin");
                    if (IsEmqxBinDir(b)) return b;
                }
            }
            catch { }

            // 情况 B:EMQX 放在 D:\EMQX\emqx-5.0.26-windows-amd64\bin(本机就是这种)
            string[] level2;
            try { level2 = Directory.GetDirectories(l1); }
            catch { continue; }   // 【修复】只跳过这一个目录,不再中断整轮扫描

            foreach (string l2 in level2)
            {
                try
                {
                    if (LooksLikeEmqxDir(Path.GetFileName(l2)))
                    {
                        string b = Path.Combine(l2, "bin");
                        if (IsEmqxBinDir(b)) return b;
                    }
                }
                catch { }

                // 情况 C:再深一层 D:\xxx\yyy\emqx-*-windows-amd64\bin —— 只对名字像容器的目录展开
                string l2name = "";
                try { l2name = Path.GetFileName(l2); } catch { }
                if (l2name.Length == 0) continue;
                string[] level3;
                try { level3 = Directory.GetDirectories(l2); }
                catch { continue; }

                foreach (string l3 in level3)
                {
                    try
                    {
                        if (LooksLikeEmqxDir(Path.GetFileName(l3)))
                        {
                            string b = Path.Combine(l3, "bin");
                            if (IsEmqxBinDir(b)) return b;
                        }
                    }
                    catch { }
                }
            }
        }
    }

    return null;
}

/// <summary>检测本机某个 TCP 端口是否已处于监听状态</summary>
static bool IsPortOpen(int port, int timeoutMs = 800)
{
    try
    {
        using var client = new TcpClient();
        IAsyncResult ar = client.BeginConnect(IPAddress.Loopback, port, null, null);
        if (!ar.AsyncWaitHandle.WaitOne(timeoutMs))
            return false;
        client.EndConnect(ar);
        return true;
    }
    catch
    {
        return false;
    }
}

/// <summary>
/// 把 DashboardPort 校正成"真正有人在听"的那个端口。
/// 顺序:已解析值 → EMQX 默认 18083 → 本机备用 18200。
/// </summary>
bool ResolveLiveDashboardPort()
{
    if (IsPortOpen(DashboardPort)) return true;
    foreach (int candidate in new[] { DefaultDashboardPort, DashboardFallbackPort })
    {
        if (candidate == DashboardPort) continue;
        if (IsPortOpen(candidate)) { DashboardPort = candidate; return true; }
    }
    return false;
}

/// <summary>EMQX 是否已在运行:MQTT 1883 在听,或 Web 控制台在听,二者之一即可</summary>
bool IsEmqxUp() => IsPortOpen(MqttPort) || ResolveLiveDashboardPort();

/// <summary>
/// 从 EMQX 自己的配置文件里读出 dashboard 实际绑定端口。
/// 【为什么必须读配置】EMQX 默认的 18083 落在这台机器的 Windows 保留端口区间
/// (实测 18027-18126)内,绑定会抛 eacces;因此 etc/emqx.conf 已改成 18200。
/// 启动器若写死探 18083,EMQX 明明起来了也会被判成"未就绪"并超时。
/// cluster.hocon 是运行时改动的落地处,优先级高于 emqx.conf。
/// </summary>
static int? ReadDashboardPortFromConf(string binDir)
{
    try
    {
        string root = Path.GetFullPath(Path.Combine(binDir, ".."));
        string[] files =
        {
            Path.Combine(root, "data", "configs", "cluster.hocon"),
            Path.Combine(root, "etc", "emqx.conf"),
        };

        foreach (string f in files)
        {
            if (!File.Exists(f)) continue;

            // 用块栈定位 dashboard { listeners.http { bind = x } },避免误读其它 listener 的 bind
            var stack = new List<string>();
            foreach (string raw in File.ReadAllLines(f))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith("##")) continue;

                if (line.EndsWith("{"))
                {
                    string name = line[..^1].Trim().TrimEnd('=').Trim().Trim('"');
                    stack.Add(name);
                    continue;
                }
                if (line.StartsWith("}"))
                {
                    if (stack.Count > 0) stack.RemoveAt(stack.Count - 1);
                    continue;
                }

                bool inDashboard = stack.Any(s => s.StartsWith("dashboard", StringComparison.OrdinalIgnoreCase));
                bool inHttp = stack.Any(s => s.StartsWith("listeners", StringComparison.OrdinalIgnoreCase)
                                             || s.StartsWith("http", StringComparison.OrdinalIgnoreCase));
                if (!inDashboard || !inHttp) continue;

                var m = System.Text.RegularExpressions.Regex.Match(line, @"^bind\s*=\s*""?([0-9]+)""?$");
                if (!m.Success)
                    m = System.Text.RegularExpressions.Regex.Match(line, @"^bind\s*=\s*""?[0-9.]+:([0-9]+)""?$");
                if (m.Success && int.TryParse(m.Groups[1].Value, out int p) && p > 0 && p < 65536)
                    return p;
            }
        }
    }
    catch { /* 读不到就用默认候选端口兜底 */ }
    return null;
}

/// <summary>已定位到安装目录后,把 DashboardPort 更新为配置里的真实端口</summary>
void AdoptConfiguredPort(string? binDir)
{
    if (binDir is null) return;
    int? conf = ReadDashboardPortFromConf(binDir);
    if (conf.HasValue) DashboardPort = conf.Value;
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

    bool exited = p.WaitForExit(60000);

    string output = ReadFileShared(outFile).TrimEnd();
    if (output.Length > 0)
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine(output);
        Console.ResetColor();
    }
    try { File.Delete(outFile); } catch { /* beam 可能仍持有文件句柄,忽略 */ }

    // 若 60 秒内没退出,不判定失败,交给后续端口轮询做最终裁决
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
    bool webUp = ResolveLiveDashboardPort();

    if (webUp)
    {
        Msg("✔ EMQX Web 控制台已就绪,正在打开浏览器 ...", ConsoleColor.Green);
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
    }
    else
    {
        Msg("… MQTT 已在运行,但 Web 控制台端口没有响应(可能被配置关掉了)。", ConsoleColor.Yellow);
        Msg($"  如需控制台,请检查 EMQX 的 etc/emqx.conf 里 dashboard.listeners.http.bind。", ConsoleColor.DarkGray);
    }

    Msg($"  MQTT 接入:TCP {MqttPort}(上位机) / WebSocket 8083(微信小程序)", ConsoleColor.Cyan);
    Msg("  默认账号:admin    默认密码:public", ConsoleColor.Cyan);
    Msg("  小技巧:命令行运行 MQTT.exe stop 可停止服务器,status 查看状态。", ConsoleColor.DarkGray);
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
