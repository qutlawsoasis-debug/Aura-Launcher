using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AuraLauncher.Models;
using AuraLauncher.Services.Interfaces;
using Velopack;
using Velopack.Sources;

namespace AuraLauncher.Services.Implementations;

public class LauncherUpdateService : ILauncherUpdateService
{
    public const string DefaultLauncherRepoUrl = "https://github.com/qutlawsoasis-debug/Aura-Launcher";

    private readonly IConfigService _configService;
    private readonly IGameLaunchService _launchService;
    private readonly UpdateManager _updateManager;
    private readonly string? _overrideSource;
    private readonly bool? _isInstalledOverride;
    private UpdateInfo? _lastUpdateInfo;

    public bool IsInstalled => _isInstalledOverride ?? _updateManager.IsInstalled;

    public string CurrentVersion
    {
        get
        {
            if (_updateManager.IsInstalled && _updateManager.CurrentVersion != null)
            {
                return _updateManager.CurrentVersion.ToFullString();
            }

            var asmVersion = typeof(LauncherUpdateService).Assembly.GetName().Version;
            return asmVersion != null ? $"{asmVersion.Major}.{asmVersion.Minor}.{asmVersion.Build}" : "1.0.0";
        }
    }

    public LauncherUpdateService(
        IConfigService configService,
        IGameLaunchService launchService,
        string? overrideSource = null,
        UpdateManager? customUpdateManager = null,
        bool? isInstalledOverride = null)
    {
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _launchService = launchService ?? throw new ArgumentNullException(nameof(launchService));
        _isInstalledOverride = isInstalledOverride;

        _overrideSource = overrideSource ?? ResolveOverrideSourceFromArgs();

        if (customUpdateManager != null)
        {
            _updateManager = customUpdateManager;
        }
        else if (!string.IsNullOrWhiteSpace(_overrideSource))
        {
            _updateManager = new UpdateManager(_overrideSource.Trim());
        }
        else
        {
            var source = new GithubSource(DefaultLauncherRepoUrl, accessToken: null, prerelease: false);
            _updateManager = new UpdateManager(source);
        }
    }

    private static string? ResolveOverrideSourceFromArgs()
    {
        try
        {
            var args = Program.StartupArgs;
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i].Equals("--update-source", StringComparison.OrdinalIgnoreCase))
                {
                    return args[i + 1];
                }
            }
        }
        catch { }

        return null;
    }

    public async Task<string?> CheckForUpdatesAsync(CancellationToken ct = default)
    {
        if (!IsInstalled)
        {
            FabricGameLaunchService.LogLauncherEvent("[LAUNCHER-UPDATE] Лаунчер запущен не через Velopack (портативный/IDE режим). Пропуск проверки.");
            return null;
        }

        try
        {
            FabricGameLaunchService.LogLauncherEvent($"[LAUNCHER-UPDATE] Проверка наличия обновлений лаунчера (источник: {_overrideSource ?? DefaultLauncherRepoUrl})...");
            var updateInfo = await _updateManager.CheckForUpdatesAsync().WaitAsync(ct).ConfigureAwait(false);
            _lastUpdateInfo = updateInfo;

            if (updateInfo == null)
            {
                FabricGameLaunchService.LogLauncherEvent("[LAUNCHER-UPDATE] Установлена последняя версия лаунчера.");
                return null;
            }

            var targetVersion = updateInfo.TargetFullRelease?.Version?.ToFullString() ?? "новая версия";
            FabricGameLaunchService.LogLauncherEvent($"[LAUNCHER-UPDATE] Найдено обновление лаунчера: {targetVersion}");
            return targetVersion;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            FabricGameLaunchService.LogLauncherEvent($"[LAUNCHER-UPDATE: ERROR] Ошибка при проверке обновлений: {ex.Message}");
            return null;
        }
    }

    public async Task<LauncherUpdateResult> DownloadAndApplyAsync(
        IProgress<DownloadProgressReport>? progress = null, 
        CancellationToken ct = default)
    {
        if (!IsInstalled)
        {
            return new LauncherUpdateResult(LauncherUpdateStatus.NotInstalled, "Лаунчер запущен в портативном режиме");
        }

        if (_launchService.IsGameRunning)
        {
            FabricGameLaunchService.LogLauncherEvent("[LAUNCHER-UPDATE] Игра сейчас запущена. Обновление лаунчера отложено.");
            return new LauncherUpdateResult(LauncherUpdateStatus.Skipped, "Игра запущена, обновление отложено");
        }

        try
        {
            var updateInfo = _lastUpdateInfo;
            if (updateInfo == null)
            {
                updateInfo = await _updateManager.CheckForUpdatesAsync().WaitAsync(ct).ConfigureAwait(false);
                _lastUpdateInfo = updateInfo;
            }

            if (updateInfo == null)
            {
                return new LauncherUpdateResult(LauncherUpdateStatus.UpToDate, "Установлена последняя версия лаунчера");
            }

            var targetVersion = updateInfo.TargetFullRelease?.Version?.ToFullString() ?? "новая версия";

            progress?.Report(new DownloadProgressReport
            {
                Percentage = 0,
                StatusText = "Скачиваем обновление… 0%"
            });

            await _updateManager.DownloadUpdatesAsync(updateInfo, percent =>
            {
                progress?.Report(new DownloadProgressReport
                {
                    Percentage = percent,
                    StatusText = $"Скачиваем обновление… {percent}%"
                });
            }, ct).ConfigureAwait(false);

            if (_launchService.IsGameRunning)
            {
                FabricGameLaunchService.LogLauncherEvent("[LAUNCHER-UPDATE] Игра была запущена во время скачивания. Перезапуск отложен.");
                return new LauncherUpdateResult(LauncherUpdateStatus.Skipped, "Игра запущена, перезапуск отложен", targetVersion);
            }

            // Убеждаемся, что конфиг сохранён перед рестартом
            await _configService.SaveConfigAsync(_configService.CurrentConfig, ct).ConfigureAwait(false);

            FabricGameLaunchService.LogLauncherEvent($"[LAUNCHER-UPDATE] Применение обновления (silent=true) и перезапуск лаунчера (версия {targetVersion})...");
            _updateManager.WaitExitThenApplyUpdates(updateInfo, silent: true, restart: true, restartArgs: new[] { "--updated-restart" });

            return new LauncherUpdateResult(LauncherUpdateStatus.UpdatedRestarting, "Лаунчер обновляется и перезапускается...", targetVersion);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            FabricGameLaunchService.LogLauncherEvent($"[LAUNCHER-UPDATE: ERROR] Ошибка применения обновления: {ex.Message}");
            return new LauncherUpdateResult(LauncherUpdateStatus.Failed, $"Ошибка обновления лаунчера: {ex.Message}");
        }
    }

    public async Task<LauncherUpdateResult> CheckAndApplyAsync(
        IProgress<DownloadProgressReport>? progress = null, 
        CancellationToken ct = default)
    {
        var newVer = await CheckForUpdatesAsync(ct).ConfigureAwait(false);
        if (newVer == null)
        {
            return new LauncherUpdateResult(LauncherUpdateStatus.UpToDate, "Установлена последняя версия лаунчера");
        }

        return await DownloadAndApplyAsync(progress, ct).ConfigureAwait(false);
    }
}
