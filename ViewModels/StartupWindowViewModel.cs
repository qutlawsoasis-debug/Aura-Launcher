using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using AuraLauncher.Core;
using AuraLauncher.Models;
using AuraLauncher.Services.Interfaces;

namespace AuraLauncher.ViewModels;

public class StartupWindowViewModel : ObservableObject
{
    private static readonly Brush DefaultTextPrimary = CreateFrozenBrush(0xE8, 0xF0, 0xF8);
    private static readonly Brush DefaultAccent = CreateFrozenBrush(0xF2, 0xA6, 0x3C);

    private static Brush CreateFrozenBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private readonly ILauncherUpdateService _launcherUpdateService;
    private readonly Action _onLaunchMainRequested;
    private readonly Action _onCloseRequested;

    private string _statusText = "Проверка обновления";
    private string _dotsText = "";
    private Brush _statusForeground = DefaultTextPrimary;
    private double _progressValue = 0;
    private bool _isProgressVisible = false;
    private string _progressPercentText = "";
    private string _versionText = "—";
    private bool _isFlowRunning = false;
    private CancellationTokenSource? _flowCts;

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public string DotsText
    {
        get => _dotsText;
        set => SetProperty(ref _dotsText, value);
    }

    public Brush StatusForeground
    {
        get => _statusForeground;
        set => SetProperty(ref _statusForeground, value);
    }

    public double ProgressValue
    {
        get => _progressValue;
        set
        {
            if (SetProperty(ref _progressValue, value))
            {
                OnPropertyChanged(nameof(ProgressBarWidth));
                ProgressPercentText = $"{Math.Round(value)}%";
            }
        }
    }

    public double ProgressBarWidth => Math.Max(0, Math.Min(258, _progressValue * 2.58));

    public bool IsProgressVisible
    {
        get => _isProgressVisible;
        set => SetProperty(ref _isProgressVisible, value);
    }

    public string ProgressPercentText
    {
        get => _progressPercentText;
        set => SetProperty(ref _progressPercentText, value);
    }

    public string VersionText
    {
        get => _versionText;
        set => SetProperty(ref _versionText, value);
    }

    public StartupWindowViewModel(
        ILauncherUpdateService launcherUpdateService,
        Action onLaunchMainRequested,
        Action onCloseRequested)
    {
        _launcherUpdateService = launcherUpdateService ?? throw new ArgumentNullException(nameof(launcherUpdateService));
        _onLaunchMainRequested = onLaunchMainRequested ?? throw new ArgumentNullException(nameof(onLaunchMainRequested));
        _onCloseRequested = onCloseRequested ?? throw new ArgumentNullException(nameof(onCloseRequested));

        ResolveResources();
        ResolveVersionText();
    }

    private void ResolveResources()
    {
        try
        {
            if (Application.Current != null && !Application.Current.Dispatcher.HasShutdownStarted)
            {
                if (Application.Current.TryFindResource("TextPrimary") is Brush tp)
                {
                    _statusForeground = tp;
                }
            }
        }
        catch { }
    }

    private void SetStatusColor(bool isAccent)
    {
        try
        {
            if (Application.Current != null && !Application.Current.Dispatcher.HasShutdownStarted)
            {
                string key = isAccent ? "Accent" : "TextPrimary";
                if (Application.Current.TryFindResource(key) is Brush brush)
                {
                    StatusForeground = brush;
                    return;
                }
            }
        }
        catch { }

        StatusForeground = isAccent ? DefaultAccent : DefaultTextPrimary;
    }

    private void ResolveVersionText()
    {
        try
        {
            var (_, userFacingVer) = Services.Implementations.LauncherUpdateService.ResolveVersions(_launcherUpdateService.CurrentVersion);
            VersionText = userFacingVer;
        }
        catch
        {
            VersionText = "—";
        }
    }

    public async Task StartStartupFlowAsync()
    {
        if (_isFlowRunning) return;
        _isFlowRunning = true;

        _flowCts?.Dispose();
        _flowCts = new CancellationTokenSource();
        var ct = _flowCts.Token;

        var stopwatch = Stopwatch.StartNew();

        try
        {
            // 1. Начальное: «Проверка обновления» + анимированные точки каждые 0.4s
            StatusText = "Проверка обновления";
            DotsText = ".";
            SetStatusColor(isAccent: false);
            IsProgressVisible = false;

            using var dotsCts = new CancellationTokenSource();
            var dotsTask = AnimateDotsAsync(dotsCts.Token);

            string? newVersion = null;
            bool checkSucceeded = true;

            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(6));

                if (_launcherUpdateService.IsInstalled)
                {
                    newVersion = await Task.Run(() => _launcherUpdateService.CheckForUpdatesAsync(timeoutCts.Token), timeoutCts.Token);
                }
                else
                {
                    // В dev-режиме даем сцене показаться
                    await Task.Delay(1200, timeoutCts.Token);
                }
            }
            catch
            {
                checkSucceeded = false;
            }

