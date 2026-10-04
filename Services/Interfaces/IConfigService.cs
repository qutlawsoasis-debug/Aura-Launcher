using System;
using System.Threading;
using System.Threading.Tasks;
using AuraLauncher.Models;

namespace AuraLauncher.Services.Interfaces;

/// <summary>
/// Сервис управления конфигурацией лаунчера. Единый источник правды для настроек.
/// </summary>
public interface IConfigService
{
    /// <summary>
    /// Текущая активная конфигурация.
    /// </summary>
    LauncherConfig CurrentConfig { get; }

    /// <summary>
    /// Событие изменения конфигурации (смена ника, RAM, путей и т.д.).
    /// </summary>
    event EventHandler<LauncherConfig>? ConfigChanged;

    /// <summary>
    /// Асинхронная загрузка конфигурации из файла (%AppData%\Aura\config.json) с миграцией старого файла.
    /// </summary>
    Task<LauncherConfig> LoadConfigAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Асинхронное сохранение конфигурации в файл.
    /// </summary>
    Task SaveConfigAsync(LauncherConfig config, CancellationToken cancellationToken = default);

    /// <summary>
    /// Атомарное обновление конфигурации через делегат с автоматическим сохранением и вызовом ConfigChanged.
    /// </summary>
    Task UpdateConfigAsync(Action<LauncherConfig> updateAction, CancellationToken cancellationToken = default);
}
