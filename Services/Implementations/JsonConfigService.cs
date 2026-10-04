using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AuraLauncher.Models;
using AuraLauncher.Services.Interfaces;

namespace AuraLauncher.Services.Implementations;

/// <summary>
/// Реализация сервиса конфигурации с сохранением в %AppData%\Aura\config.json
/// и автоматической миграцией старых настроек.
/// </summary>
public class JsonConfigService : IConfigService
{
    private readonly string _configFilePath;
    private readonly string _legacyConfigFilePath;
    private readonly JsonSerializerOptions _jsonOptions;
    private LauncherConfig _currentConfig = new();
    private readonly SemaphoreSlim _fileLock = new(1, 1);

    public LauncherConfig CurrentConfig => _currentConfig;

    public event EventHandler<LauncherConfig>? ConfigChanged;

    public JsonConfigService(string? customConfigPath = null)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _configFilePath = customConfigPath ?? Path.Combine(appData, "Aura", "config.json");
        _legacyConfigFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "aura_config.json");

        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };
    }

    public async Task<LauncherConfig> LoadConfigAsync(CancellationToken cancellationToken = default)
    {
        await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            bool needSave = false;

            // 1. Если рядом с exe лежит старый aura_config.json, читаем его для миграции
            string? legacyNickname = null;
            string? legacyRepo = null;
            if (File.Exists(_legacyConfigFilePath))
            {
                try
                {
                    var legacyJson = await File.ReadAllTextAsync(_legacyConfigFilePath, cancellationToken).ConfigureAwait(false);
                    using var legacyDoc = JsonDocument.Parse(legacyJson);
                    if (legacyDoc.RootElement.TryGetProperty("Nickname", out var ln)) legacyNickname = ln.GetString();
                    if (legacyDoc.RootElement.TryGetProperty("PackRepo", out var lpr)) legacyRepo = lpr.GetString();
                    else if (legacyDoc.RootElement.TryGetProperty("GitHubRepo", out var lgh)) legacyRepo = lgh.GetString();
                }
                catch { }
            }

            // 2. Проверяем, существует ли целевой файл в %AppData%\Aura\config.json
            if (File.Exists(_configFilePath))
            {
                var json = await File.ReadAllTextAsync(_configFilePath, cancellationToken).ConfigureAwait(false);
                _currentConfig = JsonSerializer.Deserialize<LauncherConfig>(json, _jsonOptions) ?? new LauncherConfig();

                // Проверяем наличие устаревшего GitHubRepo в JSON файле
                try
                {
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("GitHubRepo", out var ghProp) && !doc.RootElement.TryGetProperty("PackRepo", out _))
                    {
                        var legacyGh = ghProp.GetString();
                        if (!string.IsNullOrWhiteSpace(legacyGh) && legacyGh != "qutlawsoasis-debug/Aura")
                        {
                            _currentConfig.PackRepo = legacyGh;
                        }
                        needSave = true;
                    }
                }
                catch { }
            }
            else
            {
                _currentConfig = new LauncherConfig();
                needSave = true;
            }

            // 3. Миграция настроек
            if (legacyNickname != null && _currentConfig.Nickname == "Player" && legacyNickname != "Player")
            {
                _currentConfig.Nickname = legacyNickname;
                needSave = true;
            }

            if (string.IsNullOrWhiteSpace(_currentConfig.PackRepo) || _currentConfig.PackRepo == "qutlawsoasis-debug/Aura")
            {
                _currentConfig.PackRepo = !string.IsNullOrWhiteSpace(legacyRepo) && legacyRepo != "qutlawsoasis-debug/Aura"
                    ? legacyRepo
                    : LauncherConfig.DefaultPackRepo;
                needSave = true;
            }

            // Гарантируем корректность GameDir по умолчанию
            if (string.IsNullOrWhiteSpace(_currentConfig.GameDir))
            {
                _currentConfig.GameDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".aura");
                needSave = true;
            }

            if (needSave)
            {
                await SaveInternalAsync(_currentConfig, cancellationToken).ConfigureAwait(false);
            }

            ConfigChanged?.Invoke(this, _currentConfig);
            return _currentConfig;
        }
        catch
        {
            _currentConfig = new LauncherConfig();
            ConfigChanged?.Invoke(this, _currentConfig);
            return _currentConfig;
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task SaveConfigAsync(LauncherConfig config, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _currentConfig = config;
            await SaveInternalAsync(config, cancellationToken).ConfigureAwait(false);
            ConfigChanged?.Invoke(this, _currentConfig);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task UpdateConfigAsync(Action<LauncherConfig> updateAction, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(updateAction);

        await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            updateAction(_currentConfig);
            await SaveInternalAsync(_currentConfig, cancellationToken).ConfigureAwait(false);
            ConfigChanged?.Invoke(this, _currentConfig);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    private async Task SaveInternalAsync(LauncherConfig config, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_configFilePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(config, _jsonOptions);
        await File.WriteAllTextAsync(_configFilePath, json, cancellationToken).ConfigureAwait(false);
    }
}
