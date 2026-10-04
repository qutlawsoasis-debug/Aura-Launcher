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

    private static readonly SolidColorBrush SuccessBrush = new(Color.FromRgb(0x19, 0xD3, 0xA7));
    private static readonly SolidColorBrush ErrorBrush = new(Color.FromRgb(0xEF, 0x44, 0x44));

    static WardrobeViewModel()
    {
        SuccessBrush.Freeze();
        ErrorBrush.Freeze();
    }

    public string Nickname => _configService.CurrentConfig.Nickname;

    public string SkinPath
    {
        get => _configService.CurrentConfig.SkinPath;
        set
        {
            if (_configService.CurrentConfig.SkinPath != value)
            {
                _configService.CurrentConfig.SkinPath = value ?? string.Empty;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SkinDisplayName));
                _ = _configService.SaveConfigAsync(_configService.CurrentConfig);
                UpdateSkinPreviews();
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

    public RelayCommand SelectSkinCommand { get; }
    public RelayCommand ResetSkinCommand { get; }

    public WardrobeViewModel(ISkinService skinService, IConfigService configService)
    {
        _skinService = skinService ?? throw new ArgumentNullException(nameof(skinService));
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));

        SelectSkinCommand = new RelayCommand(_ => SelectSkinFile());
        ResetSkinCommand = new RelayCommand(async _ => await ResetSkinToDefaultAsync());

        UpdateSkinPreviews();

        _configService.ConfigChanged += (s, cfg) =>
        {
            OnPropertyChanged(nameof(Nickname));
            OnPropertyChanged(nameof(SkinPath));
            UpdateSkinPreviews();
        };
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

            StatusMessage = "Скин успешно применен и синхронизирован!";
        }
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
