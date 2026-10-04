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
    private CancellationTokenSource? _debounceCts;
    private string _errorMessage = string.Empty;

    public string Nickname
    {
        get => _configService.CurrentConfig.Nickname;
        set
        {
            if (_configService.CurrentConfig.Nickname != value)
            {
                _configService.CurrentConfig.Nickname = value ?? string.Empty;
                OnPropertyChanged();
                _ = SaveImmediatelyAsync();
            }
        }
    }

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
                ScheduleDebouncedSave();
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
                ScheduleDebouncedSave();
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

    public RelayCommand SelectGameFolderCommand { get; }

    public SettingsViewModel(IConfigService configService)
    {
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));

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
            OnPropertyChanged(null);
        };
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
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Ошибка сохранения: {ex.Message}";
        }
    }
}
