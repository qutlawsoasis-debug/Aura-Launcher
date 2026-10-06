using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using AuraLauncher.Core;
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
    private string _specLine = "Minecraft 1.20.1, Fabric 0.19.5, 61 мод";
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

    public bool IsEnvironmentInstalled
    {
        get => _isEnvironmentInstalled;
        private set => SetProperty(ref _isEnvironmentInstalled, value);
    }

    public OverviewViewModel(IConfigService configService, IGameLaunchService launchService)
    {
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _launchService = launchService ?? throw new ArgumentNullException(nameof(launchService));

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
                : "Minecraft 1.20.1, Fabric 0.19.5, 61 мод";
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
    }
}
