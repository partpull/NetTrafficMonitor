using System.Globalization;

namespace NetTrafficMonitor
{
    /// <summary>界面数值格式化：速率统一 MB/s 两位小数，总量自动按 B/KB/MB/GB 取值。</summary>
    internal static class Fmt
    {
        private const double KB = 1024.0;
        private const double MB = 1048576.0;
        private const double GB = 1073741824.0;

        public static string MBps(double bytesPerSec)
        {
            double v = bytesPerSec < 0 ? 0 : bytesPerSec / MB;
            return v.ToString("0.00", CultureInfo.InvariantCulture) + " MB/s";
        }

        public static string Bytes(double bytes)
        {
            double b = bytes < 0 ? 0 : bytes;
            if (b < KB) return b.ToString("0", CultureInfo.InvariantCulture) + " B";
            if (b < MB) return (b / KB).ToString("0.0", CultureInfo.InvariantCulture) + " KB";
            if (b < GB) return (b / MB).ToString("0.0", CultureInfo.InvariantCulture) + " MB";
            return (b / GB).ToString("0.00", CultureInfo.InvariantCulture) + " GB";
        }

        public static string Percent(double p)
        {
            double v = p < 0 ? 0 : p;
            return v.ToString("0", CultureInfo.InvariantCulture) + "%";
        }
    }
}