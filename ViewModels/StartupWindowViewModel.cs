using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AuraLauncher.Core;
using AuraLauncher.Models;
using AuraLauncher.Services.Interfaces;

namespace AuraLauncher.ViewModels;

public class StartupWindowViewModel : ObservableObject
{
    private readonly ILauncherUpdateService _launcherUpdateService;
    private readonly Action _onLaunchMainRequested;
    private readonly Action _onCloseRequested;

    private string _statusText = "Проверка обновления.";
    private double _progressValue = 0;
    private bool _isProgressVisible = false;
    private string _progressPercentText = "";
    private string _versionText = "beta 1.0.32";
    private bool _isFlowRunning = false;
    private CancellationTokenSource? _flowCts;

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public double ProgressValue
    {
        get => _progressValue;
        set
        {
            if (SetProperty(ref _progressValue, value))
            {
                ProgressPercentText = $"{Math.Round(value)}%";
            }
        }
    }

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

        ResolveVersionText();
    }

    private void ResolveVersionText()
    {
        try
        {
            string verFile = Path.Combine(AppContext.BaseDirectory, "version.json");
            if (File.Exists(verFile))
            {
                string json = File.ReadAllText(verFile);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("userFacingVersion", out var prop))
                {
                    string? ver = prop.GetString();
                    if (!string.IsNullOrWhiteSpace(ver))
                    {
                        VersionText = ver;
                        return;
                    }
                }
            }
        }
        catch { }

        try
        {
            string cur = _launcherUpdateService.CurrentVersion;
            var parts = cur.Split('.');
            if (parts.Length == 3 && int.TryParse(parts[2], out int patch) && patch >= 8)
            {
                VersionText = $"beta 1.0.{patch - 8}";
                return;
            }
        }
        catch { }

        VersionText = "beta 1.0.32";
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
            // 1. Анимация точек при проверке: «Проверка обновления.» -> «..» -> «...»
            using var dotsCts = new CancellationTokenSource();
            var dotsTask = AnimateDotsAsync("Проверка обновления", dotsCts.Token);

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
                    // В dev-режиме даем спиннеру показаться
                    await Task.Delay(1200, timeoutCts.Token);
                }
            }
            catch
            {
                checkSucceeded = false;
            }

            dotsCts.Cancel();
            try { await dotsTask; } catch { }

            if (!checkSucceeded)
            {
                // Если проверка не удалась (нет сети): «Не удалось проверить обновления», через 2 сек продолжить запуск без обновления.
                StatusText = "Не удалось проверить обновления";
                IsProgressVisible = false;
                await Task.Delay(2000, ct);
            }
            else if (!string.IsNullOrWhiteSpace(newVersion))
            {
                // Есть обновление: «Загрузка обновления» + тонкая полоса прогресса amber и процент; затем «Установка...» и автоматический перезапуск
                StatusText = "Загрузка обновления";
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
                    IsProgressVisible = false;
                    await Task.Delay(1200, ct);
                    _onCloseRequested();
                    return;
                }
                else
                {
                    StatusText = "Не удалось обновить";
                    IsProgressVisible = false;
                    await Task.Delay(1500, ct);
                }
            }
            else
            {
                // Обновлений нет: «Установлено последнее обновление!» (держится около 1 сек)
                StatusText = "Установлено последнее обновление!";
                IsProgressVisible = false;
                await Task.Delay(1000, ct);
            }

            // Минимальное время показа окна 1.5 сек, чтобы оно не мигало на быстрой сети
            long elapsedMs = stopwatch.ElapsedMilliseconds;
            if (elapsedMs < 1500)
            {
                await Task.Delay((int)(1500 - elapsedMs), ct);
            }

            // Затем «Запуск лаунчера.» → «..» → «...» (около 1 сек), после чего окно плавно гаснет и открывается главное окно
            using var launchDotsCts = new CancellationTokenSource();
            var launchDotsTask = AnimateDotsAsync("Запуск лаунчера", launchDotsCts.Token);
            await Task.Delay(1000, ct);
            launchDotsCts.Cancel();
            try { await launchDotsTask; } catch { }

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

    private async Task AnimateDotsAsync(string baseText, CancellationToken token)
    {
        int dots = 1;
        while (!token.IsCancellationRequested)
        {
            StatusText = baseText + new string('.', dots);
            dots = (dots % 3) + 1;
            try
            {
                await Task.Delay(320, token);
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
                StatusText = "Проверка обновления...";
                IsProgressVisible = false;
                break;
            case "latest":
            case "uptodate":
                StatusText = "Установлено последнее обновление!";
                IsProgressVisible = false;
                break;
            case "downloading":
                StatusText = "Загрузка обновления";
                IsProgressVisible = true;
                ProgressValue = 63;
                break;
            case "launching":
                StatusText = "Запуск лаунчера...";
                IsProgressVisible = false;
                break;
            case "error":
                StatusText = "Не удалось проверить обновления";
                IsProgressVisible = false;
                break;
            case "installing":
                StatusText = "Установка...";
                IsProgressVisible = false;
                break;
        }
    }
}
