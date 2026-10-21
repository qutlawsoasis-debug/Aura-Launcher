using System;
using System.IO;
using System.Linq;
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
    private readonly Window? _window;
    private readonly IGameSessionService _gameSessionService;
    private readonly IConfigService _configService;
    private readonly IGraphicsPresetService? _presetService;
    private readonly IWorkshopService? _workshopService;
    private readonly IPackUpdateService? _packUpdateService;
    private readonly Action<string> _logger;
    private CoreWebView2? _coreWebView2;
    private CancellationTokenSource? _gameLaunchCts;
    private CancellationTokenSource? _packSyncCts;
    private bool _isSyncing;

    public WebBridge(
        Window? window,
        IGameSessionService gameSessionService,
        IConfigService configService,
        IGraphicsPresetService? presetService = null,
        IWorkshopService? workshopService = null,
        IPackUpdateService? packUpdateService = null,
        Action<string>? logger = null)
    {
        _window = window;
        _gameSessionService = gameSessionService;
        _configService = configService;
        _presetService = presetService;
        _workshopService = workshopService;
        _packUpdateService = packUpdateService;
        _logger = logger ?? FabricGameLaunchService.LogLauncherEvent;

        _gameSessionService.StateChanged += OnSessionStateChanged;
        _gameSessionService.GameExited += OnSessionGameExited;
        if (_window != null)
        {
            _window.StateChanged += OnWindowStateChanged;
        }
    }

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        SendWindowState();
    }

    public void SendWindowState()
    {
        if (_window == null) return;
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
        _logger($"[WEBBRIDGE] StateChanged -> {state}");
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
        _logger($"[WEBBRIDGE] GameExited -> exitCode={e.ExitCode}");
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

        await ProcessRawMessageAsync(rawInput);
    }

    public async Task ProcessRawMessageAsync(string? rawInput)
    {
        var (msg, parseError) = BridgeMessageParser.Parse(rawInput);
        if (parseError != null || msg == null)
        {
            _logger($"[WEBBRIDGE: ERROR] JSON parse failed: {parseError}");
            SendError(null, "parse.error", parseError ?? "unknown_parse_error");
            return;
        }

        _logger($"[WEBBRIDGE] Command received: {msg.Name} (id={msg.Id})");

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

                case "config.get":
                    HandleConfigGet(msg);
                    break;

                case "config.set":
                    await HandleConfigSetAsync(msg);
                    break;

                case "preset.apply":
                    HandlePresetApply(msg);
                    break;

                case "workshop.list":
                    await HandleWorkshopListAsync(msg);
                    break;

                case "workshop.setShader":
                    await HandleWorkshopSetShaderAsync(msg);
                    break;

                case "workshop.toggleMod":
                    await HandleWorkshopToggleModAsync(msg);
                    break;

                case "pack.sync":
                    await HandlePackSyncAsync(msg);
                    break;

                case "fs.openModsFolder":
                    HandleFsOpenModsFolder(msg);
                    break;

                default:
                    _logger($"[WEBBRIDGE: WARN] Unknown command: {msg.Name}");
                    SendError(msg.Id, "unknown.command", $"Command '{msg.Name}' is not implemented yet");
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger($"[WEBBRIDGE: ERROR] Error handling {msg.Name}: {ex.Message}");
            SendError(msg.Id, msg.Name, ex.Message);
        }
    }

    private Task HandleGameLaunchAsync(BridgeMessage msg)
    {
        if (_gameSessionService.State != GameSessionState.Idle && _gameSessionService.State != GameSessionState.Failed)
        {
            _logger("[WEBBRIDGE: WARN] game.launch rejected: already_launching");
            SendError(msg.Id, msg.Name, "already_launching");
            return Task.CompletedTask;
        }

        string nickname = _configService.CurrentConfig.Nickname?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(nickname) || !NicknameValidator.Validate(nickname).IsValid)
        {
            _logger("[WEBBRIDGE: WARN] game.launch rejected: invalid_nickname");
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
                _logger("[WEBBRIDGE] Starting GameSessionService.LaunchAsync...");
                var process = await _gameSessionService.LaunchAsync(
                    options: null,
                    progress: progress,
                    cancellationToken: _gameLaunchCts.Token);

                _logger($"[WEBBRIDGE] Game launched successfully! PID={process.Id}");
            }
            catch (Exception ex)
            {
                _logger($"[WEBBRIDGE: ERROR] Launch failed: {ex.Message}");
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
        _logger("[WEBBRIDGE] game.stop requested");
        _gameLaunchCts?.Cancel();
        bool stopped = await _gameSessionService.StopAsync(5000);
        _logger($"[WEBBRIDGE] game.stop completed: stopped={stopped}");
        SendResponse(msg.Id, msg.Name, new { stopped });
    }

    private void HandleWindowAction(BridgeMessage msg)
    {
        string action = string.Empty;
        if (msg.Payload is JsonElement el && el.TryGetProperty("action", out var prop))
        {
            action = prop.GetString() ?? string.Empty;
        }

        _logger($"[WEBBRIDGE] window.action: {action}");

        if (_window != null)
        {
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
        }

        SendResponse(msg.Id, msg.Name, new { action, success = true });
    }

    private void HandleConfigGet(BridgeMessage msg)
    {
        var cfg = _configService.CurrentConfig;
        _logger($"[WEBBRIDGE] config.get: ram={cfg.RamMb}, preset={cfg.GraphicsPreset}, fx={cfg.VisualEffectsMode}");
        SendResponse(msg.Id, msg.Name, new
        {
            nickname = cfg.Nickname,
            ramMb = cfg.RamMb,
            hideLauncherWhilePlaying = cfg.HideLauncherWhilePlaying,
            graphicsPreset = cfg.GraphicsPreset,
            visualEffectsMode = cfg.VisualEffectsMode
        });
    }

    private async Task HandleConfigSetAsync(BridgeMessage msg)
    {
        if (msg.Payload is not JsonElement el || el.ValueKind != JsonValueKind.Object)
        {
            SendError(msg.Id, msg.Name, "invalid_payload");
            return;
        }

        if (!el.TryGetProperty("key", out var keyProp) || keyProp.ValueKind != JsonValueKind.String)
        {
            SendError(msg.Id, msg.Name, "invalid_key");
            return;
        }

        string key = keyProp.GetString() ?? string.Empty;
        if (!el.TryGetProperty("value", out var valProp))
        {
            SendError(msg.Id, msg.Name, "invalid_value");
            return;
        }

        switch (key)
        {
            case "ramMb":
            {
                if (valProp.ValueKind != JsonValueKind.Number || !valProp.TryGetInt32(out int ramVal))
                {
                    _logger($"[WEBBRIDGE: WARN] config.set ramMb rejected: not an integer");
                    SendError(msg.Id, msg.Name, "invalid_value");
                    return;
                }

                int maxRam = GraphicsPresetService.GetPhysicalRamMb();
                if (ramVal < 2048 || ramVal > maxRam)
                {
                    _logger($"[WEBBRIDGE: WARN] config.set ramMb rejected: {ramVal} outside 2048..{maxRam}");
                    SendError(msg.Id, msg.Name, "invalid_value");
                    return;
                }

                await _configService.UpdateConfigAsync(c => c.RamMb = ramVal);
                _logger($"[WEBBRIDGE] config.set ramMb -> {ramVal}");
                break;
            }

            case "hideLauncherWhilePlaying":
            {
                if (valProp.ValueKind != JsonValueKind.True && valProp.ValueKind != JsonValueKind.False)
                {
                    _logger($"[WEBBRIDGE: WARN] config.set hideLauncherWhilePlaying rejected: not a bool");
                    SendError(msg.Id, msg.Name, "invalid_value");
                    return;
                }

                bool hideVal = valProp.GetBoolean();
                await _configService.UpdateConfigAsync(c => c.HideLauncherWhilePlaying = hideVal);
                _logger($"[WEBBRIDGE] config.set hideLauncherWhilePlaying -> {hideVal}");
                break;
            }

            case "visualEffectsMode":
            {
                if (valProp.ValueKind != JsonValueKind.String)
                {
                    _logger($"[WEBBRIDGE: WARN] config.set visualEffectsMode rejected: not a string");
                    SendError(msg.Id, msg.Name, "invalid_value");
                    return;
                }

                string fxVal = valProp.GetString() ?? string.Empty;
                if (fxVal != "full" && fxVal != "low" && fxVal != "off")
                {
                    _logger($"[WEBBRIDGE: WARN] config.set visualEffectsMode rejected: unknown '{fxVal}'");
                    SendError(msg.Id, msg.Name, "invalid_value");
                    return;
                }

                await _configService.UpdateConfigAsync(c => c.VisualEffectsMode = fxVal);
                _logger($"[WEBBRIDGE] config.set visualEffectsMode -> {fxVal}");
                break;
            }

            default:
                _logger($"[WEBBRIDGE: WARN] config.set rejected: unknown key '{key}'");
                SendError(msg.Id, msg.Name, "invalid_key");
                return;
        }

        var cfg = _configService.CurrentConfig;
        SendResponse(msg.Id, msg.Name, new
        {
            nickname = cfg.Nickname,
            ramMb = cfg.RamMb,
            hideLauncherWhilePlaying = cfg.HideLauncherWhilePlaying,
            graphicsPreset = cfg.GraphicsPreset,
            visualEffectsMode = cfg.VisualEffectsMode
        });
    }

    private void HandlePresetApply(BridgeMessage msg)
    {
        if (_presetService == null)
        {
            _logger("[WEBBRIDGE: ERROR] preset.apply failed: IGraphicsPresetService not available");
            SendError(msg.Id, msg.Name, "service_unavailable");
            return;
        }

        string presetKey = string.Empty;
        if (msg.Payload is JsonElement el && el.TryGetProperty("presetKey", out var prop) && prop.ValueKind == JsonValueKind.String)
        {
            presetKey = prop.GetString() ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(presetKey))
        {
            _logger("[WEBBRIDGE: WARN] preset.apply rejected: empty presetKey");
            SendError(msg.Id, msg.Name, "invalid_preset");
            return;
        }

        _logger($"[WEBBRIDGE] preset.apply: {presetKey}");

        var cfg = _configService.CurrentConfig;
        var result = _presetService.ApplyPreset(cfg.GameDir, presetKey, persistConfig: true);

        _logger($"[WEBBRIDGE] preset.apply result: success={result.Success}, ram={result.FinalRamMb}, shader={result.ChosenShader}");
        SendResponse(msg.Id, msg.Name, result);
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

    private async Task HandleWorkshopListAsync(BridgeMessage msg)
    {
        if (_workshopService == null)
        {
            _logger("[WEBBRIDGE: ERROR] workshop.list failed: IWorkshopService not available");
            SendError(msg.Id, msg.Name, "service_unavailable");
            return;
        }

        string gameDir = _configService.CurrentConfig.GameDir;
        _logger($"[WEBBRIDGE] workshop.list requesting for {gameDir}");

        var shaderPacks = await _workshopService.GetShaderPacksAsync(gameDir);
        var mods = await _workshopService.GetModsAsync(gameDir);

        var shaderList = shaderPacks.Select(s => new
        {
            fileName = s.FileName,
            name = s.Name,
            isActive = s.IsActive
        }).ToList();

        var modList = mods.Select(m => new
        {
            fileName = m.FileName,
            name = m.DisplayName,
            version = m.Version,
            enabled = m.IsEnabled
        }).ToList();

        _logger($"[WEBBRIDGE] workshop.list response: {shaderList.Count} shaders, {modList.Count} mods");
        SendResponse(msg.Id, msg.Name, new
        {
            shaders = shaderList,
            mods = modList
        });
    }

    private async Task HandleWorkshopSetShaderAsync(BridgeMessage msg)
    {
        if (_workshopService == null)
        {
            _logger("[WEBBRIDGE: ERROR] workshop.setShader failed: IWorkshopService not available");
            SendError(msg.Id, msg.Name, "service_unavailable");
            return;
        }

        string fileName = string.Empty;
        if (msg.Payload is JsonElement el && el.TryGetProperty("fileName", out var fnProp) && fnProp.ValueKind == JsonValueKind.String)
        {
            fileName = fnProp.GetString() ?? string.Empty;
        }

        string gameDir = _configService.CurrentConfig.GameDir;
        _logger($"[WEBBRIDGE] workshop.setShader fileName='{fileName}'");

        if (string.IsNullOrWhiteSpace(fileName) || fileName.Equals("none", StringComparison.OrdinalIgnoreCase) || fileName.Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            _workshopService.SetActiveShaderPack(gameDir, "OFF");
            _logger("[WEBBRIDGE] workshop.setShader disabled shaders (OFF)");
            SendResponse(msg.Id, msg.Name, new { fileName = "", active = false });
            return;
        }

        var packs = await _workshopService.GetShaderPacksAsync(gameDir);
        var targetPack = packs.FirstOrDefault(p => string.Equals(p.FileName, fileName, StringComparison.OrdinalIgnoreCase) || string.Equals(p.Name, fileName, StringComparison.OrdinalIgnoreCase));
        if (targetPack == null)
        {
            _logger($"[WEBBRIDGE: WARN] workshop.setShader rejected: invalid_shader '{fileName}'");
            SendError(msg.Id, msg.Name, "invalid_shader");
            return;
        }

        _workshopService.SetActiveShaderPack(gameDir, targetPack.FileName);
        _logger($"[WEBBRIDGE] workshop.setShader activated '{targetPack.FileName}'");
        SendResponse(msg.Id, msg.Name, new { fileName = targetPack.FileName, active = true });
    }

    private async Task HandleWorkshopToggleModAsync(BridgeMessage msg)
    {
        if (_workshopService == null)
        {
            _logger("[WEBBRIDGE: ERROR] workshop.toggleMod failed: IWorkshopService not available");
            SendError(msg.Id, msg.Name, "service_unavailable");
            return;
        }

        string fileName = string.Empty;
        bool enabled = false;

        if (msg.Payload is JsonElement el)
        {
            if (el.TryGetProperty("fileName", out var fnProp) && fnProp.ValueKind == JsonValueKind.String)
            {
                fileName = fnProp.GetString() ?? string.Empty;
            }
            if (el.TryGetProperty("enabled", out var enProp) && (enProp.ValueKind == JsonValueKind.True || enProp.ValueKind == JsonValueKind.False))
            {
                enabled = enProp.GetBoolean();
            }
            else
            {
                _logger("[WEBBRIDGE: WARN] workshop.toggleMod rejected: missing enabled");
                SendError(msg.Id, msg.Name, "invalid_payload");
                return;
            }
        }

        if (string.IsNullOrWhiteSpace(fileName))
        {
            _logger("[WEBBRIDGE: WARN] workshop.toggleMod rejected: empty fileName");
            SendError(msg.Id, msg.Name, "invalid_payload");
            return;
        }

        string gameDir = _configService.CurrentConfig.GameDir;
        _logger($"[WEBBRIDGE] workshop.toggleMod fileName='{fileName}', targetEnabled={enabled}");

        var mods = await _workshopService.GetModsAsync(gameDir);
        var mod = mods.FirstOrDefault(m => string.Equals(m.FileName, fileName, StringComparison.OrdinalIgnoreCase)
                                        || string.Equals(m.DisplayName, fileName, StringComparison.OrdinalIgnoreCase));

        if (mod == null)
        {
            _logger($"[WEBBRIDGE: WARN] workshop.toggleMod rejected: mod_not_found '{fileName}'");
            SendError(msg.Id, msg.Name, "mod_not_found");
            return;
        }

        bool success = _workshopService.ToggleMod(mod, enabled);
        if (!success)
        {
            _logger($"[WEBBRIDGE: ERROR] workshop.toggleMod failed to toggle '{fileName}' to {enabled}");
            SendError(msg.Id, msg.Name, "toggle_failed");
            return;
        }

        _logger($"[WEBBRIDGE] workshop.toggleMod toggled: '{mod.FileName}', enabled={mod.IsEnabled}");
        SendResponse(msg.Id, msg.Name, new { fileName = mod.FileName, enabled = mod.IsEnabled });
    }

    private async Task HandlePackSyncAsync(BridgeMessage msg)
    {
        if (_packUpdateService == null)
        {
            _logger("[WEBBRIDGE: ERROR] pack.sync failed: IPackUpdateService not available");
            SendError(msg.Id, msg.Name, "service_unavailable");
            return;
        }

        if (_isSyncing || _gameSessionService.State == GameSessionState.Preparing || _gameSessionService.State == GameSessionState.Launching)
        {
            _logger("[WEBBRIDGE: WARN] pack.sync rejected: busy");
            SendError(msg.Id, msg.Name, "busy");
            return;
        }

        _isSyncing = true;
        _packSyncCts = new CancellationTokenSource();
        _logger("[WEBBRIDGE] pack.sync started");

        _ = Task.Run(async () =>
        {
            try
            {
                var progress = new Progress<DownloadProgressReport>(report =>
                {
                    SendEvent("pack.progress", new
                    {
                        percentage = Math.Round(report.Percentage, 1),
                        detail = string.IsNullOrWhiteSpace(report.CurrentFileName) ? report.StatusText : $"{report.StatusText} ({report.CurrentFileName})"
                    });
                });

                var result = await _packUpdateService.CheckAndApplyAsync(progress, _packSyncCts.Token);
                _logger($"[WEBBRIDGE] pack.sync finished: status={result.Status}, filesChanged={result.FilesChanged}, msg={result.Message}");

                SendResponse(msg.Id, msg.Name, new
                {
                    status = result.Status.ToString().ToLowerInvariant(),
                    message = result.Message,
                    filesChanged = result.FilesChanged
                });
            }
            catch (Exception ex)
            {
                _logger($"[WEBBRIDGE: ERROR] pack.sync failed: {ex.Message}");
                SendError(msg.Id, msg.Name, ex.Message);
            }
            finally
            {
                _isSyncing = false;
            }
        });

        await Task.CompletedTask;
    }

    private void HandleFsOpenModsFolder(BridgeMessage msg)
    {
        string gameDir = _configService.CurrentConfig.GameDir;
        string modsDir = Path.Combine(gameDir, "mods");
        Directory.CreateDirectory(modsDir);

        _logger($"[WEBBRIDGE] fs.openModsFolder: {modsDir}");

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{modsDir}\"",
                UseShellExecute = true
            });
            SendResponse(msg.Id, msg.Name, new { success = true, path = modsDir });
        }
        catch (Exception ex)
        {
            _logger($"[WEBBRIDGE: ERROR] fs.openModsFolder failed: {ex.Message}");
            SendError(msg.Id, msg.Name, ex.Message);
        }
    }

    public event Action<BridgeMessage>? MessagePosted;

    private void Post(BridgeMessage msg)
    {
        MessagePosted?.Invoke(msg);
        if (_coreWebView2 == null) return;
        _window?.Dispatcher.Invoke(() =>
        {
            try
            {
                string json = JsonSerializer.Serialize(msg);
                _coreWebView2?.PostWebMessageAsJson(json);
            }
            catch { }
        });
    }

    public void Dispose()
    {
        _gameSessionService.StateChanged -= OnSessionStateChanged;
        _gameSessionService.GameExited -= OnSessionGameExited;
        if (_window != null)
        {
            _window.StateChanged -= OnWindowStateChanged;
        }
        if (_coreWebView2 != null)
        {
            _coreWebView2.WebMessageReceived -= OnWebMessageReceived;
        }
        _gameLaunchCts?.Cancel();
        _gameLaunchCts?.Dispose();
    }
}
