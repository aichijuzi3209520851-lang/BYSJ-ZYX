using System.Globalization;
using System.Text.RegularExpressions;

namespace IotLinkLab
{
    /// <summary>一帧串口协议的解析结果(字段与 02-PC-UpperComputer/Form1.cs 逐字一致)。</summary>
    public class FrameData
    {
        public bool HasTempHum;
        public double Temp;
        public double Hum;
        public int? Soil;
        public int? Raw;
        public int? Light;
        public string Servo = "OFF";
        public string Motor = "OFF";
        public string Mode = "AUTO";
        public string Air = "nice";
        public bool Warning;
    }

    /// <summary>报警类型(与上位机 GetAlarmType 同源阈值: 26/85/40)。</summary>
    public static class Alarm
    {
        public const double TempMax = 26.0;
        public const double HumiMax = 85.0;
        public const int SoilMax = 40;

        public static (string Type, bool Has) Evaluate(double temp, double hum, int? soil, string air)
        {
            bool t = temp > TempMax, h = hum > HumiMax;
            bool s = (soil ?? 0) > SoilMax, a = air == "bad";
            if (t && h) return ("温度湿度报警", true);
            if (t) return ("温度报警", true);
            if (h) return ("湿度报警", true);
            if (s) return ("土壤过湿报警", true);
            if (a) return ("空气污染报警", true);
            return ("正常", false);
        }
    }

    /// <summary>
    /// 协议编解码核心(可复用): 固件帧模板渲染 / 与上位机一致的解析 / 同契约 JSON。
    /// 帧契约(固件 User/main.c V1.2, 1.5s/帧, \r\n 结尾):
    ///   Temp:%dC   Humi:%d%%RH   Soil:%d%%(raw=%d)  Light:%d%%  Servo:ON |OFF Motor:ON|OFF Mode:AUTO|MAN Air:nice|bad [Warning]\r\n
    /// DHT11 失败帧: "DHT11 Read Failed!   Soil:.. Light:.. Servo:.. Motor:.. Mode:.. Air:.."
    /// </summary>
    public static class FrameCodec
    {
        public const string DefaultTemplate =
            "Temp:{temp}C   Humi:{humi}%RH   Soil:{soil}%(raw={raw})  Light:{light}%  Servo:{servo} Motor:{motor} Mode:{mode} Air:{air}{warning}";

        // 解析正则与 Form1.cs 逐字一致(温湿度允许负号/小数; 单字段异常不弃帧)
        private static readonly Regex RxTemp = new(@"Temp:(-?\d+(?:\.\d+)?)C", RegexOptions.Compiled);
        private static readonly Regex RxHumi = new(@"Humi:(\d+(?:\.\d+)?)%RH", RegexOptions.Compiled);
        private static readonly Regex RxSoil = new(@"Soil:(\d+)%", RegexOptions.Compiled);
        private static readonly Regex RxRaw = new(@"raw=(\d+)", RegexOptions.Compiled);
        private static readonly Regex RxLight = new(@"Light:(\d+)%", RegexOptions.Compiled);
        private static readonly Regex RxServo = new(@"Servo:(ON|OFF)", RegexOptions.Compiled);
        private static readonly Regex RxMotor = new(@"Motor:(ON|OFF)", RegexOptions.Compiled);
        private static readonly Regex RxMode = new(@"Mode:(AUTO|MAN)", RegexOptions.Compiled);
        private static readonly Regex RxAir = new(@"Air:(nice|bad)", RegexOptions.Compiled);

        /// <summary>解析一行串口数据(与上位机同一套容错语义)。</summary>
        public static FrameData? Parse(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            string s = raw.Trim();

            var fd = new FrameData { Warning = s.Contains("Warning", StringComparison.Ordinal) };
            bool any = fd.Warning;

            var m = RxTemp.Match(s);
            if (m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double t) && t >= -50 && t <= 100)
            { fd.Temp = t; fd.HasTempHum = true; any = true; }

            m = RxHumi.Match(s);
            if (m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double h) && h >= 0 && h <= 100)
            { fd.Hum = h; fd.HasTempHum = true; any = true; }

            m = RxSoil.Match(s);
            if (m.Success && int.TryParse(m.Groups[1].Value, out int soil)) { fd.Soil = soil; any = true; }

            m = RxRaw.Match(s);
            if (m.Success && int.TryParse(m.Groups[1].Value, out int rawV)) { fd.Raw = rawV; any = true; }

