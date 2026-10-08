using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using AuraLauncher.Models;
using AuraLauncher.Services.Interfaces;
using fNbt;

namespace AuraLauncher.Services.Implementations;

public class WorkshopService : IWorkshopService
{
    public Task<List<WorldSaveItem>> GetWorldSavesAsync(string gameDir)
    {
        return Task.Run(() =>
        {
            var result = new List<WorldSaveItem>();
            string savesDir = Path.Combine(gameDir, "saves");
            if (!Directory.Exists(savesDir))
            {
                return result;
            }

            var dirs = Directory.GetDirectories(savesDir);
            foreach (var d in dirs)
            {
                try
                {
                    var dirInfo = new DirectoryInfo(d);
                    string levelDat = Path.Combine(d, "level.dat");
                    if (!File.Exists(levelDat))
                    {
                        continue; // Не валидная папка мира
                    }

                    string displayName = dirInfo.Name;
                    string gameMode = "Выживание";
                    DateTime lastPlayed = dirInfo.LastWriteTime;

                    try
                    {
                        var nbt = new NbtFile();
                        nbt.LoadFromFile(levelDat);
                        var data = nbt.RootTag["Data"] as NbtCompound;
                        if (data != null)
                        {
                            displayName = data["LevelName"]?.StringValue ?? dirInfo.Name;
                            long lp = data["LastPlayed"]?.LongValue ?? 0;
                            if (lp > 0)
                            {
                                lastPlayed = DateTimeOffset.FromUnixTimeMilliseconds(lp).LocalDateTime;
                            }
                            int gm = data["GameType"]?.IntValue ?? 0;
                            gameMode = gm switch
                            {
                                1 => "Творческий",
                                2 => "Приключение",
                                3 => "Наблюдатель",
                                _ => "Выживание"
                            };
                        }
                    }
                    catch { }

                    long totalBytes = 0;
                    try
                    {
                        foreach (var f in Directory.EnumerateFiles(d, "*", SearchOption.AllDirectories))
                        {
                            totalBytes += new FileInfo(f).Length;
                        }
                    }
                    catch { }

                    BitmapImage? icon = null;
                    string iconPath = Path.Combine(d, "icon.png");
                    if (File.Exists(iconPath))
                    {
                        try
                        {
                            var bmp = new BitmapImage();
                            bmp.BeginInit();
                            bmp.UriSource = new Uri(iconPath, UriKind.Absolute);
                            bmp.DecodePixelWidth = 64;
                            bmp.CacheOption = BitmapCacheOption.OnLoad;
                            bmp.EndInit();
                            bmp.Freeze();
                            icon = bmp;
                        }
                        catch { }
                    }

                    string playtimeFormatted = string.Empty;
                    try
                    {
                        string statsDir = Path.Combine(d, "stats");
                        if (Directory.Exists(statsDir))
                        {
                            long maxTicks = 0;
                            foreach (var sf in Directory.EnumerateFiles(statsDir, "*.json"))
                            {
                                string j = File.ReadAllText(sf);
                                using var doc = System.Text.Json.JsonDocument.Parse(j);
                                if (doc.RootElement.TryGetProperty("stats", out var stObj) &&
                                    stObj.TryGetProperty("minecraft:custom", out var cObj))
                                {
                                    if (cObj.TryGetProperty("minecraft:play_time", out var ptProp))
                                        maxTicks = Math.Max(maxTicks, ptProp.GetInt64());
                                    else if (cObj.TryGetProperty("minecraft:total_world_time", out var twProp))
                                        maxTicks = Math.Max(maxTicks, twProp.GetInt64());
                                }
                            }
                            if (maxTicks > 0)
                            {
                                int hours = (int)(maxTicks / (20 * 3600));
                                if (hours > 0) playtimeFormatted = $"{hours} ч";
                            }
                        }
                    }
                    catch { }

                    var backupsList = new List<WorldBackupItem>();
                    string backupDir = Path.Combine(gameDir, "backups", "AuraBackups");
                    if (Directory.Exists(backupDir))
                    {
                        string safeName = string.Join("_", displayName.Split(Path.GetInvalidFileNameChars()));
                        var zipFiles = Directory.EnumerateFiles(backupDir, "*.zip")
                            .Where(z => Path.GetFileName(z).StartsWith(safeName, StringComparison.OrdinalIgnoreCase) ||
                                        Path.GetFileName(z).StartsWith(dirInfo.Name, StringComparison.OrdinalIgnoreCase))
                            .ToList();

                        foreach (var z in zipFiles)
                        {
                            var fi = new FileInfo(z);
                            backupsList.Add(new WorldBackupItem
                            {
                                FileName = fi.Name,
                                FilePath = z,
                                CreatedAt = fi.LastWriteTime,
                                SizeFormatted = FormatFileSize(fi.Length)
                            });
                        }
                    }
                    backupsList = backupsList.OrderByDescending(b => b.CreatedAt).ToList();

                    var ticks = new List<BackupTickItem>();
                    for (int dayOffset = 29; dayOffset >= 0; dayOffset--)
                    {
                        var date = DateTime.Today.AddDays(-dayOffset);
                        bool hasB = backupsList.Any(b => b.CreatedAt.Date == date);
                        ticks.Add(new BackupTickItem { HasBackup = hasB });
                    }

                    string lastBackupText = "бэкапов нет";
                    if (backupsList.Count > 0)
                    {
                        int daysAgo = (int)Math.Max(0, (DateTime.Today - backupsList[0].CreatedAt.Date).TotalDays);
                        lastBackupText = daysAgo == 0 ? "последний сегодня" : daysAgo == 1 ? "последний вчера" : $"последний {daysAgo} дн. назад";
                    }

                    result.Add(new WorldSaveItem
                    {
                        FolderName = dirInfo.Name,
                        DisplayName = displayName,
                        FolderPath = d,
                        LastPlayed = lastPlayed,
                        GameMode = gameMode,
                        SizeFormatted = FormatFileSize(totalBytes),
                        IconSource = icon,
                        PlaytimeFormatted = playtimeFormatted,
                        BackupsCount = backupsList.Count,
                        LastBackupText = lastBackupText,
                        BackupTicks = ticks,
                        Backups = backupsList
                    });
                }
                catch { }
            }

            return result.OrderByDescending(w => w.LastPlayed).ToList();
        });
    }

