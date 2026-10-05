using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using AuraLauncher.Services.Interfaces;
using AuraLauncher.Services.Implementations;
using AuraLauncher.ViewModels;

namespace AuraLauncher;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;
    private static int _hasShownCrashDialog = 0;
    private static Mutex? _singleInstanceMutex;

    private const int SW_RESTORE = 9;

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    protected override void OnStartup(StartupEventArgs e)
    {
        // Перехват и логирование необработанных исключений
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            LogCrash(args.ExceptionObject as Exception);
        };

        DispatcherUnhandledException += (s, args) =>
        {
            LogCrash(args.Exception);
            args.Handled = true; // Предотвращаем падение приложения при сбоях в UI/рендере
        };

        TaskScheduler.UnobservedTaskException += (s, args) =>
        {
            LogCrash(args.Exception);
            args.SetObserved();
        };

        // Режим самодиагностики (--selftest, --selftest-shots или --selftest-lobby) или отдельный профиль
        bool isLobbyTest = Array.Exists(e.Args, a => a.Equals("--selftest-lobby", StringComparison.OrdinalIgnoreCase));
        bool isSelfTest = isLobbyTest || Array.Exists(e.Args, a => a.Equals("--selftest", StringComparison.OrdinalIgnoreCase) || a.Equals("--selftest-shots", StringComparison.OrdinalIgnoreCase));
        
        string? profileArg = Environment.GetEnvironmentVariable("AURA_PROFILE_DIR");
        if (string.IsNullOrWhiteSpace(profileArg))
        {
            for (int i = 0; i < e.Args.Length - 1; i++)
            {
                if (string.Equals(e.Args[i], "--profile", StringComparison.OrdinalIgnoreCase))
                {
                    profileArg = e.Args[i + 1];
                    break;
                }
            }
        }

        string mutexName = string.IsNullOrWhiteSpace(profileArg)
            ? @"Local\Aura.Launcher"
            : $@"Local\Aura.Launcher.{profileArg.Replace('\\', '_').Replace(':', '_').Replace('/', '_')}";

        // Именованный Mutex для контроля единого экземпляра приложения
        _singleInstanceMutex = new Mutex(true, mutexName, out bool isNewInstance);
        if (!isNewInstance && !isSelfTest)
        {
            BringExistingInstanceToFront();
            Shutdown();
            return;
        }

        // Убиваем зависшие процессы playit* (в т.ч. claim exchange)
        PlayitTunnelProvider.KillStalePlayitProcesses();

        base.OnStartup(e);

        // Инициализация DI-контейнера
        bool useFakeTunnel = Array.Exists(e.Args, a => a.Equals("--fake-tunnel", StringComparison.OrdinalIgnoreCase)) ||
                             string.Equals(Environment.GetEnvironmentVariable("AURA_FAKE_TUNNEL"), "1", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(Environment.GetEnvironmentVariable("UseFakeTunnel"), "true", StringComparison.OrdinalIgnoreCase);

        var services = new ServiceCollection();
        ConfigureServices(services, useFakeTunnel);
        Services = services.BuildServiceProvider();

        // Логирование версии лаунчера при старте
        try
        {
            var launcherUpdateService = Services.GetRequiredService<ILauncherUpdateService>();
            FabricGameLaunchService.LogLauncherEvent($"Aura Launcher v{launcherUpdateService.CurrentVersion}, установлен через Velopack: {(launcherUpdateService.IsInstalled ? "да" : "нет")}");
        }
        catch { }

        // Создаем главное окно и передаем MainViewModel в качестве DataContext
        var mainWindow = new MainWindow
        {
            DataContext = Services.GetRequiredService<MainViewModel>()
        };

        mainWindow.Show();

        // Запускаем фоновую инициализацию ViewModel
        if (mainWindow.DataContext is MainViewModel mainVM)
        {
            _ = mainVM.InitializeAsync();
        }

        // Проверка режима самодиагностики (--selftest-lobby, --selftest или --selftest-shots)
        if (isLobbyTest)
        {
            _ = Task.Run(async () =>
            {
                bool success = await Core.SceneDiagnostics.LiveTestLobbyAsync(mainWindow);
            });
        }
        else if (isSelfTest)
        {
            _ = Task.Run(async () =>
            {
                bool success = await Core.SceneDiagnostics.RunSelfTestShotsAsync(mainWindow);
                Environment.Exit(success ? 0 : 1);
            });
        }
    }

    private static void ConfigureServices(IServiceCollection services, bool useFakeTunnel = false)
    {
        // Регистрация сервисов как синглтонов
        services.AddSingleton<IConfigService, JsonConfigService>();
        services.AddSingleton<ILauncherUpdateService, LauncherUpdateService>();
        services.AddSingleton<IPackUpdateService, PackUpdateService>();
        services.AddSingleton<IGameLaunchService, FabricGameLaunchService>();
        services.AddSingleton<ISkinService, SkinService>();
        services.AddSingleton<IServerListSyncService, ServerListSyncService>();
        services.AddSingleton<ILobbyApiClient>(sp => new LobbyApiClient(null, "https://lobby-api.vercel.app", sp.GetRequiredService<IConfigService>()));
        
        if (useFakeTunnel)
        {
            services.AddSingleton<ITunnelProvider, FakeTunnelProvider>();
        }
        else
        {
            services.AddSingleton<ITunnelProvider, PlayitTunnelProvider>();
        }

        services.AddSingleton<ILobbyService, LobbyService>();
        services.AddSingleton<ILanWorldWatcher, LanWorldWatcher>();

        // Регистрация ViewModels
        services.AddSingleton<OverviewViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<WardrobeViewModel>();
        services.AddSingleton<LobbyViewModel>();
        services.AddSingleton<MainViewModel>();
    }

    private static void LogCrash(Exception? ex)
    {
        if (ex == null) return;
        try
        {
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string logEntry = $"[{timestamp}] Unhandled Crash:\n{ex}\n----------------------------------------\n";

            // Запись в системный %AppData%\Aura\launcher.log
            try
            {
                string appDataAura = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Aura");
                Directory.CreateDirectory(appDataAura);
                string appDataLogPath = Path.Combine(appDataAura, "launcher.log");
                File.AppendAllText(appDataLogPath, logEntry);
            }
            catch { }

            // Показываем диалог пользователю ОДИН РАЗ, предотвращая зацикливание модальных окон
            if (Interlocked.CompareExchange(ref _hasShownCrashDialog, 1, 0) == 0)
            {
                MessageBox.Show(
                    $"Произошла ошибка в работе AURA Launcher:\n\n{ex.Message}\n\nПолный стек ошибки сохранен в launcher.log.\nПриложение продолжит работу.",
                    "AURA Launcher — Внимание",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        catch { }
    }

    public static void Log(string message)
    {
        try
        {
            string appDataAura = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Aura");
            Directory.CreateDirectory(appDataAura);
            string appDataLogPath = Path.Combine(appDataAura, "launcher.log");
            string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}";
            File.AppendAllText(appDataLogPath, line);
            System.Diagnostics.Debug.WriteLine(line);
        }
        catch { }
    }

    private static void BringExistingInstanceToFront()
    {
        try
        {
            var currentProc = Process.GetCurrentProcess();
            var processes = Process.GetProcessesByName(currentProc.ProcessName);
            foreach (var p in processes)
            {
                if (p.Id != currentProc.Id && p.MainWindowHandle != IntPtr.Zero)
                {
                    ShowWindow(p.MainWindowHandle, SW_RESTORE);
                    SetForegroundWindow(p.MainWindowHandle);
                    return;
                }
            }

            var hWnd = FindWindow(null, "AURA");
            if (hWnd != IntPtr.Zero)
            {
                ShowWindow(hWnd, SW_RESTORE);
                SetForegroundWindow(hWnd);
            }
        }
        catch { }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _singleInstanceMutex?.ReleaseMutex();
            _singleInstanceMutex?.Dispose();
        }
        catch { }
        base.OnExit(e);
    }
}
