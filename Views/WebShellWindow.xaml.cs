using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using AuraLauncher.Core.WebBridge;
using AuraLauncher.Services.Implementations;
using AuraLauncher.Services.Interfaces;
using Microsoft.Web.WebView2.Core;

namespace AuraLauncher.Views;

public partial class WebShellWindow : Window
{
    private readonly IGameSessionService _gameSessionService;
    private readonly IConfigService _configService;
    private readonly IGraphicsPresetService? _presetService;
    private readonly IWorkshopService? _workshopService;
    private readonly IPackUpdateService? _packUpdateService;
    private readonly IGameLaunchService? _launchService;
    private readonly ISkinService? _skinService;
    private readonly IFriendService? _friendService;
    private readonly IReportService? _reportService;
    private readonly ILobbySessionService? _lobbySession;
    private readonly ILauncherUpdateService? _launcherUpdateService;
    private readonly ICloudLobbyService? _cloudLobbyService;
    private readonly IAchievementService? _achievementService;
    private readonly IDiscordRpcService? _discordRpcService;
    private readonly IPrewarmGameService? _prewarmGameService;
    private WebBridge? _bridge;

    /// <summary>Лаунчер только что перезапущен после обновления — экран загрузки покажет «Что нового».</summary>
    public bool IsUpdatedRestart { get; init; }

    // Флаг в адресе виден странице до первой отрисовки: заставка сразу продолжает «после обновления»
    private string StartupQuery => IsUpdatedRestart ? "?updated=1" : string.Empty;

    /// <summary>Ссылка aura://, с которой запустили лаунчер (обрабатывается, когда мост готов).</summary>
    public string? StartupProtocolUri { get; set; }

    /// <summary>Ссылка aura:// из второго экземпляра лаунчера.</summary>
    public void HandleProtocolUri(string? uri)
    {
        if (_bridge != null) _bridge.HandleProtocolUri(uri);
        else StartupProtocolUri = uri;
    }

    public WebShellWindow(
        IGameSessionService gameSessionService,
        IConfigService configService,
        IGraphicsPresetService? presetService = null,
        IWorkshopService? workshopService = null,
        IPackUpdateService? packUpdateService = null,
        IGameLaunchService? launchService = null,
        ISkinService? skinService = null,
        IFriendService? friendService = null,
        IReportService? reportService = null,
        ILobbySessionService? lobbySession = null,
        ILauncherUpdateService? launcherUpdateService = null,
        ICloudLobbyService? cloudLobbyService = null,
        IAchievementService? achievementService = null,
        IDiscordRpcService? discordRpcService = null,
        IPrewarmGameService? prewarmGameService = null)
    {
        InitializeComponent();
        if (TryFindResource("BgBase") is System.Windows.Media.SolidColorBrush bgBrush)
        {
            WebViewControl.DefaultBackgroundColor = System.Drawing.Color.FromArgb(bgBrush.Color.A, bgBrush.Color.R, bgBrush.Color.G, bgBrush.Color.B);
        }
        _gameSessionService = gameSessionService;
        _configService = configService;
        _presetService = presetService;
        _workshopService = workshopService;
        _packUpdateService = packUpdateService;
        _launchService = launchService;
        _skinService = skinService;
        _friendService = friendService;
        _reportService = reportService;
        _lobbySession = lobbySession;
        _launcherUpdateService = launcherUpdateService;
        _cloudLobbyService = cloudLobbyService;
        _achievementService = achievementService;
        _discordRpcService = discordRpcService;
        _prewarmGameService = prewarmGameService;

        Loaded += WebShellWindow_Loaded;
        Closing += WebShellWindow_Closing;
    }

    private async void WebShellWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            string userDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Aura", "WebView2");
            var env = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataDir);
            await WebViewControl.EnsureCoreWebView2Async(env);

            WebViewControl.CoreWebView2.Settings.IsNonClientRegionSupportEnabled = true;
#if DEBUG
            WebViewControl.CoreWebView2.Settings.AreDevToolsEnabled = true;
#else
            WebViewControl.CoreWebView2.Settings.AreDevToolsEnabled = false;
