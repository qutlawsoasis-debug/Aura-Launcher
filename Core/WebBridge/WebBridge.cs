using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using AuraLauncher.Models;
using AuraLauncher.Services.Implementations;
using AuraLauncher.Services.Interfaces;
using Microsoft.Web.WebView2.Core;

namespace AuraLauncher.Core.WebBridge;

public class WebBridge : IDisposable
{
    private readonly Window _window;
    private readonly IGameSessionService _gameSessionService;
    private readonly IConfigService _configService;
    private CoreWebView2? _coreWebView2;
    private CancellationTokenSource? _gameLaunchCts;

    public WebBridge(
        Window window,
        IGameSessionService gameSessionService,
        IConfigService configService)
    {
        _window = window;
        _gameSessionService = gameSessionService;
        _configService = configService;

        _gameSessionService.StateChanged += OnSessionStateChanged;
        _gameSessionService.GameExited += OnSessionGameExited;
        _window.StateChanged += OnWindowStateChanged;
    }

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        SendWindowState();
    }

    public void SendWindowState()
    {
        string state = _window.WindowState switch
        {
            WindowState.Maximized => "maximized",
            WindowState.Minimized => "minimized",
            _ => "normal"
        };
        SendEvent("window.state", new { state });
    }

    public void Attach(CoreWebView2 coreWebView2)
    {
        _coreWebView2 = coreWebView2;
        _coreWebView2.WebMessageReceived += OnWebMessageReceived;

        // Отправка начального статуса окна
        SendWindowState();

        // Отправка начального статуса
        SendEvent("game.status", new
        {
            state = MapState(_gameSessionService.State),
            isRunning = _gameSessionService.IsGameRunning,
            processId = _gameSessionService.CurrentProcess?.Id,
            exitCode = (int?)null
        });
    }

    private static string MapState(GameSessionState state) => state switch
    {
        GameSessionState.Idle => "idle",
        GameSessionState.Preparing => "preparing",
        GameSessionState.Launching => "launching",
        GameSessionState.Running => "running",
        GameSessionState.Failed => "failed",
        _ => "idle"
    };

    private void OnSessionStateChanged(object? sender, GameSessionState state)
    {
        FabricGameLaunchService.LogLauncherEvent($"[WEBBRIDGE] StateChanged -> {state}");
        SendEvent("game.status", new
        {
            state = MapState(state),
            isRunning = state == GameSessionState.Running,
            processId = _gameSessionService.CurrentProcess?.Id,
            exitCode = (int?)null
        });
    }

    private void OnSessionGameExited(object? sender, GameExitedEventArgs e)
    {
        FabricGameLaunchService.LogLauncherEvent($"[WEBBRIDGE] GameExited -> exitCode={e.ExitCode}");
        SendEvent("game.status", new
        {
            state = e.ExitCode == 0 ? "idle" : "failed",
            isRunning = false,
            processId = (int?)null,
            exitCode = e.ExitCode
        });
    }

    private async void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string rawInput;
        try
        {
            rawInput = e.TryGetWebMessageAsString();
            if (string.IsNullOrEmpty(rawInput))
            {
                rawInput = e.WebMessageAsJson;
            }
        }
        catch
        {
            rawInput = e.WebMessageAsJson;
        }

        var (msg, parseError) = BridgeMessageParser.Parse(rawInput);
        if (parseError != null || msg == null)
        {
            FabricGameLaunchService.LogLauncherEvent($"[WEBBRIDGE: ERROR] JSON parse failed: {parseError}");
            SendError(null, "parse.error", parseError ?? "unknown_parse_error");
            return;
        }

        FabricGameLaunchService.LogLauncherEvent($"[WEBBRIDGE] Command received: {msg.Name} (id={msg.Id})");

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
                    FabricGameLaunchService.LogLauncherEvent($"[WEBBRIDGE: WARN] Unknown command: {msg.Name}");
                    SendError(msg.Id, "unknown.command", $"Command '{msg.Name}' is not implemented yet");
                    break;
            }
        }
        catch (Exception ex)
        {
            FabricGameLaunchService.LogLauncherEvent($"[WEBBRIDGE: ERROR] Error handling {msg.Name}: {ex.Message}");
            SendError(msg.Id, msg.Name, ex.Message);
        }
    }

    private Task HandleGameLaunchAsync(BridgeMessage msg)
    {
        if (_gameSessionService.State != GameSessionState.Idle && _gameSessionService.State != GameSessionState.Failed)
        {
            FabricGameLaunchService.LogLauncherEvent("[WEBBRIDGE: WARN] game.launch rejected: already_launching");
            SendError(msg.Id, msg.Name, "already_launching");
            return Task.CompletedTask;
        }

        string nickname = _configService.CurrentConfig.Nickname?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(nickname) || !NicknameValidator.Validate(nickname).IsValid)
        {
            FabricGameLaunchService.LogLauncherEvent("[WEBBRIDGE: WARN] game.launch rejected: invalid_nickname");
            SendError(msg.Id, msg.Name, "invalid_nickname");
            return Task.CompletedTask;
        }

        // Асинхронное подтверждение приема команды на запуск
        SendResponse(msg.Id, msg.Name, new { accepted = true });

        _gameLaunchCts?.Cancel();
        _gameLaunchCts = new CancellationTokenSource();

        var progress = new Progress<LaunchProgress>(p =>
        {
            SendEvent("game.progress", new
            {
                stage = p.Stage ?? string.Empty,
                percentage = p.Percentage,
                detail = p.DetailText ?? string.Empty,
                speed = p.FormattedSpeed ?? string.Empty,
                remaining = p.FormattedRemainingTime ?? string.Empty
            });
        });

        _ = Task.Run(async () =>
        {
            try
            {
                FabricGameLaunchService.LogLauncherEvent("[WEBBRIDGE] Starting GameSessionService.LaunchAsync...");
                var process = await _gameSessionService.LaunchAsync(
                    options: null,
                    progress: progress,
                    cancellationToken: _gameLaunchCts.Token);

                FabricGameLaunchService.LogLauncherEvent($"[WEBBRIDGE] Game launched successfully! PID={process.Id}");
            }
            catch (Exception ex)
            {
                FabricGameLaunchService.LogLauncherEvent($"[WEBBRIDGE: ERROR] Launch failed: {ex.Message}");
                SendEvent("game.status", new
                {
                    state = "failed",
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
        FabricGameLaunchService.LogLauncherEvent("[WEBBRIDGE] game.stop requested");
        _gameLaunchCts?.Cancel();
        bool stopped = await _gameSessionService.StopAsync(5000);
        FabricGameLaunchService.LogLauncherEvent($"[WEBBRIDGE] game.stop completed: stopped={stopped}");
        SendResponse(msg.Id, msg.Name, new { stopped });
    }

    private void HandleWindowAction(BridgeMessage msg)
    {
        string action = string.Empty;
        if (msg.Payload is JsonElement el && el.TryGetProperty("action", out var prop))
        {
            action = prop.GetString() ?? string.Empty;
        }

        FabricGameLaunchService.LogLauncherEvent($"[WEBBRIDGE] window.action: {action}");

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
        _gameSessionService.StateChanged -= OnSessionStateChanged;
        _gameSessionService.GameExited -= OnSessionGameExited;
        if (_coreWebView2 != null)
        {
            _coreWebView2.WebMessageReceived -= OnWebMessageReceived;
        }
        _gameLaunchCts?.Cancel();
        _gameLaunchCts?.Dispose();
    }
}
