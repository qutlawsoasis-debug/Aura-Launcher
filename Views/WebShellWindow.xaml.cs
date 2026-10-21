using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using AuraLauncher.Core.WebBridge;
using AuraLauncher.Services.Interfaces;
using Microsoft.Web.WebView2.Core;

namespace AuraLauncher.Views;

public partial class WebShellWindow : Window
{
    private readonly IGameLaunchService _gameLaunchService;
    private readonly IConfigService _configService;
    private WebBridge? _bridge;

    public WebShellWindow(
        IGameLaunchService gameLaunchService,
        IConfigService configService)
    {
        InitializeComponent();
        _gameLaunchService = gameLaunchService;
        _configService = configService;

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

            _bridge = new WebBridge(this, _gameLaunchService, _configService);
            _bridge.Attach(WebViewControl.CoreWebView2);

            string targetUrl = await ResolveTargetUrlAsync();
            WebViewControl.CoreWebView2.Navigate(targetUrl);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка запуска Web UI: {ex.Message}", "Aura Launcher", MessageBoxButton.OK, MessageBoxImage.Error);
        }
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
                return Task.FromResult("http://localhost:5173");
            }
        }
        catch { }
#endif

        // Режим виртуального хоста aura.local
        string uiFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ui");
        if (!Directory.Exists(uiFolder))
        {
            // Фолбэк на репозиторий docs/redesign/v3
            string repoDocsV3 = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "docs", "redesign", "v3"));
            if (Directory.Exists(repoDocsV3))
            {
                uiFolder = repoDocsV3;
            }
        }

        if (Directory.Exists(uiFolder))
        {
            WebViewControl.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "aura.local",
                uiFolder,
                CoreWebView2HostResourceAccessKind.Allow);
            return Task.FromResult("https://aura.local/index.html");
        }

        return Task.FromResult("about:blank");
    }

    private void WebShellWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _bridge?.Dispose();
    }
}
