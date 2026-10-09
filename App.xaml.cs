using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Toolkit.Uwp.Notifications;
using AuraLauncher.Services.Interfaces;
using AuraLauncher.Services.Implementations;
using AuraLauncher.ViewModels;
using AuraLauncher.Views;

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
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // Перехват и логирование необработанных исключений
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            if (Current?.Dispatcher?.HasShutdownStarted == true) return;
            LogCrash(args.ExceptionObject as Exception, isFatal: args.IsTerminating);
        };

        DispatcherUnhandledException += (s, args) =>
        {
            if (Dispatcher.HasShutdownStarted)
            {
                args.Handled = true;
                return;
            }
            LogCrash(args.Exception, isFatal: false);
            args.Handled = true; // Предотвращаем падение приложения при сбоях в UI/рендере
        };

        TaskScheduler.UnobservedTaskException += (s, args) =>
        {
            if (Current?.Dispatcher?.HasShutdownStarted == true)
            {
                args.SetObserved();
                return;
            }
            LogCrash(args.Exception, isFatal: false);
            args.SetObserved();
        };

        string[] args = (e.Args != null && e.Args.Length > 0)
            ? e.Args
            : ((Program.StartupArgs != null && Program.StartupArgs.Length > 0)
                ? Program.StartupArgs
                : Environment.GetCommandLineArgs().Skip(1).ToArray());

        // Режим самодиагностики (--selftest, --selftest-shots, --selftest-lobby или --selftest-kill-playit) или отдельный профиль
        string? captureShotsPrefix = null;
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals("--capture-shots", StringComparison.OrdinalIgnoreCase))
            {
                captureShotsPrefix = args[i + 1];
                break;
            }
        }

        string? fakeUpdateUiMode = null;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith("--fake-update-ui=", StringComparison.OrdinalIgnoreCase))
            {
                fakeUpdateUiMode = args[i].Substring("--fake-update-ui=".Length);
                break;
            }
            if (args[i].Equals("--fake-update-ui", StringComparison.OrdinalIgnoreCase) && i < args.Length - 1)
            {
                fakeUpdateUiMode = args[i + 1];
                break;
            }
        }

        bool isKillPlayitTest = Array.Exists(args, a => a.Equals("--selftest-kill-playit", StringComparison.OrdinalIgnoreCase));
        bool isLobbyTest = Array.Exists(args, a => a.Equals("--selftest-lobby", StringComparison.OrdinalIgnoreCase));
        bool isLifecycleTest = Array.Exists(args, a => a.Equals("--selftest-lifecycle", StringComparison.OrdinalIgnoreCase));
        bool isAnthemTest = Array.Exists(args, a => a.Equals("--selftest-anthem", StringComparison.OrdinalIgnoreCase));
        bool isRapidNavTest = Array.Exists(args, a => a.Equals("--selftest-rapid", StringComparison.OrdinalIgnoreCase));
        bool isTrayTest = Array.Exists(args, a => a.Equals("--selftest-tray", StringComparison.OrdinalIgnoreCase));
        bool isFriendsTest = Array.Exists(args, a => a.Equals("--selftest-friends", StringComparison.OrdinalIgnoreCase));
        bool isProtocolTest = Array.Exists(args, a => a.Equals("--selftest-protocol", StringComparison.OrdinalIgnoreCase));
        bool isNotificationsReportTest = Array.Exists(args, a => a.Equals("--selftest-notifications-report", StringComparison.OrdinalIgnoreCase));
        bool isIconTest = Array.Exists(args, a => a.Equals("--selftest-icon", StringComparison.OrdinalIgnoreCase));
        bool isReportTest = Array.Exists(args, a => a.Equals("--selftest-report", StringComparison.OrdinalIgnoreCase));
        bool isLayoutAudit = Array.Exists(args, a => a.Equals("--layout-audit", StringComparison.OrdinalIgnoreCase));
        bool isColorsTest = Array.Exists(args, a => a.Equals("--selftest-colors", StringComparison.OrdinalIgnoreCase));
        bool isScaleCrispTest = Array.Exists(args, a => a.Equals("--selftest-scale-crisp", StringComparison.OrdinalIgnoreCase));
        bool isSplashTest = Array.Exists(args, a => a.Equals("--selftest-splash", StringComparison.OrdinalIgnoreCase));
        bool isSelfTest = !string.IsNullOrWhiteSpace(captureShotsPrefix) || !string.IsNullOrWhiteSpace(fakeUpdateUiMode) || isKillPlayitTest || isLobbyTest || isLifecycleTest || isAnthemTest || isRapidNavTest || isTrayTest || isFriendsTest || isProtocolTest || isNotificationsReportTest || isIconTest || isReportTest || isLayoutAudit || isColorsTest || isScaleCrispTest || isSplashTest || Array.Exists(args, a => a.Equals("--selftest", StringComparison.OrdinalIgnoreCase) || a.Equals("--selftest-shots", StringComparison.OrdinalIgnoreCase));
        
        string? profileArg = Environment.GetEnvironmentVariable("AURA_PROFILE_DIR");
        if (string.IsNullOrWhiteSpace(profileArg))
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], "--profile", StringComparison.OrdinalIgnoreCase))
                {
                    profileArg = args[i + 1];
                    break;
                }
            }
        }

        string mutexName = string.IsNullOrWhiteSpace(profileArg)
            ? @"Local\Aura.Launcher"
            : $@"Local\Aura.Launcher.{profileArg.Replace('\\', '_').Replace(':', '_').Replace('/', '_')}";

        string pipeName = string.IsNullOrWhiteSpace(profileArg)
            ? "Aura.Launcher.Pipe"
            : $"Aura.Launcher.Pipe.{profileArg.Replace('\\', '_').Replace(':', '_').Replace('/', '_')}";

        RegisterAuraProtocol();

        string? protocolArg = Array.Find(args, a => a.StartsWith("aura://", StringComparison.OrdinalIgnoreCase));

        // Именованный Mutex для контроля единого экземпляра приложения
        _singleInstanceMutex = new Mutex(true, mutexName, out bool isNewInstance);
        Log($"[STARTUP] args: '{string.Join(' ', args)}', isNewInstance={isNewInstance}, isSelfTest={isSelfTest}");
        if (!isNewInstance && !isSelfTest)
        {
            Log($"[STARTUP] Second instance detected. Signaling pipe: {pipeName}, uri: {protocolArg}");
            SignalExistingInstanceViaPipe(pipeName, protocolArg);
            BringExistingInstanceToFront();
            Shutdown();
            return;
        }

        // Убиваем зависшие процессы playit* (в т.ч. claim exchange)
        PlayitTunnelProvider.KillStalePlayitProcesses();
        PlayitTunnelProvider.PurgeLegacyLocalSecrets();

        base.OnStartup(e);

        // Инициализация DI-контейнера
        bool useFakeTunnel = Array.Exists(e.Args, a => a.Equals("--fake-tunnel", StringComparison.OrdinalIgnoreCase)) ||
                             string.Equals(Environment.GetEnvironmentVariable("AURA_FAKE_TUNNEL"), "1", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(Environment.GetEnvironmentVariable("UseFakeTunnel"), "true", StringComparison.OrdinalIgnoreCase);

        var services = new ServiceCollection();
        ConfigureServices(services, useFakeTunnel);
        Services = services.BuildServiceProvider();
        _ = Services.GetRequiredService<IConfigService>();

        // Логирование версии лаунчера при старте
        try
        {
            var launcherUpdateService = Services.GetRequiredService<ILauncherUpdateService>();
            FabricGameLaunchService.LogLauncherEvent($"Aura Launcher v{launcherUpdateService.CurrentVersion}, установлен через Velopack: {(launcherUpdateService.IsInstalled ? "да" : "нет")}");
        }
        catch { }

        // Предстартовое окно (splash): открывается до тяжелых ресурсов и главного окна
        bool isUpdatedRestart = Array.Exists(args, a => a.Equals("--updated-restart", StringComparison.OrdinalIgnoreCase));
        bool skipSplash = isUpdatedRestart || isSelfTest || Array.Exists(args, a => a.Equals("--no-splash", StringComparison.OrdinalIgnoreCase) || a.Equals("--no-preupdate", StringComparison.OrdinalIgnoreCase));

        StartupWindow? splashWin = null;

        if (!skipSplash)
        {
            bool shouldContinueToMain = true;
            try
            {
                var launcherUpdateService = Services.GetRequiredService<ILauncherUpdateService>();
                var startupCompletedTcs = new TaskCompletionSource<bool>();
                StartupWindowViewModel? startupVm = null;

                shouldContinueToMain = false;
                startupVm = new StartupWindowViewModel(
                    launcherUpdateService,
                    onLaunchMainRequested: () =>
                    {
                        Dispatcher.Invoke(() =>
                        {
                            if (Dispatcher.HasShutdownStarted) return;
                            shouldContinueToMain = true;
                            startupCompletedTcs.TrySetResult(true);
                        });
                    },
                    onCloseRequested: () =>
                    {
                        Dispatcher.Invoke(() =>
                        {
                            shouldContinueToMain = false;
                            startupCompletedTcs.TrySetResult(false);
                            if (Dispatcher.HasShutdownStarted) return;
                            splashWin?.Close();
                            Shutdown(0);
                        });
                    });

                splashWin = new StartupWindow
                {
                    DataContext = startupVm
                };
                splashWin.Show();
                _ = startupVm.StartStartupFlowAsync();

                // Ждем завершения splash-флоу в неблокирующем цикле событий WPF
                var frame = new System.Windows.Threading.DispatcherFrame();
                _ = startupCompletedTcs.Task.ContinueWith(_ => frame.Continue = false);
                System.Windows.Threading.Dispatcher.PushFrame(frame);
            }
            catch (Exception ex)
            {
                FabricGameLaunchService.LogLauncherEvent($"[STARTUP-WINDOW: ERROR] {ex.Message}");
                shouldContinueToMain = true;
            }

            if (!shouldContinueToMain || Dispatcher.HasShutdownStarted)
            {
                return;
            }
        }

        // Инициализация сервиса динамических фонов
        try
        {
            var bgService = Services.GetRequiredService<IBackgroundService>();
            bgService.Initialize();
        }
        catch { }

        // Создаем главное окно и передаем MainViewModel в качестве DataContext
        MainWindow mainWindow;
        try
        {
            mainWindow = new MainWindow
            {
                DataContext = Services.GetRequiredService<MainViewModel>()
            };
        }
        catch (Exception ex)
        {
            if (Current?.Dispatcher?.HasShutdownStarted == true) return;
            LogCrash(ex, isFatal: true);
            try
            {
                var launcherUpdateService = Services.GetRequiredService<ILauncherUpdateService>();
                if (launcherUpdateService.IsInstalled)
                {
                    FabricGameLaunchService.LogLauncherEvent($"[EMERGENCY-UPDATE] Сбой запуска интерфейса ({ex.Message}). Запуск аварийного обновления...");
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                    var emergencyResult = Task.Run(() => launcherUpdateService.CheckAndApplyAsync(ct: cts.Token)).GetAwaiter().GetResult();
                    if (emergencyResult.Status == LauncherUpdateStatus.UpdatedRestarting)
                    {
                        FabricGameLaunchService.LogLauncherEvent("[EMERGENCY-UPDATE] Аварийное обновление применено! Перезапуск...");
                        Environment.Exit(0);
                        return;
                    }
                }
            }
            catch { }

            MessageBox.Show($"Не удалось запустить интерфейс Aura Launcher:\n\n{ex.Message}\n\nЛог ошибки сохранён в:\n%APPDATA%\\Aura\\launcher.log", "Aura Launcher - Ошибка запуска", MessageBoxButton.OK, MessageBoxImage.Error);
            Environment.Exit(1);
            return;
        }

        StartPipeServer(pipeName, mainWindow);

        MainWindow = mainWindow;
        mainWindow.Show();
        ShutdownMode = ShutdownMode.OnMainWindowClose;

        // Плавное закрытие splash-окна ПОСЛЕ показа главного окна
        if (splashWin != null)
        {
            try
            {
                var fadeOut = new System.Windows.Media.Animation.DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(250));
                fadeOut.Completed += (s, ev) =>
                {
                    try { splashWin.Close(); } catch { }
                };
                splashWin.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            }
            catch
            {
                try { splashWin.Close(); } catch { }
            }
        }

        // Запускаем фоновую инициализацию ViewModel
        if (mainWindow.DataContext is MainViewModel mainVM)
        {
            _ = mainVM.InitializeAsync();
            if (!string.IsNullOrWhiteSpace(protocolArg))
            {
                mainVM.HandleProtocolUri(protocolArg);
            }
        }

        try
        {
            ToastNotificationManagerCompat.OnActivated += toastArgs =>
            {
                var toastArguments = ToastArguments.Parse(toastArgs.Argument);
                Application.Current?.Dispatcher?.InvokeAsync(() =>
                {
                    var win = Application.Current.MainWindow;
                    if (win != null)
                    {
                        if (!win.IsVisible)
                        {
                            win.Show();
                            win.ShowInTaskbar = true;
                        }
                        if (win.WindowState == WindowState.Minimized)
                        {
                            win.WindowState = WindowState.Normal;
                        }
                        win.Activate();
                        win.Focus();
                    }

                    if (toastArguments.TryGetValue("tab", out var tab) && !string.IsNullOrWhiteSpace(tab))
                    {
                        var mv = Services?.GetService<MainViewModel>();
                        mv?.SwitchTab(tab);
                    }
                });
            };
        }
        catch { }

        // Инициализация сервиса достижений
        try
        {
            var achievementService = Services.GetRequiredService<IAchievementService>();
            achievementService.Initialize();
        }
        catch { }

        // Инициализация сервиса гимна (воспроизведение при первом старте, если не mute)
        try
        {
            var anthemService = Services.GetRequiredService<IAnthemService>();
            anthemService.Initialize();
            mainWindow.Closed += (s, args) =>
            {
                try { anthemService.Stop(); } catch { }
            };
        }
        catch { }

