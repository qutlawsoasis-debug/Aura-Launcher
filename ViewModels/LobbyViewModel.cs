using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using AuraLauncher.Core;
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
    private bool _hasPlayitSecret;

    // === Общее состояние ===
    private bool _isInLobby;
    private string _statusText = "Создайте лобби или введите код друга";
    private string _statusIcon = "⚡";
    private bool _isBusy;

    // === Хост ===
    private string? _lobbyCode;
    private string _hostStatusText = "Ожидание мира...";
    private bool _isLobbyCreated;
    private bool _isWorldOpen;
    private bool _codeCopied;

    // === Гость ===
    private string _guestCodeInput = string.Empty;
    private bool _isGuestJoined;
    private string _guestStatusText = "Введите 6-значный код лобби";
    private bool _canGuestConnect;

    public bool HasPlayitSecret
    {
        get => _hasPlayitSecret;
        private set
        {
            if (SetProperty(ref _hasPlayitSecret, value))
            {
                OnPropertyChanged(nameof(ShowClaimTunnelButton));
                OnPropertyChanged(nameof(ShowCreateLobbyButton));
            }
        }
    }

    public bool ShowClaimTunnelButton => !HasPlayitSecret;
    public bool ShowCreateLobbyButton => HasPlayitSecret;

    public AsyncRelayCommand ClaimTunnelCommand { get; }

    public LobbyViewModel(
        ILobbyService lobbyService,
        IGameLaunchService launchService,
        IConfigService configService,
        ILanWorldWatcher? worldWatcher = null,
        ITunnelProvider? tunnelProvider = null)
    {
        _lobbyService = lobbyService ?? throw new ArgumentNullException(nameof(lobbyService));
        _launchService = launchService ?? throw new ArgumentNullException(nameof(launchService));
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _worldWatcher = worldWatcher ?? new LanWorldWatcher();
        _tunnelProvider = tunnelProvider;

        UpdateSecretState();

        ClaimTunnelCommand = new AsyncRelayCommand(ClaimTunnelAsync, () => !IsBusy && !IsInLobby);
        CreateLobbyCommand = new AsyncRelayCommand(CreateLobbyAsync, () => !IsBusy && !IsInLobby);
        JoinLobbyCommand = new AsyncRelayCommand(JoinLobbyAsync, () => !IsBusy && !IsInLobby && GuestCodeInput.Length >= 6);
        CopyCodeCommand = new RelayCommand(_ => CopyCode(), _ => !string.IsNullOrWhiteSpace(LobbyCode));
        ConnectToGameCommand = new AsyncRelayCommand(ConnectToGameAsync, () => !IsBusy && CanGuestConnect);
        OpenWorldCommand = new AsyncRelayCommand(OpenWorldAsHostAsync, () => !IsBusy && IsLobbyCreated && _lobbyService.IsHost);
        LeaveLobbyCommand = new RelayCommand(_ => LeaveLobby(), _ => IsInLobby);

        // Подписка на события LobbyService
        _lobbyService.StatusChanged += OnLobbyStatusChanged;
        _lobbyService.TunnelAddressReady += OnTunnelAddressReady;

        // Подписка на события лога игры хоста
        _worldWatcher.WorldOpened += OnLanWorldOpened;
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
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
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
        private set => SetProperty(ref _lobbyCode, value);
    }

    public string HostStatusText
    {
        get => _hostStatusText;
        private set => SetProperty(ref _hostStatusText, value);
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
        private set => SetProperty(ref _isWorldOpen, value);
    }

    public bool CodeCopied
    {
        get => _codeCopied;
        private set => SetProperty(ref _codeCopied, value);
    }

    public bool IsHost => _lobbyService.IsHost;

    // --- Гость ---

    public string GuestCodeInput
    {
        get => _guestCodeInput;
        set
        {
            // Ограничиваем до 6 символов и приводим к верхнему регистру
            var cleaned = (value ?? string.Empty).Trim().ToUpperInvariant();
            if (cleaned.Length > 6) cleaned = cleaned[..6];
            if (SetProperty(ref _guestCodeInput, cleaned))
            {
                RaiseAllCommands();
            }
        }
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
        private set => SetProperty(ref _guestStatusText, value);
    }

    public bool CanGuestConnect
    {
        get => _canGuestConnect;
        private set
        {
            if (SetProperty(ref _canGuestConnect, value))
            {
                RaiseAllCommands();
            }
        }
    }

    // === Команды ===

    public AsyncRelayCommand CreateLobbyCommand { get; }
    public AsyncRelayCommand JoinLobbyCommand { get; }
    public RelayCommand CopyCodeCommand { get; }
    public AsyncRelayCommand ConnectToGameCommand { get; }
    public AsyncRelayCommand OpenWorldCommand { get; }
    public RelayCommand LeaveLobbyCommand { get; }

    public void UpdateSecretState()
    {
        if (_tunnelProvider is PlayitTunnelProvider playit)
        {
            HasPlayitSecret = playit.HasSecret;
        }
        else
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var toolsDir = System.IO.Path.Combine(appData, ".aura", "tools");
            var playitDefault = new PlayitTunnelProvider(null, toolsDir);
            HasPlayitSecret = playitDefault.HasSecret;
        }
    }

    private async Task ClaimTunnelAsync()
    {
        IsBusy = true;
        StatusText = "Подготовка ссылки привязки...";
        StatusIcon = "⏳";

        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var toolsDir = System.IO.Path.Combine(appData, ".aura", "tools");
            var provider = _tunnelProvider as PlayitTunnelProvider ?? new PlayitTunnelProvider(null, toolsDir);

            StatusText = "Открываем браузер... Подтвердите в браузере";
            StatusIcon = "🌐";

            var secret = await provider.ClaimTunnelAsync(url =>
            {
                Dispatch(() =>
                {
                    StatusText = "Подтвердите в браузере";
                    StatusIcon = "🌐";
                });
            });

            if (!string.IsNullOrWhiteSpace(secret))
            {
                UpdateSecretState();
                StatusText = "Туннель успешно привязан! Теперь можно создать лобби.";
                StatusIcon = "✅";
            }
            else
            {
                StatusText = "Туннель не привязан (время ожидания истекло)";
                StatusIcon = "❌";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Ошибка привязки: {ex.Message}";
            StatusIcon = "❌";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task CreateLobbyAsync()
    {
        UpdateSecretState();
        if (!HasPlayitSecret)
        {
            StatusText = "Сначала нажмите «ПРИВЯЗАТЬ ТУННЕЛЬ»";
            StatusIcon = "⚠️";
            return;
        }

        IsBusy = true;
        StatusText = "Создание лобби...";
        StatusIcon = "⏳";

        try
        {
            var hostName = _configService.CurrentConfig.Nickname;
            var code = await _lobbyService.CreateLobbyAsHostAsync(hostName);

            if (!string.IsNullOrWhiteSpace(code))
            {
                LobbyCode = code;
                IsLobbyCreated = true;
                IsInLobby = true;
                HostStatusText = "Ожидание мира...";
                StatusText = "Лобби создано! Отправь код другу и открой мир в Minecraft";
                StatusIcon = "🎮";
            }
            else
            {
                StatusText = "Не удалось создать лобби. Проверь lobby-api сервер";
                StatusIcon = "❌";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Ошибка: {ex.Message}";
            StatusIcon = "❌";
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
        Dispatch(async () =>
        {
            HostStatusText = $"Мир открыт на порту {port}! Подключение туннеля...";
            try
            {
                var success = await _lobbyService.HostOpenWorldAsync(localPort: port);
                if (success)
                {
                    IsWorldOpen = true;
                    HostStatusText = "Лобби открыто!";
                    StatusText = $"Мир открыт (порт {port}) — друзья могут подключиться!";
                    StatusIcon = "✅";
                }
                else
                {
                    HostStatusText = "Ошибка: туннель не поднялся";
                    StatusText = "Туннель не поднялся. Лобби отменено.";
                    StatusIcon = "❌";
                    try { await _lobbyService.CloseLobbyAsHostAsync(); } catch { }
                }
            }
            catch (Exception ex)
            {
                HostStatusText = "Ошибка";
                StatusText = $"Ошибка туннеля: {ex.Message}";
                StatusIcon = "❌";
                try { await _lobbyService.CloseLobbyAsHostAsync(); } catch { }
            }
        });
    }

    private async Task JoinLobbyAsync()
    {
        IsBusy = true;
        GuestStatusText = "Подключение к лобби...";
        StatusText = "Подключение...";
        StatusIcon = "⏳";

        try
        {
            var playerName = _configService.CurrentConfig.Nickname;
            var joined = await _lobbyService.JoinLobbyAsGuestAsync(GuestCodeInput, playerName);

            if (joined)
            {
                IsGuestJoined = true;
                IsInLobby = true;
                UpdateGuestStatus();
            }
            else
            {
                GuestStatusText = "Лобби не найдено или код неверный";
                StatusText = "Не удалось подключиться к лобби";
                StatusIcon = "❌";
            }
        }
        catch (Exception ex)
        {
            GuestStatusText = "Ошибка подключения";
            StatusText = $"Ошибка: {ex.Message}";
            StatusIcon = "❌";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private Task ConnectToGameAsync()
    {
        if (string.IsNullOrWhiteSpace(_lobbyService.CurrentTunnelAddress))
        {
            StatusText = "Адрес сервера не получен";
            return Task.CompletedTask;
        }

        IsBusy = true;
        StatusText = "Запуск игры...";
        StatusIcon = "🚀";

        try
        {
            // Запускаем событие, которое MainViewModel перехватывает
            // для запуска игры с --quickPlayMultiplayer <tunnelAddress>
            GuestConnectRequested?.Invoke(this, _lobbyService.CurrentTunnelAddress);

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

                // Сбрасываем через 2 секунды
                _ = Task.Delay(2000).ContinueWith(_ =>
                {
                    Application.Current?.Dispatcher?.InvokeAsync(() => CodeCopied = false);
                });
            }
            catch { }
        }
    }

    private void LeaveLobby()
    {
        if (_lobbyService.IsHost)
        {
            _ = _lobbyService.CloseLobbyAsHostAsync();
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
        GuestCodeInput = string.Empty;
        CodeCopied = false;
        HostStatusText = "Ожидание мира...";
        GuestStatusText = "Введите 6-значный код лобби";
        StatusText = "Создайте лобби или введите код друга";
        StatusIcon = "⚡";

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

    private void RaiseAllCommands()
    {
        CreateLobbyCommand.RaiseCanExecuteChanged();
        JoinLobbyCommand.RaiseCanExecuteChanged();
        ConnectToGameCommand.RaiseCanExecuteChanged();
        OpenWorldCommand.RaiseCanExecuteChanged();
    }
}
