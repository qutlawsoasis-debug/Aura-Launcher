using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using AuraLauncher.Core;
using AuraLauncher.Models;
using AuraLauncher.Services.Interfaces;

namespace AuraLauncher.ViewModels;

/// <summary>
/// ViewModel экрана настроек лаунчера (ник, ОЗУ, пути, расширенный репозиторий).
/// Автосохранение настроек с дебаунсом 500 мс и обработкой потери фокуса/Enter.
/// Источником правды является IConfigService.
/// </summary>
public class SettingsViewModel : ObservableObject
{
    private readonly IConfigService _configService;
    private readonly ISkinService _skinService;
    private readonly IDiscordRpcService? _discordRpcService;
    private readonly IReportService? _reportService;
    private readonly IBackgroundService? _backgroundService;
    private readonly IPackUpdateService? _packUpdateService;
    private readonly INotificationService? _notificationService;
    private readonly IFriendService? _friendService;
    private bool _isCheckingIntegrity;
    private string _integrityStatusText = string.Empty;
    private CancellationTokenSource? _debounceCts;
    private string _errorMessage = string.Empty;
    private string _nickname = string.Empty;
    private string _nicknameErrorText = string.Empty;

    public bool AutoRotateBackgrounds
    {
        get => _backgroundService != null ? _backgroundService.AutoRotationEnabled : _configService.CurrentConfig.AutoRotateBackgrounds;
        set
        {
            bool changed = false;
            if (_backgroundService != null && _backgroundService.AutoRotationEnabled != value)
            {
                _backgroundService.AutoRotationEnabled = value;
                changed = true;
            }
            if (_configService.CurrentConfig.AutoRotateBackgrounds != value)
            {
                _configService.CurrentConfig.AutoRotateBackgrounds = value;
                changed = true;
            }
            if (changed)
            {
                OnPropertyChanged();
                _ = SaveImmediatelyAsync();
            }
        }
    }

    public RelayCommand NextBackgroundCommand { get; }

    public string Nickname
    {
        get => _nickname;
        set
        {
            var val = value ?? string.Empty;
            if (SetProperty(ref _nickname, val))
            {
                ValidateAndApplyNickname(val);
            }
        }
    }

    public string NicknameErrorText
    {
        get => _nicknameErrorText;
        private set
        {
            if (SetProperty(ref _nicknameErrorText, value))
            {
                OnPropertyChanged(nameof(HasNicknameError));
            }
        }
    }

    public bool HasNicknameError => !string.IsNullOrWhiteSpace(_nicknameErrorText);

    private void ValidateAndApplyNickname(string nick)
    {
        var result = NicknameValidator.Validate(nick);
        if (result.IsValid)
        {
            NicknameErrorText = string.Empty;
            if (_configService.CurrentConfig.Nickname != nick)
            {
                _configService.CurrentConfig.Nickname = nick;
                _ = SaveImmediatelyAsync();
                _ = ReuploadSkinUnderNewNickAsync(nick);
                _ = _friendService?.ChangeNicknameAsync(nick);
            }
        }
        else
        {
            NicknameErrorText = GetNicknameErrorMessage(result.Error);
        }
    }

    private async Task ReuploadSkinUnderNewNickAsync(string newNick)
    {
        try
        {
            var cfg = _configService.CurrentConfig;
            var res = await _skinService.UploadSkinToLobbyApiAsync(
                cfg.SkinPath,
                newNick,
                cfg.SkinModel,
                cfg.SkinOwnerToken,
                cfg.LobbyApiBaseUrl);

            if (res.Success && !string.IsNullOrWhiteSpace(res.OwnerToken))
            {
                cfg.SkinOwnerToken = res.OwnerToken;
                await _configService.SaveConfigAsync(cfg);
            }

            await _skinService.SyncSkinToGameAsync(cfg.SkinPath, newNick, cfg.GameDir);
            _skinService.ClearCustomSkinLoaderCache(cfg.GameDir);
        }
        catch { }
    }

    public static string GetNicknameErrorMessage(NicknameError error) => error switch
    {
        NicknameError.Empty => "Ник: от 3 до 16 символов, латиница, цифры и _.",
        NicknameError.TooShort => "Ник: от 3 до 16 символов, латиница, цифры и _.",
        NicknameError.TooLong => "Ник: от 3 до 16 символов, латиница, цифры и _.",
        NicknameError.InvalidChars => "Ник: от 3 до 16 символов, латиница, цифры и _.",
        _ => string.Empty
    };

