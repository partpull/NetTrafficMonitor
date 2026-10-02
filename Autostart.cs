using System;
using System.Diagnostics;
using System.Reflection;
using Microsoft.Win32;

namespace NetTrafficMonitor
{
    /// <summary>开机自启：写入 HKCU\Software\Microsoft\Windows\CurrentVersion\Run。</summary>
    internal static class Autostart
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "NetTrafficMonitorV1";

        public static string ExePath
        {
            get
            {
                try
                {
                    return Process.GetCurrentProcess().MainModule.FileName;
                }
                catch
                {
                    return Assembly.GetExecutingAssembly().Location;
                }
            }
        }

        public static bool IsEnabled()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, false))
                {
                    return k != null && k.GetValue(ValueName) != null;
                }
            }
            catch
            {
                return false;
            }
        }

        public static void SetEnabled(bool on)
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (k == null) return;
                if (on) k.SetValue(ValueName, "\"" + ExePath + "\"");
                else k.DeleteValue(ValueName, false);
            }
        }
    }
}