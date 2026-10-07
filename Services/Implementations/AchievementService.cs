using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using AuraLauncher.Models;
using AuraLauncher.Services.Interfaces;

namespace AuraLauncher.Services.Implementations;

public class AchievementService : IAchievementService
{
    private readonly IConfigService _configService;
    private readonly List<AchievementDefinition> _definitions = new();
    private readonly Dictionary<string, AchievementProgress> _progress = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    private readonly string _storagePath;
    private DateTime? _sessionStartTimeUtc;

    public IReadOnlyList<AchievementDefinition> Definitions
    {
        get
        {
            lock (_lock) return _definitions.ToList();
        }
    }

    public IReadOnlyDictionary<string, AchievementProgress> Progress
    {
        get
        {
            lock (_lock) return new Dictionary<string, AchievementProgress>(_progress, StringComparer.OrdinalIgnoreCase);
        }
    }

    public int UnlockedCount
    {
        get
        {
            lock (_lock) return _progress.Values.Count(p => p.Unlocked);
        }
    }

    public int TotalCount
    {
        get
        {
            lock (_lock) return _definitions.Count;
        }
    }

    public (string Id, string Title, DateTime UnlockedAtUtc)? LatestUnlocked
    {
        get
        {
            lock (_lock)
            {
                var latest = _progress.Values
                    .Where(p => p.Unlocked && p.UnlockedAtUtc.HasValue)
                    .OrderByDescending(p => p.UnlockedAtUtc!.Value)
                    .FirstOrDefault();

                if (latest != null)
                {
                    var def = _definitions.FirstOrDefault(d => string.Equals(d.Id, latest.Id, StringComparison.OrdinalIgnoreCase));
                    string title = def?.Title ?? latest.Id;
                    return (latest.Id, title, latest.UnlockedAtUtc!.Value);
                }
                return null;
            }
        }
    }

    public event EventHandler<AchievementDefinition>? AchievementUnlocked;

    public AchievementService(IConfigService configService)
    {
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _storagePath = Path.Combine(appData, ".aura", "achievements.json");
    }

    public void Initialize()
    {
        LoadDefinitions();
        LoadProgress();
        SyncExistingCounters();
    }

