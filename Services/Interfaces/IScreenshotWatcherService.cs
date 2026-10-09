using System;

namespace AuraLauncher.Services.Interfaces;

public interface IScreenshotWatcherService : IDisposable
{
    event Action<string>? ScreenshotCaptured;
    void StartWatching(string gameDir);
    void StopWatching();
}
