using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using AuraLauncher.Core;
using AuraLauncher.Models;
using AuraLauncher.Services.Interfaces;
using AuraLauncher.Services.Implementations;

namespace AuraLauncher.ViewModels;

/// <summary>
/// Главная ViewModel приложения, координирующая процесс обновления, автоустановки окружения (CmlLib.Core),
/// запуска игры и навигации.
/// Не хранит собственных копий RAM и ника: источником правды является IConfigService.
/// </summary>
public class MainViewModel : ObservableObject
{
    private readonly IConfigService _configService;
    private readonly ILauncherUpdateService _launcherUpdateService;
    private readonly IPackUpdateService _packUpdateService;
    private readonly IGameLaunchService _launchService;
    private readonly ISkinService _skinService;
    private readonly IServerListSyncService _serverListSyncService;

    // Свойства состояния UI
    private object _currentView = null!;
    private string _currentTabName = "Overview";
    private LauncherState _state = LauncherState.Idle;
    private string _stateTitle = "Готов к игре";
    private string _statusText = "Готов к запуску";
    private double _progressPercentage;
    private string _speedText = string.Empty;
    private string _remainingTimeText = string.Empty;
    private bool _isProgressVisible;
    private bool _isBusy;
    private bool _isGameRunning;
    private bool _isLaunching;
    private string _latestLogLine = string.Empty;
    private ImageSource _playerAvatar = null!;
    private Process? _gameProcess;
    private CancellationTokenSource? _launchCts;
    private System.Windows.Threading.DispatcherTimer? _backgroundUpdateTimer;

    // Свойства баннера/тоста обновления
    private bool _isUpdateBannerVisible;
    private string _updateBannerTitle = string.Empty;
    private string _updateBannerMessage = string.Empty;
    private string _updateBannerButtonText = string.Empty;
    private bool _isLauncherUpdatePending;

    public OverviewViewModel OverviewVM { get; }
    public SettingsViewModel SettingsVM { get; }
    public WardrobeViewModel WardrobeVM { get; }

    public object CurrentView
    {
        get => _currentView;
        set => SetProperty(ref _currentView, value);
    }

    public string CurrentTabName
    {
        get => _currentTabName;
        set
        {
            if (SetProperty(ref _currentTabName, value))
            {
                OnPropertyChanged(nameof(IsOverviewActive));
                OnPropertyChanged(nameof(IsWardrobeActive));
                OnPropertyChanged(nameof(IsSettingsActive));
            }
        }
    }

    public bool IsOverviewActive
    {
        get => CurrentTabName.Equals("Overview", StringComparison.OrdinalIgnoreCase);
        set
        {
            if (value) SwitchTab("Overview");
        }
    }

    public bool IsWardrobeActive
    {
        get => CurrentTabName.Equals("Wardrobe", StringComparison.OrdinalIgnoreCase);
        set
        {
            if (value) SwitchTab("Wardrobe");
        }
    }

    public bool IsSettingsActive
    {
        get => CurrentTabName.Equals("Settings", StringComparison.OrdinalIgnoreCase);
        set
        {
            if (value) SwitchTab("Settings");
        }
    }

    public void SwitchTab(string viewName)
    {
        if (string.IsNullOrWhiteSpace(viewName)) return;
        CurrentTabName = viewName;
        if (viewName.Equals("Settings", StringComparison.OrdinalIgnoreCase))
        {
            CurrentView = SettingsVM;
        }
        else if (viewName.Equals("Wardrobe", StringComparison.OrdinalIgnoreCase))
        {
            CurrentView = WardrobeVM;
        }
        else
        {
            OverviewVM.RefreshStats();
            CurrentView = OverviewVM;
        }
    }

    public LauncherState State
    {
        get => _state;
        set => SetProperty(ref _state, value);
    }

