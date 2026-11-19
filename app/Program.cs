using System;
using System.IO;
using System.Threading;
using System.Windows;

namespace D4;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // служебные режимы без окон
        if (args.Length >= 1 && args[0] == "--restore-taskbar")
        {
            Native.RecoverTaskbar();
            return;
        }
        if (args.Length >= 2 && args[0] == "--watch")
        {
            // сторож: ждём завершения D4 и возвращаем панель Windows, как бы он ни закрылся
            try
            {
                if (int.TryParse(args[1], out var pid)) System.Diagnostics.Process.GetProcessById(pid).WaitForExit();
            }
            catch { /* процесса уже нет */ }
            Native.RecoverTaskbar();
            return;
        }

        using var mutex = new Mutex(true, "D4-Desktop-Singleton", out bool isFirst);
        if (!isFirst)
        {
            MessageBox.Show("D4 уже запущен — значок находится в трее.", "D4");
            return;
        }

        Native.RecoverTaskbar(); // если прошлый запуск закрылся аварийно

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var settings = Settings.Load();
        var shell = new Shell(settings);

        // что бы ни случилось, панель задач Windows не должна остаться скрытой
        AppDomain.CurrentDomain.UnhandledException += (_, _) => Shell.RestoreTaskbar();
        app.Exit += (_, _) => Shell.RestoreTaskbar();
        app.SessionEnding += (_, _) => Shell.RestoreTaskbar();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Shell.RestoreTaskbar();
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