    private void LoadDefinitions()
    {
        try
        {
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            string defPath = Path.Combine(appDir, "Data", "achievements.json");
            if (!File.Exists(defPath))
            {
                // Fallback looking up in current project structure
                defPath = Path.Combine(Directory.GetCurrentDirectory(), "Data", "achievements.json");
            }

            if (File.Exists(defPath))
            {
                string json = File.ReadAllText(defPath);
                var list = JsonSerializer.Deserialize<List<AchievementDefinition>>(json);
                if (list != null)
                {
                    lock (_lock)
                    {
                        _definitions.Clear();
                        _definitions.AddRange(list);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            FabricGameLaunchService.LogLauncherEvent($"[ACHIEVEMENTS: ERROR LoadDefinitions] {ex.Message}");
        }
    }

    private void LoadProgress()
    {
        try
        {
            if (File.Exists(_storagePath))
            {
                string json = File.ReadAllText(_storagePath);
                var data = JsonSerializer.Deserialize<AchievementsData>(json);
                if (data?.Progress != null)
                {
                    lock (_lock)
                    {
                        _progress.Clear();
                        foreach (var kv in data.Progress)
                        {
                            _progress[kv.Key] = kv.Value;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            FabricGameLaunchService.LogLauncherEvent($"[ACHIEVEMENTS: ERROR LoadProgress] {ex.Message}");
        }
    }

    private void SaveProgress()
    {
        try
        {
            lock (_lock)
            {
                string dir = Path.GetDirectoryName(_storagePath)!;
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var data = new AchievementsData
                {
                    Progress = new Dictionary<string, AchievementProgress>(_progress, StringComparer.OrdinalIgnoreCase)
                };

                string json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_storagePath, json);
            }
        }
        catch (Exception ex)
        {
            FabricGameLaunchService.LogLauncherEvent($"[ACHIEVEMENTS: ERROR SaveProgress] {ex.Message}");
        }
    }

    public bool IsUnlocked(string id)
    {
        lock (_lock)
        {
            return _progress.TryGetValue(id, out var prog) && prog.Unlocked;
        }
    }

    public void Report(string eventId, double value = 1.0)
    {
        AchievementDefinition? unlockedDef = null;

        lock (_lock)
        {
            switch (eventId.ToLowerInvariant())
            {
                case "game_launch":
                    {
                        _sessionStartTimeUtc = DateTime.UtcNow;

                        // first_launch
                        CheckAndAdvance_NoLock("first_launch", 1, ref unlockedDef);

                        // ten_launches: check from config or report
                        int launches = _configService.CurrentConfig?.TotalGameLaunches ?? 0;
                        if (launches >= 10)
                        {
                            CheckAndAdvance_NoLock("ten_launches", 10, ref unlockedDef);
                        }

                        // night_shift: 03:00 to 05:00 local time
                        int hour = DateTime.Now.Hour;
                        if (hour >= 3 && hour < 5)
                        {
                            CheckAndAdvance_NoLock("night_shift", 1, ref unlockedDef);
                        }
                    }
                    break;

                case "game_exit":
                    {
                        if (_sessionStartTimeUtc.HasValue)
                        {
                            TimeSpan session = DateTime.UtcNow - _sessionStartTimeUtc.Value;
                            if (session.TotalHours >= 5.0)
                            {
                                CheckAndAdvance_NoLock("marathon", 5, ref unlockedDef);
                            }
                            _sessionStartTimeUtc = null;
                        }

                        // hundred_hours
                        long totalSeconds = _configService.CurrentConfig?.TotalPlayTimeSeconds ?? 0;
                        if (totalSeconds >= 100 * 3600)
                        {
                            CheckAndAdvance_NoLock("hundred_hours", 100, ref unlockedDef);
                        }
                    }
                    break;

                case "play_time_check":
                    {
                        long totalSeconds = _configService.CurrentConfig?.TotalPlayTimeSeconds ?? 0;
                        if (totalSeconds >= 100 * 3600)
                        {
                            CheckAndAdvance_NoLock("hundred_hours", 100, ref unlockedDef);
                        }

                        if (_sessionStartTimeUtc.HasValue)
                        {
                            TimeSpan session = DateTime.UtcNow - _sessionStartTimeUtc.Value;
                            if (session.TotalHours >= 5.0)
                            {
                                CheckAndAdvance_NoLock("marathon", 5, ref unlockedDef);
                            }
                        }
                    }
                    break;

                case "lobby_created":
                    CheckAndAdvance_NoLock("first_lobby", 1, ref unlockedDef);
                    break;

                case "lobby_players_count":
                    if (value >= 4)
                    {
                        CheckAndAdvance_NoLock("full_table", 4, ref unlockedDef);
                    }
                    break;

                case "reconnected":
                    CheckAndAdvance_NoLock("reconnected", 1, ref unlockedDef);
                    break;

                case "friends_count":
                    if (value >= 1)
                    {
                        CheckAndAdvance_NoLock("first_friend", 1, ref unlockedDef);
                    }
                    if (value >= 5)
                    {
                        CheckAndAdvance_NoLock("five_friends", 5, ref unlockedDef);
                    }
                    break;

                case "backup_created":
                    {
                        var prog = GetOrCreateProgress_NoLock("ten_backups");
                        prog.CurrentValue += 1;
                        CheckAndAdvance_NoLock("first_backup", 1, ref unlockedDef);
                        if (prog.CurrentValue >= 10 && !prog.Unlocked)
                        {
                            prog.Unlocked = true;
                            prog.UnlockedAtUtc = DateTime.UtcNow;
                            unlockedDef = _definitions.FirstOrDefault(d => d.Id == "ten_backups");
                        }
                    }
                    break;

                case "screenshots_count":
                    if (value >= 10)
                    {
                        CheckAndAdvance_NoLock("photographer", 10, ref unlockedDef);
                    }
                    break;

                case "custom_skin":
                    CheckAndAdvance_NoLock("own_style", 1, ref unlockedDef);
                    break;

                default:
                    // Generic report if matching definition id
                    var def = _definitions.FirstOrDefault(d => string.Equals(d.Id, eventId, StringComparison.OrdinalIgnoreCase));
                    if (def != null)
                    {
                        CheckAndAdvance_NoLock(def.Id, value, ref unlockedDef);
                    }
                    break;
            }

            if (unlockedDef != null)
            {
                SaveProgress();
            }
        }

        if (unlockedDef != null)
        {
            AchievementUnlocked?.Invoke(this, unlockedDef);
        }
    }

    private void SyncExistingCounters()
    {
        lock (_lock)
        {
            var config = _configService.CurrentConfig;
            if (config != null)
            {
                AchievementDefinition? unlockedDef = null;

                if (config.TotalGameLaunches >= 1)
                {
                    CheckAndAdvance_NoLock("first_launch", 1, ref unlockedDef);
                }
                if (config.TotalGameLaunches >= 10)
                {
                    CheckAndAdvance_NoLock("ten_launches", 10, ref unlockedDef);
                }
                if (config.TotalPlayTimeSeconds >= 100 * 3600)
                {
                    CheckAndAdvance_NoLock("hundred_hours", 100, ref unlockedDef);
                }
                if (!string.IsNullOrWhiteSpace(config.SkinPath) && File.Exists(config.SkinPath))
                {
                    CheckAndAdvance_NoLock("own_style", 1, ref unlockedDef);
                }

                if (unlockedDef != null)
                {
                    SaveProgress();
                }
            }
        }
    }

    private AchievementProgress GetOrCreateProgress_NoLock(string id)
    {
        if (!_progress.TryGetValue(id, out var prog))
        {
            prog = new AchievementProgress { Id = id };
            _progress[id] = prog;
        }
        return prog;
    }

    private void CheckAndAdvance_NoLock(string id, double targetValue, ref AchievementDefinition? unlockedDef)
    {
        var prog = GetOrCreateProgress_NoLock(id);
        if (prog.Unlocked) return;

        var def = _definitions.FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));
        int target = def?.Target ?? (int)targetValue;

        prog.CurrentValue = Math.Max(prog.CurrentValue, targetValue);
        if (prog.CurrentValue >= target)
        {
            prog.Unlocked = true;
            prog.UnlockedAtUtc = DateTime.UtcNow;
            unlockedDef = def;
        }
    }

#if DEBUG
    public void DebugUnlock(string id)
    {
        AchievementDefinition? def;
        lock (_lock)
        {
            def = _definitions.FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));
            if (def == null)
            {
                def = new AchievementDefinition { Id = id, Title = id, Description = "Debug achievement", Target = 1 };
                _definitions.Add(def);
            }

            var prog = GetOrCreateProgress_NoLock(id);
            prog.CurrentValue = def.Target;
            prog.Unlocked = true;
            prog.UnlockedAtUtc = DateTime.UtcNow;
            SaveProgress();
        }

        AchievementUnlocked?.Invoke(this, def);
    }
#endif
}
