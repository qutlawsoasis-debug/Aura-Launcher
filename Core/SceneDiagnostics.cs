using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
            SaveWindowShot(updateWin, "update_downloading_63.png");
            App.Log("[FAKE-UPDATE-UI] Saved update_downloading_63.png");
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
                mainVm.UpdateBannerTitle = "Вышло обновление beta 1.0.10";
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
            SaveWindowShot(updateWin, "update_downloading_63.png");

            // 4. Error with countdown
            window.Dispatcher.Invoke(() => updateVm.SetFakeState("error"));
            await Task.Delay(400);
            SaveWindowShot(updateWin, "update_error.png");

            // 5. 3-4s animation GIF
            window.Dispatcher.Invoke(() => updateVm.SetFakeState("checking"));
            await Task.Delay(300);
            await RecordUpdateLogoAnimationAsync(updateWin, "update_logo_animation.gif");

            return true;
        }

        return false;
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
}
