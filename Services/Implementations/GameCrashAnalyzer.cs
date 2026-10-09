using System.IO;
using System.Text;

namespace AuraLauncher.Services.Implementations;

public sealed record GameCrashAnalysisResult(
    string Title,
    string UserAdvice,
    string TechnicalSummary);

public static class GameCrashAnalyzer
{
    private static readonly string[] GpuMarkers =
    [
        "nvoglv64.dll",
        "atio6axx.dll",
        "amdocl",
        "ig75icd64.dll",
        "ig9icd64.dll",
        "igxelpicd64.dll",
        "GLFW error",
        "GL_OUT_OF_MEMORY",
        "CompileShader",
        "net.irisshaders",
        "me.jellysquid.mods.sodium"
    ];

    private static readonly string[] ModConflictMarkers =
    [
        "MixinApplyError",
        "MixinTransformerError",
        "Mixin prepare failed",
        "Mixin apply failed",
        "Mod resolution failed",
        "Incompatible mods found",
        "net.fabricmc.loader.impl.FormattedException"
    ];

    private static readonly string[] WorldCorruptionMarkers =
    [
        "SessionLock",
        "already locked",
        "Corrupt",
        "RegionFile",
        "Failed to read chunk",
        "level.dat"
    ];

    public static GameCrashAnalysisResult AnalyzeCrash(string gameDir, int exitCode)
    {
        var snippetBuilder = new StringBuilder();
        snippetBuilder.AppendLine($"ExitCode: {exitCode}");

        string combinedLog = CollectRecentLogs(gameDir, snippetBuilder);

        if (ContainsAny(combinedLog, ["OutOfMemoryError", "Out of Memory Error", "There is insufficient memory for the Java Runtime Environment"]))
        {
            return new GameCrashAnalysisResult(
                "Нехватка оперативной памяти (ОЗУ)",
                "Увеличьте выделенный объём памяти в Настройках лаунчера (рекомендуется 6-8 ГБ).",
                snippetBuilder.ToString());
        }

        if (exitCode == -805306369)
        {
            return new GameCrashAnalysisResult(
                "Игра перестала отвечать и была закрыта",
                "Обычно это происходит при нехватке ОЗУ или тяжёлых шейдерах. Выделите больше памяти в Настройках или включите профиль «Баланс».",
                snippetBuilder.ToString());
        }

        if (ContainsAny(combinedLog, GpuMarkers) || exitCode is -1073741819 or -1073740791)
        {
            return new GameCrashAnalysisResult(
                "Сбой видеодрайвера или шейдеров",
                "Отключите шейдеры во вкладке Мастерская, обновите драйвер видеокарты или выберите профиль «Слабый ПК».",
                snippetBuilder.ToString());
        }

        if (ContainsAny(combinedLog, ModConflictMarkers))
        {
            return new GameCrashAnalysisResult(
                "Конфликт модов при запуске",
                "Проверьте добавленные вручную моды во вкладке Мастерская или обновите сборку.",
                snippetBuilder.ToString());
        }

        if (ContainsAny(combinedLog, WorldCorruptionMarkers))
        {
            return new GameCrashAnalysisResult(
                "Ошибка чтения файлов мира",
                "Восстановите мир из резервной копии во вкладке Мастерская (раздел Миры).",
                snippetBuilder.ToString());
        }

        return new GameCrashAnalysisResult(
            $"Игра завершилась с ошибкой (код {exitCode})",
            "Нажмите «Отчёт», чтобы отправить лог разработчикам для разбора причины.",
            snippetBuilder.ToString());
    }

    private static string CollectRecentLogs(string gameDir, StringBuilder summaryBuilder)
    {
        if (string.IsNullOrWhiteSpace(gameDir) || !Directory.Exists(gameDir))
        {
            return string.Empty;
        }

        var combined = new StringBuilder();
        var recentThresholdUtc = DateTime.UtcNow.AddMinutes(-10);

        try
        {
            var crashReportsDir = Path.Combine(gameDir, "crash-reports");
            if (Directory.Exists(crashReportsDir))
            {
                var latestCrash = new DirectoryInfo(crashReportsDir)
                    .GetFiles("*.txt")
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .FirstOrDefault();

                if (latestCrash != null && latestCrash.LastWriteTimeUtc >= recentThresholdUtc)
                {
                    string crashText = ReadTailText(latestCrash.FullName, 160);
                    combined.AppendLine(crashText);
                    summaryBuilder.AppendLine($"CrashReport ({latestCrash.Name}):");
                    summaryBuilder.AppendLine(Truncate(crashText, 1200));
                }
            }
        }
        catch { }

        try
        {
            var latestHsErr = new DirectoryInfo(gameDir)
                .GetFiles("hs_err_pid*.log")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .FirstOrDefault();

            if (latestHsErr != null && latestHsErr.LastWriteTimeUtc >= recentThresholdUtc)
            {
                string hsErrText = ReadHeadText(latestHsErr.FullName, 80);
                combined.AppendLine(hsErrText);
                summaryBuilder.AppendLine($"NativeCrash ({latestHsErr.Name}):");
                summaryBuilder.AppendLine(Truncate(hsErrText, 800));
            }
        }
        catch { }

        try
        {
            var latestLogPath = Path.Combine(gameDir, "logs", "latest.log");
            if (File.Exists(latestLogPath))
            {
                string latestLogTail = ReadTailText(latestLogPath, 150);
                combined.AppendLine(latestLogTail);
                if (summaryBuilder.Length < 400)
                {
                    summaryBuilder.AppendLine("latest.log tail:");
                    summaryBuilder.AppendLine(Truncate(latestLogTail, 1000));
                }
            }
        }
        catch { }

        try
        {
            var gameLogPath = Path.Combine(gameDir, "logs", "launcher-game.log");
            if (File.Exists(gameLogPath))
            {
                string gameLogTail = ReadTailText(gameLogPath, 120);
                combined.AppendLine(gameLogTail);
            }
        }
        catch { }

        return combined.ToString();
    }

    private static bool ContainsAny(string text, IEnumerable<string> markers)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        foreach (var marker in markers)
        {
            if (text.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string ReadTailText(string filePath, int maxLines)
    {
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(fs, Encoding.UTF8);
        var queue = new Queue<string>(maxLines);
        while (reader.ReadLine() is { } line)
        {
            if (queue.Count >= maxLines)
            {
                queue.Dequeue();
            }
            queue.Enqueue(line);
        }
        return string.Join(Environment.NewLine, queue);
    }

    private static string ReadHeadText(string filePath, int maxLines)
    {
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(fs, Encoding.UTF8);
        var lines = new List<string>(maxLines);
        for (int i = 0; i < maxLines && reader.ReadLine() is { } line; i++)
        {
            lines.Add(line);
        }
        return string.Join(Environment.NewLine, lines);
    }

    private static string Truncate(string value, int maxChars)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxChars)
        {
            return value;
        }

        return value[..maxChars];
    }
}