            dotsCts.Cancel();
            try { await dotsTask; } catch { }
            DotsText = "";

            if (!checkSucceeded)
            {
                // Ошибка сети/нет связи: «Не удалось проверить обновления», через 2s запуск без обновления
                StatusText = "Не удалось проверить обновления";
                DotsText = "";
                SetStatusColor(isAccent: false);
                IsProgressVisible = false;
                await Task.Delay(2000, ct);
            }
            else if (!string.IsNullOrWhiteSpace(newVersion))
            {
                // Если есть обновление: «Загрузка обновления», тонкий Accent-прогрессбар 2px под сценой + процент числом, затем «Установка...» -> автоперезапуск
                StatusText = "Загрузка обновления";
                DotsText = "";
                SetStatusColor(isAccent: false);
                IsProgressVisible = true;
                ProgressValue = 0;

                var progress = new Progress<DownloadProgressReport>(report =>
                {
                    ProgressValue = report.Percentage;
                });

                var updateResult = await _launcherUpdateService.DownloadAndApplyAsync(progress, ct);

                if (updateResult.Status == LauncherUpdateStatus.UpdatedRestarting)
                {
                    StatusText = "Установка...";
                    DotsText = "";
                    IsProgressVisible = false;
                    await Task.Delay(1200, ct);
                    _onCloseRequested();
                    return;
                }
                else
                {
                    StatusText = "Не удалось обновить";
                    DotsText = "";
                    IsProgressVisible = false;
                    await Task.Delay(1500, ct);
                }
            }
            else
            {
                // Если обновлений нет: «Установлено последнее обновление!» (Accent, ~1s)
                StatusText = "Установлено последнее обновление!";
                DotsText = "";
                SetStatusColor(isAccent: true);
                IsProgressVisible = false;
                await Task.Delay(1000, ct);
            }

            // Минимальное время показа: 1.5s (даже если проверка мгновенная), чтобы окно не мелькало
            long elapsedMs = stopwatch.ElapsedMilliseconds;
            if (elapsedMs < 1500)
            {
                await Task.Delay((int)(1500 - elapsedMs), ct);
            }

            // «Запуск лаунчера» с точками (~1s) -> плавное угасание окна (Opacity 250ms) -> открытие главного окна
            StatusText = "Запуск лаунчера";
            SetStatusColor(isAccent: false);
            using var launchDotsCts = new CancellationTokenSource();
            var launchDotsTask = AnimateDotsAsync(launchDotsCts.Token);
            await Task.Delay(1000, ct);
            launchDotsCts.Cancel();
            try { await launchDotsTask; } catch { }
            DotsText = "";

            _onLaunchMainRequested();
        }
        catch (OperationCanceledException)
        {
            // Flow cancelled
        }
        finally
        {
            _isFlowRunning = false;
        }
    }

    private async Task AnimateDotsAsync(CancellationToken token)
    {
        int dots = 1;
        while (!token.IsCancellationRequested)
        {
            DotsText = new string('.', dots);
            dots = (dots % 3) + 1;
            try
            {
                await Task.Delay(400, token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public void SetFakeState(string state)
    {
        _flowCts?.Cancel();
        switch (state.ToLowerInvariant())
        {
            case "checking":
                StatusText = "Проверка обновления";
                DotsText = "...";
                SetStatusColor(isAccent: false);
                IsProgressVisible = false;
                break;
            case "latest":
            case "uptodate":
                StatusText = "Установлено последнее обновление!";
                DotsText = "";
                SetStatusColor(isAccent: true);
                IsProgressVisible = false;
                break;
            case "downloading":
                StatusText = "Загрузка обновления";
                DotsText = "";
                SetStatusColor(isAccent: false);
                IsProgressVisible = true;
                ProgressValue = 63;
                break;
            case "launching":
                StatusText = "Запуск лаунчера";
                DotsText = "...";
                SetStatusColor(isAccent: false);
                IsProgressVisible = false;
                break;
            case "error":
                StatusText = "Не удалось проверить обновления";
                DotsText = "";
                SetStatusColor(isAccent: false);
                IsProgressVisible = false;
                break;
            case "installing":
                StatusText = "Установка...";
                DotsText = "";
                SetStatusColor(isAccent: false);
                IsProgressVisible = false;
                break;
        }
    }
}
