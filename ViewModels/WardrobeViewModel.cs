using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media;
using Microsoft.Win32;
using AuraLauncher.Core;
using AuraLauncher.Services.Interfaces;

namespace AuraLauncher.ViewModels;

/// <summary>
/// ViewModel гардероба (выбор и 2D плоский предпросмотр скина персонажа: спереди и сзади).
/// Не хранит собственных копий: источником правды для ника и путей является IConfigService.
/// </summary>
public class WardrobeViewModel : ObservableObject
{
    private readonly ISkinService _skinService;
    private readonly IConfigService _configService;

    private ImageSource? _skinFrontPreview;
    private ImageSource? _skinBackPreview;
    private string _formatDescription = "Классический скин Стива (64×64)";
    private string _statusMessage = string.Empty;
    private bool _isStatusError;

    private static readonly SolidColorBrush SuccessBrush = new(Color.FromRgb(0x5F, 0xD3, 0x9A));
    private static readonly SolidColorBrush ErrorBrush = new(Color.FromRgb(0xFF, 0x8A, 0x7A));

    static WardrobeViewModel()
    {
        SuccessBrush.Freeze();
        ErrorBrush.Freeze();
    }

    public event Action? CustomSkinApplied;

    public string Nickname => _configService.CurrentConfig.Nickname;

