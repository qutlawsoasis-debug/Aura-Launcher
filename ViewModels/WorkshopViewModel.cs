using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using AuraLauncher.Core;
using AuraLauncher.Models;
using AuraLauncher.Services.Interfaces;
using AuraLauncher.Services.Implementations;

namespace AuraLauncher.ViewModels;

public class WorkshopViewModel : ObservableObject
{
    private readonly IWorkshopService _workshopService;
    private readonly IConfigService _configService;
    private readonly IGameLaunchService _launchService;

    private string _activeSubTab = "Worlds";
    private bool _isWorldsTabActive = true;
    private bool _isModsTabActive = false;
    private bool _isScreenshotsTabActive = false;
    private string _searchModText = string.Empty;
    private bool _isLoading;
    private string _statusMessage = string.Empty;
    private ScreenshotItem? _selectedScreenshot;
    private bool _isScreenshotPreviewOpen;
    private BitmapImage? _fullPreviewImage;
    private string _activeShaderName = string.Empty;

    public ObservableCollection<WorldSaveItem> WorldSaves { get; } = new();
    public ObservableCollection<ModItem> Mods { get; } = new();
    public ObservableCollection<ModItem> FilteredMods { get; } = new();
    public ObservableCollection<ShaderPackItem> ShaderPacks { get; } = new();
    public ObservableCollection<ScreenshotItem> Screenshots { get; } = new();

    public event Action? BackupCreated;
    public event Action<int>? ScreenshotsCountChanged;

    public string ActiveSubTab
    {
        get => _activeSubTab;
        set
        {
            if (SetProperty(ref _activeSubTab, value))
            {
                IsWorldsTabActive = value == "Worlds";
                IsModsTabActive = value == "Mods";
                IsScreenshotsTabActive = value == "Screenshots";
            }
        }
    }

    public bool IsWorldsTabActive
    {
        get => _isWorldsTabActive;
        set => SetProperty(ref _isWorldsTabActive, value);
    }

    public bool IsModsTabActive
    {
        get => _isModsTabActive;
        set => SetProperty(ref _isModsTabActive, value);
    }

    public bool IsScreenshotsTabActive
    {
        get => _isScreenshotsTabActive;
        set => SetProperty(ref _isScreenshotsTabActive, value);
    }

