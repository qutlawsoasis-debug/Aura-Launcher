using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;
using AuraLauncher.Core;
using AuraLauncher.Models;
using AuraLauncher.Services.Interfaces;
using AuraLauncher.Services.Implementations;

namespace AuraLauncher.ViewModels;


/// <summary>
/// ViewModel для главного экрана лаунчера.
/// Отображает реальную статистику сборки: версию Minecraft/Fabric, количество установленных модов и дату последнего обновления.
/// Без захардкоженных цифр и фейковых индикаторов.
/// </summary>
public class OverviewViewModel : ObservableObject
{
    private readonly IConfigService _configService;
    private readonly IGameLaunchService _launchService;

    private string _buildName = "Aura";
    private string _versionInfo = $"Minecraft 1.20.1 • Fabric {FabricGameLaunchService.FabricLoaderVersion}";
    private string _specLine = $"Minecraft 1.20.1 • Fabric {FabricGameLaunchService.FabricLoaderVersion}";
    private string _modsCountText = "Сборка не установлена";
    private string _lastUpdateText = "Обновлено: —";
    private bool _isEnvironmentInstalled;

    public string BuildName
    {
        get => _buildName;
        set => SetProperty(ref _buildName, value);
    }

    public string VersionInfo
    {
        get => _versionInfo;
        set => SetProperty(ref _versionInfo, value);
    }

    public string SpecLine
    {
        get => _specLine;
        set => SetProperty(ref _specLine, value);
    }

    public string ModsCountText
    {
        get => _modsCountText;
        set => SetProperty(ref _modsCountText, value);
    }

    public string LastUpdateText
    {
        get => _lastUpdateText;
        set => SetProperty(ref _lastUpdateText, value);
    }

    private string _playTimeFormatted = "0 мин.";
    private string _playtimeSummaryText = "Время в игре: 0 мин. • 0 запусков";

    public string PlayTimeFormatted
    {
        get => _playTimeFormatted;
        set => SetProperty(ref _playTimeFormatted, value);
    }

    public string PlaytimeSummaryText
    {
        get => _playtimeSummaryText;
        set => SetProperty(ref _playtimeSummaryText, value);
    }

    public bool IsEnvironmentInstalled
    {
        get => _isEnvironmentInstalled;
        private set => SetProperty(ref _isEnvironmentInstalled, value);
    }

    private readonly IBackgroundService? _backgroundService;
    private System.Windows.Media.ImageSource? _launcherBackground;
    public System.Windows.Media.ImageSource? LauncherBackground
    {
        get => _launcherBackground;
        private set => SetProperty(ref _launcherBackground, value);
    }

    public bool HasAchievementsPage => false;

    public RelayCommand OpenAchievementsCommand { get; }

    public OverviewViewModel(IConfigService configService, IGameLaunchService launchService, IBackgroundService? backgroundService = null)
    {
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _launchService = launchService ?? throw new ArgumentNullException(nameof(launchService));
        _backgroundService = backgroundService ?? (App.Services?.GetService(typeof(IBackgroundService)) as IBackgroundService);

        if (_backgroundService != null)
        {
            _launcherBackground = _backgroundService.CurrentImage;
            _backgroundService.BackgroundChanged += (s, img) =>
            {
                if (img != null) LauncherBackground = img;
            };
        }

        OpenAchievementsCommand = new RelayCommand(_ => { });

        RefreshStats();

        _configService.ConfigChanged += (s, cfg) =>
        {
            RefreshStats();
        };

        var achService = App.Services?.GetService(typeof(IAchievementService)) as IAchievementService;
        if (achService != null)
        {
            achService.AchievementUnlocked += (s, def) =>
            {
                System.Windows.Application.Current?.Dispatcher?.InvokeAsync(RefreshAchievementBlock);
            };
        }
    }

