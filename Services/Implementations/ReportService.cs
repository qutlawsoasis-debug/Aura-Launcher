using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AuraLauncher.Services.Interfaces;

namespace AuraLauncher.Services.Implementations;

public class ReportService : IReportService
{
    private readonly IConfigService _configService;
    private readonly INotificationService _notificationService;

    public ReportService(IConfigService configService, INotificationService notificationService)
    {
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
    }

    public async Task<string> GenerateReportZipAsync()
    {
        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrWhiteSpace(desktop) || !Directory.Exists(desktop))
        {
            desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        }

        string zipFileName = $"Aura-report-{DateTime.Now:yyyyMMdd-HHmm}.zip";
        string zipPath = Path.Combine(desktop, zipFileName);

        // В случае коллизии добавляем секунды
        if (File.Exists(zipPath))
        {
            zipFileName = $"Aura-report-{DateTime.Now:yyyyMMdd-HHmmss}.zip";
            zipPath = Path.Combine(desktop, zipFileName);
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var auraAppData = Path.Combine(appData, "Aura");
        var dotAuraDir = Path.Combine(appData, ".aura");

        using (var zipStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
        {
            // 1. config.json с замаскированными секретами
            string maskedConfig = GetMaskedConfigJson(auraAppData);
            AddStringToZip(archive, "config.json", maskedConfig);

            // 2. versions.txt
            string versionsContent = BuildVersionsInfo();
            AddStringToZip(archive, "versions.txt", versionsContent);

            // 3. Последние 3 launcher.log
            var launcherLogs = FindRecentLauncherLogs(auraAppData, 3);
            for (int i = 0; i < launcherLogs.Count; i++)
            {
                var filePath = launcherLogs[i];
                string entryName = (i == 0) ? "launcher.log" : $"launcher.{i}.log";
                string content = ReadAndMaskTextFile(filePath);
                AddStringToZip(archive, entryName, content);
            }

            // 4. tunnel.log
            string? tunnelLogPath = FindFirstExistingFile(
                Path.Combine(dotAuraDir, "logs", "tunnel.log"),
                Path.Combine(auraAppData, "logs", "tunnel.log"),
                Path.Combine(dotAuraDir, "tunnel.log")
            );
            string tunnelContent = tunnelLogPath != null ? ReadAndMaskTextFile(tunnelLogPath) : "[tunnel.log not found]";
            AddStringToZip(archive, "tunnel.log", tunnelContent);

            // 5. latest.log игры
            string? latestLogPath = FindFirstExistingFile(
                Path.Combine(dotAuraDir, "logs", "latest.log"),
                Path.Combine(auraAppData, "logs", "latest.log")
            );
            string latestContent = latestLogPath != null ? ReadAndMaskTextFile(latestLogPath) : "[latest.log not found]";
            AddStringToZip(archive, "latest.log", latestContent);
        }

        // Уведомление: «Отчёт на рабочем столе»
        _notificationService.Notify("Отчёт на рабочем столе", zipFileName, "Settings");

        // Открыть проводник с выделенным файлом
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{zipPath}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            FabricGameLaunchService.LogLauncherEvent($"[REPORT: EXPLORER ERROR] {ex.Message}");
        }

        return zipPath;
    }

    private string GetMaskedConfigJson(string auraAppData)
    {
        try
        {
            string configPath = Path.Combine(auraAppData, "config.json");
            string json;
            if (File.Exists(configPath))
            {
                json = File.ReadAllText(configPath);
            }
            else
            {
                json = JsonSerializer.Serialize(_configService.CurrentConfig, new JsonSerializerOptions { WriteIndented = true });
            }

            // Парсим через JsonNode для точной модификации
            var node = JsonNode.Parse(json);
            if (node is JsonObject obj)
            {
                MaskJsonObject(obj);
                json = obj.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            }

            // Дополнительная очистка регулярными выражениями по всем возможным названиям токенов и секретов
            json = Regex.Replace(json, @"(?i)""(userToken|UserTokenEncrypted|SkinOwnerToken|ownerToken|CurrentHostToken|hostToken|discordAppId|DiscordAppId|playitSecret|secret|secret_path)""\s*:\s*""[^""]*""", @"""$1"": ""***""");
            return json;
        }
        catch (Exception ex)
        {
            return $"{{\n  \"error\": \"Failed to read config: {ex.Message}\"\n}}";
        }
    }

