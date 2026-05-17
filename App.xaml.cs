using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace Image2Text
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            AppDomain.CurrentDomain.UnhandledException += (s, ex) =>
                WriteCrashLog("AppDomain", ex.ExceptionObject as Exception);
            DispatcherUnhandledException += (s, ex) =>
            {
                WriteCrashLog("Dispatcher", ex.Exception);
                ex.Handled = false;
            };
            base.OnStartup(e);
        }

        private static void WriteCrashLog(string source, Exception? ex)
        {
            if (ex == null) return;
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Image2Text");
                Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, "crash.log");
                File.AppendAllText(file, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}\n{ex}\n\n");
            }
            catch { /* swallow */ }
        }
    }
}
