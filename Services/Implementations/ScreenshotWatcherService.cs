using System;
using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using AuraLauncher.Services.Interfaces;

namespace AuraLauncher.Services.Implementations;

public class ScreenshotWatcherService : IScreenshotWatcherService
{
    private FileSystemWatcher? _watcher;
    private readonly ConcurrentDictionary<string, DateTime> _recentFiles = new(StringComparer.OrdinalIgnoreCase);

    public event Action<string>? ScreenshotCaptured;

    public void StartWatching(string gameDir)
    {
        StopWatching();

        if (string.IsNullOrWhiteSpace(gameDir)) return;

        try
        {
            string screenshotsDir = Path.Combine(gameDir, "screenshots");
            if (!Directory.Exists(screenshotsDir))
            {
                Directory.CreateDirectory(screenshotsDir);
            }

            _watcher = new FileSystemWatcher(screenshotsDir, "*.png")
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
                EnableRaisingEvents = true,
                IncludeSubdirectories = false
            };

            _watcher.Created += OnFileCreated;
            _watcher.Renamed += (s, e) => ProcessScreenshotFile(e.FullPath);
        }
        catch (Exception ex)
        {
            FabricGameLaunchService.LogLauncherEvent($"[SCREENSHOT WATCHER: ERROR] {ex.Message}");
        }
    }

    public void StopWatching()
    {
        if (_watcher != null)
        {
            try
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Dispose();
            }
            catch { }
            _watcher = null;
        }
    }

    private void OnFileCreated(object sender, FileSystemEventArgs e)
    {
        ProcessScreenshotFile(e.FullPath);
    }

    private void ProcessScreenshotFile(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath) || !fullPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var now = DateTime.UtcNow;
        if (_recentFiles.TryGetValue(fullPath, out var lastTime) && (now - lastTime).TotalSeconds < 5)
        {
            return;
        }
        _recentFiles[fullPath] = now;

        _ = Task.Run(async () =>
        {
            // Ждем, пока Minecraft закончит запись файла
            bool fileReady = false;
            for (int attempt = 0; attempt < 12; attempt++)
            {
                await Task.Delay(200);
                try
                {
                    if (File.Exists(fullPath))
                    {
                        var fi = new FileInfo(fullPath);
                        if (fi.Length > 0)
                        {
                            using var fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                            fileReady = true;
                            break;
                        }
                    }
                }
                catch
                {
                    // Файл ещё занят игрой
                }
            }

            if (!fileReady) return;

            try
            {
                BitmapImage? bmp = null;
                using (var fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.StreamSource = fs;
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    bmp.Freeze();
                }

                if (bmp != null)
                {
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        for (int clipAttempt = 0; clipAttempt < 3; clipAttempt++)
                        {
                            try
                            {
                                var dataObject = new DataObject();
                                dataObject.SetImage(bmp);
                                var fileList = new StringCollection { fullPath };
                                dataObject.SetFileDropList(fileList);
                                Clipboard.SetDataObject(dataObject, true);
                                break;
                            }
                            catch
                            {
                                System.Threading.Thread.Sleep(80);
                            }
                        }

                        ScreenshotCaptured?.Invoke(fullPath);
                        FabricGameLaunchService.LogLauncherEvent($"[SCREENSHOT] Скриншот {Path.GetFileName(fullPath)} скопирован в буфер обмена.");
                    });
                }
            }
            catch (Exception ex)
            {
                FabricGameLaunchService.LogLauncherEvent($"[SCREENSHOT: ERROR] Ошибка обработки скриншота: {ex.Message}");
            }
        });
    }

    public void Dispose()
    {
        StopWatching();
    }
}