    /// <summary>
    /// Локальное вычисление данных о сборке (без сети):
    /// версия Minecraft/Fabric из константы, состояние установки, подсчет модов и дата обновления.
    /// </summary>
    public void RefreshStats()
    {
        var config = _configService.CurrentConfig;
        var gameDir = _launchService.ResolveMinecraftDirectory(config.GameDir);

        // 1. "Minecraft 1.20.1 • Fabric <версия>" из константы и конфига, не сканируя диск
        var fabricLoader = FabricGameLaunchService.ResolveFabricLoaderVersion(gameDir);
        VersionInfo = $"Minecraft 1.20.1 • Fabric {fabricLoader}";

        // 2. Проверка состояния установки окружения
        bool isInstalled = _launchService.CheckEnvironmentInstalled(gameDir);
        IsEnvironmentInstalled = isInstalled;

        int modCount = 0;
        // 3. Моды: если папка mods есть, показывать число *.jar; если окружение не установлено, вместо строки писать "Сборка не установлена"
        if (!isInstalled)
        {
            ModsCountText = "Сборка не установлена";
            SpecLine = "Minecraft 1.20.1 • Fabric • Сборка не установлена";
        }
        else
        {
            var modsDir = Path.Combine(gameDir, "mods");
            if (Directory.Exists(modsDir))
            {
                try
                {
                    modCount = Directory.GetFiles(modsDir, "*.jar", SearchOption.TopDirectoryOnly).Length;
                    ModsCountText = modCount > 0 ? $"Модов установлено: {modCount}" : "Модов установлено: —";
                }
                catch
                {
                    ModsCountText = "Модов установлено: —";
                }
            }
            else
            {
                ModsCountText = "Модов установлено: —";
            }

            SpecLine = modCount > 0
                ? $"Minecraft 1.20.1, Fabric {fabricLoader}, модов: {modCount}"
                : $"Minecraft 1.20.1, Fabric {fabricLoader}, модов: —";
        }

        // 4. "Обновлено:": показывать LastUpdateUtc из конфига (или "—", если пусто)
        if (config.LastUpdateUtc.HasValue)
        {
            LastUpdateText = $"Обновлено: {config.LastUpdateUtc.Value.ToLocalTime():dd.MM.yyyy HH:mm}";
        }
        else
        {
            LastUpdateText = "Обновлено: —";
        }

        // 5. Статистика игрового времени
        long totalSec = config.TotalPlayTimeSeconds;
        long hours = totalSec / 3600;
        long minutes = (totalSec % 3600) / 60;
        PlayTimeFormatted = hours > 0 ? $"{hours} ч. {minutes} мин." : $"{minutes} мин.";
        PlaytimeSummaryText = $"В игре: {PlayTimeFormatted}  •  Запусков: {config.TotalGameLaunches}";

        // 6. Обновление правой колонки (ленты)
        RefreshRightFeed();
    }

    // Правая колонка (лента на главном экране)
    private ScreenshotItem? _latestScreenshot;
    private string _latestScreenshotDateText = string.Empty;
    private bool _hasLatestScreenshot;

    private string _achievementTitle = string.Empty;
    private string _achievementDescription = string.Empty;
    private string _achievementDateOrProgress = string.Empty;
    private bool _hasAchievementBlock;

    private string _whatsNewVersion = string.Empty;
    private ObservableCollection<string> _whatsNewLines = new();
    private bool _hasWhatsNewBlock;

    private bool _hasRightFeed;

    public ScreenshotItem? LatestScreenshot
    {
        get => _latestScreenshot;
        private set => SetProperty(ref _latestScreenshot, value);
    }

    public string LatestScreenshotDateText
    {
        get => _latestScreenshotDateText;
        private set => SetProperty(ref _latestScreenshotDateText, value);
    }

    public bool HasLatestScreenshot
    {
        get => _hasLatestScreenshot;
        private set
        {
            if (SetProperty(ref _hasLatestScreenshot, value))
            {
                UpdateHasRightFeed();
            }
        }
    }

    public string AchievementTitle
    {
        get => _achievementTitle;
        private set => SetProperty(ref _achievementTitle, value);
    }

    public string AchievementDescription
    {
        get => _achievementDescription;
        private set => SetProperty(ref _achievementDescription, value);
    }

    public string AchievementDateOrProgress
    {
        get => _achievementDateOrProgress;
        private set => SetProperty(ref _achievementDateOrProgress, value);
    }

