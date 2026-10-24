using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using AuraLauncher.Models;
using AuraLauncher.Services.Interfaces;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;

namespace AuraLauncher.Core.WebBridge;

/// <summary>
/// Миры и скриншоты в Мастерской (повторяет WorkshopViewModel): бэкапы, восстановление,
/// экспорт и импорт, удаление, галерея F2-скриншотов.
/// </summary>
public partial class WebBridge
{
    private const string ShotsHost = "shots.aura.local";
    private const string ShotsThumbsHost = "shots-thumbs.aura.local";
    private const int ShotsThumbSize = 320;

    private string ThumbsDir(string screenshotsDir)
    {
        string dir = Path.Combine(screenshotsDir, ".thumbs");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private void RegisterShotsHosts(string screenshotsDir)
    {
        var core = _coreWebView2;
        if (core == null) return;
        string thumbs = ThumbsDir(screenshotsDir);
        RunOnUi(() =>
        {
            core.SetVirtualHostNameToFolderMapping(ShotsHost, screenshotsDir, CoreWebView2HostResourceAccessKind.Allow);
            core.SetVirtualHostNameToFolderMapping(ShotsThumbsHost, thumbs, CoreWebView2HostResourceAccessKind.Allow);
        });
    }

    // Миниатюра 320px jpg в кэше .thumbs; имя включает время файла — при изменении png кэш инвалидируется
    private string EnsureShotThumb(string pngPath, string thumbsDir)
    {
        var fi = new FileInfo(pngPath);
        string baseName = Path.GetFileNameWithoutExtension(fi.Name);
        string thumbName = $"{baseName}_{fi.LastWriteTimeUtc.Ticks}.jpg";
        string thumbPath = Path.Combine(thumbsDir, thumbName);
        if (File.Exists(thumbPath)) return thumbName;

        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.UriSource = new Uri(pngPath, UriKind.Absolute);
        bmp.DecodePixelWidth = ShotsThumbSize;
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.EndInit();
        bmp.Freeze();

        string tmpPath = thumbPath + ".tmp";
        var encoder = new JpegBitmapEncoder { QualityLevel = 75 };
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        using (var stream = File.Create(tmpPath))
        {
            encoder.Save(stream);
        }
        File.Move(tmpPath, thumbPath, true);
        return thumbName;
    }

    private void PruneOldThumbs(string thumbsDir, System.Collections.Generic.HashSet<string> keepNames)
    {
        try
        {
            foreach (string file in Directory.GetFiles(thumbsDir, "*.jpg"))
            {
                if (!keepNames.Contains(Path.GetFileName(file)))
                {
                    try { File.Delete(file); } catch { }
                }
            }
        }
        catch { }
    }

    private async Task<WorldSaveItem?> FindSaveAsync(BridgeMessage msg)
    {
        if (_workshopService == null)
        {
            SendError(msg.Id, msg.Name, "workshop_service_unavailable");
            return null;
        }
        string folder = ReadString(msg, "folder");
        var saves = await _workshopService.GetWorldSavesAsync(GetInstanceDir());
        var save = saves.FirstOrDefault(s => string.Equals(s.FolderName, folder, StringComparison.OrdinalIgnoreCase));
        if (save == null) SendError(msg.Id, msg.Name, "Мир не найден");
        return save;
    }

    private bool GameIsBusy(BridgeMessage msg)
    {
        if (_gameSessionService.State == GameSessionState.Idle || _gameSessionService.State == GameSessionState.Failed) return false;
        SendError(msg.Id, msg.Name, "Сначала закройте игру");
        return true;
    }

    private async Task HandleWorldsBackupAsync(BridgeMessage msg)
    {
        var save = await FindSaveAsync(msg);
        if (save == null) return;
        _logger($"[WEBBRIDGE] worlds.backup '{save.FolderName}'");
        string zip = await _workshopService!.CreateWorldBackupAsync(save);
        ReportAchievement("backup_created");
        SendResponse(msg.Id, msg.Name, new { ok = true, file = Path.GetFileName(zip) });
    }

    private async Task HandleWorldsRestoreAsync(BridgeMessage msg)
    {
        if (GameIsBusy(msg)) return;
        var save = await FindSaveAsync(msg);
        if (save == null) return;
        var backup = save.Backups.FirstOrDefault(b => string.Equals(b.FileName, ReadString(msg, "file"), StringComparison.OrdinalIgnoreCase));
        if (backup == null)
        {
            SendError(msg.Id, msg.Name, "Бэкап не найден");
            return;
        }
        _logger($"[WEBBRIDGE] worlds.restore '{save.FolderName}' <- {backup.FileName}");
        await _workshopService!.RestoreWorldBackupAsync(save, backup);
        SendResponse(msg.Id, msg.Name, new { ok = true });
    }

    private async Task HandleWorldsDeleteBackupAsync(BridgeMessage msg)
    {
        var save = await FindSaveAsync(msg);
        if (save == null) return;
        var backup = save.Backups.FirstOrDefault(b => string.Equals(b.FileName, ReadString(msg, "file"), StringComparison.OrdinalIgnoreCase));
        if (backup == null)
        {
            SendError(msg.Id, msg.Name, "Бэкап не найден");
            return;
        }
        _logger($"[WEBBRIDGE] worlds.deleteBackup {backup.FileName}");
        await _workshopService!.DeleteWorldBackupAsync(backup);
        SendResponse(msg.Id, msg.Name, new { ok = true });
    }

    private async Task HandleWorldsDeleteAsync(BridgeMessage msg)
    {
        if (GameIsBusy(msg)) return;
        var save = await FindSaveAsync(msg);
        if (save == null) return;
        _logger($"[WEBBRIDGE] worlds.delete '{save.FolderName}'");
        bool ok = await _workshopService!.DeleteWorldAsync(save);
        if (!ok)
        {
            SendError(msg.Id, msg.Name, "Не удалось удалить мир");
            return;
        }
        SendResponse(msg.Id, msg.Name, new { ok = true });
    }

    private async Task HandleWorldsExportAsync(BridgeMessage msg)
    {
        var save = await FindSaveAsync(msg);
        if (save == null) return;

        string safeName = string.Join("_", save.DisplayName.Split(Path.GetInvalidFileNameChars()));
        if (string.IsNullOrWhiteSpace(safeName)) safeName = save.FolderName;

        string? target = null;
        RunOnUi(() =>
        {
            var sfd = new SaveFileDialog
            {
                Title = "Экспорт мира в архив (.zip)",
                Filter = "Архив мира Minecraft (*.zip)|*.zip",
                FileName = $"{safeName}.zip",
                DefaultExt = ".zip"
            };
            bool? ok = _window != null ? sfd.ShowDialog(_window) : sfd.ShowDialog();
            if (ok == true) target = sfd.FileName;
        });
        if (string.IsNullOrWhiteSpace(target))
        {
            SendResponse(msg.Id, msg.Name, new { cancelled = true });
            return;
        }

        _logger($"[WEBBRIDGE] worlds.export '{save.FolderName}' -> {target}");
        await _workshopService!.ExportWorldToZipAsync(save, target);
        SendResponse(msg.Id, msg.Name, new { ok = true, path = target });
    }

    private async Task HandleWorldsImportAsync(BridgeMessage msg)
    {
        if (_workshopService == null)
        {
            SendError(msg.Id, msg.Name, "workshop_service_unavailable");
            return;
        }

        string? source = null;
        RunOnUi(() =>
        {
            var ofd = new OpenFileDialog
            {
                Title = "Выберите архив мира Minecraft (.zip)",
                Filter = "Архив мира Minecraft (*.zip)|*.zip",
                Multiselect = false
            };
            bool? ok = _window != null ? ofd.ShowDialog(_window) : ofd.ShowDialog();
            if (ok == true) source = ofd.FileName;
        });
        if (string.IsNullOrWhiteSpace(source))
        {
            SendResponse(msg.Id, msg.Name, new { cancelled = true });
            return;
        }

        _logger($"[WEBBRIDGE] worlds.import {source}");
        string name = await _workshopService.ImportWorldFromZipAsync(GetInstanceDir(), source);
        SendResponse(msg.Id, msg.Name, new { ok = true, name });
    }

    private async Task HandleWorldsOpenFolderAsync(BridgeMessage msg)
    {
        string? path;
        if (string.IsNullOrWhiteSpace(ReadString(msg, "folder")))
        {
            path = Path.Combine(GetInstanceDir(), ReadString(msg, "kind") == "backups" ? Path.Combine("backups", "AuraBackups") : "saves");
            Directory.CreateDirectory(path);
        }
        else
        {
            var save = await FindSaveAsync(msg);
            if (save == null) return;
            path = save.FolderPath;
        }
        OpenInExplorer(path);
        SendResponse(msg.Id, msg.Name, new { ok = true });
    }

    // ---------- Скриншоты ----------

    private string ScreenshotsDir()
    {
        string dir = Path.Combine(GetInstanceDir(), "screenshots");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private async Task HandleScreenshotsListAsync(BridgeMessage msg)
    {
        if (_workshopService == null)
        {
            SendError(msg.Id, msg.Name, "workshop_service_unavailable");
            return;
        }

        string dir = ScreenshotsDir();
        // Полные png и миниатюры отдаются с виртуальных хостов WebView2;
        // маппинг регистрируем и здесь, и при Attach, чтобы он не зависел от порядка вызовов
        RegisterShotsHosts(dir);

        var shots = await _workshopService.GetScreenshotsAsync(GetInstanceDir());
        ReportAchievement("screenshots_count", shots.Count);

        string thumbs = ThumbsDir(dir);
        var thumbsByName = await Task.Run(() =>
        {
            var map = new System.Collections.Generic.Dictionary<string, string>();
            foreach (var s in shots)
            {
                try { map[s.FileName] = EnsureShotThumb(s.FullPath, thumbs); }
                catch (Exception ex) { _logger($"[WEBBRIDGE] shot thumb failed {s.FileName}: {ex.Message}"); }
            }
            return map;
        });
        PruneOldThumbs(thumbs, new System.Collections.Generic.HashSet<string>(thumbsByName.Values));

        SendResponse(msg.Id, msg.Name, new
        {
            shots = shots.OrderByDescending(s => s.CreatedDate).Select(s => new
            {
                file = s.FileName,
                url = $"https://{ShotsHost}/{Uri.EscapeDataString(s.FileName)}",
                thumb = thumbsByName.TryGetValue(s.FileName, out string? t)
                    ? $"https://{ShotsThumbsHost}/{Uri.EscapeDataString(t)}"
                    : $"https://{ShotsHost}/{Uri.EscapeDataString(s.FileName)}",
                date = s.DateFormatted,
                size = s.SizeFormatted
            })
        });
    }

    private string? ShotPath(BridgeMessage msg)
    {
        string file = Path.GetFileName(ReadString(msg, "file"));
        string path = Path.Combine(ScreenshotsDir(), file);
        if (!string.IsNullOrWhiteSpace(file) && File.Exists(path)) return path;
        SendError(msg.Id, msg.Name, "Скриншот не найден");
        return null;
    }

    private void HandleScreenshotsCopy(BridgeMessage msg)
    {
        string? path = ShotPath(msg);
        if (path == null) return;
        try
        {
            RunOnUi(() =>
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                System.Windows.Clipboard.SetImage(bmp);
            });
            SendResponse(msg.Id, msg.Name, new { ok = true });
        }
        catch (Exception ex)
        {
            SendError(msg.Id, msg.Name, ex.Message);
        }
    }

    private void HandleScreenshotsOpen(BridgeMessage msg)
    {
        string? path = ShotPath(msg);
        if (path == null) return;
        try { Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true }); } catch { }
        SendResponse(msg.Id, msg.Name, new { ok = true });
    }

    private void HandleScreenshotsDelete(BridgeMessage msg)
    {
        string? path = ShotPath(msg);
        if (path == null || _workshopService == null) return;
        bool ok = _workshopService.DeleteScreenshot(new ScreenshotItem { FileName = Path.GetFileName(path), FullPath = path });
        if (!ok)
        {
            SendError(msg.Id, msg.Name, "Не удалось удалить скриншот");
            return;
        }
        _logger($"[WEBBRIDGE] screenshots.delete {Path.GetFileName(path)}");
        SendResponse(msg.Id, msg.Name, new { ok = true });
    }

    private void HandleScreenshotsOpenFolder(BridgeMessage msg)
    {
        OpenInExplorer(ScreenshotsDir());
        SendResponse(msg.Id, msg.Name, new { ok = true });
    }

    // ---------- общее ----------

    private void RunOnUi(Action action)
    {
        if (_window != null) _window.Dispatcher.Invoke(action);
        else action();
    }

    private static void OpenInExplorer(string path)
    {
        try { Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true }); } catch { }
    }
}
