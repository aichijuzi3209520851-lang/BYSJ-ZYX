# 性能探针 v3: 定位剩余布局成本的归属。
# 三种场景分别计时, 用于区分「每次 Resize 的基线成本」与「跨断点的一次性成本」。
#   A: 同档位内小幅改宽 (1240 -> 1300, 都在 >=1120 且 >=1200 的宽档)
#   B: 跨宽度断点      (1300 -> 900,  跨 1120/1200)
#   C: 跨高度断点      (高 728 -> 480, 跨 700 与 560)
# 用法: powershell -File perf_probe3.ps1 -TargetPid <pid> [-Cycles 8]
param(
    [Parameter(Mandatory = $true)][int]$TargetPid,
    [int]$Cycles = 8
)

$sig = @'
using System;
using System.Runtime.InteropServices;
public class Probe3 {
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll", CharSet=CharSet.Auto)] public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wp, IntPtr lp, uint flags, uint timeout, out IntPtr result);
    public struct RECT { public int Left, Top, Right, Bottom; }
    public static void Sync(IntPtr hwnd) { IntPtr r; SendMessageTimeout(hwnd, 0x0000, IntPtr.Zero, IntPtr.Zero, 0x0001, 20000, out r); }
}
'@
Add-Type -TypeDefinition $sig -Language CSharp | Out-Null

$proc = Get-Process -Id $TargetPid -ErrorAction Stop
$hwnd = $proc.MainWindowHandle
if ($hwnd -eq [IntPtr]::Zero) { Write-Host "ERR: no main window"; exit 1 }

$r = New-Object Probe3+RECT
[Probe3]::GetWindowRect($hwnd, [ref]$r) | Out-Null
$x = $r.Left; $y = $r.Top

function Measure-Scenario($w, $h, $label, $n) {
    $times = New-Object System.Collections.Generic.List[double]
    for ($i = 0; $i -lt $n; $i++) {
        # 每轮都先回到起点尺寸, 保证每次都真的发生该场景的跨越
        [Probe3]::SetWindowPos($hwnd, [IntPtr]::Zero, $x, $y, $script:startW, $script:startH, 0x0014) | Out-Null
        [Probe3]::Sync($hwnd)
        $t = [System.Diagnostics.Stopwatch]::StartNew()
        [Probe3]::SetWindowPos($hwnd, [IntPtr]::Zero, $x, $y, $w, $h, 0x0014) | Out-Null
        [Probe3]::Sync($hwnd)
        $t.Stop()
        $times.Add($t.Elapsed.TotalMilliseconds)
    }
    $s = $times | Sort-Object
    $avg = ($times | Measure-Object -Average).Average
    Write-Host ("{0,-22} avg {1,8:N1} ms | min {2,8:N1} | max {3,8:N1}" -f $label, $avg, $s[0], $s[$s.Count - 1])
}

# 先把窗口放到宽档基准 1300x800
$script:startW = 1300; $script:startH = 800
[Probe3]::SetWindowPos($hwnd, [IntPtr]::Zero, $x, $y, $script:startW, $script:startH, 0x0014) | Out-Null
[Probe3]::Sync($hwnd)
Write-Host ("base=1300x800 hwnd=0x{0:X}" -f [int64]$hwnd)
Write-Host ""

Measure-Scenario 1340 800 "A same-tier +40w" $Cycles
Measure-Scenario 900  800 "B cross-width"    $Cycles
Measure-Scenario 1300 480 "C cross-height"   $Cycles
Measure-Scenario 1360 860 "D both small"     $Cycles

[Probe3]::SetWindowPos($hwnd, [IntPtr]::Zero, $x, $y, 1300, 800, 0x0014) | Out-Null
