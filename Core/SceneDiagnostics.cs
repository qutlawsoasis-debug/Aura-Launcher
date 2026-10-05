using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using AuraLauncher.Services.Interfaces;
using AuraLauncher.Services.Implementations;
using AuraLauncher.ViewModels;

namespace AuraLauncher.Core;

public record PixelAnalysisResult(
    int Width,
    int Height,
    int TotalPixels,
    double MeanBrightness,
    double Variance,
    int UniqueColorsCount,
    bool IsPass,
    string Summary
);

public record SelfTestReport(
    PixelAnalysisResult PlayTab,
    PixelAnalysisResult SkinTab,
    PixelAnalysisResult SettingsTab,
    bool AllPassed,
    DateTime Timestamp
);

/// <summary>
/// Диагностический модуль для WPF окна AuraLauncher.
/// Выполняет снимки RenderTargetBitmap для всех трех вкладок (Играть, Скин, Настройки)
/// и проверяет наличие изображения программным анализом пикселей.
/// </summary>
public static class SceneDiagnostics
{
    public static PixelAnalysisResult AnalyzeImage(string pngFilePath)
    {
        if (!File.Exists(pngFilePath))
        {
            throw new FileNotFoundException($"Captured image file not found at: {pngFilePath}");
        }

        using var stream = File.OpenRead(pngFilePath);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];

        var formatted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        int width = formatted.PixelWidth;
        int height = formatted.PixelHeight;
        int stride = width * 4;
        byte[] pixels = new byte[height * stride];
        formatted.CopyPixels(pixels, stride, 0);

        int totalPixels = width * height;
        var uniqueColors = new HashSet<int>();
        double sumBrightness = 0;

        for (int i = 0; i < pixels.Length; i += 4)
        {
            byte b = pixels[i];
            byte g = pixels[i + 1];
            byte r = pixels[i + 2];
            int rgb = (r << 16) | (g << 8) | b;
            uniqueColors.Add(rgb);

            double brightness = 0.299 * r + 0.587 * g + 0.114 * b;
            sumBrightness += brightness;
        }

        double meanBrightness = sumBrightness / totalPixels;

        double sumSqDiff = 0;
        for (int i = 0; i < pixels.Length; i += 4)
        {
            byte b = pixels[i];
            byte g = pixels[i + 1];
            byte r = pixels[i + 2];
            double brightness = 0.299 * r + 0.587 * g + 0.114 * b;
            double diff = brightness - meanBrightness;
            sumSqDiff += diff * diff;
        }

        double variance = sumSqDiff / totalPixels;

        // Порог: если дисперсия < 8.0 или уникальных цветов < 25, картинка считается однородной (черный/белый экран)
        bool isPass = variance >= 8.0 && uniqueColors.Count >= 25;

        string summary = isPass
            ? $"[PASS] ({width}x{height}, цветов: {uniqueColors.Count}, среднее: {meanBrightness:F2}, дисперсия: {variance:F2})"
            : $"[FAIL] Однородно ({width}x{height}, цветов: {uniqueColors.Count}, среднее: {meanBrightness:F2}, дисперсия: {variance:F2})";

