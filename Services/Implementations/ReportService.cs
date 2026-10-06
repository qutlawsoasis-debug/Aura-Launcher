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
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AuraLauncher.Services.Interfaces;

namespace AuraLauncher.Services.Implementations;

public class ReportService : IReportService
{
    private readonly IConfigService _configService;
    private readonly INotificationService _notificationService;
    private readonly HttpClient _httpClient;
    private readonly string _defaultBaseUrl;

    public ReportService(IConfigService configService, INotificationService notificationService, HttpClient? httpClient = null, string baseUrl = "https://lobby-api.vercel.app")
    {
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        _httpClient = httpClient ?? new HttpClient();
        _defaultBaseUrl = baseUrl.TrimEnd('/') + "/";
    }

    private Uri GetRequestUri(string relativePath)
    {
        string baseStr = _configService.CurrentConfig?.LobbyApiBaseUrl ?? _defaultBaseUrl;
        if (string.IsNullOrWhiteSpace(baseStr)) baseStr = _defaultBaseUrl;
        baseStr = baseStr.TrimEnd('/') + "/";
        return new Uri(new Uri(baseStr), relativePath);
    }

    public async Task<string> GenerateReportZipAsync(string? errorText = null, string? userComment = null)
    {
        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrWhiteSpace(desktop) || !Directory.Exists(desktop))
        {
            desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        }

        string zipFileName = $"Aura-report-{DateTime.Now:yyyyMMdd-HHmm}.zip";
        string zipPath = Path.Combine(desktop, zipFileName);

        if (File.Exists(zipPath))
        {
            zipFileName = $"Aura-report-{DateTime.Now:yyyyMMdd-HHmmss}.zip";
            zipPath = Path.Combine(desktop, zipFileName);
        }

        byte[] zipBytes = await GenerateReportBytesAsync(errorText, userComment);
        await File.WriteAllBytesAsync(zipPath, zipBytes);

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

    public async Task<byte[]> GenerateReportBytesAsync(string? errorText = null, string? userComment = null)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var auraAppData = Path.Combine(appData, "Aura");
        var dotAuraDir = Path.Combine(appData, ".aura");

        using var memoryStream = new MemoryStream();
        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            // 1. config.json с замаскированными секретами и путями
            string maskedConfig = GetMaskedConfigJson(auraAppData);
            AddStringToZip(archive, "config.json", maskedConfig);

            // 2. versions.txt
            string versionsContent = BuildVersionsInfo();
            AddStringToZip(archive, "versions.txt", versionsContent);

            // 3. Последние 3 launcher.log (tail до 1500 строк каждого)
            var launcherLogs = FindRecentLauncherLogs(auraAppData, 3);
            for (int i = 0; i < launcherLogs.Count; i++)
            {
                var filePath = launcherLogs[i];
                string entryName = (i == 0) ? "launcher.log" : $"launcher.{i}.log";
                string content = ReadTailAndMaskTextFile(filePath, 1500);
                AddStringToZip(archive, entryName, content);
            }

            // 4. tunnel.log (tail до 1500 строк)
            string? tunnelLogPath = FindFirstExistingFile(
                Path.Combine(dotAuraDir, "logs", "tunnel.log"),
                Path.Combine(auraAppData, "logs", "tunnel.log"),
                Path.Combine(dotAuraDir, "tunnel.log")
            );
            string tunnelContent = tunnelLogPath != null ? ReadTailAndMaskTextFile(tunnelLogPath, 1500) : "[tunnel.log not found]";
            AddStringToZip(archive, "tunnel.log", tunnelContent);

            // 5. latest.log игры (tail до 1500 строк)
            string? latestLogPath = FindFirstExistingFile(
                Path.Combine(dotAuraDir, "logs", "latest.log"),
                Path.Combine(auraAppData, "logs", "latest.log")
            );
            string latestContent = latestLogPath != null ? ReadTailAndMaskTextFile(latestLogPath, 1500) : "[latest.log not found]";
            AddStringToZip(archive, "latest.log", latestContent);

            // 6. Последний crash-report игры
            string? crashReportPath = FindLatestCrashReport(dotAuraDir, auraAppData);
            if (crashReportPath != null)
            {
                string crashContent = ReadTailAndMaskTextFile(crashReportPath, 1500);
                AddStringToZip(archive, Path.GetFileName(crashReportPath), crashContent);
            }

