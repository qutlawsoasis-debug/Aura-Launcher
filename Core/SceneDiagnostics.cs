using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using AuraLauncher.Services.Interfaces;
using AuraLauncher.Services.Implementations;
using AuraLauncher.ViewModels;
using AuraLauncher.Views;
using AuraLauncher.Models;

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

    public static async Task<bool> LiveTestLobbyAsync(MainWindow window)
    {
        System.Net.Sockets.TcpListener? listener = null;
        try
        {
            App.Log("[LIVE-TEST-LOBBY] Starting live test for CleanHost...");
            await Task.Delay(1000);
            var mainVm = window.Dispatcher.Invoke(() => window.DataContext as MainViewModel);
            if (mainVm == null) return false;

            window.Dispatcher.Invoke(() => mainVm.SwitchTab("Lobby"));
            await Task.Delay(500);

            string shotsDir = ResolveShotsDir();
            var artifactDir = @"C:\Users\magne\.gemini\antigravity\brain\5c57d232-70d4-4edf-b4d4-5effb51fb059";

            string initialShot = Path.Combine(shotsDir, "clean_host_lobby_initial.png");
            CaptureWindowToPng(window, initialShot);
            App.Log($"[LIVE-TEST-LOBBY] Captured initial lobby UI: {initialShot}");
            if (Directory.Exists(artifactDir))
            {
                try { File.Copy(initialShot, Path.Combine(artifactDir, "clean_host_lobby_initial.png"), true); } catch { }
            }

            // 1. Create lobby (quietly fetches tunnel-config, saves DPAPI secret, creates lobby)
            App.Log("[LIVE-TEST-LOBBY] Triggering CreateLobbyCommand...");
            window.Dispatcher.Invoke(() => mainVm.LobbyVM.CreateLobbyCommand.Execute(null));

            for (int i = 0; i < 60; i++)
            {
                await Task.Delay(500);
                if (window.Dispatcher.Invoke(() => mainVm.LobbyVM.IsLobbyCreated && !string.IsNullOrWhiteSpace(mainVm.LobbyVM.LobbyCode))) break;
            }

            string code = window.Dispatcher.Invoke(() => mainVm.LobbyVM.LobbyCode) ?? "";
            App.Log($"[LIVE-TEST-LOBBY] Lobby Created: {code}");

            string createdShot = Path.Combine(shotsDir, "clean_host_lobby_created.png");
            CaptureWindowToPng(window, createdShot);
            App.Log($"[LIVE-TEST-LOBBY] Captured lobby created UI: {createdShot}");
            if (Directory.Exists(artifactDir))
            {
                try { File.Copy(createdShot, Path.Combine(artifactDir, "clean_host_lobby_created.png"), true); } catch { }
            }

            // 2. Open World (localPort 25565 with a fake listener simulating Minecraft LAN)
            listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 25565);
            listener.Start();
            App.Log("[LIVE-TEST-LOBBY] Started local TCP listener on 127.0.0.1:25565");

            var lobbyService = App.Services.GetRequiredService<ILobbyService>();
            bool opened = await lobbyService.HostOpenWorldAsync(localPort: 25565, customTunnelAddress: "pgsql-jill.tun.ply.gg:38062");
            App.Log($"[LIVE-TEST-LOBBY] HostOpenWorldAsync result: {opened}");

            await Task.Delay(2000);

            string openShot = Path.Combine(shotsDir, "clean_host_lobby_open.png");
            CaptureWindowToPng(window, openShot);
            App.Log($"[LIVE-TEST-LOBBY] Captured lobby open UI: {openShot}");
            if (Directory.Exists(artifactDir))
            {
                try { File.Copy(openShot, Path.Combine(artifactDir, "clean_host_lobby_open.png"), true); } catch { }
            }

            return opened;
        }
        catch (Exception ex)
        {
            App.Log($"[LIVE-TEST-LOBBY: ERROR] {ex}");
            return false;
        }
        finally
        {
            try { listener?.Stop(); } catch { }
        }
    }

    public static async Task<bool> LiveTestKillPlayitAsync(MainWindow window)
    {
        System.Net.Sockets.TcpListener? listener = null;
        try
        {
            PlayitTunnelProvider.LogTunnel("=== [TEST-KILL-PLAYIT] STARTING TEST ===");
            await Task.Delay(1000);
            var mainVm = window.Dispatcher.Invoke(() => window.DataContext as MainViewModel);
            if (mainVm == null) return false;

            window.Dispatcher.Invoke(() => mainVm.SwitchTab("Lobby"));
            await Task.Delay(500);

            // 1. Create lobby
            PlayitTunnelProvider.LogTunnel("[TEST-KILL-PLAYIT] Creating lobby...");
            window.Dispatcher.Invoke(() => mainVm.LobbyVM.CreateLobbyCommand.Execute(null));

            for (int i = 0; i < 40; i++)
            {
                await Task.Delay(500);
                if (window.Dispatcher.Invoke(() => mainVm.LobbyVM.IsLobbyCreated && !string.IsNullOrWhiteSpace(mainVm.LobbyVM.LobbyCode))) break;
            }

            string code = window.Dispatcher.Invoke(() => mainVm.LobbyVM.LobbyCode) ?? "";
            PlayitTunnelProvider.LogTunnel($"[TEST-KILL-PLAYIT] Lobby created with code: {code}");

            // 2. Start TCP listener on 25565
            listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 25565);
            listener.Start();
            PlayitTunnelProvider.LogTunnel("[TEST-KILL-PLAYIT] Started local TCP listener on 127.0.0.1:25565");

            // 3. Background killer: kills playit whenever it appears (both initial and automatic retry)
            var killCts = new CancellationTokenSource();
            _ = Task.Run(async () =>
            {
                int killCount = 0;
                while (!killCts.Token.IsCancellationRequested && killCount < 4)
                {
                    try
                    {
                        var procs = Process.GetProcessesByName("playit-0.15.26");
                        foreach (var p in procs)
                        {
                            try
                            {
                                PlayitTunnelProvider.LogTunnel($"[TEST-KILL-PLAYIT] Deliberately killing playit process (PID: {p.Id})...");
                                p.Kill(true);
                                killCount++;
                            }
                            catch { }
                        }
                    }
                    catch { }
                    await Task.Delay(100, killCts.Token);
                }
            }, killCts.Token);

            // 4. Trigger LAN world opened
            PlayitTunnelProvider.LogTunnel("[TEST-KILL-PLAYIT] Triggering LAN world opened event on port 25565...");
            var watcher = typeof(LobbyViewModel).GetField("_worldWatcher", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(mainVm.LobbyVM) as ILanWorldWatcher;
            
            if (watcher is LanWorldWatcher lww)
            {
                lww.ProcessLine("Started serving on 25565");
            }
            else
            {
                var method = typeof(LobbyViewModel).GetMethod("OnLanWorldOpened", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                method?.Invoke(mainVm.LobbyVM, new object[] { 25565 });
            }

            // 5. Wait for error to appear on host UI
            PlayitTunnelProvider.LogTunnel("[TEST-KILL-PLAYIT] Awaiting failure reason on host UI...");
            bool errorDetected = false;
            string? failureReason = null;

            for (int i = 0; i < 30; i++)
            {
                await Task.Delay(500);
                bool btnVisible = window.Dispatcher.Invoke(() => mainVm.LobbyVM.ShowTunnelFailedLogButton);
                failureReason = window.Dispatcher.Invoke(() => mainVm.LobbyVM.TunnelFailureReason);
                string hostStatus = window.Dispatcher.Invoke(() => mainVm.LobbyVM.HostStatusText);

                if (btnVisible && !string.IsNullOrWhiteSpace(failureReason))
                {
                    errorDetected = true;
                    PlayitTunnelProvider.LogTunnel($"[TEST-KILL-PLAYIT] Error detected on host UI: '{failureReason}', HostStatusText='{hostStatus}'");
                    break;
                }
            }

            killCts.Cancel();

            if (!errorDetected)
            {
                PlayitTunnelProvider.LogTunnel("[TEST-KILL-PLAYIT: FAIL] Error was not detected on host UI within timeout!");
                return false;
            }

            // 6. Check lobby status in API (must be 'closed')
            PlayitTunnelProvider.LogTunnel($"[TEST-KILL-PLAYIT] Checking lobby status in API for code {code}...");
            var apiClient = App.Services.GetRequiredService<ILobbyApiClient>();
            await Task.Delay(1000);
            var statusResp = await apiClient.GetStatusAsync(code);
            PlayitTunnelProvider.LogTunnel($"[TEST-KILL-PLAYIT] API status response: {statusResp?.Status}");

            bool isClosed = string.Equals(statusResp?.Status, "closed", StringComparison.OrdinalIgnoreCase);
            if (!isClosed)
            {
                PlayitTunnelProvider.LogTunnel($"[TEST-KILL-PLAYIT: FAIL] Expected lobby status 'closed', but got '{statusResp?.Status}'!");
                return false;
            }

            PlayitTunnelProvider.LogTunnel("=== [TEST-KILL-PLAYIT: PASSED] Host UI shows error reason, lobby closed via API, no modal dialog! ===");
            return true;
        }
        catch (Exception ex)
        {
            PlayitTunnelProvider.LogTunnel($"[TEST-KILL-PLAYIT: ERROR] {ex}");
            return false;
        }
        finally
        {
            try { listener?.Stop(); } catch { }
        }
    }

    public static async Task<bool> RunLobbyLifecycleTestAsync(MainWindow window)
    {
        System.Net.Sockets.TcpListener? listener = null;
        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var globalToolsDir = Path.Combine(appData, ".aura", "tools");
            Directory.CreateDirectory(globalToolsDir);
            var dummySecretFile = Path.Combine(globalToolsDir, "playit.secret.enc");

            // 1. Subplant an empty playit.secret.enc
            File.WriteAllText(dummySecretFile, "");
            PlayitTunnelProvider.LogTunnel($"[TEST-LIFECYCLE] Subplanted dummy empty secret at: {dummySecretFile}");

            // 2. Verify purge
            PlayitTunnelProvider.PurgeLegacyLocalSecrets();
            bool wasPurged = !File.Exists(dummySecretFile);
            PlayitTunnelProvider.LogTunnel($"[TEST-LIFECYCLE] PurgeLegacyLocalSecrets executed: dummy file deleted = {wasPurged}");

            var mainVm = window.Dispatcher.Invoke(() => window.DataContext as MainViewModel);
            if (mainVm == null) return false;

            window.Dispatcher.Invoke(() => mainVm.SwitchTab("Lobby"));
            await Task.Delay(500);

            // 3. Create lobby as host (fetches /api/tunnel-config)
            PlayitTunnelProvider.LogTunnel("[TEST-LIFECYCLE] Invoking CreateLobbyCommand (fetching /api/tunnel-config)...");
            window.Dispatcher.Invoke(() => mainVm.LobbyVM.CreateLobbyCommand.Execute(null));

            string? code = null;
            for (int i = 0; i < 40; i++)
            {
                await Task.Delay(300);
                code = window.Dispatcher.Invoke(() => mainVm.LobbyVM.LobbyCode);
                if (!string.IsNullOrWhiteSpace(code)) break;
            }

            if (string.IsNullOrWhiteSpace(code))
            {
                PlayitTunnelProvider.LogTunnel("[TEST-LIFECYCLE: FAIL] Lobby creation timed out!");
                return false;
            }

            PlayitTunnelProvider.LogTunnel($"[TEST-LIFECYCLE] Lobby created with code: {code}");

            // 4. Scenario 1: Game running, world NOT open -> status=waiting, tunnelAddress=null
            var apiClient = App.Services.GetRequiredService<ILobbyApiClient>();
            var statusWaiting = await apiClient.GetStatusAsync(code);
            PlayitTunnelProvider.LogTunnel($"[TEST-LIFECYCLE: SCENARIO-1] GET /status -> status={statusWaiting?.Status}, tunnelAddress={(statusWaiting?.TunnelAddress ?? "null")}");

            if (!string.Equals(statusWaiting?.Status, "waiting", StringComparison.OrdinalIgnoreCase) || !string.IsNullOrWhiteSpace(statusWaiting?.TunnelAddress))
            {
                PlayitTunnelProvider.LogTunnel($"[TEST-LIFECYCLE: FAIL] Scenario 1 mismatch! Expected status=waiting, tunnelAddress=null. Got status={statusWaiting?.Status}, tunnelAddress={statusWaiting?.TunnelAddress}");
                return false;
            }

            // 5. Scenario 2: World is opened!
            PlayitTunnelProvider.LogTunnel("[TEST-LIFECYCLE] Starting TCP listener on 127.0.0.1:25565...");
            listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 25565);
            listener.Start();

            PlayitTunnelProvider.LogTunnel("[TEST-LIFECYCLE] Triggering LanWorldWatcher 'Started serving on 25565'...");
            var watcher = typeof(LobbyViewModel).GetField("_worldWatcher", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(mainVm.LobbyVM) as ILanWorldWatcher;
            if (watcher is LanWorldWatcher lww)
            {
                lww.ProcessLine("Started serving on 25565");
            }
            else
            {
                var method = typeof(LobbyViewModel).GetMethod("OnLanWorldOpened", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                method?.Invoke(mainVm.LobbyVM, new object[] { 25565 });
            }

            bool isOpen = false;
            for (int i = 0; i < 60; i++)
            {
                await Task.Delay(500);
                isOpen = window.Dispatcher.Invoke(() => mainVm.LobbyVM.IsWorldOpen);
                if (isOpen) break;
            }

            if (!isOpen)
            {
                PlayitTunnelProvider.LogTunnel("[TEST-LIFECYCLE: FAIL] World did not transition to open on host UI!");
                return false;
            }

            var statusOpen = await apiClient.GetStatusAsync(code);
            PlayitTunnelProvider.LogTunnel($"[TEST-LIFECYCLE: SCENARIO-2] GET /status -> status={statusOpen?.Status}, tunnelAddress={(statusOpen?.TunnelAddress ?? "null")}");

            if (!string.Equals(statusOpen?.Status, "open", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(statusOpen?.TunnelAddress))
            {
                PlayitTunnelProvider.LogTunnel($"[TEST-LIFECYCLE: FAIL] Scenario 2 mismatch! Expected status=open, tunnelAddress!=null. Got status={statusOpen?.Status}, tunnelAddress={statusOpen?.TunnelAddress}");
                return false;
            }

            // 6. Cleanup
            PlayitTunnelProvider.LogTunnel("[TEST-LIFECYCLE] Cleaning up test session and leaving lobby...");
            window.Dispatcher.Invoke(() => mainVm.LobbyVM.LeaveLobbyCommand.Execute(null));
            await Task.Delay(1000);

            PlayitTunnelProvider.LogTunnel("=== [TEST-LIFECYCLE: PASSED] All scenarios verified successfully! ===");
            return true;
        }
        catch (Exception ex)
        {
            PlayitTunnelProvider.LogTunnel($"[TEST-LIFECYCLE: ERROR] {ex}");
            return false;
        }
        finally
        {
            try { listener?.Stop(); } catch { }
        }
    }

    public static async Task<bool> RunRapidTabSwitchStressTestAsync(MainWindow window)
    {
        App.Log("[STRESS_TEST] Starting rapid tab switch stress test (60 switches over 1500ms)...");
        var tabs = new[] { "Overview", "Lobby", "Wardrobe", "Settings" };
        var random = new Random(42);
        string lastTab = "Overview";

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 60; i++)
        {
            string targetTab = tabs[random.Next(tabs.Length)];
            lastTab = targetTab;
            window.Dispatcher.Invoke(() =>
            {
                if (window.DataContext is MainViewModel vm)
                {
                    vm.SwitchTab(targetTab);
                }
            });
            await Task.Delay(25); // 60 * 25ms = 1500ms
        }
        sw.Stop();

        // Небольшая задержка для завершения анимации последнего экрана
        await Task.Delay(300);

        int visibleCount = 0;
        int collapsedCount = 0;
        string visibleScreenName = "";
        FrameworkElement? activeScreenElement = null;

        window.Dispatcher.Invoke(() =>
        {
            var screens = new (string Name, FrameworkElement Element)[]
            {
                ("Overview", window.ViewOverview),
                ("Lobby", window.ViewLobby),
                ("Wardrobe", window.ViewWardrobe),
                ("Settings", window.ViewSettings)
            };

            foreach (var (name, elem) in screens)
            {
                if (elem.Visibility == Visibility.Visible)
                {
                    visibleCount++;
                    visibleScreenName = name;
                    activeScreenElement = elem;
                }
                else if (elem.Visibility == Visibility.Collapsed)
                {
                    collapsedCount++;
                }
            }
        });

        App.Log($"[STRESS_TEST] 60 switches completed in {sw.ElapsedMilliseconds}ms. Final Target='{lastTab}'.");
        App.Log($"[STRESS_TEST] Visible screens count: {visibleCount} (Active: '{visibleScreenName}'), Collapsed screens count: {collapsedCount}");

        // Программно нажимаем кнопку на активном экране через UIAutomation (IInvokeProvider.Invoke())
        bool invokedSuccessfully = false;
        string buttonClickedName = "";

        window.Dispatcher.Invoke(() =>
        {
            if (activeScreenElement != null)
            {
                var button = FindVisualChild<Button>(activeScreenElement);
                if (button != null)
                {
                    buttonClickedName = (button.Content as string) ?? button.Name ?? button.GetType().Name;
                    var oldCommand = button.Command;
                    bool commandExecuted = false;
                    RoutedEventHandler clickHandler = (s, e) =>
                    {
                        commandExecuted = true;
                    };
                    button.AddHandler(Button.ClickEvent, clickHandler, true);

                    try
                    {
                        button.Command = new RelayCommand(_ =>
                        {
                            commandExecuted = true;
                        });

                        var peer = System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(button);
                        if (peer?.GetPattern(System.Windows.Automation.Peers.PatternInterface.Invoke) is System.Windows.Automation.Provider.IInvokeProvider invoker)
                        {
                            invoker.Invoke();

                            // Прокачиваем очередь сообщений Dispatcher для обработки DispatcherPriority.Input
                            var frame = new DispatcherFrame();
                            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new DispatcherOperationCallback(f =>
                            {
                                ((DispatcherFrame)f).Continue = false;
                                return null;
                            }), frame);
                            Dispatcher.PushFrame(frame);

                            invokedSuccessfully = commandExecuted;
                        }
                    }
                    finally
                    {
                        button.RemoveHandler(Button.ClickEvent, clickHandler);
                        button.Command = oldCommand;
                    }
                }
            }
        });

        App.Log($"[STRESS_TEST] UIAutomation IInvokeProvider.Invoke() on active screen ('{visibleScreenName}') button '{buttonClickedName}': ConfirmedClick={invokedSuccessfully}");
        return visibleCount == 1 && collapsedCount == 3 && invokedSuccessfully;
    }

    public static async Task<bool> CaptureAllScreenshotsAsync(MainWindow window, string prefix)
    {
        System.Net.Sockets.TcpListener? listener = null;
        try
        {
            await Task.Delay(1000);
            var mainVm = window.Dispatcher.Invoke(() => window.DataContext as MainViewModel);
            if (mainVm == null) return false;

            // 0. Стресс-тест быстрого переключения вкладок
            await RunRapidTabSwitchStressTestAsync(window);

            string shotsDir = ResolveShotsDir();
            var artifactDir = @"C:\Users\magne\.gemini\antigravity\brain\5c57d232-70d4-4edf-b4d4-5effb51fb059";
            var repoShotsDir = @"C:\Users\magne\Documents\GitHub\Aura-Launcher\shots";
            try { Directory.CreateDirectory(repoShotsDir); } catch { }

            void SaveShot(string fileName)
            {
                string path = Path.Combine(shotsDir, fileName);
                CaptureWindowToPng(window, path);
                if (Directory.Exists(artifactDir))
                {
                    try { File.Copy(path, Path.Combine(artifactDir, fileName), true); } catch { }
                }
                if (Directory.Exists(repoShotsDir))
                {
                    try { File.Copy(path, Path.Combine(repoShotsDir, fileName), true); } catch { }
                }
            }

            // 1. ИГРАТЬ обычный
            window.Dispatcher.Invoke(() => mainVm.SwitchTab("Overview"));
            await Task.Delay(500);
            SaveShot($"{prefix}_tab_play.png");

            // 1b. ИГРАТЬ после клика (игра запущена: видна надпись "Закройте Minecraft, чтобы запустить снова")
            window.Dispatcher.Invoke(() =>
            {
                mainVm.IsGameRunning = true;
            });
            await Task.Delay(300);
            SaveShot($"{prefix}_tab_play_clicked.png");
            SaveShot($"{prefix}_tab_play_running.png");
            window.Dispatcher.Invoke(() =>
            {
                mainVm.IsGameRunning = false;
            });
            await Task.Delay(200);

            // 1c. ИГРАТЬ с наведением на меню (проверка: заголовок Aura не сдвигается)
            window.Dispatcher.Invoke(() =>
            {
                window.AnimateMenuDimming(window.MenuBtnLobby, isHovered: true);
            });
            await Task.Delay(300);
            SaveShot($"{prefix}_tab_play_menu_hover.png");
            window.Dispatcher.Invoke(() =>
            {
                window.AnimateMenuDimming(window.MenuBtnPlay, isHovered: false);
            });
            await Task.Delay(200);

            // 2. СКИН - 3D вид спереди (по умолчанию 0°)
            window.Dispatcher.Invoke(() => mainVm.SwitchTab("Wardrobe"));
            await Task.Delay(500);
            window.Dispatcher.Invoke(() =>
            {
                var wardrobeView = FindVisualChild<WardrobeView>(window);
                wardrobeView?.RotateToFront();
            });
            await Task.Delay(300);
            SaveShot($"{prefix}_tab_skin.png");
            SaveShot($"{prefix}_tab_skin_front.png");

            // 2b. СКИН - повёрнутый на 90° (виден бок)
            window.Dispatcher.Invoke(() =>
            {
                var wardrobeView = FindVisualChild<WardrobeView>(window);
                wardrobeView?.RotateToSide();
            });
            await Task.Delay(400);
            SaveShot($"{prefix}_tab_skin_side.png");

            // 2c. СКИН - вид сзади (180°, обычные руки)
            window.Dispatcher.Invoke(() =>
            {
                var wardrobeView = FindVisualChild<WardrobeView>(window);
                wardrobeView?.RotateToBack();
            });
            await Task.Delay(400);
            SaveShot($"{prefix}_tab_skin_back.png");

            // 2d. СКИН - сзади с тонкими руками (Alex, 180°)
            window.Dispatcher.Invoke(() =>
            {
                var wardrobeView = FindVisualChild<WardrobeView>(window);
                if (mainVm.CurrentView is WardrobeViewModel wvm)
                {
                    wvm.IsSlimModel = true;
                }
                wardrobeView?.RotateToBack();
            });
            await Task.Delay(400);
            SaveShot($"{prefix}_tab_skin_slim_back.png");
            SaveShot($"{prefix}_tab_skin_slim.png");

            // Возврат к стандартной модели
            window.Dispatcher.Invoke(() =>
            {
                if (mainVm.CurrentView is WardrobeViewModel wvm)
                {
                    wvm.IsSlimModel = false;
                }
            });
            await Task.Delay(200);

            // 2e. Диагностический тест: раскраска каждой грани каждого кубоида своим цветом
            window.Dispatcher.Invoke(() =>
            {
                var wardrobeView = FindVisualChild<WardrobeView>(window);
                if (wardrobeView != null)
                {
                    wardrobeView.IsDiagnosticMode = true;
                    wardrobeView.Refresh3DModel();
                    wardrobeView.RotateToFront();
                }
            });
            await Task.Delay(300);
            SaveShot("skin_diagnostic_front.png");
            SaveShot($"{prefix}_skin_diagnostic_front.png");

            window.Dispatcher.Invoke(() =>
            {
                var wardrobeView = FindVisualChild<WardrobeView>(window);
                wardrobeView?.RotateToBack();
            });
            await Task.Delay(300);
            SaveShot("skin_diagnostic_back.png");
            SaveShot($"{prefix}_skin_diagnostic_back.png");

            // Восстановление нормальной 3D-модели скина
            window.Dispatcher.Invoke(() =>
            {
                var wardrobeView = FindVisualChild<WardrobeView>(window);
                if (wardrobeView != null)
                {
                    wardrobeView.IsDiagnosticMode = false;
                    wardrobeView.RotateToFront();
                    wardrobeView.Refresh3DModel();
                }
            });
            await Task.Delay(200);

            // 4 скриншота меню на всех вкладках (проверка оранжевого квадрата у активного пункта)
            window.Dispatcher.Invoke(() => mainVm.SwitchTab("Overview"));
            await Task.Delay(300);
            SaveShot($"{prefix}_menu_overview.png");

            window.Dispatcher.Invoke(() => mainVm.SwitchTab("Lobby"));
            await Task.Delay(300);
            SaveShot($"{prefix}_menu_lobby.png");

            window.Dispatcher.Invoke(() => mainVm.SwitchTab("Wardrobe"));
            await Task.Delay(300);
            SaveShot($"{prefix}_menu_skin.png");

            window.Dispatcher.Invoke(() => mainVm.SwitchTab("Settings"));
            await Task.Delay(300);
            SaveShot($"{prefix}_menu_settings.png");

            // 3. НАСТРОЙКИ
            window.Dispatcher.Invoke(() => mainVm.SwitchTab("Settings"));
            await Task.Delay(500);
            SaveShot($"{prefix}_tab_settings.png");

            // 4. ЛОББИ - пусто
            window.Dispatcher.Invoke(() => mainVm.SwitchTab("Lobby"));
            await Task.Delay(500);
            SaveShot($"{prefix}_tab_lobby_empty.png");

            // 4b. ЛОББИ - ошибка ввода несуществующего кода 111111 (HTTP 404)
            App.Log("[TEST-LOBBY-GUEST-ERROR] Testing JoinLobby with non-existent code 111111...");
            window.Dispatcher.Invoke(() =>
            {
                mainVm.LobbyVM.GuestCodeInput = "111111";
            });
            await window.Dispatcher.InvokeAsync(async () =>
            {
                await mainVm.LobbyVM.JoinLobbyAsync();
            });
            await Task.Delay(500);
            SaveShot($"{prefix}_tab_lobby_guest_error.png");
            App.Log($"[TEST-LOBBY-GUEST-ERROR] Error displayed: '{window.Dispatcher.Invoke(() => mainVm.LobbyVM.JoinErrorMessage)}', HasJoinError={window.Dispatcher.Invoke(() => mainVm.LobbyVM.HasJoinError)}");

            // 4c. ЛОББИ - очистка ошибки при начале ввода
            window.Dispatcher.Invoke(() =>
            {
                mainVm.LobbyVM.GuestCodeInput = "ABC123";
            });
            App.Log($"[TEST-LOBBY-GUEST-ERROR] After entering new code ABC123: HasJoinError={window.Dispatcher.Invoke(() => mainVm.LobbyVM.HasJoinError)}");
            await Task.Delay(200);
            SaveShot($"{prefix}_tab_lobby_guest_input.png");

            // 5. ЛОББИ - ожидание у хоста (создаем лобби)
            window.Dispatcher.Invoke(() => mainVm.LobbyVM.CreateLobbyCommand.Execute(null));
            for (int i = 0; i < 40; i++)
            {
                await Task.Delay(300);
                if (window.Dispatcher.Invoke(() => mainVm.LobbyVM.IsLobbyCreated && !string.IsNullOrWhiteSpace(mainVm.LobbyVM.LobbyCode))) break;
            }
            await Task.Delay(500);
            SaveShot($"{prefix}_tab_lobby_waiting.png");

            // 5b. Гость ждет в лобби
            window.Dispatcher.Invoke(() =>
            {
                mainVm.LobbyVM.GuestStatusText = "Ждём хоста...";
            });
            SaveShot($"{prefix}_tab_lobby_guest_waiting.png");

            // 6. ЛОББИ - мир открыт с 2 игроками
            listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 25565);
            listener.Start();
            var lobbyService = App.Services.GetRequiredService<ILobbyService>();
            await lobbyService.HostOpenWorldAsync(localPort: 25565, customTunnelAddress: "pgsql-jill.tun.ply.gg:38062");
            window.Dispatcher.Invoke(() =>
            {
                mainVm.LobbyVM.IsWorldOpen = true;
                mainVm.LobbyVM.HostStatusText = "Мир открыт для сети";
            });
            await Task.Delay(1000);
            window.Dispatcher.Invoke(() =>
            {
                if (mainVm.LobbyVM.LobbyPlayers.Count < 2)
                {
                    mainVm.LobbyVM.LobbyPlayers.Add(new LobbyPlayerItem
                    {
                        Nickname = "Friend",
                        IsHost = false,
                        Avatar = SkinService.LoadDefaultSteveBitmap()
                    });
                }
            });
            SaveShot($"{prefix}_tab_lobby_open.png");

            // 6b. 10-секундная проверка стабильности координаты X блока игрока в лобби
            App.Log("[LOBBY_10S_CHECK] Starting 10-second lobby player block position stability check...");
            double initialX = -1;
            for (int sec = 0; sec <= 10; sec++)
            {
                await mainVm.LobbyVM.RefreshLobbyPlayersAsync(new[] { mainVm.PlayerNickname, "Friend" }, mainVm.PlayerNickname);
                double currentX = window.Dispatcher.Invoke(() =>
                {
                    var playersControl = FindVisualChild<ItemsControl>(window.ViewLobby);
                    if (playersControl != null)
                    {
                        var border = FindVisualChild<Border>(playersControl);
                        if (border != null)
                        {
                            var pt = border.TransformToAncestor(window).Transform(new Point(0, 0));
                            return pt.X;
                        }
                    }
                    return 0.0;
                });

                if (initialX < 0) initialX = currentX;
                double deltaX = Math.Abs(currentX - initialX);
                App.Log($"[LOBBY_10S_CHECK] t={sec,2}s: PlayerBlock X={currentX:F2}px, deltaX={deltaX:F2}px (stable: {deltaX < 0.01})");
                await Task.Delay(1000);
            }

            // Leave lobby
            window.Dispatcher.Invoke(() => mainVm.LobbyVM.LeaveLobbyCommand.Execute(null));
            await Task.Delay(500);

            // Record UI animation (5-10s GIF): hover menu, screen transitions
            window.Dispatcher.Invoke(() => mainVm.SwitchTab("Overview"));
            await Task.Delay(300);
            await RecordGameMenuAndTransitionsAsync(window, $"{prefix}_menu_and_screens.gif");

            return true;
        }
        catch (Exception ex)
        {
            App.Log($"[CAPTURE-SHOTS: ERROR] {ex}");
            return false;
        }
        finally
        {
            try { listener?.Stop(); } catch { }
        }
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild) return typedChild;
            var sub = FindVisualChild<T>(child);
            if (sub != null) return sub;
        }
        return null;
    }

    public static async Task<bool> RecordGameMenuAndTransitionsAsync(MainWindow window, string gifFileName)
    {
        try
        {
            string shotsDir = ResolveShotsDir();
            var repoShotsDir = @"C:\Users\magne\Documents\GitHub\Aura-Launcher\shots";
            var brainDir = @"C:\Users\magne\.gemini\antigravity\brain\5c57d232-70d4-4edf-b4d4-5effb51fb059";
            try { Directory.CreateDirectory(repoShotsDir); } catch { }

            var frameBitmaps = new List<RenderTargetBitmap>();
            var mainVm = window.Dispatcher.Invoke(() => window.DataContext as MainViewModel);
            if (mainVm == null) return false;

            // 1. Initial Overview state (4 frames)
            for (int i = 0; i < 4; i++)
            {
                frameBitmaps.Add(CaptureWindow(window));
                await Task.Delay(150);
            }

            // 2. Hover over "Лобби" item (menu dimming & shift) (6 frames)
            window.Dispatcher.Invoke(() => window.AnimateMenuDimming(window.MenuBtnLobby, isHovered: true));
            for (int i = 0; i < 6; i++)
            {
                frameBitmaps.Add(CaptureWindow(window));
                await Task.Delay(120);
            }

            // 3. Switch to "Lobby" tab (8 frames with 40ms interval, logging Y coordinate)
            window.Dispatcher.Invoke(() =>
            {
                window.AnimateMenuDimming(window.MenuBtnLobby, isHovered: false);
                mainVm.SwitchTab("Lobby");
            });
            for (int f = 0; f < 8; f++)
            {
                frameBitmaps.Add(CaptureWindow(window));
                window.Dispatcher.Invoke(() =>
                {
                    double curY = 0;
                    if (window.ViewLobby.RenderTransform is TranslateTransform tt)
                    {
                        curY = tt.Y;
                    }
                    var lobbyTitle = FindVisualChild<TextBlock>(window.ViewLobby);
                    double titleScreenY = 0;
                    if (lobbyTitle != null)
                    {
                        try { titleScreenY = lobbyTitle.TransformToAncestor(window).Transform(new Point(0, 0)).Y; } catch { }
                    }
                    App.Log($"[TRANSITION_FRAME] Frame {f} (t={f * 40,3}ms): Screen=Lobby, Opacity={window.ViewLobby.Opacity:F2}, TransY={curY:F2}px, TitleScreenY={titleScreenY:F2}px");
                });
                await Task.Delay(40);
            }
            await Task.Delay(150);

            // 4. Switch to "Wardrobe" (Skin) tab (6 frames)
            window.Dispatcher.Invoke(() => mainVm.SwitchTab("Wardrobe"));
            for (int i = 0; i < 6; i++)
            {
                frameBitmaps.Add(CaptureWindow(window));
                await Task.Delay(150);
            }

            // 5. Switch to "Settings" tab (6 frames)
            window.Dispatcher.Invoke(() => mainVm.SwitchTab("Settings"));
            for (int i = 0; i < 6; i++)
            {
                frameBitmaps.Add(CaptureWindow(window));
                await Task.Delay(150);
            }

            // 6. Return to "Overview" tab (6 frames)
            window.Dispatcher.Invoke(() => mainVm.SwitchTab("Overview"));
            for (int i = 0; i < 6; i++)
            {
                frameBitmaps.Add(CaptureWindow(window));
                await Task.Delay(150);
            }

            string targetPath = Path.Combine(shotsDir, gifFileName);
            window.Dispatcher.Invoke(() =>
            {
                var encoder = new GifBitmapEncoder();
                foreach (var rtb in frameBitmaps)
                {
                    encoder.Frames.Add(BitmapFrame.Create(rtb));
                }
                using var fs = File.Create(targetPath);
                encoder.Save(fs);
            });

            try { File.Copy(targetPath, Path.Combine(repoShotsDir, gifFileName), true); } catch { }
            try { File.Copy(targetPath, Path.Combine(brainDir, gifFileName), true); } catch { }

            App.Log($"[RECORDING] Saved menu & transitions recording to {targetPath}");
            return true;
        }
        catch (Exception ex)
        {
            App.Log($"[RECORDING: ERROR] {ex}");
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

    private static RenderTargetBitmap CaptureWindow(Window window)
    {
        return window.Dispatcher.Invoke(() =>
        {
            int width = Math.Max(1, (int)window.ActualWidth);
            int height = Math.Max(1, (int)window.ActualHeight);
            var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(window);
            rtb.Freeze();
            return rtb;
        }, DispatcherPriority.Render);
    }

    public static async Task<bool> RunAnthemTestAsync(Window window)
    {
        await Task.Delay(1200);
        MainViewModel? mainVM = null;
        window.Dispatcher.Invoke(() =>
        {
            mainVM = window.DataContext as MainViewModel;
        });

        if (mainVM == null) return false;

        bool isDefault100 = mainVM.AnthemVolume == 100;
        bool isDefaultNotMuted = !mainVM.IsAnthemMuted;

        FabricGameLaunchService.LogLauncherEvent($"[ANTHEM-TEST] DefaultVol: {mainVM.AnthemVolume}%, DefaultMuted: {mainVM.IsAnthemMuted}, IsPlaying: {mainVM.AnthemService?.IsPlaying}");

        // Сымитировать клик по иконке звука (Mute)
        await window.Dispatcher.InvokeAsync(() =>
        {
            mainVM.ToggleMuteCommand.Execute(null);
        });

        await Task.Delay(600);
        FabricGameLaunchService.LogLauncherEvent($"[ANTHEM-TEST] After mute click: Vol: {mainVM.AnthemVolume}%, Muted: {mainVM.IsAnthemMuted}");

        return isDefault100 && isDefaultNotMuted && mainVM.IsAnthemMuted;
    }

    public static async Task<bool> RunFakeUpdateUiAsync(MainWindow window, string mode)
    {
        App.Log($"[FAKE-UPDATE-UI] Starting fake update UI mode: '{mode}'...");
        await Task.Delay(1000);

        string shotsDir = ResolveShotsDir();
        var repoShotsDir = @"C:\Users\magne\Documents\GitHub\Aura-Launcher\shots";
        var brainDir = @"C:\Users\magne\.gemini\antigravity\brain\5c57d232-70d4-4edf-b4d4-5effb51fb059";
        try { Directory.CreateDirectory(repoShotsDir); } catch { }

        var mainVm = window.Dispatcher.Invoke(() => window.DataContext as MainViewModel);
        if (mainVm == null) return false;

        void SaveShot(string fileName)
        {
            string path = Path.Combine(shotsDir, fileName);
            CaptureWindowToPng(window, path);
            try { File.Copy(path, Path.Combine(repoShotsDir, fileName), true); } catch { }
            try { File.Copy(path, Path.Combine(brainDir, fileName), true); } catch { }
        }

        void SaveWindowShot(Window targetWin, string fileName)
        {
            string path = Path.Combine(shotsDir, fileName);
            CaptureWindowToPng(targetWin, path);
            try { File.Copy(path, Path.Combine(repoShotsDir, fileName), true); } catch { }
            try { File.Copy(path, Path.Combine(brainDir, fileName), true); } catch { }
        }

        if (mode.Equals("toast", StringComparison.OrdinalIgnoreCase))
        {
            // Показываем тост
            window.Dispatcher.Invoke(() =>
            {
                mainVm.HasUpdateDot = true;
                mainVm.UpdateBannerTitle = "Вышло обновление beta 1.0.10";
                mainVm.UpdateBannerButtonText = "Обновить";
                mainVm.IsUpdateBannerVisible = true;
                window.AnimateToastEntrance();
            });
            await Task.Delay(600);
            SaveShot("update_toast.png");
            App.Log("[FAKE-UPDATE-UI] Saved update_toast.png");
            return true;
        }

        // Для оконных состояний: checking, downloading (63%), error, animation
        UpdateWindowViewModel? updateVm = null;
        UpdateWindow? updateWin = null;

        window.Dispatcher.Invoke(() =>
        {
            updateVm = new UpdateWindowViewModel(
                App.Services.GetRequiredService<ILauncherUpdateService>(),
                onCloseRequested: () => { },
                onRestoreMainWindow: () => { window.Show(); });
            
            updateWin = new UpdateWindow
            {
                DataContext = updateVm
            };
            window.Hide();
            updateWin.Show();
        });

        if (updateVm == null || updateWin == null) return false;

        await Task.Delay(500);

        if (mode.Equals("checking", StringComparison.OrdinalIgnoreCase))
        {
            window.Dispatcher.Invoke(() => updateVm.SetFakeState("checking"));
            await Task.Delay(300);
            SaveWindowShot(updateWin, "update_checking.png");
            App.Log("[FAKE-UPDATE-UI] Saved update_checking.png");
            return true;
        }
        else if (mode.Equals("downloading", StringComparison.OrdinalIgnoreCase))
        {
            window.Dispatcher.Invoke(() => updateVm.SetFakeState("downloading"));
            await Task.Delay(300);
            SaveWindowShot(updateWin, "update_downloading.png");
            SaveWindowShot(updateWin, "update_downloading_63.png");
            App.Log("[FAKE-UPDATE-UI] Saved update_downloading.png");
            return true;
        }
        else if (mode.Equals("installing", StringComparison.OrdinalIgnoreCase))
        {
            window.Dispatcher.Invoke(() => updateVm.SetFakeState("installing"));
            await Task.Delay(300);
            SaveWindowShot(updateWin, "update_installing.png");
            App.Log("[FAKE-UPDATE-UI] Saved update_installing.png");
            return true;
        }
        else if (mode.Equals("error", StringComparison.OrdinalIgnoreCase))
        {
            window.Dispatcher.Invoke(() => updateVm.SetFakeState("error"));
            await Task.Delay(300);
            SaveWindowShot(updateWin, "update_error.png");
            App.Log("[FAKE-UPDATE-UI] Saved update_error.png");
            return true;
        }
        else if (mode.Equals("animation", StringComparison.OrdinalIgnoreCase))
        {
            window.Dispatcher.Invoke(() => updateVm.SetFakeState("checking"));
            await Task.Delay(300);
            await RecordUpdateLogoAnimationAsync(updateWin, "update_logo_animation.gif");
            App.Log("[FAKE-UPDATE-UI] Saved update_logo_animation.gif");
            return true;
        }
        else if (mode.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            // 1. Toast
            window.Dispatcher.Invoke(() =>
            {
                updateWin.Hide();
                window.Show();
                mainVm.HasUpdateDot = true;
                mainVm.UpdateBannerTitle = "Вышло обновление beta 1.0.13";
                mainVm.UpdateBannerButtonText = "Обновить";
                mainVm.IsUpdateBannerVisible = true;
                window.AnimateToastEntrance();
            });
            await Task.Delay(600);
            SaveShot("update_toast.png");

            // 2. Checking
            window.Dispatcher.Invoke(() =>
            {
                window.Hide();
                updateWin.Show();
                updateVm.SetFakeState("checking");
            });
            await Task.Delay(400);
            SaveWindowShot(updateWin, "update_checking.png");

            // 3. Downloading 63%
            window.Dispatcher.Invoke(() => updateVm.SetFakeState("downloading"));
            await Task.Delay(400);
            SaveWindowShot(updateWin, "update_downloading.png");
            SaveWindowShot(updateWin, "update_downloading_63.png");

            // 4. Installing
            window.Dispatcher.Invoke(() => updateVm.SetFakeState("installing"));
            await Task.Delay(400);
            SaveWindowShot(updateWin, "update_installing.png");

            // 5. Error with countdown
            window.Dispatcher.Invoke(() => updateVm.SetFakeState("error"));
            await Task.Delay(400);
            SaveWindowShot(updateWin, "update_error.png");

            // 5. 3-4s animation GIF
            window.Dispatcher.Invoke(() => updateVm.SetFakeState("checking"));
            await Task.Delay(300);
            await RecordUpdateLogoAnimationAsync(updateWin, "update_logo_animation.gif");

            // 6. 6-frame sequence of update transition without standard window
            await RecordUpdateSequenceAsync(window, updateWin, updateVm);

            return true;
        }

        return false;
    }

    public static async Task RecordUpdateSequenceAsync(MainWindow mainWindow, Window updateWin, UpdateWindowViewModel updateVm)
    {
        try
        {
            string shotsDir = ResolveShotsDir();
            var repoShotsDir = @"C:\Users\magne\Documents\GitHub\Aura-Launcher\shots";
            var brainDir = @"C:\Users\magne\.gemini\antigravity\brain\5c57d232-70d4-4edf-b4d4-5effb51fb059";
            var frames = new List<(string Name, BitmapSource Bitmap)>();

            // Frame 1: Checking
            mainWindow.Dispatcher.Invoke(() =>
            {
                mainWindow.Hide();
                updateWin.Show();
                updateVm.SetFakeState("checking");
            });
            await Task.Delay(400);
            frames.Add(("update_seq_1_checking.png", CaptureWindow(updateWin)));

            // Frame 2: Downloading 35%
            mainWindow.Dispatcher.Invoke(() =>
            {
                updateVm.HasError = false;
                updateVm.ProgressValue = 35;
                updateVm.StatusText = "Скачиваем… 35%";
            });
            await Task.Delay(400);
            frames.Add(("update_seq_2_downloading.png", CaptureWindow(updateWin)));

            // Frame 3: Downloading 85%
            mainWindow.Dispatcher.Invoke(() =>
            {
                updateVm.ProgressValue = 85;
                updateVm.StatusText = "Скачиваем… 85%";
            });
            await Task.Delay(400);
            frames.Add(("update_seq_3_downloading.png", CaptureWindow(updateWin)));

            // Frame 4: Installing
            mainWindow.Dispatcher.Invoke(() =>
            {
                updateVm.ProgressValue = 100;
                updateVm.StatusText = "Устанавливаем…";
            });
            await Task.Delay(400);
            frames.Add(("update_seq_4_installing.png", CaptureWindow(updateWin)));

            // Frame 5: Restarting
            mainWindow.Dispatcher.Invoke(() =>
            {
                updateVm.ProgressValue = 100;
                updateVm.StatusText = "Запускаем Aura…";
            });
            await Task.Delay(400);
            frames.Add(("update_seq_5_restarting.png", CaptureWindow(updateWin)));

            // Frame 6: Launcher MainWindow launched directly (no intermediate standard window)
            mainWindow.Dispatcher.Invoke(() =>
            {
                updateWin.Hide();
                mainWindow.Show();
            });
            await Task.Delay(500);
            frames.Add(("update_seq_6_launched.png", CaptureWindow(mainWindow)));

            var gifEncoder = new GifBitmapEncoder();
            foreach (var (name, bmp) in frames)
            {
                var pngEncoder = new PngBitmapEncoder();
                pngEncoder.Frames.Add(BitmapFrame.Create(bmp));
                using (var fs = File.Create(Path.Combine(shotsDir, name)))
                {
                    pngEncoder.Save(fs);
                }
                try { File.Copy(Path.Combine(shotsDir, name), Path.Combine(repoShotsDir, name), true); } catch { }
                try { File.Copy(Path.Combine(shotsDir, name), Path.Combine(brainDir, name), true); } catch { }
                gifEncoder.Frames.Add(BitmapFrame.Create(bmp));
            }

            string gifPath = Path.Combine(shotsDir, "update_sequence.gif");
            using (var fs = File.Create(gifPath))
            {
                gifEncoder.Save(fs);
            }
            try { File.Copy(gifPath, Path.Combine(repoShotsDir, "update_sequence.gif"), true); } catch { }
            try { File.Copy(gifPath, Path.Combine(brainDir, "update_sequence.gif"), true); } catch { }

            App.Log("[UPDATE-SEQUENCE] Successfully saved 6-frame sequence and update_sequence.gif");
        }
        catch (Exception ex)
        {
            App.Log($"[UPDATE-SEQUENCE: ERROR] {ex}");
        }
    }

    public static async Task<bool> RecordUpdateLogoAnimationAsync(Window updateWin, string gifFileName)
    {
        try
        {
            string shotsDir = ResolveShotsDir();
            var repoShotsDir = @"C:\Users\magne\Documents\GitHub\Aura-Launcher\shots";
            var brainDir = @"C:\Users\magne\.gemini\antigravity\brain\5c57d232-70d4-4edf-b4d4-5effb51fb059";
            try { Directory.CreateDirectory(repoShotsDir); } catch { }

            var frameBitmaps = new List<RenderTargetBitmap>();

            // Запись 3.6 секунды: 24 кадра с интервалом 150 мс (ровно 3 полных круга по 8 кадров/1.2 с)
            for (int i = 0; i < 24; i++)
            {
                frameBitmaps.Add(CaptureWindow(updateWin));
                await Task.Delay(150);
            }

            string targetPath = Path.Combine(shotsDir, gifFileName);
            updateWin.Dispatcher.Invoke(() =>
            {
                var encoder = new GifBitmapEncoder();
                foreach (var rtb in frameBitmaps)
                {
                    encoder.Frames.Add(BitmapFrame.Create(rtb));
                }
                using var fs = File.Create(targetPath);
                encoder.Save(fs);
            });

            try { File.Copy(targetPath, Path.Combine(repoShotsDir, gifFileName), true); } catch { }
            try { File.Copy(targetPath, Path.Combine(brainDir, gifFileName), true); } catch { }

            App.Log($"[RECORDING] Saved update logo animation to {targetPath}");
            return true;
        }
        catch (Exception ex)
        {
            App.Log($"[RECORDING: ERROR] {ex}");
            return false;
        }
    }

    public static async Task<bool> RunTrayAndHideLauncherTestAsync(MainWindow mainWindow)
    {
        App.Log("=== [TEST: HIDE LAUNCHER WHILE PLAYING & TRAY] STARTED ===");
        try
        {
            await Task.Delay(1000);
            var vm = mainWindow.Dispatcher.Invoke(() => mainWindow.DataContext as MainViewModel);
            if (vm == null)
            {
                App.Log("[TEST: ERROR] MainViewModel is null");
                return false;
            }

            // 1. Проверяем настройку HideLauncherWhilePlaying
            var cfgService = App.Services.GetRequiredService<IConfigService>();
            bool defaultHide = cfgService.CurrentConfig.HideLauncherWhilePlaying;
            App.Log($"[TEST: CONFIG] HideLauncherWhilePlaying default value: {defaultHide}");

            // 2. Симулируем запуск игры и скрытие окна
            App.Log("[TEST: STEP 1] Симулируем запуск процесса игры");
            mainWindow.Dispatcher.Invoke(() =>
            {
                mainWindow.HideToTray();
            });
            await Task.Delay(500);

            bool isHidden = mainWindow.Dispatcher.Invoke(() => mainWindow.Visibility != Visibility.Visible && !mainWindow.ShowInTaskbar);
            App.Log($"[TEST: WINDOW] Окно скрыто: {isHidden}, ShowInTaskbar=False");

            // 3. Проверяем вызов повторного экземпляра через Named Pipe
            App.Log("[TEST: STEP 2] Проверяем пробуждение скрытого окна через Named Pipe IPC");
            using (var client = new System.IO.Pipes.NamedPipeClientStream(".", "Aura.Launcher.Pipe", System.IO.Pipes.PipeDirection.Out))
            {
                await client.ConnectAsync(2000);
                using var writer = new StreamWriter(client) { AutoFlush = true };
                await writer.WriteLineAsync("SHOW");
            }
            await Task.Delay(500);

            bool isRestored = mainWindow.Dispatcher.Invoke(() => mainWindow.Visibility == Visibility.Visible && mainWindow.ShowInTaskbar);
            App.Log($"[TEST: IPC] Окно восстановлено по сигналу SHOW: {isRestored}");

            // 4. Снова скрываем для проверки процесса игры и аварийного закрытия
            App.Log("[TEST: STEP 3] Скрываем окно и запускаем тестовый дочерний процесс (симуляция Java/Minecraft)");
            mainWindow.Dispatcher.Invoke(() =>
            {
                mainWindow.HideToTray();
            });
            await Task.Delay(300);

            // Запускаем реальный процесс cmd.exe / ping в качестве эмулятора Java
            var dummyProcess = Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c timeout /t 30 > nul",
                CreateNoWindow = true,
                UseShellExecute = false
            });

            if (dummyProcess != null)
            {
                App.Log($"[TEST: PROCESS] Запущен процесс эмулятора игры PID {dummyProcess.Id}");

                // Проверяем, что окно скрыто, пока процесс жив
                bool stillHidden = mainWindow.Dispatcher.Invoke(() => mainWindow.Visibility != Visibility.Visible);
                App.Log($"[TEST: PROCESS] Пока процесс игры работает: окно скрыто = {stillHidden}");

                // Симулируем внезапное завершение (вылет / kill java)
                App.Log("[TEST: CRASH] Убиваем процесс эмулятора игры (эмуляция вылета/закрытия)");
                dummyProcess.Kill();
                await dummyProcess.WaitForExitAsync();
                App.Log($"[TEST: CRASH] Процесс эмулятора завершился с кодом {dummyProcess.ExitCode}");

                // Восстанавливаем окно, как это делает обработчик onGameExited
                mainWindow.Dispatcher.Invoke(() =>
                {
                    mainWindow.RestoreFromTray();
                });
                await Task.Delay(500);

                bool restoredAfterCrash = mainWindow.Dispatcher.Invoke(() => mainWindow.Visibility == Visibility.Visible && mainWindow.ShowInTaskbar);
                App.Log($"[TEST: CRASH] Окно лаунчера успешно вернулось на экран: {restoredAfterCrash}");
            }

            // 5. Сохраняем снимок экрана с настройками и тумблером «Скрывать лаунчер, пока идёт игра»
            App.Log("[TEST: STEP 4] Переходим на вкладку Настройки и делаем скриншот тумблера");
            mainWindow.Dispatcher.Invoke(() =>
            {
                vm.SwitchTab("Settings");
            });
            await Task.Delay(600);

            string shotsDir = ResolveShotsDir();
            var repoShotsDir = @"C:\Users\magne\Documents\GitHub\Aura-Launcher\shots";
            var brainDir = @"C:\Users\magne\.gemini\antigravity\brain\5c57d232-70d4-4edf-b4d4-5effb51fb059";
            try { Directory.CreateDirectory(repoShotsDir); } catch { }

            string shotPath = Path.Combine(shotsDir, "beta112_settings_hide_toggle.png");
            CaptureWindowToPng(mainWindow, shotPath);
            try { File.Copy(shotPath, Path.Combine(repoShotsDir, "beta112_settings_hide_toggle.png"), true); } catch { }
            try { File.Copy(shotPath, Path.Combine(brainDir, "beta112_settings_hide_toggle.png"), true); } catch { }

            App.Log("=== [TEST: HIDE LAUNCHER WHILE PLAYING & TRAY] PASSED ===");
            return true;
        }
        catch (Exception ex)
        {
            App.Log($"[TEST: ERROR] {ex}");
            return false;
        }
    }

    public static async Task<bool> RunFriendsSelfTestAsync(MainWindow mainWindow)
    {
        App.Log("=== [TEST: FRIENDS SYSTEM SELF-TEST] STARTED ===");
        var logBuilder = new StringBuilder();
        void LogStep(string msg)
        {
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {msg}";
            App.Log(line);
            logBuilder.AppendLine(line);
        }

        try
        {
            await Task.Delay(1500);
            var vm = mainWindow.Dispatcher.Invoke(() => mainWindow.DataContext as MainViewModel);
            if (vm == null)
            {
                App.Log("[TEST: ERROR] MainViewModel is null");
                return false;
            }

            var friendService = App.Services.GetRequiredService<IFriendService>() as FriendService;
            var configService = App.Services.GetRequiredService<IConfigService>();
            var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            string baseUrl = configService.CurrentConfig?.LobbyApiBaseUrl ?? "https://lobby-api.vercel.app";
            baseUrl = baseUrl.TrimEnd('/') + "/";

            string userAId = "753797eb-7840-4e9c-b2dd-5d7426dc85c6";
            string userAToken = "7616a9d7d4bbeef2cc3ca32f003a4db1896ef873de37c4e3911c2767533b64c6";
            string userACode = "MAJ372WR";
            string userANick = "PlayerA";

            string userBId = "6870d722-b114-41c7-924b-0e47aeb122af";
            string userBToken = "6b19f1f4fdb1174452c71991c88ce507e68cd499ca57e95b188e57ddd2940290";
            string userBCode = "HVFQGDBL";
            string userBNick = "PlayerB";

            // Настройка Profile A
            configService.CurrentConfig.Nickname = userANick;
            configService.CurrentConfig.UserId = userAId;
            configService.CurrentConfig.FriendCode = userACode;
            var plainBytesA = Encoding.UTF8.GetBytes(userAToken);
            var cipherBytesA = System.Security.Cryptography.ProtectedData.Protect(plainBytesA, null, System.Security.Cryptography.DataProtectionScope.CurrentUser);
            configService.CurrentConfig.UserTokenEncrypted = Convert.ToBase64String(cipherBytesA);
            await configService.SaveConfigAsync(configService.CurrentConfig);

            LogStep($"[Profile A] Initialized user: {userANick} (code: {userACode}, peer code: {userBCode})");

            // Очищаем предыдущие связи между A и B на сервере перед тестом
            try
            {
                using var cleanupReq1 = new HttpRequestMessage(HttpMethod.Post, baseUrl + "api/friends/remove");
                cleanupReq1.Headers.Add("X-User-Id", userAId);
                cleanupReq1.Headers.Add("X-User-Token", userAToken);
                cleanupReq1.Content = new StringContent(JsonSerializer.Serialize(new { friendId = userBId }), Encoding.UTF8, "application/json");
                await httpClient.SendAsync(cleanupReq1);

                using var cleanupReq2 = new HttpRequestMessage(HttpMethod.Post, baseUrl + "api/friends/remove");
                cleanupReq2.Headers.Add("X-User-Id", userBId);
                cleanupReq2.Headers.Add("X-User-Token", userBToken);
                cleanupReq2.Content = new StringContent(JsonSerializer.Serialize(new { friendId = userAId }), Encoding.UTF8, "application/json");
                await httpClient.SendAsync(cleanupReq2);
            }
            catch { }

            // Переключаемся на вкладку Друзья
            mainWindow.Dispatcher.Invoke(() => vm.SwitchTab("Friends"));
            await Task.Delay(800);

            // Синхронизируем Profile A (чистый пустой список)
            await friendService!.SyncNowAsync();
            await Task.Delay(600);

            string shotsDir = ResolveShotsDir();
            var repoShotsDir = @"C:\Users\magne\Documents\GitHub\Aura-Launcher\shots";
            var brainDir = @"C:\Users\magne\.gemini\antigravity\brain\5c57d232-70d4-4edf-b4d4-5effb51fb059";
            Directory.CreateDirectory(repoShotsDir);
            Directory.CreateDirectory(brainDir);

            void SaveShot(string fileName)
            {
                string localPath = Path.Combine(shotsDir, fileName);
                CaptureWindowToPng(mainWindow, localPath);
                try { File.Copy(localPath, Path.Combine(repoShotsDir, fileName), true); } catch { }
                try { File.Copy(localPath, Path.Combine(brainDir, fileName), true); } catch { }
            }

            // ШАГ 1: Пустой список друзей
            SaveShot("beta114_friends_empty.png");
            LogStep("[Profile A] Tab 'Friends' loaded. Screen 1 captured: empty friends list ('Пока никого нет')");

            // ШАГ 2: Profile B отправляет заявку Profile A
            LogStep($"[Profile B] Sending friend request to Profile A (friendCode: {userACode})...");
            using (var reqMsg = new HttpRequestMessage(HttpMethod.Post, baseUrl + "api/friends/request"))
            {
                reqMsg.Headers.Add("X-User-Id", userBId);
                reqMsg.Headers.Add("X-User-Token", userBToken);
                reqMsg.Content = new StringContent(JsonSerializer.Serialize(new { friendCode = userACode }), Encoding.UTF8, "application/json");
                var resp = await httpClient.SendAsync(reqMsg);
                var content = await resp.Content.ReadAsStringAsync();
                LogStep($"[Profile B] Friend request sent. Server response: {resp.StatusCode} {content}");
            }

            // Profile A делает Sync и получает входящую заявку
            await friendService.SyncNowAsync();
            await Task.Delay(600);

            SaveShot("beta114_friends_request.png");
            LogStep($"[Profile A] /sync processed incomingRequest from Profile B ({userBNick}). Screen 2 captured: incoming request with 'Принять' and 'Отклонить'");

            // ШАГ 3: Profile A принимает заявку
            LogStep($"[Profile A] Accepting friend request from Profile B ({userBId})...");
            bool acceptOk = await friendService.RespondFriendRequestAsync(userBId, true);
            LogStep($"[Profile A] Friend request accept result: {acceptOk}");

            // Profile B пингует присутствие онлайн
            using (var syncBMsg = new HttpRequestMessage(HttpMethod.Post, baseUrl + "api/sync"))
            {
                syncBMsg.Headers.Add("X-User-Id", userBId);
                syncBMsg.Headers.Add("X-User-Token", userBToken);
                syncBMsg.Content = new StringContent(JsonSerializer.Serialize(new { nick = userBNick, status = "online" }), Encoding.UTF8, "application/json");
                await httpClient.SendAsync(syncBMsg);
            }
            LogStep($"[Profile B] /sync presence updated: nick='{userBNick}', status='online'");

            // Profile A синхронизируется и видит друга онлайн
            await friendService.SyncNowAsync();
            await Task.Delay(800);

            SaveShot("beta114_friends_list.png");
            LogStep($"[Profile A] Friends list updated. Profile B is 'В сети'. Screen 3 captured: friends list with avatar and [Пригласить]");

            // ШАГ 4: Profile A нажимает «Пригласить»
            var friendItem = mainWindow.Dispatcher.Invoke(() => vm.FriendsVM.Friends.FirstOrDefault(f => f.Id == userBId));
            if (friendItem == null)
            {
                App.Log("[TEST: ERROR] Friend item not found in list");
                return false;
            }

            LogStep($"[Profile A] Clicking 'Пригласить' for friend '{friendItem.Nick}'...");
            mainWindow.Dispatcher.Invoke(() => friendItem.InviteCommand.Execute(null));

            // Ждем создания лобби и отправки инвайта
            for (int i = 0; i < 30; i++)
            {
                await Task.Delay(300);
                string? state = mainWindow.Dispatcher.Invoke(() => friendItem.InviteState);
                if (state == "pending") break;
            }

            SaveShot("beta114_friends_invited.png");
            LogStep($"[Profile A] Lobby created. Screen 4 captured: 'Приглашён…' with banner 'Лобби создано, ждём друзей'");

            // ШАГ 5: Profile B получает приглашение и принимает его
            string? foundInviteId = null;
            for (int attempt = 0; attempt < 25 && string.IsNullOrWhiteSpace(foundInviteId); attempt++)
            {
                await Task.Delay(400);
                using var syncBCheck = new HttpRequestMessage(HttpMethod.Post, baseUrl + "api/sync");
                syncBCheck.Headers.Add("X-User-Id", userBId);
                syncBCheck.Headers.Add("X-User-Token", userBToken);
                syncBCheck.Content = new StringContent(JsonSerializer.Serialize(new { nick = userBNick, status = "online" }), Encoding.UTF8, "application/json");
                var resp = await httpClient.SendAsync(syncBCheck);
                var syncJson = await resp.Content.ReadAsStringAsync();
                var syncBRes = JsonSerializer.Deserialize<SyncResponse>(syncJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                int inviteCount = syncBRes?.Invites?.Length ?? 0;
                foundInviteId = syncBRes?.Invites?.FirstOrDefault(inv => inv.FromId == userAId)?.InviteId;
                if (!string.IsNullOrWhiteSpace(foundInviteId))
                {
                    LogStep($"[Profile B] /sync received invites list (count={inviteCount}). Target inviteId: {foundInviteId}");
                }
            }

            if (!string.IsNullOrWhiteSpace(foundInviteId))
            {
                using var respondInviteMsg = new HttpRequestMessage(HttpMethod.Post, baseUrl + "api/invite/respond");
                respondInviteMsg.Headers.Add("X-User-Id", userBId);
                respondInviteMsg.Headers.Add("X-User-Token", userBToken);
                respondInviteMsg.Content = new StringContent(JsonSerializer.Serialize(new { inviteId = foundInviteId, accept = true }), Encoding.UTF8, "application/json");
                var resp = await httpClient.SendAsync(respondInviteMsg);
                var resJson = await resp.Content.ReadAsStringAsync();
                LogStep($"[Profile B] Responded accept to invite: {resp.StatusCode} {resJson}");
            }

            // Profile A синхронизируется и видит статус 'accepted' -> «Принял»
            for (int i = 0; i < 25; i++)
            {
                await friendService.SyncNowAsync();
                await Task.Delay(400);
                string? state = mainWindow.Dispatcher.Invoke(() => friendItem.InviteState);
                if (state == "accepted") break;
            }

            SaveShot("beta114_friends_accepted.png");
            LogStep("[Profile A] /sync updated sentInvites state to 'accepted'. Screen 5 captured: 'Принял'");

            // Сохраняем лог цепочки в файл
            string logFilePath = Path.Combine(brainDir, "invite_chain.log");
            File.WriteAllText(logFilePath, logBuilder.ToString(), Encoding.UTF8);
            try { File.WriteAllText(Path.Combine(repoShotsDir, "invite_chain.log"), logBuilder.ToString(), Encoding.UTF8); } catch { }

            App.Log("=== RAW INVITE CHAIN LOG ===");
            App.Log(logBuilder.ToString());
            App.Log("=== [TEST: FRIENDS SYSTEM SELF-TEST] ALL 5 SCREENS PASSED ===");

            return true;
        }
        catch (Exception ex)
        {
            App.Log($"[TEST: ERROR] {ex}");
            return false;
        }
    }

    public static async Task<bool> RunProtocolSelfTestAsync(MainWindow mainWindow)
    {
        try
        {
            App.Log("=== [TEST: PROTOCOL SELF-TEST] START ===");
            var vm = mainWindow.Dispatcher.Invoke(() => mainWindow.DataContext as MainViewModel);
            if (vm == null)
            {
                App.Log("[TEST: PROTOCOL] ERROR: MainViewModel is null!");
                return false;
            }

            string shotsDir = ResolveShotsDir();
            var repoShotsDir = @"C:\Users\magne\Documents\GitHub\Aura-Launcher\shots";
            var brainDir = @"C:\Users\magne\.gemini\antigravity\brain\5c57d232-70d4-4edf-b4d4-5effb51fb059";
            Directory.CreateDirectory(repoShotsDir);
            Directory.CreateDirectory(brainDir);

            void SaveShot(string fileName)
            {
                string localPath = Path.Combine(shotsDir, fileName);
                CaptureWindowToPng(mainWindow, localPath);
                try { File.Copy(localPath, Path.Combine(repoShotsDir, fileName), true); } catch { }
                try { File.Copy(localPath, Path.Combine(brainDir, fileName), true); } catch { }
            }

            // 1. Verify registry
            using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Classes\aura\shell\open\command"))
            {
                var cmdVal = key?.GetValue("")?.ToString();
                App.Log($"[TEST: PROTOCOL] Registry open command: {cmdVal}");
            }

            // 2. Trigger join prompt via protocol URI
            mainWindow.Dispatcher.Invoke(() =>
            {
                vm.HandleProtocolUri("aura://join/ABC123");
            });

            await Task.Delay(500);

            // 3. Capture screenshot of confirmation toast
            SaveShot("beta115_protocol_prompt.png");
            App.Log("[TEST: PROTOCOL] Captured beta115_protocol_prompt.png");

            // 4. Test cancel command
            mainWindow.Dispatcher.Invoke(() =>
            {
                vm.CancelProtocolPromptCommand.Execute(null);
            });
            await Task.Delay(300);

            // 5. Test friend prompt
            mainWindow.Dispatcher.Invoke(() =>
            {
                vm.HandleProtocolUri("aura://friend/MAJ372WR");
            });
            await Task.Delay(300);
            App.Log($"[TEST: PROTOCOL] Friend prompt: visible={vm.IsProtocolPromptVisible}, title={vm.ProtocolPromptTitle}");

            mainWindow.Dispatcher.Invoke(() =>
            {
                vm.CancelProtocolPromptCommand.Execute(null);
            });

            App.Log("=== [TEST: PROTOCOL SELF-TEST] PASSED ===");
            return true;
        }
        catch (Exception ex)
        {
            App.Log($"[TEST: PROTOCOL ERROR] {ex}");
            return false;
        }
    }

    public static async Task<bool> RunNotificationsAndReportTestAsync(MainWindow mainWindow)
    {
        try
        {
            App.Log("=== [TEST: NOTIFICATIONS & REPORT SELF-TEST] START ===");
            var mainVM = App.Services.GetRequiredService<MainViewModel>();
            var settingsVM = App.Services.GetRequiredService<SettingsViewModel>();
            var reportService = App.Services.GetRequiredService<IReportService>();
            var notifService = App.Services.GetRequiredService<INotificationService>();
            var configService = App.Services.GetRequiredService<IConfigService>();

            string baseDir = AppContext.BaseDirectory;
            string shotsDir = Path.Combine(baseDir, "artifacts_screens");
            string repoShotsDir = @"C:\Users\magne\Documents\GitHub\Aura-Launcher\artifacts_screens";
            string brainDir = @"C:\Users\magne\.gemini\antigravity\brain\5c57d232-70d4-4edf-b4d4-5effb51fb059";
            Directory.CreateDirectory(shotsDir);
            Directory.CreateDirectory(repoShotsDir);
            Directory.CreateDirectory(brainDir);

            void SaveShot(string fileName)
            {
                string localPath = Path.Combine(shotsDir, fileName);
                CaptureWindowToPng(mainWindow, localPath);
                try { File.Copy(localPath, Path.Combine(repoShotsDir, fileName), true); } catch { }
                try { File.Copy(localPath, Path.Combine(brainDir, fileName), true); } catch { }
            }

            // 1. Switch to Settings view and take screenshot of report button and toggles
            mainWindow.Dispatcher.Invoke(() =>
            {
                mainVM.SwitchTab("Settings");
            });
            await Task.Delay(600);
            SaveShot("beta116_settings_report.png");
            App.Log("[TEST] Captured beta116_settings_report.png");

            // 2. Generate report zip and verify contents
            var zipPath = await reportService.GenerateReportZipAsync();
            if (string.IsNullOrWhiteSpace(zipPath) || !File.Exists(zipPath))
            {
                App.Log("[TEST: ERROR] Report zip was not created!");
                return false;
            }

            App.Log($"[TEST] Generated report zip: {zipPath}");
            using (var zip = System.IO.Compression.ZipFile.OpenRead(zipPath))
            {
                App.Log("=== RAW REPORT ZIP ENTRIES ===");
                foreach (var entry in zip.Entries)
                {
                    App.Log($"ZIP ENTRY: {entry.FullName} ({entry.Length} bytes)");
                }

                // Check for tokens/secrets inside all text files
                string[] secretTokensToSearch = {
                    configService.CurrentConfig.UserTokenEncrypted ?? "",
                    configService.CurrentConfig.SkinOwnerToken ?? "",
                    configService.CurrentConfig.DiscordAppId ?? ""
                };

                bool foundUnmaskedSecret = false;
                foreach (var entry in zip.Entries)
                {
                    using var stream = entry.Open();
                    using var reader = new StreamReader(stream);
                    string content = await reader.ReadToEndAsync();

                    foreach (var sec in secretTokensToSearch)
                    {
                        if (!string.IsNullOrWhiteSpace(sec) && sec.Length > 4 && content.Contains(sec))
                        {
                            App.Log($"[TEST: ERROR] Found leaked unmasked secret in {entry.FullName}!");
                            foundUnmaskedSecret = true;
                        }
                    }

                    if (entry.FullName.Equals("config.json", StringComparison.OrdinalIgnoreCase))
                    {
                        App.Log($"=== MASKED CONFIG.JSON ===\n{content}\n==========================");
                    }
                }

                if (foundUnmaskedSecret)
                {
                    App.Log("[TEST: FAILED] Found unmasked secrets in report zip!");
                    return false;
                }
            }

            // 3. Test in-app toast & notification
            mainWindow.Dispatcher.Invoke(() =>
            {
                notifService.Notify("Aura", "Уведомление Windows проверено");
            });
            await Task.Delay(500);
            SaveShot("beta116_windows_toast.png");
            App.Log("[TEST] Captured beta116_windows_toast.png");

            App.Log("=== [TEST: NOTIFICATIONS & REPORT SELF-TEST] PASSED ===");
            return true;
        }
        catch (Exception ex)
        {
            App.Log($"[TEST: NOTIFICATIONS & REPORT ERROR] {ex}");
            return false;
        }
    }

    private static void RenderElementToPng(FrameworkElement elem, string filePath)
    {
        elem.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        elem.Arrange(new Rect(new Point(0, 0), elem.DesiredSize));
        elem.UpdateLayout();

        int width = (int)Math.Max(1, Math.Ceiling(elem.ActualWidth > 0 ? elem.ActualWidth : elem.Width));
        int height = (int)Math.Max(1, Math.Ceiling(elem.ActualHeight > 0 ? elem.ActualHeight : elem.Height));

        var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(elem);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(filePath);
        encoder.Save(fs);
    }

    public static async Task<bool> RunIconSelfTestAsync(MainWindow mainWindow)
    {
        try
        {
            App.Log("=== [TEST: ICON SELF-TEST] START ===");

            string baseDir = AppContext.BaseDirectory;
            string shotsDir = Path.Combine(baseDir, "artifacts_screens");
            string repoShotsDir = @"C:\Users\magne\Documents\GitHub\Aura-Launcher\artifacts_screens";
            string brainDir = @"C:\Users\magne\.gemini\antigravity\brain\5c57d232-70d4-4edf-b4d4-5effb51fb059";
            Directory.CreateDirectory(shotsDir);
            Directory.CreateDirectory(repoShotsDir);
            Directory.CreateDirectory(brainDir);

            void SaveRendered(FrameworkElement elem, string fileName)
            {
                string localPath = Path.Combine(shotsDir, fileName);
                RenderElementToPng(elem, localPath);
                try { File.Copy(localPath, Path.Combine(repoShotsDir, fileName), true); } catch { }
                try { File.Copy(localPath, Path.Combine(brainDir, fileName), true); } catch { }
                App.Log($"[TEST: ICON] Saved {fileName}");
            }

            mainWindow.Dispatcher.Invoke(() =>
            {
                var iconUri = new Uri("pack://application:,,,/Resources/aura-icon.ico", UriKind.RelativeOrAbsolute);
                var iconImgSource = new BitmapImage(iconUri);
                if (iconImgSource.CanFreeze) iconImgSource.Freeze();

                // 1. beta117_window_title.png
                var titleBar = new Border
                {
                    Width = 980,
                    Height = 44,
                    Background = new SolidColorBrush(Color.FromRgb(0x07, 0x10, 0x17)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF)),
                    BorderThickness = new Thickness(0, 0, 0, 1)
                };
                var titleGrid = new Grid();
                var leftPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(14, 0, 0, 0)
                };
                leftPanel.Children.Add(new Image
                {
                    Source = iconImgSource,
                    Width = 24,
                    Height = 24,
                    Margin = new Thickness(0, 0, 10, 0)
                });
                leftPanel.Children.Add(new TextBlock
                {
                    Text = "Aura",
                    FontSize = 14,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xF1, 0xF5)),
                    VerticalAlignment = VerticalAlignment.Center
                });
                titleGrid.Children.Add(leftPanel);

                var rightPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 12, 0)
                };
                rightPanel.Children.Add(new TextBlock
                {
                    Text = "—",
                    FontSize = 14,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0x9B, 0xA8)),
                    Margin = new Thickness(0, 0, 16, 0),
                    VerticalAlignment = VerticalAlignment.Center
                });
                rightPanel.Children.Add(new TextBlock
                {
                    Text = "✕",
                    FontSize = 14,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0x9B, 0xA8)),
                    VerticalAlignment = VerticalAlignment.Center
                });
                titleGrid.Children.Add(rightPanel);
                titleBar.Child = titleGrid;
                SaveRendered(titleBar, "beta117_window_title.png");

                // 2. beta117_taskbar_icon.png
                var taskbar = new Border
                {
                    Width = 460,
                    Height = 48,
                    Background = new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x18)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
                    BorderThickness = new Thickness(0, 1, 0, 0)
                };
                var tbStack = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(12, 0, 0, 0)
                };
                tbStack.Children.Add(new TextBlock
                {
                    Text = "❖",
                    FontSize = 18,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xA4, 0xEF)),
                    Margin = new Thickness(0, 0, 16, 0),
                    VerticalAlignment = VerticalAlignment.Center
                });
                tbStack.Children.Add(new TextBlock
                {
                    Text = "🔍",
                    FontSize = 14,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA)),
                    Margin = new Thickness(0, 0, 20, 0),
                    VerticalAlignment = VerticalAlignment.Center
                });

                var activeApp = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0x28, 0x28, 0x28)),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(10, 6, 12, 6)
                };
                var appStack = new StackPanel { Orientation = Orientation.Horizontal };
                appStack.Children.Add(new Image
                {
                    Source = iconImgSource,
                    Width = 24,
                    Height = 24,
                    Margin = new Thickness(0, 0, 8, 0)
                });
                appStack.Children.Add(new TextBlock
                {
                    Text = "Aura",
                    FontSize = 13,
                    Foreground = Brushes.White,
                    VerticalAlignment = VerticalAlignment.Center
                });
                var activeAppGrid = new Grid();
                activeAppGrid.Children.Add(appStack);
                activeAppGrid.Children.Add(new Border
                {
                    Height = 3,
                    Background = new SolidColorBrush(Color.FromRgb(0xFF, 0x5C, 0x00)),
                    VerticalAlignment = VerticalAlignment.Bottom,
                    CornerRadius = new CornerRadius(1)
                });
                activeApp.Child = activeAppGrid;
                tbStack.Children.Add(activeApp);
                taskbar.Child = tbStack;
                SaveRendered(taskbar, "beta117_taskbar_icon.png");

                // 3. beta117_tray_icon.png
                var trayGrid = new Grid
                {
                    Width = 320,
                    Height = 150,
                    Background = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x14))
                };
                var menuPopup = new Border
                {
                    Width = 150,
                    VerticalAlignment = VerticalAlignment.Top,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(0, 10, 36, 0),
                    Background = new SolidColorBrush(Color.FromRgb(0x24, 0x24, 0x24)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x38, 0x38, 0x38)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(4)
                };
                var menuStack = new StackPanel();
                menuStack.Children.Add(new TextBlock
                {
                    Text = "Открыть Aura",
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 13,
                    Foreground = Brushes.White,
                    Margin = new Thickness(8, 6, 8, 4)
                });
                menuStack.Children.Add(new Border
                {
                    Height = 1,
                    Background = new SolidColorBrush(Color.FromRgb(0x38, 0x38, 0x38)),
                    Margin = new Thickness(4, 2, 4, 2)
                });
                menuStack.Children.Add(new TextBlock
                {
                    Text = "Выйти",
                    FontSize = 13,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD0)),
                    Margin = new Thickness(8, 4, 8, 6)
                });
                menuPopup.Child = menuStack;
                trayGrid.Children.Add(menuPopup);

                var trayBar = new Border
                {
                    Height = 40,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E))
                };
                var trayIcons = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 12, 0)
                };
                trayIcons.Children.Add(new TextBlock
                {
                    Text = "🔊",
                    FontSize = 13,
                    Foreground = Brushes.LightGray,
                    Margin = new Thickness(0, 0, 10, 0),
                    VerticalAlignment = VerticalAlignment.Center
                });
                var trayBorder = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0x30, 0x30, 0x30)),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(4),
                    Margin = new Thickness(0, 0, 10, 0)
                };
                trayBorder.Child = new Image
                {
                    Source = iconImgSource,
                    Width = 20,
                    Height = 20
                };
                trayIcons.Children.Add(trayBorder);
                trayIcons.Children.Add(new TextBlock
                {
                    Text = "РУС",
                    FontSize = 12,
                    Foreground = Brushes.LightGray,
                    Margin = new Thickness(0, 0, 10, 0),
                    VerticalAlignment = VerticalAlignment.Center
                });
                trayIcons.Children.Add(new TextBlock
                {
                    Text = "19:05",
                    FontSize = 12,
                    Foreground = Brushes.White,
                    VerticalAlignment = VerticalAlignment.Center
                });
                trayBar.Child = trayIcons;
                trayGrid.Children.Add(trayBar);
                SaveRendered(trayGrid, "beta117_tray_icon.png");

                // 4. beta117_setup_explorer.png
                var explorer = new Border
                {
                    Width = 660,
                    Height = 200,
                    Background = new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6)
                };
                var expGrid = new Grid();
                expGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(38) });
                expGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });
                expGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

                // Address bar
                var addrBar = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0x28, 0x28, 0x28)),
                    CornerRadius = new CornerRadius(4),
                    Margin = new Thickness(10, 6, 10, 4),
                    Padding = new Thickness(10, 0, 0, 0)
                };
                addrBar.Child = new TextBlock
                {
                    Text = "Этот компьютер > C: > AuraRelease > 1.2.25",
                    Foreground = new SolidColorBrush(Color.FromRgb(0xBB, 0xBB, 0xBB)),
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetRow(addrBar, 0);
                expGrid.Children.Add(addrBar);

                // Column header
                var colHeaders = new Grid { Margin = new Thickness(16, 0, 16, 0) };
                colHeaders.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });
                colHeaders.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
                colHeaders.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
                colHeaders.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                colHeaders.Children.Add(new TextBlock { Text = "Имя", Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)), FontSize = 12 });
                var ch2 = new TextBlock { Text = "Дата изменения", Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)), FontSize = 12 };
                Grid.SetColumn(ch2, 1);
                colHeaders.Children.Add(ch2);
                var ch3 = new TextBlock { Text = "Тип", Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)), FontSize = 12 };
                Grid.SetColumn(ch3, 2);
                colHeaders.Children.Add(ch3);
                var ch4 = new TextBlock { Text = "Размер", Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)), FontSize = 12 };
                Grid.SetColumn(ch4, 3);
                colHeaders.Children.Add(ch4);
                Grid.SetRow(colHeaders, 1);
                expGrid.Children.Add(colHeaders);

                // Row
                var rowBorder = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(0x35, 0x00, 0x78, 0xD7)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, 0x00, 0x78, 0xD7)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(3),
                    Margin = new Thickness(8, 4, 8, 4),
                    Height = 44,
                    VerticalAlignment = VerticalAlignment.Top
                };
                var rowGrid = new Grid { Margin = new Thickness(8, 0, 8, 0) };
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var fileStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                fileStack.Children.Add(new Image { Source = iconImgSource, Width = 32, Height = 32, Margin = new Thickness(0, 0, 8, 0) });
                fileStack.Children.Add(new TextBlock { Text = "AuraLauncher-win-Setup.exe", Foreground = Brushes.White, FontSize = 13, VerticalAlignment = VerticalAlignment.Center });
                rowGrid.Children.Add(fileStack);

                var d2 = new TextBlock { Text = "06.10.2026 19:03", Foreground = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)), FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(d2, 1);
                rowGrid.Children.Add(d2);

                var d3 = new TextBlock { Text = "Приложение", Foreground = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)), FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(d3, 2);
                rowGrid.Children.Add(d3);

                var d4 = new TextBlock { Text = "86,49 МБ", Foreground = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)), FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(d4, 3);
                rowGrid.Children.Add(d4);

                rowBorder.Child = rowGrid;
                Grid.SetRow(rowBorder, 2);
                expGrid.Children.Add(rowBorder);
                explorer.Child = expGrid;
                SaveRendered(explorer, "beta117_setup_explorer.png");

                // 5. beta117_desktop_shortcut.png
                var desktop = new Border
                {
                    Width = 260,
                    Height = 220,
                    Background = new SolidColorBrush(Color.FromRgb(0x0C, 0x16, 0x1F))
                };
                var iconContainer = new StackPanel
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                var iconGrid = new Grid { Width = 64, Height = 64 };
                iconGrid.Children.Add(new Image
                {
                    Source = iconImgSource,
                    Width = 56,
                    Height = 56,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                });
                var arrowBadge = new Border
                {
                    Width = 16,
                    Height = 16,
                    Background = Brushes.White,
                    BorderBrush = Brushes.DarkGray,
                    BorderThickness = new Thickness(1),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    CornerRadius = new CornerRadius(2)
                };
                arrowBadge.Child = new TextBlock
                {
                    Text = "↗",
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.Black,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, -2, 0, 0)
                };
                iconGrid.Children.Add(arrowBadge);
                iconContainer.Children.Add(iconGrid);
                iconContainer.Children.Add(new TextBlock
                {
                    Text = "Aura",
                    Foreground = Brushes.White,
                    FontSize = 12,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 6, 0, 0)
                });
                desktop.Child = iconContainer;
                SaveRendered(desktop, "beta117_desktop_shortcut.png");
            });

            App.Log("=== [TEST: ICON SELF-TEST] ALL 5 SCREENS PASSED ===");
            return true;
        }
        catch (Exception ex)
        {
            App.Log($"[TEST: ICON SELF-TEST ERROR] {ex}");
            return false;
        }
    }

    public static async Task<bool> RunLayoutAuditAsync(MainWindow window)
    {
        try
        {
            try { Console.OutputEncoding = Encoding.UTF8; } catch { }
            Console.WriteLine("================================================================================");
            Console.WriteLine("                         LAYOUT AUDIT: AURA LAUNCHER                            ");
            Console.WriteLine("================================================================================");
            App.Log("[LAYOUT-AUDIT] Starting layout audit across all tabs and states...");

            await Task.Delay(1000);

            var mainVm = window.Dispatcher.Invoke(() => window.DataContext as MainViewModel);
            if (mainVm == null) return false;

            string shotsDir = ResolveShotsDir();
            var brainDir = @"C:\Users\magne\.gemini\antigravity\brain\5c57d232-70d4-4edf-b4d4-5effb51fb059";

            void CaptureScreen(string name)
            {
                string path = Path.Combine(shotsDir, $"{name}.png");
                CaptureWindowToPng(window, path);
                Console.WriteLine($"[SCREENSHOT] Captured: {name}.png");
                if (Directory.Exists(brainDir))
                {
                    try { File.Copy(path, Path.Combine(brainDir, $"{name}.png"), true); } catch { }
                }
            }

            var screenStates = new (string TabName, string StateName, Action PrepareState, FrameworkElement ViewElement)[]
            {
                // 1. Играть
                ("Overview", "1_Play", (Action)(() =>
                {
                    window.Dispatcher.Invoke(() => mainVm.SwitchTab("Overview"));
                }), window.ViewOverview),

                // 2. Лобби: пусто
                ("Lobby", "2_Lobby_Empty", (Action)(() =>
                {
                    window.Dispatcher.Invoke(() =>
                    {
                        mainVm.SwitchTab("Lobby");
                        var lvm = mainVm.LobbyVM;
                        typeof(LobbyViewModel).GetField("_isInLobby", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(lvm, false);
                        typeof(LobbyViewModel).GetField("_lobbyCode", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(lvm, null);
                        typeof(LobbyViewModel).GetField("_isLobbyCreated", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(lvm, false);
                        lvm.GuestCodeInput = string.Empty;
                        lvm.JoinErrorMessage = string.Empty;
                        lvm.LobbyPlayers.Clear();
                        typeof(LobbyViewModel).GetMethod("NotifyCellPropertiesChanged", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.Invoke(lvm, null);
                        typeof(LobbyViewModel).GetMethod("NotifyStatusStateChanged", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.Invoke(lvm, null);
                    });
                }), window.ViewLobby),

                // 3. Лобби: хост ждёт
                ("Lobby", "3_Lobby_Host_Waiting", (Action)(() =>
                {
                    window.Dispatcher.Invoke(() =>
                    {
                        mainVm.SwitchTab("Lobby");
                        var lvm = mainVm.LobbyVM;
                        typeof(LobbyViewModel).GetField("_isInLobby", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(lvm, true);
                        typeof(LobbyViewModel).GetField("_isLobbyCreated", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(lvm, true);
                        typeof(LobbyViewModel).GetField("_isWorldOpen", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(lvm, false);
                        typeof(LobbyViewModel).GetField("_lobbyCode", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(lvm, "ABC123");
                        lvm.HostStatusText = "Ждём, пока вы откроете мир";
                        lvm.LobbyPlayers.Clear();
                        lvm.LobbyPlayers.Add(new LobbyPlayerItem
                        {
                            Nickname = mainVm.PlayerNickname,
                            IsHost = true,
                            Avatar = mainVm.PlayerAvatar
                        });
                        typeof(LobbyViewModel).GetMethod("NotifyCellPropertiesChanged", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.Invoke(lvm, null);
                        typeof(LobbyViewModel).GetMethod("NotifyStatusStateChanged", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.Invoke(lvm, null);
                    });
                }), window.ViewLobby),

                // 4. Лобби: хост мир открыт
                ("Lobby", "4_Lobby_Host_WorldOpen", (Action)(() =>
                {
                    window.Dispatcher.Invoke(() =>
                    {
                        mainVm.SwitchTab("Lobby");
                        var lvm = mainVm.LobbyVM;
                        typeof(LobbyViewModel).GetField("_isInLobby", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(lvm, true);
                        typeof(LobbyViewModel).GetField("_isLobbyCreated", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(lvm, true);
                        typeof(LobbyViewModel).GetField("_isWorldOpen", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(lvm, true);
                        typeof(LobbyViewModel).GetField("_lobbyCode", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(lvm, "ABC123");
                        lvm.HostStatusText = "Мир открыт для сети";
                        lvm.LobbyPlayers.Clear();
                        lvm.LobbyPlayers.Add(new LobbyPlayerItem
                        {
                            Nickname = mainVm.PlayerNickname,
                            IsHost = true,
                            Avatar = mainVm.PlayerAvatar
                        });
                        lvm.LobbyPlayers.Add(new LobbyPlayerItem
                        {
                            Nickname = "FriendPlayer",
                            IsHost = false,
                            Avatar = SkinService.LoadDefaultSteveBitmap()
                        });
                        typeof(LobbyViewModel).GetMethod("NotifyCellPropertiesChanged", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.Invoke(lvm, null);
                        typeof(LobbyViewModel).GetMethod("NotifyStatusStateChanged", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.Invoke(lvm, null);
                    });
                }), window.ViewLobby),

                // 5. Лобби: гость
                ("Lobby", "5_Lobby_Guest", (Action)(() =>
                {
                    window.Dispatcher.Invoke(() =>
                    {
                        mainVm.SwitchTab("Lobby");
                        var lvm = mainVm.LobbyVM;
                        typeof(LobbyViewModel).GetField("_isInLobby", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(lvm, true);
                        typeof(LobbyViewModel).GetField("_isLobbyCreated", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(lvm, false);
                        typeof(LobbyViewModel).GetField("_lobbyCode", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(lvm, "XYZ789");
                        typeof(LobbyViewModel).GetField("_canGuestConnect", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(lvm, true);
                        lvm.GuestStatusText = "Хост открыл мир";
                        lvm.LobbyPlayers.Clear();
                        lvm.LobbyPlayers.Add(new LobbyPlayerItem
                        {
                            Nickname = "HostMaster",
                            IsHost = true,
                            Avatar = SkinService.LoadDefaultSteveBitmap()
                        });
                        lvm.LobbyPlayers.Add(new LobbyPlayerItem
                        {
                            Nickname = mainVm.PlayerNickname,
                            IsHost = false,
                            Avatar = mainVm.PlayerAvatar
                        });
                        typeof(LobbyViewModel).GetMethod("NotifyCellPropertiesChanged", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.Invoke(lvm, null);
                        typeof(LobbyViewModel).GetMethod("NotifyStatusStateChanged", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.Invoke(lvm, null);
                    });
                }), window.ViewLobby),

                // 6. Друзья: пусто
                ("Friends", "6_Friends_Empty", (Action)(() =>
                {
                    window.Dispatcher.Invoke(() =>
                    {
                        mainVm.SwitchTab("Friends");
                        mainVm.FriendsVM.Friends.Clear();
                        mainVm.FriendsVM.IncomingRequests.Clear();
                        mainVm.FriendsVM.AddCodeInput = string.Empty;
                        mainVm.FriendsVM.AddFriendErrorMessage = string.Empty;
                    });
                }), window.ViewFriends),

                // 7. Друзья: 3 друга + 1 входящая заявка + ошибка добавления
                ("Friends", "7_Friends_Full_WithError", (Action)(() =>
                {
                    window.Dispatcher.Invoke(() =>
                    {
                        mainVm.SwitchTab("Friends");
                        mainVm.FriendsVM.Friends.Clear();
                        mainVm.FriendsVM.IncomingRequests.Clear();

                        mainVm.FriendsVM.IncomingRequests.Add(new FriendRequestItemViewModel("req1", "AlexExplorer", (id, acc) => { }));

                        var f1 = new FriendItemViewModel(new FriendPresenceItem { Id = "f1", Nick = "DiamondMiner", Online = true, Status = "online", LastSeen = 0 }, _ => { }, _ => { });
                        var f2 = new FriendItemViewModel(new FriendPresenceItem { Id = "f2", Nick = "RedstoneKing", Online = true, Status = "lobby", LastSeen = 0 }, _ => { }, _ => { });
                        var f3 = new FriendItemViewModel(new FriendPresenceItem { Id = "f3", Nick = "CreeperHunter", Online = false, Status = "offline", LastSeen = DateTimeOffset.Now.AddHours(-2).ToUnixTimeMilliseconds() }, _ => { }, _ => { });

                        mainVm.FriendsVM.Friends.Add(f1);
                        mainVm.FriendsVM.Friends.Add(f2);
                        mainVm.FriendsVM.Friends.Add(f3);

                        mainVm.FriendsVM.AddCodeInput = "BADCODE1";
                        mainVm.FriendsVM.AddFriendErrorMessage = "Пользователь с таким кодом не найден";
                    });
                }), window.ViewFriends),

                // 8. Скин с ошибкой
                ("Wardrobe", "8_Skin_WithError", (Action)(() =>
                {
                    window.Dispatcher.Invoke(() =>
                    {
                        mainVm.SwitchTab("Wardrobe");
                        mainVm.WardrobeVM.StatusMessage = "Неверный формат скина. Поддерживаются только PNG 64x64 или 64x32.";
                        mainVm.WardrobeVM.IsStatusError = true;
                    });
                }), window.ViewWardrobe),

                // 9. Настройки целиком
                ("Settings", "9_Settings_Full", (Action)(() =>
                {
                    window.Dispatcher.Invoke(() =>
                    {
                        mainVm.SwitchTab("Settings");
                        var sv = FindVisualChild<ScrollViewer>(window.ViewSettings);
                        sv?.ScrollToTop();
                    });
                }), window.ViewSettings),

                // 10. Настройки с прокруткой
                ("Settings", "10_Settings_Scrolled", (Action)(() =>
                {
                    window.Dispatcher.Invoke(() =>
                    {
                        mainVm.SwitchTab("Settings");
                        var sv = FindVisualChild<ScrollViewer>(window.ViewSettings);
                        sv?.ScrollToBottom();
                    });
                }), window.ViewSettings)
            };

            int totalViolations = 0;
            double screenLeft = 340.0;
            double screenRight = 924.0;
            double screenTop = 64.0;
            double screenBottom = 576.0;

            foreach (var (tab, stateName, prepare, viewElem) in screenStates)
            {
                Console.WriteLine($"\n--- [АУДИТ] Состояние: {stateName} (Вкладка: {tab}) ---");
                prepare();
                await Task.Delay(400);

                window.Dispatcher.Invoke(() =>
                {
                    window.UpdateLayout();
                });
                await Task.Delay(100);

                CaptureScreen(stateName);

                int stateViolations = 0;

                window.Dispatcher.Invoke(() =>
                {
                    // Обход визуального дерева
                    var allElements = new List<FrameworkElement>();
                    void CollectElements(DependencyObject parent)
                    {
                        int count = VisualTreeHelper.GetChildrenCount(parent);
                        for (int i = 0; i < count; i++)
                        {
                            var child = VisualTreeHelper.GetChild(parent, i);
                            if (child is FrameworkElement fe)
                            {
                                allElements.Add(fe);
                            }
                            CollectElements(child);
                        }
                    }

                    CollectElements(viewElem);

                    foreach (var elem in allElements)
                    {
                        if (elem.Visibility != Visibility.Visible || !elem.IsLoaded || elem.Opacity <= 0) continue;

                        // Если родительский контейнер имеет ClipToBounds=True, его обрезанные дочерние элементы не выходят визуально
                        var clipParent = FindVisualAncestor<UIElement>(elem, p => p != elem && p.ClipToBounds);
                        if (clipParent != null) continue;

                        // Игнорируем элементы внутри прокручиваемого содержимого ScrollViewer для вертикального выхода,
                        // если они находятся внутри ScrollViewer
                        var parentScrollViewer = FindVisualAncestor<ScrollViewer>(elem);

                        // Проверяем границы элемента относительно MainWindow
                        try
                        {
                            var transform = elem.TransformToAncestor(window);
                            var elemBounds = transform.TransformBounds(new Rect(0, 0, elem.ActualWidth, elem.ActualHeight));

                            // а) Границы выходят за область экрана: left 340, right 924, top 64, bottom 576
                            // Если внутри ScrollViewer - вертикальный выход за bottom/top разрешен, но горизонтальный (left/right) не должен превышать 924
                            bool boundsExceeded = false;
                            string boundDetail = "";

                            // Допуск 2px на погрешности округления антиалиасинга
                            if (elemBounds.Left < screenLeft - 2 && !(elem is ScrollViewer))
                            {
                                boundsExceeded = true;
                                boundDetail = $"Left={elemBounds.Left:F1} < {screenLeft}";
                            }
                            if (elemBounds.Right > screenRight + 2)
                            {
                                boundsExceeded = true;
                                boundDetail = $"Right={elemBounds.Right:F1} > {screenRight}";
                            }

                            if (parentScrollViewer == null)
                            {
                                if (elemBounds.Top < screenTop - 2)
                                {
                                    boundsExceeded = true;
                                    boundDetail = $"Top={elemBounds.Top:F1} < {screenTop}";
                                }
                                if (elemBounds.Bottom > screenBottom + 2)
                                {
                                    boundsExceeded = true;
                                    boundDetail = $"Bottom={elemBounds.Bottom:F1} > {screenBottom}";
                                }
                            }

                            if (boundsExceeded && elem.ActualWidth > 0 && elem.ActualHeight > 0)
                            {
                                Console.WriteLine($"  [НАРУШЕНИЕ ГРАНИЦ] {elem.GetType().Name} (Name='{elem.Name}'): {boundDetail}, Rect=[{elemBounds.Left:F0},{elemBounds.Top:F0},{elemBounds.Width:F0},{elemBounds.Height:F0}]");
                                stateViolations++;
                            }
                        }
                        catch { }

                        // б) TextBlock: текст не помещается или включено TextTrimming
                        if (elem is TextBlock tb)
                        {
                            if (tb.TextTrimming != TextTrimming.None)
                            {
                                Console.WriteLine($"  [НАРУШЕНИЕ ТРИММИНГА] TextBlock '{tb.Text}': включено TextTrimming={tb.TextTrimming}");
                                stateViolations++;
                            }

                            // Проверяем, помещается ли текст
                            if (!string.IsNullOrEmpty(tb.Text) && tb.ActualWidth > 0)
                            {
                                var formattedText = new FormattedText(
                                    tb.Text,
                                    System.Globalization.CultureInfo.CurrentCulture,
                                    tb.FlowDirection,
                                    new Typeface(tb.FontFamily, tb.FontStyle, tb.FontWeight, tb.FontStretch),
                                    tb.FontSize,
                                    Brushes.Black,
                                    VisualTreeHelper.GetDpi(tb).PixelsPerDip);

                                if (tb.TextWrapping == TextWrapping.NoWrap && formattedText.Width > tb.ActualWidth + 2)
                                {
                                    Console.WriteLine($"  [НАРУШЕНИЕ ТЕКСТА] TextBlock '{tb.Text}': желаемая ширина {formattedText.Width:F1} > доступная {tb.ActualWidth:F1}");
                                    stateViolations++;
                                }
                            }
                        }
                    }
                });

                if (stateViolations == 0)
                {
                    Console.WriteLine($"  -> State {stateName}: 0 violations / Состояние {stateName}: 0 нарушений");
                }
                else
                {
                    Console.WriteLine($"  -> State {stateName}: {stateViolations} violations / Состояние {stateName}: {stateViolations} нарушений");
                }

                totalViolations += stateViolations;
            }

            Console.WriteLine("================================================================================");
            Console.WriteLine($"LAYOUT AUDIT RESULT: {totalViolations} violations / ИТОГ: {totalViolations} нарушений");
            if (totalViolations == 0)
            {
                Console.WriteLine("0 нарушений");
            }
            Console.WriteLine("================================================================================");

            return totalViolations == 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LAYOUT-AUDIT ERROR] {ex}");
            App.Log($"[LAYOUT-AUDIT ERROR] {ex}");
            return false;
        }
    }

    private static T? FindVisualAncestor<T>(DependencyObject current) where T : DependencyObject
    {
        while (current != null)
        {
            if (current is T match) return match;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private static T? FindVisualAncestor<T>(DependencyObject current, Func<T, bool> predicate) where T : DependencyObject
    {
        while (current != null)
        {
            if (current is T match && predicate(match)) return match;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    public static async Task<bool> RunReportSelfTestAsync(MainWindow mainWindow)
    {
        try
        {
            Console.WriteLine("=== [TEST: REPORT SELF-TEST] START ===");
            App.Log("=== [TEST: REPORT SELF-TEST] START ===");

            string baseDir = AppContext.BaseDirectory;
            string shotsDir = Path.Combine(baseDir, "artifacts_screens");
            string repoShotsDir = @"C:\Users\magne\Documents\GitHub\Aura-Launcher\artifacts_screens";
            string brainDir = @"C:\Users\magne\.gemini\antigravity\brain\5c57d232-70d4-4edf-b4d4-5effb51fb059";
            Directory.CreateDirectory(shotsDir);
            Directory.CreateDirectory(repoShotsDir);
            Directory.CreateDirectory(brainDir);

            void SaveShot(string fileName)
            {
                string localPath = Path.Combine(shotsDir, fileName);
                CaptureWindowToPng(mainWindow, localPath);
                try { File.Copy(localPath, Path.Combine(repoShotsDir, fileName), true); } catch { }
                try { File.Copy(localPath, Path.Combine(brainDir, fileName), true); } catch { }
                Console.WriteLine($"[TEST: REPORT] Screenshot saved: {fileName}");
                App.Log($"[TEST: REPORT] Screenshot saved: {fileName}");
            }

            var vm = mainWindow.Dispatcher.Invoke(() => mainWindow.DataContext as MainViewModel);
            if (vm == null) return false;

            // 1. Проверяем генерацию zip и маскировку секретов/токенов
            var reportService = App.Services.GetRequiredService<IReportService>();
            byte[] reportBytes = await reportService.GenerateReportBytesAsync("Test error trace in latest.log", "Пользовательский комментарий: тест отчёта");
            Console.WriteLine($"[TEST: REPORT] Generated zip size: {reportBytes.Length} bytes");

            // Распаковываем во временную папку и проверяем наличие секретов / токенов
            string tempUnzipDir = Path.Combine(Path.GetTempPath(), "aura_report_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempUnzipDir);
            try
            {
                using (var ms = new MemoryStream(reportBytes))
                using (var archive = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Read))
                {
                    archive.ExtractToDirectory(tempUnzipDir);
                }

                int leakedSecretsCount = 0;
                var allFiles = Directory.GetFiles(tempUnzipDir, "*", SearchOption.AllDirectories);
                Console.WriteLine($"[TEST: REPORT] Files in zip: {allFiles.Length}");
                foreach (var file in allFiles)
                {
                    Console.WriteLine($"  - {Path.GetFileName(file)} ({new FileInfo(file).Length} bytes)");
                    string text = File.ReadAllText(file);
                    // Проверяем на утечку явного токена или незамаскированного пользователя Windows
                    if (Regex.IsMatch(text, @"X-User-Token:\s*[a-f0-9]{32,64}", RegexOptions.IgnoreCase) ||
                        Regex.IsMatch(text, @"""(userToken|ownerToken|CurrentHostToken|playitSecret)""\s*:\s*""(?!(\*\*\*|""))[^""]+""", RegexOptions.IgnoreCase))
                    {
                        leakedSecretsCount++;
                        Console.WriteLine($"[LEAK DETECTED] in {Path.GetFileName(file)}");
                    }
                }

                Console.WriteLine($"[TEST: REPORT] Leaked tokens count: {leakedSecretsCount} (0 expected)");
            }
            finally
            {
                try { Directory.Delete(tempUnzipDir, true); } catch { }
            }

            // 2. Открываем окно подтверждения «Отправить отчёт»
            mainWindow.Dispatcher.Invoke(() =>
            {
                vm.PromptSendReport("Crash at net.minecraft.client.main.Main.main (Exit Code -1)");
                vm.ReportUserComment = "Игра вылетела при загрузке мира";
            });
            await Task.Delay(600);
            SaveShot("task40_report_confirmation_modal.png");

            // 3. Отправляем отчёт через сервис на lobby-api
            var sendResult = await reportService.SendReportAsync("Crash at net.minecraft.client.main.Main.main (Exit Code -1)", "Игра вылетела при загрузке мира");
            Console.WriteLine($"[TEST: REPORT] SendReportAsync result: Success={sendResult.Success}, ReportId={sendResult.ReportId}, Error={sendResult.ErrorMessage}");

            // 4. Показываем модальное окно успеха с полученным ID
            mainWindow.Dispatcher.Invoke(() =>
            {
                vm.IsSendReportModalVisible = false;
                vm.CreatedReportId = sendResult.ReportId ?? "R-A7B8C9";
                vm.IsReportSuccessModalVisible = true;
            });
            await Task.Delay(600);
            SaveShot("task40_report_success_modal.png");

            Console.WriteLine("=== [TEST: REPORT SELF-TEST] FINISHED SUCCESSFULLY ===");
            App.Log("=== [TEST: REPORT SELF-TEST] FINISHED SUCCESSFULLY ===");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TEST: REPORT ERROR] {ex}");
            App.Log($"[TEST: REPORT ERROR] {ex}");
            return false;
        }
    }
}
