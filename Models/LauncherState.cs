namespace AuraLauncher.Models;

/// <summary>
/// Состояния жизненного цикла лаунчера.
/// </summary>
public enum LauncherState
{
    Idle,
    Checking,
    Downloading,
    Ready,
    Error
}
