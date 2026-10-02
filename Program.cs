using System;
using System.IO;
using System.Threading;
using System.Windows;

namespace NetTrafficMonitor
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            // 单实例：已有程序在运行时直接退出
            bool createdNew;
            Mutex mutex = new Mutex(true, "Global\\NetTrafficMonitorV1", out createdNew);
            if (!createdNew) return;

            AppDomain.CurrentDomain.UnhandledException += OnUnhandled;
            Application app = new Application();
            app.ShutdownMode = ShutdownMode.OnMainWindowClose;
            app.DispatcherUnhandledException += OnDispatcherUnhandledException;

            MainWindow win = new MainWindow();
            app.Run(win);

            GC.KeepAlive(mutex);
        }

        private static void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            LogError(e.Exception);
            e.Handled = true;   // 小错误不终止程序，仅记录日志
        }

        private static void OnUnhandled(object sender, UnhandledExceptionEventArgs e)
        {
            LogError(e.ExceptionObject as Exception);
        }

        private static void LogError(Exception ex)
        {
            try
            {
                Directory.CreateDirectory(Config.DirPath);
                File.AppendAllText(
                    Path.Combine(Config.DirPath, "error.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " +
                    (ex == null ? "未知错误" : ex.ToString()) + Environment.NewLine,
                    System.Text.Encoding.UTF8);
            }
            catch { }
        }
    }
}