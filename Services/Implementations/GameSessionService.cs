using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using AuraLauncher.Core;
using AuraLauncher.Models;
using AuraLauncher.Services.Interfaces;

namespace AuraLauncher.Services.Implementations;

public class GameSessionService : IGameSessionService
{
    private readonly IGameLaunchService _launchService;
    private readonly IPackUpdateService _packUpdateService;
    private readonly IServerListSyncService _serverListSyncService;
    private readonly ISkinService _skinService;
    private readonly IConfigService _configService;
    private readonly IDiscordRpcService? _discordRpcService;
    private readonly IFriendService? _friendService;
    private readonly IPrewarmGameService? _prewarmService;

    private readonly object _stateLock = new();
    private GameSessionState _state = GameSessionState.Idle;
    private Process? _currentProcess;
    private CancellationTokenSource? _activeCts;
    private DateTime? _gameSessionStartTime;
    private string? _runningGameVersion;

    public GameSessionState State
    {
        get
        {
            lock (_stateLock) return _state;
        }
        private set
        {
            lock (_stateLock)
            {
                if (_state == value) return;
                _state = value;
            }
            StateChanged?.Invoke(this, value);
        }
    }

    public bool IsGameRunning => State == GameSessionState.Running && _currentProcess != null && !_currentProcess.HasExited;

    public Process? CurrentProcess => _currentProcess;

    public string? RunningGameVersion
    {
        get
        {
            lock (_stateLock) return _runningGameVersion;
        }
        private set
        {
            lock (_stateLock) _runningGameVersion = value;
        }
    }

    public event EventHandler<GameSessionState>? StateChanged;
    public event EventHandler<string>? LogReceived;
    public event EventHandler<GameExitedEventArgs>? GameExited;
    public event EventHandler<Process>? GameStarted;

    public GameSessionService(
        IGameLaunchService launchService,
        IPackUpdateService packUpdateService,
        IServerListSyncService serverListSyncService,
        ISkinService skinService,
        IConfigService configService,
        IDiscordRpcService? discordRpcService = null,
        IFriendService? friendService = null,
        IPrewarmGameService? prewarmService = null)
    {
        _launchService = launchService;
        _packUpdateService = packUpdateService;
        _serverListSyncService = serverListSyncService;
        _skinService = skinService;
        _configService = configService;
        _discordRpcService = discordRpcService;
        _friendService = friendService;
        _prewarmService = prewarmService;
    }