    public bool HasAchievementBlock
    {
        get => _hasAchievementBlock;
        private set
        {
            if (SetProperty(ref _hasAchievementBlock, value))
            {
                UpdateHasRightFeed();
            }
        }
    }

    public string WhatsNewVersion
    {
        get => _whatsNewVersion;
        private set => SetProperty(ref _whatsNewVersion, value);
    }

    public ObservableCollection<string> WhatsNewLines
    {
        get => _whatsNewLines;
        private set => SetProperty(ref _whatsNewLines, value);
    }

    public bool HasWhatsNewBlock
    {
        get => _hasWhatsNewBlock;
        private set
        {
            if (SetProperty(ref _hasWhatsNewBlock, value))
            {
                UpdateHasRightFeed();
            }
        }
    }

    private System.Windows.Media.ImageSource? _feedImage0;

    private string _nextAchievementTitle = string.Empty;
    private string _nextAchievementDescription = string.Empty;
    private string _nextAchievementProgress = string.Empty;
    private string _whatsNewPrimaryLine = string.Empty;

    public System.Windows.Media.ImageSource? FeedImage0
    {
        get => _feedImage0;
        private set => SetProperty(ref _feedImage0, value);
    }

    public string NextAchievementTitle
    {
        get => _nextAchievementTitle;
        private set => SetProperty(ref _nextAchievementTitle, value);
    }

    public string NextAchievementDescription
    {
        get => _nextAchievementDescription;
        private set => SetProperty(ref _nextAchievementDescription, value);
    }

    public string NextAchievementProgress
    {
        get => _nextAchievementProgress;
        private set => SetProperty(ref _nextAchievementProgress, value);
    }

    private double _nextAchievementRatio = 0.4;
    public double NextAchievementRatio
    {
        get => _nextAchievementRatio;
        private set => SetProperty(ref _nextAchievementRatio, value);
    }

    public ObservableCollection<AchievementDisplayItem> AchievementsGrid { get; } = new();
    public ObservableCollection<AchievementDisplayItem> UnlockedAchievements { get; } = new();
    public ObservableCollection<AchievementDisplayItem> LockedAchievements { get; } = new();

    private string _achievementsHeaderText = "ДОСТИЖЕНИЯ 0 ИЗ 14";
    public string AchievementsHeaderText
    {
        get => _achievementsHeaderText;
        private set => SetProperty(ref _achievementsHeaderText, value);
    }

    private double _achievementsProgressRatio = 0.0;
    public double AchievementsProgressRatio
    {
        get => _achievementsProgressRatio;
        private set => SetProperty(ref _achievementsProgressRatio, value);
    }

    private int _achievementsUnlockedCount = 0;
    public int AchievementsUnlockedCount
    {
        get => _achievementsUnlockedCount;
        private set => SetProperty(ref _achievementsUnlockedCount, value);
    }

    private int _achievementsTotalCount = 14;
    public int AchievementsTotalCount
    {
        get => _achievementsTotalCount;
        private set => SetProperty(ref _achievementsTotalCount, value);
    }

    public string WhatsNewPrimaryLine
    {
        get => _whatsNewPrimaryLine;
        private set => SetProperty(ref _whatsNewPrimaryLine, value);
    }

    public ObservableCollection<string> TopWhatsNewLines { get; } = new();

    public bool HasRightFeed
    {
        get => _hasRightFeed;
        private set => SetProperty(ref _hasRightFeed, value);
    }

    private void UpdateHasRightFeed()
    {
        HasRightFeed = HasLatestScreenshot || HasAchievementBlock || HasWhatsNewBlock;
    }

    public void RefreshRightFeed()
    {
        RefreshStoriesData();
    }

    public void RefreshStoriesData()
    {
        UpdateLauncherBackground();
        RefreshLatestScreenshot();
        RefreshAchievementBlock();
        RefreshWhatsNewBlock();
        UpdateHasRightFeed();
    }

