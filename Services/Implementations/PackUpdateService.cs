using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AuraLauncher.Models;
using AuraLauncher.Services.Interfaces;

namespace AuraLauncher.Services.Implementations;

/// <summary>
/// Сервис обновления сборки игры по manifest.json из GitHub.
/// </summary>
public class PackUpdateService : IPackUpdateService, IDisposable
{
    public const string DefaultPackRepo = "qutlawsoasis-debug/Aura-Pack";
    public const string DefaultBranch = "main";
    public const long MaxManifestSizeBytes = 5 * 1024 * 1024; // 5 MB
    public const long MaxFileSizeBytes = 200 * 1024 * 1024; // 200 MB
    public const int MaxManifestFilesCount = 5000;
    public const int DownloadConcurrency = 6;
    public const int PerReadTimeoutSeconds = 30;

    private static readonly Regex RepoRegex = new(@"^[A-Za-z0-9._-]+/[A-Za-z0-9._-]+$", RegexOptions.Compiled);
    private static readonly string[] ReservedWindowsNames = new[]
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    private readonly IConfigService _configService;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly string? _customStateFilePath;

    private readonly object _concurrencyLock = new();
    private Task<PackUpdateResult>? _activeUpdateTask;

    public PackUpdateService(IConfigService configService, HttpClient? httpClient = null, string? customStateFilePath = null)
    {
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _customStateFilePath = customStateFilePath;
        if (httpClient != null)
        {
            _httpClient = httpClient;
            _ownsHttpClient = false;
        }
        else
        {
            _httpClient = new HttpClient
            {
                Timeout = Timeout.InfiniteTimeSpan
            };
            _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("AuraLauncher", "1.0"));
            _ownsHttpClient = true;
        }
    }

    public async Task<PackUpdateResult> CheckAndApplyAsync(IProgress<DownloadProgressReport>? progress = null, CancellationToken ct = default, bool forceFullCheck = false)
    {
        Task<PackUpdateResult> taskToAwait;
        lock (_concurrencyLock)
        {
            if (_activeUpdateTask != null && !_activeUpdateTask.IsCompleted)
            {
                taskToAwait = _activeUpdateTask;
            }
            else
            {
                taskToAwait = ExecuteCheckAndApplyAsync(progress, ct, forceFullCheck);
                _activeUpdateTask = taskToAwait;
            }
        }

        try
        {
            return await taskToAwait.ConfigureAwait(false);
        }
        finally
        {
            lock (_concurrencyLock)
            {
                if (_activeUpdateTask == taskToAwait && taskToAwait.IsCompleted)
                {
                    _activeUpdateTask = null;
                }
            }
        }
    }

    private async Task<PackUpdateResult> ExecuteCheckAndApplyAsync(IProgress<DownloadProgressReport>? progress, CancellationToken ct, bool forceFullCheck = false)
    {
        var sw = Stopwatch.StartNew();
        var config = _configService.CurrentConfig;
        var gameDir = config.GameDir;

        if (string.IsNullOrWhiteSpace(gameDir))
        {
            return new PackUpdateResult
            {
                Status = PackUpdateStatus.Failed,
                Message = "Не указана папка игры",
                Duration = sw.Elapsed
            };
        }

        var repo = string.IsNullOrWhiteSpace(config.PackRepo) ? DefaultPackRepo : config.PackRepo.Trim();
        if (!RepoRegex.IsMatch(repo))
        {
            return new PackUpdateResult
            {
                Status = PackUpdateStatus.Failed,
                Message = "Некорректный репозиторий сборки",
                Duration = sw.Elapsed
            };
        }

        var currentState = PackState.LoadValidState(gameDir, _customStateFilePath);
        bool isInstalled = currentState != null;

        // 1. Скачивание manifest.json
        string manifestJson;
        var manifestUrl = $"https://raw.githubusercontent.com/{repo}/{DefaultBranch}/manifest.json?t={DateTime.UtcNow.Ticks}";

        using var ctsManifest = CancellationTokenSource.CreateLinkedTokenSource(ct);
        ctsManifest.CancelAfter(TimeSpan.FromSeconds(8));

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, manifestUrl);
            using var resp = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ctsManifest.Token).ConfigureAwait(false);

            if (resp.StatusCode == HttpStatusCode.NotFound)
            {
                return new PackUpdateResult
                {
                    Status = PackUpdateStatus.Failed,
                    Message = "Манифест сборки не найден в репозитории (404)",
                    Duration = sw.Elapsed
                };
            }
            if (resp.StatusCode == HttpStatusCode.Forbidden || (int)resp.StatusCode == 429)
            {
                return new PackUpdateResult
                {
                    Status = PackUpdateStatus.Failed,
                    Message = "Превышен лимит запросов GitHub, повторите позже (403/429)",
                    Duration = sw.Elapsed
                };
            }
            if ((int)resp.StatusCode >= 500)
            {
                return new PackUpdateResult
                {
                    Status = PackUpdateStatus.Failed,
                    Message = $"Ошибка сервера GitHub ({(int)resp.StatusCode})",
                    Duration = sw.Elapsed
                };
            }
            if (!resp.IsSuccessStatusCode)
            {
                return new PackUpdateResult
                {
                    Status = PackUpdateStatus.Failed,
                    Message = $"Ошибка загрузки манифеста: {(int)resp.StatusCode}",
                    Duration = sw.Elapsed
                };
            }

            var contentLength = resp.Content.Headers.ContentLength;
            if (contentLength.HasValue && contentLength.Value > MaxManifestSizeBytes)
            {
                return new PackUpdateResult
                {
                    Status = PackUpdateStatus.Failed,
                    Message = "Манифест превышает допустимый размер (5 МБ)",
                    Duration = sw.Elapsed
                };
            }

            using var contentStream = await resp.Content.ReadAsStreamAsync(ctsManifest.Token).ConfigureAwait(false);
            using var ms = new MemoryStream();
            var buf = new byte[8192];
            int read;
            long totalRead = 0;
            while ((read = await contentStream.ReadAsync(buf.AsMemory(0, buf.Length), ctsManifest.Token).ConfigureAwait(false)) > 0)
            {
                totalRead += read;
                if (totalRead > MaxManifestSizeBytes)
                {
                    return new PackUpdateResult
                    {
                        Status = PackUpdateStatus.Failed,
                        Message = "Манифест превышает допустимый размер (5 МБ)",
                        Duration = sw.Elapsed
                    };
                }
                ms.Write(buf, 0, read);
            }

            manifestJson = System.Text.Encoding.UTF8.GetString(ms.ToArray());
        }
        catch (Exception ex) when (IsNetworkException(ex, ctsManifest.IsCancellationRequested, ct.IsCancellationRequested))
        {
            if (isInstalled)
            {
                return new PackUpdateResult
                {
                    Status = PackUpdateStatus.OfflineContinue,
                    Message = "Нет сети, запускаю установленную сборку",
                    Duration = sw.Elapsed
                };
            }

            return new PackUpdateResult
            {
                Status = PackUpdateStatus.Failed,
                Message = "Нужен интернет для первой установки",
                Duration = sw.Elapsed
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            FabricGameLaunchService.LogLauncherEvent($"[PACK-UPDATE: MANIFEST ERROR] {ex.Message}");
            return new PackUpdateResult
            {
                Status = PackUpdateStatus.Failed,
                Message = "Сборка не обновилась, повтори",
                Duration = sw.Elapsed
            };
        }

        // 2. Строгий парсинг и валидация безопасности манифеста
        ManifestModel manifest;
        try
        {
            manifest = ParseAndValidateManifest(manifestJson, gameDir);
        }
        catch (Exception ex)
        {
            FabricGameLaunchService.LogLauncherEvent($"[PACK-UPDATE: VALIDATION ERROR] {ex.Message}");
            return new PackUpdateResult
            {
                Status = PackUpdateStatus.Failed,
                Message = ex.Message,
                Duration = sw.Elapsed
            };
        }

        // Проверка версии Minecraft
        if (!string.Equals(manifest.Minecraft, FabricGameLaunchService.MinecraftVersion, StringComparison.OrdinalIgnoreCase))
        {
            return new PackUpdateResult
            {
                Status = PackUpdateStatus.Failed,
                Message = "Сборка для другой версии Minecraft",
                Duration = sw.Elapsed
            };
        }

        // 3. Составление плана обновления (Быстрая vs Полная проверка)
        var toDownload = new List<ManifestFileEntry>();
        var fullPathByEntry = new Dictionary<ManifestFileEntry, string>();
        var normalizedGameDir = Path.GetFullPath(gameDir);

        foreach (var file in manifest.Files)
        {
            var localPath = Path.Combine(normalizedGameDir, file.Path.Replace('/', Path.DirectorySeparatorChar));
            fullPathByEntry[file] = localPath;
        }

        bool sameVersion = isInstalled && string.Equals(currentState?.PackVersion, manifest.PackVersion, StringComparison.Ordinal);
        bool needsFullCheck = !sameVersion || forceFullCheck;

        if (sameVersion && !forceFullCheck)
        {
            // Быстрая проверка
            foreach (var file in manifest.Files)
            {
                var localPath = fullPathByEntry[file];
                if (!File.Exists(localPath))
                {
                    needsFullCheck = true;
                    break;
                }

                if (file.Mode == "sync")
                {
                    var fi = new FileInfo(localPath);
                    if (fi.Length != file.Size)
                    {
                        needsFullCheck = true;
                        break;
                    }
                }
            }
        }

        // Проверка удаления лишних jar из mods\
        var modsDir = Path.Combine(normalizedGameDir, "mods");
        var manifestModsJars = new HashSet<string>(
            manifest.Files
                .Where(f => f.Path.StartsWith("mods/", StringComparison.OrdinalIgnoreCase) && f.Path.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
                .Select(f => Path.GetFileName(f.Path)),
            StringComparer.OrdinalIgnoreCase);

        var jarsToDelete = new List<string>();
        if (Directory.Exists(modsDir))
        {
            var existingJars = Directory.GetFiles(modsDir, "*.jar", SearchOption.TopDirectoryOnly);
            foreach (var jar in existingJars)
            {
                var fileName = Path.GetFileName(jar);
                if (!manifestModsJars.Contains(fileName))
                {
                    jarsToDelete.Add(jar);
                }
            }
        }

        if (needsFullCheck)
        {
            // Полная проверка (при forceFullCheck сверяем хеши всех файлов)
            foreach (var file in manifest.Files)
            {
                var localPath = fullPathByEntry[file];
                if (file.Mode == "sync" || forceFullCheck)
                {
                    if (!File.Exists(localPath))
                    {
                        toDownload.Add(file);
                    }
                    else
                    {
                        var fi = new FileInfo(localPath);
                        if (fi.Length != file.Size)
                        {
                            toDownload.Add(file);
                        }
                        else
                        {
                            var hash = ComputeFileSha256(localPath);
                            if (!string.Equals(hash, file.Sha256, StringComparison.OrdinalIgnoreCase))
                            {
                                toDownload.Add(file);
                            }
                        }
                    }
                }
                else // default
                {
                    if (!File.Exists(localPath))
                    {
                        toDownload.Add(file);
                    }
                }
            }
        }

        int filesChangedCount = toDownload.Count + jarsToDelete.Count;

        // Если скачивать и удалять нечего: сборка актуальна
        if (toDownload.Count == 0 && jarsToDelete.Count == 0)
        {
            // Сохраняем/обновляем state, если его не было или packVersion отличался
            if (!sameVersion || currentState == null)
            {
                PackState.SaveState(new PackState
                {
                    PackVersion = manifest.PackVersion,
                    GameDir = normalizedGameDir,
                    Minecraft = manifest.Minecraft,
                    FabricLoader = manifest.FabricLoader,
                    InstalledAtUtc = DateTime.UtcNow,
                    ManagedServers = currentState?.ManagedServers ?? new List<string>()
                }, _customStateFilePath);
            }

            return new PackUpdateResult
            {
                Status = PackUpdateStatus.UpToDate,
                Message = forceFullCheck ? "Все файлы сборки проверены и в порядке" : "Установлена последняя версия сборки",
                FilesChanged = 0,
                Duration = sw.Elapsed,
                Servers = manifest.Servers
            };
        }

        // 4. Очистка устаревших/невалидных .part файлов
        var plannedPartFiles = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in toDownload)
        {
            var p = fullPathByEntry[item] + ".part";
            plannedPartFiles[p] = item.Size;
        }

        CleanupPartFiles(normalizedGameDir, plannedPartFiles);

        // 5. Удаление лишних jar в mods
        foreach (var jarPath in jarsToDelete)
        {
            try
            {
                File.Delete(jarPath);
            }
            catch (Exception ex)
            {
                FabricGameLaunchService.LogLauncherEvent($"[PACK-UPDATE: DELETE WARN] Не удалось удалить лишний мод {jarPath}: {ex.Message}");
            }
        }

        // 6. Параллельное скачивание файлов (Concurrency = 6)
        long totalPlanBytes = toDownload.Sum(f => f.Size);
        long completedBytesTotal = 0;
        int completedFilesCount = 0;
        var progressLock = new object();
        var downloadStopwatch = Stopwatch.StartNew();

        using var semaphore = new SemaphoreSlim(DownloadConcurrency, DownloadConcurrency);
        var downloadTasks = toDownload.Select(async file =>
        {
            await semaphore.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                ct.ThrowIfCancellationRequested();
                var targetFile = fullPathByEntry[file];
                await DownloadSingleFileWithRetryAsync(repo, file, targetFile, bytesRead =>
                {
                    lock (progressLock)
                    {
                        completedBytesTotal += bytesRead;
                        ReportProgress(progress, completedBytesTotal, totalPlanBytes, completedFilesCount, toDownload.Count, downloadStopwatch);
                    }
                }, ct).ConfigureAwait(false);

                lock (progressLock)
                {
                    completedFilesCount++;
                    ReportProgress(progress, completedBytesTotal, totalPlanBytes, completedFilesCount, toDownload.Count, downloadStopwatch);
                }
            }
            finally
            {
                semaphore.Release();
            }
        });

        try
        {
            await Task.WhenAll(downloadTasks).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            FabricGameLaunchService.LogLauncherEvent($"[PACK-UPDATE: DOWNLOAD FAILED] {ex.Message}");
            return new PackUpdateResult
            {
                Status = PackUpdateStatus.Failed,
                Message = "Сборка не обновилась, повтори",
                Duration = sw.Elapsed
            };
        }

        // 7. Успешное завершение: сохранение pack-state.json
        PackState.SaveState(new PackState
        {
            PackVersion = manifest.PackVersion,
            GameDir = normalizedGameDir,
            Minecraft = manifest.Minecraft,
            FabricLoader = manifest.FabricLoader,
            InstalledAtUtc = DateTime.UtcNow,
            ManagedServers = currentState?.ManagedServers ?? new List<string>()
        }, _customStateFilePath);

        // Обновление LastUpdateUtc в конфигурации лаунчера
        await _configService.UpdateConfigAsync(c =>
        {
            c.LastUpdateUtc = DateTime.UtcNow;
        }, ct).ConfigureAwait(false);

        progress?.Report(new DownloadProgressReport
        {
            Percentage = 100.0,
            SpeedMBs = 0,
            RemainingTime = TimeSpan.Zero,
            BytesReceived = totalPlanBytes,
            TotalBytes = totalPlanBytes,
            StatusText = "Сборка успешно обновлена!"
        });

        return new PackUpdateResult
        {
            Status = PackUpdateStatus.Updated,
            Message = "Сборка успешно обновлена",
            FilesChanged = filesChangedCount,
            Duration = sw.Elapsed,
            Servers = manifest.Servers
        };
    }

    private static void ReportProgress(
        IProgress<DownloadProgressReport>? progress, 
        long completedBytes, 
        long totalPlanBytes, 
        int completedFiles, 
        int totalFiles, 
        Stopwatch sw)
    {
        if (progress == null) return;

        var elapsedSec = Math.Max(0.001, sw.Elapsed.TotalSeconds);
        var speedMBs = (completedBytes / (1024.0 * 1024.0)) / elapsedSec;
        var percentage = totalPlanBytes > 0 ? Math.Min(100.0, (double)completedBytes / totalPlanBytes * 100.0) : 100.0;

        TimeSpan? remaining = null;
        if (speedMBs > 0.01 && totalPlanBytes > completedBytes)
        {
            var secLeft = (totalPlanBytes - completedBytes) / (speedMBs * 1024.0 * 1024.0);
            remaining = TimeSpan.FromSeconds(Math.Min(secLeft, 86400));
        }

        progress.Report(new DownloadProgressReport
        {
            Percentage = percentage,
            SpeedMBs = speedMBs,
            RemainingTime = remaining,
            BytesReceived = completedBytes,
            TotalBytes = totalPlanBytes,
            StatusText = $"Обновление сборки: файл {Math.Min(completedFiles + 1, totalFiles)} из {totalFiles}"
        });
    }

    private async Task DownloadSingleFileWithRetryAsync(
        string repo, 
        ManifestFileEntry file, 
        string targetPath, 
        Action<int> onBytesRead, 
        CancellationToken ct)
    {
        var targetDir = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
        {
            Directory.CreateDirectory(targetDir);
        }

        var partPath = targetPath + ".part";
        var escapedSegments = file.Path.Split('/').Select(Uri.EscapeDataString);
        var escapedPath = string.Join('/', escapedSegments);
        var fileUrl = $"https://raw.githubusercontent.com/{repo}/{DefaultBranch}/{escapedPath}";

        for (int attempt = 1; attempt <= 3; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                long existingPartLength = 0;
                if (File.Exists(partPath))
                {
                    existingPartLength = new FileInfo(partPath).Length;
                    if (existingPartLength > file.Size)
                    {
                        File.Delete(partPath);
                        existingPartLength = 0;
                    }
                }

                using var req = new HttpRequestMessage(HttpMethod.Get, fileUrl);
                bool isResuming = existingPartLength > 0 && existingPartLength < file.Size;
                if (isResuming)
                {
                    req.Headers.Range = new RangeHeaderValue(existingPartLength, null);
                }

                using var resp = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if (resp.StatusCode != HttpStatusCode.OK && resp.StatusCode != HttpStatusCode.PartialContent)
                {
                    throw new HttpRequestException($"HTTP {(int)resp.StatusCode} при скачивании {file.Path}", null, resp.StatusCode);
                }

                bool isPartial = resp.StatusCode == HttpStatusCode.PartialContent;
                if (isResuming && !isPartial)
                {
                    // Сервер не поддержал Range и вернул полный 200 OK — перекачиваем с нуля
                    existingPartLength = 0;
                }

                using (var contentStream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
                using (var fileStream = new FileStream(partPath, existingPartLength > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 65536, useAsync: true))
                {
                    var buffer = new byte[65536];
                    while (true)
                    {
                        using var readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        readCts.CancelAfter(TimeSpan.FromSeconds(PerReadTimeoutSeconds));

                        int bytesRead = await contentStream.ReadAsync(buffer.AsMemory(0, buffer.Length), readCts.Token).ConfigureAwait(false);
                        if (bytesRead == 0) break;

                        await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct).ConfigureAwait(false);
                        onBytesRead(bytesRead);
                    }
                }

                // Проверка размера и SHA-256 после скачивания
                var downloadedFi = new FileInfo(partPath);
                if (downloadedFi.Length != file.Size)
                {
                    throw new Exception($"Несовпадение размера файла {file.Path}: ожидалось {file.Size}, получено {downloadedFi.Length}");
                }

                var actualHash = ComputeFileSha256(partPath);
                if (!string.Equals(actualHash, file.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new Exception($"Несовпадение SHA-256 для {file.Path}: ожидалось {file.Sha256}, получено {actualHash}");
                }

                // Атомарная замена
                File.Move(partPath, targetPath, overwrite: true);
                return; // Успех!
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                FabricGameLaunchService.LogLauncherEvent($"[PACK-UPDATE: RETRY {attempt}/3] Ошибка скачивания {file.Path}: {ex.Message}");

                if (File.Exists(partPath))
                {
                    try { File.Delete(partPath); } catch { }
                }

                if (attempt == 3)
                {
                    throw new Exception($"Файл {file.Path} не удалось скачать после 3 попыток: {ex.Message}", ex);
                }

                var backoffSec = (int)Math.Pow(2, attempt - 1); // 1, 2, 4 сек
                await Task.Delay(TimeSpan.FromSeconds(backoffSec), ct).ConfigureAwait(false);
            }
        }
    }

    private static void CleanupPartFiles(string gameDir, Dictionary<string, long> plannedParts)
    {
        var targetSubDirs = new[] { "mods", "shaderpacks", "resourcepacks", "config", "tlm_custom_pack" };
        foreach (var sub in targetSubDirs)
        {
            var dir = Path.Combine(gameDir, sub);
            if (!Directory.Exists(dir)) continue;

            try
            {
                var partFiles = Directory.GetFiles(dir, "*.part", SearchOption.AllDirectories);
                foreach (var part in partFiles)
                {
                    if (!plannedParts.TryGetValue(part, out long expectedSize))
                    {
                        try { File.Delete(part); } catch { }
                    }
                    else
                    {
                        var len = new FileInfo(part).Length;
                        if (len > expectedSize)
                        {
                            try { File.Delete(part); } catch { }
                        }
                    }
                }
            }
            catch { }
        }

        // Проверяем корень (options.txt.part)
        var optPart = Path.Combine(gameDir, "options.txt.part");
        if (File.Exists(optPart))
        {
            if (!plannedParts.TryGetValue(optPart, out long expectedSize) || new FileInfo(optPart).Length > expectedSize)
            {
                try { File.Delete(optPart); } catch { }
            }
        }
    }

    public static string ComputeFileSha256(string filePath)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536);
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static bool IsNetworkException(Exception ex, bool isTimeout, bool isUserCancellation)
    {
        if (isUserCancellation)
            return false;

        if (isTimeout)
            return true;

        if (ex is SocketException || ex is TimeoutException)
            return true;

        if (ex is HttpRequestException httpEx && !httpEx.StatusCode.HasValue)
            return true;

        if (ex.InnerException != null)
            return IsNetworkException(ex.InnerException, false, false);

        return false;
    }

    public static ManifestModel ParseAndValidateManifest(string json, string gameDir)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var name = root.TryGetProperty("name", out var np) ? np.GetString() ?? "" : "";
        var packVersion = root.TryGetProperty("packVersion", out var pvp) ? pvp.GetString() ?? "" : "";
        var minecraft = root.TryGetProperty("minecraft", out var mp) ? mp.GetString() ?? "" : "";
        var fabricLoader = root.TryGetProperty("fabricLoader", out var flp) ? flp.GetString() ?? "" : "";

        if (string.IsNullOrWhiteSpace(packVersion))
        {
            throw new FormatException("Манифест поврежден: отсутствует packVersion");
        }

        if (!root.TryGetProperty("files", out var filesProp) || filesProp.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException("Манифест поврежден: отсутствует список files");
        }

        if (filesProp.GetArrayLength() > MaxManifestFilesCount)
        {
            throw new FormatException($"Превышен лимит файлов в манифесте ({MaxManifestFilesCount})");
        }

        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalizedGameDir = Path.GetFullPath(gameDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var files = new List<ManifestFileEntry>();

        foreach (var item in filesProp.EnumerateArray())
        {
            var path = item.TryGetProperty("path", out var pp) ? pp.GetString() ?? "" : "";
            var sha256 = item.TryGetProperty("sha256", out var sp) ? sp.GetString() ?? "" : "";
            var size = item.TryGetProperty("size", out var sip) ? sip.GetInt64() : -1;
            var mode = item.TryGetProperty("mode", out var mp2) ? mp2.GetString() ?? "" : "";

            ValidatePathSecurity(path);

            if (sha256.Length != 64 || !sha256.All(Uri.IsHexDigit))
            {
                throw new FormatException($"Некорректный SHA-256 для файла: {path}");
            }

            if (size < 0 || size > MaxFileSizeBytes)
            {
                throw new FormatException($"Некорректный размер файла ({size} байт): {path}");
            }

            if (mode != "sync" && mode != "default")
            {
                throw new FormatException($"Некорректный режим mode '{mode}' для файла: {path}");
            }

            if (!seenPaths.Add(path))
            {
                throw new FormatException($"Дубликат пути в манифесте: {path}");
            }

            // Дополнительная проверка: итоговый путь строго внутри GameDir
            var fullPath = Path.GetFullPath(Path.Combine(normalizedGameDir, path.Replace('/', Path.DirectorySeparatorChar)));
            if (!fullPath.StartsWith(normalizedGameDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(fullPath, Path.Combine(normalizedGameDir, "options.txt"), StringComparison.OrdinalIgnoreCase))
            {
                throw new UnauthorizedAccessException($"Путь выходит за пределы игровой директории: {path}");
            }

            files.Add(new ManifestFileEntry
            {
                Path = path,
                Sha256 = sha256.ToLowerInvariant(),
                Size = size,
                Mode = mode
            });
        }

        List<ManifestServerEntry>? servers = null;
        if (root.TryGetProperty("servers", out var serversProp) && serversProp.ValueKind == JsonValueKind.Array)
        {
            servers = new List<ManifestServerEntry>();
            foreach (var sItem in serversProp.EnumerateArray())
            {
                var sId = sItem.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? "" : "";
                var sName = sItem.TryGetProperty("name", out var nProp) ? nProp.GetString() ?? "" : "";
                var sAddress = sItem.TryGetProperty("address", out var aProp) ? aProp.GetString() ?? "" : "";
                servers.Add(new ManifestServerEntry
                {
                    Id = sId,
                    Name = sName,
                    Address = sAddress
                });
            }
        }

        return new ManifestModel
        {
            Name = name,
            PackVersion = packVersion,
            Minecraft = minecraft,
            FabricLoader = fabricLoader,
            Servers = servers,
            Files = files
        };
    }

    public static void ValidatePathSecurity(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new FormatException("Пустой путь к файлу в манифесте");
        }

        if (path.Contains('\\') || path.Contains(':'))
        {
            throw new FormatException($"Путь содержит запрещенные символы ('\\' или ':'): {path}");
        }

        if (path.StartsWith('/'))
        {
            throw new FormatException($"Путь не должен начинаться со слэша: {path}");
        }

        if (path.Contains(".."))
        {
            throw new FormatException($"Путь содержит запрещенный переход '..': {path}");
        }

        if (path.StartsWith('"') || path.EndsWith('"'))
        {
            throw new FormatException($"Путь содержит кавычки: {path}");
        }

        if (path.Any(char.IsControl))
        {
            throw new FormatException($"Путь содержит управляющие символы: {path}");
        }

        // Разрешены только: mods/, shaderpacks/, resourcepacks/, config/, CustomSkinLoader/, tlm_custom_pack/ и options.txt в корне
        bool isAllowedRoot = path.StartsWith("mods/", StringComparison.Ordinal) ||
                             path.StartsWith("shaderpacks/", StringComparison.Ordinal) ||
                             path.StartsWith("resourcepacks/", StringComparison.Ordinal) ||
                             path.StartsWith("config/", StringComparison.Ordinal) ||
                             path.StartsWith("CustomSkinLoader/", StringComparison.Ordinal) ||
                             path.StartsWith("tlm_custom_pack/", StringComparison.Ordinal) ||
                             string.Equals(path, "options.txt", StringComparison.Ordinal);

        if (!isAllowedRoot)
        {
            throw new FormatException($"Запрещенный корневой каталог файла: {path}");
        }

        var segments = path.Split('/');
        foreach (var seg in segments)
        {
            if (string.IsNullOrWhiteSpace(seg))
            {
                throw new FormatException($"Пустой сегмент пути в: {path}");
            }

            if (seg != seg.Trim())
            {
                throw new FormatException($"Сегмент пути содержит пробелы по краям: '{seg}'");
            }

            if (seg.EndsWith('.'))
            {
                throw new FormatException($"Сегмент пути заканчивается точкой: '{seg}'");
            }

            var nameWithoutExt = Path.GetFileNameWithoutExtension(seg);
            if (ReservedWindowsNames.Contains(nameWithoutExt, StringComparer.OrdinalIgnoreCase) ||
                ReservedWindowsNames.Contains(seg, StringComparer.OrdinalIgnoreCase))
            {
                throw new FormatException($"Сегмент пути содержит зарезервированное имя Windows: '{seg}'");
            }
        }
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
        GC.SuppressFinalize(this);
    }
}

public class ManifestModel
{
    public string Name { get; set; } = string.Empty;
    public string PackVersion { get; set; } = string.Empty;
    public string Minecraft { get; set; } = string.Empty;
    public string FabricLoader { get; set; } = string.Empty;
    public List<ManifestServerEntry>? Servers { get; set; }
    public List<ManifestFileEntry> Files { get; set; } = new();
}

public class ManifestFileEntry
{
    public string Path { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public long Size { get; set; }
    public string Mode { get; set; } = "sync";
}
