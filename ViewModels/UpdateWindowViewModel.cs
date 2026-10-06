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
    private bool _isProgressIndeterminate = false;
    private bool _hasError = false;
    private int _retryCountdown = 30;
    private int _failedAttempts = 0;
    private bool _canLaunchCurrentVersion = false;
    private bool _isRetrying = false;
    private System.Windows.Threading.DispatcherTimer? _retryTimer;
    private CancellationTokenSource? _updateCts;

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public double ProgressValue
    {
        get => _progressValue;
        set => SetProperty(ref _progressValue, value);
    }

    public bool IsProgressIndeterminate
    {
        get => _isProgressIndeterminate;
        set => SetProperty(ref _isProgressIndeterminate, value);
    }

    public bool HasError
    {
        get => _hasError;
        set => SetProperty(ref _hasError, value);
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
        Action onRestoreMainWindow)
    {
        _launcherUpdateService = launcherUpdateService ?? throw new ArgumentNullException(nameof(launcherUpdateService));
        _onCloseRequested = onCloseRequested ?? throw new ArgumentNullException(nameof(onCloseRequested));
        _onRestoreMainWindow = onRestoreMainWindow ?? throw new ArgumentNullException(nameof(onRestoreMainWindow));

        RetryNowCommand = new RelayCommand(_ => RetryNow());
        LaunchCurrentVersionCommand = new RelayCommand(_ => LaunchCurrentVersion());
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
            await Task.Delay(400, ct); // Небольшая пауза для читаемости статуса

            var progress = new Progress<DownloadProgressReport>(report =>
            {
                HasError = false;
                ProgressValue = report.Percentage;
                if (report.Percentage < 100)
                {
                    StatusText = $"Скачиваем обновление… {report.Percentage:F0}%";
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
                // Velopack сам перезапустит приложение
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
                StatusText = "Скачиваем обновление… 63%";
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