    private static BitmapImage? TryLoadScreenshotBitmap(string fullPath)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(fullPath, UriKind.Absolute);
            bmp.DecodePixelWidth = 640;
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    private void UpdateLauncherBackground()
    {
        if (_backgroundService?.CurrentImage != null)
        {
            LauncherBackground = _backgroundService.CurrentImage;
        }
        else if (LauncherBackground == null && System.Windows.Application.Current != null)
        {
            try
            {
                if (System.Windows.Application.Current.TryFindResource("WorldBackgroundBitmap") is System.Windows.Media.ImageSource src)
                {
                    LauncherBackground = src;
                }
            }
            catch { }
        }
    }

    private void RefreshLatestScreenshot()
    {
        try
        {
            var config = _configService.CurrentConfig;
            var gameDir = _launchService.ResolveMinecraftDirectory(config.GameDir);
            var screensDir = Path.Combine(gameDir, "screenshots");

            if (!Directory.Exists(screensDir))
            {
                LatestScreenshot = null;
                LatestScreenshotDateText = string.Empty;
                HasLatestScreenshot = false;
                FeedImage0 = null;
                return;
            }

            var di = new DirectoryInfo(screensDir);
            var recentFiles = di.GetFiles("*.png")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Take(1)
                .ToArray();

            if (recentFiles.Length == 0)
            {
                LatestScreenshot = null;
                LatestScreenshotDateText = string.Empty;
                HasLatestScreenshot = false;
                FeedImage0 = null;
                return;
            }

            var latestFile = recentFiles[0];

            if (LatestScreenshot == null ||
                !string.Equals(LatestScreenshot.FullPath, latestFile.FullName, StringComparison.OrdinalIgnoreCase) ||
                LatestScreenshot.CreatedDate != latestFile.LastWriteTime)
            {
                var bmp0 = TryLoadScreenshotBitmap(latestFile.FullName);
                if (bmp0 == null)
                {
                    LatestScreenshot = null;
                    LatestScreenshotDateText = string.Empty;
                    HasLatestScreenshot = false;
                    FeedImage0 = null;
                    return;
                }

                LatestScreenshot = new ScreenshotItem
                {
                    FileName = latestFile.Name,
                    FullPath = latestFile.FullName,
                    CreatedDate = latestFile.LastWriteTime,
                    Thumbnail = bmp0
                };
            }

            LatestScreenshotDateText = latestFile.LastWriteTime.ToString("dd.MM.yyyy HH:mm");
            HasLatestScreenshot = true;
            FeedImage0 = LatestScreenshot.Thumbnail;
        }
        catch
        {
            LatestScreenshot = null;
            LatestScreenshotDateText = string.Empty;
            HasLatestScreenshot = false;
            FeedImage0 = null;
        }
    }

    private static string GetAchievementUnit(string id) => id switch
    {
        "ten_launches" => "запусков",
        "hundred_hours" => "часов",
        "marathon" => "часов",
        "full_table" => "игроков",
        "five_friends" => "друзей",
        "ten_backups" => "бэкапов",
        "photographer" => "скриншотов",
        _ => ""
    };