    public async Task<Process> LaunchAsync(
        LaunchOptions? options = null,
        IProgress<LaunchProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        lock (_stateLock)
        {
            if (_state != GameSessionState.Idle && _state != GameSessionState.Failed)
            {
                throw new InvalidOperationException("Game session is already preparing, launching, or running.");
            }
            _state = GameSessionState.Preparing;
        }
        StateChanged?.Invoke(this, GameSessionState.Preparing);

        _activeCts?.Dispose();
        _activeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var ct = _activeCts.Token;

        try
        {
            var config = _configService.CurrentConfig.Clone();
            var sessionVersion = GameVersionProfiles.NormalizeVersion(config.SelectedVersion);
            config.SelectedVersion = sessionVersion;

            // 0. Валидация никнейма
            var nickVal = NicknameValidator.Validate(config.Nickname);
            if (!nickVal.IsValid)
            {
                FabricGameLaunchService.LogLauncherEvent("[CONFIG] Обнаружен невалидный никнейм в конфигурации. Запуск заблокирован до исправления.");
                State = GameSessionState.Failed;
                throw new InvalidOperationException("Укажи никнейм в Настройках (3-16 символов, A-Z, 0-9, _)");
            }

            config.QuickPlayMultiplayer = options?.QuickPlayMultiplayer;
            config.QuickPlaySingleplayer = options?.QuickPlaySingleplayer;

            // 1. Проверяем наличие и применяем обновления сборки модов через IPackUpdateService
            progress?.Report(new LaunchProgress
            {
                Stage = "Проверка обновлений сборки...",
                Percentage = 0,
                DetailText = "Проверка обновлений сборки..."
            });

            var packProgress = new Progress<DownloadProgressReport>(report =>
            {
                progress?.Report(new LaunchProgress
                {
                    Stage = "Обновление сборки...",
                    Percentage = report.Percentage,
                    FormattedSpeed = report.FormattedSpeed,
                    FormattedRemainingTime = report.FormattedRemainingTime,
                    DetailText = report.TotalBytes > 0
                        ? $"Сборка: {report.Percentage:F0}% ({report.FormattedSpeed})"
                        : report.StatusText
                });
            });

            PackUpdateResult updateResult;
            try
            {
                updateResult = await _packUpdateService.CheckAndApplyAsync(packProgress, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                FabricGameLaunchService.LogLauncherEvent($"[UPDATE: ERROR] {ex.Message}");
                updateResult = new PackUpdateResult(PackUpdateStatus.Failed, $"Ошибка обновления: {ex.Message}");
            }

            if (updateResult.Status == PackUpdateStatus.Failed)
            {
                FabricGameLaunchService.LogLauncherEvent($"[UPDATE: FAILED] {updateResult.Message}");
                State = GameSessionState.Failed;
                throw new InvalidOperationException($"Сборка не обновилась: {updateResult.Message}");
            }

            ct.ThrowIfCancellationRequested();

            // 1.5. Синхронизация управляемых серверов в servers.dat (перед запуском игры)
            try
            {
                var targetGameDir = _launchService.ResolveInstanceDirectory(config.GameDir, config.SelectedVersion);
                await _serverListSyncService.SyncServersAsync(targetGameDir, updateResult.Servers, cancellationToken: ct);
            }
            catch (Exception ex)
            {
                FabricGameLaunchService.LogLauncherEvent($"[SERVER-SYNC] Ошибка синхронизации серверов: {ex.Message}");
            }

            // 1.6. Конфигурация CustomSkinLoader
            try
            {
                var targetGameDir = _launchService.ResolveInstanceDirectory(config.GameDir, config.SelectedVersion);
                _skinService.EnsureCustomSkinLoaderConfig(targetGameDir);
                var uploadResult = await _skinService.UploadSkinToLobbyApiAsync(
                    config.SkinPath,
                    config.Nickname,
                    config.SkinModel,
                    config.SkinOwnerToken,
                    config.LobbyApiBaseUrl,
                    ct);

                if (uploadResult.Success && !string.IsNullOrWhiteSpace(uploadResult.OwnerToken) &&
                    uploadResult.OwnerToken != config.SkinOwnerToken)
                {
                    config.SkinOwnerToken = uploadResult.OwnerToken;
                    await _configService.SaveConfigAsync(config, ct);
                }

                await _skinService.SyncSkinToGameAsync(config.SkinPath, config.Nickname, targetGameDir, ct);
                await _skinService.SyncCapeToGameAsync(config.CapePath, config.Nickname, targetGameDir, ct);
            }
            catch (Exception ex)
            {
                FabricGameLaunchService.LogLauncherEvent($"[CSL-SYNC: ERROR] {ex.Message}");
            }

            // 2. Автоустановка окружения и запуск игры
            State = GameSessionState.Launching;
            progress?.Report(new LaunchProgress
            {
                Stage = "Подготовка компонентов игры...",
                Percentage = 0,
                DetailText = "Подготовка компонентов игры..."
            });

            var installProgress = new Progress<InstallProgressReport>(report =>
            {
                progress?.Report(new LaunchProgress
                {
                    Stage = $"Установка: {report.Stage}",
                    Percentage = report.Percentage,
                    FormattedSpeed = report.FormattedSpeed,
                    DetailText = string.IsNullOrWhiteSpace(report.FormattedSpeed)
                        ? report.DetailText
                        : $"{report.DetailText} • {report.FormattedSpeed}"
                });
            });

            if (_prewarmService != null && _prewarmService.IsPrewarmedReady)
            {
                progress?.Report(new LaunchProgress
                {
                    Stage = "Мгновенный запуск...",
                    Percentage = 90,
                    DetailText = "Подключение к предзапущенной игре..."
                });

                var fastProcess = await _prewarmService.ActivateAsync(
                    quickPlayMultiplayer: options?.QuickPlayMultiplayer,
                    quickPlaySingleplayer: options?.QuickPlaySingleplayer);

                if (fastProcess != null && !fastProcess.HasExited)
                {
                    FabricGameLaunchService.LogLauncherEvent($"[FAST-START] Мгновенный запуск активирован! PID={fastProcess.Id}");
                    _currentProcess = fastProcess;
                    _gameSessionStartTime = DateTime.UtcNow;
                    RunningGameVersion = sessionVersion;
                    _discordRpcService?.SetPlayingGame($"Aura Pack ({sessionVersion})", _gameSessionStartTime, sessionVersion);
                    State = GameSessionState.Running;

                    var targetGameDir = _launchService.ResolveInstanceDirectory(config.GameDir, config.SelectedVersion);
                    var gameLogPath = System.IO.Path.Combine(targetGameDir, "logs", "launcher-game.log");

                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await fastProcess.WaitForExitAsync();
                        }
                        catch { }

                        int exitCode = 0;
                        try { exitCode = fastProcess.ExitCode; } catch { }
                        HandleGameExited(exitCode, gameLogPath);
                    });

                    try
                    {
                        if (_friendService != null)
                        {
                            _friendService.IsGameRunning = true;
                            _ = _friendService.SyncNowAsync();
                        }
                        GameStarted?.Invoke(this, fastProcess);
                    }
                    catch { }

                    progress?.Report(new LaunchProgress
                    {
                        Stage = "В игре",
                        Percentage = 100,
                        DetailText = "В игре"
                    });
                    return fastProcess;
                }
            }

