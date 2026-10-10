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
    private readonly string? _definitionsPath;
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
            List<AchievementDefinition> unlockedList;
            Dictionary<string, AchievementProgress> snapshot;
            lock (_lock)
            {
                unlockedList = SyncExistingCounters_NoLock();
                snapshot = new Dictionary<string, AchievementProgress>(_progress, StringComparer.OrdinalIgnoreCase);
            }

            foreach (var def in unlockedList)
            {
                AchievementUnlocked?.Invoke(this, def);
            }

            return snapshot;
        }
    }

    public int UnlockedCount
    {
        get
        {
            lock (_lock)
            {
                SyncExistingCounters_NoLock();
                return _progress.Values.Count(p => p.Unlocked);
            }
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
            List<AchievementDefinition> unlockedList;
            (string Id, string Title, DateTime UnlockedAtUtc)? result = null;
            lock (_lock)
            {
                unlockedList = SyncExistingCounters_NoLock();
                var latest = _progress.Values
                    .Where(p => p.Unlocked && p.UnlockedAtUtc.HasValue)
                    .OrderByDescending(p => p.UnlockedAtUtc!.Value)
                    .FirstOrDefault();

                if (latest != null)
                {
                    var def = _definitions.FirstOrDefault(d => string.Equals(d.Id, latest.Id, StringComparison.OrdinalIgnoreCase));
                    string title = def?.Title ?? latest.Id;
                    result = (latest.Id, title, latest.UnlockedAtUtc!.Value);
                }
            }

            foreach (var def in unlockedList)
            {
                AchievementUnlocked?.Invoke(this, def);
            }

            return result;
        }
    }

    public event EventHandler<AchievementDefinition>? AchievementUnlocked;

    public AchievementService(IConfigService configService, string? storagePath = null, string? definitionsPath = null)
    {
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _storagePath = storagePath ?? Path.Combine(appData, ".aura", "achievements.json");
        _definitionsPath = definitionsPath;
        _configService.ConfigChanged += (s, cfg) => SyncExistingCounters();
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
            string? defPath = _definitionsPath;
            if (string.IsNullOrWhiteSpace(defPath) || !File.Exists(defPath))
            {
                string appDir = AppDomain.CurrentDomain.BaseDirectory;
                var candidates = new[]
                {
                    Path.Combine(appDir, "Data", "achievements.json"),
                    Path.Combine(Directory.GetCurrentDirectory(), "Data", "achievements.json"),
                    Path.Combine(appDir, "..", "..", "..", "..", "Data", "achievements.json"),
                    Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", "Data", "achievements.json"),
                    Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "Data", "achievements.json")
                };

                defPath = candidates.FirstOrDefault(File.Exists);
            }

            if (!string.IsNullOrWhiteSpace(defPath) && File.Exists(defPath))
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
                string tmpPath = _storagePath + ".tmp";
                File.WriteAllText(tmpPath, json);
                File.Move(tmpPath, _storagePath, overwrite: true);
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
        var unlockedList = new List<AchievementDefinition>();

        lock (_lock)
        {
            bool changed = false;

            switch (eventId.ToLowerInvariant())
            {
                case "game_launch":
                    {
                        _sessionStartTimeUtc = DateTime.UtcNow;

                        int launches = _configService.CurrentConfig?.TotalGameLaunches ?? 0;
                        changed |= CheckAndAdvance_NoLock("first_launch", Math.Max(1, launches), unlockedList);
                        changed |= CheckAndAdvance_NoLock("ten_launches", launches, unlockedList);

                        // night_shift: 03:00 to 05:00 local time
                        int hour = DateTime.Now.Hour;
                        if (hour >= 3 && hour < 5)
                        {
                            changed |= CheckAndAdvance_NoLock("night_shift", 1, unlockedList);
                        }
                    }
                    break;

                case "game_exit":
                    {
                        if (_sessionStartTimeUtc.HasValue)
                        {
                            TimeSpan session = DateTime.UtcNow - _sessionStartTimeUtc.Value;
                            changed |= CheckAndAdvance_NoLock("marathon", session.TotalHours, unlockedList);
                            _sessionStartTimeUtc = null;
                        }

                        long totalSeconds = _configService.CurrentConfig?.TotalPlayTimeSeconds ?? 0;
                        if (totalSeconds > 0)
                        {
                            double hours = (double)totalSeconds / 3600.0;
                            changed |= CheckAndAdvance_NoLock("hundred_hours", hours, unlockedList);
                        }
                    }
                    break;

                case "play_time_check":
                    {
                        long totalSeconds = _configService.CurrentConfig?.TotalPlayTimeSeconds ?? 0;
                        if (totalSeconds > 0)
                        {
                            double hours = (double)totalSeconds / 3600.0;
                            changed |= CheckAndAdvance_NoLock("hundred_hours", hours, unlockedList);
                        }

                        if (_sessionStartTimeUtc.HasValue)
                        {
                            TimeSpan session = DateTime.UtcNow - _sessionStartTimeUtc.Value;
                            changed |= CheckAndAdvance_NoLock("marathon", session.TotalHours, unlockedList);
                        }
                    }
                    break;

                case "lobby_created":
                    changed |= CheckAndAdvance_NoLock("first_lobby", 1, unlockedList);
                    break;

                case "lobby_players_count":
                    changed |= CheckAndAdvance_NoLock("full_table", value, unlockedList);
                    break;

                case "reconnected":
                    changed |= CheckAndAdvance_NoLock("reconnected", 1, unlockedList);
                    break;

                case "friends_count":
                    changed |= CheckAndAdvance_NoLock("first_friend", value, unlockedList);
                    changed |= CheckAndAdvance_NoLock("five_friends", value, unlockedList);
                    break;

                case "backup_created":
                    {
                        var prog = GetOrCreateProgress_NoLock("ten_backups");
                        prog.CurrentValue += 1;
                        changed = true;
                        changed |= CheckAndAdvance_NoLock("first_backup", 1, unlockedList);
                        if (prog.CurrentValue >= 10 && !prog.Unlocked)
                        {
                            prog.Unlocked = true;
                            prog.UnlockedAtUtc = DateTime.UtcNow;
                            var tenDef = _definitions.FirstOrDefault(d => d.Id == "ten_backups");
                            if (tenDef != null && !unlockedList.Any(d => string.Equals(d.Id, tenDef.Id, StringComparison.OrdinalIgnoreCase)))
                            {
                                unlockedList.Add(tenDef);
                            }
                        }
                    }
                    break;

                case "screenshots_count":
                    changed |= CheckAndAdvance_NoLock("photographer", value, unlockedList);
                    break;

                case "custom_skin":
                    changed |= CheckAndAdvance_NoLock("own_style", 1, unlockedList);
                    break;

                default:
                    // Generic report if matching definition id
                    var def = _definitions.FirstOrDefault(d => string.Equals(d.Id, eventId, StringComparison.OrdinalIgnoreCase));
                    if (def != null)
                    {
                        changed |= CheckAndAdvance_NoLock(def.Id, value, unlockedList);
                    }
                    break;
            }

            if (changed)
            {
                SaveProgress();
            }
        }

        foreach (var def in unlockedList)
        {
            AchievementUnlocked?.Invoke(this, def);
        }
    }

    private void SyncExistingCounters()
    {
        List<AchievementDefinition> unlockedList;
        lock (_lock)
        {
            unlockedList = SyncExistingCounters_NoLock();
        }

        foreach (var def in unlockedList)
        {
            AchievementUnlocked?.Invoke(this, def);
        }
    }

    private List<AchievementDefinition> SyncExistingCounters_NoLock()
    {
        var unlockedList = new List<AchievementDefinition>();
        var config = _configService.CurrentConfig;
        if (config == null) return unlockedList;

        bool changed = false;

        if (config.TotalGameLaunches > 0)
        {
            changed |= CheckAndAdvance_NoLock("first_launch", config.TotalGameLaunches, unlockedList);
            changed |= CheckAndAdvance_NoLock("ten_launches", config.TotalGameLaunches, unlockedList);
        }

        if (config.TotalPlayTimeSeconds > 0)
        {
            double hours = (double)config.TotalPlayTimeSeconds / 3600.0;
            changed |= CheckAndAdvance_NoLock("hundred_hours", hours, unlockedList);
        }

        if (!string.IsNullOrWhiteSpace(config.SkinPath) && File.Exists(config.SkinPath))
        {
            changed |= CheckAndAdvance_NoLock("own_style", 1, unlockedList);
        }

        if (changed)
        {
            SaveProgress();
        }

        return unlockedList;
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

    private bool CheckAndAdvance_NoLock(string id, double currentValue, List<AchievementDefinition> unlockedList)
    {
        var prog = GetOrCreateProgress_NoLock(id);
        if (prog.Unlocked) return false;

        var def = _definitions.FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));
        if (def == null) return false;

        int target = def.Target;

        bool changed = false;
        if (currentValue > prog.CurrentValue)
        {
            prog.CurrentValue = currentValue;
            changed = true;
        }

        if (prog.CurrentValue >= target)
        {
            prog.Unlocked = true;
            prog.UnlockedAtUtc = DateTime.UtcNow;
            if (!unlockedList.Any(d => string.Equals(d.Id, def.Id, StringComparison.OrdinalIgnoreCase)))
            {
                unlockedList.Add(def);
            }
            changed = true;
        }

        return changed;
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
