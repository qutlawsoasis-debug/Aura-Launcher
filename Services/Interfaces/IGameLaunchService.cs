using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using AuraLauncher.Models;

namespace AuraLauncher.Services.Interfaces;

/// <summary>
/// Сервис подготовки аргументов, автоустановки окружения (CmlLib.Core) и запуска игры Minecraft (Fabric).
/// </summary>
public interface IGameLaunchService
{
    /// <summary>
    /// Определение корневой рабочей папки Minecraft (.minecraft / .aura).
    /// </summary>
    string ResolveMinecraftDirectory(string? customPath = null);

    /// <summary>
    /// Определение пути к исполняемому файлу Java 17+ (javaw.exe).
    /// Приоритет: &lt;GameDir&gt;\runtime, затем системная Java 17+.
    /// </summary>
    string? ResolveJavaRuntime(string gameDir);

    /// <summary>
    /// Локальная проверка готовности окружения (Fabric profile, vanilla jar, Java runtime) без сети.
    /// </summary>
    bool CheckEnvironmentInstalled(string gameDir);

    /// <summary>
    /// Флаг активности запущенного процесса игры.
    /// </summary>
    bool IsGameRunning { get; }

    /// <summary>
    /// Текущий активный процесс игры Minecraft (если запущен).
    /// </summary>
    Process? CurrentGameProcess { get; }

    /// <summary>
    /// Поиск уже запущенного процесса javaw.exe с командной строкой, содержащей gameDir.
    /// </summary>
    Process? FindRunningGameProcess(string gameDir);

    /// <summary>
    /// Событие завершения запущенной игры (передает код выхода).
    /// </summary>
    event EventHandler<int>? GameExited;

    /// <summary>
    /// Проверка и автоустановка окружения (Vanilla 1.20.1, Java 17, Fabric, библиотеки, ассеты).
    /// </summary>
    Task EnsureInstalledAsync(
        string gameDir,
        IProgress<InstallProgressReport>? progress = null,
        Action<string>? onLogReceived = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Асинхронный запуск процесса игры с потоковым перенаправлением логов в файл и UI.
    /// </summary>
    Task<Process> LaunchGameAsync(
        LauncherConfig config, 
        Action<string>? onLogReceived = null,
        Action<int, string>? onGameExited = null,
        IProgress<InstallProgressReport>? installProgress = null,
        CancellationToken cancellationToken = default);
}
