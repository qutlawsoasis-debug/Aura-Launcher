using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

namespace AuraLauncher;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            LogCrash(args.ExceptionObject as Exception);
        };

        DispatcherUnhandledException += (s, args) =>
        {
            LogCrash(args.Exception);
            // Show crash message and avoid hard silent exit without notification
            args.Handled = true;
        };

        TaskScheduler.UnobservedTaskException += (s, args) =>
        {
            LogCrash(args.Exception);
            args.SetObserved();
        };

        base.OnStartup(e);
    }

    private static void LogCrash(Exception? ex)
    {
        if (ex == null) return;
        try
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string logPath = Path.Combine(baseDir, "launcher_crash.log");
            File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Unhandled Crash:\n{ex}\n----------------------------------------\n");
            MessageBox.Show(
                $"Ошибка при запуске или работе AURA Launcher:\n\n{ex.Message}\n\nСтек ошибки записан в launcher_crash.log",
                "AURA Launcher — Ошибка",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch { }
    }
}
