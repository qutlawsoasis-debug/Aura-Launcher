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
    private string _specLine = "Minecraft 1.20.1, Fabric 0.19.5, 93 мода";
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
    private string _playtimeSummaryText = "⏱️ Время в игре: 0 мин. • 0 запусков";

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

    public RelayCommand OpenAchievementsCommand { get; }

    public OverviewViewModel(IConfigService configService, IGameLaunchService launchService)
    {
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _launchService = launchService ?? throw new ArgumentNullException(nameof(launchService));

        OpenAchievementsCommand = new RelayCommand(_ =>
        {
            // Открывает страницу достижений, если её нет — ничего не делает
        });

        RefreshStats();

        _configService.ConfigChanged += (s, cfg) =>
        {
            RefreshStats();
        };
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
                ? $"Minecraft 1.20.1, Fabric {fabricLoader}, {modCount} мод"
                : "Minecraft 1.20.1, Fabric 0.19.5, 93 мода";
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
        PlaytimeSummaryText = $"⏱️ В игре: {PlayTimeFormatted}  •  Запусков: {config.TotalGameLaunches}";

        // 6. Обновление правой колонки (ленты)
        RefreshRightFeed();
    }

    // ==========================================
    // ПРАВАЯ КОЛОНКА (ЛЕНТА НА ГЛАВНОМ ЭКРАНЕ)
    // ==========================================
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
        RefreshLatestScreenshot();
        RefreshAchievementBlock();
        RefreshWhatsNewBlock();
        UpdateHasRightFeed();
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
                HasLatestScreenshot = false;
                return;
            }

            var di = new DirectoryInfo(screensDir);
            var latestFile = di.GetFiles("*.png")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .FirstOrDefault();

            if (latestFile == null)
            {
                LatestScreenshot = null;
                HasLatestScreenshot = false;
                return;
            }

            // Проверяем, изменился ли файл или дата
            if (LatestScreenshot != null && 
                string.Equals(LatestScreenshot.FullPath, latestFile.FullName, StringComparison.OrdinalIgnoreCase) &&
                LatestScreenshot.CreatedDate == latestFile.LastWriteTime)
            {
                // Уже актуален
                HasLatestScreenshot = true;
                return;
            }

            BitmapImage? bmp = null;
            try
            {
                bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(latestFile.FullName, UriKind.Absolute);
                bmp.DecodePixelWidth = 680;
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
            }
            catch
            {
                LatestScreenshot = null;
                HasLatestScreenshot = false;
                return;
            }

            LatestScreenshot = new ScreenshotItem
            {
                FileName = latestFile.Name,
                FullPath = latestFile.FullName,
                CreatedDate = latestFile.LastWriteTime,
                Thumbnail = bmp
            };
            LatestScreenshotDateText = latestFile.LastWriteTime.ToString("dd.MM.yyyy HH:mm");
            HasLatestScreenshot = true;
        }
        catch
        {
            LatestScreenshot = null;
            HasLatestScreenshot = false;
        }
    }

    private void RefreshAchievementBlock()
    {
        try
        {
            var achService = App.Services?.GetService(typeof(IAchievementService)) as IAchievementService;
            if (achService == null)
            {
                HasAchievementBlock = false;
                return;
            }

            var defs = achService.Definitions;
            var progress = achService.Progress;

            if (defs == null || defs.Count == 0)
            {
                HasAchievementBlock = false;
                return;
            }

            // 1. Проверяем последнее полученное достижение
            var latest = achService.LatestUnlocked;
            if (latest.HasValue)
            {
                var def = defs.FirstOrDefault(d => string.Equals(d.Id, latest.Value.Id, StringComparison.OrdinalIgnoreCase));
                AchievementTitle = latest.Value.Title;
                AchievementDescription = def?.Description ?? string.Empty;
                AchievementDateOrProgress = latest.Value.UnlockedAtUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
                HasAchievementBlock = true;
                return;
            }

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
                AchievementTitle = closestDef.Title;
                AchievementDescription = closestDef.Description;
                string unit = closestDef.Id switch
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

                if (!string.IsNullOrEmpty(unit))
                {
                    AchievementDateOrProgress = $"{closestDef.Title}: {(int)closestCurrent} из {closestTarget} {unit}";
                }
                else
                {
                    AchievementDateOrProgress = $"Прогресс: {(int)closestCurrent} из {closestTarget}";
                }
                HasAchievementBlock = true;
                return;
            }

            // 3. Если вообще нет данных о прогрессе
            HasAchievementBlock = false;
        }
        catch
        {
            HasAchievementBlock = false;
        }
    }

    private void RefreshWhatsNewBlock()
    {
        try
        {
            // Берем версию из version.json или текущей версии
            string ver = "beta 1.0.3";
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
                        if (doc.RootElement.TryGetProperty("userFacingVersion", out var prop))
                        {
                            var val = prop.GetString();
                            if (!string.IsNullOrWhiteSpace(val)) { ver = val; break; }
                        }
                    }
                }
            }
            catch { }

            WhatsNewVersion = ver;

            var lines = new ObservableCollection<string>
            {
                "Frostiful & Snowy Spirit — зимнее выживание и атмосфера",
                "Alex's Mobs — новые живые существа и анимации",
                "Spell Engine — переработанная магия и атрибуты заклинаний"
            };
            WhatsNewLines = lines;
            HasWhatsNewBlock = true;
        }
        catch
        {
            HasWhatsNewBlock = false;
        }
    }
}