#if DEBUG
        if (!string.IsNullOrWhiteSpace(fakeUpdateUiMode))
        {
            _ = Task.Run(async () =>
            {
                bool success = await Core.SceneDiagnostics.RunFakeUpdateUiAsync(mainWindow, fakeUpdateUiMode);
                if (Array.Exists(e.Args, a => a.Equals("--exit-after-test", StringComparison.OrdinalIgnoreCase)))
                {
                    await Task.Delay(1000);
                    Environment.Exit(success ? 0 : 1);
                }
            });
        }
        else if (!string.IsNullOrWhiteSpace(captureShotsPrefix))
        {
            _ = Task.Run(async () =>
            {
                bool success = await Core.SceneDiagnostics.CaptureAllScreenshotsAsync(mainWindow, captureShotsPrefix);
                if (Array.Exists(e.Args, a => a.Equals("--exit-after-test", StringComparison.OrdinalIgnoreCase)))
                {
                    await Task.Delay(1000);
                    Environment.Exit(success ? 0 : 1);
                }
            });
        }
        else if (isKillPlayitTest)
        {
            _ = Task.Run(async () =>
            {
                bool success = await Core.SceneDiagnostics.LiveTestKillPlayitAsync(mainWindow);
                if (Array.Exists(e.Args, a => a.Equals("--exit-after-test", StringComparison.OrdinalIgnoreCase)))
                {
                    await Task.Delay(1000);
                    Environment.Exit(success ? 0 : 1);
                }
            });
        }
        else if (isLifecycleTest)
        {
            _ = Task.Run(async () =>
            {
                bool success = await Core.SceneDiagnostics.RunLobbyLifecycleTestAsync(mainWindow);
                if (Array.Exists(e.Args, a => a.Equals("--exit-after-test", StringComparison.OrdinalIgnoreCase)))
                {
                    await Task.Delay(1000);
                    Environment.Exit(success ? 0 : 1);
                }
            });
        }
        else if (isLobbyTest)
        {
            _ = Task.Run(async () =>
            {
                bool success = await Core.SceneDiagnostics.LiveTestLobbyAsync(mainWindow);
                if (Array.Exists(e.Args, a => a.Equals("--exit-after-test", StringComparison.OrdinalIgnoreCase)))
                {
                    await Task.Delay(1000);
                    Environment.Exit(success ? 0 : 1);
                }
            });
        }
        else if (isAnthemTest)
        {
            _ = Task.Run(async () =>
            {
                bool success = await Core.SceneDiagnostics.RunAnthemTestAsync(mainWindow);
                if (Array.Exists(e.Args, a => a.Equals("--exit-after-test", StringComparison.OrdinalIgnoreCase)))
                {
                    await Task.Delay(1000);
                    Environment.Exit(success ? 0 : 1);
                }
            });
        }
        else if (isRapidNavTest)
        {
            _ = Task.Run(async () =>
            {
                bool success = await Core.SceneDiagnostics.RunRapidTabSwitchStressTestAsync(mainWindow);
                if (Array.Exists(e.Args, a => a.Equals("--exit-after-test", StringComparison.OrdinalIgnoreCase)))
                {
                    await Task.Delay(1000);
                    Environment.Exit(success ? 0 : 1);
                }
            });
        }
        else if (isTrayTest)
        {
            _ = Task.Run(async () =>
            {
                bool success = await Core.SceneDiagnostics.RunTrayAndHideLauncherTestAsync(mainWindow);
                if (Array.Exists(e.Args, a => a.Equals("--exit-after-test", StringComparison.OrdinalIgnoreCase)))
                {
                    await Task.Delay(1000);
                    Environment.Exit(success ? 0 : 1);
                }
            });
        }
        else if (isFriendsTest)
        {
            _ = Task.Run(async () =>
            {
                bool success = await Core.SceneDiagnostics.RunFriendsSelfTestAsync(mainWindow);
                if (Array.Exists(e.Args, a => a.Equals("--exit-after-test", StringComparison.OrdinalIgnoreCase)))
                {
                    await Task.Delay(1000);
                    Environment.Exit(success ? 0 : 1);
                }
            });
        }
        else if (isProtocolTest)
        {
            _ = Task.Run(async () =>
            {
                bool success = await Core.SceneDiagnostics.RunProtocolSelfTestAsync(mainWindow);
                if (Array.Exists(e.Args, a => a.Equals("--exit-after-test", StringComparison.OrdinalIgnoreCase)))
                {
                    await Task.Delay(1000);
                    Environment.Exit(success ? 0 : 1);
                }
            });
        }
        else if (isNotificationsReportTest)
        {
            _ = Task.Run(async () =>
            {
                bool success = await Core.SceneDiagnostics.RunNotificationsAndReportTestAsync(mainWindow);
                if (Array.Exists(e.Args, a => a.Equals("--exit-after-test", StringComparison.OrdinalIgnoreCase)))
                {
                    await Task.Delay(1000);
                    Environment.Exit(success ? 0 : 1);
                }
            });
        }
        else if (isIconTest)
        {
            _ = Task.Run(async () =>
            {
                bool success = await Core.SceneDiagnostics.RunIconSelfTestAsync(mainWindow);
                if (Array.Exists(e.Args, a => a.Equals("--exit-after-test", StringComparison.OrdinalIgnoreCase)))
                {
                    await Task.Delay(1000);
                    Environment.Exit(success ? 0 : 1);
                }
            });
        }
        else if (isReportTest)
        {
            _ = Task.Run(async () =>
            {
                bool success = await Core.SceneDiagnostics.RunReportSelfTestAsync(mainWindow);
                if (Array.Exists(e.Args, a => a.Equals("--exit-after-test", StringComparison.OrdinalIgnoreCase)))
                {
                    await Task.Delay(1000);
                    Environment.Exit(success ? 0 : 1);
                }
            });
        }
        else if (isLayoutAudit)
        {
            _ = Task.Run(async () =>
            {
                bool success = await Core.SceneDiagnostics.RunLayoutAuditAsync(mainWindow);
                Environment.Exit(success ? 0 : 1);
            });
        }
        else if (isScaleCrispTest)
        {
            _ = Task.Run(async () =>
            {
                bool success = await Core.SceneDiagnostics.RunScalingCrispnessTestAsync(mainWindow);
                if (Array.Exists(e.Args, a => a.Equals("--exit-after-test", StringComparison.OrdinalIgnoreCase)))
                {
                    await Task.Delay(1000);
                    Environment.Exit(success ? 0 : 1);
                }
            });
        }
        else if (isColorsTest)
        {
            _ = Task.Run(async () =>
            {
                bool success = await Core.SceneDiagnostics.RunColorsTestAsync(mainWindow);
                if (Array.Exists(e.Args, a => a.Equals("--exit-after-test", StringComparison.OrdinalIgnoreCase)))
                {
                    await Task.Delay(1000);
                    Environment.Exit(success ? 0 : 1);
                }
            });
        }
        else if (isSplashTest)
        {
            _ = Task.Run(async () =>
            {
                bool success = await Core.SceneDiagnostics.RunSplashSelfTestAsync(mainWindow);
                if (Array.Exists(e.Args, a => a.Equals("--exit-after-test", StringComparison.OrdinalIgnoreCase)))
                {
                    await Task.Delay(1000);
                    Environment.Exit(success ? 0 : 1);
                }
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
#endif
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
        services.AddSingleton<IModManifestService, ModManifestService>();
        services.AddSingleton<ILobbyApiClient>(sp => new LobbyApiClient(null, "https://lobby-api.vercel.app", sp.GetRequiredService<IConfigService>()));
        
        if (useFakeTunnel)
        {
            services.AddSingleton<ITunnelProvider, FakeTunnelProvider>();
        }
        else
        {
            services.AddSingleton<ITunnelProvider, PlayitTunnelProvider>();
        }

        services.AddSingleton<ILobbyService>(sp => new LobbyService(
            sp.GetRequiredService<ILobbyApiClient>(),
            sp.GetRequiredService<ITunnelProvider>(),
            sp.GetRequiredService<IConfigService>(),
            sp.GetRequiredService<IModManifestService>(),
            sp.GetRequiredService<IGameLaunchService>()));
        services.AddSingleton<ILanWorldWatcher, LanWorldWatcher>();
        services.AddSingleton<IAnthemService, AnthemService>();
        services.AddSingleton<IFriendService, FriendService>();
        services.AddSingleton<INotificationService, NotificationService>();
        services.AddSingleton<IDiscordRpcService, DiscordRpcService>();
        services.AddSingleton<IReportService, ReportService>();
        services.AddSingleton<IBackgroundService, BackgroundService>();
        services.AddSingleton<IWorkshopService, WorkshopService>();
        services.AddSingleton<IAchievementService, AchievementService>();
        services.AddSingleton<IMinecraftPingService, MinecraftPingService>();
        services.AddSingleton<IScreenshotWatcherService, ScreenshotWatcherService>();

        // Регистрация ViewModels
        services.AddSingleton<OverviewViewModel>();
        services.AddSingleton<SettingsViewModel>(sp => new SettingsViewModel(
            sp.GetRequiredService<IConfigService>(),
            sp.GetRequiredService<ISkinService>(),
            sp.GetService<IDiscordRpcService>(),
            sp.GetService<IReportService>(),
            sp.GetService<IBackgroundService>(),
            sp.GetRequiredService<IPackUpdateService>(),
            sp.GetService<INotificationService>(),
            sp.GetService<IFriendService>()));
        services.AddSingleton<WorkshopViewModel>();
        services.AddSingleton<WardrobeViewModel>(sp => new WardrobeViewModel(
            sp.GetRequiredService<ISkinService>(),
            sp.GetRequiredService<IConfigService>(),
            sp.GetRequiredService<IFriendService>()));
        services.AddSingleton<LobbyViewModel>(sp => new LobbyViewModel(
            sp.GetRequiredService<ILobbyService>(),
            sp.GetRequiredService<IGameLaunchService>(),
            sp.GetRequiredService<IConfigService>(),
            sp.GetRequiredService<ILanWorldWatcher>(),
            sp.GetRequiredService<ITunnelProvider>(),
            sp.GetRequiredService<ISkinService>(),
            sp.GetRequiredService<ILobbyApiClient>(),
            sp.GetRequiredService<INotificationService>(),
            sp.GetRequiredService<IDiscordRpcService>(),
            sp.GetRequiredService<IModManifestService>(),
            sp.GetRequiredService<IServerListSyncService>(),
            sp.GetRequiredService<IWorkshopService>(),
            sp.GetRequiredService<IMinecraftPingService>()));
        services.AddSingleton<FriendsViewModel>();
        services.AddSingleton<MainViewModel>();
    }

    private static void LogCrash(Exception? ex, bool isFatal = false)
    {
        if (ex == null || Current?.Dispatcher?.HasShutdownStarted == true) return;
        try
        {
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string logEntry = $"[{timestamp}] {(isFatal ? "Fatal Crash" : "Unhandled Exception")}:\n{ex}\n----------------------------------------\n";

            // Запись в системный %AppData%\Aura\launcher.log
            try
            {
                string appDataAura = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Aura");
                Directory.CreateDirectory(appDataAura);
                string appDataLogPath = Path.Combine(appDataAura, "launcher.log");
                File.AppendAllText(appDataLogPath, logEntry);
            }
            catch { }

            // Показываем диалог пользователю ТОЛЬКО при фатальных сбоях (и если не идет штатное завершение)
            if (isFatal && Current?.Dispatcher?.HasShutdownStarted != true && Interlocked.CompareExchange(ref _hasShownCrashDialog, 1, 0) == 0)
            {
                var result = MessageBox.Show(
                    $"Произошла фатальная ошибка в работе AURA Launcher:\n\n{ex.Message}\n\nОтправить отчёт разработчикам?",
                    "AURA Launcher — Фатальная ошибка",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Error);

                if (result == MessageBoxResult.Yes)
                {
                    try
                    {
                        var reportService = Services?.GetService<IReportService>();
                        reportService?.SendReportAsync(ex.ToString(), "Fatal Launcher Crash").GetAwaiter().GetResult();
                    }
                    catch { }
                }
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

    private static void RegisterAuraProtocol()
    {
        try
        {
            string? exePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exePath)) return;

            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Classes\aura");
            if (key != null)
            {
                key.SetValue("", "URL:Aura Protocol");
                key.SetValue("URL Protocol", "");
                using var commandKey = key.CreateSubKey(@"shell\open\command");
                if (commandKey != null)
                {
                    commandKey.SetValue("", $"\"{exePath}\" \"%1\"");
                }
            }
        }
        catch (Exception ex)
        {
            Log($"Failed to register aura:// protocol: {ex.Message}");
        }
    }

    private static void SignalExistingInstanceViaPipe(string pipeName, string? protocolArg = null)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out);
            client.Connect(1500); // таймаут 1.5 секунды
            using var writer = new StreamWriter(client) { AutoFlush = true };
            if (!string.IsNullOrWhiteSpace(protocolArg))
            {
                writer.WriteLine($"URI:{protocolArg.Trim()}");
            }
            else
            {
                writer.WriteLine("SHOW");
            }
        }
        catch { }
    }

    private static void StartPipeServer(string pipeName, MainWindow mainWindow)
    {
        _ = Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    using var server = new NamedPipeServerStream(
                        pipeName,
                        PipeDirection.In,
                        1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    await server.WaitForConnectionAsync();
                    using var reader = new StreamReader(server);
                    var msg = await reader.ReadLineAsync();
                    if (!string.IsNullOrWhiteSpace(msg))
                    {
                        mainWindow.Dispatcher.Invoke(() =>
                        {
                            mainWindow.RestoreFromTray();
                            if (msg.StartsWith("URI:", StringComparison.OrdinalIgnoreCase))
                            {
                                string uri = msg.Substring(4).Trim();
                                if (mainWindow.DataContext is MainViewModel mainVM)
                                {
                                    mainVM.HandleProtocolUri(uri);
                                }
                            }
                        });
                    }
                }
                catch
                {
                    await Task.Delay(1000);
                }
            }
        });
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
            var lobbyService = Services?.GetService<ILobbyService>();
            if (lobbyService != null)
            {
                if (lobbyService.IsHost && !string.IsNullOrWhiteSpace(lobbyService.CurrentLobbyCode) && !string.IsNullOrWhiteSpace(lobbyService.CurrentHostToken))
                {
                    var code = lobbyService.CurrentLobbyCode;
                    var token = lobbyService.CurrentHostToken;
                    var apiClient = Services?.GetService<ILobbyApiClient>();
                    if (apiClient != null)
                    {
                        Task.Run(async () => await apiClient.CloseLobbyAsync(code, token)).Wait(TimeSpan.FromMilliseconds(1500));
                    }
                }
                lobbyService.LeaveLobby();
            }
        }
        catch { }

        try
        {
            var friendService = Services?.GetService<IFriendService>();
            if (friendService != null)
            {
                // Synchronously wait max 1.5s for offline status delivery to server
                Task.Run(async () => await friendService.ReportOfflineAsync()).Wait(TimeSpan.FromMilliseconds(1500));
                friendService.Stop();
                friendService.Dispose();
            }
        }
        catch { }

        try
        {
            var anthemService = Services?.GetService<IAnthemService>();
            anthemService?.Stop();
            anthemService?.Dispose();
        }
        catch { }

        try
        {
            _singleInstanceMutex?.ReleaseMutex();
            _singleInstanceMutex?.Dispose();
        }
        catch { }
        base.OnExit(e);
    }
}
