using System;

namespace AuraLauncher.Services.Interfaces;

/// <summary>
/// Наблюдатель за журналом latest.log для детекции открытия и закрытия LAN-мира в Minecraft.
/// </summary>
public interface ILanWorldWatcher : IDisposable
{
    /// <summary>
    /// Вызывается при успешном открытии мира для сети. Передаёт локальный TCP-порт.
    /// </summary>
    event Action<int>? WorldOpened;

    /// <summary>
    /// Вызывается при закрытии мира / сервера или завершении сессии.
    /// </summary>
    event Action? WorldClosed;

    /// <summary>
    /// Открыт ли мир для сети в данный момент.
    /// </summary>
    bool IsWorldOpen { get; }

    /// <summary>
    /// Текущий активный локальный порт LAN-мира или null, если мир не открыт.
    /// </summary>
    int? CurrentPort { get; }

    /// <summary>
    /// Запустить мониторинг указанного файла latest.log.
    /// </summary>
    void Start(string logFilePath);

    /// <summary>
    /// Остановить мониторинг.
    /// </summary>
    void Stop();
}
