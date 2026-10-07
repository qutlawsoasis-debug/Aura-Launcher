using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using AuraLauncher.Models;
using AuraLauncher.Services.Interfaces;

namespace AuraLauncher.Services.Implementations;

/// <summary>
/// Реализация сервиса конфигурации с сохранением в %AppData%\.aura\config.json
/// и автоматической миграцией старых настроек.
/// </summary>
public class JsonConfigService : IConfigService
{
    private readonly string _configFilePath;
    private readonly string _oldAuraConfigFilePath;
    private readonly string _appDirConfigFilePath;
    private readonly string _velopackRootConfigFilePath;
    private readonly string _legacyConfigFilePath;
    private readonly bool _isCustomPath;
    private readonly JsonSerializerOptions _jsonOptions;
    private LauncherConfig _currentConfig = new();
    private readonly SemaphoreSlim _fileLock = new(1, 1);

    public LauncherConfig CurrentConfig => _currentConfig;

    public event EventHandler<LauncherConfig>? ConfigChanged;

    public JsonConfigService(string? customConfigPath = null)
    {
        string? profileDir = Environment.GetEnvironmentVariable("AURA_PROFILE_DIR");
        if (string.IsNullOrWhiteSpace(profileDir) && Program.StartupArgs != null)
        {
            for (int i = 0; i < Program.StartupArgs.Length - 1; i++)
            {
                if (string.Equals(Program.StartupArgs[i], "--profile", StringComparison.OrdinalIgnoreCase))
                {
                    profileDir = Program.StartupArgs[i + 1];
                    break;
                }
            }
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var baseDir = !string.IsNullOrWhiteSpace(profileDir)
            ? (Path.IsPathRooted(profileDir) ? profileDir : Path.Combine(appData, ".aura", "profiles", profileDir))
            : Path.Combine(appData, ".aura");

        var oldAuraBaseDir = !string.IsNullOrWhiteSpace(profileDir)
            ? (Path.IsPathRooted(profileDir) ? profileDir : Path.Combine(appData, "Aura", "profiles", profileDir))
            : Path.Combine(appData, "Aura");

        _isCustomPath = !string.IsNullOrWhiteSpace(customConfigPath);
        _configFilePath = customConfigPath ?? Path.Combine(baseDir, "config.json");
        _oldAuraConfigFilePath = Path.Combine(oldAuraBaseDir, "config.json");
        _appDirConfigFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
        _velopackRootConfigFilePath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "config.json"));
        _legacyConfigFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "aura_config.json");

        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: true) }
        };

        LoadConfigSync();
    }

    private bool MigrateFromOldLocationsIfNeeded()
    {
        if (_isCustomPath || File.Exists(_configFilePath))
            return false;

        string[] candidates =
        {
            _oldAuraConfigFilePath,
            _appDirConfigFilePath,
            _velopackRootConfigFilePath
        };

        foreach (var candidate in candidates)
        {
            try
            {
                if (File.Exists(candidate))
                {
                    var dir = Path.GetDirectoryName(_configFilePath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }
                    File.Copy(candidate, _configFilePath, overwrite: true);
                    FabricGameLaunchService.LogLauncherEvent($"[CONFIG: MIGRATION] Перенесён конфиг из '{candidate}' в '{_configFilePath}'");
                    return true;
                }
            }
            catch (Exception ex)
            {
                FabricGameLaunchService.LogLauncherEvent($"[CONFIG: MIGRATION ERROR] Ошибка переноса из '{candidate}': {ex.Message}");
            }
        }

        return false;
    }

    public LauncherConfig LoadConfigSync()
    {
        _fileLock.Wait();
        try
        {
            return LoadCoreSync();
        }
        finally
        {
            _fileLock.Release();
        }
    }

    private LauncherConfig LoadCoreSync()
    {
        bool needSave = false;

        bool migrated = MigrateFromOldLocationsIfNeeded();

        // 1. Если рядом с exe лежит старый aura_config.json, читаем его для миграции
        string? legacyNickname = null;
        string? legacyRepo = null;
        if (File.Exists(_legacyConfigFilePath))
        {
            try
            {
                var legacyJson = File.ReadAllText(_legacyConfigFilePath);
                using var legacyDoc = JsonDocument.Parse(legacyJson);
                if (legacyDoc.RootElement.TryGetProperty("Nickname", out var ln)) legacyNickname = ln.GetString();
                if (legacyDoc.RootElement.TryGetProperty("PackRepo", out var lpr)) legacyRepo = lpr.GetString();
                else if (legacyDoc.RootElement.TryGetProperty("GitHubRepo", out var lgh)) legacyRepo = lgh.GetString();
            }
            catch (Exception ex)
            {
                FabricGameLaunchService.LogLauncherEvent($"[CONFIG: LEGACY WARN] Не удалось прочитать '{_legacyConfigFilePath}': {ex.Message}");
            }
        }

        // 2. Проверяем, существует ли целевой файл в %AppData%\.aura\config.json
        if (File.Exists(_configFilePath))
        {
            try
            {
                var json = File.ReadAllText(_configFilePath);
                var parsed = JsonSerializer.Deserialize<LauncherConfig>(json, _jsonOptions);
                if (parsed == null)
                {
                    throw new JsonException("Deserialized LauncherConfig was null.");
                }
                _currentConfig = parsed;

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
            catch (Exception ex)
            {
                FabricGameLaunchService.LogLauncherEvent($"[CONFIG: ERROR] Файл конфигурации '{_configFilePath}' повреждён или не читается: {ex}");
                BackupCorruptedConfig();
                _currentConfig = new LauncherConfig();
                needSave = true;
            }
        }
        else
        {
            _currentConfig = new LauncherConfig();
            needSave = true;
        }

        // 3. Миграция и нормализация настроек
        if (legacyNickname != null && _currentConfig.Nickname == "Player" && legacyNickname != "Player")
        {
            _currentConfig.Nickname = legacyNickname;
            needSave = true;
        }

        if (migrated && _currentConfig.Nickname == "Player")
        {
            try
            {
                var dotAura = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".aura");
                var argsFile = Path.Combine(dotAura, "argsFile.txt");
                if (File.Exists(argsFile))
                {
                    var lines = File.ReadAllLines(argsFile);
                    for (int i = 0; i < lines.Length - 1; i++)
                    {
                        if (lines[i].Trim().Equals("--username", StringComparison.OrdinalIgnoreCase))
                        {
                            var candidateNick = lines[i + 1].Trim().Trim('"');
                            if (!string.IsNullOrWhiteSpace(candidateNick) && candidateNick != "Player" && candidateNick != "TestPlayer")
                            {
                                _currentConfig.Nickname = candidateNick;
                                needSave = true;
                            }
                            break;
                        }
                    }
                }

                if (string.IsNullOrWhiteSpace(_currentConfig.SkinPath) && _currentConfig.Nickname != "Player")
                {
                    var localSkin = Path.Combine(dotAura, "CustomSkinLoader", "LocalSkin", "skins", $"{_currentConfig.Nickname}.png");
                    if (File.Exists(localSkin))
                    {
                        _currentConfig.SkinPath = localSkin;
                        _currentConfig.SkinOriginalName = $"{_currentConfig.Nickname}.png";
                        needSave = true;
                    }
                }
            }
            catch { }
        }

        if (string.IsNullOrWhiteSpace(_currentConfig.PackRepo) || _currentConfig.PackRepo == "qutlawsoasis-debug/Aura")
        {
            _currentConfig.PackRepo = !string.IsNullOrWhiteSpace(legacyRepo) && legacyRepo != "qutlawsoasis-debug/Aura"
                ? legacyRepo
                : LauncherConfig.DefaultPackRepo;
            needSave = true;
        }

        if (string.IsNullOrWhiteSpace(_currentConfig.GameDir))
        {
            _currentConfig.GameDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".aura");
            needSave = true;
        }

        if (string.IsNullOrWhiteSpace(_currentConfig.DiscordAppId))
        {
            _currentConfig.DiscordAppId = "1556968494673690674";
            needSave = true;
        }

        if (string.IsNullOrWhiteSpace(_currentConfig.StartMode))
        {
            _currentConfig.StartMode = "Maximized";
            needSave = true;
        }

        if (!_isCustomPath && !string.IsNullOrWhiteSpace(_currentConfig.SkinPath) && File.Exists(_currentConfig.SkinPath))
        {
            try
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var skinsDir = Path.Combine(appData, ".aura", "skins");
                Directory.CreateDirectory(skinsDir);
                var fileName = Path.GetFileName(_currentConfig.SkinPath);
                if (!string.IsNullOrWhiteSpace(fileName))
                {
                    var targetSkinPath = Path.Combine(skinsDir, fileName);
                    if (!string.Equals(Path.GetFullPath(_currentConfig.SkinPath), Path.GetFullPath(targetSkinPath), StringComparison.OrdinalIgnoreCase))
                    {
                        File.Copy(_currentConfig.SkinPath, targetSkinPath, overwrite: true);
                        _currentConfig.SkinPath = targetSkinPath;
                        needSave = true;
                    }
                }
            }
            catch { }
        }

        if (needSave)
        {
            try
            {
                SaveInternalSync(_currentConfig);
            }
            catch { }
        }

        ConfigChanged?.Invoke(this, _currentConfig);
        return _currentConfig;
    }

    public async Task<LauncherConfig> LoadConfigAsync(CancellationToken cancellationToken = default)
    {
        await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return LoadCoreSync();
        }
        finally
        {
            _fileLock.Release();
        }
    }

    private void BackupCorruptedConfig()
    {
        try
        {
            if (File.Exists(_configFilePath))
            {
                var bakPath = _configFilePath + ".bak";
                File.Copy(_configFilePath, bakPath, overwrite: true);
                FabricGameLaunchService.LogLauncherEvent($"[CONFIG: BACKUP] Повреждённый конфиг скопирован в '{bakPath}'");
            }
        }
        catch (Exception bakEx)
        {
            FabricGameLaunchService.LogLauncherEvent($"[CONFIG: BACKUP ERROR] Не удалось создать копию '{_configFilePath}.bak': {bakEx.Message}");
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

    private void SaveInternalSync(LauncherConfig config)
    {
        try
        {
            var directory = Path.GetDirectoryName(_configFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(config, _jsonOptions);
            var tmpPath = _configFilePath + ".tmp";
            File.WriteAllText(tmpPath, json);
            File.Move(tmpPath, _configFilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            FabricGameLaunchService.LogLauncherEvent($"[CONFIG: SAVE ERROR] Ошибка записи '{_configFilePath}': {ex}");
            throw;
        }
    }

    private async Task SaveInternalAsync(LauncherConfig config, CancellationToken cancellationToken)
    {
        try
        {
            var directory = Path.GetDirectoryName(_configFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(config, _jsonOptions);
            var tmpPath = _configFilePath + ".tmp";
            await File.WriteAllTextAsync(tmpPath, json, cancellationToken).ConfigureAwait(false);
            File.Move(tmpPath, _configFilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            FabricGameLaunchService.LogLauncherEvent($"[CONFIG: SAVE ERROR] Ошибка записи '{_configFilePath}': {ex}");
            throw;
        }
    }
}
