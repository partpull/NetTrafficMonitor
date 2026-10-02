using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace NetTrafficMonitor
{
    /// <summary>配置读写：%APPDATA%\NetTrafficMonitor\config.ini（纯 key=value，容错解析）。</summary>
    internal sealed class Config
    {
        public string Mode = "compact";        // compact | expanded
        public int RefreshMs = 1000;           // 500 | 1000 | 2000
        public int WindowX = -1;               // 窗口坐标（DIP），-1 表示使用默认位置（右上角）
        public int WindowY = -1;
        public string ProxyMode = "auto";      // auto | api | adapter
        public int ProxyPort = 0;              // 0 表示自动探测
        public string ProxySecret = "";
        public string ProxyAdapter = "";

        public static string DirPath
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NetTrafficMonitor");
            }
        }

        private static string FilePath
        {
            get { return Path.Combine(DirPath, "config.ini"); }
        }

        public static Config Load()
        {
            Config c = new Config();
            try
            {
                if (!File.Exists(FilePath)) return c;
                string[] lines = File.ReadAllLines(FilePath);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    int p = line.IndexOf('=');
                    if (p <= 0) continue;
                    string k = line.Substring(0, p).Trim();
                    string v = line.Substring(p + 1).Trim();
                    switch (k)
                    {
                        case "mode":
                            c.Mode = (v == "expanded") ? "expanded" : "compact";
                            break;
                        case "refreshMs":
                            c.RefreshMs = ParseInt(v, 1000, 500, 2000);
                            break;
                        case "windowX":
                            c.WindowX = ParseInt(v, -1, -100000, 100000);
                            break;
                        case "windowY":
                            c.WindowY = ParseInt(v, -1, -100000, 100000);
                            break;
                        case "proxyMode":
                            c.ProxyMode = (v == "api" || v == "adapter") ? v : "auto";
                            break;
                        case "proxyPort":
                            c.ProxyPort = ParseInt(v, 0, 0, 65535);
                            break;
                        case "proxySecret":
                            c.ProxySecret = v;
                            break;
                        case "proxyAdapter":
                            c.ProxyAdapter = v;
                            break;
                    }
                }
            }
            catch
            {
                // 配置损坏时回退默认值
            }
            return c;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(DirPath);
                List<string> lines = new List<string>();
                lines.Add("mode=" + Mode);
                lines.Add("refreshMs=" + RefreshMs.ToString(CultureInfo.InvariantCulture));
                lines.Add("windowX=" + WindowX.ToString(CultureInfo.InvariantCulture));
                lines.Add("windowY=" + WindowY.ToString(CultureInfo.InvariantCulture));
                lines.Add("proxyMode=" + ProxyMode);
                lines.Add("proxyPort=" + ProxyPort.ToString(CultureInfo.InvariantCulture));
                lines.Add("proxySecret=" + ProxySecret);
                lines.Add("proxyAdapter=" + ProxyAdapter);
                File.WriteAllLines(FilePath, lines.ToArray(), System.Text.Encoding.UTF8);
            }
            catch
            {
                // 写入失败不阻塞程序运行
            }
        }

        private static int ParseInt(string s, int fallback, int min, int max)
        {
            int v;
            if (!int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) return fallback;
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }
    }
}