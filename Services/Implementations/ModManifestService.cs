using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AuraLauncher.Models;
using AuraLauncher.Services.Interfaces;

namespace AuraLauncher.Services.Implementations;

public class ModManifestService : IModManifestService
{
    private readonly ConcurrentDictionary<string, CachedModInfo> _fileCache = new(StringComparer.OrdinalIgnoreCase);

    private record CachedModInfo(
        DateTime LastWriteTimeUtc,
        long FileLength,
        string Id,
        string Name,
        string Version,
        string Environment,
        bool IsClientOnly
    );

    public IReadOnlyList<ModManifestEntry> BuildModManifest(string gameDir)
    {
        var result = new List<ModManifestEntry>();
        if (string.IsNullOrWhiteSpace(gameDir)) return result;

        string modsDir = Path.Combine(gameDir, "mods");
        if (!Directory.Exists(modsDir)) return result;

        string[] files;
        try
        {
            files = Directory.GetFiles(modsDir, "*.*", SearchOption.TopDirectoryOnly);
        }
        catch
        {
            return result;
        }

        foreach (var filePath in files)
        {
            bool isJar = filePath.EndsWith(".jar", StringComparison.OrdinalIgnoreCase);
            bool isDisabled = filePath.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase);
            if (!isJar && !isDisabled) continue;

            try
            {
                var fi = new FileInfo(filePath);
                if (!fi.Exists) continue;

                if (!_fileCache.TryGetValue(filePath, out var cached) ||
                    cached.LastWriteTimeUtc != fi.LastWriteTimeUtc ||
                    cached.FileLength != fi.Length)
                {
                    cached = ReadModInfoFromJar(fi);
                    _fileCache[filePath] = cached;
                }

                // Моды с environment "client" не участвуют в манифесте для проверки синхронизации
                if (cached.IsClientOnly || string.IsNullOrWhiteSpace(cached.Id))
                {
                    continue;
                }

                bool enabled = isJar;
                result.Add(new ModManifestEntry
                {
                    Id = cached.Id,
                    Name = cached.Name,
                    Version = cached.Version,
                    Enabled = enabled
                });
            }
            catch
            {
                // Игнорируем битые/заблокированные файлы
            }
        }

        return result
            .GroupBy(m => m.Id, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(m => m.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public string ComputeManifestHash(IReadOnlyList<ModManifestEntry> manifest)
    {
        if (manifest == null || manifest.Count == 0) return "";

        var sb = new StringBuilder();
        foreach (var m in manifest.OrderBy(x => x.Id, StringComparer.OrdinalIgnoreCase))
        {
            sb.Append(m.Id.ToLowerInvariant())
              .Append(':')
              .Append(m.Version)
              .Append(':')
              .Append(m.Enabled ? '1' : '0')
              .Append('\n');
        }

        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(hashBytes).ToLowerInvariant().Substring(0, 16);
    }

    public int FixMismatches(string gameDir, IEnumerable<ModMismatchItem> mismatches)
    {
        if (string.IsNullOrWhiteSpace(gameDir) || mismatches == null) return 0;
        string modsDir = Path.Combine(gameDir, "mods");
        if (!Directory.Exists(modsDir)) return 0;

        int fixedCount = 0;
        var mismatchList = mismatches.Where(m => m.IsFixable).ToList();
        if (mismatchList.Count == 0) return 0;

        string[] files;
        try
        {
            files = Directory.GetFiles(modsDir, "*.*", SearchOption.TopDirectoryOnly);
        }
        catch
        {
            return 0;
        }

        // Построим маппинг modId -> filePath
        var idToFileMap = new Dictionary<string, (string FilePath, bool IsJar, bool IsDisabled)>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in files)
        {
            bool isJar = f.EndsWith(".jar", StringComparison.OrdinalIgnoreCase);
            bool isDisabled = f.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase);
            if (!isJar && !isDisabled) continue;

            if (_fileCache.TryGetValue(f, out var cached) && !string.IsNullOrWhiteSpace(cached.Id))
            {
                idToFileMap[cached.Id] = (f, isJar, isDisabled);
            }
            else
            {
                try
                {
                    var fi = new FileInfo(f);
                    cached = ReadModInfoFromJar(fi);
                    _fileCache[f] = cached;
                    if (!string.IsNullOrWhiteSpace(cached.Id))
                    {
                        idToFileMap[cached.Id] = (f, isJar, isDisabled);
                    }
                }
                catch { }
            }
        }

        foreach (var mm in mismatchList)
        {
            if (!idToFileMap.TryGetValue(mm.ModId, out var fileEntry))
            {
                continue;
            }

            try
            {
                if (mm.Type == "disabled" && fileEntry.IsDisabled)
                {
                    // Включить: убрать .disabled
                    string newPath = fileEntry.FilePath.Substring(0, fileEntry.FilePath.Length - ".disabled".Length);
                    if (File.Exists(newPath))
                    {
                        File.Delete(newPath);
                    }
                    File.Move(fileEntry.FilePath, newPath);
                    _fileCache.TryRemove(fileEntry.FilePath, out _);
                    fixedCount++;
                }
                else if (mm.Type == "extra" && fileEntry.IsJar)
                {
                    // Выключить лишний: добавить .disabled
                    string newPath = fileEntry.FilePath + ".disabled";
                    if (File.Exists(newPath))
                    {
                        File.Delete(newPath);
                    }
                    File.Move(fileEntry.FilePath, newPath);
                    _fileCache.TryRemove(fileEntry.FilePath, out _);
                    fixedCount++;
                }
            }
            catch (Exception ex)
            {
                FabricGameLaunchService.LogLauncherEvent($"[MOD-SYNC: FIX ERROR] {mm.ModId}: {ex.Message}");
            }
        }

        return fixedCount;
    }

    private static CachedModInfo ReadModInfoFromJar(FileInfo fi)
    {
        try
        {
            using var fs = new FileStream(fi.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var archive = new ZipArchive(fs, ZipArchiveMode.Read);
            var entry = archive.GetEntry("fabric.mod.json");
            if (entry != null)
            {
                using var stream = entry.Open();
                using var doc = JsonDocument.Parse(stream);
                var root = doc.RootElement;
                string id = root.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? "" : "";
                string name = root.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? id : id;
                string version = root.TryGetProperty("version", out var vProp) ? vProp.GetString() ?? "1.0.0" : "1.0.0";
                string environment = root.TryGetProperty("environment", out var envProp) ? envProp.GetString() ?? "*" : "*";
                bool isClientOnly = string.Equals(environment, "client", StringComparison.OrdinalIgnoreCase);

                return new CachedModInfo(fi.LastWriteTimeUtc, fi.Length, id, name, version, environment, isClientOnly);
            }
        }
        catch
        {
            // Не удалось прочитать fabric.mod.json
        }

        // Запасной вариант по имени файла
        string rawName = fi.Name;
        if (rawName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)) rawName = rawName[..^9];
        if (rawName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)) rawName = rawName[..^4];

        return new CachedModInfo(fi.LastWriteTimeUtc, fi.Length, rawName.ToLowerInvariant(), rawName, "1.0.0", "*", false);
    }
}
