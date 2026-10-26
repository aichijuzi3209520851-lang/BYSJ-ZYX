using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DHT11
{
    /// <summary>
    /// 统一的应用图标解析器。让「exe 文件图标」「窗体标题栏图标」「任务栏图标」三者来自同一个源。
    ///
    /// 背景：csproj 里的 &lt;ApplicationIcon&gt; 只决定 **exe 文件本身** 内嵌哪张图标，
    /// 它<strong>不会</strong>自动变成窗体运行时显示的那个小图标。
    /// 不显式给 Form.Icon 赋值时，WinForms 用的是框架默认图标（那个蓝白小方块），
    /// 于是任务栏和标题栏跟 exe 文件图标对不上——这正是需要这个类的原因。
    ///
    /// 取值优先级（越靠前越优先，逐级降级，任何一级失败都不抛异常）：
    ///   1. 程序目录下的 ICON\app_icon.ico —— 允许美术/测试直接替换文件、不重新编译就能看效果；
    ///   2. 程序集内嵌资源 DHT11.ICON.app_icon.ico —— 单文件拷走、或 ICON 目录被删掉时仍然有图标；
    ///   3. 从 exe 自身提取的关联图标 —— csproj 已配 ApplicationIcon，这一级几乎必然成功；
    ///   4. 全部失败则返回 null，窗体保持系统默认图标，绝不影响启动。
    /// </summary>
    internal static class AppIcon
    {
        /// <summary>相对程序目录的图标文件路径（与 csproj 的 ApplicationIcon 保持一致）。</summary>
        private const string RelativeIconPath = @"ICON\app_icon.ico";

        /// <summary>内嵌资源的逻辑名，由 csproj 中的 LogicalName 指定。</summary>
        private const string EmbeddedResourceName = "DHT11.ICON.app_icon.ico";

        // 图标原始字节只读一次：多帧 ICO 解一次要几十毫秒，登录/注销来回切窗口时没必要反复读盘
        private static byte[] _rawBytes;
        private static bool _resolved;

        // 交给 Win32 的 HICON 必须活到窗口销毁为止，否则任务栏会掉图标。
        // 用弱表挂到窗体上，窗体被回收时这堆 Icon 一起被回收，不构成静态内存泄漏。
        private static readonly ConditionalWeakTable<Form, List<Icon>> _retained =
            new ConditionalWeakTable<Form, List<Icon>>();

        #region Win32：让标题栏小图标用 16px 帧、Alt+Tab 用 32px 帧，避免拉伸糊掉

        private const int WM_SETICON = 0x0080;
        private const int ICON_SMALL = 0;
        private const int ICON_BIG = 1;

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        #endregion

        /// <summary>
        /// 把新图标挂到窗体上。幂等，可以在构造函数里放心调用。
        /// </summary>
        internal static void Apply(Form form)
        {
            if (form == null) return;

            try
            {
                byte[] bytes = ResolveBytes();
                if (bytes == null || bytes.Length == 0) return;   // 四级全部失败：保持系统默认图标

                // ---- 第 1 步：常规赋值 ----
                // 传整份多帧 ICO，由 Windows 按当前 DPI 挑合适的帧（100%→16/32，150%→24/48）。
                Icon full = CreateIcon(bytes);
                if (full == null) return;
                form.Icon = full;

                // ---- 第 2 步：句柄就绪后再精修一次 ----
                // Form.Icon 的默认实现是把同一个 HICON 同时塞给 ICON_SMALL 和 ICON_BIG，
                // 大尺寸帧被缩到 16px 时边缘会糊。这里按系统实际请求的尺寸重新挑帧，各给各的。
                // 全部包在 try 里：万一某个系统/主题下拿不到句柄，退化成第 1 步的结果，不影响启动。
                EventHandler onShown = null;
                onShown = delegate
                {
                    form.Shown -= onShown;
                    try
                    {
                        PinExactFrame(form, bytes);
                    }
                    catch
                    {
                        // 精修是锦上添花，失败就保持 form.Icon 的效果
                    }
                };
                if (form.IsHandleCreated)
                {
                    try { PinExactFrame(form, bytes); } catch { }
                }
                else
                {
                    form.Shown += onShown;
                }
            }
            catch
            {
                // 图标永远不该成为程序起不来的理由
            }
        }

        /// <summary>按 ICON_SMALL / ICON_BIG 各自需要的尺寸挑帧，再分别 WM_SETICON。</summary>
        private static void PinExactFrame(Form form, byte[] bytes)
        {
            IntPtr hWnd = form.Handle;
            if (hWnd == IntPtr.Zero) return;

            Size smallSize = SystemInformation.SmallIconSize;   // 标题栏 + 任务栏
            Size bigSize = SystemInformation.IconSize;          // Alt+Tab / 大图标视图

            Icon small = CreateIcon(bytes, smallSize);
            Icon big = CreateIcon(bytes, bigSize);
            if (small == null || big == null) return;

            SendMessage(hWnd, WM_SETICON, (IntPtr)ICON_SMALL, small.Handle);
            SendMessage(hWnd, WM_SETICON, (IntPtr)ICON_BIG, big.Handle);

            // 交给 Win32 后对象必须继续存活，挂到窗体上保命
            List<Icon> keep;
            if (!_retained.TryGetValue(form, out keep))
            {
                keep = new List<Icon>(4);
                _retained.Add(form, keep);
            }
            keep.Add(small);
            keep.Add(big);
        }

        /// <summary>从缓存字节造一个 Icon（指定尺寸时挑最接近的帧）。</summary>
        private static Icon CreateIcon(byte[] bytes, Size? size = null)
        {
            if (bytes == null || bytes.Length == 0) return null;
            using (MemoryStream ms = new MemoryStream(bytes, false))
            {
                // Icon(Stream) 会把整段数据读进内存，不持有 stream，因此不存在文件被锁的问题
                return size.HasValue ? new Icon(ms, size.Value) : new Icon(ms);
            }
        }

        /// <summary>按优先级找出图标字节；找不到返回 null。</summary>
        private static byte[] ResolveBytes()
        {
            if (_resolved) return _rawBytes;
            _resolved = true;

            _rawBytes = ReadFromDisk() ?? ReadFromResource() ?? ReadFromExecutable();
            return _rawBytes;
        }

        /// <summary>① 程序目录 ICON\app_icon.ico</summary>
        private static byte[] ReadFromDisk()
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                if (string.IsNullOrEmpty(baseDir)) baseDir = Application.StartupPath;
                if (string.IsNullOrEmpty(baseDir)) return null;

                string path = Path.Combine(baseDir, RelativeIconPath);
                if (!File.Exists(path)) return null;

                // FileShare.ReadWrite：万一有资源编辑器正开着这个文件，也不至于直接抛 IOException
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    if (fs.Length <= 0 || fs.Length > 4 * 1024 * 1024) return null;  // 明显不是图标的文件直接跳过
                    byte[] buffer = new byte[fs.Length];
                    int read = 0;
                    while (read < buffer.Length)
                    {
                        int n = fs.Read(buffer, read, buffer.Length - read);
                        if (n <= 0) break;
                        read += n;
                    }
                    if (read != buffer.Length) return null;

                    // 校验 ICO 文件头（保留字 0 / 类型 1 / 至少一帧），防止有人把 png/jpg 直接改后缀放进来
                    if (buffer.Length < 22 || buffer[0] != 0 || buffer[1] != 0 || buffer[2] != 1 || buffer[3] != 0)
                        return null;
                    return buffer;
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>② 程序集内嵌资源</summary>
        private static byte[] ReadFromResource()
        {
            try
            {
                Assembly asm = typeof(AppIcon).Assembly;

                using (Stream stream = asm.GetManifestResourceStream(EmbeddedResourceName))
                {
                    if (stream != null && stream.Length > 0 && stream.Length <= 4 * 1024 * 1024)
                    {
                        byte[] buffer = new byte[stream.Length];
                        int read = 0;
                        while (read < buffer.Length)
                        {
                            int n = stream.Read(buffer, read, buffer.Length - read);
                            if (n <= 0) break;
                            read += n;
                        }
                        if (read == buffer.Length) return buffer;
                    }
                }

                // 兜底：万一 csproj 里的 LogicalName 被删了，就按后缀在资源清单里找一遍
                foreach (string name in asm.GetManifestResourceNames())
                {
                    if (name == null || !name.EndsWith("app_icon.ico", StringComparison.OrdinalIgnoreCase))
                        continue;

                    using (Stream stream = asm.GetManifestResourceStream(name))
                    {
                        if (stream == null || stream.Length <= 0) continue;
                        byte[] buffer = new byte[stream.Length];
                        if (stream.Read(buffer, 0, buffer.Length) == buffer.Length) return buffer;
                    }
                }
            }
            catch
            {
                // 忽略，交给下一级
            }
            return null;
        }

        /// <summary>③ 从 exe 自身提取（csproj 的 ApplicationIcon 已经把图标写进 PE 资源了）</summary>
        private static byte[] ReadFromExecutable()
        {
            try
            {
                string exe = Application.ExecutablePath;
                if (string.IsNullOrEmpty(exe)) return null;

                using (Icon extracted = Icon.ExtractAssociatedIcon(exe))
                {
                    if (extracted == null) return null;
                    using (MemoryStream ms = new MemoryStream())
                    {
                        extracted.Save(ms);           // 存成单帧 ICO（32bpp）
                        return ms.ToArray();
                    }
                }
            }
            catch
            {
                return null;
            }
        }
    }
}
