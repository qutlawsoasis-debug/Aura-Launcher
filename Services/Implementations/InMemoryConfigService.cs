using System;
using System.Threading;
using System.Threading.Tasks;
using AuraLauncher.Models;
using AuraLauncher.Services.Interfaces;

namespace AuraLauncher.Services.Implementations;

/// <summary>
/// Реализация IConfigService в оперативной памяти (без сохранения на диск).
/// Используется для безопасных вызовов в тестах и при фоновой подготовке параметров запуска,
/// чтобы исключить перезапись пользовательского config.json.
/// </summary>
public class InMemoryConfigService : IConfigService
{
    private LauncherConfig _config;

    public LauncherConfig CurrentConfig => _config;

    public event EventHandler<LauncherConfig>? ConfigChanged;

    public InMemoryConfigService(LauncherConfig? initialConfig = null)
    {
        _config = initialConfig ?? new LauncherConfig();
    }

    public Task<LauncherConfig> LoadConfigAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_config);
    }

    public Task SaveConfigAsync(LauncherConfig config, CancellationToken cancellationToken = default)
    {
        _config = config;
        ConfigChanged?.Invoke(this, _config);
        return Task.CompletedTask;
    }

    public Task UpdateConfigAsync(Action<LauncherConfig> updateAction, CancellationToken cancellationToken = default)
    {
        updateAction(_config);
        ConfigChanged?.Invoke(this, _config);
        return Task.CompletedTask;
    }
}
