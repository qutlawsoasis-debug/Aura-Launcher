using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using AuraLauncher.Models;
using AuraLauncher.Services.Interfaces;
using Microsoft.Web.WebView2.Core;

namespace AuraLauncher.Core.WebBridge;

public class WebBridge : IDisposable
{
    private readonly Window _window;
    private readonly IGameLaunchService _gameLaunchService;
    private readonly IConfigService _configService;
    private CoreWebView2? _coreWebView2;
    private CancellationTokenSource? _gameLaunchCts;

    public WebBridge(
        Window window,
        IGameLaunchService gameLaunchService,
        IConfigService configService)
    {
        _window = window;
        _gameLaunchService = gameLaunchService;
        _configService = configService;

        _gameLaunchService.GameExited += OnGameExited;
    }

    public void Attach(CoreWebView2 coreWebView2)
    {
        _coreWebView2 = coreWebView2;
        _coreWebView2.WebMessageReceived += OnWebMessageReceived;

        // Отправка начального статуса
        SendEvent("game.status", new
        {
            isRunning = _gameLaunchService.IsGameRunning,
            processId = _gameLaunchService.CurrentGameProcess?.Id,
            exitCode = (int?)null
        });
    }

    private async void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string rawJson = e.WebMessageAsJson;
        BridgeMessage? msg;
        try
        {
            msg = JsonSerializer.Deserialize<BridgeMessage>(rawJson);
        }
        catch (Exception ex)
        {
            SendError(null, "parse.error", ex.Message);
            return;
        }

        if (msg == null) return;

        try
        {
            switch (msg.Name)
            {
                case "game.launch":
                    await HandleGameLaunchAsync(msg);
                    break;

                case "game.stop":
                    await HandleGameStopAsync(msg);
                    break;

                case "window.action":
                    HandleWindowAction(msg);
                    break;

                default:
                    SendError(msg.Id, "unknown.command", $"Command '{msg.Name}' is not implemented yet");
                    break;
            }
        }
        catch (Exception ex)
        {
            SendError(msg.Id, msg.Name, ex.Message);
        }
    }

    private Task HandleGameLaunchAsync(BridgeMessage msg)
    {
        if (_gameLaunchService.IsGameRunning)
        {
            SendResponse(msg.Id, msg.Name, new { started = false, reason = "Game is already running" });
            return Task.CompletedTask;
        }

        _gameLaunchCts?.Cancel();
        _gameLaunchCts = new CancellationTokenSource();

        var config = _configService.CurrentConfig;

        var progress = new Progress<InstallProgressReport>(report =>
        {
            SendEvent("game.progress", new
            {
                percentage = report.Percentage,
                stage = report.Stage ?? string.Empty,
                detail = report.DetailText ?? string.Empty
            });
        });

        SendEvent("game.status", new
        {
            isRunning = true,
            isLaunching = true
        });

        _ = Task.Run(async () =>
        {
            try
            {
                var process = await _gameLaunchService.LaunchGameAsync(
                    config,
                    onLogReceived: null,
                    onGameExited: null,
                    installProgress: progress,
                    cancellationToken: _gameLaunchCts.Token);

                SendResponse(msg.Id, msg.Name, new { started = true, processId = process.Id });

                SendEvent("game.status", new
                {
                    isRunning = true,
                    processId = process.Id,
                    exitCode = (int?)null
                });
            }
            catch (Exception ex)
            {
                SendError(msg.Id, msg.Name, ex.Message);
                SendEvent("game.status", new
                {
                    isRunning = false,
                    processId = (int?)null,
                    exitCode = -1,
                    error = ex.Message
                });
            }
        });
        return Task.CompletedTask;
    }

    private async Task HandleGameStopAsync(BridgeMessage msg)
    {
        _gameLaunchCts?.Cancel();
        bool stopped = await _gameLaunchService.StopGameProcessAsync(5000);
        SendResponse(msg.Id, msg.Name, new { stopped });
    }

    private void HandleWindowAction(BridgeMessage msg)
    {
        string action = string.Empty;
        if (msg.Payload is JsonElement el && el.TryGetProperty("action", out var prop))
        {
            action = prop.GetString() ?? string.Empty;
        }

        _window.Dispatcher.Invoke(() =>
        {
            switch (action)
            {
                case "minimize":
                    _window.WindowState = WindowState.Minimized;
                    break;
                case "maximize":
                    _window.WindowState = _window.WindowState == WindowState.Maximized
                        ? WindowState.Normal
                        : WindowState.Maximized;
                    break;
                case "close":
                    _window.Close();
                    break;
                case "drag":
                    try { _window.DragMove(); } catch { }
                    break;
            }
        });

        SendResponse(msg.Id, msg.Name, new { action, success = true });
    }

    private void OnGameExited(object? sender, int exitCode)
    {
        SendEvent("game.status", new
        {
            isRunning = false,
            processId = (int?)null,
            exitCode
        });
    }

    public void SendEvent(string eventName, object payload)
    {
        Post(new BridgeMessage
        {
            Type = "event",
            Name = eventName,
            Payload = payload
        });
    }

    public void SendResponse(string? id, string name, object payload)
    {
        Post(new BridgeMessage
        {
            Id = id,
            Type = "response",
            Name = name,
            Payload = payload
        });
    }

    public void SendError(string? id, string name, string error)
    {
        Post(new BridgeMessage
        {
            Id = id,
            Type = "response",
            Name = name,
            Error = error
        });
    }

    private void Post(BridgeMessage msg)
    {
        if (_coreWebView2 == null) return;
        string json = JsonSerializer.Serialize(msg);
        _window.Dispatcher.Invoke(() =>
        {
            try
            {
                _coreWebView2?.PostWebMessageAsJson(json);
            }
            catch { }
        });
    }

    public void Dispose()
    {
        _gameLaunchService.GameExited -= OnGameExited;
        if (_coreWebView2 != null)
        {
            _coreWebView2.WebMessageReceived -= OnWebMessageReceived;
        }
        _gameLaunchCts?.Cancel();
        _gameLaunchCts?.Dispose();
    }
}
