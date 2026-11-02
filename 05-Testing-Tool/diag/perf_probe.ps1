# 性能探针: 对运行中的 IoT Link Lab 反复改窗口尺寸, 采集
#   - GDI 句柄数 / User 句柄数  (泄漏与抖动)
#   - 每次 SetWindowPos 后窗口处理完 WM_SIZE 的耗时
# 用法: powershell -File perf_probe.ps1 -Pid <pid> [-Cycles 40]
param(
    [Parameter(Mandatory = $true)][int]$TargetPid,
    [int]$Cycles = 40
)

Add-Type -AssemblyName System.Windows.Forms | Out-Null

$sig = @'
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
public class Probe {
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll", CharSet=CharSet.Auto)] public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wp, IntPtr lp, uint flags, uint timeout, out IntPtr result);
    [DllImport("user32.dll")] public static extern bool RedrawWindow(IntPtr hWnd, IntPtr rect, IntPtr rgn, uint flags);
    [DllImport("user32.dll")] public static extern bool UpdateWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern int GetGuiResources(IntPtr hProcess, int flags);
    [DllImport("kernel32.dll")] public static extern IntPtr OpenProcess(int access, bool inherit, int pid);
    [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);
    public struct RECT { public int Left, Top, Right, Bottom; }

    public static int GdiHandles(int pid) {
        IntPtr h = OpenProcess(0x0400, false, pid);   // PROCESS_QUERY_INFORMATION
        if (h == IntPtr.Zero) return -1;
        try { return GetGuiResources(h, 0); } finally { CloseHandle(h); }
    }
    public static int UserHandles(int pid) {
        IntPtr h = OpenProcess(0x0400, false, pid);
        if (h == IntPtr.Zero) return -1;
        try { return GetGuiResources(h, 1); } finally { CloseHandle(h); }
    }
    // 0x0002 = WM_SIZE 排空; 用一个自定消息不行, 这里发 WM_NULL 做同步屏障
    public static void Sync(IntPtr hwnd) {
        IntPtr r;
        SendMessageTimeout(hwnd, 0x0000 /*WM_NULL*/, IntPtr.Zero, IntPtr.Zero, 0x0001 /*BLOCK*/, 5000, out r);
    }
}
'@
Add-Type -TypeDefinition $sig -Language CSharp | Out-Null

$proc = Get-Process -Id $TargetPid -ErrorAction Stop
$hwnd = $proc.MainWindowHandle
if ($hwnd -eq [IntPtr]::Zero) { Write-Error "主窗口句柄为空, 程序可能尚未初始化完成"; exit 1 }
if (-not [Probe]::IsWindow($hwnd)) { Write-Error "窗口句柄无效"; exit 1 }

$rect = New-Object Probe+RECT
[Probe]::GetWindowRect($hwnd, [ref]$rect) | Out-Null
$baseW = $rect.Right - $rect.Left
$baseH = $rect.Bottom - $rect.Top
$x = $rect.Left; $y = $rect.Top

Write-Host ("窗口 {0}  初始 {1}x{2}  hwnd=0x{3:X}" -f $proc.ProcessName, $baseW, $baseH, [int64]$hwnd)
$gdi0 = [Probe]::GdiHandles($TargetPid)
$usr0 = [Probe]::UserHandles($TargetPid)
Write-Host ("基线: GDI={0}  User={1}" -f $gdi0, $usr0)

# 预热: 让字体/路径/画刷缓存先建立, 否则首轮会把"一次性分配"算进增长
for ($i = 0; $i -lt 3; $i++) {
    [Probe]::SetWindowPos($hwnd, [IntPtr]::Zero, $x, $y, $baseW - 40, $baseH - 30, 0x0014) | Out-Null
    [Probe]::Sync($hwnd)
    [Probe]::SetWindowPos($hwnd, [IntPtr]::Zero, $x, $y, $baseW, $baseH, 0x0014) | Out-Null
    [Probe]::Sync($hwnd)
}
$gdiWarm = [Probe]::GdiHandles($TargetPid)
$usrWarm = [Probe]::UserHandles($TargetPid)
Write-Host ("预热后: GDI={0}  User={1}" -f $gdiWarm, $usrWarm)

$sw = [System.Diagnostics.Stopwatch]::StartNew()
$samples = New-Object System.Collections.Generic.List[double]
for ($i = 0; $i -lt $Cycles; $i++) {
    # 交替大/小尺寸, 跨越 1120/1200/1280 三个宽度断点与 560/700 两个高度断点
    if ($i % 2 -eq 0) { $w = [int]($baseW * 0.72); $h = [int]($baseH * 0.66) }
    else { $w = $baseW; $h = $baseH }

    $t = [System.Diagnostics.Stopwatch]::StartNew()
    [Probe]::SetWindowPos($hwnd, [IntPtr]::Zero, $x, $y, $w, $h, 0x0014) | Out-Null
    [Probe]::Sync($hwnd)          # 阻塞到窗口把 WM_SIZE 处理完
    [Probe]::RedrawWindow($hwnd, [IntPtr]::Zero, [IntPtr]::Zero, 0x0001 -bor 0x0100) | Out-Null  # INVALIDATE|UPDATENOW
    $t.Stop()
    $samples.Add($t.Elapsed.TotalMilliseconds)
}
$sw.Stop()

$gdi1 = [Probe]::GdiHandles($TargetPid)
$usr1 = [Probe]::UserHandles($TargetPid)

$sorted = $samples | Sort-Object
$avg = ($samples | Measure-Object -Average).Average
$p50 = $sorted[[int]($sorted.Count * 0.5)]
$p95 = $sorted[[Math]::Min($sorted.Count - 1, [int]($sorted.Count * 0.95))]
$max = ($samples | Measure-Object -Maximum).Maximum

Write-Host ""
Write-Host "==== 结果 ===="
Write-Host ("循环 {0} 次改尺寸+强制重绘, 总耗时 {1:N0} ms" -f $Cycles, $sw.Elapsed.TotalMilliseconds)
Write-Host ("单次尺寸变更→布局→重绘: 平均 {0:N1} ms | p50 {1:N1} | p95 {2:N1} | 最大 {3:N1}" -f $avg, $p50, $p95, $max)
Write-Host ("GDI 句柄: 预热 {0} → 结束 {1}   净增 {2}" -f $gdiWarm, $gdi1, ($gdi1 - $gdiWarm))
Write-Host ("User 句柄: 预热 {0} → 结束 {1}   净增 {2}" -f $usrWarm, $usr1, ($usr1 - $usrWarm))

# 收尾: 复位窗口尺寸
[Probe]::SetWindowPos($hwnd, [IntPtr]::Zero, $x, $y, $baseW, $baseH, 0x0014) | Out-Null