            _gameSessionStartTime = DateTime.UtcNow;
            RunningGameVersion = sessionVersion;
            _discordRpcService?.SetPlayingGame($"Aura Pack ({sessionVersion})", _gameSessionStartTime, sessionVersion);

            var process = await _launchService.LaunchGameAsync(
                config,
                line =>
                {
                    LogReceived?.Invoke(this, line);
                },
                onGameExited: (exitCode, logPath) =>
                {
                    HandleGameExited(exitCode, logPath);
                },
                installProgress: installProgress,
                cancellationToken: ct);

            _currentProcess = process;
            State = GameSessionState.Running;

            try
            {
                if (_friendService != null)
                {
                    _friendService.IsGameRunning = true;
                    _ = _friendService.SyncNowAsync();
                }
                _discordRpcService?.SetPlayingGame(startTime: DateTime.UtcNow, gameVersion: sessionVersion);
                GameStarted?.Invoke(this, process);
            }
            catch { }

            return process;
        }
        catch (OperationCanceledException)
        {
            RunningGameVersion = null;
            State = GameSessionState.Idle;
            throw;
        }
        catch (Exception ex)
        {
            RunningGameVersion = null;
            State = GameSessionState.Failed;

            // Сбой этапа подготовки/скачивания тоже сохраняем до следующего сбоя
            try
            {
                var cfgNow = _configService.CurrentConfig;
                string prepLogsDir = System.IO.Path.Combine(
                    _launchService.ResolveInstanceDirectory(cfgNow.GameDir, cfgNow.SelectedVersion), "logs");
                LaunchFailureStore.Save(prepLogsDir, new
                {
                    savedAtUtc = DateTime.UtcNow,
                    version = GameVersionProfiles.NormalizeVersion(cfgNow.SelectedVersion),
                    stage = "prepare",
                    exitCode = (int?)null,
                    reason = ex.Message
                });
            }
            catch { }

            throw;
        }
        finally
        {
            if (_configService?.CurrentConfig != null)
            {
                _configService.CurrentConfig.QuickPlayMultiplayer = null;
                _configService.CurrentConfig.QuickPlaySingleplayer = null;
            }
        }
    }

    private void HandleGameExited(int exitCode, string logPath)
    {
        _currentProcess = null;
        string? versionAtExit = RunningGameVersion;
        DateTime? startedAt = _gameSessionStartTime;
        RunningGameVersion = null;
        State = exitCode == 0 ? GameSessionState.Idle : GameSessionState.Failed;

        string? failureReason = null;
        if (exitCode != 0)
        {
            var uptime = startedAt.HasValue ? DateTime.UtcNow - startedAt.Value : TimeSpan.Zero;

            try
            {
                // Каталог инстанса выводим из фактического пути лога этой сессии
                string? gameDir = null;
                if (!string.IsNullOrWhiteSpace(logPath))
                {
                    string? logsDirOfSession = System.IO.Path.GetDirectoryName(logPath);
                    if (!string.IsNullOrEmpty(logsDirOfSession))
                    {
                        gameDir = System.IO.Directory.GetParent(logsDirOfSession)?.FullName;
                    }
                }
                gameDir ??= _launchService.ResolveInstanceDirectory(_configService.CurrentConfig.GameDir);

                // Этап сбоя: ранний выход JVM (игра не успела начаться) или краш в работе
                bool earlyExit = uptime.TotalSeconds <= 30;
                var analysis = GameCrashAnalyzer.AnalyzeCrash(gameDir, exitCode, logPath);
                string fallbackTitle = $"Игра завершилась с ошибкой (код {exitCode})";
                bool matchedSpecific = !string.Equals(analysis.Title, fallbackTitle, StringComparison.Ordinal);
                string? logName = string.IsNullOrWhiteSpace(logPath) ? null : System.IO.Path.GetFileName(logPath);
                long uptimeSeconds = (long)Math.Max(uptime.TotalSeconds, 0);

                failureReason = earlyExit
                    ? matchedSpecific
                        ? $"Игра не запустилась: {analysis.Title}. Процесс Java завершился через {uptimeSeconds} с после старта (код {exitCode}). {analysis.UserAdvice}"
                        : $"Игра не запустилась: процесс Java завершился через {uptimeSeconds} с после старта (код {exitCode}). Полный вывод Java сохранён в logs/{logName ?? "launcher-game.log"}. Перезапустите игру; при повторном сбое приложите этот файл к отчёту."
                    : $"{analysis.Title}. Код {exitCode}, время работы {(long)uptime.TotalMinutes} мин. {analysis.UserAdvice}";

                // Причина сбоя сохраняется до следующего сбоя: успешный запуск этот файл не трогает
                string? logsDirForStore = string.IsNullOrWhiteSpace(logPath)
                    ? System.IO.Path.Combine(_launchService.ResolveInstanceDirectory(_configService.CurrentConfig.GameDir), "logs")
                    : System.IO.Path.GetDirectoryName(logPath);
                LaunchFailureStore.Save(logsDirForStore, new
                {
                    savedAtUtc = DateTime.UtcNow,
                    version = versionAtExit,
                    stage = earlyExit ? "java-early-exit" : "game-crash",
                    exitCode,
                    uptimeSeconds,
                    reason = failureReason,
                    logFile = logName
                });
            }
            catch
            {
                failureReason = $"Игра завершилась с ошибкой (код {exitCode})";
            }
        }

        if (startedAt.HasValue)
        {
            var elapsed = DateTime.UtcNow - startedAt.Value;
            long addSec = (long)elapsed.TotalSeconds;
            if (addSec > 5)
            {
                _ = _configService.UpdateConfigAsync(c =>
                {
                    c.TotalPlayTimeSeconds += addSec;
                    c.TotalGameLaunches += 1;
                    c.LastPlayedUtc = DateTime.UtcNow;
                });
            }
            _gameSessionStartTime = null;
        }

        try
        {
            if (_friendService != null)
            {
                _friendService.IsGameRunning = false;
                _ = _friendService.SyncNowAsync();
            }
            _discordRpcService?.SetInLauncher();
        }
        catch { }

        GameExited?.Invoke(this, new GameExitedEventArgs(exitCode, logPath, failureReason));
    }

    public async Task<bool> StopAsync(int timeoutMs = 5000)
    {
        _activeCts?.Cancel();
        bool stopped = await _launchService.StopGameProcessAsync(timeoutMs);
        if (stopped)
        {
            _currentProcess = null;
            State = GameSessionState.Idle;
        }
        return stopped;
    }
}
