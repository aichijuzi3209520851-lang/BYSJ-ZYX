using System.Text;

namespace IotLinkLab
{
    /// <summary>
    /// 应用数据目录(单一真源): 设置 / 日志 / 导出。
    /// 根目录可由用户在「设置」抽屉里改(保存到 settings.json 的 saveRoot),
    /// 不再写死在 %APPDATA% 下 —— 演示/交付时常需要把日志和导出 CSV 放到 U 盘或项目目录。
    /// </summary>
    public static class AppPaths
    {
        private static string? _customRoot;

        /// <summary>默认根目录: %APPDATA%\IotLinkLab。</summary>
        public static string DefaultRoot => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "IotLinkLab");

        /// <summary>当前生效的保存根目录。未设置时就是 <see cref="DefaultRoot"/>。</summary>
        public static string Root => string.IsNullOrWhiteSpace(_customRoot) ? DefaultRoot : _customRoot!;

        /// <summary>用户是否指定了自定义目录(用于界面上区分「默认」/「自定义」)。</summary>
        public static bool IsCustom => !string.IsNullOrWhiteSpace(_customRoot);

        /// <summary>
        /// 把用户输入(可能含环境变量、可能是相对路径)规整成绝对路径; 空或非法返回 null。
        /// </summary>
        public static string? NormalizeRoot(string? dir)
        {
            if (string.IsNullOrWhiteSpace(dir)) return null;
            string d = Environment.ExpandEnvironmentVariables(dir.Trim().Trim('"'));
            try { d = Path.GetFullPath(d); } catch { return null; }
            if (d.Length == 0) return null;
            // 盘符根目录(如 "D:\")保留, 其余去掉尾部斜杠, 避免拼出 "D:\logs" 之外的多余分隔
            if (d.Length > 3) d = d.TrimEnd('\\', '/');
            return d;
        }

        /// <summary>设置保存根目录; 传空/null 恢复默认。返回根目录是否真的变了。</summary>
        public static bool SetRoot(string? dir)
        {
            string before = Root;
            _customRoot = NormalizeRoot(dir);
            return !string.Equals(before, Root, StringComparison.OrdinalIgnoreCase);
        }

        public static string LogsDir => Path.Combine(Root, "logs");
        public static string ExportsDir => Path.Combine(Root, "exports");

        public static void EnsureAll()
        {
            if (TryEnsure(Root)) return;

            // 自定义目录不可用(盘符被拔出 / 权限不足 / 路径写错)时静默回退默认目录,
            // 不能让「改了个保存路径」把日志和导出功能整体搞坏。
            if (IsCustom)
            {
                _customRoot = null;
                TryEnsure(Root);
            }
        }

        private static bool TryEnsure(string root)
        {
            try
            {
                Directory.CreateDirectory(root);
                Directory.CreateDirectory(Path.Combine(root, "logs"));
                Directory.CreateDirectory(Path.Combine(root, "exports"));
                return true;
            }
            catch { return false; }
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

        /// <summary>
        /// 丢弃当前会话文件句柄, 下次写入时在新目录下开一个新会话文件。
        /// 用户中途改了保存路径时必须调用, 否则后续日志仍写回旧目录。
        /// </summary>
        public static void ResetFile()
        {
            lock (Gate) { _file = null; }
        }

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
