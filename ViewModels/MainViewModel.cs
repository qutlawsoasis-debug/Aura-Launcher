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
    private bool _hasUpdateDot;
    private bool _isToastDismissedForSession;
    private string? _pendingNewVersion;
    private readonly IFriendService? _friendService;
    private readonly INotificationService? _notificationService;
    private readonly IDiscordRpcService? _discordRpcService;
    private readonly IReportService? _reportService;
    private readonly IAchievementService? _achievementService;

    // Свойства диалога «Отправить отчёт»
    private bool _isSendReportModalVisible;
    private string _reportErrorText = string.Empty;
    private string _reportUserComment = string.Empty;
    private bool _isSubmittingReport;
    private string _reportSubmitErrorMessage = string.Empty;

    // Свойства диалога успешной отправки отчёта
    private bool _isReportSuccessModalVisible;
    private string _createdReportId = string.Empty;
    private bool _isReportIdCopied;

    // Свойства диалога ошибки отправки (предложение сохранить на рабочий стол)
    private bool _isReportFailedPromptVisible;

    private bool _isInviteToastVisible;
    private string _inviteToastTitle = string.Empty;
    private string _inviteToastSubtitle = string.Empty;
    private IncomingInviteItem? _activeInvite;
    private CancellationTokenSource? _inviteToastCts;

    private bool _isInfoToastVisible;
    private string _infoToastTitle = string.Empty;
    private string _infoToastSubtitle = string.Empty;
    private CancellationTokenSource? _infoToastCts;

    public event EventHandler<Process>? GameStarted;
    public event EventHandler<int>? GameExited;

    public OverviewViewModel OverviewVM { get; }
    public SettingsViewModel SettingsVM { get; }
    public WardrobeViewModel WardrobeVM { get; }
    public LobbyViewModel LobbyVM { get; }
    public FriendsViewModel FriendsVM { get; }

    public bool IsInviteToastVisible
    {
        get => _isInviteToastVisible;
        set => SetProperty(ref _isInviteToastVisible, value);
    }

    public string InviteToastTitle
    {
        get => _inviteToastTitle;
        set => SetProperty(ref _inviteToastTitle, value);
    }

    public string InviteToastSubtitle
    {
        get => _inviteToastSubtitle;
        set => SetProperty(ref _inviteToastSubtitle, value);
    }

    public bool IsInfoToastVisible
    {
        get => _isInfoToastVisible;
        set => SetProperty(ref _isInfoToastVisible, value);
    }

    public string InfoToastTitle
    {
        get => _infoToastTitle;
        set => SetProperty(ref _infoToastTitle, value);
    }

    public string InfoToastSubtitle
    {
        get => _infoToastSubtitle;
        set
        {
            if (SetProperty(ref _infoToastSubtitle, value))
            {
                OnPropertyChanged(nameof(HasInfoToastSubtitle));
            }
        }
    }

    public bool HasInfoToastSubtitle => !string.IsNullOrWhiteSpace(_infoToastSubtitle);

    public void ShowInfoToast(string title, string? subtitle = null, int autoDismissMs = 4000)
    {
        _infoToastCts?.Cancel();
        _infoToastCts = new CancellationTokenSource();
        var ct = _infoToastCts.Token;

        InfoToastTitle = title;
        InfoToastSubtitle = subtitle ?? string.Empty;
        IsInfoToastVisible = true;

        if (autoDismissMs > 0)
        {
            _ = Task.Delay(autoDismissMs, ct).ContinueWith(t =>
            {
                if (!t.IsCanceled)
                {
                    System.Windows.Application.Current?.Dispatcher?.InvokeAsync(() =>
                    {
                        IsInfoToastVisible = false;
                    });
                }
            }, ct);
        }
    }

    private bool _isProtocolPromptVisible;
    private string _protocolPromptTitle = string.Empty;
    private string _protocolPromptSubtitle = string.Empty;
    private string _protocolPromptConfirmText = "Войти";
    private Func<Task>? _pendingProtocolAction;

    public bool IsProtocolPromptVisible
    {
        get => _isProtocolPromptVisible;
        set => SetProperty(ref _isProtocolPromptVisible, value);
    }

    public string ProtocolPromptTitle
    {
        get => _protocolPromptTitle;
        set => SetProperty(ref _protocolPromptTitle, value);
    }

    public string ProtocolPromptSubtitle
    {
        get => _protocolPromptSubtitle;
        set => SetProperty(ref _protocolPromptSubtitle, value);
    }

    public string ProtocolPromptConfirmText
    {
        get => _protocolPromptConfirmText;
        set => SetProperty(ref _protocolPromptConfirmText, value);
    }

    private bool _isChangelogModalVisible;
    public bool IsChangelogModalVisible
    {
        get => _isChangelogModalVisible;
        set => SetProperty(ref _isChangelogModalVisible, value);
    }

    public RelayCommand OpenChangelogCommand { get; }
    public RelayCommand CloseChangelogCommand { get; }
    public RelayCommand OpenOverviewScreenshotCommand { get; }

    public bool IsSendReportModalVisible
    {
        get => _isSendReportModalVisible;
        set => SetProperty(ref _isSendReportModalVisible, value);
    }

    public string ReportErrorText
    {
        get => _reportErrorText;
        set => SetProperty(ref _reportErrorText, value);
    }

    public string ReportUserComment
    {
        get => _reportUserComment;
        set
        {
            var val = value ?? string.Empty;
            if (val.Length > 300) val = val.Substring(0, 300);
            SetProperty(ref _reportUserComment, val);
        }
    }

    public bool IsSubmittingReport
    {
        get => _isSubmittingReport;
        set => SetProperty(ref _isSubmittingReport, value);
    }

    public string ReportSubmitErrorMessage
    {
        get => _reportSubmitErrorMessage;
        set => SetProperty(ref _reportSubmitErrorMessage, value);
    }

    public bool IsReportSuccessModalVisible
    {
        get => _isReportSuccessModalVisible;
        set => SetProperty(ref _isReportSuccessModalVisible, value);
    }

    public string CreatedReportId
    {
        get => _createdReportId;
        set => SetProperty(ref _createdReportId, value);
    }

    public bool IsReportIdCopied
    {
        get => _isReportIdCopied;
        set => SetProperty(ref _isReportIdCopied, value);
    }

    public bool IsReportFailedPromptVisible
    {
        get => _isReportFailedPromptVisible;
        set => SetProperty(ref _isReportFailedPromptVisible, value);
    }

    public RelayCommand OpenSendReportCommand { get; }
    public RelayCommand CancelSendReportCommand { get; }
    public AsyncRelayCommand SubmitReportCommand { get; }
    public RelayCommand CopyReportIdCommand { get; }
    public RelayCommand CloseReportSuccessCommand { get; }
    public AsyncRelayCommand SaveReportToDesktopCommand { get; }
    public RelayCommand DismissReportFailedPromptCommand { get; }

    public AsyncRelayCommand ConfirmProtocolPromptCommand { get; }
    public RelayCommand CancelProtocolPromptCommand { get; }

    public AsyncRelayCommand AcceptInviteCommand { get; }
    public AsyncRelayCommand DeclineInviteCommand { get; }

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
                OnPropertyChanged(nameof(IsLobbyActive));
                OnPropertyChanged(nameof(IsFriendsActive));
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

    public bool IsLobbyActive
    {
        get => CurrentTabName.Equals("Lobby", StringComparison.OrdinalIgnoreCase);
        set
        {
            if (value) SwitchTab("Lobby");
        }
    }

    public bool IsFriendsActive
    {
        get => CurrentTabName.Equals("Friends", StringComparison.OrdinalIgnoreCase);
        set
        {
            if (value) SwitchTab("Friends");
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

    public bool IsWorkshopActive
    {
        get => CurrentTabName.Equals("Workshop", StringComparison.OrdinalIgnoreCase);
        set
        {
            if (value) SwitchTab("Workshop");
        }
    }

    public void SwitchTab(string viewName)
    {
        if (string.IsNullOrWhiteSpace(viewName)) return;

        if (CurrentTabName.Equals("Friends", StringComparison.OrdinalIgnoreCase) && !viewName.Equals("Friends", StringComparison.OrdinalIgnoreCase))
        {
            FriendsVM?.OnTabDeactivated();
        }

        CurrentTabName = viewName;
        if (viewName.Equals("Settings", StringComparison.OrdinalIgnoreCase))
        {
            CurrentView = SettingsVM;
        }
        else if (viewName.Equals("Wardrobe", StringComparison.OrdinalIgnoreCase))
        {
            CurrentView = WardrobeVM;
        }
        else if (viewName.Equals("Workshop", StringComparison.OrdinalIgnoreCase))
        {
            CurrentView = WorkshopVM;
            _ = WorkshopVM.RefreshAllAsync();
        }
        else if (viewName.Equals("Lobby", StringComparison.OrdinalIgnoreCase))
        {
            CurrentView = LobbyVM;
        }
        else if (viewName.Equals("Friends", StringComparison.OrdinalIgnoreCase))
        {
            FriendsVM?.OnTabActivated();
            CurrentView = FriendsVM!;
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
                OnPropertyChanged(nameof(AllocatedRamText));
                OnPropertyChanged(nameof(CanApplyUpdate));
                OnPropertyChanged(nameof(UpdateBannerMessage));
                LaunchOrCancelCommand.RaiseCanExecuteChanged();
                LaunchGameCommand.RaiseCanExecuteChanged();
                ApplyBannerUpdateCommand?.RaiseCanExecuteChanged();
            }
        }
    }

    public Process? CurrentGameProcess => _gameProcess ?? _launchService.CurrentGameProcess;

    public string LaunchButtonText
    {
        get
        {
            if (IsGameRunning) return "Игра запущена";
            if (IsBusy) return "Отмена";
            var gameDir = _launchService.ResolveMinecraftDirectory(_configService.CurrentConfig.GameDir);
            return _launchService.CheckEnvironmentInstalled(gameDir) ? "Играть" : "Установить";
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

    public string AllocatedRamText
    {
        get
        {
            if (IsGameRunning) return "Закройте Minecraft, чтобы запустить снова";
            return $"Выделено {Math.Round(_configService.CurrentConfig.RamMb / 1024.0, 0)} ГБ памяти";
        }
    }

    public ObservableCollection<string> GameLogs { get; } = new();

    // Свойства баннера обновлений / тоста
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
        get
        {
            if (IsGameRunning)
            {
                return "Обновление скачается и лаунчер перезапустится. Сначала закройте игру";
            }
            if (LobbyVM != null && LobbyVM.IsLobbyCreated)
            {
                return "Обновление скачается и лаунчер перезапустится. Лобби закроется";
            }
            return !string.IsNullOrWhiteSpace(_updateBannerMessage) 
                ? _updateBannerMessage 
                : "Обновление скачается и лаунчер перезапустится.";
        }
        set => SetProperty(ref _updateBannerMessage, value);
    }

    public string UpdateBannerButtonText
    {
        get => !string.IsNullOrWhiteSpace(_updateBannerButtonText) ? _updateBannerButtonText : "Обновить";
        set => SetProperty(ref _updateBannerButtonText, value);
    }

    public bool HasUpdateDot
    {
        get => _hasUpdateDot;
        set => SetProperty(ref _hasUpdateDot, value);
    }

    public bool CanApplyUpdate => !IsGameRunning;

    public string LauncherVersionText => LauncherUpdateService.ResolveVersions(_launcherUpdateService.CurrentVersion).UserFacingVersion;

    // Команды
    public RelayCommand LaunchOrCancelCommand { get; }
    public AsyncRelayCommand LaunchGameCommand { get; }
    public RelayCommand CancelCommand { get; }
    public AsyncRelayCommand CheckUpdatesCommand { get; }
    public RelayCommand NavigateCommand { get; }
    public RelayCommand CloseWindowCommand { get; }
    public RelayCommand MinimizeWindowCommand { get; }
    public RelayCommand ToggleMaximizeWindowCommand { get; }
    public AsyncRelayCommand ApplyBannerUpdateCommand { get; }
    public RelayCommand DismissBannerCommand { get; }
    public RelayCommand ToggleMuteCommand { get; }
    public RelayCommand TogglePlayPauseCommand { get; }
    public RelayCommand NextTrackCommand { get; }
    public RelayCommand PreviousTrackCommand { get; }

    private bool _isWindowMaximized = true;
    public bool IsWindowMaximized
    {
        get => _isWindowMaximized;
        set
        {
            if (SetProperty(ref _isWindowMaximized, value))
            {
                OnPropertyChanged(nameof(MaximizeRestoreToolTip));
            }
        }
    }

    public string MaximizeRestoreToolTip => IsWindowMaximized ? "Оконный режим" : "Во весь экран";

    public IAnthemService? AnthemService { get; }
    public string CurrentTrackTitle => AnthemService?.CurrentTrackTitle ?? "Aura Theme";

    public WorkshopViewModel WorkshopVM { get; }

    private DateTime? _gameSessionStartTime;

    public int AnthemVolume
    {
        get => AnthemService?.VolumePercent ?? 100;
        set
        {
            if (AnthemService != null && AnthemService.VolumePercent != value)
            {
                AnthemService.VolumePercent = value;
                OnPropertyChanged(nameof(AnthemVolume));
                OnPropertyChanged(nameof(IsAnthemMuted));
            }
        }
    }

    public bool IsAnthemMuted
    {
        get => AnthemService?.IsMuted ?? false;
        set
        {
            if (AnthemService != null && AnthemService.IsMuted != value)
            {
                AnthemService.IsMuted = value;
                OnPropertyChanged(nameof(IsAnthemMuted));
                OnPropertyChanged(nameof(AnthemVolume));
            }
        }
    }

    public MainViewModel(
        IConfigService configService,
        ILauncherUpdateService launcherUpdateService,
        IPackUpdateService packUpdateService,
        IGameLaunchService launchService,
        ISkinService skinService,
        OverviewViewModel overviewViewModel,
        SettingsViewModel settingsViewModel,
        WardrobeViewModel wardrobeViewModel,
        LobbyViewModel? lobbyViewModel = null,
        FriendsViewModel? friendsViewModel = null,
        IFriendService? friendService = null,
        IServerListSyncService? serverListSyncService = null,
        IAnthemService? anthemService = null,
        INotificationService? notificationService = null,
        IDiscordRpcService? discordRpcService = null,
        IReportService? reportService = null,
        WorkshopViewModel? workshopViewModel = null,
        IWorkshopService? workshopService = null,
        IAchievementService? achievementService = null)
    {
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _launcherUpdateService = launcherUpdateService ?? throw new ArgumentNullException(nameof(launcherUpdateService));
        _packUpdateService = packUpdateService ?? throw new ArgumentNullException(nameof(packUpdateService));
        _launchService = launchService ?? throw new ArgumentNullException(nameof(launchService));
        _skinService = skinService ?? throw new ArgumentNullException(nameof(skinService));
        _serverListSyncService = serverListSyncService ?? new ServerListSyncService(launchService);
        _notificationService = notificationService;
        _discordRpcService = discordRpcService;
        _reportService = reportService ?? new ReportService(configService, notificationService ?? new NotificationService(configService));
        _achievementService = achievementService;
        AnthemService = anthemService;
        OverviewVM = overviewViewModel ?? throw new ArgumentNullException(nameof(overviewViewModel));
        SettingsVM = settingsViewModel ?? throw new ArgumentNullException(nameof(settingsViewModel));
        WardrobeVM = wardrobeViewModel ?? throw new ArgumentNullException(nameof(wardrobeViewModel));
        var manifestService = new ModManifestService();
        ILobbyService? sharedLobbyService = App.Services?.GetService(typeof(ILobbyService)) as ILobbyService;
        if (sharedLobbyService == null && (lobbyViewModel == null || friendsViewModel == null))
        {
            var apiClient = new LobbyApiClient(configService: configService);
            sharedLobbyService = new LobbyService(
                apiClient,
                new PlayitTunnelProvider(),
                configService: configService,
                manifestService: manifestService,
                launchService: launchService);
        }
        LobbyVM = lobbyViewModel ?? new LobbyViewModel(sharedLobbyService!, launchService, configService, notificationService: notificationService, discordRpcService: discordRpcService, modManifestService: manifestService);
        WorkshopVM = workshopViewModel ?? new WorkshopViewModel(workshopService ?? new WorkshopService(), _configService, _launchService);
        WorkshopVM.ModToggled += () => _ = LobbyVM.UpdateManifestAsync();
        WorkshopVM.ScreenshotsCountChanged += _ => OverviewVM.RefreshRightFeed();
        
        _friendService = friendService;
        FriendsVM = friendsViewModel ?? new FriendsViewModel(_friendService ?? new FriendService(_configService), sharedLobbyService!, _skinService, LobbyVM);
        FriendsVM.OpenLobbyRequested += () => SwitchTab("Lobby");

        if (_notificationService != null)
        {
            _notificationService.RegisterInAppToastHandler((title, subtitle) =>
            {
                System.Windows.Application.Current?.Dispatcher?.InvokeAsync(() =>
                {
                    ShowInfoToast(title, subtitle);
                });
            });
        }

        if (_friendService != null)
        {
            _friendService.InviteReceived += OnInviteReceived;
            _friendService.FriendRequestReceived += req =>
            {
                _notificationService?.NotifyFriendRequest(req.Nick, req.Id);
            };
            _friendService.FriendsListUpdated += friends =>
            {
                _achievementService?.Report("friends_count", friends?.Count ?? 0);
            };
        }

        if (_achievementService != null)
        {
            GameStarted += (s, proc) => _achievementService.Report("game_launch");
            GameExited += (s, code) => _achievementService.Report("game_exit");

            LobbyVM.LobbyCreated += () => _achievementService.Report("lobby_created");
            LobbyVM.PlayerCountChanged += count => _achievementService.Report("lobby_players_count", count);
            LobbyVM.Reconnected += () => _achievementService.Report("reconnected");

            WorkshopVM.BackupCreated += () => _achievementService.Report("backup_created");
            WorkshopVM.ScreenshotsCountChanged += count => _achievementService.Report("screenshots_count", count);

            WardrobeVM.CustomSkinApplied += () => _achievementService.Report("custom_skin");
        }

        _discordRpcService?.Initialize();
        _discordRpcService?.SetInLauncher();

        OpenChangelogCommand = new RelayCommand(_ => IsChangelogModalVisible = true);
        CloseChangelogCommand = new RelayCommand(_ => IsChangelogModalVisible = false);
        OpenOverviewScreenshotCommand = new RelayCommand(p =>
        {
            if (p is ScreenshotItem item)
            {
                SwitchTab("Workshop");
                WorkshopVM.ActiveSubTab = "Screenshots";
                WorkshopVM.OpenScreenshotPreview(item);
            }
            else
            {
                SwitchTab("Workshop");
                WorkshopVM.ActiveSubTab = "Screenshots";
            }
        });

        ConfirmProtocolPromptCommand = new AsyncRelayCommand(async () =>
        {
            IsProtocolPromptVisible = false;
            var action = _pendingProtocolAction;
            _pendingProtocolAction = null;
            if (action != null)
            {
                await action();
            }
        });

        CancelProtocolPromptCommand = new RelayCommand(_ =>
        {
            IsProtocolPromptVisible = false;
            _pendingProtocolAction = null;
        });

        AcceptInviteCommand = new AsyncRelayCommand(async () =>
        {
            _inviteToastCts?.Cancel();
            IsInviteToastVisible = false;
            if (_activeInvite == null || _friendService == null) return;

            var inv = _activeInvite;
            _activeInvite = null;

            var (success, lobbyCode, error) = await _friendService.RespondInviteAsync(inv.InviteId, accept: true);
            if (success && !string.IsNullOrWhiteSpace(lobbyCode))
            {
                SwitchTab("Lobby");
                await LobbyVM.JoinByCodeAsync(lobbyCode, fromInvite: true);
            }
        });

        DeclineInviteCommand = new AsyncRelayCommand(async () =>
        {
            _inviteToastCts?.Cancel();
            IsInviteToastVisible = false;
            if (_activeInvite == null || _friendService == null) return;

            var inv = _activeInvite;
            _activeInvite = null;

            await _friendService.RespondInviteAsync(inv.InviteId, accept: false);
        });

        LobbyVM.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(LobbyViewModel.IsLobbyCreated))
            {
                OnPropertyChanged(nameof(UpdateBannerMessage));
            }
            if (_friendService != null && (e.PropertyName == nameof(LobbyViewModel.IsInLobby) || e.PropertyName == nameof(LobbyViewModel.LobbyCode)))
            {
                bool changed = _friendService.IsInLobby != LobbyVM.IsInLobby ||
                               !string.Equals(_friendService.CurrentLobbyCode, LobbyVM.LobbyCode, StringComparison.OrdinalIgnoreCase);
                _friendService.IsInLobby = LobbyVM.IsInLobby;
                _friendService.CurrentLobbyCode = LobbyVM.LobbyCode;
                if (changed)
                {
                    _ = _friendService.SyncNowAsync();
                }
            }
            if (_discordRpcService != null && (e.PropertyName == nameof(LobbyViewModel.IsInLobby) || e.PropertyName == nameof(LobbyViewModel.LobbyPlayers)))
            {
                if (LobbyVM.IsInLobby)
                {
                    _discordRpcService.SetInLobby(LobbyVM.LobbyPlayers.Count, LobbyVM.HostName, LobbyVM.LobbyCode);
                }
                else if (!IsGameRunning)
                {
                    _discordRpcService.SetInLauncher();
                }
            }
        };
        SettingsVM.SendReportRequested += (err) =>
        {
            PromptSendReport(err);
        };

        LobbyVM.SendReportRequested += (err) =>
        {
            PromptSendReport(err);
        };

        OpenSendReportCommand = new RelayCommand(p =>
        {
            var err = p as string;
            PromptSendReport(err);
        });

        CancelSendReportCommand = new RelayCommand(_ =>
        {
            IsSendReportModalVisible = false;
        });

        SubmitReportCommand = new AsyncRelayCommand(async () =>
        {
            if (_reportService == null || IsSubmittingReport) return;

            IsSubmittingReport = true;
            ReportSubmitErrorMessage = string.Empty;

            try
            {
                var res = await _reportService.SendReportAsync(ReportErrorText, ReportUserComment);
                if (res.Success && !string.IsNullOrWhiteSpace(res.ReportId))
                {
                    IsSendReportModalVisible = false;
                    CreatedReportId = res.ReportId;
                    IsReportIdCopied = false;
                    IsReportSuccessModalVisible = true;
                }
                else
                {
                    IsSendReportModalVisible = false;
                    IsReportFailedPromptVisible = true;
                }
            }
            catch (Exception ex)
            {
                IsSendReportModalVisible = false;
                IsReportFailedPromptVisible = true;
            }
            finally
            {
                IsSubmittingReport = false;
            }
        });

        CopyReportIdCommand = new RelayCommand(_ =>
        {
            if (!string.IsNullOrWhiteSpace(CreatedReportId))
            {
                try
                {
                    System.Windows.Clipboard.SetText(CreatedReportId);
                    IsReportIdCopied = true;
                }
                catch { }
            }
        });

        CloseReportSuccessCommand = new RelayCommand(_ =>
        {
            IsReportSuccessModalVisible = false;
        });

        SaveReportToDesktopCommand = new AsyncRelayCommand(async () =>
        {
            IsReportFailedPromptVisible = false;
            if (_reportService != null)
            {
                await _reportService.GenerateReportZipAsync(ReportErrorText, ReportUserComment);
            }
        });

        DismissReportFailedPromptCommand = new RelayCommand(_ =>
        {
            IsReportFailedPromptVisible = false;
        });

        AnthemService = anthemService;

        ToggleMuteCommand = new RelayCommand(_ =>
        {
            if (AnthemService != null)
            {
                AnthemService.ToggleMute();
                OnPropertyChanged(nameof(IsAnthemMuted));
                OnPropertyChanged(nameof(AnthemVolume));
            }
        });

        TogglePlayPauseCommand = new RelayCommand(_ =>
        {
            AnthemService?.TogglePlayPause();
            OnPropertyChanged(nameof(CurrentTrackTitle));
        });

        NextTrackCommand = new RelayCommand(_ =>
        {
            AnthemService?.NextTrack();
            OnPropertyChanged(nameof(CurrentTrackTitle));
        });

        PreviousTrackCommand = new RelayCommand(_ =>
        {
            AnthemService?.PreviousTrack();
            OnPropertyChanged(nameof(CurrentTrackTitle));
        });

        if (AnthemService != null)
        {
            AnthemService.TrackChanged += (s, title) =>
            {
                OnPropertyChanged(nameof(CurrentTrackTitle));
            };
        }

        LobbyVM.GuestConnectRequested += (s, tunnelAddress) =>
        {
            _ = LaunchGameAsync(tunnelAddress);
        };

        LobbyVM.HostLaunchRequested += (s, e) =>
        {
            _ = LaunchGameAsync(null);
        };

        _currentView = OverviewVM;

        LaunchGameCommand = new AsyncRelayCommand(() => LaunchGameAsync(null), () => !IsBusy && !IsGameRunning && !_isLaunching && NicknameValidator.Validate(_configService.CurrentConfig.Nickname).IsValid);
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
        ToggleMaximizeWindowCommand = new RelayCommand(_ =>
        {
            if (System.Windows.Application.Current.MainWindow is MainWindow mw)
            {
                mw.ToggleMaximizeRestore();
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
            _isToastDismissedForSession = true;
            IsUpdateBannerVisible = false;
        });

        ApplyBannerUpdateCommand = new AsyncRelayCommand(async () =>
        {
            if (IsGameRunning) return;
            IsUpdateBannerVisible = false;

            if (_isLauncherUpdatePending)
            {
                // Открываем отдельное окно обновления лаунчера (StartupWindow)
                OpenStartupWindow();
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
                    PromptGameCrashToast(exitCode);
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
            OnPropertyChanged(nameof(AllocatedRamText));
            OnPropertyChanged(nameof(AnthemVolume));
            OnPropertyChanged(nameof(IsAnthemMuted));
            UpdateAvatar();
            OverviewVM.RefreshStats();
            UpdateIdleState();
            _ = _friendService?.SyncNowAsync();
        };
    }

    private void OnInviteReceived(IncomingInviteItem invite)
    {
        _notificationService?.NotifyLobbyInvite(invite.FromNick, invite.InviteId);

        System.Windows.Application.Current?.Dispatcher?.InvokeAsync(() =>
        {
            _activeInvite = invite;
            InviteToastTitle = $"{invite.FromNick} приглашает вас в лобби";
            InviteToastSubtitle = "Приглашение действительно 2 мин";
            IsInviteToastVisible = true;

            _inviteToastCts?.Cancel();
            _inviteToastCts = new CancellationTokenSource();
            var token = _inviteToastCts.Token;
            _ = Task.Delay(TimeSpan.FromSeconds(120), token).ContinueWith(_ =>
            {
                if (!token.IsCancellationRequested)
                {
                    System.Windows.Application.Current?.Dispatcher?.InvokeAsync(() =>
                    {
                        if (_activeInvite?.InviteId == invite.InviteId)
                        {
                            IsInviteToastVisible = false;
                            _activeInvite = null;
                        }
                    });
                }
            });
        });
    }

    private void Dispatch(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.InvokeAsync(action);
        }
    }

    public void HandleProtocolUri(string? rawUri)
    {
        if (string.IsNullOrWhiteSpace(rawUri)) return;

        FabricGameLaunchService.LogLauncherEvent($"[PROTOCOL] Received URI: {rawUri}");

        try
        {
            var cleaned = rawUri.Trim().Trim('"', '\'').Trim();
            if (cleaned.StartsWith("aura://", StringComparison.OrdinalIgnoreCase))
            {
                cleaned = cleaned.Substring(7);
            }
            else if (cleaned.StartsWith("aura:", StringComparison.OrdinalIgnoreCase))
            {
                cleaned = cleaned.Substring(5);
            }

            cleaned = cleaned.Trim('/');
            var parts = cleaned.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                FabricGameLaunchService.LogLauncherEvent($"[PROTOCOL: WARN] Insufficient URI parts: {cleaned}");
                return;
            }

            string action = parts[0].Trim().ToLowerInvariant();
            string code = parts[1].Trim().ToUpperInvariant();

            // Проверка кода по ^[A-Z0-9]{6,8}$
            if (!System.Text.RegularExpressions.Regex.IsMatch(code, "^[A-Z0-9]{6,8}$"))
            {
                FabricGameLaunchService.LogLauncherEvent($"[PROTOCOL: WARN] Invalid code format: {code}");
                return;
            }

            Dispatch(() =>
            {
                if (action == "join")
                {
                    ProtocolPromptTitle = $"Войти в лобби {code}?";
                    ProtocolPromptSubtitle = "Подключиться к игре хоста по ссылке?";
                    ProtocolPromptConfirmText = "Войти";
                    _pendingProtocolAction = async () =>
                    {
                        SwitchTab("Lobby");
                        await LobbyVM.JoinByCodeAsync(code);
                    };
                    IsProtocolPromptVisible = true;
                }
                else if (action == "friend")
                {
                    ProtocolPromptTitle = $"Отправить заявку в друзья по коду {code}?";
                    ProtocolPromptSubtitle = "Добавить игрока в список друзей?";
                    ProtocolPromptConfirmText = "Отправить";
                    _pendingProtocolAction = async () =>
                    {
                        SwitchTab("Friends");
                        FriendsVM.AddCodeInput = code;
                        if (_friendService != null)
                        {
                            await _friendService.SendFriendRequestAsync(code);
                        }
                    };
                    IsProtocolPromptVisible = true;
                }
            });
        }
        catch (Exception ex)
        {
            FabricGameLaunchService.LogLauncherEvent($"[PROTOCOL: ERROR] Error handling URI: {ex.Message}");
        }
    }

    public async Task InitializeAsync()
    {
        CurrentView = OverviewVM;
        await _configService.LoadConfigAsync();
        OverviewVM.RefreshStats();
        UpdateAvatar();
        _friendService?.Start();

        var initialNickValidation = NicknameValidator.Validate(_configService.CurrentConfig.Nickname);
        if (!initialNickValidation.IsValid)
        {
            FabricGameLaunchService.LogLauncherEvent("[CONFIG] Обнаружен невалидный никнейм в конфигурации. Запуск заблокирован до исправления.");
        }

        // Определение уже запущенной игры при старте лаунчера
        var gameDir = _launchService.ResolveMinecraftDirectory(_configService.CurrentConfig.GameDir);
        FabricGameLaunchService.EnsureDefaultOptions(gameDir);
        FabricGameLaunchService.EnsureDefaultLspConfig(gameDir);
        _skinService.EnsureCustomSkinLoaderConfig(gameDir);
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

    private async Task LaunchGameAsync(string? quickPlayMultiplayer = null)
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
            config.QuickPlayMultiplayer = quickPlayMultiplayer;

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

            // 1.6. Конфигурация CustomSkinLoader: добавление AuraLobby первым источником и загрузка актуального скина
            try
            {
                var targetGameDir = _launchService.ResolveMinecraftDirectory(config.GameDir);
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
            }
            catch (Exception ex)
            {
                FabricGameLaunchService.LogLauncherEvent($"[CSL-SYNC: ERROR] {ex.Message}");
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

            _gameSessionStartTime = DateTime.UtcNow;
            _discordRpcService?.SetPlayingGame("Aura Pack (1.20.1)", _gameSessionStartTime);

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
                        if (_gameSessionStartTime.HasValue)
                        {
                            var elapsed = DateTime.UtcNow - _gameSessionStartTime.Value;
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

                        OverviewVM.RefreshStats();
                        LobbyVM.OnGameExited();
                        if (exitCode != 0)
                        {
                            SetLauncherState(LauncherState.Error, $"Игра завершилась с ошибкой (код {exitCode}). Лог: {logPath}");
                        }
                        else
                        {
                            UpdateIdleState();
                        }

                        // Если обновление лаунчера было отложено из-за игры, показываем тост сейчас
                        if (_isLauncherUpdatePending && !_isToastDismissedForSession && !string.IsNullOrWhiteSpace(_pendingNewVersion))
                        {
                            var parts = _pendingNewVersion.Split('.');
                            string displayVer = parts.Length == 3 && int.TryParse(parts[2], out int patch) && patch >= 8
                                ? $"beta 1.0.{patch - 8}"
                                : _pendingNewVersion;

                            UpdateBannerTitle = $"Вышло обновление {displayVer}";
                            UpdateBannerButtonText = "Обновить";
                            OnPropertyChanged(nameof(UpdateBannerMessage));
                            IsUpdateBannerVisible = true;
                        }

                        try
                        {
                            if (_friendService != null)
                            {
                                _friendService.IsGameRunning = false;
                                _ = _friendService.SyncNowAsync();
                            }
                            if (LobbyVM.IsInLobby && LobbyVM.LobbyPlayers.Count > 0)
                            {
                                _discordRpcService?.SetInLobby(LobbyVM.LobbyPlayers.Count, LobbyVM.HostName, LobbyVM.LobbyCode);
                            }
                            else
                            {
                                _discordRpcService?.SetInLauncher();
                            }
                            GameExited?.Invoke(this, exitCode);
                        }
                        catch { }
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

            try
            {
                if (_friendService != null)
                {
                    _friendService.IsGameRunning = true;
                    _ = _friendService.SyncNowAsync();
                }
                _discordRpcService?.SetPlayingGame(startTime: DateTime.UtcNow);
                GameStarted?.Invoke(this, process);
            }
            catch { }
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
            if (_launcherUpdateService.IsInstalled)
            {
                var newVersion = await _launcherUpdateService.CheckForUpdatesAsync(CancellationToken.None);
                if (!string.IsNullOrWhiteSpace(newVersion))
                {
                    _isLauncherUpdatePending = true;
                    _pendingNewVersion = newVersion;
                    HasUpdateDot = true;

                    if (!_isToastDismissedForSession && !IsGameRunning)
                    {
                        var parts = newVersion.Split('.');
                        string displayVer = parts.Length == 3 && int.TryParse(parts[2], out int patch) && patch >= 8
                            ? $"beta 1.0.{patch - 8}"
                            : newVersion;

                        UpdateBannerTitle = $"Вышло обновление {displayVer}";
                        UpdateBannerButtonText = "Обновить";
                        OnPropertyChanged(nameof(UpdateBannerMessage));
                        IsUpdateBannerVisible = true;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            FabricGameLaunchService.LogLauncherEvent($"[LAUNCHER-UPDATE: UNHANDLED] {ex.Message}");
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
            Interval = TimeSpan.FromMinutes(30)
        };
        _backgroundUpdateTimer.Tick += async (s, e) =>
        {
            await CheckBackgroundUpdatesAsync();
        };
        _backgroundUpdateTimer.Start();
    }

    public async Task CheckBackgroundUpdatesAsync()
    {
        if (IsBusy || _isLaunching)
        {
            return;
        }

        try
        {
            // 1. Проверяем обновление лаунчера
            if (_launcherUpdateService.IsInstalled)
            {
                var newVersion = await _launcherUpdateService.CheckForUpdatesAsync(CancellationToken.None);
                if (!string.IsNullOrWhiteSpace(newVersion))
                {
                    _isLauncherUpdatePending = true;
                    _pendingNewVersion = newVersion;
                    HasUpdateDot = true;

                    if (!_isToastDismissedForSession && !IsGameRunning)
                    {
                        var parts = newVersion.Split('.');
                        string displayVer = parts.Length == 3 && int.TryParse(parts[2], out int patch) && patch >= 8
                            ? $"beta 1.0.{patch - 8}"
                            : newVersion;

                        UpdateBannerTitle = $"Вышло обновление {displayVer}";
                        UpdateBannerButtonText = "Обновить";
                        OnPropertyChanged(nameof(UpdateBannerMessage));
                        IsUpdateBannerVisible = true;
                    }
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

    public void OpenStartupWindow()
    {
        var mainWin = System.Windows.Application.Current.MainWindow;
        Views.StartupWindow? splashWin = null;
        var startupVm = new StartupWindowViewModel(
            _launcherUpdateService,
            onLaunchMainRequested: () =>
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    splashWin?.Close();
                    if (mainWin != null)
                    {
                        mainWin.Show();
                        mainWin.Activate();
                    }
                });
            },
            onCloseRequested: () =>
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    splashWin?.Close();
                    System.Windows.Application.Current.Shutdown(0);
                });
            });

        splashWin = new Views.StartupWindow
        {
            DataContext = startupVm
        };

        if (mainWin != null)
        {
            mainWin.Hide();
        }

        splashWin.Show();
        _ = startupVm.StartStartupFlowAsync();
    }

    public void PromptSendReport(string? errorText = null)
    {
        ReportErrorText = errorText ?? string.Empty;
        ReportUserComment = string.Empty;
        ReportSubmitErrorMessage = string.Empty;
        IsSendReportModalVisible = true;
    }

    private void PromptGameCrashToast(int exitCode)
    {
        ProtocolPromptTitle = $"Игра завершилась с ошибкой (код {exitCode})";
        ProtocolPromptSubtitle = "Отправить отчёт об ошибке разработчикам?";
        ProtocolPromptConfirmText = "Отчёт";
        _pendingProtocolAction = () =>
        {
            PromptSendReport($"Game exited with error code {exitCode}");
            return Task.CompletedTask;
        };
        IsProtocolPromptVisible = true;
    }
}