    public string SearchModText
    {
        get => _searchModText;
        set
        {
            if (SetProperty(ref _searchModText, value))
            {
                ApplyModFilter();
            }
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public ScreenshotItem? SelectedScreenshot
    {
        get => _selectedScreenshot;
        set => SetProperty(ref _selectedScreenshot, value);
    }

    public bool IsScreenshotPreviewOpen
    {
        get => _isScreenshotPreviewOpen;
        set => SetProperty(ref _isScreenshotPreviewOpen, value);
    }

    public BitmapImage? FullPreviewImage
    {
        get => _fullPreviewImage;
        set => SetProperty(ref _fullPreviewImage, value);
    }

    public string ActiveShaderName
    {
        get => _activeShaderName;
        set => SetProperty(ref _activeShaderName, value);
    }

    // Commands
    public RelayCommand SwitchSubTabCommand { get; }
    public AsyncRelayCommand RefreshAllCommand { get; }
    public AsyncRelayCommand CreateBackupCommand { get; }
    public RelayCommand OpenWorldFolderCommand { get; }
    public RelayCommand OpenBackupsFolderCommand { get; }
    public event Action? ModToggled;

    public RelayCommand ToggleModCommand { get; }
    public RelayCommand SelectShaderCommand { get; }
    public RelayCommand OpenModsFolderCommand { get; }
    public RelayCommand OpenShadersFolderCommand { get; }
    public RelayCommand OpenScreenshotsFolderCommand { get; }
    public RelayCommand PreviewScreenshotCommand { get; }
    public RelayCommand CloseScreenshotPreviewCommand { get; }
    public RelayCommand CopyScreenshotCommand { get; }
    public RelayCommand OpenScreenshotFileCommand { get; }
    public RelayCommand DeleteScreenshotCommand { get; }

    public WorkshopViewModel(
        IWorkshopService workshopService,
        IConfigService configService,
        IGameLaunchService launchService)
    {
        _workshopService = workshopService ?? throw new ArgumentNullException(nameof(workshopService));
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _launchService = launchService ?? throw new ArgumentNullException(nameof(launchService));

        SwitchSubTabCommand = new RelayCommand(p =>
        {
            if (p is string tab)
            {
                ActiveSubTab = tab;
            }
        });

        RefreshAllCommand = new AsyncRelayCommand(RefreshAllAsync);

        CreateBackupCommand = new AsyncRelayCommand(async p =>
        {
            if (p is not WorldSaveItem world || world.IsBackingUp) return;
            try
            {
                world.IsBackingUp = true;
                world.BackupStatusText = "Создание бэкапа...";
                string zip = await _workshopService.CreateWorldBackupAsync(world);
                world.BackupStatusText = "Бэкап сохранён!";
                BackupCreated?.Invoke();
                _ = Task.Delay(3000).ContinueWith(_ =>
                {
                    Application.Current?.Dispatcher?.InvokeAsync(() => world.BackupStatusText = string.Empty);
                });
            }
            catch (Exception ex)
            {
                world.BackupStatusText = "Ошибка создания";
                FabricGameLaunchService.LogLauncherEvent($"[WORKSHOP: ERROR Backup] {ex.Message}");
            }
            finally
            {
                world.IsBackingUp = false;
            }
        });

        OpenWorldFolderCommand = new RelayCommand(p =>
        {
            if (p is WorldSaveItem world && Directory.Exists(world.FolderPath))
            {
                try
                {
                    Process.Start(new ProcessStartInfo { FileName = world.FolderPath, UseShellExecute = true });
                }
                catch { }
            }
        });

        OpenBackupsFolderCommand = new RelayCommand(_ =>
        {
            string gameDir = GetGameDir();
            string backupDir = Path.Combine(gameDir, "backups", "AuraBackups");
            Directory.CreateDirectory(backupDir);
            try
            {
                Process.Start(new ProcessStartInfo { FileName = backupDir, UseShellExecute = true });
            }
            catch { }
        });

        ToggleModCommand = new RelayCommand(p =>
        {
            if (p is not ModItem mod) return;
            bool newState = !mod.IsEnabled;
            if (_workshopService.ToggleMod(mod, newState))
            {
                ApplyModFilter();
                ModToggled?.Invoke();
            }
        });

        SelectShaderCommand = new RelayCommand(p =>
        {
            if (p is not ShaderPackItem shader) return;
            string gameDir = GetGameDir();
            _workshopService.SetActiveShaderPack(gameDir, shader.FileName);
            foreach (var s in ShaderPacks)
            {
                s.IsActive = (s == shader);
            }
            ActiveShaderName = shader.Name;
        });

        OpenModsFolderCommand = new RelayCommand(_ =>
        {
            string gameDir = GetGameDir();
            string modsDir = Path.Combine(gameDir, "mods");
            Directory.CreateDirectory(modsDir);
            try
            {
                Process.Start(new ProcessStartInfo { FileName = modsDir, UseShellExecute = true });
            }
            catch { }
        });

        OpenShadersFolderCommand = new RelayCommand(_ =>
        {
            string gameDir = GetGameDir();
            string shDir = Path.Combine(gameDir, "shaderpacks");
            Directory.CreateDirectory(shDir);
            try
            {
                Process.Start(new ProcessStartInfo { FileName = shDir, UseShellExecute = true });
            }
            catch { }
        });

        OpenScreenshotsFolderCommand = new RelayCommand(_ =>
        {
            string gameDir = GetGameDir();
            string scDir = Path.Combine(gameDir, "screenshots");
            Directory.CreateDirectory(scDir);
            try
            {
                Process.Start(new ProcessStartInfo { FileName = scDir, UseShellExecute = true });
            }
            catch { }
        });

        PreviewScreenshotCommand = new RelayCommand(p =>
        {
            if (p is ScreenshotItem item)
            {
                OpenScreenshotPreview(item);
            }
        });

        CloseScreenshotPreviewCommand = new RelayCommand(_ =>
        {
            IsScreenshotPreviewOpen = false;
            FullPreviewImage = null;
        });

        CopyScreenshotCommand = new RelayCommand(p =>
        {
            var target = p as ScreenshotItem ?? SelectedScreenshot;
            if (target != null && File.Exists(target.FullPath))
            {
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(target.FullPath, UriKind.Absolute);
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    bmp.Freeze();
                    Clipboard.SetImage(bmp);
                    StatusMessage = "Скриншот скопирован в буфер обмена!";
                    _ = Task.Delay(2500).ContinueWith(_ =>
                    {
                        Application.Current?.Dispatcher?.InvokeAsync(() => StatusMessage = string.Empty);
                    });
                }
                catch { }
            }
        });

        OpenScreenshotFileCommand = new RelayCommand(p =>
        {
            var target = p as ScreenshotItem ?? SelectedScreenshot;
            if (target != null && File.Exists(target.FullPath))
            {
                try
                {
                    Process.Start(new ProcessStartInfo { FileName = target.FullPath, UseShellExecute = true });
                }
                catch { }
            }
        });

        DeleteScreenshotCommand = new RelayCommand(p =>
        {
            var target = p as ScreenshotItem ?? SelectedScreenshot;
            if (target != null)
            {
                if (_workshopService.DeleteScreenshot(target))
                {
                    Screenshots.Remove(target);
                    if (SelectedScreenshot == target)
                    {
                        IsScreenshotPreviewOpen = false;
                        FullPreviewImage = null;
                        SelectedScreenshot = null;
                    }
                }
            }
        });
    }

    public void OpenScreenshotPreview(ScreenshotItem item)
    {
        if (item == null || !File.Exists(item.FullPath)) return;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(item.FullPath, UriKind.Absolute);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();

            FullPreviewImage = bmp;
            SelectedScreenshot = item;
            IsScreenshotPreviewOpen = true;
        }
        catch { }
    }

