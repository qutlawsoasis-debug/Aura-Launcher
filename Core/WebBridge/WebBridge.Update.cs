using System;
using System.Threading;
using System.Threading.Tasks;
using AuraLauncher.Models;
using AuraLauncher.Services.Interfaces;

namespace AuraLauncher.Core.WebBridge;

/// <summary>
/// Фоновая проверка обновления лаунчера (как MainViewModel.CheckBackgroundUpdatesAsync)
/// и установка по кнопке из интерфейса.
/// </summary>
public partial class WebBridge
{
    private static readonly TimeSpan UpdateFirstCheckDelay = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan UpdatePollInterval = TimeSpan.FromMinutes(3);

    private CancellationTokenSource? _updatePollCts;
    private string? _pendingLauncherVersion;
    private bool _isApplyingUpdate;

    /// <summary>
    /// Velopack-версия 1.2.N показывается пользователю как beta 1.0.(N-8).
    /// </summary>
    internal static string DisplayVersion(string version)
    {
        var parts = version.Split('.');
        return parts.Length == 3 && int.TryParse(parts[2], out int patch) && patch >= 8
            ? $"beta 1.0.{patch - 8}"
            : version;
    }

    private void StartUpdatePolling()
    {
        if (_launcherUpdateService == null || !_launcherUpdateService.IsInstalled) return;
        if (!_configService.CurrentConfig.AutoUpdateEnabled) return;

        _updatePollCts = new CancellationTokenSource();
        var token = _updatePollCts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(UpdateFirstCheckDelay, token);
                using var timer = new PeriodicTimer(UpdatePollInterval);
                do
                {
                    await CheckLauncherUpdateAsync(token);
                }
                while (await timer.WaitForNextTickAsync(token));
            }
            catch (OperationCanceledException) { }
        }, token);
    }

    private void StopUpdatePolling()
    {
        _updatePollCts?.Cancel();
        _updatePollCts?.Dispose();
        _updatePollCts = null;
    }

    private async Task CheckLauncherUpdateAsync(CancellationToken token)
    {
        if (_launcherUpdateService == null || _isApplyingUpdate) return;
        try
        {
            string? newVersion = await _launcherUpdateService.CheckForUpdatesAsync(token);
            if (string.IsNullOrWhiteSpace(newVersion))
            {
                if (_pendingLauncherVersion != null)
                {
                    _pendingLauncherVersion = null;
                    SendEvent("launcher.update", new { available = false });
                }
                return;
            }

            if (string.Equals(_pendingLauncherVersion, newVersion, StringComparison.OrdinalIgnoreCase)) return;
            _pendingLauncherVersion = newVersion;
            _logger($"[WEBBRIDGE] launcher update available: {newVersion}");
            SendEvent("launcher.update", new { available = true, version = newVersion, display = DisplayVersion(newVersion) });
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger($"[WEBBRIDGE: WARN] launcher update check: {ex.Message}");
        }
    }

    private void HandleLauncherUpdateGet(BridgeMessage msg)
    {
        SendResponse(msg.Id, msg.Name, new
        {
            current = _launcherUpdateService?.CurrentVersion,
            currentDisplay = _launcherUpdateService != null ? DisplayVersion(_launcherUpdateService.CurrentVersion) : null,
            available = _pendingLauncherVersion != null,
            version = _pendingLauncherVersion,
            display = _pendingLauncherVersion != null ? DisplayVersion(_pendingLauncherVersion) : null
        });
    }

    private async Task HandleLauncherApplyUpdateAsync(BridgeMessage msg)
    {
        if (_launcherUpdateService == null || !_launcherUpdateService.IsInstalled)
        {
            SendError(msg.Id, msg.Name, "Обновление доступно только в установленной версии");
            return;
        }
        if (_gameSessionService.State != GameSessionState.Idle && _gameSessionService.State != GameSessionState.Failed)
        {
            SendError(msg.Id, msg.Name, "Сначала закройте игру");
            return;
        }
        if (_isApplyingUpdate)
        {
            SendError(msg.Id, msg.Name, "busy");
            return;
        }

        _isApplyingUpdate = true;
        _logger("[WEBBRIDGE] launcher.applyUpdate");
        try
        {
            if (IsInLobby) await _lobbySession!.LeaveLobbyAsync();

            long lastProgressTick = 0;
            var progress = new Progress<DownloadProgressReport>(p =>
            {
                long now = Environment.TickCount64;
                if (now - lastProgressTick < 100 && p.Percentage < 100) return;
                lastProgressTick = now;
                SendEvent("launcher.updateProgress", new { percentage = p.Percentage, status = p.StatusText });
            });
            var result = await _launcherUpdateService.DownloadAndApplyAsync(progress);

            if (result.Status == LauncherUpdateStatus.UpdatedRestarting)
            {
                SendResponse(msg.Id, msg.Name, new { status = "restarting" });
                _logger("[WEBBRIDGE] launcher update applied, restarting");
                // Velopack ждёт выхода процесса, затем ставит обновление и перезапускает лаунчер
                await Task.Delay(600);
                if (System.Windows.Application.Current is { } app)
                    app.Dispatcher.Invoke(app.Shutdown);
                else
                    Environment.Exit(0);
                return;
            }

            _isApplyingUpdate = false;
            SendError(msg.Id, msg.Name, string.IsNullOrWhiteSpace(result.Message) ? "Не удалось обновить" : result.Message);
        }
        catch (Exception ex)
        {
            _isApplyingUpdate = false;
            _logger($"[WEBBRIDGE: ERROR] launcher.applyUpdate: {ex.Message}");
            SendError(msg.Id, msg.Name, ex.Message);
        }
    }
}
