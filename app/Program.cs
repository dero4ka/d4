using System;
using System.IO;
using System.Threading;
using System.Windows;

namespace D4;

public static class Program
{
    [STAThread]
    public static void Main()
    {
        using var mutex = new Mutex(true, "D4-Desktop-Singleton", out bool isFirst);
        if (!isFirst)
        {
            MessageBox.Show("D4 уже запущен — значок находится в трее.", "D4");
            return;
        }

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var settings = Settings.Load();
        var shell = new Shell(settings);

        // что бы ни случилось, панель задач Windows не должна остаться скрытой
        AppDomain.CurrentDomain.UnhandledException += (_, _) => Shell.RestoreTaskbar();
        app.Exit += (_, _) => Shell.RestoreTaskbar();
        app.DispatcherUnhandledException += (_, e) =>
        {
            try
            {
                Directory.CreateDirectory(Settings.DataDir);
                File.AppendAllText(Path.Combine(Settings.DataDir, "error.log"), DateTime.Now + "\n" + e.Exception + "\n\n");
            }
            catch { /* лог не критичен */ }
            e.Handled = true;
        };

        app.Startup += (_, _) => shell.Start();
        app.Run();
    }
}
