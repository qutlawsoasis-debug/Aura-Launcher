using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using AuraLauncher.Core;
using AuraLauncher.Models;
using AuraLauncher.Services.Interfaces;
using AuraLauncher.Services.Implementations;

namespace AuraLauncher.ViewModels;

/// <summary>
/// ViewModel для вкладки "ЛОББИ" — P2P LAN-лобби.
/// Управляет UI для хоста (создание лобби, отображение кода, статус мира)
/// и для гостя (ввод кода, присоединение, ожидание и подключение к игре).
/// </summary>
public class LobbyViewModel : ObservableObject
{
    private readonly ILobbyService _lobbyService;
    private readonly IGameLaunchService _launchService;
    private readonly IConfigService _configService;
    private readonly IModManifestService _modManifestService;
    private readonly ILanWorldWatcher _worldWatcher;
    private readonly ITunnelProvider? _tunnelProvider;
    private readonly ISkinService? _skinService;
    private readonly ILobbyApiClient? _lobbyApiClient;
    private readonly INotificationService? _notificationService;
    private readonly IDiscordRpcService? _discordRpcService;
    private readonly IServerListSyncService? _serverListSyncService;
    private readonly IWorkshopService? _workshopService;
    private readonly HashSet<string> _knownPlayerNicks = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _lastPlayerMismatchSignatures = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _ignoredMismatchSignatures = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _lobbyOpLock = new(1, 1);
    private int _lobbyOpGeneration;
    private CancellationTokenSource? _syncNoticeCts;
    private List<ModMismatchItem> _currentMismatchItems = new();
    private Func<Task>? _pendingConnectAction;
    private string _lastSelfMismatchSignature = string.Empty;
    private bool _previousWorldIsOpen;
    private string _lastLocalSkinSignature = string.Empty;

    public event Action? LobbyCreated;
    public event Action<int>? PlayerCountChanged;
    public event Action? Reconnected;

    // Список игроков лобби
    public ObservableCollection<LobbyPlayerItem> LobbyPlayers { get; } = new();

    private System.Windows.Media.Media3D.Model3D? _localPlayerModel3D;
    public System.Windows.Media.Media3D.Model3D LocalPlayerModel3D
    {
        get => _localPlayerModel3D ?? LobbyPlayerItem.DefaultSteveModel3D;
        private set => SetProperty(ref _localPlayerModel3D, value);
    }

    public string LocalPlayerNickname => _configService.CurrentConfig?.Nickname ?? "Player";

    // Уведомление вверху списка игроков (6 секунд)
    private bool _isSyncNoticeVisible;
    public bool IsSyncNoticeVisible
    {
        get => _isSyncNoticeVisible;
        set => SetProperty(ref _isSyncNoticeVisible, value);
    }

    private string _syncNoticeText = string.Empty;
    public string SyncNoticeText
    {
        get => _syncNoticeText;
        set => SetProperty(ref _syncNoticeText, value);
    }

    // Модальный диалог расхождений модов (ModMismatchDialog)
    private bool _isModMismatchDialogVisible;
    public bool IsModMismatchDialogVisible
    {
        get => _isModMismatchDialogVisible;
        set => SetProperty(ref _isModMismatchDialogVisible, value);
    }

    private string _modMismatchTitle = "Моды не совпадают";
    public string ModMismatchTitle
    {
        get => _modMismatchTitle;
        set => SetProperty(ref _modMismatchTitle, value);
    }

    private string _modMismatchSummary = string.Empty;
    public string ModMismatchSummary
    {
        get => _modMismatchSummary;
        set => SetProperty(ref _modMismatchSummary, value);
    }

    public ObservableCollection<ModMismatchItem> ModMismatchItems { get; } = new();

    private bool _hasUnfixableMods;
    public bool HasUnfixableMods
    {
        get => _hasUnfixableMods;
        set => SetProperty(ref _hasUnfixableMods, value);
    }

    private bool _canFixAny;
    public bool CanFixAny
    {
        get => _canFixAny;
        set
        {
            if (SetProperty(ref _canFixAny, value))
            {
                FixAndProceedCommand?.RaiseCanExecuteChanged();
            }
        }
    }