    public static string CopySkinToPersistentStorage(string sourcePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
                return sourcePath;

            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var skinsDir = Path.Combine(appData, ".aura", "skins");
            Directory.CreateDirectory(skinsDir);

            var fileName = Path.GetFileName(sourcePath);
            if (string.IsNullOrWhiteSpace(fileName))
                fileName = "skin.png";

            var targetPath = Path.Combine(skinsDir, fileName);
            if (!string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(targetPath), StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(sourcePath, targetPath, overwrite: true);
            }
            return targetPath;
        }
        catch
        {
            return sourcePath;
        }
    }

    public string SkinPath
    {
        get => _configService.CurrentConfig.SkinPath;
        set
        {
            var resolved = !string.IsNullOrWhiteSpace(value) && File.Exists(value)
                ? CopySkinToPersistentStorage(value)
                : (value ?? string.Empty);

            if (_configService.CurrentConfig.SkinPath != resolved)
            {
                _configService.CurrentConfig.SkinPath = resolved;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SkinDisplayName));
                _ = _configService.SaveConfigAsync(_configService.CurrentConfig);
                UpdateSkinPreviews();
                if (!string.IsNullOrWhiteSpace(resolved) && File.Exists(resolved))
                {
                    CustomSkinApplied?.Invoke();
                }
            }
        }
    }

    public string SkinOriginalName
    {
        get => _configService.CurrentConfig.SkinOriginalName;
        set
        {
            if (_configService.CurrentConfig.SkinOriginalName != value)
            {
                _configService.CurrentConfig.SkinOriginalName = value ?? string.Empty;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SkinDisplayName));
                _ = _configService.SaveConfigAsync(_configService.CurrentConfig);
            }
        }
    }

    public string SkinDisplayName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_configService.CurrentConfig.SkinOriginalName))
                return _configService.CurrentConfig.SkinOriginalName;

            return "Стандартный";
        }
    }

    public ImageSource? SkinFrontPreview
    {
        get => _skinFrontPreview;
        set => SetProperty(ref _skinFrontPreview, value);
    }

    public ImageSource? SkinBackPreview
    {
        get => _skinBackPreview;
        set => SetProperty(ref _skinBackPreview, value);
    }

    public string FormatDescription
    {
        get => _formatDescription;
        set => SetProperty(ref _formatDescription, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set
        {
            if (SetProperty(ref _statusMessage, value))
            {
                OnPropertyChanged(nameof(HasStatusMessage));
            }
        }
    }

    public bool IsStatusError
    {
        get => _isStatusError;
        set
        {
            if (SetProperty(ref _isStatusError, value))
            {
                OnPropertyChanged(nameof(StatusBrush));
            }
        }
    }

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(_statusMessage);

    public SolidColorBrush StatusBrush => _isStatusError ? ErrorBrush : SuccessBrush;

    private readonly IFriendService? _friendService;
    private string _nicknameInput = string.Empty;
    private string _nicknameErrorText = string.Empty;
    private bool _isProfileIdCopied;

    public string NicknameInput
    {
        get => _nicknameInput;
        set
        {
            if (SetProperty(ref _nicknameInput, value ?? string.Empty))
            {
                ValidateNicknameInput(value);
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

    public string ProfileId => _friendService?.CurrentFriendCode ?? _configService.CurrentConfig.FriendCode ?? "--------";

    public bool IsProfileIdCopied
    {
        get => _isProfileIdCopied;
        private set
        {
            if (SetProperty(ref _isProfileIdCopied, value))
            {
                OnPropertyChanged(nameof(CopyProfileIdText));
            }
        }
    }

    public string CopyProfileIdText => IsProfileIdCopied ? "Скопировано!" : "Копировать";

    public RelayCommand CopyProfileIdCommand { get; }
    public RelayCommand SaveNicknameCommand { get; }

    public bool IsSlimModel
    {
        get => _configService.CurrentConfig.SkinModel == "slim";
        set
        {
            var newModel = value ? "slim" : "default";
            if (_configService.CurrentConfig.SkinModel != newModel)
            {
                _configService.CurrentConfig.SkinModel = newModel;
                OnPropertyChanged();
                _ = _configService.SaveConfigAsync(_configService.CurrentConfig);
                if (!string.IsNullOrWhiteSpace(_configService.CurrentConfig.SkinPath) && File.Exists(_configService.CurrentConfig.SkinPath))
                {
                    _ = UploadCurrentSkinAsync();
                }
            }
        }
    }

    public RelayCommand SelectSkinCommand { get; }
    public RelayCommand ResetSkinCommand { get; }

    public WardrobeViewModel(ISkinService skinService, IConfigService configService, IFriendService? friendService = null)
    {
        _skinService = skinService ?? throw new ArgumentNullException(nameof(skinService));
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _friendService = friendService;

        _nicknameInput = _configService.CurrentConfig.Nickname ?? "Player";

        SelectSkinCommand = new RelayCommand(_ => SelectSkinFile());
        ResetSkinCommand = new RelayCommand(async _ => await ResetSkinToDefaultAsync());
        CopyProfileIdCommand = new RelayCommand(_ => CopyProfileId());
        SaveNicknameCommand = new RelayCommand(async _ => await ApplyNicknameChangeAsync());

        UpdateSkinPreviews();

        _configService.ConfigChanged += (s, cfg) =>
        {
            OnPropertyChanged(nameof(Nickname));
            if (_nicknameInput != cfg.Nickname && !HasNicknameError)
            {
                _nicknameInput = cfg.Nickname;
                OnPropertyChanged(nameof(NicknameInput));
            }
            OnPropertyChanged(nameof(ProfileId));
            OnPropertyChanged(nameof(SkinPath));
            OnPropertyChanged(nameof(IsSlimModel));
            UpdateSkinPreviews();
        };
    }

    private void ValidateNicknameInput(string? val)
    {
        var result = NicknameValidator.Validate(val);
        if (result.IsValid)
        {
            NicknameErrorText = string.Empty;
        }
        else
        {
            NicknameErrorText = "Ник: от 3 до 16 символов, латиница, цифры и _.";
        }
    }

    public async Task ApplyNicknameChangeAsync()
    {
        var clean = (_nicknameInput ?? string.Empty).Trim();
        var validation = NicknameValidator.Validate(clean);
        if (!validation.IsValid)
        {
            NicknameErrorText = "Ник: от 3 до 16 символов, латиница, цифры и _.";
            return;
        }

        var oldNick = _configService.CurrentConfig.Nickname;
        if (string.Equals(oldNick, clean, StringComparison.Ordinal))
        {
            NicknameErrorText = string.Empty;
            return;
        }

        if (_friendService != null)
        {
            var res = await _friendService.ChangeNicknameAsync(clean);
            if (!res.Success)
            {
                NicknameErrorText = res.ErrorMessage ?? "Ошибка смены ника";
                return;
            }
        }

        NicknameErrorText = string.Empty;
        _configService.CurrentConfig.Nickname = clean;
        await _configService.SaveConfigAsync(_configService.CurrentConfig);

        var cfg = _configService.CurrentConfig;
        await _skinService.SyncSkinToGameAsync(cfg.SkinPath, clean, cfg.GameDir);
        _skinService.ClearCustomSkinLoaderCache(cfg.GameDir);

        OnPropertyChanged(nameof(Nickname));
    }

    private void CopyProfileId()
    {
        var code = ProfileId;
        if (string.IsNullOrWhiteSpace(code) || code.Contains('-')) return;

        try
        {
            System.Windows.Clipboard.SetText(code);
            IsProfileIdCopied = true;
            _ = Task.Delay(1600).ContinueWith(_ =>
            {
                System.Windows.Application.Current?.Dispatcher?.InvokeAsync(() => IsProfileIdCopied = false);
            });
        }
        catch { }
    }

    public void SelectSkinFile()
    {
        var ofd = new OpenFileDialog
        {
            Title = "Выберите скин Minecraft (.png)",
            Filter = "Скины Minecraft (*.png)|*.png|Все файлы (*.*)|*.*",
            Multiselect = false
        };

        if (ofd.ShowDialog() == true)
        {
            var validation = _skinService.ValidateSkinFile(ofd.FileName);
            if (!validation.IsValid)
            {
                IsStatusError = true;
                StatusMessage = validation.ErrorMessage ?? "Неподдерживаемый файл скина.";
                return;
            }

            IsStatusError = false;
            SkinPath = ofd.FileName;
            SkinOriginalName = Path.GetFileName(ofd.FileName);
            FormatDescription = validation.Height == 32
                ? "Классический скин Minecraft (64×32)"
                : "Современный скин Minecraft (64×64)";

            // Синхронизируем с игрой через единый путь config.GameDir
            var mcDir = _configService.CurrentConfig.GameDir;
            _ = _skinService.SyncSkinToGameAsync(SkinPath, Nickname, mcDir);

            // Загружаем в lobby-api
            _ = UploadCurrentSkinAsync();
        }
    }

    public async Task UploadCurrentSkinAsync()
    {
        try
        {
            var cfg = _configService.CurrentConfig;
            var res = await _skinService.UploadSkinToLobbyApiAsync(
                cfg.SkinPath,
                cfg.Nickname,
                cfg.SkinModel,
                cfg.SkinOwnerToken,
                cfg.LobbyApiBaseUrl);

            if (res.Success)
            {
                if (!string.IsNullOrWhiteSpace(res.OwnerToken))
                {
                    cfg.SkinOwnerToken = res.OwnerToken;
                    await _configService.SaveConfigAsync(cfg);
                }
                _skinService.ClearCustomSkinLoaderCache(cfg.GameDir);
                IsStatusError = false;
                StatusMessage = "Скин успешно загружен и применён!";
                ScheduleStatusMessageClear();
            }
            else
            {
                IsStatusError = true;
                StatusMessage = res.ErrorMessage ?? "Ошибка загрузки скина.";
            }
        }
        catch (Exception ex)
        {
            IsStatusError = true;
            StatusMessage = $"Ошибка отправки скина: {ex.Message}";
        }
    }

    private System.Windows.Threading.DispatcherTimer? _statusMessageTimer;

    private void ScheduleStatusMessageClear()
    {
        _statusMessageTimer?.Stop();
        _statusMessageTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(3)
        };
        _statusMessageTimer.Tick += (s, e) =>
        {
            _statusMessageTimer.Stop();
            if (!_isStatusError)
            {
                StatusMessage = string.Empty;
            }
        };
        _statusMessageTimer.Start();
    }

    public async Task ResetSkinToDefaultAsync()
    {
        IsStatusError = false;
        SkinPath = string.Empty;
        SkinOriginalName = string.Empty;

        var mcDir = _configService.CurrentConfig.GameDir;
        await _skinService.ResetToDefaultSteveAsync(Nickname, mcDir);

        FormatDescription = "Классический скин Стива (64×64)";
        StatusMessage = "Скин сброшен на классического Стива.";

        // Загружаем сброшенный дефолтный скин на сервер
        _ = UploadCurrentSkinAsync();
    }

    private void UpdateSkinPreviews()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(SkinPath) && File.Exists(SkinPath))
            {
                var validation = _skinService.ValidateSkinFile(SkinPath);
                if (validation.IsValid)
                {
                    FormatDescription = validation.Height == 32
                        ? "Классический скин Minecraft (64×32)"
                        : "Современный скин Minecraft (64×64)";
                }
                else
                {
                    IsStatusError = true;
                    StatusMessage = validation.ErrorMessage ?? "Неподдерживаемый размер скина";
                    FormatDescription = "Классический скин Стива (64×64)";
                }
            }
            else
            {
                FormatDescription = "Классический скин Стива (64×64)";
            }

            SkinFrontPreview = _skinService.ExtractFrontSkinPreview(SkinPath);
            SkinBackPreview = _skinService.ExtractBackSkinPreview(SkinPath);
        }
        catch (Exception ex)
        {
            IsStatusError = true;
            StatusMessage = $"Ошибка загрузки скина: {ex.Message}";
        }
    }
}