            m = RxLight.Match(s);
            if (m.Success && int.TryParse(m.Groups[1].Value, out int light)) { fd.Light = light; any = true; }

            m = RxServo.Match(s);
            if (m.Success) { fd.Servo = m.Groups[1].Value; any = true; }

            m = RxMotor.Match(s);
            if (m.Success) { fd.Motor = m.Groups[1].Value; any = true; }

            m = RxMode.Match(s);
            if (m.Success) { fd.Mode = m.Groups[1].Value; any = true; }

            m = RxAir.Match(s);
            if (m.Success) { fd.Air = m.Groups[1].Value; any = true; }

            return any ? fd : null;
        }

        /// <summary>
        /// 按模板渲染一帧固件文本。servo 固件侧输出 "ON "(带尾随空格), 这里忠实还原;
        /// warning 传 "" 或 " Warning"。DHT11 失败场景直接给 rawFrame。
        /// </summary>
        public static string Render(string template, double temp, double hum, int soil, int raw,
            int light, string servo, string motor, string mode, string air, string warning)
        {
            string inv(double v) => v.ToString("0.0", CultureInfo.InvariantCulture);
            return template
                .Replace("{temp}", inv(temp))
                .Replace("{humi}", inv(hum))
                .Replace("{soil}", soil.ToString(CultureInfo.InvariantCulture))
                .Replace("{raw}", raw.ToString(CultureInfo.InvariantCulture))
                .Replace("{light}", light.ToString(CultureInfo.InvariantCulture))
                .Replace("{servo}", servo)
                .Replace("{motor}", motor)
                .Replace("{mode}", mode)
                .Replace("{air}", air)
                .Replace("{warning}", warning);
        }

        public const string Dht11FailPrefix = "DHT11 Read Failed!   ";

        /// <summary>解析结果 → 与上位机 PublishDataToMqtt 同契约的 JSON(字段名/语义逐字段一致)。</summary>
        public static string ToJson(FrameData fd, DateTime now)
        {
            // 温湿度缺失时输出 null(上位机同语义: DHT11 失败帧仍发布其余字段)
            string tempJson = fd.HasTempHum ? fd.Temp.ToString("F1", CultureInfo.InvariantCulture) : "null";
            string humJson = fd.HasTempHum ? fd.Hum.ToString("F1", CultureInfo.InvariantCulture) : "null";
            double tempForAlarm = fd.HasTempHum ? fd.Temp : 0;
            double humForAlarm = fd.HasTempHum ? fd.Hum : 0;

            var (alarmType, hasAlarm) = Alarm.Evaluate(tempForAlarm, humForAlarm, fd.Soil, fd.Air);
            string mode = fd.Mode == "MAN" ? "manual" : "auto";

            string soil = (fd.Soil ?? 0).ToString(CultureInfo.InvariantCulture);
            string light = (fd.Light ?? 0).ToString(CultureInfo.InvariantCulture);

            return
                "{\n" +
                $"  \"timestamp\": \"{now:yyyy-MM-dd HH:mm:ss.fff}\",\n" +
                $"  \"temperature\": {tempJson},\n" +
                $"  \"humidity\": {humJson},\n" +
                $"  \"soilMoisture\": {soil},\n" +
                $"  \"lightPercent\": {light},\n" +
                $"  \"airQuality\": \"{fd.Air}\",\n" +
                $"  \"servoStatus\": \"{fd.Servo}\",\n" +
                $"  \"motorStatus\": \"{fd.Motor}\",\n" +
                $"  \"mode\": \"{mode}\",\n" +
                $"  \"alarmType\": \"{alarmType}\",\n" +
                $"  \"hasAlarm\": {(hasAlarm ? "true" : "false").ToLowerInvariant()}\n" +
                "}";
        }

        /// <summary>内置场景预设(键名/含义/覆盖字段), UI 下拉直接消费。</summary>
        public static readonly (string Key, double Temp, double Hum, int Soil, int Light, string Air, string Warn)[] Scenes =
        {
            ("正常态",        24, 62, 35, 72, "nice", ""),
            ("温度报警(>26°C)", 29, 62, 35, 72, "nice", " Warning"),
            ("湿度报警(>85%RH)", 24, 88, 35, 72, "nice", " Warning"),
            ("土壤过湿(>40%)", 24, 62, 46, 72, "nice", " Warning"),
            ("烟雾污染(bad)",  24, 62, 35, 72, "bad",  " Warning"),
            ("DHT11读取失败",   0,  0, 35, 72, "nice", ""),
        };
    }
}