        return new PixelAnalysisResult(width, height, totalPixels, meanBrightness, variance, uniqueColors.Count, isPass, summary);
    }

    private static void CaptureWindowToPng(Window window, string filePath)
    {
        window.Dispatcher.Invoke(() =>
        {
            int width = Math.Max(1, (int)window.ActualWidth);
            int height = Math.Max(1, (int)window.ActualHeight);

            var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(window);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));

            using var fs = File.Create(filePath);
            encoder.Save(fs);
        }, DispatcherPriority.Render);
    }

    public static async Task<bool> RunSelfTestShotsAsync(MainWindow window)
    {
        try
        {
            App.Log("[SELFTEST] Starting WPF RenderTargetBitmap self-test for all 3 tabs...");

            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string debugDir = Path.Combine(appData, "Aura", "debug");
            Directory.CreateDirectory(debugDir);

            // Ожидание первоначальной отрисовки окна
            await Task.Delay(1000);

            void Navigate(string tab)
            {
                window.Dispatcher.Invoke(() =>
                {
                    if (window.DataContext is MainViewModel vm)
                    {
                        vm.NavigateCommand.Execute(tab);
                    }
                });
            }

            var configService = App.Services.GetRequiredService<IConfigService>();
            var originalGameDir = configService.CurrentConfig.GameDir;

            // 1. Вкладка "Играть" (Overview)
            App.Log("[SELFTEST] Navigating to Overview tab...");
            Navigate("Overview");
            await Task.Delay(500);

            // Состояние А: Пустая папка (кнопка УСТАНОВИТЬ, текст Сборка не установлена, статус Готов к установке)
            string tempEmptyDir = Path.Combine(Path.GetTempPath(), "aura_empty_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempEmptyDir);
            string emptyPlayPng = Path.Combine(debugDir, "tab_play_empty.png");

            try
            {
                await configService.UpdateConfigAsync(c => c.GameDir = tempEmptyDir);
                await Task.Delay(400);

                CaptureWindowToPng(window, emptyPlayPng);
                App.Log($"[SELFTEST] Tab Play (State A - Empty): Captured {emptyPlayPng}");
            }
            finally
            {
                // Возврат оригинального GameDir с установленным окружением
                await configService.UpdateConfigAsync(c => c.GameDir = originalGameDir);
                await Task.Delay(400);
                try { Directory.Delete(tempEmptyDir, true); } catch { }
            }

            // Состояние Б: Установленное окружение (кнопка ИГРАТЬ, число модов, статус Готов к игре)
            string installedPlayPng = Path.Combine(debugDir, "tab_play_installed.png");
            CaptureWindowToPng(window, installedPlayPng);
            string playPng = Path.Combine(debugDir, "tab_play.png");
            File.Copy(installedPlayPng, playPng, true);

            var playResult = AnalyzeImage(installedPlayPng);
            App.Log($"[SELFTEST] Tab Play (State B - Installed): {playResult.Summary}");

            // Сохраняем также scene.png для совместимости
            File.Copy(playPng, Path.Combine(debugDir, "scene.png"), true);

            // Копируем снимки в папку снимков (из --shots-dir или %TEMP%\AuraShots)
            string artifactDir = ResolveShotsDir();
            try
            {
                File.Copy(emptyPlayPng, Path.Combine(artifactDir, "tab_play_empty.png"), true);
                File.Copy(installedPlayPng, Path.Combine(artifactDir, "tab_play_installed.png"), true);
                File.Copy(playPng, Path.Combine(artifactDir, "tab_play.png"), true);
            }
            catch { }

            // 1.1. Проверка создания options.txt по умолчанию
            string tempOptDir = Path.Combine(Path.GetTempPath(), "test_opt_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempOptDir);
            FabricGameLaunchService.EnsureDefaultOptions(tempOptDir);
            string testOptFile = Path.Combine(tempOptDir, "options.txt");
            bool optCreated = File.Exists(testOptFile);
            string optContent = optCreated ? await File.ReadAllTextAsync(testOptFile) : "";
            bool hasOpt = optContent.Contains("version:3465") &&
                          optContent.Contains("lang:ru_ru") &&
                          optContent.Contains("guiScale:2") &&
                          optContent.Contains("fullscreen:true");
            App.Log($"[SELFTEST] Default options.txt created in empty dir (version:3465, lang:ru_ru, guiScale:2, fullscreen:true): {hasOpt}");
            try { Directory.Delete(tempOptDir, true); } catch { }

            // 1.2. Проверка запуска игры (Java @argsFile, кнопка ИГРА ЗАПУЩЕНА, статус, Process.Exited)
            var mainVm = window.Dispatcher.Invoke(() => window.DataContext as MainViewModel);
            if (mainVm != null)
            {
                var latestLogPath = Path.Combine(originalGameDir, "logs", "latest.log");
                var gameLogPath = Path.Combine(originalGameDir, "logs", "launcher-game.log");
                try { if (File.Exists(latestLogPath)) File.Delete(latestLogPath); } catch { }
                try { if (File.Exists(gameLogPath)) File.Delete(gameLogPath); } catch { }

                App.Log("[SELFTEST] Testing Game Launch flow (Java @argsFile, button lock, double-click protection)...");
                window.Dispatcher.Invoke(() =>
                {
                    mainVm.LaunchOrCancelCommand.Execute(null);
                });

                // Ожидание запуска процесса
                for (int i = 0; i < 40 && !window.Dispatcher.Invoke(() => mainVm.IsGameRunning); i++)
                {
                    await Task.Delay(250);
                }

                bool isGameRunning = window.Dispatcher.Invoke(() => mainVm.IsGameRunning);
                var proc = window.Dispatcher.Invoke(() => mainVm.CurrentGameProcess);

                if (isGameRunning && proc != null)
                {
                    string btnText = window.Dispatcher.Invoke(() => mainVm.LaunchButtonText);
                    bool canExec = window.Dispatcher.Invoke(() => mainVm.LaunchOrCancelCommand.CanExecute(null));
                    App.Log($"[SELFTEST] Game process detected! PID: {proc.Id}, ButtonText='{btnText}', CanExecute={canExec}");

                    string runningPlayPng = Path.Combine(debugDir, "tab_play_running.png");
                    CaptureWindowToPng(window, runningPlayPng);
                    try { File.Copy(runningPlayPng, Path.Combine(artifactDir, "tab_play_running.png"), true); } catch { }

                    // Проверка WMI поиска уже запущенного процесса
                    var launchService = App.Services.GetRequiredService<IGameLaunchService>();
                    var foundProc = launchService.FindRunningGameProcess(originalGameDir);
                    App.Log($"[SELFTEST] WMI running game process check: FoundPID={foundProc?.Id}");

                    // Честная проверка: ожидание в latest.log "Reloading ResourceManager" И "Created: ... atlas" (таймаут 120 с)
                    var sw = Stopwatch.StartNew();
                    bool targetLineFound = false;
                    string matchedLogLine = string.Empty;

                    App.Log("[SELFTEST] Monitoring latest.log for 'Reloading ResourceManager' AND 'Created: ... atlas' (timeout 120s)...");

                    while (sw.Elapsed.TotalSeconds < 120)
                    {
                        if (proc.HasExited)
                        {
                            double elapsedSec = sw.Elapsed.TotalSeconds;
                            App.Log($"[SELFTEST: FAILURE] Process exited prematurely after {elapsedSec:F1}s with code {proc.ExitCode} before reaching ResourceManager/atlas!");
                            if (File.Exists(latestLogPath))
                            {
                                var logLines = await File.ReadAllLinesAsync(latestLogPath);
                                var tail = logLines.TakeLast(40);
                                App.Log($"[SELFTEST: LATEST-LOG-TAIL]\n{string.Join(Environment.NewLine, tail)}");
                            }
                            else if (File.Exists(gameLogPath))
                            {
                                var logLines = await File.ReadAllLinesAsync(gameLogPath);
                                var tail = logLines.TakeLast(50);
                                App.Log($"[SELFTEST: GAME-LOG-TAIL]\n{string.Join(Environment.NewLine, tail)}");
                            }
                            break;
                        }

                        if (File.Exists(latestLogPath))
                        {
                            try
                            {
                                using var fs = new FileStream(latestLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                                using var reader = new StreamReader(fs, Encoding.UTF8);
                                var text = await reader.ReadToEndAsync();
                                bool hasReloading = text.Contains("Reloading ResourceManager");
                                bool hasAtlas = System.Text.RegularExpressions.Regex.IsMatch(text, @"Created:\s+.*atlas", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                                if (!proc.HasExited && hasReloading && hasAtlas)
                                {
                                    targetLineFound = true;
                                    matchedLogLine = "Reloading ResourceManager & atlas created";
                                    break;
                                }
                            }
                            catch { }

                            if (targetLineFound)
                            {
                                App.Log($"[SELFTEST: SUCCESS] Found target criteria in latest.log after {sw.Elapsed.TotalSeconds:F1}s: {matchedLogLine}");
                                break;
                            }
                        }

                        await Task.Delay(500);
                    }

                    if (!targetLineFound && !proc.HasExited)
                    {
                        App.Log("[SELFTEST: FAILURE] 120s timeout exceeded without matching target criteria in latest.log!");
                    }

                    // Завершение тестового процесса игры ТОЛЬКО после проверки строки из latest.log
                    try { proc.Kill(); } catch { }

                    for (int i = 0; i < 30 && window.Dispatcher.Invoke(() => mainVm.IsGameRunning); i++)
                    {
                        await Task.Delay(200);
                    }

                    // Сброс в нейтральный статус "Готов к игре"
                    window.Dispatcher.Invoke(() =>
                    {
                        mainVm.UpdateIdleState();
                    });
                    string finalBtn = window.Dispatcher.Invoke(() => mainVm.LaunchButtonText);
                    string finalTitle = window.Dispatcher.Invoke(() => mainVm.StateTitle);
                    App.Log($"[SELFTEST] Game stopped and UI unlocked: ButtonText='{finalBtn}', Status='{finalTitle}'");
                }
                else
                {
                    App.Log($"[SELFTEST: WARN] Game did not start within timeout: State={window.Dispatcher.Invoke(() => mainVm.State)}, StatusText={window.Dispatcher.Invoke(() => mainVm.StatusText)}");
                }
            }

            // 2. Вкладка "Скин" (Wardrobe)
            App.Log("[SELFTEST] Navigating to Wardrobe tab...");
            Navigate("Wardrobe");
            await Task.Delay(500);

            string skinPng = Path.Combine(debugDir, "tab_skin.png");
            CaptureWindowToPng(window, skinPng);
            var skinResult = AnalyzeImage(skinPng);
            App.Log($"[SELFTEST] Tab Skin: {skinResult.Summary}");
            try { File.Copy(skinPng, Path.Combine(artifactDir, "tab_skin.png"), true); } catch { }

            // 3. Вкладка "Настройки" (Settings)
            App.Log("[SELFTEST] Navigating to Settings tab...");
            Navigate("Settings");
            await Task.Delay(500);

            string settingsPng = Path.Combine(debugDir, "tab_settings.png");
            CaptureWindowToPng(window, settingsPng);
            var settingsResult = AnalyzeImage(settingsPng);
            App.Log($"[SELFTEST] Tab Settings: {settingsResult.Summary}");
            try { File.Copy(settingsPng, Path.Combine(artifactDir, "tab_settings.png"), true); } catch { }

            // Возврат на вкладку Играть
            Navigate("Overview");

            bool allPassed = playResult.IsPass && skinResult.IsPass && settingsResult.IsPass;
            var report = new SelfTestReport(playResult, skinResult, settingsResult, allPassed, DateTime.Now);

            string jsonReport = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
            string reportFile = Path.Combine(debugDir, "selftest.json");
            await File.WriteAllTextAsync(reportFile, jsonReport);
            try { File.Copy(reportFile, Path.Combine(artifactDir, "selftest.json"), true); } catch { }

            App.Log($"[SELFTEST] All 3 tabs captured successfully. AllPassed={allPassed}");
            return allPassed;
        }
        catch (Exception ex)
        {
            App.Log($"[SELFTEST: ERROR] Exception during selftest shots: {ex}");
            return false;
        }
    }

    public static async Task<bool> CaptureLobbyShotsAsync(MainWindow window)
    {
        try
        {
            App.Log("[SELFTEST-LOBBY] Starting Lobby screenshot capture...");
            await Task.Delay(1000);

            var mainVm = window.Dispatcher.Invoke(() => window.DataContext as MainViewModel);
            if (mainVm == null) return false;

            // 1. Switch to Lobby
            window.Dispatcher.Invoke(() => mainVm.SwitchTab("Lobby"));
            await Task.Delay(600);

            string shotsDir = ResolveShotsDir();
            string idlePng = Path.Combine(shotsDir, "lobby_tab_idle.png");
            CaptureWindowToPng(window, idlePng);
            App.Log($"[SELFTEST-LOBBY] Captured idle screenshot: {idlePng}");

            // 2. Click Create Lobby
            window.Dispatcher.Invoke(() =>
            {
                if (mainVm.LobbyVM.CreateLobbyCommand.CanExecute(null))
                {
                    mainVm.LobbyVM.CreateLobbyCommand.Execute(null);
                }
            });

            // Wait for lobby to be created (LobbyCode populated)
            for (int i = 0; i < 50; i++)
            {
                await Task.Delay(200);
                bool created = window.Dispatcher.Invoke(() => mainVm.LobbyVM.IsLobbyCreated && !string.IsNullOrWhiteSpace(mainVm.LobbyVM.LobbyCode));
                if (created) break;
            }

            await Task.Delay(500);

            string createdPng = Path.Combine(shotsDir, "lobby_tab_created.png");
            CaptureWindowToPng(window, createdPng);
            App.Log($"[SELFTEST-LOBBY] Captured created screenshot: {createdPng}");

            // Leave lobby
            window.Dispatcher.Invoke(() =>
            {
                if (mainVm.LobbyVM.LeaveLobbyCommand.CanExecute(null))
                {
                    mainVm.LobbyVM.LeaveLobbyCommand.Execute(null);
                }
            });
            await Task.Delay(300);

            return true;
        }
        catch (Exception ex)
        {
            App.Log($"[SELFTEST-LOBBY: ERROR] {ex}");
            return false;
        }
    }

    private static string ResolveShotsDir()
    {
        var args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals("--shots-dir", StringComparison.OrdinalIgnoreCase))
            {
                var candidate = args[i + 1];
                if (!string.IsNullOrWhiteSpace(candidate))
                {
                    try
                    {
                        Directory.CreateDirectory(candidate);
                        return candidate;
                    }
                    catch
                    {
                        // Ignore and fallback
                    }
                }
            }
        }

        string tempDir = Path.Combine(Path.GetTempPath(), "AuraShots");
        try
        {
            Directory.CreateDirectory(tempDir);
        }
        catch { }
        return tempDir;
    }
}
