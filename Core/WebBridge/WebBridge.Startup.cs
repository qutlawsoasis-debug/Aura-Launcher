using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AuraLauncher.Models;
using AuraLauncher.Services.Implementations;
using AuraLauncher.Services.Interfaces;

namespace AuraLauncher.Core.WebBridge;

/// <summary>
/// Экран загрузки в новом окне: тот же сценарий, что StartupWindowViewModel
/// (проверка обновления → скачивание и перезапуск либо запуск), но состояние уходит в интерфейс.
/// </summary>
public partial class WebBridge
{
    private static readonly TimeSpan StartupCheckTimeout = TimeSpan.FromSeconds(10);

    private TaskCompletionSource<bool>? _startupErrorTcs;
    private int _startupRunning;

    /// <summary>Лаунчер только что перезапущен после обновления (--updated-restart).</summary>
    public bool IsUpdatedRestart { get; init; }

    private void SendStartup(string stage, object? extra = null)
    {
        SendEvent("startup.state", new { stage, extra });
    }

    private void HandleStartupRun(BridgeMessage msg)
    {
        SendResponse(msg.Id, msg.Name, new
        {
            version = DisplayVersion(_launcherUpdateService?.CurrentVersion ?? "0.0.0"),
            updated = IsUpdatedRestart,
            changelog = IsUpdatedRestart ? ReadChangelog() : Array.Empty<string>()
        });

        if (Interlocked.Exchange(ref _startupRunning, 1) == 1) return;
        _ = Task.Run(async () =>
        {
            try { await RunStartupFlowAsync(); }
            catch (Exception ex)
            {
                _logger($"[WEBBRIDGE: ERROR] startup flow: {ex.Message}");
                SendStartup("ready");
            }
            finally { Interlocked.Exchange(ref _startupRunning, 0); }
        });
    }

    private void HandleStartupChoice(BridgeMessage msg, bool retry)
    {
        _startupErrorTcs?.TrySetResult(retry);
        SendResponse(msg.Id, msg.Name, new { ok = true });
    }

    private async Task RunStartupFlowAsync()
    {
        // Сразу после обновления повторно не проверяем — как StartupWindow (он пропускается при --updated-restart)
        if (!_configService.CurrentConfig.AutoUpdateEnabled && !IsUpdatedRestart)
        {
            SendStartup("ready");
            return;
        }

        if (IsUpdatedRestart || _launcherUpdateService == null || !_launcherUpdateService.IsInstalled)
        {
            SendStartup("checking");
            await Task.Delay(900);
            SendStartup(IsUpdatedRestart ? "updated" : "upToDate");
            await Task.Delay(IsUpdatedRestart ? 300 : 700);
            SendStartup("ready");
            return;
        }

        while (true)
        {
            SendStartup("checking");
            string? newVersion = null;
            bool ok = true;
            try
            {
                using var timeout = new CancellationTokenSource(StartupCheckTimeout);
                newVersion = await _launcherUpdateService.CheckForUpdatesAsync(timeout.Token);
            }
            catch (Exception ex)
            {
                _logger($"[LAUNCHER-UPDATE: ERROR] startup check: {ex.Message}");
                ok = false;
            }

            if (!ok)
            {
                _startupErrorTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                SendStartup("error", new { text = "Не удалось проверить обновления" });
                if (await _startupErrorTcs.Task) continue; // «Повторить»
                break;                                     // «Пропустить»
            }

            if (string.IsNullOrWhiteSpace(newVersion))
            {
                SendStartup("upToDate");
                await Task.Delay(900);
                break;
            }

            _logger($"[WEBBRIDGE] startup: downloading {newVersion}");
            SendStartup("downloading", new { percentage = 0.0, speed = "", version = DisplayVersion(newVersion) });
            var progress = new Progress<DownloadProgressReport>(p => SendStartup("downloading", new
            {
                percentage = p.Percentage,
                speed = p.SpeedMBs > 0 ? $"{p.SpeedMBs:F1} МБ/с" : "",
                version = DisplayVersion(newVersion)
            }));

            var result = await _launcherUpdateService.DownloadAndApplyAsync(progress);
            if (result.Status == LauncherUpdateStatus.UpdatedRestarting)
            {
                SendStartup("installing");
                await Task.Delay(1200);
                if (System.Windows.Application.Current is { } app)
                    app.Dispatcher.Invoke(app.Shutdown);
                return;
            }

            SendStartup("error", new { text = "Не удалось обновить", skipOnly = true });
            await Task.Delay(1500);
            break;
        }

        SendStartup("ready");
    }

    /// <summary>Список изменений текущей версии из version.json рядом с exe.</summary>
    private static string[] ReadChangelog()
    {
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "version.json");
            if (!File.Exists(path)) return Array.Empty<string>();
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("changelog", out var list) || list.ValueKind != JsonValueKind.Array)
                return Array.Empty<string>();
            return list.EnumerateArray().Select(e => e.GetString() ?? string.Empty)
                .Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private void HandleChangelogGet(BridgeMessage msg)
    {
        SendResponse(msg.Id, msg.Name, new
        {
            version = DisplayVersion(_launcherUpdateService?.CurrentVersion ?? "0.0.0"),
            changelog = ReadChangelog()
        });
    }
}