    public int RamMb
    {
        get => _configService.CurrentConfig.RamMb;
        set
        {
            if (_configService.CurrentConfig.RamMb != value)
            {
                _configService.CurrentConfig.RamMb = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(RamGb));
                OnPropertyChanged(nameof(IsRam4));
                OnPropertyChanged(nameof(IsRam6));
                OnPropertyChanged(nameof(IsRam8));
                OnPropertyChanged(nameof(IsRam12));
                _ = SaveImmediatelyAsync();
            }
        }
    }

    public double RamGb
    {
        get => Math.Round(_configService.CurrentConfig.RamMb / 1024.0, 1);
        set
        {
            var calculatedMb = (int)Math.Round(value * 1024.0);
            if (calculatedMb != _configService.CurrentConfig.RamMb)
            {
                RamMb = calculatedMb;
            }
        }
    }

    public bool IsRam4
    {
        get => _configService.CurrentConfig.RamMb == 4096;
        set { if (value) RamMb = 4096; }
    }

    public bool IsRam6
    {
        get => _configService.CurrentConfig.RamMb == 6144;
        set { if (value) RamMb = 6144; }
    }

    public bool IsRam8
    {
        get => _configService.CurrentConfig.RamMb == 8192;
        set { if (value) RamMb = 8192; }
    }

    public bool IsRam12
    {
        get => _configService.CurrentConfig.RamMb == 12288;
        set { if (value) RamMb = 12288; }
    }

    public string GraphicsPreset
    {
        get => string.IsNullOrWhiteSpace(_configService.CurrentConfig.GraphicsPreset)
            ? "Balanced"
            : _configService.CurrentConfig.GraphicsPreset;
        set
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? "Balanced" : value.Trim();
            if (!string.Equals(_configService.CurrentConfig.GraphicsPreset, normalized, StringComparison.OrdinalIgnoreCase))
            {
                _configService.CurrentConfig.GraphicsPreset = normalized;
                AuraLauncher.Services.Implementations.FabricGameLaunchService.ApplyGraphicsPreset(GameDir, normalized);
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsPresetLow));
                OnPropertyChanged(nameof(IsPresetBalanced));
                OnPropertyChanged(nameof(IsPresetUltra));
                OnPropertyChanged(nameof(GraphicsPresetDescription));
                _ = SaveImmediatelyAsync();
            }
        }
    }

    public bool IsPresetLow
    {
        get => string.Equals(GraphicsPreset, "Low", StringComparison.OrdinalIgnoreCase);
        set { if (value) GraphicsPreset = "Low"; }
    }

    public bool IsPresetBalanced
    {
        get => !IsPresetLow && !IsPresetUltra;
        set { if (value) GraphicsPreset = "Balanced"; }
    }

    public bool IsPresetUltra
    {
        get => string.Equals(GraphicsPreset, "Ultra", StringComparison.OrdinalIgnoreCase);
        set { if (value) GraphicsPreset = "Ultra"; }
    }

    public string GraphicsPresetDescription => GraphicsPreset.ToLowerInvariant() switch
    {
        "low" => "Прорисовка 8 чанков, быстрая графика, без шейдеров",
        "ultra" => "Прорисовка 16 чанков, максимум деталей и теней",
        _ => "Прорисовка 12 чанков, оптимальный баланс FPS и качества"
    };

    public string GameDir
    {
        get => _configService.CurrentConfig.GameDir;
        set
        {
            if (_configService.CurrentConfig.GameDir != value)
            {
                _configService.CurrentConfig.GameDir = value ?? string.Empty;
                OnPropertyChanged();
                _ = SaveImmediatelyAsync();
            }
        }
    }

    public string DefaultGameDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".aura");

    public string PackRepo
    {
        get => _configService.CurrentConfig.PackRepo;
        set
        {
            if (_configService.CurrentConfig.PackRepo != value)
            {
                _configService.CurrentConfig.PackRepo = value ?? string.Empty;
                OnPropertyChanged();
                _ = SaveImmediatelyAsync();
            }
        }
    }

    public bool HideLauncherWhilePlaying
    {
        get => _configService.CurrentConfig.HideLauncherWhilePlaying;
        set
        {
            if (_configService.CurrentConfig.HideLauncherWhilePlaying != value)
            {
                _configService.CurrentConfig.HideLauncherWhilePlaying = value;
                OnPropertyChanged();
                _ = SaveImmediatelyAsync();
            }
        }
    }

    public bool MusicOnStartup
    {
        get => _configService.CurrentConfig.MusicOnStartup;
        set
        {
            if (_configService.CurrentConfig.MusicOnStartup != value)
            {
                _configService.CurrentConfig.MusicOnStartup = value;
                OnPropertyChanged();
                _ = SaveImmediatelyAsync();
            }
        }
    }

    public bool AutoConnectOnInviteAccept
    {
        get => _configService.CurrentConfig.AutoConnectOnInviteAccept;
        set
        {
            if (_configService.CurrentConfig.AutoConnectOnInviteAccept != value)
            {
                _configService.CurrentConfig.AutoConnectOnInviteAccept = value;
                OnPropertyChanged();
                _ = SaveImmediatelyAsync();
            }
        }
    }

    public bool WindowsNotificationsEnabled
    {
        get => _configService.CurrentConfig.WindowsNotificationsEnabled;
        set
        {
            if (_configService.CurrentConfig.WindowsNotificationsEnabled != value)
            {
                _configService.CurrentConfig.WindowsNotificationsEnabled = value;
                OnPropertyChanged();
                _ = SaveImmediatelyAsync();
            }
        }
    }

    public bool DiscordRpcEnabled
    {
        get => _configService.CurrentConfig.DiscordRpcEnabled;
        set
        {
            if (_configService.CurrentConfig.DiscordRpcEnabled != value)
            {
                _configService.CurrentConfig.DiscordRpcEnabled = value;
                OnPropertyChanged();
                _ = SaveImmediatelyAsync();
                _discordRpcService?.UpdateSettings();
            }
        }
    }

    public string StartMode
    {
        get => _configService.CurrentConfig.StartMode ?? "Maximized";
        set
        {
            if (_configService.CurrentConfig.StartMode != value)
            {
                _configService.CurrentConfig.StartMode = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsStartModeWindowed));
                OnPropertyChanged(nameof(IsStartModeMaximized));
                _ = SaveImmediatelyAsync();
            }
        }
    }

    public bool IsStartModeWindowed
    {
        get => string.Equals(_configService.CurrentConfig.StartMode, "Windowed", StringComparison.OrdinalIgnoreCase);
        set
        {
            if (value && StartMode != "Windowed")
            {
                StartMode = "Windowed";
            }
        }
    }

    public bool IsStartModeMaximized
    {
        get => !IsStartModeWindowed;
        set
        {
            if (value && StartMode != "Maximized")
            {
                StartMode = "Maximized";
            }
        }
    }

    public string CurrentVersion
    {
        get
        {
            var state = PackState.LoadValidState(_configService.CurrentConfig.GameDir);
            return !string.IsNullOrWhiteSpace(state?.PackVersion) ? state.PackVersion : "—";
        }
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        set
        {
            if (SetProperty(ref _errorMessage, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(_errorMessage);

    public bool IsCheckingIntegrity
    {
        get => _isCheckingIntegrity;
        set
        {
            if (SetProperty(ref _isCheckingIntegrity, value))
            {
                CheckIntegrityCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string IntegrityStatusText
    {
        get => _integrityStatusText;
        set
        {
            if (SetProperty(ref _integrityStatusText, value))
            {
                OnPropertyChanged(nameof(HasIntegrityStatus));
            }
        }
    }

    public bool HasIntegrityStatus => !string.IsNullOrWhiteSpace(_integrityStatusText);

    public RelayCommand SelectGameFolderCommand { get; }
    public AsyncRelayCommand GenerateReportCommand { get; }
    public RelayCommand OpenSendReportCommand { get; }
    public AsyncRelayCommand CheckIntegrityCommand { get; }

    public event Action<string?>? SendReportRequested;

    public SettingsViewModel(
        IConfigService configService,
        ISkinService skinService,
        IDiscordRpcService? discordRpcService = null,
        IReportService? reportService = null,
        IBackgroundService? backgroundService = null,
        IPackUpdateService? packUpdateService = null,
        INotificationService? notificationService = null,
        IFriendService? friendService = null)
    {
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _skinService = skinService ?? throw new ArgumentNullException(nameof(skinService));
        _discordRpcService = discordRpcService;
        _reportService = reportService;
        _backgroundService = backgroundService;
        _packUpdateService = packUpdateService;
        _notificationService = notificationService;
        _friendService = friendService;

        CheckIntegrityCommand = new AsyncRelayCommand(CheckIntegrityAsync, () => !IsCheckingIntegrity);

        NextBackgroundCommand = new RelayCommand(_ =>
        {
            _backgroundService?.NextBackground();
        });

        OpenSendReportCommand = new RelayCommand(_ =>
        {
            SendReportRequested?.Invoke(null);
        });

        GenerateReportCommand = new AsyncRelayCommand(async () =>
        {
            if (_reportService != null)
            {
                await _reportService.GenerateReportZipAsync();
            }
        });

        _nickname = _configService.CurrentConfig.Nickname ?? string.Empty;
        var initialResult = NicknameValidator.Validate(_nickname);
        if (!initialResult.IsValid)
        {
            _nicknameErrorText = GetNicknameErrorMessage(initialResult.Error);
        }

        SelectGameFolderCommand = new RelayCommand(_ =>
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Выберите рабочую папку игры",
                Multiselect = false,
                InitialDirectory = Directory.Exists(GameDir) ? GameDir : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
            };

            if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.FolderName))
            {
                GameDir = dialog.FolderName;
            }
        });

        _configService.ConfigChanged += (s, e) =>
        {
            if (_configService.CurrentConfig.Nickname != _nickname)
            {
                _nickname = _configService.CurrentConfig.Nickname ?? string.Empty;
                var res = NicknameValidator.Validate(_nickname);
                NicknameErrorText = res.IsValid ? string.Empty : GetNicknameErrorMessage(res.Error);
                OnPropertyChanged(nameof(Nickname));
            }
            OnPropertyChanged(null);
        };
    }

    private bool _isSavedToastVisible;
    public bool IsSavedToastVisible
    {
        get => _isSavedToastVisible;
        private set => SetProperty(ref _isSavedToastVisible, value);
    }

    private void ShowSavedToast()
    {
        IsSavedToastVisible = true;
        _ = Task.Delay(1400).ContinueWith(_ =>
        {
            System.Windows.Application.Current?.Dispatcher?.InvokeAsync(() => IsSavedToastVisible = false);
        });
    }

    public void ScheduleDebouncedSave()
    {
        _debounceCts?.Cancel();
        _debounceCts = new CancellationTokenSource();
        var token = _debounceCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(500, token);
                if (token.IsCancellationRequested) return;

                await _configService.SaveConfigAsync(_configService.CurrentConfig);
                ErrorMessage = string.Empty;
                ShowSavedToast();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                ErrorMessage = $"Ошибка сохранения: {ex.Message}";
            }
        }, token);
    }

    public async Task SaveImmediatelyAsync()
    {
        _debounceCts?.Cancel();
        try
        {
            await _configService.SaveConfigAsync(_configService.CurrentConfig);
            ErrorMessage = string.Empty;
            ShowSavedToast();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Ошибка сохранения: {ex.Message}";
        }
    }

    public async Task CheckIntegrityAsync()
    {
        if (IsCheckingIntegrity || _packUpdateService == null) return;

        IsCheckingIntegrity = true;
        IntegrityStatusText = "Проверка файлов сборки...";

        try
        {
            var progress = new Progress<DownloadProgressReport>(report =>
            {
                IntegrityStatusText = !string.IsNullOrWhiteSpace(report.FormattedSpeed)
                    ? $"{report.StatusText} ({report.Percentage:F0}%)"
                    : report.StatusText;
            });

            var result = await _packUpdateService.CheckAndApplyAsync(progress, forceFullCheck: true);
            if (result.Status == PackUpdateStatus.UpToDate)
            {
                IntegrityStatusText = "Все файлы сборки проверены и в порядке";
            }
            else if (result.Status == PackUpdateStatus.Updated)
            {
                IntegrityStatusText = $"Восстановлено файлов: {result.FilesChanged}";
                OnPropertyChanged(nameof(CurrentVersion));
            }
            else
            {
                IntegrityStatusText = result.Message;
            }

            _notificationService?.NotifyIntegrityChecked(IntegrityStatusText);
        }
        catch (Exception ex)
        {
            IntegrityStatusText = $"Ошибка проверки: {ex.Message}";
        }
        finally
        {
            IsCheckingIntegrity = false;
        }
    }
}