#endif

            _bridge = new WebBridge(this, _gameSessionService, _configService, _presetService, _workshopService, _packUpdateService, _launchService, _skinService, _friendService, _reportService, _lobbySession, _launcherUpdateService, _cloudLobbyService, _achievementService, _discordRpcService, _prewarmGameService)
            {
                IsUpdatedRestart = IsUpdatedRestart
            };
            _bridge.Attach(WebViewControl.CoreWebView2);
            if (!string.IsNullOrWhiteSpace(StartupProtocolUri)) _bridge.HandleProtocolUri(StartupProtocolUri);

            _friendService?.Start();

            // Фоновый предзапуск игры при старте лаунчера, если включен Быстрый старт
            _ = Task.Run(async () =>
            {
                try
                {
                    if (_prewarmGameService != null && _configService.CurrentConfig.FastStartEnabled)
                    {
                        await _prewarmGameService.StartPrewarmAsync();
                    }
                }
                catch (Exception ex)
                {
                    FabricGameLaunchService.LogLauncherEvent($"[PREWARM: STARTUP ERROR] {ex.Message}");
                }
            });

            RegisterVirtualHosts();

            string targetUrl = await ResolveTargetUrlAsync();
            WebViewControl.CoreWebView2.Navigate(targetUrl);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка запуска Web UI: {ex.Message}", "Aura Launcher", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Папка собранного интерфейса: ui рядом с exe (релиз) или docs/redesign/v3/dist в репозитории (Debug).
    /// </summary>
    private static string? ResolveUiFolder()
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string[] candidates =
        {
            Path.Combine(baseDir, "ui"),
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "docs", "redesign", "v3", "dist"))
        };
        foreach (var dir in candidates)
        {
            if (File.Exists(Path.Combine(dir, "index.html"))) return dir;
        }
        return null;
    }

    /// <summary>
    /// Есть ли из чего показать новый интерфейс (собранный бандл или, в Debug, dev-сервер Vite).
    /// </summary>
    public static bool HasUiBundle()
    {
        if (ResolveUiFolder() != null) return true;
#if DEBUG
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(500) };
            return client.GetAsync("http://localhost:5173").GetAwaiter().GetResult().IsSuccessStatusCode;
        }
        catch { }
#endif
        return false;
    }

    private Task<string> ResolveTargetUrlAsync()
    {
#if DEBUG
        // Проверка отклика dev-сервера Vite
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(500) };
            var res = client.GetAsync("http://localhost:5173").GetAwaiter().GetResult();
            if (res.IsSuccessStatusCode)
            {
                return Task.FromResult("http://localhost:5173/" + StartupQuery);
            }
        }
        catch { }
#endif

        // Режим виртуального хоста aura.local
        string? uiFolder = ResolveUiFolder();
        if (uiFolder != null)
        {
            WebViewControl.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "aura.local",
                uiFolder,
                CoreWebView2HostResourceAccessKind.Allow);
            return Task.FromResult("https://aura.local/index.html" + StartupQuery);
        }

        return Task.FromResult("about:blank");
    }

    private void RegisterVirtualHosts()
    {
        string? uiFolder = ResolveUiFolder();
        if (uiFolder != null)
        {
            WebViewControl.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "aura.local",
                uiFolder,
                CoreWebView2HostResourceAccessKind.Allow);
        }

        var cfg = _configService.CurrentConfig;
        string instanceDir = _launchService != null
            ? _launchService.ResolveInstanceDirectory(cfg.GameDir, cfg.SelectedVersion)
            : (string.IsNullOrWhiteSpace(cfg.GameDir)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".aura")
                : cfg.GameDir);

        string screenshotsDir = Path.Combine(instanceDir, "screenshots");
        try
        {
            Directory.CreateDirectory(screenshotsDir);
            string thumbsDir = Path.Combine(screenshotsDir, ".thumbs");
            Directory.CreateDirectory(thumbsDir);

            WebViewControl.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "shots.aura.local",
                screenshotsDir,
                CoreWebView2HostResourceAccessKind.Allow);

            WebViewControl.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "shots-thumbs.aura.local",
                thumbsDir,
                CoreWebView2HostResourceAccessKind.Allow);
        }
        catch { }
    }

    private void WebShellWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _prewarmGameService?.StopPrewarm();
        _bridge?.Dispose();
    }
}
