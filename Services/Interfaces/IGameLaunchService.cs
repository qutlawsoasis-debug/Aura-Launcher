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
    /// Определение изолированной папки инстанса для выбранной версии (.aura/instances/1.20.1 или 1.21.1).
    /// </summary>
    string ResolveInstanceDirectory(string? customPath = null, string? version = null);

    /// <summary>
    /// Определение пути к исполняемому файлу Java (javaw.exe) с проверкой минимальной версии.
    /// Приоритет: &lt;GameDir&gt;\runtime, затем системная Java.
    /// </summary>
    string? ResolveJavaRuntime(string gameDir, int minMajorVersion = 17);

    /// <summary>
    /// Локальная проверка готовности окружения (Fabric profile, vanilla jar, Java runtime) без сети.
    /// </summary>
    bool CheckEnvironmentInstalled(string gameDir, string? version = null);

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
    /// Корректно завершает запущенный процесс игры (CloseMainWindow, затем при необходимости Kill).
    /// </summary>
    Task<bool> StopGameProcessAsync(int timeoutMs = 6000);

    /// <summary>
    /// Проверка и автоустановка окружения (Vanilla, Java, Fabric, библиотеки, ассеты).
    /// </summary>
    Task EnsureInstalledAsync(
        string gameDir,
        IProgress<InstallProgressReport>? progress = null,
        Action<string>? onLogReceived = null,
        CancellationToken cancellationToken = default,
        string? version = null);

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
