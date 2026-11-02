# 性能探针 v2: 分离「布局成本」与「重绘成本」, 并跟踪 GDI/User 句柄是否稳定。
#   (a) 只改尺寸 -> 阻塞到 WM_SIZE 处理完   = 布局/重排成本
#   (b) 再强制整窗立即重绘                 = 绘制成本
# 用法: powershell -File perf_probe.ps1 -TargetPid <pid> [-Cycles 30]
param(
    [Parameter(Mandatory = $true)][int]$TargetPid,
    [int]$Cycles = 30
)

$sig = @'
using System;
using System.Runtime.InteropServices;
public class Probe2 {
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll", CharSet=CharSet.Auto)] public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wp, IntPtr lp, uint flags, uint timeout, out IntPtr result);
    [DllImport("user32.dll")] public static extern bool RedrawWindow(IntPtr hWnd, IntPtr rect, IntPtr rgn, uint flags);
    [DllImport("user32.dll")] public static extern int GetGuiResources(IntPtr hProcess, int flags);
    [DllImport("kernel32.dll")] public static extern IntPtr OpenProcess(int access, bool inherit, int pid);
    [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);
    public struct RECT { public int Left, Top, Right, Bottom; }

    static IntPtr Open(int pid) { return OpenProcess(0x0400, false, pid); }
    public static int Gdi(int pid) { IntPtr h = Open(pid); if (h == IntPtr.Zero) return -1; try { return GetGuiResources(h, 0); } finally { CloseHandle(h); } }
    public static int Usr(int pid) { IntPtr h = Open(pid); if (h == IntPtr.Zero) return -1; try { return GetGuiResources(h, 1); } finally { CloseHandle(h); } }
    public static void Sync(IntPtr hwnd) { IntPtr r; SendMessageTimeout(hwnd, 0x0000, IntPtr.Zero, IntPtr.Zero, 0x0001, 10000, out r); }
}
'@
Add-Type -TypeDefinition $sig -Language CSharp | Out-Null

$proc = Get-Process -Id $TargetPid -ErrorAction Stop
$hwnd = $proc.MainWindowHandle
if ($hwnd -eq [IntPtr]::Zero) { Write-Host "ERR: no main window"; exit 1 }
if (-not [Probe2]::IsWindow($hwnd)) { Write-Host "ERR: bad hwnd"; exit 1 }

$r = New-Object Probe2+RECT
[Probe2]::GetWindowRect($hwnd, [ref]$r) | Out-Null
$baseW = $r.Right - $r.Left; $baseH = $r.Bottom - $r.Top
$x = $r.Left; $y = $r.Top

Write-Host ("target={0} hwnd=0x{1:X} base={2}x{3}" -f $proc.ProcessName, [int64]$hwnd, $baseW, $baseH)

# 预热: 建字体/路径/画刷缓存
for ($i = 0; $i -lt 3; $i++) {
    [Probe2]::SetWindowPos($hwnd, [IntPtr]::Zero, $x, $y, $baseW - 40, $baseH - 30, 0x0014) | Out-Null
    [Probe2]::Sync($hwnd)
    [Probe2]::SetWindowPos($hwnd, [IntPtr]::Zero, $x, $y, $baseW, $baseH, 0x0014) | Out-Null
    [Probe2]::Sync($hwnd)
}
$gdiW = [Probe2]::Gdi($TargetPid); $usrW = [Probe2]::Usr($TargetPid)
Write-Host ("warm: GDI={0} USER={1}" -f $gdiW, $usrW)

$layout = New-Object System.Collections.Generic.List[double]
$paint  = New-Object System.Collections.Generic.List[double]

for ($i = 0; $i -lt $Cycles; $i++) {
    if ($i % 2 -eq 0) { $w = [int]($baseW * 0.72); $h = [int]($baseH * 0.66) }
    else { $w = $baseW; $h = $baseH }

    $t1 = [System.Diagnostics.Stopwatch]::StartNew()
    [Probe2]::SetWindowPos($hwnd, [IntPtr]::Zero, $x, $y, $w, $h, 0x0014) | Out-Null
    [Probe2]::Sync($hwnd)                 # 只等布局, 不主动刷绘制
    $t1.Stop()
    $layout.Add($t1.Elapsed.TotalMilliseconds)

    $t2 = [System.Diagnostics.Stopwatch]::StartNew()
    [Probe2]::RedrawWindow($hwnd, [IntPtr]::Zero, [IntPtr]::Zero, 0x0001 -bor 0x0100) | Out-Null
    $t2.Stop()
    $paint.Add($t2.Elapsed.TotalMilliseconds)
}

$gdiE = [Probe2]::Gdi($TargetPid); $usrE = [Probe2]::Usr($TargetPid)

function Stat($name, $list) {
    $s = $list | Sort-Object
    $avg = ($list | Measure-Object -Average).Average
    $p50 = $s[[int]($s.Count * 0.5)]
    $p95 = $s[[Math]::Min($s.Count - 1, [int]($s.Count * 0.95))]
    $mx  = ($list | Measure-Object -Maximum).Maximum
    Write-Host ("{0,-8} avg {1,7:N1} ms | p50 {2,7:N1} | p95 {3,7:N1} | max {4,7:N1}" -f $name, $avg, $p50, $p95, $mx)
}

Write-Host ""
Write-Host ("==== {0} cycles ====" -f $Cycles)
Stat "layout" $layout
Stat "paint"  $paint
Write-Host ("GDI  warm {0} -> end {1}  delta {2}" -f $gdiW, $gdiE, ($gdiE - $gdiW))
Write-Host ("USER warm {0} -> end {1}  delta {2}" -f $usrW, $usrE, ($usrE - $usrW))

[Probe2]::SetWindowPos($hwnd, [IntPtr]::Zero, $x, $y, $baseW, $baseH, 0x0014) | Out-Null
