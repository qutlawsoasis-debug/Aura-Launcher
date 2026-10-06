using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using AuraLauncher.Core;
using AuraLauncher.Models;
using AuraLauncher.Services.Interfaces;

namespace AuraLauncher.ViewModels;

public class UpdateWindowViewModel : ObservableObject
{
    private readonly ILauncherUpdateService _launcherUpdateService;
    private readonly Action _onCloseRequested;
    private readonly Action _onRestoreMainWindow;

    private string _statusText = "Проверяем обновления…";
    private double _progressValue = 0;
    private bool _hasError = false;
    private int _retryCountdown = 30;
    private int _failedAttempts = 0;
    private bool _canLaunchCurrentVersion = false;
    private bool _isRetrying = false;
    private System.Windows.Threading.DispatcherTimer? _retryTimer;
    private CancellationTokenSource? _updateCts;
    private string _versionTransitionText = "beta 1.0.12 → beta 1.0.13";

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public string VersionTransitionText
    {
        get => _versionTransitionText;
        set => SetProperty(ref _versionTransitionText, value);
    }

    public double ProgressValue
    {
        get => _progressValue;
        set
        {
            if (SetProperty(ref _progressValue, value))
            {
                OnPropertyChanged(nameof(HasDefiniteProgress));
            }
        }
    }

    public bool HasDefiniteProgress => ProgressValue > 0 && ProgressValue <= 100 && !HasError;

    public bool HasError
    {
        get => _hasError;
        set
        {
            if (SetProperty(ref _hasError, value))
            {
                OnPropertyChanged(nameof(HasDefiniteProgress));
            }
        }
    }

    public int RetryCountdown
    {
        get => _retryCountdown;
        set
        {
            if (SetProperty(ref _retryCountdown, value))
            {
                OnPropertyChanged(nameof(RetryCountdownText));
            }
        }
    }

    public string RetryCountdownText => $"Повтор через {RetryCountdown} с";

    public bool CanLaunchCurrentVersion
    {
        get => _canLaunchCurrentVersion;
        set => SetProperty(ref _canLaunchCurrentVersion, value);
    }

    public ICommand RetryNowCommand { get; }
    public ICommand LaunchCurrentVersionCommand { get; }

    public UpdateWindowViewModel(
        ILauncherUpdateService launcherUpdateService,
        Action onCloseRequested,
        Action onRestoreMainWindow,
        string? targetVersion = null)
    {
        _launcherUpdateService = launcherUpdateService ?? throw new ArgumentNullException(nameof(launcherUpdateService));
        _onCloseRequested = onCloseRequested ?? throw new ArgumentNullException(nameof(onCloseRequested));
        _onRestoreMainWindow = onRestoreMainWindow ?? throw new ArgumentNullException(nameof(onRestoreMainWindow));

        FormatVersionTransition(targetVersion);

        RetryNowCommand = new RelayCommand(_ => RetryNow());
        LaunchCurrentVersionCommand = new RelayCommand(_ => LaunchCurrentVersion());
    }

    private void FormatVersionTransition(string? targetVersion)
    {
        string curVer = "beta 1.0.12";
        try
        {
            var cur = _launcherUpdateService.CurrentVersion;
            var parts = cur.Split('.');
            if (parts.Length == 3 && int.TryParse(parts[2], out int patch) && patch >= 8)
            {
                // Если сборка уже 1.2.21 (beta 1.0.13), для окна обновления текущая версия до обновления - это beta 1.0.12
                int currentPatch = patch > 20 ? patch - 9 : patch - 8;
                curVer = $"beta 1.0.{currentPatch}";
            }
        }
        catch { }

        string newVer = "beta 1.0.13";
        if (!string.IsNullOrWhiteSpace(targetVersion))
        {
            var parts = targetVersion.Split('.');
            if (parts.Length == 3 && int.TryParse(parts[2], out int patch) && patch >= 8)
            {
                newVer = $"beta 1.0.{patch - 8}";
            }
            else
            {
                newVer = targetVersion;
            }
        }

        VersionTransitionText = $"{curVer} → {newVer}";
    }

    public async Task StartUpdateFlowAsync()
    {
        if (_isRetrying) return;
        _isRetrying = true;

        StopRetryCountdown();
        HasError = false;
        ProgressValue = 0;
        StatusText = "Проверяем обновления…";

        _updateCts?.Dispose();
        _updateCts = new CancellationTokenSource();
        var ct = _updateCts.Token;

        try
        {
            await Task.Delay(400, ct);

            var progress = new Progress<DownloadProgressReport>(report =>
            {
                HasError = false;
                ProgressValue = report.Percentage;
                if (report.Percentage < 100)
                {
                    StatusText = $"Скачиваем… {report.Percentage:F0}%";
                }
                else
                {
                    StatusText = "Устанавливаем…";
                }
            });

            var result = await _launcherUpdateService.DownloadAndApplyAsync(progress, ct);

            if (result.Status == LauncherUpdateStatus.UpdatedRestarting)
            {
                ProgressValue = 100;
                StatusText = "Запускаем Aura…";
                await Task.Delay(800, ct);
            }
            else if (result.Status == LauncherUpdateStatus.UpToDate)
            {
                StatusText = "Запускаем Aura…";
                await Task.Delay(600, ct);
                LaunchCurrentVersion();
            }
            else
            {
                HandleFailure(result.Message);
            }
        }
        catch (OperationCanceledException)
        {
            // Отмена
        }
        catch (Exception ex)
        {
            HandleFailure(ex.Message);
        }
        finally
        {
            _isRetrying = false;
        }
    }

    private void HandleFailure(string? message)
    {
        _failedAttempts++;
        HasError = true;
        StatusText = "Не удалось обновиться";
        RetryCountdown = 30;

        if (_failedAttempts >= 3)
        {
            CanLaunchCurrentVersion = true;
        }

        StartRetryCountdown();
    }

    private void StartRetryCountdown()
    {
        StopRetryCountdown();
        _retryTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _retryTimer.Tick += (s, e) =>
        {
            if (RetryCountdown > 1)
            {
                RetryCountdown--;
            }
            else
            {
                StopRetryCountdown();
                _ = StartUpdateFlowAsync();
            }
        };
        _retryTimer.Start();
    }

    private void StopRetryCountdown()
    {
        _retryTimer?.Stop();
        _retryTimer = null;
    }

    public void RetryNow()
    {
        StopRetryCountdown();
        _ = StartUpdateFlowAsync();
    }

    public void LaunchCurrentVersion()
    {
        StopRetryCountdown();
        _updateCts?.Cancel();
        _onRestoreMainWindow?.Invoke();
        _onCloseRequested?.Invoke();
    }

    public void SetFakeState(string state)
    {
        StopRetryCountdown();
        switch (state.ToLowerInvariant())
        {
            case "checking":
                HasError = false;
                StatusText = "Проверяем обновления…";
                ProgressValue = 0;
                break;
            case "downloading":
                HasError = false;
                StatusText = "Скачиваем… 63%";
                ProgressValue = 63;
                break;
            case "installing":
                HasError = false;
                StatusText = "Устанавливаем…";
                ProgressValue = 100;
                break;
            case "launching":
                HasError = false;
                StatusText = "Запускаем Aura…";
                ProgressValue = 100;
                break;
            case "error":
                HasError = true;
                _failedAttempts = 3;
                CanLaunchCurrentVersion = true;
                StatusText = "Не удалось обновиться";
                RetryCountdown = 28;
                break;
        }
    }
}