    public Task<string> CreateWorldBackupAsync(WorldSaveItem world)
    {
        return Task.Run(() =>
        {
            if (string.IsNullOrWhiteSpace(world.FolderPath) || !Directory.Exists(world.FolderPath))
            {
                throw new DirectoryNotFoundException("Папка мира не найдена.");
            }

            string gameDir = Path.GetFullPath(Path.Combine(world.FolderPath, "..", ".."));
            string backupDir = Path.Combine(gameDir, "backups", "AuraBackups");
            Directory.CreateDirectory(backupDir);

            string safeName = string.Join("_", world.DisplayName.Split(Path.GetInvalidFileNameChars()));
            string zipFileName = $"{safeName}_{DateTime.Now:yyyyMMdd_HHmmss}.zip";
            string zipPath = Path.Combine(backupDir, zipFileName);

            ZipFile.CreateFromDirectory(world.FolderPath, zipPath, CompressionLevel.Optimal, false);
            return zipPath;
        });
    }

    public Task RestoreWorldBackupAsync(WorldSaveItem world, WorldBackupItem backup)
    {
        return Task.Run(() =>
        {
            if (!File.Exists(backup.FilePath)) throw new FileNotFoundException("Файл бэкапа не найден.");
            if (Directory.Exists(world.FolderPath))
            {
                Directory.Delete(world.FolderPath, true);
            }
            Directory.CreateDirectory(world.FolderPath);
            ZipFile.ExtractToDirectory(backup.FilePath, world.FolderPath, true);
        });
    }

    public Task DeleteWorldBackupAsync(WorldBackupItem backup)
    {
        return Task.Run(() =>
        {
            if (File.Exists(backup.FilePath))
            {
                File.Delete(backup.FilePath);
            }
        });
    }