    public string StateTitle
    {
        get => _stateTitle;
        set => SetProperty(ref _stateTitle, value);
    }

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public double ProgressPercentage
    {
        get => _progressPercentage;
        set => SetProperty(ref _progressPercentage, value);
    }

    public string SpeedText
    {
        get => _speedText;
        set => SetProperty(ref _speedText, value);
    }

    public string RemainingTimeText
    {
        get => _remainingTimeText;
        set => SetProperty(ref _remainingTimeText, value);
    }

    public bool IsProgressVisible
    {
        get => _isProgressVisible;
        set => SetProperty(ref _isProgressVisible, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(LaunchButtonText));
                LaunchOrCancelCommand.RaiseCanExecuteChanged();
                LaunchGameCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsGameRunning
    {
        get => _isGameRunning;
        set
        {
            if (SetProperty(ref _isGameRunning, value))
            {
                OnPropertyChanged(nameof(LaunchButtonText));
                LaunchOrCancelCommand.RaiseCanExecuteChanged();
                LaunchGameCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public Process? CurrentGameProcess => _gameProcess ?? _launchService.CurrentGameProcess;

    public string LaunchButtonText
    {
        get
        {
            if (IsGameRunning) return "ИГРА ЗАПУЩЕНА";
            if (IsBusy) return "ОТМЕНА";
            var gameDir = _launchService.ResolveMinecraftDirectory(_configService.CurrentConfig.GameDir);
            return _launchService.CheckEnvironmentInstalled(gameDir) ? "ИГРАТЬ" : "УСТАНОВИТЬ";
        }
    }

    public string LatestLogLine
    {
        get => _latestLogLine;
        set => SetProperty(ref _latestLogLine, value);
    }

    public ImageSource PlayerAvatar
    {
        get => _playerAvatar;
        set => SetProperty(ref _playerAvatar, value);
    }

    public string PlayerNickname => _configService.CurrentConfig.Nickname;

    public string RamFormatted => $"RAM: {Math.Round(_configService.CurrentConfig.RamMb / 1024.0, 1)} ГБ";

    public ObservableCollection<string> GameLogs { get; } = new();

    // Свойства баннера обновлений
    public bool IsUpdateBannerVisible
    {
        get => _isUpdateBannerVisible;
        set => SetProperty(ref _isUpdateBannerVisible, value);
    }

    public string UpdateBannerTitle
    {
        get => _updateBannerTitle;
        set => SetProperty(ref _updateBannerTitle, value);
    }

    public string UpdateBannerMessage
    {
        get => _updateBannerMessage;
        set => SetProperty(ref _updateBannerMessage, value);
    }

    public string UpdateBannerButtonText
    {
        get => _updateBannerButtonText;
        set => SetProperty(ref _updateBannerButtonText, value);
    }

    public string LauncherVersionText => $"v{_launcherUpdateService.CurrentVersion}";

    // Команды
    public RelayCommand LaunchOrCancelCommand { get; }
    public AsyncRelayCommand LaunchGameCommand { get; }
    public RelayCommand CancelCommand { get; }
    public AsyncRelayCommand CheckUpdatesCommand { get; }
    public RelayCommand NavigateCommand { get; }
    public RelayCommand CloseWindowCommand { get; }
    public RelayCommand MinimizeWindowCommand { get; }
    public AsyncRelayCommand ApplyBannerUpdateCommand { get; }
    public RelayCommand DismissBannerCommand { get; }

    public MainViewModel(
        IConfigService configService,
        ILauncherUpdateService launcherUpdateService,
        IPackUpdateService packUpdateService,
        IGameLaunchService launchService,
        ISkinService skinService,
        OverviewViewModel overviewViewModel,
        SettingsViewModel settingsViewModel,
        WardrobeViewModel wardrobeViewModel,
        IServerListSyncService? serverListSyncService = null)
    {
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _launcherUpdateService = launcherUpdateService ?? throw new ArgumentNullException(nameof(launcherUpdateService));
        _packUpdateService = packUpdateService ?? throw new ArgumentNullException(nameof(packUpdateService));
        _launchService = launchService ?? throw new ArgumentNullException(nameof(launchService));
        _skinService = skinService ?? throw new ArgumentNullException(nameof(skinService));
        _serverListSyncService = serverListSyncService ?? new ServerListSyncService(launchService);
        OverviewVM = overviewViewModel ?? throw new ArgumentNullException(nameof(overviewViewModel));
        SettingsVM = settingsViewModel ?? throw new ArgumentNullException(nameof(settingsViewModel));
        WardrobeVM = wardrobeViewModel ?? throw new ArgumentNullException(nameof(wardrobeViewModel));

        _currentView = OverviewVM;

        LaunchGameCommand = new AsyncRelayCommand(LaunchGameAsync, () => !IsBusy && !IsGameRunning && !_isLaunching && NicknameValidator.Validate(_configService.CurrentConfig.Nickname).IsValid);
        CancelCommand = new RelayCommand(_ => CancelLaunch());
        LaunchOrCancelCommand = new RelayCommand(_ =>
        {
            if (IsGameRunning) return;
            if (IsBusy)
            {
                CancelLaunch();
            }
            else
            {
                _ = LaunchGameAsync();
            }
        }, _ => !IsGameRunning && (IsBusy || NicknameValidator.Validate(_configService.CurrentConfig.Nickname).IsValid));

        CheckUpdatesCommand = new AsyncRelayCommand(() => CheckUpdatesAsync(isStartup: false), () => !IsBusy && !IsGameRunning);
        CloseWindowCommand = new RelayCommand(_ => System.Windows.Application.Current.Shutdown());
        MinimizeWindowCommand = new RelayCommand(_ =>
        {
            if (System.Windows.Application.Current.MainWindow != null)
            {
                System.Windows.Application.Current.MainWindow.WindowState = System.Windows.WindowState.Minimized;
            }
        });

        NavigateCommand = new RelayCommand(param =>
        {
            if (param is string viewName)
            {
                SwitchTab(viewName);
            }
        });

        DismissBannerCommand = new RelayCommand(_ =>
        {
            IsUpdateBannerVisible = false;
        });

        ApplyBannerUpdateCommand = new AsyncRelayCommand(async () =>
        {
            IsUpdateBannerVisible = false;
            if (_isLauncherUpdatePending)
            {
                // Для лаунчера вызываем проверку/применение обновления лаунчера
                await CheckUpdatesAsync(isStartup: false);
            }
            else
            {
                // Для сборки запускаем скачивание новых модов
                await CheckUpdatesAsync(isStartup: false);
            }
        }, () => !IsBusy && !IsGameRunning);

        UpdateAvatar();

        _launchService.GameExited += (s, exitCode) =>
        {
            System.Windows.Application.Current?.Dispatcher?.InvokeAsync(() =>
            {
                IsGameRunning = false;
                OverviewVM.RefreshStats();
                if (exitCode != 0)
                {
                    var logsDir = Path.Combine(_launchService.ResolveMinecraftDirectory(_configService.CurrentConfig.GameDir), "logs");
                    var gameLogPath = Path.Combine(logsDir, "launcher-game.log");
                    SetLauncherState(LauncherState.Error, $"Игра завершилась с ошибкой (код {exitCode}). Лог: {gameLogPath}");
                }
                else
                {
                    UpdateIdleState();
                }
            });
        };

        _configService.ConfigChanged += (s, cfg) =>
        {
            OnPropertyChanged(nameof(PlayerNickname));
            OnPropertyChanged(nameof(RamFormatted));
            UpdateAvatar();
            OverviewVM.RefreshStats();
            UpdateIdleState();
        };
    }

    public async Task InitializeAsync()
    {
        CurrentView = OverviewVM;
        await _configService.LoadConfigAsync();
        OverviewVM.RefreshStats();
        UpdateAvatar();

        var initialNickValidation = NicknameValidator.Validate(_configService.CurrentConfig.Nickname);
        if (!initialNickValidation.IsValid)
        {
            FabricGameLaunchService.LogLauncherEvent("[CONFIG] Обнаружен невалидный никнейм в конфигурации. Запуск заблокирован до исправления.");
        }

        // Определение уже запущенной игры при старте лаунчера
        var gameDir = _launchService.ResolveMinecraftDirectory(_configService.CurrentConfig.GameDir);
        var running = _launchService.FindRunningGameProcess(gameDir);
        if (running != null)
        {
            _gameProcess = running;
            IsGameRunning = true;
            UpdateIdleState();
        }
        else
        {
            UpdateIdleState();
            await CheckUpdatesAsync(isStartup: true);
        }

        StartBackgroundUpdatePolling();
    }

    public void UpdateIdleState()
    {
        if (IsGameRunning)
        {
            State = LauncherState.Idle;
            StateTitle = "Игра уже запущена";
            StatusText = "Закрой окно Minecraft, чтобы запустить снова";
        }
        else
        {
            var nickVal = NicknameValidator.Validate(_configService.CurrentConfig.Nickname);
            if (!nickVal.IsValid)
            {
                State = LauncherState.Idle;
                StateTitle = "Некорректный никнейм";
                StatusText = "Укажи никнейм в Настройках (3-16 символов, A-Z, 0-9, _)";
            }
            else
            {
                var gameDir = _launchService.ResolveMinecraftDirectory(_configService.CurrentConfig.GameDir);
                bool isInstalled = _launchService.CheckEnvironmentInstalled(gameDir);
                State = LauncherState.Idle;
                if (isInstalled)
                {
                    StateTitle = "Готов к игре";
                    StatusText = "Готов к запуску";
                }
                else
                {
                    StateTitle = "Готов к установке";
                    StatusText = "Готов к установке";
                }
            }
        }
        OnPropertyChanged(nameof(LaunchButtonText));
        LaunchOrCancelCommand.RaiseCanExecuteChanged();
        LaunchGameCommand.RaiseCanExecuteChanged();
    }

    public void CancelLaunch()
    {
        if (IsBusy && _launchCts != null && !_launchCts.IsCancellationRequested)
        {
            _launchCts.Cancel();
            SetLauncherState(LauncherState.Idle, "Отмена операции...");
        }
    }

    public void SetLauncherState(LauncherState state, string? customMessage = null)
    {
        State = state;
        switch (state)
        {
            case LauncherState.Idle:
                StateTitle = "Готов к игре";
                StatusText = customMessage ?? "Готов к запуску";
                break;
            case LauncherState.Checking:
                StateTitle = "Проверка обновлений...";
                StatusText = customMessage ?? "Проверка обновлений сборки...";
                break;
            case LauncherState.Downloading:
                StateTitle = "Загрузка...";
                StatusText = customMessage ?? $"Загрузка: {ProgressPercentage:F0}% ({SpeedText})";
                break;
            case LauncherState.Ready:
                StateTitle = "Всё готово";
                StatusText = customMessage ?? "Клиент готов к запуску";
                break;
            case LauncherState.Error:
                StateTitle = "Внимание";
                StatusText = customMessage ?? "Произошла ошибка";
                break;
        }
    }

    private void UpdateAvatar()
    {
        try
        {
            PlayerAvatar = _skinService.ExtractHeadAvatar(_configService.CurrentConfig.SkinPath);
        }
        catch
        {
            // fallback
        }
    }

    private async Task LaunchGameAsync()
    {
        // Защита от двойного клика (флаг до любого await)
        if (_isLaunching || _isBusy || IsGameRunning)
        {
            return;
        }

        var nickVal = NicknameValidator.Validate(_configService.CurrentConfig.Nickname);
        if (!nickVal.IsValid)
        {
            SetLauncherState(LauncherState.Idle, "Укажи никнейм в Настройках (3-16 символов, A-Z, 0-9, _)");
            return;
        }

        _isLaunching = true;
        IsBusy = true;
        LaunchOrCancelCommand.RaiseCanExecuteChanged();
        LaunchGameCommand.RaiseCanExecuteChanged();

        try
        {
            _launchCts?.Dispose();
            _launchCts = new CancellationTokenSource();
            var ct = _launchCts.Token;

            var config = _configService.CurrentConfig;

            // 1. Проверяем наличие и применяем обновления сборки модов через IPackUpdateService
            SetLauncherState(LauncherState.Checking, "Проверка обновлений сборки...");
            var packProgress = new Progress<DownloadProgressReport>(report =>
            {
                IsProgressVisible = true;
                SetLauncherState(LauncherState.Downloading, "Обновление сборки...");
                ProgressPercentage = report.Percentage;
                SpeedText = report.FormattedSpeed;
                RemainingTimeText = report.FormattedRemainingTime;
                StatusText = report.TotalBytes > 0
                    ? $"Сборка: {report.Percentage:F0}% ({report.FormattedSpeed})"
                    : report.StatusText;
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
                SetLauncherState(LauncherState.Idle, "Сборка не обновилась, повтори");
                IsProgressVisible = false;
                return;
            }

            IsProgressVisible = false;
            OverviewVM.RefreshStats();

            ct.ThrowIfCancellationRequested();

            // 1.5. Синхронизация управляемых серверов в servers.dat (перед запуском игры)
            try
            {
                var targetGameDir = _launchService.ResolveMinecraftDirectory(config.GameDir);
                await _serverListSyncService.SyncServersAsync(targetGameDir, updateResult.Servers, cancellationToken: ct);
            }
            catch (Exception ex)
            {
                FabricGameLaunchService.LogLauncherEvent($"[SERVER-SYNC] Ошибка синхронизации серверов: {ex.Message}");
            }

            // 2. Автоустановка окружения (CmlLib.Core) и запуск игры
            SetLauncherState(LauncherState.Downloading, "Подготовка компонентов игры...");
            IsProgressVisible = true;

            var installProgress = new Progress<InstallProgressReport>(report =>
            {
                IsProgressVisible = true;
                ProgressPercentage = report.Percentage;
                SpeedText = report.FormattedSpeed;
                StateTitle = $"Установка: {report.Stage}";
                StatusText = string.IsNullOrWhiteSpace(report.FormattedSpeed)
                    ? report.DetailText
                    : $"{report.DetailText} • {report.FormattedSpeed}";
            });

            var process = await _launchService.LaunchGameAsync(
                config,
                line =>
                {
                    System.Windows.Application.Current?.Dispatcher?.InvokeAsync(() =>
                    {
                        LatestLogLine = line;
                        GameLogs.Add(line);
                        if (GameLogs.Count > 300)
                        {
                            GameLogs.RemoveAt(0);
                        }
                    });
                },
                onGameExited: (exitCode, logPath) =>
                {
                    System.Windows.Application.Current?.Dispatcher?.InvokeAsync(() =>
                    {
                        IsGameRunning = false;
                        OverviewVM.RefreshStats();
                        if (exitCode != 0)
                        {
                            SetLauncherState(LauncherState.Error, $"Игра завершилась с ошибкой (код {exitCode}). Лог: {logPath}");
                        }
                        else
                        {
                            UpdateIdleState();
                        }
                    });
                },
                installProgress: installProgress,
                cancellationToken: ct);

            _gameProcess = process;
            IsGameRunning = true;
            IsProgressVisible = false;
            IsBusy = false;
            _isLaunching = false;
            OverviewVM.RefreshStats();
            UpdateIdleState();
        }
        catch (OperationCanceledException)
        {
            SetLauncherState(LauncherState.Idle, "Операция отменена");
            LatestLogLine = "[INFO] Операция отменена пользователем.";
            UpdateIdleState();
        }
        catch (Exception ex)
        {
            SetLauncherState(LauncherState.Error, ex.Message);
            LatestLogLine = $"[ERROR] {ex.Message}";
        }
        finally
        {
            _isLaunching = false;
            IsProgressVisible = false;
            if (!IsGameRunning)
            {
                IsBusy = false;
            }
            OnPropertyChanged(nameof(LaunchButtonText));
            LaunchOrCancelCommand.RaiseCanExecuteChanged();
            LaunchGameCommand.RaiseCanExecuteChanged();
        }
    }

    private async Task CheckUpdatesAsync(bool isStartup = false)
    {
        // 1. Сначала обновление лаунчера через Velopack
        try
        {
            if (!isStartup)
            {
                SetLauncherState(LauncherState.Checking, "Проверка обновлений лаунчера...");
            }

            var launcherProgress = new Progress<DownloadProgressReport>(report =>
            {
                IsProgressVisible = true;
                SetLauncherState(LauncherState.Downloading, report.StatusText);
                ProgressPercentage = report.Percentage;
                SpeedText = string.Empty;
                RemainingTimeText = string.Empty;
                StatusText = report.StatusText;
            });

            var launcherResult = await _launcherUpdateService.CheckAndApplyAsync(launcherProgress, CancellationToken.None);
            if (launcherResult.Status == LauncherUpdateStatus.UpdatedRestarting)
            {
                IsProgressVisible = false;
                SetLauncherState(LauncherState.Ready, launcherResult.Message);
                return;
            }
        }
        catch (Exception ex)
        {
            FabricGameLaunchService.LogLauncherEvent($"[LAUNCHER-UPDATE: UNHANDLED] {ex.Message}");
        }
        finally
        {
            IsProgressVisible = false;
        }

        // 2. Затем обновление сборки модов
        var progress = new Progress<DownloadProgressReport>(report =>
        {
            IsProgressVisible = true;
            SetLauncherState(LauncherState.Downloading, "Обновление сборки...");
            ProgressPercentage = report.Percentage;
            SpeedText = report.FormattedSpeed;
            RemainingTimeText = report.FormattedRemainingTime;
            StatusText = report.TotalBytes > 0
                ? $"Сборка: {report.Percentage:F0}% ({report.FormattedSpeed})"
                : report.StatusText;
        });

        try
        {
            if (!isStartup)
            {
                SetLauncherState(LauncherState.Checking, "Проверка обновлений сборки...");
            }

            var result = await _packUpdateService.CheckAndApplyAsync(progress, CancellationToken.None);

            IsProgressVisible = false;
            OverviewVM.RefreshStats();

            if (isStartup)
            {
                if (result.Status == PackUpdateStatus.OfflineContinue)
                {
                    UpdateIdleState();
                }
                else if (result.Status == PackUpdateStatus.Failed)
                {
                    FabricGameLaunchService.LogLauncherEvent($"[UPDATE: STARTUP ERROR] {result.Message}");
                    UpdateIdleState();
                }
                else
                {
                    UpdateIdleState();
                    if (result.Status == PackUpdateStatus.Updated)
                    {
                        StatusText = result.Message;
                    }
                }
            }
            else
            {
                if (result.Status == PackUpdateStatus.Failed)
                {
                    SetLauncherState(LauncherState.Error, result.Message);
                }
                else
                {
                    SetLauncherState(LauncherState.Ready, result.Message);
                }
            }
        }
        catch (HttpRequestException ex)
        {
            FabricGameLaunchService.LogLauncherEvent($"[UPDATE: HTTP ERROR] {ex.Message}");
            IsProgressVisible = false;
            if (!isStartup)
            {
                SetLauncherState(LauncherState.Error, ex.Message);
            }
            else
            {
                UpdateIdleState();
            }
        }
        catch (Exception ex)
        {
            FabricGameLaunchService.LogLauncherEvent($"[UPDATE: ERROR] {ex.Message}");
            IsProgressVisible = false;
            if (!isStartup)
            {
                SetLauncherState(LauncherState.Error, $"Ошибка: {ex.Message}");
            }
            else
            {
                UpdateIdleState();
            }
        }
        finally
        {
            IsProgressVisible = false;
        }
    }

    private void StartBackgroundUpdatePolling()
    {
        _backgroundUpdateTimer?.Stop();
        _backgroundUpdateTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(15)
        };
        _backgroundUpdateTimer.Tick += async (s, e) =>
        {
            await CheckBackgroundUpdatesAsync();
        };
        _backgroundUpdateTimer.Start();
    }

    public async Task CheckBackgroundUpdatesAsync()
    {
        if (IsBusy || IsGameRunning || _isLaunching)
        {
            return;
        }

        try
        {
            // 1. Проверяем обновление лаунчера
            if (_launcherUpdateService.IsInstalled)
            {
                var launcherResult = await _launcherUpdateService.CheckAndApplyAsync(null, CancellationToken.None);
                if (launcherResult.Status == LauncherUpdateStatus.UpdatedRestarting)
                {
                    // Обновление скачано и готово к перезапуску
                    _isLauncherUpdatePending = true;
                    UpdateBannerTitle = "Обновление лаунчера";
                    UpdateBannerMessage = string.IsNullOrWhiteSpace(launcherResult.NewVersion)
                        ? "Доступна новая версия лаунчера. Нажмите для перезапуска."
                        : $"Доступна версия {launcherResult.NewVersion}. Нажмите для перезапуска.";
                    UpdateBannerButtonText = "ПЕРЕЗАПУСТИТЬ";
                    IsUpdateBannerVisible = true;
                    return;
                }
            }

            // 2. Проверяем обновление сборки (манифеста)
            var config = _configService.CurrentConfig;
            var gameDir = config.GameDir;
            if (!string.IsNullOrWhiteSpace(gameDir))
            {
                var currentState = PackState.LoadValidState(gameDir);
                if (currentState != null)
                {
                    var repo = string.IsNullOrWhiteSpace(config.PackRepo) ? PackUpdateService.DefaultPackRepo : config.PackRepo.Trim();
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
                    using var client = new HttpClient();
                    client.DefaultRequestHeaders.UserAgent.Add(new System.Net.Http.Headers.ProductInfoHeaderValue("AuraLauncher", "1.0"));
                    string manifestUrl = $"https://raw.githubusercontent.com/{repo}/{PackUpdateService.DefaultBranch}/manifest.json?t={DateTime.UtcNow.Ticks}";
                    var resp = await client.GetAsync(manifestUrl, cts.Token);
                    if (resp.IsSuccessStatusCode)
                    {
                        var json = await resp.Content.ReadAsStringAsync(cts.Token);
                        using var doc = System.Text.Json.JsonDocument.Parse(json);
                        if (doc.RootElement.TryGetProperty("packVersion", out var pvElem))
                        {
                            var remoteVersion = pvElem.GetString();
                            if (!string.IsNullOrWhiteSpace(remoteVersion) && !string.Equals(remoteVersion, currentState.PackVersion, StringComparison.Ordinal))
                            {
                                _isLauncherUpdatePending = false;
                                UpdateBannerTitle = "Обновление сборки";
                                UpdateBannerMessage = $"Доступны новые моды или файлы сборки ({remoteVersion}).";
                                UpdateBannerButtonText = "ОБНОВИТЬ";
                                IsUpdateBannerVisible = true;
                            }
                        }
                    }
                }
            }
        }
        catch
        {
            // Фоновая проверка не должна мешать пользователю
        }
    }
}
