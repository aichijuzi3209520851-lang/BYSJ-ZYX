using System.Diagnostics;
using System.Runtime.InteropServices;

namespace IotLinkLab
{
    /// <summary>
    /// 轻量诊断计时器: 仅当环境变量 IOTLINK_DIAG=1 时生效, 结果追加到 %TEMP%\iotlink-diag.log。
    /// 用途: 定位"改窗口尺寸/切页"时的耗时归属, 生产运行零开销(未开启时所有方法立即返回)。
    /// </summary>
    internal static class Diag
    {
        private static readonly bool Enabled =
            Environment.GetEnvironmentVariable("IOTLINK_DIAG") == "1";

        private static readonly object Gate = new();
        private static readonly List<string> Buffer = new();
        private static int _depth;

        public static bool On => Enabled;

        public static string LogPath => Path.Combine(Path.GetTempPath(), "iotlink-diag.log");

        public static void Reset()
        {
            if (!Enabled) return;
            lock (Gate) { Buffer.Clear(); _depth = 0; }
            try { File.WriteAllText(LogPath, ""); } catch { }
        }

        public static void Mark(string text)
        {
            if (!Enabled) return;
            lock (Gate) { Buffer.Add(Indent() + "# " + text); }
        }

        public static Scope T(string name)
        {
            if (!Enabled) return default;
            return new Scope(name);
        }

        public static void Flush()
        {
            if (!Enabled) return;
            string[] lines;
            lock (Gate) { lines = Buffer.ToArray(); Buffer.Clear(); }
            if (lines.Length == 0) return;
            try { File.AppendAllText(LogPath, string.Join(Environment.NewLine, lines) + Environment.NewLine); }
            catch { }
        }

        private static string Indent() => new string(' ', Math.Max(0, _depth) * 2);

        // ── 计数器: 统计一段时间内实际发生了多少次 paint / layout ──
        // 用途: 我们的布局代码都在 0.15ms 以内, 秒级耗时发生在返回之后的原生绘制阶段,
        // 所以必须数"次数"而不是"时长", 才能判断是不是重绘风暴。
        public static int PaintCount;
        public static int LayoutCount;
        public static int ArrangeCount;

        public static void BumpPaint() { if (Enabled) Interlocked.Increment(ref PaintCount); }
        public static void BumpLayout() { if (Enabled) Interlocked.Increment(ref LayoutCount); }
        public static void BumpArrange() { if (Enabled) Interlocked.Increment(ref ArrangeCount); }

        /// <summary>取走并清零计数器, 返回 "paint=N layout=N arrange=N"。</summary>
        public static string TakeCounters()
        {
            if (!Enabled) return string.Empty;
            int p = Interlocked.Exchange(ref PaintCount, 0);
            int l = Interlocked.Exchange(ref LayoutCount, 0);
            int a = Interlocked.Exchange(ref ArrangeCount, 0);
            return "paint=" + p + " erase=" + l + " size=" + a;
        }

        /// <summary>
        /// 全局消息过滤器: 统计原生消息循环里实际派发了多少次 PAINT/ERASEBKGND/SIZE。
        /// 自有布局代码都在 0.15ms 内, 秒级耗时发生在返回之后, 所以数消息次数才能定性。
        /// 仅 IOTLINK_DIAG=1 时安装。
        /// </summary>
        internal sealed class MessageCounter : IMessageFilter
        {
            private const int WM_PAINT = 0x000F;
            private const int WM_ERASEBKGND = 0x0014;
            private const int WM_SIZE = 0x0005;
            private const int WM_WINDOWPOSCHANGED = 0x0047;
            private const int WM_NCPAINT = 0x0085;

            public bool PreFilterMessage(ref Message m)
            {
                switch (m.Msg)
                {
                    case WM_PAINT: Interlocked.Increment(ref PaintCount); break;
                    case WM_ERASEBKGND: Interlocked.Increment(ref LayoutCount); break;
                    case WM_SIZE: Interlocked.Increment(ref ArrangeCount); break;
                    case WM_WINDOWPOSCHANGED:
                    case WM_NCPAINT: Interlocked.Increment(ref ArrangeCount); break;
                }
                return false;   // 不拦截, 照常派发
            }
        }

        public static void InstallMessageCounter()
        {
            if (!Enabled) return;
            Application.AddMessageFilter(new MessageCounter());
        }

        internal readonly struct Scope : IDisposable
        {
            private readonly string? _name;
            private readonly long _start;      // Stopwatch.GetTimestamp()
            private readonly int _myDepth;

            internal Scope(string name)
            {
                _name = name;
                lock (Gate) { _myDepth = _depth; _depth++; }
                _start = Stopwatch.GetTimestamp();
            }

            public void Dispose()
            {
                if (_name == null) return;
                double ms = (Stopwatch.GetTimestamp() - _start) * 1000.0 / Stopwatch.Frequency;
                lock (Gate)
                {
                    _depth = _myDepth;
                    Buffer.Add(Indent() + _name + " " + ms.ToString("0.00") + " ms");
                    if (_myDepth == 0) { /* 顶层结束时由调用方 Flush */ }
                }
                if (_myDepth == 0) Flush();
            }
        }
    }
}