    public Task<List<ModItem>> GetModsAsync(string gameDir)
    {
        return Task.Run(() =>
        {
            var result = new List<ModItem>();
            string modsDir = Path.Combine(gameDir, "mods");
            if (!Directory.Exists(modsDir))
            {
                return result;
            }

            var files = Directory.GetFiles(modsDir, "*.*", SearchOption.TopDirectoryOnly)
                .Where(f => f.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase));

            foreach (var f in files)
            {
                try
                {
                    var fi = new FileInfo(f);
                    bool isEnabled = f.EndsWith(".jar", StringComparison.OrdinalIgnoreCase);
                    string rawName = fi.Name;
                    if (rawName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
                    {
                        rawName = rawName[..^9];
                    }
                    if (rawName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
                    {
                        rawName = rawName[..^4];
                    }

                    string version = string.Empty;
                    var verMatch = System.Text.RegularExpressions.Regex.Match(rawName, @"[-_v](\d+(\.\d+)+.*)$");
                    if (verMatch.Success)
                    {
                        version = verMatch.Groups[1].Value;
                        rawName = rawName.Substring(0, verMatch.Index).TrimEnd('-', '_');
                    }

                    result.Add(new ModItem
                    {
                        FileName = fi.Name,
                        DisplayName = rawName,
                        Version = version,
                        FullPath = f,
                        IsEnabled = isEnabled,
                        SizeFormatted = FormatFileSize(fi.Length)
                    });
                }
                catch { }
            }

            return result.OrderBy(m => m.DisplayName).ToList();
        });
    }

    public bool ToggleMod(ModItem mod, bool enable)
    {
        try
        {
            if (enable && mod.FullPath.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
            {
                string target = mod.FullPath[..^9];
                if (File.Exists(target))
                {
                    File.Delete(target);
                }
                File.Move(mod.FullPath, target);
                mod.FullPath = target;
                mod.FileName = Path.GetFileName(target);
                mod.IsEnabled = true;
                return true;
            }
            else if (!enable && mod.FullPath.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
            {
                string target = mod.FullPath + ".disabled";
                if (File.Exists(target))
                {
                    File.Delete(target);
                }
                File.Move(mod.FullPath, target);
                mod.FullPath = target;
                mod.FileName = Path.GetFileName(target);
                mod.IsEnabled = false;
                return true;
            }
        }
        catch (Exception ex)
        {
            FabricGameLaunchService.LogLauncherEvent($"[WORKSHOP: ERROR ToggleMod] {ex.Message}");
        }
        return false;
    }

    public Task<List<ShaderPackItem>> GetShaderPacksAsync(string gameDir)
    {
        return Task.Run(() =>
        {
            var result = new List<ShaderPackItem>();
            string shadersDir = Path.Combine(gameDir, "shaderpacks");
            string irisConfig = Path.Combine(gameDir, "config", "iris.properties");
            string optionsShaders = Path.Combine(gameDir, "optionsshaders.txt");

            string currentShader = string.Empty;
            bool shadersEnabled = true;

            if (File.Exists(irisConfig))
            {
                try
                {
                    var lines = File.ReadAllLines(irisConfig);
                    foreach (var line in lines)
                    {
                        if (line.StartsWith("shaderPack=", StringComparison.OrdinalIgnoreCase))
                        {
                            currentShader = line["shaderPack=".Length..].Trim();
                        }
                        if (line.StartsWith("enableShaders=", StringComparison.OrdinalIgnoreCase))
                        {
                            bool.TryParse(line["enableShaders=".Length..].Trim(), out shadersEnabled);
                        }
                    }
                }
                catch { }
            }
            else if (File.Exists(optionsShaders))
            {
                try
                {
                    var lines = File.ReadAllLines(optionsShaders);
                    foreach (var line in lines)
                    {
                        if (line.StartsWith("shaderPack=", StringComparison.OrdinalIgnoreCase))
                        {
                            currentShader = line["shaderPack=".Length..].Trim();
                        }
                    }
                }
                catch { }
            }

            result.Add(new ShaderPackItem
            {
                Name = "Без шейдеров",
                FileName = "",
                Description = "Шейдеры отключены",
                IsActive = !shadersEnabled || string.IsNullOrWhiteSpace(currentShader) || currentShader.Equals("OFF", StringComparison.OrdinalIgnoreCase)
            });

            if (Directory.Exists(shadersDir))
            {
                var files = Directory.GetFiles(shadersDir, "*.zip");
                foreach (var f in files)
                {
                    var fi = new FileInfo(f);
                    string cleanName = Path.GetFileNameWithoutExtension(fi.Name);
                    bool active = shadersEnabled && string.Equals(fi.Name, currentShader, StringComparison.OrdinalIgnoreCase);
                    double sizeMb = fi.Length / (1024.0 * 1024.0);
                    string desc = sizeMb >= 0.1 ? $"{sizeMb:F1} МБ" : $"{Math.Max(1, fi.Length / 1024)} КБ";

                    result.Add(new ShaderPackItem
                    {
                        Name = cleanName,
                        FileName = fi.Name,
                        Description = desc,
                        IsActive = active
                    });
                }
            }

            return result;
        });
    }

    public void SetActiveShaderPack(string gameDir, string shaderFileName)
    {
        try
        {
            string configDir = Path.Combine(gameDir, "config");
            Directory.CreateDirectory(configDir);
            string irisConfig = Path.Combine(configDir, "iris.properties");

            bool enable = !string.IsNullOrWhiteSpace(shaderFileName) && !shaderFileName.Equals("OFF", StringComparison.OrdinalIgnoreCase);
            string chosenPack = enable ? shaderFileName : "OFF";
            string content = $"enableShaders={enable.ToString().ToLowerInvariant()}\nshaderPack={chosenPack}\n";
            File.WriteAllText(irisConfig, content);

            string optionsShaders = Path.Combine(gameDir, "optionsshaders.txt");
            File.WriteAllText(optionsShaders, $"currentShaderPacks={chosenPack}\nshaderPack={chosenPack}\n");

            FabricGameLaunchService.LogLauncherEvent($"[WORKSHOP] Set active shader: '{chosenPack}' (enabled: {enable})");
        }
        catch (Exception ex)
        {
            FabricGameLaunchService.LogLauncherEvent($"[WORKSHOP: ERROR SetActiveShaderPack] {ex.Message}");
        }
    }

    public Task<List<ScreenshotItem>> GetScreenshotsAsync(string gameDir)
    {
        return Task.Run(() =>
        {
            var result = new List<ScreenshotItem>();
            string screensDir = Path.Combine(gameDir, "screenshots");
            if (!Directory.Exists(screensDir))
            {
                return result;
            }

            var files = Directory.GetFiles(screensDir, "*.png")
                .Select(f => new FileInfo(f))
                .OrderByDescending(fi => fi.LastWriteTimeUtc);

            foreach (var fi in files)
            {
                try
                {
                    BitmapImage? thumb = null;
                    try
                    {
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.UriSource = new Uri(fi.FullName, UriKind.Absolute);
                        bmp.DecodePixelWidth = 360;
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.EndInit();
                        bmp.Freeze();
                        thumb = bmp;
                    }
                    catch { }

                    result.Add(new ScreenshotItem
                    {
                        FileName = fi.Name,
                        FullPath = fi.FullName,
                        CreatedDate = fi.LastWriteTime,
                        SizeFormatted = FormatFileSize(fi.Length),
                        Thumbnail = thumb
                    });
                }
                catch { }
            }

            return result;
        });
    }

    public bool DeleteScreenshot(ScreenshotItem item)
    {
        try
        {
            if (File.Exists(item.FullPath))
            {
                File.Delete(item.FullPath);
                return true;
            }
        }
        catch (Exception ex)
        {
            FabricGameLaunchService.LogLauncherEvent($"[WORKSHOP: ERROR DeleteScreenshot] {ex.Message}");
        }
        return false;
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes >= 1024 * 1024 * 1024)
        {
            return $"{(bytes / (1024.0 * 1024 * 1024)):F1} ГБ";
        }
        if (bytes >= 1024 * 1024)
        {
            return $"{(bytes / (1024.0 * 1024)):F1} МБ";
        }
        if (bytes >= 1024)
        {
            return $"{bytes / 1024} КБ";
        }
        return $"{bytes} Б";
    }
}
