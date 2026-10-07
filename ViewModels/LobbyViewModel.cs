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
    private readonly ILanWorldWatcher _worldWatcher;
    private readonly ITunnelProvider? _tunnelProvider;
    private readonly ISkinService? _skinService;
    private readonly ILobbyApiClient? _lobbyApiClient;
    private readonly INotificationService? _notificationService;
    private readonly IDiscordRpcService? _discordRpcService;
    private readonly HashSet<string> _knownPlayerNicks = new(StringComparer.OrdinalIgnoreCase);
    private bool _previousWorldIsOpen;

    // === Список игроков лобби ===
    public ObservableCollection<LobbyPlayerItem> LobbyPlayers { get; } = new();

    // === Общее состояние ===
    private bool _isInLobby;
    private string _statusText = "Создайте лобби или введите код друга";
    private string _statusIcon = "⚡";
    private bool _isBusy;

    // === Хост ===
    private string? _lobbyCode;
    private string? _hostName;
    private string _hostStatusText = "Ожидание мира...";
    private bool _isLobbyCreated;
    private bool _isWorldOpen;
    private bool _codeCopied;

    // === Ошибка туннеля и копирование лога ===
    private bool _showTunnelFailedLogButton;
    private string? _tunnelFailureReason;
    private bool _tunnelLogCopied;

    // === Гость ===
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
        IDiscordRpcService? discordRpcService = null)
    {
        _lobbyService = lobbyService ?? throw new ArgumentNullException(nameof(lobbyService));
        _launchService = launchService ?? throw new ArgumentNullException(nameof(launchService));
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _worldWatcher = worldWatcher ?? new LanWorldWatcher();
        _tunnelProvider = tunnelProvider;
        _skinService = skinService;
        _lobbyApiClient = lobbyApiClient;
        _notificationService = notificationService;
        _discordRpcService = discordRpcService;

        CreateLobbyCommand = new AsyncRelayCommand(CreateLobbyAsync, () => !IsBusy && !IsInLobby);
        JoinLobbyCommand = new AsyncRelayCommand(JoinLobbyAsync, () => !IsBusy && !IsInLobby && GuestCodeInput.Length >= 6 && !IsJoiningLobby);
        CopyCodeCommand = new RelayCommand(_ => CopyCode(), _ => !string.IsNullOrWhiteSpace(LobbyCode));
        CopyLinkCommand = new RelayCommand(_ => CopyLobbyLink(), _ => !string.IsNullOrWhiteSpace(LobbyCode));
        ConnectToGameCommand = new AsyncRelayCommand(ConnectToGameAsync, () => !IsBusy && CanGuestConnect);
        ReconnectCommand = new AsyncRelayCommand(ReconnectAsync, () => CanReconnect);
        OpenWorldCommand = new AsyncRelayCommand(OpenWorldAsHostAsync, () => !IsBusy && IsLobbyCreated && _lobbyService.IsHost);
        LeaveLobbyCommand = new RelayCommand(_ => LeaveLobby(), _ => IsInLobby);

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

        // Подписка на события лога игры хоста
        _worldWatcher.WorldOpened += OnLanWorldOpened;
        _worldWatcher.WorldClosed += OnLanWorldClosed;

        // Подписка на изменение статуса туннеля
        if (_tunnelProvider != null)
        {
            _tunnelProvider.StatusChanged += OnTunnelStatusChanged;
        }
    }

    // === Свойства ===

    public bool IsInLobby
    {
        get => _isInLobby;
        private set
        {
            if (SetProperty(ref _isInLobby, value))
            {
                RaiseAllCommands();
                NotifyCellPropertiesChanged();
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

    // --- Хост ---

    public string? LobbyCode
    {
        get => _lobbyCode;
        private set
        {
            if (SetProperty(ref _lobbyCode, value))
            {
                NotifyCellPropertiesChanged();
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

    public bool IsHost => _lobbyService.IsHost;

    // --- Гость ---

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
    }

    public async Task<string?> CreateLobbyAsync()
    {
        IsBusy = true;
        StatusText = "Создание лобби...";
        StatusIcon = "⏳";
        ShowTunnelFailedLogButton = false;
        TunnelFailureReason = null;

        try
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
                StatusIcon = "❌";
                ShowTunnelFailedLogButton = true;
                TunnelFailureReason = "Не удалось получить конфигурацию туннеля с сервера (GET /api/tunnel-config).";
                return null;
            }

            var hostName = _configService.CurrentConfig?.Nickname ?? "Player";

            // Автоматически перезаливаем текущий скин под актуальным ником
            _ = EnsureSkinUploadedAsync();

            var code = await _lobbyService.CreateLobbyAsHostAsync(hostName);

            if (!string.IsNullOrWhiteSpace(code))
            {
                LobbyCode = code;
                HostName = hostName;
                IsLobbyCreated = true;
                IsInLobby = true;
                HostStatusText = "Ожидание мира...";
                StatusText = "Лобби создано! Отправь код другу и нажми «ОТКРЫТЬ МИР»";
                StatusIcon = "👑";

                ImageSource? hostAvatar = _skinService?.ExtractHeadAvatar(_configService.CurrentConfig?.SkinPath);
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
                        Avatar = hostAvatar ?? SkinService.LoadDefaultSteveBitmap()
                    });
                });
                return code;
            }
            else
            {
                StatusText = "Не удалось создать лобби. Проверь lobby-api сервер";
                StatusIcon = "❌";
                ShowTunnelFailedLogButton = true;
                TunnelFailureReason = "Сервер лобби вернул ошибку при создании лобби.";
                return null;
            }
        }
        catch (Exception ex)
        {
            PlayitTunnelProvider.LogTunnel($"[CREATE-LOBBY: ERROR] {ex.Message}");
            StatusText = $"Ошибка: {ex.Message}";
            StatusIcon = "❌";
            ShowTunnelFailedLogButton = true;
            TunnelFailureReason = ex.Message;
            return null;
        }
        finally
        {
            IsBusy = false;
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
                    StatusText = "Мир открыт — друзья могут подключиться!";
                    StatusIcon = "✅";
                }
                return;
            }

            // Реальный режим: запускаем watcher на latest.log
            var gameDir = _launchService.ResolveMinecraftDirectory(_configService.CurrentConfig.GameDir);
            var logPath = System.IO.Path.Combine(gameDir, "logs", "latest.log");
            
            _worldWatcher.Start(logPath);

            // Запускаем игру хоста через MainViewModel
            HostLaunchRequested?.Invoke(this, EventArgs.Empty);

            HostStatusText = "Ожидание открытия мира...";
            StatusText = "Игра запущена. Открой мир для сети (Esc → Открыть для сети)";
            StatusIcon = "⏳";
        }
        catch (Exception ex)
        {
            HostStatusText = "Ошибка";
            StatusText = $"Ошибка: {ex.Message}";
            StatusIcon = "❌";
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
                        StatusIcon = "❌";
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
                        StatusIcon = "❌";
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
                        StatusText = "Мир готов — друзья могут подключаться!";
                        StatusIcon = "✅";
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
                        StatusIcon = "❌";
                        ShowTunnelFailedLogButton = true;
                        TunnelFailureReason = reason;
                    });
                    await _lobbyService.CloseLobbyAsHostAsync();
                }
            }
            catch (Exception ex)
            {
                PlayitTunnelProvider.LogTunnel($"[EXCEPTION] OnLanWorldOpened failed: {ex.GetType().FullName}: {ex.Message}\n{ex.StackTrace}");
                Dispatch(() =>
                {
                    HostStatusText = "Ошибка туннеля";
                    StatusText = $"Ошибка туннеля: {ex.Message}";
                    StatusIcon = "❌";
                    ShowTunnelFailedLogButton = true;
                    TunnelFailureReason = ex.Message;
                });
                try
                {
                    await _lobbyService.CloseLobbyAsHostAsync();
                }
                catch (Exception closeEx)
                {
                    PlayitTunnelProvider.LogTunnel($"[EXCEPTION] CloseLobbyAsHostAsync on error failed: {closeEx.GetType().FullName}: {closeEx.Message}\n{closeEx.StackTrace}");
                }
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
                    StatusIcon = "ℹ️";
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
                        StatusIcon = "ℹ️";
                    });
                }
            });
        }
    }

    private void OnTunnelStatusChanged(TunnelInfo info)
    {
        if (!_lobbyService.IsHost || !IsInLobby) return;

        if (info.Status == TunnelStatus.Failed)
        {
            var reason = info.ErrorMessage ?? "Процесс туннеля playit завершился с ошибкой.";
            PlayitTunnelProvider.LogTunnel($"[LOBBY: STATUS_CHANGED] Tunnel failed: {reason}. Closing lobby via API.");
            Dispatch(() =>
            {
                HostStatusText = "Ошибка туннеля";
                StatusText = $"Ошибка туннеля: {reason}";
                StatusIcon = "❌";
                ShowTunnelFailedLogButton = true;
                TunnelFailureReason = reason;
            });

            _ = Task.Run(async () =>
            {
                try
                {
                    await _lobbyService.CloseLobbyAsHostAsync();
                }
                catch (Exception ex)
                {
                    PlayitTunnelProvider.LogTunnel($"[EXCEPTION] CloseLobbyAsHostAsync on tunnel failure failed: {ex.GetType().FullName}: {ex.Message}\n{ex.StackTrace}");
                }
            });
        }
    }

    public async Task JoinLobbyAsync()
    {
        if (GuestCodeInput.Length < 6) return;

        IsJoiningLobby = true;
        JoinErrorMessage = string.Empty;
        GuestStatusText = "Подключение к лобби...";
        StatusText = "Подключение...";
        StatusIcon = "⏳";

        var cleanCode = GuestCodeInput.Trim().ToUpperInvariant();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        try
        {
            // Если в тестах используется мок-сервис без реального клиента API:
            if (_lobbyService is not LobbyService && _lobbyApiClient == null)
            {
                var playerNameMock = _configService.CurrentConfig.Nickname;
                var joinedMock = await _lobbyService.JoinLobbyAsGuestAsync(cleanCode, playerNameMock, cts.Token);
                if (joinedMock)
                {
                    IsGuestJoined = true;
                    IsInLobby = true;
                    UpdateGuestStatus();
                }
                else
                {
                    JoinErrorMessage = $"Лобби с кодом {cleanCode} не найдено. Проверьте код или попросите хоста создать новое.";
                    GuestStatusText = "Лобби не найдено";
                    StatusText = "Не удалось подключиться к лобби";
                    StatusIcon = "❌";
                }
                return;
            }

            var client = _lobbyApiClient ?? new LobbyApiClient(null, _configService.CurrentConfig?.LobbyApiBaseUrl ?? "https://lobby-api.vercel.app", _configService);

            var (statusCode, statusResp, rawJson) = await client.GetStatusDetailedAsync(cleanCode, cts.Token);
            PlayitTunnelProvider.LogTunnel($"[JOIN: CHECK-STATUS] GET /status?code={cleanCode} -> HTTP {(statusCode.HasValue ? (int)statusCode.Value : -1)}, body: {rawJson}");

            if (!statusCode.HasValue)
            {
                JoinErrorMessage = "Нет связи с сервером лобби. Проверьте интернет и попробуйте снова.";
                GuestStatusText = "Ошибка соединения";
                StatusText = "Нет связи с сервером";
                StatusIcon = "❌";
                return;
            }

            if (statusCode == System.Net.HttpStatusCode.NotFound)
            {
                JoinErrorMessage = $"Лобби с кодом {cleanCode} не найдено. Проверьте код или попросите хоста создать новое.";
                GuestStatusText = "Лобби не найдено";
                StatusText = "Лобби не найдено";
                StatusIcon = "❌";
                return;
            }

            if (statusResp != null && string.Equals(statusResp.Status, "closed", StringComparison.OrdinalIgnoreCase))
            {
                JoinErrorMessage = "Это лобби закрыто. Попросите хоста создать новое.";
                GuestStatusText = "Лобби закрыто";
                StatusText = "Лобби закрыто";
                StatusIcon = "❌";
                return;
            }

            if (statusCode != System.Net.HttpStatusCode.OK)
            {
                JoinErrorMessage = "Нет связи с сервером лобби. Проверьте интернет и попробуйте снова.";
                GuestStatusText = "Ошибка сервера";
                StatusText = "Ошибка сервера";
                StatusIcon = "❌";
                return;
            }

            var playerName = _configService.CurrentConfig?.Nickname ?? "Player";

            // Автоматически перезаливаем текущий скин под актуальным ником
            _ = EnsureSkinUploadedAsync();

            var joined = await _lobbyService.JoinLobbyAsGuestAsync(cleanCode, playerName, cts.Token);

            if (joined)
            {
                IsGuestJoined = true;
                IsInLobby = true;
                _discordRpcService?.SetInLobby(1);
                UpdateGuestStatus();
            }
            else
            {
                JoinErrorMessage = $"Лобби с кодом {cleanCode} не найдено. Проверьте код или попросите хоста создать новое.";
                GuestStatusText = "Лобби не найдено или код неверный";
                StatusText = "Не удалось подключиться к лобби";
                StatusIcon = "❌";
            }
        }
        catch (OperationCanceledException)
        {
            JoinErrorMessage = "Нет связи с сервером лобби. Проверьте интернет и попробуйте снова.";
            GuestStatusText = "Таймаут подключения";
            StatusText = "Таймаут подключения";
            StatusIcon = "❌";
        }
        catch (Exception ex)
        {
            PlayitTunnelProvider.LogTunnel($"[JOIN: ERROR] {ex.Message}");
            JoinErrorMessage = "Нет связи с сервером лобби. Проверьте интернет и попробуйте снова.";
            GuestStatusText = "Ошибка подключения";
            StatusText = $"Ошибка: {ex.Message}";
            StatusIcon = "❌";
        }
        finally
        {
            IsJoiningLobby = false;
        }
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

    private Task ConnectToGameAsync()
    {
        var address = _lobbyService.CurrentTunnelAddress;
        if (string.IsNullOrWhiteSpace(address))
        {
            address = _lastTunnelAddress;
        }

        if (string.IsNullOrWhiteSpace(address))
        {
            StatusText = "Адрес сервера не получен";
            return Task.CompletedTask;
        }

        _lastTunnelAddress = address;
        IsBusy = true;
        StatusText = "Запуск игры...";
        StatusIcon = "🚀";

        try
        {
            // Запускаем событие, которое MainViewModel перехватывает
            // для запуска игры с --quickPlayMultiplayer <tunnelAddress>
            GuestConnectRequested?.Invoke(this, address);

            StatusText = "Игра запускается с подключением к серверу...";
            StatusIcon = "🎮";
        }
        catch (Exception ex)
        {
            StatusText = $"Ошибка запуска: {ex.Message}";
            StatusIcon = "❌";
        }
        finally
        {
            IsBusy = false;
        }

        return Task.CompletedTask;
    }

    private Task ReconnectAsync()
    {
        return ConnectToGameAsync();
    }

    /// <summary>
    /// Событие, которое MainViewModel слушает для запуска игры с quickPlayMultiplayer.
    /// </summary>
    public event EventHandler<string>? GuestConnectRequested;

    /// <summary>
    /// Событие, которое MainViewModel слушает для запуска игры хоста.
    /// </summary>
    public event EventHandler? HostLaunchRequested;

    private void CopyCode()
    {
        if (!string.IsNullOrWhiteSpace(LobbyCode))
        {
            try
            {
                Clipboard.SetText(LobbyCode);
                CodeCopied = true;

                // Сбрасываем через 1.6 секунды по спецификации задачи 27Б
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
        if (_lobbyService.IsHost)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _lobbyService.CloseLobbyAsHostAsync();
                }
                catch (Exception ex)
                {
                    PlayitTunnelProvider.LogTunnel($"[EXCEPTION] LeaveLobby: {ex.GetType().FullName}: {ex.Message}\n{ex.StackTrace}");
                }
            });
        }
        else
        {
            _lobbyService.LeaveLobby();
        }

        // Сброс всех состояний
        IsInLobby = false;
        IsLobbyCreated = false;
        IsWorldOpen = false;
        IsGuestJoined = false;
        CanGuestConnect = false;
        LobbyCode = null;
        HostName = null;
        GuestCodeInput = string.Empty;
        CodeCopied = false;
        HostStatusText = "Ожидание мира...";
        GuestStatusText = "Введите 6-значный код лобби";
        StatusText = "Создайте лобби или введите код друга";
        StatusIcon = "⚡";

        _discordRpcService?.SetInLauncher();
        _knownPlayerNicks.Clear();
        _previousWorldIsOpen = false;

        LobbyPlayers.Clear();
        JoinErrorMessage = string.Empty;
        IsJoiningLobby = false;
        OnPropertyChanged(nameof(IsHost));
    }

    // === Обработчики событий LobbyService ===

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
                        StatusIcon = "✅";
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
            }
        });
    }

    private void OnTunnelAddressReady(string tunnelAddress)
    {
        Dispatch(() =>
        {
            if (!_lobbyService.IsHost)
            {
                CanGuestConnect = true;
                GuestStatusText = "Хост открыл мир — можно подключаться!";
                StatusText = "Мир готов! Нажми «Подключиться к игре»";
                StatusIcon = "✅";

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
                StatusIcon = "⏳";
                break;
            case "open":
                CanGuestConnect = !string.IsNullOrWhiteSpace(_lobbyService.CurrentTunnelAddress);
                GuestStatusText = CanGuestConnect ? "Хост открыл мир — можно подключаться!" : "Ожидание адреса сервера...";
                StatusText = CanGuestConnect ? "Мир готов! Нажми «Подключиться к игре»" : "Получение адреса сервера...";
                StatusIcon = CanGuestConnect ? "✅" : "⏳";
                break;
            case "closed":
                CanGuestConnect = false;
                GuestStatusText = "Лобби закрыто хостом";
                StatusText = "Лобби было закрыто";
                StatusIcon = "❌";
                break;
            default:
                CanGuestConnect = false;
                GuestStatusText = "Введите 6-значный код лобби";
                StatusText = "Создайте лобби или введите код друга";
                StatusIcon = "⚡";
                break;
        }
    }

    private void OnLobbyStatusUpdated(LobbyStatusResponse status)
    {
        if (IsInLobby && status.Players != null && status.Players.Length > 0)
        {
            _discordRpcService?.SetInLobby(status.Players.Length);
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

        _ = RefreshLobbyPlayersAsync(status.Players, status.HostName);
    }

    public async Task RefreshLobbyPlayersAsync(string[]? players, string? hostName)
    {
        if (players == null) return;
        if (!string.IsNullOrWhiteSpace(hostName))
        {
            HostName = hostName;
        }

        // Build desired player descriptors
        var myNick = _configService.CurrentConfig?.Nickname ?? string.Empty;
        var incomingPlayers = new List<(string Nick, bool IsHost, ImageSource Avatar)>();
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
            if (_skinService != null)
            {
                avatar = await _skinService.GetAvatarForPlayerAsync(player);
            }
            else
            {
                avatar = SkinService.LoadDefaultSteveBitmap();
            }

            incomingPlayers.Add((player, isHost, avatar));
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
        });
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
}