            // 7. Пользовательский комментарий и текст ошибки если есть
            if (!string.IsNullOrWhiteSpace(errorText) || !string.IsNullOrWhiteSpace(userComment))
            {
                var sb = new StringBuilder();
                if (!string.IsNullOrWhiteSpace(userComment))
                {
                    sb.AppendLine($"User Comment:\n{userComment}\n");
                }
                if (!string.IsNullOrWhiteSpace(errorText))
                {
                    sb.AppendLine($"Error Details:\n{errorText}\n");
                }
                AddStringToZip(archive, "report_info.txt", MaskSecretsInText(sb.ToString()));
            }
        }

        memoryStream.Position = 0;
        return memoryStream.ToArray();
    }

    public async Task<SendReportResult> SendReportAsync(string? errorText = null, string? userComment = null)
    {
        try
        {
            var userId = _configService.CurrentConfig?.UserId;
            var plainToken = GetDecryptedUserToken();

            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(plainToken))
            {
                return new SendReportResult(false, null, "Профиль не зарегистрирован");
            }

            byte[] zipBytes = await GenerateReportBytesAsync(errorText, userComment);
            if (zipBytes.Length > 1024 * 1024)
            {
                return new SendReportResult(false, null, "Размер архива отчёта превышает 1 МБ");
            }

            string base64Zip = Convert.ToBase64String(zipBytes);

            var payload = new
            {
                zipBase64 = base64Zip,
                errorText = errorText ?? string.Empty,
                userComment = userComment ?? string.Empty,
                launcherVersion = "beta 1.0.22",
                os = $"{Environment.OSVersion.VersionString} ({(Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit")})"
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, GetRequestUri("api/report"));
            request.Headers.TryAddWithoutValidation("X-User-Id", userId);
            request.Headers.TryAddWithoutValidation("X-User-Token", plainToken);
            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(request);
            var responseString = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                string errMsg = "Ошибка при отправке отчёта";
                try
                {
                    using var doc = JsonDocument.Parse(responseString);
                    if (doc.RootElement.TryGetProperty("error", out var errElem))
                    {
                        errMsg = errElem.GetString() ?? errMsg;
                    }
                }
                catch { }

                return new SendReportResult(false, null, errMsg);
            }

            using var respDoc = JsonDocument.Parse(responseString);
            string? reportId = null;
            if (respDoc.RootElement.TryGetProperty("reportId", out var idElem))
            {
                reportId = idElem.GetString();
            }

            return new SendReportResult(true, reportId, null);
        }
        catch (Exception ex)
        {
            FabricGameLaunchService.LogLauncherEvent($"[REPORT: SEND ERROR] {ex.Message}");
            return new SendReportResult(false, null, ex.Message);
        }
    }

    private string? GetDecryptedUserToken()
    {
        var encrypted = _configService.CurrentConfig?.UserTokenEncrypted;
        if (string.IsNullOrWhiteSpace(encrypted)) return null;

        try
        {
            var cipherBytes = Convert.FromBase64String(encrypted);
            var plainBytes = System.Security.Cryptography.ProtectedData.Unprotect(cipherBytes, null, System.Security.Cryptography.DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plainBytes);
        }
        catch
        {
            return encrypted;
        }
    }

    private string? FindLatestCrashReport(params string[] searchDirs)
    {
        try
        {
            var files = new List<FileInfo>();
            foreach (var dir in searchDirs)
            {
                var crashDir = Path.Combine(dir, "crash-reports");
                if (Directory.Exists(crashDir))
                {
                    files.AddRange(Directory.GetFiles(crashDir, "crash-*.txt").Select(f => new FileInfo(f)));
                }
            }

            return files.OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault()?.FullName;
        }
        catch
        {
            return null;
        }
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

            var node = JsonNode.Parse(json);
            if (node is JsonObject obj)
            {
                MaskJsonObject(obj);
                json = obj.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            }

            json = Regex.Replace(json, @"(?i)""(userToken|UserTokenEncrypted|SkinOwnerToken|ownerToken|CurrentHostToken|hostToken|discordAppId|DiscordAppId|playitSecret|secret|secret_path)""\s*:\s*""[^""]*""", @"""$1"": ""***""");
            json = MaskSecretsInText(json);
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
        sb.AppendLine("Launcher: 1.2.30 (beta 1.0.22)");
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

    private string ReadTailAndMaskTextFile(string path, int maxLines)
    {
        try
        {
            if (!File.Exists(path)) return $"[{Path.GetFileName(path)} not found]";

            var lines = new List<string>();
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    lines.Add(line);
                    if (lines.Count > maxLines * 2)
                    {
                        lines.RemoveRange(0, lines.Count - maxLines);
                    }
                }
            }

            if (lines.Count > maxLines)
            {
                lines = lines.Skip(lines.Count - maxLines).ToList();
            }

            string content = string.Join(Environment.NewLine, lines);
            return MaskSecretsInText(content);
        }
        catch (Exception ex)
        {
            return $"[Error reading {Path.GetFileName(path)}: {ex.Message}]";
        }
    }

    private string MaskSecretsInText(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        string masked = text;

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

        // Маскируем пути пользователей Windows: C:\Users\<username> -> C:\Users\***
        masked = Regex.Replace(masked, @"(?i)(C:\\Users\\)[^\\/\r\n""]+", "$1***");
        masked = Regex.Replace(masked, @"(?i)(C:/Users/)[^\\/\r\n""]+", "$1***");

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
