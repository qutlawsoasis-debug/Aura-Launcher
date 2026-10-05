using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Management;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AuraLauncher.Core;
using AuraLauncher.Models;
using AuraLauncher.Services.Interfaces;
using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.FileExtractors;
using CmlLib.Core.Files;
using CmlLib.Core.Installers;
using CmlLib.Core.ModLoaders.FabricMC;
using CmlLib.Core.ProcessBuilder;
using CmlLib.Core.Rules;

namespace AuraLauncher.Services.Implementations;

/// <summary>
/// Асинхронный сервис автоустановки игрового окружения Minecraft (CmlLib.Core)
/// и запуска клиента игры с Fabric Loader.
/// Не модифицирует папки mods, config, saves, shaderpacks, resourcepacks.
/// </summary>
public class FabricGameLaunchService : IGameLaunchService
{
    public const string FabricLoaderVersion = "0.19.5";
    public const string MinecraftVersion = "1.20.1";

    public static string ResolveFabricLoaderVersion(string gameDir)
    {
        try
        {
            var state = PackState.LoadValidState(gameDir);
            if (state != null && !string.IsNullOrWhiteSpace(state.FabricLoader))
            {
                return state.FabricLoader.Trim();
            }
        }
        catch
        {
        }
        return FabricLoaderVersion;
    }

    private Process? _gameProcess;

    public bool IsGameRunning => _gameProcess != null && !_gameProcess.HasExited;
    public Process? CurrentGameProcess => _gameProcess;
    public event EventHandler<int>? GameExited;