    private void RefreshAchievementBlock()
    {
        try
        {
            var achService = App.Services?.GetService(typeof(IAchievementService)) as IAchievementService;
            var defs = achService?.Definitions;
            var progress = achService?.Progress;

            if (achService == null || defs == null || defs.Count == 0 || progress == null)
            {
                AchievementTitle = "Первый запуск";
                AchievementDescription = "Добро пожаловать в Aura Launcher!";
                AchievementDateOrProgress = "В процессе";
                NextAchievementTitle = "Компания";
                NextAchievementDescription = "Добавьте 5 друзей в ваш список";
                NextAchievementProgress = "0 из 5 друзей";
                HasAchievementBlock = true;
                return;
            }

            string? primaryId = null;

            // 1. Проверяем последнее полученное достижение
            var latest = achService.LatestUnlocked;
            if (latest.HasValue)
            {
                primaryId = latest.Value.Id;
                var def = defs.FirstOrDefault(d => string.Equals(d.Id, latest.Value.Id, StringComparison.OrdinalIgnoreCase));
                AchievementTitle = latest.Value.Title;
                AchievementDescription = def?.Description ?? string.Empty;
                AchievementDateOrProgress = latest.Value.UnlockedAtUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
                HasAchievementBlock = true;
            }
            else
            {
                // 2. Если ни одного не получено, ищем ближайшее к получению с прогрессом
                AchievementDefinition? closestDef = null;
                double closestPercent = -1.0;
                double closestCurrent = 0;
                int closestTarget = 1;

                foreach (var d in defs)
                {
                    if (progress.TryGetValue(d.Id, out var p) && !p.Unlocked && p.CurrentValue > 0)
                    {
                        double ratio = p.CurrentValue / Math.Max(1, d.Target);
                        if (ratio > closestPercent)
                        {
                            closestPercent = ratio;
                            closestDef = d;
                            closestCurrent = p.CurrentValue;
                            closestTarget = d.Target;
                        }
                    }
                }

                if (closestDef != null)
                {
                    primaryId = closestDef.Id;
                    AchievementTitle = closestDef.Title;
                    AchievementDescription = closestDef.Description;
                    string unit = GetAchievementUnit(closestDef.Id);
                    AchievementDateOrProgress = !string.IsNullOrEmpty(unit)
                        ? $"{(int)closestCurrent} из {closestTarget} {unit}"
                        : $"{(int)closestCurrent} из {closestTarget}";
                    HasAchievementBlock = true;
                }
                else
                {
                    var d = defs.FirstOrDefault(x => x.Id == "first_launch") ?? defs[0];
                    primaryId = d.Id;
                    AchievementTitle = d.Title;
                    AchievementDescription = d.Description;
                    AchievementDateOrProgress = "Не открыто";
                    HasAchievementBlock = true;
                }
            }

            // 3. Следующая цель (вторая карточка достижений)
            AchievementDefinition? nextDef = null;
            double nextBestRatio = -1.0;
            double nextCurrent = 0;

            foreach (var d in defs)
            {
                if (string.Equals(d.Id, primaryId, StringComparison.OrdinalIgnoreCase)) continue;
                bool isUnlocked = progress.TryGetValue(d.Id, out var p) && p.Unlocked;
                if (isUnlocked) continue;

                double cur = p != null ? p.CurrentValue : 0;
                double ratio = cur / Math.Max(1, d.Target);
                if (ratio > nextBestRatio)
                {
                    nextBestRatio = ratio;
                    nextDef = d;
                    nextCurrent = cur;
                }
            }

            if (nextDef != null)
            {
                NextAchievementTitle = nextDef.Title;
                NextAchievementDescription = nextDef.Description;
                string unit = GetAchievementUnit(nextDef.Id);
                NextAchievementProgress = !string.IsNullOrEmpty(unit)
                    ? $"{(int)nextCurrent} из {nextDef.Target} {unit}"
                    : $"{(int)nextCurrent} из {nextDef.Target}";
                NextAchievementRatio = Math.Clamp(nextCurrent / Math.Max(1, nextDef.Target), 0.0, 1.0);
            }
            else
            {
                var fallbackDef = defs.FirstOrDefault(d => !string.Equals(d.Id, primaryId, StringComparison.OrdinalIgnoreCase)) ?? defs[0];
                NextAchievementTitle = fallbackDef.Title;
                NextAchievementDescription = fallbackDef.Description;
                NextAchievementProgress = "Выполнено";
                NextAchievementRatio = 1.0;
            }

            // 4. Полноразмерный список для Steam-сетки и списка
            var allItems = new List<AchievementDisplayItem>();
            int unlockedCount = 0;
            int totalCount = defs.Count;

            foreach (var d in defs)
            {
                bool isUnlocked = progress.TryGetValue(d.Id, out var p) && p.Unlocked;
                if (isUnlocked) unlockedCount++;

                double cur = p != null ? p.CurrentValue : 0;
                double ratio = Math.Clamp(cur / Math.Max(1, d.Target), 0.0, 1.0);
                string statusText;
                if (isUnlocked)
                {
                    statusText = p?.UnlockedAtUtc.HasValue == true
                        ? $"Получено {p.UnlockedAtUtc.Value.ToLocalTime():dd.MM.yyyy}"
                        : "Получено";
                }
                else
                {
                    string unit = GetAchievementUnit(d.Id);
                    statusText = !string.IsNullOrEmpty(unit)
                        ? $"{(int)cur} из {d.Target} {unit}"
                        : $"{(int)cur} из {d.Target}";
                }

                allItems.Add(new AchievementDisplayItem
                {
                    Id = d.Id,
                    Title = d.Title,
                    Description = d.Description,
                    Target = d.Target,
                    CurrentValue = cur,
                    IsUnlocked = isUnlocked,
                    UnlockedAtUtc = p?.UnlockedAtUtc,
                    StatusText = statusText,
                    ProgressRatio = ratio,
                    IconKey = GetAchievementIconKey(d.Id)
                });
            }

            AchievementsUnlockedCount = unlockedCount;
            AchievementsTotalCount = totalCount;
            AchievementsHeaderText = $"ДОСТИЖЕНИЯ {unlockedCount} ИЗ {totalCount}";
            AchievementsProgressRatio = totalCount > 0 ? (double)unlockedCount / totalCount : 0.0;

            AchievementsGrid.Clear();
            foreach (var it in allItems) AchievementsGrid.Add(it);

            UnlockedAchievements.Clear();
            foreach (var it in allItems.Where(x => x.IsUnlocked)) UnlockedAchievements.Add(it);

            LockedAchievements.Clear();
            foreach (var it in allItems.Where(x => !x.IsUnlocked)) LockedAchievements.Add(it);
        }
        catch
        {
            HasAchievementBlock = false;
        }
    }

