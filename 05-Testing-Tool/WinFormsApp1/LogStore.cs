using System.Text;

namespace IotLinkLab
{
    /// <summary>应用数据目录(单一真源): 设置 / 日志 / 导出。</summary>
    public static class AppPaths
    {
        public static string Root => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "IotLinkLab");

        public static string LogsDir => Path.Combine(Root, "logs");
        public static string ExportsDir => Path.Combine(Root, "exports");

        public static void EnsureAll()
        {
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(LogsDir);
            Directory.CreateDirectory(ExportsDir);
        }

        /// <summary>文件名用时间戳: yyyyMMdd-HHmmss。</summary>
        public static string Stamp() => DateTime.Now.ToString("yyyyMMdd-HHmmss");
    }

    /// <summary>
    /// 运行日志落盘: 每次运行一个会话文件, 逐行追加(线程安全)。
    /// 界面里的日志框只保留最近若干行, 完整记录始终在文件里, 供「查看/导出」使用。
    /// </summary>
    public static class LogStore
    {
        private static readonly object Gate = new();
        private static string? _file;

        public static string CurrentFile
        {
            get
            {
                lock (Gate)
                {
                    if (_file != null) return _file;
                    try
                    {
                        AppPaths.EnsureAll();
                        _file = Path.Combine(AppPaths.LogsDir, $"session-{AppPaths.Stamp()}.log");
                        File.WriteAllText(_file,
                            $"# 物联网链路调试台 运行日志  {DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}",
                            new UTF8Encoding(true));
                    }
                    catch
                    {
                        _file = Path.Combine(Path.GetTempPath(), "iotlinklab-session.log");
                    }
                    return _file!;
                }
            }
        }

        public static void Append(string line)
        {
            lock (Gate)
            {
                try { File.AppendAllText(CurrentFile, line + Environment.NewLine, new UTF8Encoding(false)); }
                catch { /* 落盘失败不影响界面 */ }
            }
        }
    }
}
