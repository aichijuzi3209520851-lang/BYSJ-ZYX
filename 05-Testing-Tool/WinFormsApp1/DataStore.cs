using System.Globalization;
using System.Text;

namespace IotLinkLab
{
    /// <summary>一条链路数据记录(上行帧 / 下行指令 / 订阅到的消息)。</summary>
    public sealed class DataRow
    {
        public DateTime Time;
        public string Direction = "";      // 上行 / 下行 / 订阅
        public string Topic = "";
        public string Frame = "";          // 原始串口帧文本(上行时有)
        public string Payload = "";        // MQTT 载荷
        public double? Temp, Hum;
        public int? Soil, Light, Raw;
        public string Servo = "", Motor = "", Mode = "", Air = "", Alarm = "";
    }

    /// <summary>
    /// 运行期数据记录 + CSV 导出。
    /// 上行帧、下行指令、订阅消息全部进这里, 「导出数据」输出中文表头 CSV, 可直接用 Excel 打开。
    /// </summary>
    public static class DataStore
    {
        private const int MaxRows = 50000;
        private static readonly object Gate = new();
        private static readonly List<DataRow> Rows = new();

        public static int Count { get { lock (Gate) return Rows.Count; } }

        public static void Add(DataRow row)
        {
            lock (Gate)
            {
                Rows.Add(row);
                if (Rows.Count > MaxRows) Rows.RemoveRange(0, Rows.Count - MaxRows);
            }
        }

        public static void Clear() { lock (Gate) Rows.Clear(); }

        public static string ExportCsv()
        {
            List<DataRow> rows;
            lock (Gate) rows = new List<DataRow>(Rows);

            AppPaths.EnsureAll();
            string path = Path.Combine(AppPaths.ExportsDir, $"data-{AppPaths.Stamp()}.csv");

            var sb = new StringBuilder();
            sb.AppendLine("时间,方向,主题,温度℃,湿度%RH,土壤%,光照%,raw,天窗,风扇,模式,空气质量,报警,帧文本,载荷");

            var inv = CultureInfo.InvariantCulture;
            foreach (var r in rows)
            {
                sb.Append(Csv(r.Time.ToString("yyyy-MM-dd HH:mm:ss.fff", inv))).Append(',')
                  .Append(Csv(r.Direction)).Append(',')
                  .Append(Csv(r.Topic)).Append(',')
                  .Append(Csv(r.Temp?.ToString("0.0", inv) ?? "")).Append(',')
                  .Append(Csv(r.Hum?.ToString("0.0", inv) ?? "")).Append(',')
                  .Append(Csv(r.Soil?.ToString(inv) ?? "")).Append(',')
                  .Append(Csv(r.Light?.ToString(inv) ?? "")).Append(',')
                  .Append(Csv(r.Raw?.ToString(inv) ?? "")).Append(',')
                  .Append(Csv(r.Servo)).Append(',')
                  .Append(Csv(r.Motor)).Append(',')
                  .Append(Csv(r.Mode)).Append(',')
                  .Append(Csv(r.Air)).Append(',')
                  .Append(Csv(r.Alarm)).Append(',')
                  .Append(Csv(r.Frame)).Append(',')
                  .Append(Csv(r.Payload))
                  .Append("\r\n");
            }

            // 带 BOM, Excel 打开中文表头不乱码
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
            return path;
        }

        private static string Csv(string v)
        {
            if (string.IsNullOrEmpty(v)) return "";

            // 字段内部的换行会把一条记录折成十几行, Excel 里完全没法看, 统一压成单行
            if (v.IndexOf('\n') >= 0 || v.IndexOf('\r') >= 0)
            {
                v = v.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ');
                while (v.Contains("  ")) v = v.Replace("  ", " ");   // JSON 缩进留下的连续空格一并压掉
            }

            if (v.IndexOfAny(new[] { ',', '"' }) < 0) return v;
            return "\"" + v.Replace("\"", "\"\"") + "\"";
        }
    }
}