    private bool _isGameRunningWarning;
    public bool IsGameRunningWarning
    {
        get => _isGameRunningWarning;
        set
        {
            if (SetProperty(ref _isGameRunningWarning, value))
            {
                FixAndProceedCommand?.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(CancelButtonText));
            }
        }
    }

    private bool _isJoinDialogMode;
    public bool IsJoinDialogMode
    {
        get => _isJoinDialogMode;
        set
        {
            if (SetProperty(ref _isJoinDialogMode, value))
            {
                OnPropertyChanged(nameof(FixButtonText));
                OnPropertyChanged(nameof(CancelButtonText));
            }
        }
    }

    private bool _isViewOnlyDialogMode;
    public bool IsViewOnlyDialogMode
    {
        get => _isViewOnlyDialogMode;
        set
        {
            if (SetProperty(ref _isViewOnlyDialogMode, value))
            {
                OnPropertyChanged(nameof(CancelButtonText));
            }
        }
    }

    public string FixButtonText => IsJoinDialogMode ? "Включить и войти" : "Включить";

    public string CancelButtonText
    {
        get
        {
            if (IsViewOnlyDialogMode || IsGameRunningWarning) return "Закрыть";
            return IsJoinDialogMode ? "Отмена" : "Позже";
        }
    }

    // Общее состояние
    private bool _isInLobby;
    private string _statusText = "Создайте лобби или введите код друга";
    private string _statusIcon = string.Empty;
    private bool _isBusy;

    // Хост
    private string? _lobbyCode;
    private string? _hostName;
    private string _hostStatusText = "Ожидание мира...";
    private bool _isLobbyCreated;
    private bool _isWorldOpen;
    private bool _codeCopied;

    // Ошибка туннеля и копирование лога
    private bool _showTunnelFailedLogButton;
    private string? _tunnelFailureReason;
    private bool _tunnelLogCopied;

    // Гость
    private string _guestCodeInput = string.Empty;
    private bool _isGuestJoined;
    public bool WasInvited { get; set; }
    private string _guestStatusText = "Введите 6-значный код лобби";
    private bool _canGuestConnect;
    private string? _lastTunnelAddress;
    private string _joinErrorMessage = string.Empty;
    private bool _isJoiningLobby;

    public string JoinErrorMessage
    {
        get => _joinErrorMessage;
        set
        {
            if (SetProperty(ref _joinErrorMessage, value))
            {
                OnPropertyChanged(nameof(HasJoinError));
            }
        }
    }

    public bool HasJoinError => !string.IsNullOrWhiteSpace(_joinErrorMessage);

    public bool IsJoiningLobby
    {
        get => _isJoiningLobby;
        private set
        {
            if (SetProperty(ref _isJoiningLobby, value))
            {
                OnPropertyChanged(nameof(JoinButtonText));
                RaiseAllCommands();
            }
        }
    }

    public string JoinButtonText => IsJoiningLobby ? "Входим…" : "Войти";

    public bool ShowTunnelFailedLogButton
    {
        get => _showTunnelFailedLogButton;
        private set => SetProperty(ref _showTunnelFailedLogButton, value);
    }

    public string? TunnelFailureReason
    {
        get => _tunnelFailureReason;
        private set => SetProperty(ref _tunnelFailureReason, value);
    }

    public bool TunnelLogCopied
    {
        get => _tunnelLogCopied;
        private set => SetProperty(ref _tunnelLogCopied, value);
    }

    public bool CanReconnect => !IsBusy && !_lobbyService.IsHost && (CanGuestConnect || !string.IsNullOrWhiteSpace(_lastTunnelAddress));

    public AsyncRelayCommand CreateLobbyCommand { get; }
    public AsyncRelayCommand JoinLobbyCommand { get; }
    public RelayCommand CopyCodeCommand { get; }
    public AsyncRelayCommand ConnectToGameCommand { get; }
    public AsyncRelayCommand ReconnectCommand { get; }
    public AsyncRelayCommand OpenWorldCommand { get; }
    public RelayCommand LeaveLobbyCommand { get; }
    public RelayCommand CopyTunnelLogCommand { get; }
    public RelayCommand SendReportCommand { get; }
    public AsyncRelayCommand FixAndProceedCommand { get; }
    public RelayCommand JoinAsIsCommand { get; }
    public RelayCommand CancelMismatchDialogCommand { get; }

    public event Action<string?>? SendReportRequested;

    public LobbyViewModel(
        ILobbyService lobbyService,
        IGameLaunchService launchService,
        IConfigService configService,
        ILanWorldWatcher? worldWatcher = null,
        ITunnelProvider? tunnelProvider = null,
        ISkinService? skinService = null,
        ILobbyApiClient? lobbyApiClient = null,
        INotificationService? notificationService = null,
        IDiscordRpcService? discordRpcService = null,
        IModManifestService? modManifestService = null,
        IServerListSyncService? serverListSyncService = null,
        IWorkshopService? workshopService = null)
    {
        _lobbyService = lobbyService ?? throw new ArgumentNullException(nameof(lobbyService));
        _launchService = launchService ?? throw new ArgumentNullException(nameof(launchService));
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _modManifestService = modManifestService ?? new ModManifestService();
        _worldWatcher = worldWatcher ?? new LanWorldWatcher();
        _tunnelProvider = tunnelProvider;
        _skinService = skinService;
        _lobbyApiClient = lobbyApiClient;
        _notificationService = notificationService;
        _discordRpcService = discordRpcService;
        _serverListSyncService = serverListSyncService ?? new ServerListSyncService(launchService);
        _workshopService = workshopService;

        CreateLobbyCommand = new AsyncRelayCommand(CreateLobbyAsync, () => !IsBusy && !IsInLobby);
        JoinLobbyCommand = new AsyncRelayCommand(JoinLobbyAsync, () => !IsBusy && !IsInLobby && GuestCodeInput.Length >= 6 && !IsJoiningLobby);
        CopyCodeCommand = new RelayCommand(_ => CopyCode(), _ => !string.IsNullOrWhiteSpace(LobbyCode));
        CopyLinkCommand = new RelayCommand(_ => CopyLobbyLink(), _ => !string.IsNullOrWhiteSpace(LobbyCode));
        ConnectToGameCommand = new AsyncRelayCommand(ConnectToGameAsync, () => !IsBusy && CanGuestConnect);
        ReconnectCommand = new AsyncRelayCommand(ReconnectAsync, () => CanReconnect);
        OpenWorldCommand = new AsyncRelayCommand(OpenWorldAsHostAsync, () => !IsBusy && IsLobbyCreated && IsHost);
        LeaveLobbyCommand = new RelayCommand(_ => LeaveLobby(), _ => IsInLobby);
        FixAndProceedCommand = new AsyncRelayCommand(FixAndProceedAsync, () => CanFixAny && !IsGameRunningWarning);
        JoinAsIsCommand = new RelayCommand(_ => JoinAsIs());
        CancelMismatchDialogCommand = new RelayCommand(_ => CancelMismatchDialog());

        CopyTunnelLogCommand = new RelayCommand(_ =>
        {
            try
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var logPath = System.IO.Path.Combine(appData, ".aura", "logs", "tunnel.log");
                string text;
                if (System.IO.File.Exists(logPath))
                {
                    var lines = System.IO.File.ReadAllLines(logPath);
                    var takeCount = Math.Min(50, lines.Length);
                    var last50 = new string[takeCount];
                    Array.Copy(lines, lines.Length - takeCount, last50, 0, takeCount);
                    text = string.Join(Environment.NewLine, last50);
                }
                else
                {
                    text = "tunnel.log не найден.";
                }

                Clipboard.SetText(text);
                TunnelLogCopied = true;
                _ = Task.Delay(2000).ContinueWith(_ => Dispatch(() => TunnelLogCopied = false));
            }
            catch (Exception ex)
            {
                PlayitTunnelProvider.LogTunnel($"[UI: ERROR] Failed to copy tunnel log: {ex.Message}");
            }
        });

        SendReportCommand = new RelayCommand(_ =>
        {
            var err = !string.IsNullOrWhiteSpace(TunnelFailureReason) ? TunnelFailureReason : JoinErrorMessage;
            SendReportRequested?.Invoke(err);
        });

        // Подписка на события LobbyService
        _lobbyService.StatusChanged += OnLobbyStatusChanged;
        _lobbyService.TunnelAddressReady += OnTunnelAddressReady;
        _lobbyService.LobbyStatusUpdated += OnLobbyStatusUpdated;
        _lobbyService.ModSyncUpdated += OnModSyncUpdated;

        // Подписка на события лога игры хоста
        _worldWatcher.WorldOpened += OnLanWorldOpened;
        _worldWatcher.WorldClosed += OnLanWorldClosed;

        // Подписка на изменение статуса туннеля
        if (_tunnelProvider != null)
        {
            _tunnelProvider.StatusChanged += OnTunnelStatusChanged;
        }

        LobbyPlayers.CollectionChanged += OnLobbyPlayersCollectionChanged;
        RefreshLocalPlayerModel();

        _configService.ConfigChanged += (s, cfg) =>
        {
            var sig = $"{cfg.Nickname}|{cfg.SkinPath}|{cfg.SkinModel}";
            if (!string.Equals(_lastLocalSkinSignature, sig, StringComparison.Ordinal))
            {
                _lastLocalSkinSignature = sig;
                Dispatch(() =>
                {
                    RefreshLocalPlayerModel();
                    var myNick = cfg.Nickname ?? string.Empty;
                    var selfItem = LobbyPlayers.FirstOrDefault(p => string.Equals(p.Nickname, myNick, StringComparison.OrdinalIgnoreCase) || (IsHost && p.IsHost));
                    if (selfItem != null)
                    {
                        selfItem.Nickname = myNick;
                        selfItem.Avatar = _skinService?.ExtractHeadAvatar(cfg.SkinPath) ?? SkinService.LoadDefaultSteveBitmap();
                        selfItem.PlayerModel3D = LocalPlayerModel3D;
                    }
                });
            }
        };
    }

    // Свойства

    public bool IsInLobby
    {
        get => _isInLobby;
        private set
        {
            if (SetProperty(ref _isInLobby, value))
            {
                RaiseAllCommands();
                NotifyCellPropertiesChanged();
                NotifyStatusStateChanged();
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        set { if (SetProperty(ref _statusText, value)) NotifyStatusStateChanged(); }
    }

    public string StatusIcon
    {
        get => _statusIcon;
        set => SetProperty(ref _statusIcon, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RaiseAllCommands();
            }
        }
    }

    // Хост

    public string? LobbyCode
    {
        get => _lobbyCode;
        private set
        {
            if (SetProperty(ref _lobbyCode, value))
            {
                NotifyCellPropertiesChanged();
                RaiseAllCommands();
            }
        }
    }

    public string? HostName
    {
        get => _hostName ?? LobbyPlayers.FirstOrDefault(p => p.IsHost)?.Nickname ?? (_lobbyService.IsHost ? _configService.CurrentConfig?.Nickname : null);
        private set => SetProperty(ref _hostName, value);
    }

    public string HostStatusText
    {
        get => _hostStatusText;
        internal set { if (SetProperty(ref _hostStatusText, value)) NotifyStatusStateChanged(); }
    }

    public bool IsLobbyCreated
    {
        get => _isLobbyCreated;
        private set
        {
            if (SetProperty(ref _isLobbyCreated, value))
            {
                RaiseAllCommands();
                NotifyStatusStateChanged();
            }
        }
    }

    public bool IsWorldOpen
    {
        get => _isWorldOpen;
        internal set { if (SetProperty(ref _isWorldOpen, value)) { RaiseAllCommands(); NotifyStatusStateChanged(); } }
    }

    public bool CodeCopied
    {
        get => _codeCopied;
        private set => SetProperty(ref _codeCopied, value);
    }

    private bool _isLinkCopied;
    public bool IsLinkCopied
    {
        get => _isLinkCopied;
        private set
        {
            if (SetProperty(ref _isLinkCopied, value))
            {
                OnPropertyChanged(nameof(CopyLinkButtonText));
            }
        }
    }

    public string CopyLinkButtonText => IsLinkCopied ? "Скопировано!" : "Копировать ссылку";
    public RelayCommand CopyLinkCommand { get; }

    public bool IsHost => _lobbyService.IsHost || _isLobbyCreated;

    // Гость

    public string GuestCodeInput
    {
        get => _guestCodeInput;
        set
        {
            // Ограничиваем до 6 символов, только A-Z и 0-9
            var raw = value ?? string.Empty;
            var cleaned = new string(raw.Where(c => char.IsLetterOrDigit(c)).Select(char.ToUpperInvariant).Take(6).ToArray());
            if (SetProperty(ref _guestCodeInput, cleaned))
            {
                JoinErrorMessage = string.Empty;
                RaiseAllCommands();
                NotifyCellPropertiesChanged();
            }
        }
    }

    // Посимвольный доступ к ячейкам кода (для хоста или гостя)
    public string DisplayCode => IsInLobby ? (LobbyCode ?? string.Empty) : GuestCodeInput;
    public string CodeChar0 => GetCodeChar(0);
    public string CodeChar1 => GetCodeChar(1);
    public string CodeChar2 => GetCodeChar(2);
    public string CodeChar3 => GetCodeChar(3);
    public string CodeChar4 => GetCodeChar(4);
    public string CodeChar5 => GetCodeChar(5);

    public bool IsCellFilled0 => DisplayCode.Length > 0;
    public bool IsCellFilled1 => DisplayCode.Length > 1;
    public bool IsCellFilled2 => DisplayCode.Length > 2;
    public bool IsCellFilled3 => DisplayCode.Length > 3;
    public bool IsCellFilled4 => DisplayCode.Length > 4;
    public bool IsCellFilled5 => DisplayCode.Length > 5;

    public bool IsCellCurrent0 => !IsInLobby && DisplayCode.Length == 0;
    public bool IsCellCurrent1 => !IsInLobby && DisplayCode.Length == 1;
    public bool IsCellCurrent2 => !IsInLobby && DisplayCode.Length == 2;
    public bool IsCellCurrent3 => !IsInLobby && DisplayCode.Length == 3;
    public bool IsCellCurrent4 => !IsInLobby && DisplayCode.Length == 4;
    public bool IsCellCurrent5 => !IsInLobby && DisplayCode.Length == 5;

    private string GetCodeChar(int index)
    {
        var code = DisplayCode;
        return index < code.Length ? code[index].ToString() : string.Empty;
    }

    private void NotifyCellPropertiesChanged()
    {
        OnPropertyChanged(nameof(DisplayCode));
        OnPropertyChanged(nameof(CodeChar0));
        OnPropertyChanged(nameof(CodeChar1));
        OnPropertyChanged(nameof(CodeChar2));
        OnPropertyChanged(nameof(CodeChar3));
        OnPropertyChanged(nameof(CodeChar4));
        OnPropertyChanged(nameof(CodeChar5));

        OnPropertyChanged(nameof(IsCellFilled0));
        OnPropertyChanged(nameof(IsCellFilled1));
        OnPropertyChanged(nameof(IsCellFilled2));
        OnPropertyChanged(nameof(IsCellFilled3));
        OnPropertyChanged(nameof(IsCellFilled4));
        OnPropertyChanged(nameof(IsCellFilled5));

        OnPropertyChanged(nameof(IsCellCurrent0));
        OnPropertyChanged(nameof(IsCellCurrent1));
        OnPropertyChanged(nameof(IsCellCurrent2));
        OnPropertyChanged(nameof(IsCellCurrent3));
        OnPropertyChanged(nameof(IsCellCurrent4));
        OnPropertyChanged(nameof(IsCellCurrent5));
    }

    public bool IsGuestJoined
    {
        get => _isGuestJoined;
        private set
        {
            if (SetProperty(ref _isGuestJoined, value))
            {
                RaiseAllCommands();
            }
        }
    }

    public string GuestStatusText
    {
        get => _guestStatusText;
        internal set { if (SetProperty(ref _guestStatusText, value)) NotifyStatusStateChanged(); }
    }

    public bool CanGuestConnect
    {
        get => _canGuestConnect;
        private set
        {
            if (SetProperty(ref _canGuestConnect, value))
            {
                RaiseAllCommands();
                NotifyStatusStateChanged();
            }
        }
    }

    public bool IsStatusOpen => IsInLobby && (IsHost ? IsWorldOpen : CanGuestConnect);
    public bool IsStatusWaiting => IsInLobby && !IsStatusOpen;

    public string CombinedStatusText
    {
        get
        {
            if (!IsInLobby) return StatusText;
            if (IsHost) return HostStatusText;
            return GuestStatusText;
        }
    }

    private void NotifyStatusStateChanged()
    {
        OnPropertyChanged(nameof(IsStatusOpen));
        OnPropertyChanged(nameof(IsStatusWaiting));
        OnPropertyChanged(nameof(CombinedStatusText));
        OnPropertyChanged(nameof(IsHost));
        RaiseAllCommands();
    }

    public async Task<string?> CreateLobbyAsync()
    {
        await _lobbyOpLock.WaitAsync();
        try
        {
            if (IsInLobby && IsHost && !string.IsNullOrWhiteSpace(LobbyCode))
            {
                return LobbyCode;
            }

            if (IsInLobby && !IsHost)
            {
                _lobbyService.LeaveLobby();
            }

            int myGen = Interlocked.Increment(ref _lobbyOpGeneration);
            IsBusy = true;
            StatusText = "Создание лобби...";
            StatusIcon = string.Empty;
            ShowTunnelFailedLogButton = false;
            TunnelFailureReason = null;

            try
            {
                if (_lobbyService is LobbyService || _lobbyApiClient != null || _tunnelProvider is PlayitTunnelProvider)
                {
                    PlayitTunnelProvider.PurgeLegacyLocalSecrets();

                    var playit = _tunnelProvider as PlayitTunnelProvider;
                    if (playit == null)
                    {
                        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                        var toolsDir = System.IO.Path.Combine(appData, ".aura", "tools");
                        playit = new PlayitTunnelProvider(null, toolsDir);
                    }

                    StatusText = "Получение конфигурации сервера...";
                    PlayitTunnelProvider.LogTunnel("Requesting /api/tunnel-config from lobby-api...");

                    var client = _lobbyApiClient ?? new LobbyApiClient(null, _configService.CurrentConfig?.LobbyApiBaseUrl ?? "https://lobby-api.vercel.app", _configService);
                    var cfg = await client.GetTunnelConfigAsync();

                    if (cfg != null && !string.IsNullOrWhiteSpace(cfg.Secret))
                    {
                        PlayitTunnelProvider.LogTunnel("Tunnel configuration received from /api/tunnel-config.");
                        playit.SetSecret(cfg.Secret);
                        if (!string.IsNullOrWhiteSpace(cfg.PublicAddress)) playit.PublicHost = cfg.PublicAddress;
                        if (cfg.PublicPort.HasValue) playit.PublicPort = cfg.PublicPort.Value;
                    }
                    else
                    {
                        PlayitTunnelProvider.LogTunnel("[ERROR] Failed to fetch tunnel configuration from server (GET /api/tunnel-config).");
                        StatusText = "Ошибка получения конфигурации туннеля";
                        StatusIcon = string.Empty;
                        ShowTunnelFailedLogButton = true;
                        TunnelFailureReason = "Не удалось получить конфигурацию туннеля с сервера (GET /api/tunnel-config).";
                        return null;
                    }
                }

                var hostName = _configService.CurrentConfig?.Nickname ?? "Player";

                _ = EnsureSkinUploadedAsync();

                var code = await _lobbyService.CreateLobbyAsHostAsync(hostName);

                if (_lobbyOpGeneration != myGen)
                {
                    return null;
                }

                if (!string.IsNullOrWhiteSpace(code))
                {
                    IsGuestJoined = false;
                    CanGuestConnect = false;
                    IsWorldOpen = false;
                    LobbyCode = code;
                    HostName = hostName;
                    IsLobbyCreated = true;
                    IsInLobby = true;
                    HostStatusText = "Ожидание мира...";
                    StatusText = "Лобби создано. Отправь код другу и нажми «Открыть мир»";
                    StatusIcon = string.Empty;

                    ImageSource? hostAvatar = _skinService?.ExtractHeadAvatar(_configService.CurrentConfig?.SkinPath);
                    var hostModel3D = BuildLocalPlayerModel3D();
                    RefreshLocalPlayerModel();
                    _knownPlayerNicks.Clear();
                    _knownPlayerNicks.Add(hostName);
                    _discordRpcService?.SetInLobby(1);
                    Dispatch(() =>
                    {
                        LobbyPlayers.Clear();
                        LobbyPlayers.Add(new LobbyPlayerItem
                        {
                            Nickname = hostName,
                            IsHost = true,
                            Avatar = hostAvatar ?? SkinService.LoadDefaultSteveBitmap(),
                            PlayerModel3D = hostModel3D
                        });
                        UpdateStagePositions();
                    });
                    LobbyCreated?.Invoke();
                    return code;
                }
                else
                {
                    StatusText = "Не удалось создать лобби. Проверь lobby-api сервер";
                    StatusIcon = string.Empty;
                    ShowTunnelFailedLogButton = true;
                    TunnelFailureReason = "Сервер лобби вернул ошибку при создании лобби.";
                    return null;
                }
            }
            catch (Exception ex)
            {
                PlayitTunnelProvider.LogTunnel($"[CREATE-LOBBY: ERROR] {ex.Message}");
                StatusText = $"Ошибка: {ex.Message}";
                StatusIcon = string.Empty;
                ShowTunnelFailedLogButton = true;
                TunnelFailureReason = ex.Message;
                return null;
            }
            finally
            {
                IsBusy = false;
            }
        }
        finally
        {
            _lobbyOpLock.Release();
        }
    }

    /// <summary>
    /// Хост нажимает «ОТКРЫТЬ МИР»: если игра не запущена, запускает клиент игры хоста,
    /// запускает наблюдение за latest.log и ожидает открытия LAN-мира ("Started serving on N").
    /// Если включен демо-режим UseFakeTunnel, открывает мир немедленно на порту 25565.
    /// </summary>
    private async Task OpenWorldAsHostAsync()
    {
        IsBusy = true;
        HostStatusText = "Запуск мира...";

        try
        {
            bool isFakeTunnel = string.Equals(Environment.GetEnvironmentVariable("AURA_FAKE_TUNNEL"), "1", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(Environment.GetEnvironmentVariable("UseFakeTunnel"), "true", StringComparison.OrdinalIgnoreCase);

            if (isFakeTunnel)
            {
                var success = await _lobbyService.HostOpenWorldAsync(localPort: 25565);
                if (success)
                {
                    IsWorldOpen = true;
                    HostStatusText = "Лобби открыто!";
                    StatusText = "Мир открыт, друзья могут подключиться";
                    StatusIcon = string.Empty;
                }
                return;
            }

            // Реальный режим: создаём быстрый авто-бэкап свежего мира перед хостингом и запускаем watcher на latest.log
            var gameDir = _launchService.ResolveMinecraftDirectory(_configService.CurrentConfig.GameDir);
            if (_workshopService != null && !_launchService.IsGameRunning)
            {
                HostStatusText = "Резервная копия мира...";
                await _workshopService.CreateAutoBackupLatestWorldAsync(gameDir);
            }

            var logPath = System.IO.Path.Combine(gameDir, "logs", "latest.log");
            
            _worldWatcher.Start(logPath);

            HostStatusText = "Запуск игры...";
            StatusText = "Подготовка и запуск Minecraft...";
            StatusIcon = string.Empty;

            // Запускаем игру хоста через MainViewModel
            HostLaunchRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            HostStatusText = "Ошибка";
            StatusText = $"Ошибка: {ex.Message}";
            StatusIcon = string.Empty;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void OnLanWorldOpened(int port)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                if (port != 25565)
                {
                    var msg = $"Мир открыт на порту {port}, а нужен 25565";
                    PlayitTunnelProvider.LogTunnel($"[LOBBY: ERROR] {msg}");
                    Dispatch(() =>
                    {
                        HostStatusText = $"Ошибка: порт {port}";
                        StatusText = msg;
                        StatusIcon = string.Empty;
                        ShowTunnelFailedLogButton = true;
                        TunnelFailureReason = msg;
                    });
                    return;
                }

                // Проверяем, что java реально слушает 25565
                bool javaListening = false;
                for (int attempt = 0; attempt < 5; attempt++)
                {
                    javaListening = await PlayitTunnelProvider.TestTcpConnectAsync("127.0.0.1", 25565, 1000, CancellationToken.None);
                    if (javaListening) break;
                    await Task.Delay(300);
                }

                if (!javaListening)
                {
                    var msg = "Игра не слушает локальный порт 25565";
                    PlayitTunnelProvider.LogTunnel($"[LOBBY: ERROR] {msg} (TCP connect failed).");
                    Dispatch(() =>
                    {
                        HostStatusText = "Порт 25565 недоступен";
                        StatusText = msg;
                        StatusIcon = string.Empty;
                        ShowTunnelFailedLogButton = true;
                        TunnelFailureReason = msg;
                    });
                    return;
                }

                Dispatch(() =>
                {
                    HostStatusText = "Мир открыт на порту 25565! Подключение туннеля...";
                });

                var success = await _lobbyService.HostOpenWorldAsync(localPort: 25565);
                if (success)
                {
                    Dispatch(() =>
                    {
                        IsWorldOpen = true;
                        HostStatusText = "Лобби открыто!";
                        StatusText = "Мир готов, друзья могут подключаться";
                        StatusIcon = string.Empty;
                        ShowTunnelFailedLogButton = false;
                        TunnelFailureReason = null;
                    });
                }
                else
                {
                    var reason = _tunnelProvider?.CurrentInfo.ErrorMessage ?? "Туннель playit не смог запуститься или подтвердить подключение.";
                    PlayitTunnelProvider.LogTunnel($"[LOBBY: ERROR] HostOpenWorldAsync returned false: {reason}");
                    Dispatch(() =>
                    {
                        HostStatusText = "Ошибка туннеля";
                        StatusText = $"Туннель не поднялся: {reason}";
                        StatusIcon = string.Empty;
                        ShowTunnelFailedLogButton = true;
                        TunnelFailureReason = reason;
                    });
                }
            }
            catch (Exception ex)
            {
                PlayitTunnelProvider.LogTunnel($"[EXCEPTION] OnLanWorldOpened failed: {ex.GetType().FullName}: {ex.Message}\n{ex.StackTrace}");
                Dispatch(() =>
                {
                    HostStatusText = "Ошибка туннеля";
                    StatusText = $"Ошибка туннеля: {ex.Message}";
                    StatusIcon = string.Empty;
                    ShowTunnelFailedLogButton = true;
                    TunnelFailureReason = ex.Message;
                });
            }
        });
    }

    private void OnLanWorldClosed()
    {
        if (!_lobbyService.IsHost || !IsInLobby) return;

        PlayitTunnelProvider.LogTunnel("[LOBBY] World closed detected in latest.log. Resetting lobby to waiting/closed.");
        _ = Task.Run(async () =>
        {
            try
            {
                await _lobbyService.CloseLobbyAsHostAsync();
            }
            catch (Exception ex)
            {
                PlayitTunnelProvider.LogTunnel($"[EXCEPTION] OnLanWorldClosed CloseLobby: {ex.Message}");
            }
            finally
            {
                Dispatch(() =>
                {
                    LeaveLobby();
                    StatusText = "Мир был закрыт хостом. Лобби закрыто.";
                    StatusIcon = string.Empty;
                });
            }
        });
    }

    public void OnGameExited()
    {
        _worldWatcher.Stop();
        if (_lobbyService.IsHost && IsInLobby)
        {
            PlayitTunnelProvider.LogTunnel("[LOBBY] Game process exited. Closing lobby as host.");
            _ = Task.Run(async () =>
            {
                try
                {
                    await _lobbyService.CloseLobbyAsHostAsync();
                }
                catch (Exception ex)
                {
                    PlayitTunnelProvider.LogTunnel($"[EXCEPTION] OnGameExited CloseLobby: {ex.Message}");
                }
                finally
                {
                    Dispatch(() =>
                    {
                        LeaveLobby();
                        StatusText = "Игра закрыта. Лобби сброшено.";
                        StatusIcon = string.Empty;
                    });
                }
            });
        }
    }

    private void OnTunnelStatusChanged(TunnelInfo info)
    {
        if (!_lobbyService.IsHost || !IsInLobby) return;

        if (info.Status == TunnelStatus.Active && IsWorldOpen)
        {
            Dispatch(() =>
            {
                HostStatusText = "Лобби открыто!";
                StatusText = "Мир готов, друзья могут подключаться";
                StatusIcon = string.Empty;
                ShowTunnelFailedLogButton = false;
                TunnelFailureReason = null;
            });
            return;
        }

        if (info.Status == TunnelStatus.Failed)
        {
            var reason = info.ErrorMessage ?? "Процесс туннеля playit завершился с ошибкой.";
            PlayitTunnelProvider.LogTunnel($"[LOBBY: STATUS_CHANGED] Tunnel failed: {reason}. Keeping lobby open.");

            if (IsWorldOpen && _tunnelProvider != null)
            {
                Dispatch(() =>
                {
                    HostStatusText = "Переподключение туннеля...";
                    StatusText = "Связь с туннелем прервалась, переподключаем...";
                    StatusIcon = string.Empty;
                });

                _ = Task.Run(async () =>
                {
                    for (int retry = 1; retry <= 10; retry++)
                    {
                        await Task.Delay(4000);
                        if (!_lobbyService.IsHost || !IsInLobby || !IsWorldOpen || _tunnelProvider == null) return;
                        if (_tunnelProvider.CurrentInfo.Status == TunnelStatus.Active) return;

                        try
                        {
                            PlayitTunnelProvider.LogTunnel($"[LOBBY] Background tunnel reconnect attempt {retry}/10...");
                            var res = await _tunnelProvider.StartAsync(25565, CancellationToken.None);
                            if (res.Status == TunnelStatus.Active)
                            {
                                Dispatch(() =>
                                {
                                    HostStatusText = "Лобби открыто!";
                                    StatusText = "Мир готов, друзья могут подключаться";
                                    StatusIcon = string.Empty;
                                    ShowTunnelFailedLogButton = false;
                                    TunnelFailureReason = null;
                                });
                                return;
                            }
                        }
                        catch (Exception ex)
                        {
                            PlayitTunnelProvider.LogTunnel($"[EXCEPTION] Background tunnel reconnect failed: {ex.Message}");
                        }
                    }

                    Dispatch(() =>
                    {
                        HostStatusText = "Ошибка туннеля";
                        StatusText = $"Ошибка туннеля: {reason}";
                        StatusIcon = string.Empty;
                        ShowTunnelFailedLogButton = true;
                        TunnelFailureReason = reason;
                    });
                });
                return;
            }

            Dispatch(() =>
            {
                HostStatusText = "Ошибка туннеля";
                StatusText = $"Ошибка туннеля: {reason}";
                StatusIcon = string.Empty;
                ShowTunnelFailedLogButton = true;
                TunnelFailureReason = reason;
            });
        }
    }

    public async Task JoinLobbyAsync()
    {
        if (GuestCodeInput.Length < 6) return;

        await _lobbyOpLock.WaitAsync();
        try
        {
            var cleanCode = GuestCodeInput.Trim().ToUpperInvariant();
            if (cleanCode.Length < 6) return;

            if (IsInLobby && string.Equals(LobbyCode, cleanCode, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (IsInLobby)
            {
                LeaveLobby();
                GuestCodeInput = cleanCode;
            }

            int myGen = Interlocked.Increment(ref _lobbyOpGeneration);
            IsJoiningLobby = true;
            JoinErrorMessage = string.Empty;
            GuestStatusText = "Подключение к лобби...";
            StatusText = "Подключение...";
            StatusIcon = string.Empty;

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            try
            {
                if (_lobbyService is not LobbyService && _lobbyApiClient == null)
                {
                    var playerNameMock = _configService.CurrentConfig?.Nickname ?? "Player";
                    var joinedMock = await _lobbyService.JoinLobbyAsGuestAsync(cleanCode, playerNameMock, cts.Token);
                    if (_lobbyOpGeneration != myGen) return;

                    if (joinedMock)
                    {
                        LobbyCode = !string.IsNullOrWhiteSpace(_lobbyService.CurrentLobbyCode) ? _lobbyService.CurrentLobbyCode : cleanCode;
                        IsLobbyCreated = false;
                        IsWorldOpen = false;
                        IsGuestJoined = true;
                        IsInLobby = true;
                        SeedGuestLobbyPlayersIfEmpty(playerNameMock, null);
                        UpdateGuestStatus();
                    }
                    else
                    {
                        JoinErrorMessage = $"Лобби с кодом {cleanCode} не найдено. Проверьте код или попросите хоста создать новое.";
                        GuestStatusText = "Лобби не найдено";
                        StatusText = "Не удалось подключиться к лобби";
                        StatusIcon = string.Empty;
                    }
                    return;
                }

                var client = _lobbyApiClient ?? new LobbyApiClient(null, _configService.CurrentConfig?.LobbyApiBaseUrl ?? "https://lobby-api.vercel.app", _configService);

                var (statusCode, statusResp, rawJson) = await client.GetStatusDetailedAsync(cleanCode, cts.Token);
                if (_lobbyOpGeneration != myGen) return;

                PlayitTunnelProvider.LogTunnel($"[JOIN: CHECK-STATUS] GET /status?code={cleanCode} -> HTTP {(statusCode.HasValue ? (int)statusCode.Value : -1)}, body: {rawJson}");

                if (!statusCode.HasValue)
                {
                    JoinErrorMessage = "Нет связи с сервером лобби. Проверьте интернет и попробуйте снова.";
                    GuestStatusText = "Ошибка соединения";
                    StatusText = "Нет связи с сервером";
                    StatusIcon = string.Empty;
                    return;
                }

                if (statusCode == System.Net.HttpStatusCode.NotFound)
                {
                    JoinErrorMessage = $"Лобби с кодом {cleanCode} не найдено. Проверьте код или попросите хоста создать новое.";
                    GuestStatusText = "Лобби не найдено";
                    StatusText = "Лобби не найдено";
                    StatusIcon = string.Empty;
                    return;
                }

                if (statusResp != null && string.Equals(statusResp.Status, "closed", StringComparison.OrdinalIgnoreCase))
                {
                    JoinErrorMessage = "Это лобби закрыто. Попросите хоста создать новое.";
                    GuestStatusText = "Лобби закрыто";
                    StatusText = "Лобби закрыто";
                    StatusIcon = string.Empty;
                    return;
                }

                if (statusCode != System.Net.HttpStatusCode.OK)
                {
                    JoinErrorMessage = "Нет связи с сервером лобби. Проверьте интернет и попробуйте снова.";
                    GuestStatusText = "Ошибка сервера";
                    StatusText = "Ошибка сервера";
                    StatusIcon = string.Empty;
                    return;
                }

                var playerName = _configService.CurrentConfig?.Nickname ?? "Player";

                _ = EnsureSkinUploadedAsync();

                var joined = await _lobbyService.JoinLobbyAsGuestAsync(cleanCode, playerName, cts.Token);
                if (_lobbyOpGeneration != myGen) return;

                if (joined)
                {
                    LobbyCode = !string.IsNullOrWhiteSpace(_lobbyService.CurrentLobbyCode) ? _lobbyService.CurrentLobbyCode : cleanCode;
                    HostName = statusResp?.HostName;
                    IsLobbyCreated = false;
                    IsWorldOpen = false;
                    IsGuestJoined = true;
                    IsInLobby = true;
                    SeedGuestLobbyPlayersIfEmpty(playerName, statusResp?.HostName);
                    _discordRpcService?.SetInLobby(1);
                    UpdateGuestStatus();

                    var status = await _lobbyService.RefreshGuestStatusAsync(cts.Token);
                    if (_lobbyOpGeneration != myGen || !IsInLobby) return;

                    if (status?.ModSync != null && HasUnresolvedMismatches(status.ModSync, out var myMismatches))
                    {
                        var sig = ComputeMismatchSignature(myMismatches);
                        if (!_ignoredMismatchSignatures.Contains($"{LobbyCode}:{sig}"))
                        {
                            ShowMismatchDialog(myMismatches, isJoinMode: true, onProceed: null);
                        }
                    }
                }
                else
                {
                    JoinErrorMessage = $"Лобби с кодом {cleanCode} не найдено. Проверьте код или попросите хоста создать новое.";
                    GuestStatusText = "Лобби не найдено или код неверный";
                    StatusText = "Не удалось подключиться к лобби";
                    StatusIcon = string.Empty;
                }
            }
            catch (OperationCanceledException)
            {
                JoinErrorMessage = "Нет связи с сервером лобби. Проверьте интернет и попробуйте снова.";
                GuestStatusText = "Таймаут подключения";
                StatusText = "Таймаут подключения";
                StatusIcon = string.Empty;
            }
            catch (Exception ex)
            {
                PlayitTunnelProvider.LogTunnel($"[JOIN: ERROR] {ex.Message}");
                JoinErrorMessage = "Нет связи с сервером лобби. Проверьте интернет и попробуйте снова.";
                GuestStatusText = "Ошибка подключения";
                StatusText = $"Ошибка: {ex.Message}";
                StatusIcon = string.Empty;
            }
            finally
            {
                IsJoiningLobby = false;
            }
        }
        finally
        {
            _lobbyOpLock.Release();
        }
    }

    private void SeedGuestLobbyPlayersIfEmpty(string guestNick, string? hostNick)
    {
        Dispatch(() =>
        {
            if (LobbyPlayers.Count > 0) return;

            if (!string.IsNullOrWhiteSpace(hostNick) && !string.Equals(hostNick, guestNick, StringComparison.OrdinalIgnoreCase))
            {
                LobbyPlayers.Add(new LobbyPlayerItem
                {
                    Nickname = hostNick,
                    IsHost = true,
                    Avatar = SkinService.LoadDefaultSteveBitmap(),
                    PlayerModel3D = LobbyPlayerItem.DefaultSteveModel3D
                });
            }

            ImageSource? guestAvatar = _skinService?.ExtractHeadAvatar(_configService.CurrentConfig?.SkinPath);
            var guestModel3D = BuildLocalPlayerModel3D();
            LobbyPlayers.Add(new LobbyPlayerItem
            {
                Nickname = guestNick,
                IsHost = false,
                Avatar = guestAvatar ?? SkinService.LoadDefaultSteveBitmap(),
                PlayerModel3D = guestModel3D
            });
            UpdateStagePositions();
        });
    }

    public async Task<bool> JoinByCodeAsync(string code, bool fromInvite = false)
    {
        if (string.IsNullOrWhiteSpace(code)) return false;
        WasInvited = fromInvite;
        GuestCodeInput = code.Trim().ToUpperInvariant();
        await JoinLobbyAsync();
        if (IsInLobby && IsStatusOpen && WasInvited && _configService.CurrentConfig.AutoConnectOnInviteAccept)
        {
            _ = ConnectToGameAsync();
        }
        return IsInLobby;
    }

    private async Task ConnectToGameAsync()
    {
        var address = _lobbyService.CurrentTunnelAddress;
        if (string.IsNullOrWhiteSpace(address))
        {
            address = _lastTunnelAddress;
        }

        if (string.IsNullOrWhiteSpace(address))
        {
            StatusText = "Адрес сервера не получен";
            return;
        }

        if (_lobbyService.CurrentModSync != null && HasUnresolvedMismatches(_lobbyService.CurrentModSync, out var myMismatches))
        {
            var sig = ComputeMismatchSignature(myMismatches);
            if (!_ignoredMismatchSignatures.Contains($"{LobbyCode}:{sig}"))
            {
                ShowMismatchDialog(myMismatches, isJoinMode: true, onProceed: () => ExecuteConnectToGameAsync(address));
                return;
            }
        }

        await ExecuteConnectToGameAsync(address);
    }

    private async Task ExecuteConnectToGameAsync(string address)
    {
        _lastTunnelAddress = address;
        IsBusy = true;
        StatusText = "Запуск игры...";
        StatusIcon = string.Empty;

        try
        {
            if (_serverListSyncService != null)
            {
                await _serverListSyncService.UpsertActiveLobbyServerAsync(GetGameDir(), address, HostName, LobbyCode);
            }

            GuestConnectRequested?.Invoke(this, address);

            StatusText = "Игра запускается с подключением к серверу...";
            StatusIcon = string.Empty;
        }
        catch (Exception ex)
        {
            StatusText = $"Ошибка запуска: {ex.Message}";
            StatusIcon = string.Empty;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private Task ReconnectAsync()
    {
        Reconnected?.Invoke();
        return ConnectToGameAsync();
    }

    /// <summary>
    /// Событие, которое MainViewModel слушает для запуска игры с quickPlayMultiplayer.
    /// </summary>
    public event EventHandler<string>? GuestConnectRequested;

    /// <summary>
    /// Событие, которое MainViewModel слушает для запуска игры хоста.
    public event EventHandler? HostLaunchRequested;

    public void OnHostGameStarted()
    {
        Dispatch(() =>
        {
            if (IsHost && !IsWorldOpen)
            {
                HostStatusText = "Ожидание открытия мира...";
                StatusText = "Игра запущена. Открой мир для сети (Esc → Открыть для сети)";
                StatusIcon = string.Empty;
            }
        });
    }

    public void NotifyLaunchFailed(string errorMessage)
    {
        Dispatch(() =>
        {
            _worldWatcher.Stop();
            HostStatusText = "Ошибка запуска";
            StatusText = string.IsNullOrWhiteSpace(errorMessage) ? "Не удалось запустить игру" : errorMessage;
            StatusIcon = string.Empty;
            ShowTunnelFailedLogButton = true;
            TunnelFailureReason = StatusText;
        });
    }

    private void CopyCode()
    {
        if (!string.IsNullOrWhiteSpace(LobbyCode))
        {
            try
            {
                Clipboard.SetText(LobbyCode);
                CodeCopied = true;

                _ = Task.Delay(1600).ContinueWith(_ =>
                {
                    Application.Current?.Dispatcher?.InvokeAsync(() => CodeCopied = false);
                });
            }
            catch { }
        }
    }

    private void CopyLobbyLink()
    {
        if (!string.IsNullOrWhiteSpace(LobbyCode))
        {
            try
            {
                string url = $"https://lobby-api.vercel.app/j/{LobbyCode}";
                Clipboard.SetText(url);
                IsLinkCopied = true;
                _ = Task.Delay(2000).ContinueWith(_ =>
                {
                    Application.Current?.Dispatcher?.InvokeAsync(() => IsLinkCopied = false);
                });
            }
            catch (Exception ex)
            {
                PlayitTunnelProvider.LogTunnel($"[UI: ERROR] Failed to copy lobby link: {ex.Message}");
            }
        }
    }

    private void LeaveLobby()
    {
        Interlocked.Increment(ref _lobbyOpGeneration);

        if (_lobbyService.IsHost)
        {
            _ = _lobbyService.CloseLobbyAsHostAsync();
        }
        else
        {
            _lobbyService.LeaveLobby();
        }

        if (_serverListSyncService != null)
        {
            _ = _serverListSyncService.RemoveActiveLobbyServerAsync(GetGameDir());
        }

        IsLobbyCreated = false;
        IsWorldOpen = false;
        IsGuestJoined = false;
        CanGuestConnect = false;
        LobbyCode = null;
        HostName = null;
        IsInLobby = false;
        GuestCodeInput = string.Empty;
        CodeCopied = false;
        HostStatusText = "Ожидание мира...";
        GuestStatusText = "Введите 6-значный код лобби";
        StatusText = "Создайте лобби или введите код друга";
        StatusIcon = string.Empty;

        _discordRpcService?.SetInLauncher();
        _knownPlayerNicks.Clear();
        _previousWorldIsOpen = false;

        LobbyPlayers.Clear();
        _lastPlayerMismatchSignatures.Clear();
        _ignoredMismatchSignatures.Clear();
        _lastSelfMismatchSignature = string.Empty;
        _syncNoticeCts?.Cancel();
        IsSyncNoticeVisible = false;
        IsModMismatchDialogVisible = false;
        JoinErrorMessage = string.Empty;
        IsJoiningLobby = false;
        OnPropertyChanged(nameof(IsHost));
    }

    // Обработчики событий LobbyService

    private void Dispatch(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.InvokeAsync(action);
        }
    }

    private void OnLobbyStatusChanged(string newStatus)
    {
        Dispatch(() =>
        {
            if (_lobbyService.IsHost)
            {
                switch (newStatus.ToLowerInvariant())
                {
                    case "waiting":
                        HostStatusText = "Ожидание мира...";
                        break;
                    case "open":
                        HostStatusText = "Лобби открыто!";
                        IsWorldOpen = true;
                        StatusIcon = string.Empty;
                        break;
                    case "closed":
                        HostStatusText = "Лобби закрыто";
                        LeaveLobby();
                        break;
                }
            }
            else
            {
                UpdateGuestStatus();
                if (string.Equals(newStatus, "closed", StringComparison.OrdinalIgnoreCase))
                {
                    _ = Task.Delay(2000).ContinueWith(_ => Dispatch(LeaveLobby));
                }
            }
        });
    }

    private void OnTunnelAddressReady(string tunnelAddress)
    {
        if (!string.IsNullOrWhiteSpace(tunnelAddress))
        {
            _lastTunnelAddress = tunnelAddress;
            if (_serverListSyncService != null)
            {
                _ = _serverListSyncService.UpsertActiveLobbyServerAsync(GetGameDir(), tunnelAddress, HostName, LobbyCode);
            }
        }

        Dispatch(() =>
        {
            if (!_lobbyService.IsHost)
            {
                CanGuestConnect = true;
                GuestStatusText = "Хост открыл мир, можно подключаться";
                StatusText = "Мир готов! Нажми «Подключиться к игре»";
                StatusIcon = string.Empty;

                if (WasInvited && _configService.CurrentConfig.AutoConnectOnInviteAccept)
                {
                    _ = ConnectToGameAsync();
                }
            }
        });
    }

    private void UpdateGuestStatus()
    {
        var status = _lobbyService.CurrentStatus?.ToLowerInvariant() ?? "idle";

        switch (status)
        {
            case "waiting":
                CanGuestConnect = false;
                GuestStatusText = "Ожидание хоста...";
                StatusText = "Ожидаем, пока хост откроет мир для сети";
                StatusIcon = string.Empty;
                break;
            case "open":
                CanGuestConnect = !string.IsNullOrWhiteSpace(_lobbyService.CurrentTunnelAddress);
                GuestStatusText = CanGuestConnect ? "Хост открыл мир, можно подключаться" : "Ожидание адреса сервера...";
                StatusText = CanGuestConnect ? "Мир готов! Нажми «Подключиться к игре»" : "Получение адреса сервера...";
                StatusIcon = string.Empty;
                break;
            case "closed":
                CanGuestConnect = false;
                GuestStatusText = "Лобби закрыто хостом";
                StatusText = "Лобби было закрыто";
                StatusIcon = string.Empty;
                break;
            default:
                CanGuestConnect = false;
                GuestStatusText = "Введите 6-значный код лобби";
                StatusText = "Создайте лобби или введите код друга";
                StatusIcon = string.Empty;
                break;
        }
    }

    private void OnLobbyStatusUpdated(LobbyStatusResponse status)
    {
        if (IsInLobby && status.Players != null && status.Players.Length > 0)
        {
            _discordRpcService?.SetInLobby(status.Players.Length);
        }

        if (!string.IsNullOrWhiteSpace(status.TunnelAddress) && string.Equals(status.Status, "open", StringComparison.OrdinalIgnoreCase))
        {
            _lastTunnelAddress = status.TunnelAddress;
            if (_serverListSyncService != null)
            {
                _ = _serverListSyncService.UpsertActiveLobbyServerAsync(GetGameDir(), status.TunnelAddress, status.HostName ?? HostName, LobbyCode);
            }
        }

        if (_lobbyService.IsHost && status.Players != null)
        {
            var myNick = _configService.CurrentConfig?.Nickname ?? string.Empty;
            foreach (var p in status.Players)
            {
                if (!string.Equals(p, myNick, StringComparison.OrdinalIgnoreCase) && _knownPlayerNicks.Add(p))
                {
                    _notificationService?.NotifyPlayerJoinedLobby(p);
                }
            }
        }

        if (!_lobbyService.IsHost && string.Equals(status.Status, "open", StringComparison.OrdinalIgnoreCase))
        {
            if (!_previousWorldIsOpen)
            {
                _previousWorldIsOpen = true;
                _notificationService?.NotifyHostOpenedWorld();
            }
        }
        else if (string.Equals(status.Status, "waiting", StringComparison.OrdinalIgnoreCase))
        {
            _previousWorldIsOpen = false;
        }

        _ = RefreshLobbyPlayersAsync(status.Players, status.HostName, status.ModSync);
    }

    public async Task RefreshLobbyPlayersAsync(string[]? players, string? hostName, IReadOnlyDictionary<string, PlayerModSyncInfo>? modSync = null)
    {
        if (players == null) return;
        if (!string.IsNullOrWhiteSpace(hostName))
        {
            HostName = hostName;
        }

        // Build desired player descriptors
        var myNick = _configService.CurrentConfig?.Nickname ?? string.Empty;
        var incomingPlayers = new List<(string Nick, bool IsHost, ImageSource Avatar, System.Windows.Media.Media3D.Model3D Model3D)>();
        foreach (var player in players)
        {
            // Игрок является хостом, если его ник совпадает с hostName сервера.
            // Если hostName не передан, то только если текущий клиент является хостом и это его ник.
            bool isHost;
            if (!string.IsNullOrWhiteSpace(hostName))
            {
                isHost = string.Equals(player, hostName, StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                isHost = _lobbyService.IsHost && string.Equals(player, myNick, StringComparison.OrdinalIgnoreCase);
            }

            ImageSource avatar;
            System.Windows.Media.Media3D.Model3D model3D;

            if (string.Equals(player, myNick, StringComparison.OrdinalIgnoreCase))
            {
                avatar = _skinService?.ExtractHeadAvatar(_configService.CurrentConfig?.SkinPath) ?? SkinService.LoadDefaultSteveBitmap();
                model3D = BuildLocalPlayerModel3D();
            }
            else if (_skinService != null)
            {
                avatar = await _skinService.GetAvatarForPlayerAsync(player);
                var (skinTex, isSlim) = await _skinService.GetSkinTextureForPlayerAsync(player);
                var skinBmp = skinTex as System.Windows.Media.Imaging.BitmapSource ?? SkinService.LoadDefaultSteveBitmap();
                model3D = SkinModel3DBuilder.BuildPlayerModel(skinBmp, isSlim);
            }
            else
            {
                avatar = SkinService.LoadDefaultSteveBitmap();
                model3D = LobbyPlayerItem.DefaultSteveModel3D;
            }

            incomingPlayers.Add((player, isHost, avatar, model3D));
        }

        Dispatch(() =>
        {
            // 1. Remove players that are no longer in incoming list
            var desiredSet = new HashSet<string>(incomingPlayers.Select(p => p.Nick), StringComparer.OrdinalIgnoreCase);
            for (int i = LobbyPlayers.Count - 1; i >= 0; i--)
            {
                if (!desiredSet.Contains(LobbyPlayers[i].Nickname))
                {
                    LobbyPlayers.RemoveAt(i);
                }
            }

            // 2. Add or update players in-place
            for (int i = 0; i < incomingPlayers.Count; i++)
            {
                var incoming = incomingPlayers[i];
                var existingIndex = -1;
                for (int j = 0; j < LobbyPlayers.Count; j++)
                {
                    if (string.Equals(LobbyPlayers[j].Nickname, incoming.Nick, StringComparison.OrdinalIgnoreCase))
                    {
                        existingIndex = j;
                        break;
                    }
                }

                if (existingIndex >= 0)
                {
                    // Existing player: update properties in-place without triggering new slide-in animation
                    var existingItem = LobbyPlayers[existingIndex];
                    existingItem.IsHost = incoming.IsHost;
                    if (incoming.Avatar != null && existingItem.Avatar != incoming.Avatar)
                    {
                        existingItem.Avatar = incoming.Avatar;
                    }
                    if (incoming.Model3D != null && !ReferenceEquals(incoming.Model3D, LobbyPlayerItem.DefaultSteveModel3D))
                    {
                        if (!ReferenceEquals(existingItem.PlayerModel3D, incoming.Model3D))
                        {
                            existingItem.PlayerModel3D = incoming.Model3D;
                        }
                    }
                    existingItem.IsNewlyAdded = false;

                    // Ensure matching order if needed
                    if (existingIndex != i && i < LobbyPlayers.Count)
                    {
                        LobbyPlayers.Move(existingIndex, i);
                    }
                }
                else
                {
                    // Newly joined player: marked as IsNewlyAdded so slide-in animation fires
                    var newItem = new LobbyPlayerItem
                    {
                        Nickname = incoming.Nick,
                        IsHost = incoming.IsHost,
                        Avatar = incoming.Avatar,
                        PlayerModel3D = incoming.Model3D,
                        IsNewlyAdded = true
                    };

                    if (i < LobbyPlayers.Count)
                    {
                        LobbyPlayers.Insert(i, newItem);
                    }
                    else
                    {
                        LobbyPlayers.Add(newItem);
                    }
                }
            }

            UpdateStagePositions();
            UpdatePlayerModSync(modSync ?? _lobbyService.CurrentModSync);
            PlayerCountChanged?.Invoke(LobbyPlayers.Count);
        });
    }

    public void RefreshLocalPlayerModel()
    {
        try
        {
            LocalPlayerModel3D = BuildLocalPlayerModel3D();
            OnPropertyChanged(nameof(LocalPlayerNickname));
        }
        catch { }
    }

    private System.Windows.Media.Media3D.Model3D BuildLocalPlayerModel3D()
    {
        try
        {
            var cfg = _configService.CurrentConfig;
            bool isSlim = string.Equals(cfg?.SkinModel, "slim", StringComparison.OrdinalIgnoreCase);
            var skinBmp = _skinService?.LoadSkinImage(cfg?.SkinPath) as System.Windows.Media.Imaging.BitmapSource
                          ?? SkinService.LoadDefaultSteveBitmap();
            return SkinModel3DBuilder.BuildPlayerModel(skinBmp, isSlim);
        }
        catch
        {
            return LobbyPlayerItem.DefaultSteveModel3D;
        }
    }

    private void OnLobbyPlayersCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (var oldObj in e.OldItems)
            {
                if (oldObj is LobbyPlayerItem oldItem)
                {
                    oldItem.PropertyChanged -= OnLobbyPlayerItemPropertyChanged;
                }
            }
        }

        if (e.NewItems != null)
        {
            var myNick = _configService.CurrentConfig?.Nickname ?? string.Empty;
            foreach (var newObj in e.NewItems)
            {
                if (newObj is LobbyPlayerItem newItem)
                {
                    newItem.PropertyChanged -= OnLobbyPlayerItemPropertyChanged;
                    newItem.PropertyChanged += OnLobbyPlayerItemPropertyChanged;

                    if (!newItem.HasCustomModel3D)
                    {
                        if (string.Equals(newItem.Nickname, myNick, StringComparison.OrdinalIgnoreCase))
                        {
                            newItem.PlayerModel3D = BuildLocalPlayerModel3D();
                        }
                        else if (_skinService != null && !string.IsNullOrWhiteSpace(newItem.Nickname))
                        {
                            var nick = newItem.Nickname;
                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    var (skinTex, isSlim) = await _skinService.GetSkinTextureForPlayerAsync(nick);
                                    var bmp = skinTex as System.Windows.Media.Imaging.BitmapSource ?? SkinService.LoadDefaultSteveBitmap();
                                    var model = SkinModel3DBuilder.BuildPlayerModel(bmp, isSlim);
                                    Dispatch(() => newItem.PlayerModel3D = model);
                                }
                                catch { }
                            });
                        }
                    }
                }
            }
        }

        UpdateStagePositions();
    }

    private void OnLobbyPlayerItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LobbyPlayerItem.IsHost))
        {
            UpdateStagePositions();
        }
    }

    /// <summary>
    /// Раскладка персонажей на сцене лобби по принципу CS2 / Brawl Stars:
    /// - Хост всегда стоит ровно по центру (StageOffsetX = 0, масштаб 1.0, передний план).
    /// - Друзья встают поочерёдно справа и слева от центра:
    ///   1-й друг -> справа от центра (+1),
    ///   2-й друг -> слева от центра (-1),
    ///   3-й друг -> справа дальше (+2),
    ///   4-й друг -> слева дальше (-2).
    /// </summary>
    public void UpdateStagePositions()
    {
        if (LobbyPlayers.Count == 0) return;

        var host = LobbyPlayers.FirstOrDefault(p => p.IsHost) ?? LobbyPlayers[0];
        host.StageSlotIndex = 0;
        host.StageOffsetX = 0.0;
        host.StageOffsetY = 0.0;
        host.StageScale = 1.0;
        host.StageYawAngle = -8.0;
        host.StageZIndex = 10;

        var guests = LobbyPlayers.Where(p => !ReferenceEquals(p, host)).ToList();
        int maxRank = (guests.Count + 1) / 2;

        for (int i = 0; i < guests.Count; i++)
        {
            var guest = guests[i];
            int rank = (i / 2) + 1;
            int direction = (i % 2 == 0) ? 1 : -1;
            guest.StageSlotIndex = direction * rank;

            double offsetX;
            if (maxRank <= 2)
            {
                offsetX = rank == 1 ? direction * 124.0 : direction * 228.0;
            }
            else
            {
                double step = 228.0 / maxRank;
                offsetX = direction * rank * step;
            }

            guest.StageOffsetX = offsetX;
            guest.StageOffsetY = rank == 1 ? -10.0 : -18.0;
            guest.StageScale = rank == 1 ? 0.90 : 0.82;
            guest.StageYawAngle = direction > 0 ? -(14.0 + rank * 4.0) : (14.0 + rank * 4.0);
            guest.StageZIndex = Math.Max(1, 10 - rank * 2);
        }
    }

    private void RaiseAllCommands()
    {
        OnPropertyChanged(nameof(CanReconnect));
        CreateLobbyCommand.RaiseCanExecuteChanged();
        JoinLobbyCommand.RaiseCanExecuteChanged();
        ConnectToGameCommand.RaiseCanExecuteChanged();
        ReconnectCommand?.RaiseCanExecuteChanged();
        OpenWorldCommand.RaiseCanExecuteChanged();
        LeaveLobbyCommand.RaiseCanExecuteChanged();
        CopyCodeCommand.RaiseCanExecuteChanged();
        CopyLinkCommand.RaiseCanExecuteChanged();
        CopyTunnelLogCommand.RaiseCanExecuteChanged();
        FixAndProceedCommand?.RaiseCanExecuteChanged();
    }

    private async Task EnsureSkinUploadedAsync()
    {
        if (_skinService == null) return;
        try
        {
            var cfg = _configService.CurrentConfig;
            var res = await _skinService.UploadSkinToLobbyApiAsync(
                cfg.SkinPath,
                cfg.Nickname,
                cfg.SkinModel,
                cfg.SkinOwnerToken,
                cfg.LobbyApiBaseUrl);

            if (res.Success && !string.IsNullOrWhiteSpace(res.OwnerToken))
            {
                cfg.SkinOwnerToken = res.OwnerToken;
                await _configService.SaveConfigAsync(cfg);
            }
        }
        catch { }
    }

    // Методы проверки и синхронизации модов

    public async Task UpdateManifestAsync()
    {
        if (IsInLobby)
        {
            await _lobbyService.UpdateManifestAsync();
        }
    }

    private string GetGameDir() => _launchService.ResolveMinecraftDirectory(_configService.CurrentConfig.GameDir);

    public void ShowSyncNotice(string text)
    {
        _syncNoticeCts?.Cancel();
        _syncNoticeCts = new CancellationTokenSource();
        var ct = _syncNoticeCts.Token;
        SyncNoticeText = text;
        IsSyncNoticeVisible = true;
        _ = Task.Delay(6000, ct).ContinueWith(t =>
        {
            if (!t.IsCanceled)
            {
                Dispatch(() => IsSyncNoticeVisible = false);
            }
        }, ct);
    }

    private void OnModSyncUpdated(IReadOnlyDictionary<string, PlayerModSyncInfo> modSync)
    {
        Dispatch(() =>
        {
            UpdatePlayerModSync(modSync);
        });
    }

    private void UpdatePlayerModSync(IReadOnlyDictionary<string, PlayerModSyncInfo>? modSync)
    {
        if (modSync == null) return;

        foreach (var p in LobbyPlayers)
        {
            PlayerModSyncInfo? info = null;
            foreach (var kvp in modSync)
            {
                if (string.Equals(kvp.Key, p.Nickname, StringComparison.OrdinalIgnoreCase))
                {
                    info = kvp.Value;
                    break;
                }
            }

            if (info != null)
            {
                p.ModSyncStatus = info.Status;
                p.Mismatches = info.Mismatches ?? new();
                p.MismatchCount = p.Mismatches.Count;
                p.ModSyncTooltip = BuildModSyncTooltip(p.Nickname, info);
                p.OpenMismatchDialogCommand = new RelayCommand(_ => OpenPlayerMismatchDialog(p));

                string newSig = info.Status switch
                {
                    "synced" => "synced",
                    "mismatch" => "mismatch:" + ComputeMismatchSignature(info.Mismatches),
                    _ => "unverified"
                };

                if (_lastPlayerMismatchSignatures.TryGetValue(p.Nickname, out var oldSig))
                {
                    if (oldSig != newSig)
                    {
                        if (newSig == "synced" && oldSig.StartsWith("mismatch:"))
                        {
                            ShowSyncNotice($"У {p.Nickname} моды совпадают");
                        }
                        else if (newSig.StartsWith("mismatch:"))
                        {
                            var first = info.Mismatches.FirstOrDefault();
                            if (first != null)
                            {
                                string action = first.Type switch
                                {
                                    "disabled" => "выключен мод",
                                    "missing" => "нет мода",
                                    "extra" => "лишний мод",
                                    "version" => "другая версия мода",
                                    _ => "расходится мод"
                                };
                                string suffix = info.Mismatches.Count > 1 ? $" и ещё {info.Mismatches.Count - 1}" : "";
                                ShowSyncNotice($"У {p.Nickname} {action} «{first.ModName}»{suffix}");
                            }
                        }
                    }
                }
                _lastPlayerMismatchSignatures[p.Nickname] = newSig;
            }
            else
            {
                p.ModSyncStatus = "unverified";
                p.Mismatches = new();
                p.MismatchCount = 0;
                p.ModSyncTooltip = "Не проверен";
                p.OpenMismatchDialogCommand = null;
            }
        }

        if (!_lobbyService.IsHost && !IsModMismatchDialogVisible)
        {
            var myNick = _configService.CurrentConfig?.Nickname ?? string.Empty;
            if (modSync.TryGetValue(myNick, out var myInfo) && myInfo.Status == "mismatch" && myInfo.Mismatches != null && myInfo.Mismatches.Count > 0)
            {
                var sig = ComputeMismatchSignature(myInfo.Mismatches);
                if (!_ignoredMismatchSignatures.Contains($"{LobbyCode}:{sig}"))
                {
                    if (_lastSelfMismatchSignature != sig)
                    {
                        _lastSelfMismatchSignature = sig;
                        ShowMismatchDialog(myInfo.Mismatches, isJoinMode: false, onProceed: null);
                    }
                }
            }
            else
            {
                _lastSelfMismatchSignature = string.Empty;
            }
        }
    }

    private void OpenPlayerMismatchDialog(LobbyPlayerItem player)
    {
        if (player.Mismatches == null || player.Mismatches.Count == 0) return;

        var myNick = _configService.CurrentConfig?.Nickname ?? string.Empty;
        bool isSelf = string.Equals(player.Nickname, myNick, StringComparison.OrdinalIgnoreCase);

        if (isSelf && !_lobbyService.IsHost)
        {
            ShowMismatchDialog(player.Mismatches, isJoinMode: false, onProceed: null);
        }
        else
        {
            ShowMismatchDialog(player.Mismatches, isJoinMode: false, onProceed: null, playerNick: player.Nickname);
        }
    }

    private static string BuildModSyncTooltip(string nick, PlayerModSyncInfo info)
    {
        if (info.Status == "synced") return "Моды совпадают";
        if (info.Status == "unverified") return "Не проверен";
        if (info.Mismatches == null || info.Mismatches.Count == 0) return "Расхождений нет";

        var lines = new List<string> { $"Расхождений: {info.Mismatches.Count}" };
        foreach (var m in info.Mismatches.Take(8))
        {
            string desc = m.Type switch
            {
                "disabled" => "выключен",
                "missing" => "нет",
                "extra" => "лишний",
                "version" => $"версия ({m.Version})",
                _ => m.Type
            };
            lines.Add($"• {m.ModName}: {desc}");
        }
        if (info.Mismatches.Count > 8)
        {
            lines.Add($"… и ещё {info.Mismatches.Count - 8}");
        }
        return string.Join(Environment.NewLine, lines);
    }

    public static string ComputeMismatchSignature(IEnumerable<ModMismatchItem> items)
    {
        if (items == null) return string.Empty;
        var parts = items.OrderBy(i => i.ModId, StringComparer.OrdinalIgnoreCase)
                         .Select(i => $"{i.ModId}:{i.Type}:{i.Version}");
        return string.Join(";", parts);
    }

    private bool HasUnresolvedMismatches(IReadOnlyDictionary<string, PlayerModSyncInfo> modSync, out List<ModMismatchItem> mismatches)
    {
        mismatches = new List<ModMismatchItem>();
        if (_lobbyService.IsHost) return false;

        var myNick = _configService.CurrentConfig?.Nickname ?? string.Empty;
        foreach (var kvp in modSync)
        {
            if (string.Equals(kvp.Key, myNick, StringComparison.OrdinalIgnoreCase))
            {
                if (kvp.Value.Status == "mismatch" && kvp.Value.Mismatches != null && kvp.Value.Mismatches.Count > 0)
                {
                    mismatches = kvp.Value.Mismatches;
                    return true;
                }
                break;
            }
        }
        return false;
    }

    public void ShowMismatchDialog(List<ModMismatchItem> mismatches, bool isJoinMode, Func<Task>? onProceed = null, string? playerNick = null)
    {
        _currentMismatchItems = mismatches ?? new();
        _pendingConnectAction = onProceed;

        IsJoinDialogMode = isJoinMode;
        IsViewOnlyDialogMode = !string.IsNullOrWhiteSpace(playerNick);
        IsGameRunningWarning = _launchService?.IsGameRunning == true;

        ModMismatchItems.Clear();
        foreach (var m in _currentMismatchItems)
        {
            ModMismatchItems.Add(m);
        }

        CanFixAny = _currentMismatchItems.Any(m => m.Type == "disabled" || m.Type == "extra");
        HasUnfixableMods = _currentMismatchItems.Any(m => m.Type == "missing" || m.Type == "version");

        if (IsViewOnlyDialogMode)
        {
            ModMismatchTitle = $"Моды игрока {playerNick}";
            ModMismatchSummary = $"С хостом расходится {_currentMismatchItems.Count} {GetMismatchesPlural(_currentMismatchItems.Count)}";
        }
        else
        {
            ModMismatchTitle = "Моды не совпадают";
            if (_currentMismatchItems.Count == 1)
            {
                var single = _currentMismatchItems[0];
                if (single.Type == "disabled")
                {
                    ModMismatchSummary = $"У хоста включён мод «{single.ModName}», у вас он выключен. Включить?";
                }
                else if (single.Type == "extra")
                {
                    ModMismatchSummary = $"У вас включён лишний мод «{single.ModName}». Отключить?";
                }
                else if (single.Type == "missing")
                {
                    ModMismatchSummary = $"У хоста включён мод «{single.ModName}», у вас его нет.";
                }
                else
                {
                    ModMismatchSummary = $"У вас другая версия мода «{single.ModName}».";
                }
            }
            else
            {
                ModMismatchSummary = $"С хостом расходится {_currentMismatchItems.Count} {GetMismatchesPlural(_currentMismatchItems.Count)}";
            }
        }

        IsModMismatchDialogVisible = true;
    }

    private async Task FixAndProceedAsync()
    {
        if (_launchService?.IsGameRunning == true)
        {
            IsGameRunningWarning = true;
            return;
        }

        try
        {
            string gameDir = GetGameDir();
            _modManifestService.FixMismatches(gameDir, _currentMismatchItems);
            await _lobbyService.UpdateManifestAsync();
        }
        catch (Exception ex)
        {
            PlayitTunnelProvider.LogTunnel($"[MOD-SYNC: FIX ERROR] {ex.Message}");
        }

        IsModMismatchDialogVisible = false;

        if (IsJoinDialogMode && _pendingConnectAction != null)
        {
            var action = _pendingConnectAction;
            _pendingConnectAction = null;
            await action();
        }
    }

    private void JoinAsIs()
    {
        if (!string.IsNullOrWhiteSpace(LobbyCode))
        {
            var sig = ComputeMismatchSignature(_currentMismatchItems);
            _ignoredMismatchSignatures.Add($"{LobbyCode}:{sig}");
        }

        IsModMismatchDialogVisible = false;

        if (_pendingConnectAction != null)
        {
            var action = _pendingConnectAction;
            _pendingConnectAction = null;
            _ = action();
        }
    }

    private void CancelMismatchDialog()
    {
        if (!IsJoinDialogMode && !IsViewOnlyDialogMode && !string.IsNullOrWhiteSpace(LobbyCode))
        {
            var sig = ComputeMismatchSignature(_currentMismatchItems);
            _ignoredMismatchSignatures.Add($"{LobbyCode}:{sig}");
        }

        IsModMismatchDialogVisible = false;
        _pendingConnectAction = null;
    }

    private static string GetMismatchesPlural(int n)
    {
        int mod100 = n % 100;
        int mod10 = n % 10;
        if (mod100 >= 11 && mod100 <= 19) return "модов";
        if (mod10 == 1) return "мод";
        if (mod10 >= 2 && mod10 <= 4) return "мода";
        return "модов";
    }

    public void ShowDebugMismatchDialog(int count = 1)
    {
        var list = new List<ModMismatchItem>();
        if (count == 1)
        {
            list.Add(new ModMismatchItem { ModId = "lithium", ModName = "Lithium", Type = "disabled", Version = "0.11.2" });
        }
        else if (count == 5)
        {
            list.Add(new ModMismatchItem { ModId = "lithium", ModName = "Lithium", Type = "disabled", Version = "0.11.2" });
            list.Add(new ModMismatchItem { ModId = "sodium", ModName = "Sodium", Type = "version", Version = "0.5.8", PlayerVersion = "0.5.3", HostVersion = "0.5.8" });
            list.Add(new ModMismatchItem { ModId = "iris", ModName = "Iris Shaders", Type = "missing", Version = "1.7.0" });
            list.Add(new ModMismatchItem { ModId = "xaeros-minimap", ModName = "Xaero's Minimap", Type = "extra", Version = "23.9.7" });
            list.Add(new ModMismatchItem { ModId = "ferritecore", ModName = "FerriteCore", Type = "disabled", Version = "6.0.1" });
        }
        else
        {
            for (int i = 1; i <= count; i++)
            {
                string type = (i % 4) switch
                {
                    0 => "disabled",
                    1 => "missing",
                    2 => "extra",
                    _ => "version"
                };
                list.Add(new ModMismatchItem
                {
                    ModId = $"sample-mod-{i}",
                    ModName = $"Sample Mod {i} (Extra Long Name For Testing Ellipsis)",
                    Type = type,
                    Version = $"1.{i}.0",
                    PlayerVersion = $"1.{i}.0",
                    HostVersion = $"1.{i}.1"
                });
            }
        }

        ShowMismatchDialog(list, isJoinMode: true, onProceed: null);
    }

    public void EmulateParticipantMismatch(string? playerNick = null, int mismatchCount = 1)
    {
        string targetNick = playerNick ?? "Vetements";
        var existing = LobbyPlayers.FirstOrDefault(p => string.Equals(p.Nickname, targetNick, StringComparison.OrdinalIgnoreCase));
        if (existing == null)
        {
            existing = new LobbyPlayerItem
            {
                Nickname = targetNick,
                IsHost = false,
                Avatar = SkinService.LoadDefaultSteveBitmap()
            };
            LobbyPlayers.Add(existing);
        }

        var list = new List<ModMismatchItem>();
        for (int i = 1; i <= mismatchCount; i++)
        {
            list.Add(new ModMismatchItem
            {
                ModId = i == 1 ? "lithium" : $"mod-{i}",
                ModName = i == 1 ? "Lithium" : $"Mod {i}",
                Type = i % 2 == 1 ? "disabled" : "missing",
                Version = "1.0.0"
            });
        }

        var dict = new Dictionary<string, PlayerModSyncInfo>
        {
            [targetNick] = new PlayerModSyncInfo
            {
                Status = "mismatch",
                Mismatches = list
            }
        };

        UpdatePlayerModSync(dict);
    }
}