    private static readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(60)
    };

    public string ResolveMinecraftDirectory(string? customPath = null)
    {
        if (!string.IsNullOrWhiteSpace(customPath))
        {
            return customPath.Trim();
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, ".aura");
    }

    /// <summary>
    /// Локальная проверка готовности окружения (Fabric profile, vanilla jar, Java runtime) без сети.
    /// </summary>
    public bool CheckEnvironmentInstalled(string gameDir)
    {
        try
        {
            var fullGameDir = ResolveMinecraftDirectory(gameDir);
            var fabricLoader = ResolveFabricLoaderVersion(fullGameDir);
            var fabricId = $"fabric-loader-{fabricLoader}-{MinecraftVersion}";
            var fabricJson = Path.Combine(fullGameDir, "versions", fabricId, $"{fabricId}.json");
            var vanillaJar = Path.Combine(fullGameDir, "versions", MinecraftVersion, $"{MinecraftVersion}.jar");
            var javaRuntime = ResolveJavaRuntime(fullGameDir);

            return File.Exists(fabricJson) && File.Exists(vanillaJar) && !string.IsNullOrWhiteSpace(javaRuntime);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Определение уже запущенной игры при старте лаунчера через Win32_Process в System.Management.
    /// </summary>
    public Process? FindRunningGameProcess(string gameDir)
    {
        try
        {
            var fullGameDir = ResolveMinecraftDirectory(gameDir);
            using var searcher = new ManagementObjectSearcher(
                "SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name = 'javaw.exe' OR Name = 'java.exe'");
            using var results = searcher.Get();
            foreach (ManagementObject obj in results)
            {
                var cmdLine = obj["CommandLine"]?.ToString();
                if (!string.IsNullOrEmpty(cmdLine) &&
                    cmdLine.Contains(fullGameDir, StringComparison.OrdinalIgnoreCase))
                {
                    var pid = Convert.ToInt32(obj["ProcessId"]);
                    try
                    {
                        var proc = Process.GetProcessById(pid);
                        if (!proc.HasExited)
                        {
                            AttachRunningProcess(proc);
                            return proc;
                        }
                    }
                    catch
                    {
                        // Процесс мог уже завершиться
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LogLauncherEvent($"[WMI: ERROR] Ошибка при поиске процесса игры: {ex.Message}");
        }
        return null;
    }

    private void AttachRunningProcess(Process proc)
    {
        _gameProcess = proc;
        try
        {
            proc.EnableRaisingEvents = true;
            proc.Exited += (s, e) =>
            {
                int exitCode = 0;
                try { exitCode = proc.ExitCode; } catch { }
                _gameProcess = null;
                LogLauncherEvent($"[EXIT] Ранее запущенная игра (PID {proc.Id}) завершилась с кодом {exitCode}");
                GameExited?.Invoke(this, exitCode);
            };
        }
        catch (Exception ex)
        {
            LogLauncherEvent($"[ATTACH: ERROR] Ошибка привязки к процессу: {ex.Message}");
        }
    }

    /// <summary>
    /// Если перед первым запуском в GameDir нет options.txt, создает его с дефолтными настройками.
    /// Не перезаписывает, если файл уже существует.
    /// </summary>
    public static void EnsureDefaultOptions(string gameDir)
    {
        try
        {
            var optionsPath = Path.Combine(gameDir, "options.txt");
            if (!File.Exists(optionsPath))
            {
                Directory.CreateDirectory(gameDir);
                var content = "version:3465\nlang:ru_ru\nguiScale:2\nfullscreen:true\n";
                File.WriteAllText(optionsPath, content, new UTF8Encoding(false));
                LogLauncherEvent($"[OPTIONS] Создан файл настроек по умолчанию (options.txt): {optionsPath}");
            }
        }
        catch (Exception ex)
        {
            LogLauncherEvent($"[OPTIONS: ERROR] Ошибка создания options.txt: {ex.Message}");
        }
    }

    /// <summary>
    /// Распаковка нативных библиотек (LWJGL и др.) в папку &lt;GameDir&gt;\versions\&lt;version&gt;\natives,
    /// на которую указывает -Djava.library.path.
    /// </summary>
    public static void EnsureNativesExtracted(string gameDir, string versionName)
    {
        try
        {
            var nativesDir = Path.Combine(gameDir, "versions", versionName, "natives");
            Directory.CreateDirectory(nativesDir);

            var libDir = Path.Combine(gameDir, "libraries");
            if (!Directory.Exists(libDir))
                return;

            string pattern = Environment.Is64BitOperatingSystem
                ? "*natives-windows.jar"
                : "*natives-windows-x86.jar";

            var nativeJars = Directory.GetFiles(libDir, pattern, SearchOption.AllDirectories);
            foreach (var jar in nativeJars)
            {
                try
                {
                    using var zip = ZipFile.OpenRead(jar);
                    foreach (var entry in zip.Entries)
                    {
                        if (entry.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                        {
                            var destPath = Path.Combine(nativesDir, entry.Name);
                            if (!File.Exists(destPath) || new FileInfo(destPath).Length != entry.Length)
                            {
                                entry.ExtractToFile(destPath, overwrite: true);
                                LogLauncherEvent($"[NATIVES] Извлечена библиотека {entry.Name} из {Path.GetFileName(jar)}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogLauncherEvent($"[NATIVES: ERROR] Ошибка распаковки {jar}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            LogLauncherEvent($"[NATIVES: ERROR] Ошибка подготовки natives: {ex.Message}");
        }
    }

    /// <summary>
    /// Парсер аргументов командной строки с учетом кавычек.
    /// </summary>
    public static List<string> ParseCommandLineArguments(string commandLine)
    {
        var tokens = new List<string>();
        if (string.IsNullOrWhiteSpace(commandLine))
            return tokens;

        var sb = new StringBuilder();
        bool inQuotes = false;
        for (int i = 0; i < commandLine.Length; i++)
        {
            char c = commandLine[i];
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (sb.Length > 0)
                {
                    tokens.Add(sb.ToString());
                    sb.Clear();
                }
            }
            else
            {
                sb.Append(c);
            }
        }
        if (sb.Length > 0)
        {
            tokens.Add(sb.ToString());
        }
        return tokens;
    }

    /// <summary>
    /// Форматирование аргумента для Java argfile: аргументы с пробелами оборачиваются в кавычки,
    /// внутри кавычек экранируются обратный слэш (\ -> \\) и кавычки (" -> \").
    /// </summary>
    public static string FormatArgForJavaArgFile(string token)
    {
        if (token.Contains(' ') || token.Contains('\t'))
        {
            var escaped = token.Replace(@"\", @"\\").Replace("\"", "\\\"");
            return $"\"{escaped}\"";
        }
        return token;
    }

    /// <summary>
    /// Поиск установленной Java 17+.
    /// Приоритет отдается локальной Java в &lt;GameDir&gt;\runtime, затем системным путям.
    /// </summary>
    public string? ResolveJavaRuntime(string gameDir)
    {
        var candidates = new List<string>();

        // 1. Приоритетный путь Mojang Java 17 (java-runtime-gamma) в папке игры
        var gammaDefault = Path.Combine(gameDir, "runtime", "windows-x64", "java-runtime-gamma", "bin", "javaw.exe");
        if (File.Exists(gammaDefault))
        {
            candidates.Add(gammaDefault);
        }

        var gammaAlt = Path.Combine(gameDir, "runtime", "java-runtime-gamma", "windows", "java-runtime-gamma", "bin", "javaw.exe");
        if (File.Exists(gammaAlt))
        {
            candidates.Add(gammaAlt);
        }

        // Поиск любых других javaw.exe внутри <GameDir>\runtime
        var runtimeDir = Path.Combine(gameDir, "runtime");
        if (Directory.Exists(runtimeDir))
        {
            try
            {
                var found = Directory.GetFiles(runtimeDir, "javaw.exe", SearchOption.AllDirectories);
                foreach (var f in found)
                {
                    if (!candidates.Contains(f))
                    {
                        candidates.Add(f);
                    }
                }
            }
            catch { }
        }

        // 2. Системные переменные окружения и директории (запасной вариант)
        var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrWhiteSpace(javaHome))
        {
            candidates.Add(Path.Combine(javaHome, "bin", "javaw.exe"));
        }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (Directory.Exists(Path.Combine(programFiles, "Eclipse Adoptium")))
        {
            foreach (var dir in Directory.GetDirectories(Path.Combine(programFiles, "Eclipse Adoptium")))
            {
                candidates.Add(Path.Combine(dir, "bin", "javaw.exe"));
            }
        }
        if (Directory.Exists(Path.Combine(programFiles, "Microsoft")))
        {
            foreach (var dir in Directory.GetDirectories(Path.Combine(programFiles, "Microsoft"), "jdk*"))
            {
                candidates.Add(Path.Combine(dir, "bin", "javaw.exe"));
            }
        }
        if (Directory.Exists(Path.Combine(programFiles, "Java")))
        {
            foreach (var dir in Directory.GetDirectories(Path.Combine(programFiles, "Java"), "jdk*"))
            {
                candidates.Add(Path.Combine(dir, "bin", "javaw.exe"));
            }
        }

        // Системный javaw в PATH
        candidates.Add("javaw.exe");

        foreach (var candidate in candidates)
        {
            if (candidate == "javaw.exe" || File.Exists(candidate))
            {
                var major = GetJavaMajorVersion(candidate);
                if (major >= 17)
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Определение мажорной версии Java через чтение release-файла или запуск javaw -version.
    /// </summary>
    public static int GetJavaMajorVersion(string javaExecutable)
    {
        try
        {
            // Вариант 1: Быстрое чтение файла release в корне JRE
            if (javaExecutable != "javaw.exe" && File.Exists(javaExecutable))
            {
                var binDir = Path.GetDirectoryName(javaExecutable);
                var jreDir = binDir != null ? Directory.GetParent(binDir)?.FullName : null;
                if (!string.IsNullOrEmpty(jreDir))
                {
                    var releasePath = Path.Combine(jreDir, "release");
                    if (File.Exists(releasePath))
                    {
                        var lines = File.ReadAllLines(releasePath);
                        foreach (var line in lines)
                        {
                            if (line.StartsWith("JAVA_VERSION=", StringComparison.OrdinalIgnoreCase))
                            {
                                var verStr = line.Split('=')[1].Trim('"', ' ', '\'');
                                var match = Regex.Match(verStr, @"^(?:1\.)?(\d+)");
                                if (match.Success && int.TryParse(match.Groups[1].Value, out int ver))
                                {
                                    return ver;
                                }
                            }
                        }
                    }
                }
            }

            // Вариант 2: Запуск java/javaw -version и чтение вывода ошибки (stderr)
            var psi = new ProcessStartInfo
            {
                FileName = javaExecutable,
                Arguments = "-version",
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return 0;

            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit(3000);

            var versionMatch = Regex.Match(stderr, @"(?:version\s+""(?:1\.)?(\d+))");
            if (versionMatch.Success && int.TryParse(versionMatch.Groups[1].Value, out int parsedVer))
            {
                return parsedVer;
            }
        }
        catch { }

        return 0;
    }

    /// <summary>
    /// Генерация детерминированного Offline UUID v3 (MD5 от UTF-8 строки "OfflinePlayer:" + nick).
    /// </summary>
    public static string GenerateOfflineUuid(string nickname)
    {
        var rawString = "OfflinePlayer:" + (nickname ?? string.Empty).Trim();
        var bytes = Encoding.UTF8.GetBytes(rawString);
        var hash = MD5.HashData(bytes);

        // Установка версии 3 (биты 4-7 шестого байта = 0011)
        hash[6] = (byte)((hash[6] & 0x0F) | 0x30);
        // Установка варианта RFC 4122 (биты 6-7 восьмого байта = 10)
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);

        // Формирование строки в big-endian, как Java UUID.nameUUIDFromBytes:
        // байты 0-3 (8 hex), 4-5 (4 hex), 6-7 (4 hex, начинается с 3), 8-9 (4 hex, начинается с 8/9/a/b), 10-15 (12 hex)
        return $"{Convert.ToHexString(hash, 0, 4)}-{Convert.ToHexString(hash, 4, 2)}-{Convert.ToHexString(hash, 6, 2)}-{Convert.ToHexString(hash, 8, 2)}-{Convert.ToHexString(hash, 10, 6)}".ToLowerInvariant();
    }

    /// <summary>
    /// Автоматическая проверка и загрузка недостающего окружения Minecraft (5 этапов):
    /// 1. "Minecraft 1.20.1" (Клиент и базовый манифест)
    /// 2. "Java 17" (Официальный Mojang JRE java-runtime-gamma)
    /// 3. "Fabric" (Профиль и загрузчик Fabric)
    /// 4. "Библиотеки" (Объединенные библиотеки Vanilla и Fabric)
    /// 5. "Ресурсы" (Индекс ассетов 5 и файлы объектов)
    /// </summary>
    public async Task EnsureInstalledAsync(
        string gameDir,
        IProgress<InstallProgressReport>? progress = null,
        Action<string>? onLogReceived = null,
        CancellationToken cancellationToken = default)
    {
        LogLauncherEvent($"[INSTALL] Начало проверки/установки окружения в {gameDir}");
        var mcPath = new MinecraftPath(gameDir);
        var launcher = new MinecraftLauncher(mcPath);
        var rules = new RulesEvaluatorContext(LauncherOSRule.Current);

        var clientExtractor = launcher.FileExtractors.OfType<ClientFileExtractor>().FirstOrDefault() ?? new ClientFileExtractor();
        var libExtractor = launcher.FileExtractors.OfType<LibraryFileExtractor>().FirstOrDefault() ?? new LibraryFileExtractor("client", launcher.RulesEvaluator);
        var assetExtractor = launcher.FileExtractors.OfType<AssetFileExtractor>().FirstOrDefault() ?? new AssetFileExtractor(_httpClient);
        var javaExtractor = launcher.FileExtractors.OfType<JavaFileExtractor>().FirstOrDefault() ?? new JavaFileExtractor(_httpClient, launcher.JavaPathResolver);
        var logExtractor = launcher.FileExtractors.OfType<LogFileExtractor>().FirstOrDefault() ?? new LogFileExtractor();

        // ==============================================================
        // ЭТАП 1: Minecraft 1.20.1 (Базовый манифест и client.jar)
        // ==============================================================
        onLogReceived?.Invoke("[AURA-INSTALL] Этап 1/5: Проверка Minecraft 1.20.1...");
        progress?.Report(new InstallProgressReport
        {
            Stage = "Minecraft 1.20.1",
            StageIndex = 1,
            TotalStages = 5,
            Percentage = 0,
            DetailText = "Проверка клиента Minecraft 1.20.1..."
        });

        var vanillaVersion = await RetryAsync(async () =>
            await launcher.GetVersionAsync(MinecraftVersion, cancellationToken),
            "Загрузка манифеста Minecraft 1.20.1", onLogReceived, cancellationToken);

        var clientFiles = (await clientExtractor.Extract(mcPath, vanillaVersion, rules, cancellationToken)).ToList();
        var logFiles = (await logExtractor.Extract(mcPath, vanillaVersion, rules, cancellationToken)).ToList();
        var stage1Files = clientFiles.Concat(logFiles).ToList();

        await InstallGameFilesStageAsync(
            launcher, stage1Files, "Minecraft 1.20.1", 1, 5, progress, onLogReceived, cancellationToken);

        // ==============================================================
        // ЭТАП 2: Java 17 (Mojang java-runtime-gamma)
        // ==============================================================
        onLogReceived?.Invoke("[AURA-INSTALL] Этап 2/5: Проверка Java 17...");
        progress?.Report(new InstallProgressReport
        {
            Stage = "Java 17",
            StageIndex = 2,
            TotalStages = 5,
            Percentage = 20,
            DetailText = "Проверка среды выполнения Java 17..."
        });

        var existingJava = ResolveJavaRuntime(gameDir);
        bool hasLocalJava17 = !string.IsNullOrWhiteSpace(existingJava) && existingJava.StartsWith(gameDir, StringComparison.OrdinalIgnoreCase);

        if (!hasLocalJava17)
        {
            onLogReceived?.Invoke("[AURA-INSTALL] Локальная Java 17 не найдена в runtime. Загрузка java-runtime-gamma от Mojang...");
            var javaFiles = (await javaExtractor.Extract(mcPath, vanillaVersion, rules, cancellationToken)).ToList();
            await InstallGameFilesStageAsync(
                launcher, javaFiles, "Java 17", 2, 5, progress, onLogReceived, cancellationToken);
        }
        else
        {
            onLogReceived?.Invoke($"[AURA-INSTALL] Найдена готовая Java 17 в runtime: {existingJava}");
            progress?.Report(new InstallProgressReport
            {
                Stage = "Java 17",
                StageIndex = 2,
                TotalStages = 5,
                Percentage = 40,
                DetailText = "Java 17 готова"
            });
        }

        // ==============================================================
        // ЭТАП 3: Fabric (Профиль загрузчика)
        // ==============================================================
        var fabricLoader = ResolveFabricLoaderVersion(gameDir);
        onLogReceived?.Invoke($"[AURA-INSTALL] Этап 3/5: Проверка профиля Fabric Loader {fabricLoader}...");
        progress?.Report(new InstallProgressReport
        {
            Stage = "Fabric",
            StageIndex = 3,
            TotalStages = 5,
            Percentage = 40,
            DetailText = $"Проверка профиля Fabric Loader {fabricLoader}..."
        });

        var fabricDir = Path.Combine(gameDir, "versions", $"fabric-loader-{fabricLoader}-{MinecraftVersion}");
        var fabricJson = Path.Combine(fabricDir, $"fabric-loader-{fabricLoader}-{MinecraftVersion}.json");

        if (!File.Exists(fabricJson))
        {
            onLogReceived?.Invoke($"[AURA-INSTALL] Загрузка профиля Fabric с meta.fabricmc.net...");
            var fabricInstaller = new FabricInstaller(_httpClient);
            await RetryAsync(async () =>
            {
                await fabricInstaller.Install(MinecraftVersion, fabricLoader, mcPath);
                return true;
            }, "Загрузка профиля Fabric", onLogReceived, cancellationToken);
        }

        await EnsureFabricClientJarAsync(gameDir, fabricLoader, onLogReceived, cancellationToken);

        var fabricVersionName = $"fabric-loader-{fabricLoader}-{MinecraftVersion}";
        var fabricVersion = await RetryAsync(async () =>
            await launcher.GetVersionAsync(fabricVersionName, cancellationToken),
            "Чтение профиля Fabric", onLogReceived, cancellationToken);

        progress?.Report(new InstallProgressReport
        {
            Stage = "Fabric",
            StageIndex = 3,
            TotalStages = 5,
            Percentage = 60,
            DetailText = "Профиль Fabric загружен"
        });

        // ==============================================================
        // ЭТАП 4: Библиотеки (Vanilla + Fabric)
        // ==============================================================
        onLogReceived?.Invoke("[AURA-INSTALL] Этап 4/5: Проверка библиотек игры и Fabric...");
        progress?.Report(new InstallProgressReport
        {
            Stage = "Библиотеки",
            StageIndex = 4,
            TotalStages = 5,
            Percentage = 60,
            DetailText = "Проверка библиотек..."
        });

        var vanillaLibs = await libExtractor.Extract(mcPath, vanillaVersion, rules, cancellationToken);
        var fabricLibs = await libExtractor.Extract(mcPath, fabricVersion, rules, cancellationToken);
        var allLibraries = vanillaLibs.Concat(fabricLibs)
            .GroupBy(f => f.Path)
            .Select(g => g.First())
            .ToList();

        await InstallGameFilesStageAsync(
            launcher, allLibraries, "Библиотеки", 4, 5, progress, onLogReceived, cancellationToken);

        // ==============================================================
        // ЭТАП 5: Ресурсы (Ассеты Minecraft 1.20.1)
        // ==============================================================
        onLogReceived?.Invoke("[AURA-INSTALL] Этап 5/5: Проверка ресурсов (ассетов)...");
        progress?.Report(new InstallProgressReport
        {
            Stage = "Ресурсы",
            StageIndex = 5,
            TotalStages = 5,
            Percentage = 80,
            DetailText = "Проверка ассетов Minecraft..."
        });

        var assets = (await assetExtractor.Extract(mcPath, vanillaVersion, rules, cancellationToken)).ToList();
        await InstallGameFilesStageAsync(
            launcher, assets, "Ресурсы", 5, 5, progress, onLogReceived, cancellationToken);

        progress?.Report(new InstallProgressReport
        {
            Stage = "Ресурсы",
            StageIndex = 5,
            TotalStages = 5,
            Percentage = 100,
            DetailText = "Все компоненты установлены!"
        });

        EnsureNativesExtracted(gameDir, fabricVersionName);

        LogLauncherEvent("[INSTALL] Окружение Minecraft и Fabric успешно проверено/установлено.");
        onLogReceived?.Invoke("[AURA-INSTALL] Установка окружения успешно завершена.");
    }

    /// <summary>
    /// Потокобезопасная установка пачки файлов этапа с расчетом скорости и обработкой разрывов сети (до 3 попыток).
    /// </summary>
    private static async Task InstallGameFilesStageAsync(
        MinecraftLauncher launcher,
        IEnumerable<GameFile> files,
        string stageName,
        int stageIndex,
        int totalStages,
        IProgress<InstallProgressReport>? progress,
        Action<string>? onLogReceived,
        CancellationToken cancellationToken)
    {
        var fileList = files.ToList();
        if (fileList.Count == 0) return;

        var sw = Stopwatch.StartNew();
        long lastBytes = 0;
        long lastTime = 0;
        string speedStr = string.Empty;
        string currentDetail = string.Empty;

        var fileProg = new SyncProgress<InstallerProgressChangedEventArgs>(e =>
        {
            currentDetail = $"{e.Name} ({e.ProgressedTasks}/{e.TotalTasks})";
        });

        var byteProg = new SyncProgress<ByteProgress>(bp =>
        {
            if (bp.TotalBytes > 0)
            {
                double stagePct = (double)bp.ProgressedBytes / bp.TotalBytes * 100.0;
                long elapsed = sw.ElapsedMilliseconds;
                if (elapsed - lastTime >= 400)
                {
                    long deltaBytes = bp.ProgressedBytes - lastBytes;
                    double seconds = (elapsed - lastTime) / 1000.0;
                    if (seconds > 0)
                    {
                        double bytesPerSec = deltaBytes / seconds;
                        double mbPerSec = bytesPerSec / (1024.0 * 1024.0);
                        speedStr = mbPerSec >= 1.0
                            ? $"{mbPerSec:F1} МБ/с"
                            : $"{(bytesPerSec / 1024.0):F0} КБ/с";
                    }
                    lastBytes = bp.ProgressedBytes;
                    lastTime = elapsed;
                }

                double stageWeight = 100.0 / totalStages;
                double overallPct = (stageIndex - 1) * stageWeight + (stagePct * stageWeight / 100.0);

                progress?.Report(new InstallProgressReport
                {
                    Stage = stageName,
                    StageIndex = stageIndex,
                    TotalStages = totalStages,
                    Percentage = Math.Clamp(overallPct, 0, 100),
                    FormattedSpeed = speedStr,
                    DetailText = currentDetail
                });
            }
        });

        int maxRetries = 3;
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await launcher.GameInstaller.Install(fileList, fileProg, byteProg, cancellationToken);
                break;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                if (attempt >= maxRetries)
                {
                    throw new InvalidOperationException(
                        $"Сбой сети на этапе \"{stageName}\" после {maxRetries} попыток. Проверьте интернет-соединение. Ошибка: {ex.Message}", ex);
                }

                onLogReceived?.Invoke($"[AURA-INSTALL] Сбой сети ({stageName}, попытка {attempt}/{maxRetries}): {ex.Message}. Повтор через 2с...");
                await Task.Delay(2000, cancellationToken);
            }
        }
    }

    private static async Task<T> RetryAsync<T>(
        Func<Task<T>> action,
        string operationName,
        Action<string>? onLogReceived,
        CancellationToken cancellationToken,
        int maxRetries = 3)
    {
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await action();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                if (attempt >= maxRetries)
                {
                    throw new InvalidOperationException($"Ошибка операции \"{operationName}\" после {maxRetries} попыток: {ex.Message}", ex);
                }

                onLogReceived?.Invoke($"[AURA-RETRY] Попытка {attempt}/{maxRetries} для \"{operationName}\" не удалась: {ex.Message}. Повтор...");
                await Task.Delay(2000, cancellationToken);
            }
        }

        throw new InvalidOperationException($"Не удалось выполнить \"{operationName}\"");
    }

    /// <summary>
    /// Запуск процесса игры с гарантированной установкой окружения и сохранением логов.
    /// </summary>
    public async Task<Process> LaunchGameAsync(
        LauncherConfig config,
        Action<string>? onLogReceived = null,
        Action<int, string>? onGameExited = null,
        IProgress<InstallProgressReport>? installProgress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        var nickResult = NicknameValidator.Validate(config.Nickname);
        if (!nickResult.IsValid)
        {
            LogLauncherEvent("[LAUNCH] Невалидный никнейм игрока. Запуск отменен.");
            throw new InvalidOperationException($"Игровой никнейм не соответствует правилам (3-16 символов, A-Z, 0-9, _): {nickResult.Error}");
        }

        var gameDir = ResolveMinecraftDirectory(config.GameDir);
        LogLauncherEvent($"[LAUNCH] Запуск игры для пользователя '{config.Nickname}', RAM: {config.RamMb} MB, GameDir: {gameDir}");

        // 0. Настройки игры по умолчанию (options.txt) перед первым запуском
        EnsureDefaultOptions(gameDir);

        // 1. Автоматическая проверка и докачка недостающих компонентов окружения
        await EnsureInstalledAsync(gameDir, installProgress, onLogReceived, cancellationToken);

        // 2. Определение пути к Java 17+
        var javaPath = ResolveJavaRuntime(gameDir);
        if (string.IsNullOrWhiteSpace(javaPath))
        {
            throw new InvalidOperationException("Не найдена Java 17+ для запуска игры Minecraft 1.20.1.");
        }
        onLogReceived?.Invoke($"[AURA-JAVA] Исполняемый файл Java 17: {javaPath}");

        // 3. Детерминированный offline UUID
        var playerUuid = GenerateOfflineUuid(config.Nickname);
        onLogReceived?.Invoke($"[AURA-UUID] Сгенерирован детерминированный Offline UUID: {playerUuid}");
        LogLauncherEvent($"[LAUNCH] Ник: {config.Nickname}, Offline UUID: {playerUuid}, Java: {javaPath}");

        // 4. Подготовка параметров запуска CmlLib.Core
        var fabricLoader = ResolveFabricLoaderVersion(gameDir);
        await EnsureFabricClientJarAsync(gameDir, fabricLoader, onLogReceived, cancellationToken);
        var mcPath = new MinecraftPath(gameDir);
        var launcher = new MinecraftLauncher(mcPath);
        var fabricVersionName = $"fabric-loader-{fabricLoader}-{MinecraftVersion}";

        // Гарантируем распаковку нативных библиотек в папку, на которую указывает -Djava.library.path
        EnsureNativesExtracted(gameDir, fabricVersionName);

        var launchOptions = new MLaunchOption
        {
            JavaPath = javaPath,
            MaximumRamMb = config.RamMb,
            MinimumRamMb = Math.Min(1024, config.RamMb),
            Session = new MSession(config.Nickname, "0", playerUuid)
            {
                UserType = "legacy"
            },
            ExtraJvmArguments = new[]
            {
                new MArgument("-DFabricMcEmu= net.minecraft.client.main.Main "),
                new MArgument("-XX:+UnlockExperimentalVMOptions"),
                new MArgument("-XX:+UseG1GC"),
                new MArgument("-XX:G1NewSizePercent=20"),
                new MArgument("-XX:G1ReservePercent=20"),
                new MArgument("-XX:MaxGCPauseMillis=50"),
                new MArgument("-XX:G1HeapRegionSize=16M")
            }
        };

        var process = await launcher.CreateProcessAsync(fabricVersionName, launchOptions);
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.CreateNoWindow = true;

        // Генерация Java @argsFile
        var rawArgs = process.StartInfo.Arguments;
        var tokens = ParseCommandLineArguments(rawArgs);

        // Убираем аргумент --fullscreen (игра прочитает из options.txt)
        tokens.RemoveAll(t => t.Equals("--fullscreen", StringComparison.OrdinalIgnoreCase));

        var formattedLines = new List<string>();
        var maskedLinesForLog = new List<string>();
        bool nextIsToken = false;
        bool nextIsClasspath = false;

        for (int i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];

            if (nextIsClasspath)
            {
                // Аргумент classpath всегда оборачивается в кавычки с экранированием обратных слэшей
                var escapedCp = token.Replace(@"\", @"\\").Replace("\"", "\\\"");
                var quotedCp = $"\"{escapedCp}\"";
                formattedLines.Add(quotedCp);
                maskedLinesForLog.Add(quotedCp);
                nextIsClasspath = false;
                continue;
            }

            if (token.Equals("-cp", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("-classpath", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("--class-path", StringComparison.OrdinalIgnoreCase))
            {
                formattedLines.Add(token);
                maskedLinesForLog.Add(token);
                nextIsClasspath = true;
                continue;
            }

            var formatted = FormatArgForJavaArgFile(token);
            formattedLines.Add(formatted);

            if (nextIsToken)
            {
                maskedLinesForLog.Add("\"***\"");
                nextIsToken = false;
            }
            else
            {
                if (token.Equals("--accessToken", StringComparison.OrdinalIgnoreCase))
                {
                    nextIsToken = true;
                    maskedLinesForLog.Add(formatted);
                }
                else if (token.StartsWith("--accessToken=", StringComparison.OrdinalIgnoreCase))
                {
                    maskedLinesForLog.Add("--accessToken=\"***\"");
                }
                else
                {
                    maskedLinesForLog.Add(formatted);
                }
            }
        }

        var argsFilePath = Path.Combine(gameDir, "argsFile.txt");
        await File.WriteAllLinesAsync(argsFilePath, formattedLines, new UTF8Encoding(false), cancellationToken);

        // Запись содержимого argsFile в launcher.log с маскировкой accessToken
        var maskedArgsContent = string.Join(Environment.NewLine, maskedLinesForLog);
        LogLauncherEvent($"[ARGSFILE]\n{maskedArgsContent}");
        onLogReceived?.Invoke($"[AURA-ARGSFILE] Записан файл аргументов: {argsFilePath}");

        // Передача аргумента @argsFile
        process.StartInfo.ArgumentList.Clear();
        process.StartInfo.Arguments = argsFilePath.Contains(' ') ? $"@\"{argsFilePath}\"" : $"@{argsFilePath}";

        LogLauncherEvent($"[ARGS] {process.StartInfo.FileName} {process.StartInfo.Arguments}");
        onLogReceived?.Invoke($"[AURA-ARGS] {process.StartInfo.FileName} {process.StartInfo.Arguments}");

        // 5. Настройка логирования в файл <GameDir>\logs\launcher-game.log
        var logsDir = Path.Combine(gameDir, "logs");
        Directory.CreateDirectory(logsDir);
        var gameLogPath = Path.Combine(logsDir, "launcher-game.log");

        var gameLogWriter = new StreamWriter(new FileStream(gameLogPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
        {
            AutoFlush = true
        };

        process.OutputDataReceived += (s, e) =>
        {
            if (e.Data != null)
            {
                gameLogWriter.WriteLine(e.Data);
                onLogReceived?.Invoke(e.Data);
            }
        };

        process.ErrorDataReceived += (s, e) =>
        {
            if (e.Data != null)
            {
                gameLogWriter.WriteLine("[STDERR] " + e.Data);
                onLogReceived?.Invoke("[STDERR] " + e.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        _gameProcess = process;

        LogLauncherEvent($"[LAUNCH] Игровой процесс запущен (PID {process.Id})");
        onLogReceived?.Invoke($"[AURA-PROCESS] Игровой процесс запущен с PID {process.Id}");

        // Отслеживание завершения игры
        _ = Task.Run(async () =>
        {
            try
            {
                await process.WaitForExitAsync();
                await gameLogWriter.FlushAsync();
                gameLogWriter.Dispose();

                int exitCode = process.ExitCode;
                _gameProcess = null;

                LogLauncherEvent($"[EXIT] Игра (PID {process.Id}) завершилась с кодом выхода {exitCode}");
                GameExited?.Invoke(this, exitCode);
                onGameExited?.Invoke(exitCode, gameLogPath);
            }
            catch (Exception ex)
            {
                _gameProcess = null;
                LogLauncherEvent($"[EXIT-ERROR] Ошибка при ожидании процесса игры: {ex.Message}");
            }
        });

        return process;
    }

    /// <summary>
    /// Обеспечивает корректность профиля Fabric и наличие клиентского jar в classpath:
    /// 1. Добавляет "jar": "1.20.1" в fabric-loader-*.json (если meta.fabricmc.net не включила его).
    /// 2. Гарантирует наличие fabric-loader-*.jar (копия vanilla 1.20.1.jar).
    /// </summary>
    private static async Task EnsureFabricClientJarAsync(string gameDir, string fabricLoaderVersion, Action<string>? onLogReceived, CancellationToken cancellationToken)
    {
        try
        {
            var vanillaDir = Path.Combine(gameDir, "versions", MinecraftVersion);
            var vanillaJar = Path.Combine(vanillaDir, $"{MinecraftVersion}.jar");

            var fabricDir = Path.Combine(gameDir, "versions", $"fabric-loader-{fabricLoaderVersion}-{MinecraftVersion}");
            var fabricJson = Path.Combine(fabricDir, $"fabric-loader-{fabricLoaderVersion}-{MinecraftVersion}.json");
            var fabricJar = Path.Combine(fabricDir, $"fabric-loader-{fabricLoaderVersion}-{MinecraftVersion}.jar");

            // 1. Патч JSON профиля Fabric (добавление поля "jar": "1.20.1", если отсутствует)
            if (File.Exists(fabricJson))
            {
                var jsonText = await File.ReadAllTextAsync(fabricJson, cancellationToken);
                if (!jsonText.Contains("\"jar\"", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var node = System.Text.Json.Nodes.JsonNode.Parse(jsonText);
                        if (node is System.Text.Json.Nodes.JsonObject obj)
                        {
                            obj["jar"] = MinecraftVersion;
                            await File.WriteAllTextAsync(fabricJson, obj.ToJsonString(), cancellationToken);
                            LogLauncherEvent($"[FABRIC-FIX] Добавлено 'jar': '{MinecraftVersion}' в {fabricJson}");
                        }
                    }
                    catch (Exception ex)
                    {
                        LogLauncherEvent($"[FABRIC-FIX: WARN] Не удалось обновить JSON профиля: {ex.Message}");
                    }
                }
            }

            // 2. Копирование 1.20.1.jar в fabric-loader-*.jar, если файл отсутствует или размер отличается
            if (File.Exists(vanillaJar))
            {
                Directory.CreateDirectory(fabricDir);
                bool needsCopy = !File.Exists(fabricJar);
                if (!needsCopy)
                {
                    var vInfo = new FileInfo(vanillaJar);
                    var fInfo = new FileInfo(fabricJar);
                    if (vInfo.Length != fInfo.Length || vInfo.Length == 0)
                    {
                        needsCopy = true;
                    }
                }

                if (needsCopy)
                {
                    File.Copy(vanillaJar, fabricJar, true);
                    onLogReceived?.Invoke($"[AURA-INSTALL] Синхронизирован клиентский JAR ({MinecraftVersion}) для Fabric Loader.");
                    LogLauncherEvent($"[FABRIC-FIX] Скопирован {vanillaJar} -> {fabricJar}");
                }
            }
        }
        catch (Exception ex)
        {
            LogLauncherEvent($"[FABRIC-FIX: ERROR] Ошибка синхронизации Fabric JAR: {ex.Message}");
        }
    }

    /// <summary>
    /// Логирование событий самого лаунчера в %AppData%\Aura\launcher.log.
    /// </summary>
    public static void LogLauncherEvent(string message)
    {
        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var logDir = Path.Combine(appData, "Aura");
            Directory.CreateDirectory(logDir);
            var logPath = Path.Combine(logDir, "launcher.log");
            File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n");
        }
        catch { }
    }
}