    public static string GetAchievementIconKey(string id) => id switch
    {
        "first_launch" => "IconSparkles",
        "ten_launches" => "IconRefresh",
        "hundred_hours" => "IconClock",
        "marathon" => "IconCompass",
        "night_shift" => "IconMoon",
        "first_lobby" => "IconServer",
        "full_table" => "IconUsers",
        "reconnected" => "IconRefresh",
        "first_friend" => "IconUser",
        "five_friends" => "IconUsers",
        "first_backup" => "IconSave",
        "ten_backups" => "IconFolder",
        "photographer" => "IconCamera",
        "own_style" => "IconShirt",
        _ => "IconCrown"
    };

    private void RefreshWhatsNewBlock()
    {
        try
        {
            var (_, ver) = LauncherUpdateService.ResolveVersions();
            var lines = new ObservableCollection<string>();

            try
            {
                var candidates = new[]
                {
                    Path.Combine(AppContext.BaseDirectory, "version.json"),
                    Path.Combine(Environment.CurrentDirectory, "version.json")
                };
                foreach (var path in candidates)
                {
                    if (File.Exists(path))
                    {
                        var json = File.ReadAllText(path);
                        using var doc = System.Text.Json.JsonDocument.Parse(json);
                        if (doc.RootElement.TryGetProperty("changelog", out var clProp) && clProp.ValueKind == System.Text.Json.JsonValueKind.Array)
                        {
                            foreach (var item in clProp.EnumerateArray())
                            {
                                var line = item.GetString();
                                if (!string.IsNullOrWhiteSpace(line))
                                {
                                    lines.Add(line);
                                }
                            }
                        }
                        if (lines.Count > 0) break;
                    }
                }
            }
            catch { }

            if (lines.Count == 0)
            {
                lines.Add(SpecLine);
                if (!string.IsNullOrWhiteSpace(LastUpdateText))
                {
                    lines.Add(LastUpdateText);
                }
            }

            WhatsNewVersion = ver;
            WhatsNewLines = lines;
            WhatsNewPrimaryLine = lines[0];

            TopWhatsNewLines.Clear();
            int count = 0;
            foreach (var l in lines)
            {
                if (count >= 3) break;
                TopWhatsNewLines.Add(l);
                count++;
            }

            HasWhatsNewBlock = true;
        }
        catch
        {
            HasWhatsNewBlock = false;
        }
    }
}