    public async Task RefreshAllAsync()
    {
        IsLoading = true;
        try
        {
            string gameDir = GetGameDir();

            // 1. Worlds
            var worlds = await _workshopService.GetWorldSavesAsync(gameDir);
            WorldSaves.Clear();
            foreach (var w in worlds) WorldSaves.Add(w);

            // 2. Mods
            var mods = await _workshopService.GetModsAsync(gameDir);
            Mods.Clear();
            foreach (var m in mods) Mods.Add(m);
            ApplyModFilter();

            // 3. Shaders
            var shaders = await _workshopService.GetShaderPacksAsync(gameDir);
            ShaderPacks.Clear();
            foreach (var s in shaders)
            {
                ShaderPacks.Add(s);
                if (s.IsActive) ActiveShaderName = s.Name;
            }

            // 4. Screenshots
            var screens = await _workshopService.GetScreenshotsAsync(gameDir);
            Screenshots.Clear();
            foreach (var sc in screens) Screenshots.Add(sc);
            ScreenshotsCountChanged?.Invoke(Screenshots.Count);
        }
        catch (Exception ex)
        {
            FabricGameLaunchService.LogLauncherEvent($"[WORKSHOP: ERROR Refresh] {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ApplyModFilter()
    {
        FilteredMods.Clear();
        string filter = SearchModText?.Trim() ?? string.Empty;
        var query = string.IsNullOrWhiteSpace(filter)
            ? Mods
            : Mods.Where(m => m.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                              m.FileName.Contains(filter, StringComparison.OrdinalIgnoreCase));

        foreach (var m in query)
        {
            FilteredMods.Add(m);
        }
    }

    private string GetGameDir()
    {
        var config = _configService.CurrentConfig;
        return _launchService.ResolveMinecraftDirectory(config.GameDir);
    }
}