    private void MaskJsonObject(JsonObject obj)
    {
        var sensitiveKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "userToken",
            "UserTokenEncrypted",
            "SkinOwnerToken",
            "ownerToken",
            "CurrentHostToken",
            "hostToken",
            "discordAppId",
            "DiscordAppId",
            "playitSecret",
            "secret",
            "secret_path",
            "SecretKey",
            "token"
        };

        var keysToMask = obj.Select(kvp => kvp.Key).Where(k => sensitiveKeys.Contains(k)).ToList();
        foreach (var key in keysToMask)
        {
            obj[key] = "***";
        }
    }

    private string BuildVersionsInfo()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== Aura Versions Report ===");
        sb.AppendLine("Launcher: 1.2.24 (beta 1.0.16)");
        sb.AppendLine("Fabric: 0.19.5 (Minecraft 1.20.1, 61 mods)");
        sb.AppendLine($"Runtime / Java: .NET {Environment.Version} ({RuntimeInformation.FrameworkDescription})");
        sb.AppendLine($"OS / Windows: {Environment.OSVersion.VersionString} ({(Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit")})");
        sb.AppendLine($"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss} (Local)");
        return sb.ToString();
    }

    private List<string> FindRecentLauncherLogs(string dir, int maxCount)
    {
        var list = new List<string>();
        try
        {
            if (Directory.Exists(dir))
            {
                var files = Directory.GetFiles(dir, "launcher*.log*")
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(fi => fi.LastWriteTimeUtc)
                    .Take(maxCount)
                    .Select(fi => fi.FullName)
                    .ToList();

                list.AddRange(files);
            }
        }
        catch { }

        // Если ничего не нашлось, но есть launcher.log
        var defaultLog = Path.Combine(dir, "launcher.log");
        if (list.Count == 0 && File.Exists(defaultLog))
        {
            list.Add(defaultLog);
        }

        return list;
    }

    private string? FindFirstExistingFile(params string[] paths)
    {
        foreach (var p in paths)
        {
            if (File.Exists(p)) return p;
        }
        return null;
    }

    private string ReadAndMaskTextFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return $"[{Path.GetFileName(path)} not found]";

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            string content = reader.ReadToEnd();

            // Маскируем любые hex-токены и секреты из логов
            content = MaskSecretsInText(content);
            return content;
        }
        catch (Exception ex)
        {
            return $"[Error reading {Path.GetFileName(path)}: {ex.Message}]";
        }
    }

    private string MaskSecretsInText(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        // Маскируем токены и секреты
        string masked = text;

        // Если известен текущий зашифрованный токен или другие токены, заменяем их
        var userToken = _configService.CurrentConfig?.UserTokenEncrypted;
        if (!string.IsNullOrWhiteSpace(userToken))
        {
            masked = masked.Replace(userToken, "***");
        }

        var skinToken = _configService.CurrentConfig?.SkinOwnerToken;
        if (!string.IsNullOrWhiteSpace(skinToken))
        {
            masked = masked.Replace(skinToken, "***");
        }

        var appId = _configService.CurrentConfig?.DiscordAppId;
        if (!string.IsNullOrWhiteSpace(appId))
        {
            masked = masked.Replace(appId, "***");
        }

        // Маскируем заголовки X-User-Token: <token>
        masked = Regex.Replace(masked, @"(?i)(X-User-Token:\s*)([a-f0-9]{32,64})", "$1***");
        // Маскируем userToken: <hex> или ownerToken: <hex>
        masked = Regex.Replace(masked, @"(?i)(userToken[""'\s:=]+)([a-f0-9]{32,64})", "$1***");
        masked = Regex.Replace(masked, @"(?i)(ownerToken[""'\s:=]+)([a-f0-9]{32,64})", "$1***");
        masked = Regex.Replace(masked, @"(?i)(hostToken[""'\s:=]+)([a-f0-9]{32,64})", "$1***");
        masked = Regex.Replace(masked, @"(?i)(secret[""'\s:=]+)([a-zA-Z0-9_-]{20,})", "$1***");

        return masked;
    }

    private void AddStringToZip(ZipArchive archive, string entryName, string content)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, Encoding.UTF8);
        writer.Write(content);
    }
}
