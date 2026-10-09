using System;
using System.Collections.Generic;
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

    private bool _isRestoreModalOpen;
    private WorldSaveItem? _activeBackupWorld;
    private WorldBackupItem? _restoreConfirmBackup;
    private WorldBackupItem? _deleteConfirmBackup;

    public ObservableCollection<WorldSaveItem> WorldSaves { get; } = new();
    public ObservableCollection<ModItem> Mods { get; } = new();
    public ObservableCollection<ModItem> FilteredMods { get; } = new();
    public ObservableCollection<ShaderPackItem> ShaderPacks { get; } = new();
    public ObservableCollection<ScreenshotItem> Screenshots { get; } = new();

    public event Action? BackupCreated;
    public event Action<int>? ScreenshotsCountChanged;
    public event Action? ModToggled;
    public event Action? SubTabChanged;

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
                SubTabChanged?.Invoke();
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

    public int WorldsCount => WorldSaves.Count;
    public int ModsCount => Mods.Count;
    public int ScreenshotsCount => Screenshots.Count;

    public bool HasWorlds => WorldSaves.Count > 0;
    public bool HasNoWorlds => WorldSaves.Count == 0 && !IsLoading;

    public bool HasMods => FilteredMods.Count > 0;
    public bool HasNoMods => FilteredMods.Count == 0 && !IsLoading;

    public bool HasScreenshots => Screenshots.Count > 0;
    public bool HasNoScreenshots => Screenshots.Count == 0 && !IsLoading;

    public WorldSaveItem? FeaturedWorld => WorldSaves.FirstOrDefault();
    public IEnumerable<WorldSaveItem> OtherWorlds => WorldSaves.Count > 1 ? WorldSaves.Skip(1) : Enumerable.Empty<WorldSaveItem>();
    public bool HasOtherWorlds => WorldSaves.Count > 1;

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
        set
        {
            if (SetProperty(ref _isLoading, value))
            {
                OnPropertyChanged(nameof(HasNoWorlds));
                OnPropertyChanged(nameof(HasNoMods));
                OnPropertyChanged(nameof(HasNoScreenshots));
            }
        }
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

    public bool IsRestoreModalOpen
    {
        get => _isRestoreModalOpen;
        set => SetProperty(ref _isRestoreModalOpen, value);
    }

    public WorldSaveItem? ActiveBackupWorld
    {
        get => _activeBackupWorld;
        set => SetProperty(ref _activeBackupWorld, value);
    }

    public WorldBackupItem? RestoreConfirmBackup
    {
        get => _restoreConfirmBackup;
        set => SetProperty(ref _restoreConfirmBackup, value);
    }

    public WorldBackupItem? DeleteConfirmBackup
    {
        get => _deleteConfirmBackup;
        set => SetProperty(ref _deleteConfirmBackup, value);
    }

    // Commands
    public RelayCommand SwitchSubTabCommand { get; }
    public AsyncRelayCommand RefreshAllCommand { get; }
    public AsyncRelayCommand CreateBackupCommand { get; }
    public AsyncRelayCommand ExportWorldCommand { get; }
    public AsyncRelayCommand ImportWorldCommand { get; }
    public RelayCommand OpenWorldFolderCommand { get; }
    public RelayCommand OpenBackupsFolderCommand { get; }

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

    public RelayCommand OpenRestoreDialogCommand { get; }
    public RelayCommand CloseRestoreDialogCommand { get; }
    public RelayCommand PromptRestoreBackupCommand { get; }
    public RelayCommand CancelRestoreConfirmCommand { get; }
    public AsyncRelayCommand ConfirmRestoreBackupCommand { get; }
    public RelayCommand PromptDeleteBackupCommand { get; }
    public RelayCommand CancelDeleteConfirmCommand { get; }
    public AsyncRelayCommand ConfirmDeleteBackupCommand { get; }

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
            var world = p as WorldSaveItem ?? FeaturedWorld;
            if (world == null || world.IsBackingUp) return;
            try
            {
                world.IsBackingUp = true;
                world.BackupStatusText = "Создание бэкапа...";
                string zip = await _workshopService.CreateWorldBackupAsync(world);
                world.BackupStatusText = "Бэкап сохранён!";
                BackupCreated?.Invoke();

                // Refresh backups list for this world
                await RefreshAllAsync();

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

        ExportWorldCommand = new AsyncRelayCommand(async p =>
        {
            var world = p as WorldSaveItem ?? FeaturedWorld;
            if (world == null || world.IsBackingUp) return;

            string safeName = string.Join("_", world.DisplayName.Split(Path.GetInvalidFileNameChars()));
            if (string.IsNullOrWhiteSpace(safeName)) safeName = world.FolderName;

            var sfd = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Экспорт мира в архив (.zip)",
                Filter = "Архив мира Minecraft (*.zip)|*.zip",
                FileName = $"{safeName}.zip",
                DefaultExt = ".zip"
            };

            if (sfd.ShowDialog() != true || string.IsNullOrWhiteSpace(sfd.FileName))
            {
                return;
            }

            try
            {
                world.IsBackingUp = true;
                world.BackupStatusText = "Экспорт мира...";
                await _workshopService.ExportWorldToZipAsync(world, sfd.FileName);
                world.BackupStatusText = "Мир экспортирован!";
                StatusMessage = $"Мир сохранён: {Path.GetFileName(sfd.FileName)}";
                _ = Task.Delay(3000).ContinueWith(_ =>
                {
                    Application.Current?.Dispatcher?.InvokeAsync(() =>
                    {
                        world.BackupStatusText = string.Empty;
                        StatusMessage = string.Empty;
                    });
                });
            }
            catch (Exception ex)
            {
                world.BackupStatusText = "Ошибка экспорта";
                StatusMessage = $"Ошибка экспорта: {ex.Message}";
                FabricGameLaunchService.LogLauncherEvent($"[WORKSHOP: ERROR Export] {ex.Message}");
            }
            finally
            {
                world.IsBackingUp = false;
            }
        });

        ImportWorldCommand = new AsyncRelayCommand(async _ =>
        {
            var ofd = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Выберите архив мира Minecraft (.zip)",
                Filter = "Архив мира Minecraft (*.zip)|*.zip",
                Multiselect = false
            };

            if (ofd.ShowDialog() != true || string.IsNullOrWhiteSpace(ofd.FileName))
            {
                return;
            }

            try
            {
                StatusMessage = "Импорт мира из архива...";
                string importedName = await _workshopService.ImportWorldFromZipAsync(GetGameDir(), ofd.FileName);
                await RefreshAllAsync();
                StatusMessage = $"Мир «{importedName}» импортирован!";
                _ = Task.Delay(3500).ContinueWith(_ =>
                {
                    Application.Current?.Dispatcher?.InvokeAsync(() => StatusMessage = string.Empty);
                });
            }
            catch (Exception ex)
            {
                StatusMessage = $"Ошибка импорта: {ex.Message}";
                FabricGameLaunchService.LogLauncherEvent($"[WORKSHOP: ERROR Import] {ex.Message}");
            }
        });

        OpenWorldFolderCommand = new RelayCommand(p =>
        {
            var world = p as WorldSaveItem ?? FeaturedWorld;
            if (world != null && Directory.Exists(world.FolderPath))
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
            bool targetState = mod.IsEnabled;
            bool ok = _workshopService.ToggleMod(mod, targetState);
            if (!ok)
            {
                mod.IsEnabled = mod.FullPath.EndsWith(".jar", StringComparison.OrdinalIgnoreCase);
            }
            ModToggled?.Invoke();
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
                    OnPropertyChanged(nameof(ScreenshotsCount));
                    OnPropertyChanged(nameof(HasScreenshots));
                    OnPropertyChanged(nameof(HasNoScreenshots));
                }
            }
        });

        OpenRestoreDialogCommand = new RelayCommand(p =>
        {
            ActiveBackupWorld = p as WorldSaveItem ?? FeaturedWorld;
            RestoreConfirmBackup = null;
            DeleteConfirmBackup = null;
            IsRestoreModalOpen = true;
        });

        CloseRestoreDialogCommand = new RelayCommand(_ =>
        {
            IsRestoreModalOpen = false;
            ActiveBackupWorld = null;
            RestoreConfirmBackup = null;
            DeleteConfirmBackup = null;
        });

        PromptRestoreBackupCommand = new RelayCommand(p =>
        {
            var item = p as WorldBackupItem;
            if (ActiveBackupWorld?.Backups != null)
            {
                foreach (var b in ActiveBackupWorld.Backups)
                {
                    b.IsConfirmingRestore = (b == item);
                    b.IsConfirmingDelete = false;
                }
            }
            RestoreConfirmBackup = item;
            DeleteConfirmBackup = null;
        });

        CancelRestoreConfirmCommand = new RelayCommand(_ =>
        {
            if (ActiveBackupWorld?.Backups != null)
            {
                foreach (var b in ActiveBackupWorld.Backups)
                    b.IsConfirmingRestore = false;
            }
            RestoreConfirmBackup = null;
        });

        ConfirmRestoreBackupCommand = new AsyncRelayCommand(async p =>
        {
            var backup = p as WorldBackupItem ?? RestoreConfirmBackup;
            if (ActiveBackupWorld != null && backup != null)
            {
                try
                {
                    await _workshopService.RestoreWorldBackupAsync(ActiveBackupWorld, backup);
                    StatusMessage = "Мир успешно восстановлен из бэкапа!";
                    _ = Task.Delay(3000).ContinueWith(_ =>
                    {
                        Application.Current?.Dispatcher?.InvokeAsync(() => StatusMessage = string.Empty);
                    });
                    RestoreConfirmBackup = null;
                    IsRestoreModalOpen = false;
                    await RefreshAllAsync();
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Ошибка восстановления: {ex.Message}";
                }
            }
        });

        PromptDeleteBackupCommand = new RelayCommand(p =>
        {
            var item = p as WorldBackupItem;
            if (ActiveBackupWorld?.Backups != null)
            {
                foreach (var b in ActiveBackupWorld.Backups)
                {
                    b.IsConfirmingDelete = (b == item);
                    b.IsConfirmingRestore = false;
                }
            }
            DeleteConfirmBackup = item;
            RestoreConfirmBackup = null;
        });

        CancelDeleteConfirmCommand = new RelayCommand(_ =>
        {
            if (ActiveBackupWorld?.Backups != null)
            {
                foreach (var b in ActiveBackupWorld.Backups)
                    b.IsConfirmingDelete = false;
            }
            DeleteConfirmBackup = null;
        });

        ConfirmDeleteBackupCommand = new AsyncRelayCommand(async p =>
        {
            var backup = p as WorldBackupItem ?? DeleteConfirmBackup;
            if (backup != null)
            {
                try
                {
                    await _workshopService.DeleteWorldBackupAsync(backup);
                    if (ActiveBackupWorld != null)
                    {
                        ActiveBackupWorld.Backups.Remove(backup);
                        ActiveBackupWorld.BackupsCount = ActiveBackupWorld.Backups.Count;
                    }
                    DeleteConfirmBackup = null;
                    await RefreshAllAsync();
                }
                catch { }
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

            OnPropertyChanged(nameof(WorldsCount));
            OnPropertyChanged(nameof(FeaturedWorld));
            OnPropertyChanged(nameof(OtherWorlds));
            OnPropertyChanged(nameof(HasOtherWorlds));
            OnPropertyChanged(nameof(HasWorlds));
            OnPropertyChanged(nameof(HasNoWorlds));

            // 2. Mods
            var mods = await _workshopService.GetModsAsync(gameDir);
            var existingByDisplay = new Dictionary<string, ModItem>(StringComparer.OrdinalIgnoreCase);
            foreach (var existingMod in Mods)
            {
                existingByDisplay.TryAdd(existingMod.DisplayName, existingMod);
            }

            var updatedMods = new List<ModItem>(mods.Count);
            foreach (var fresh in mods)
            {
                if (existingByDisplay.TryGetValue(fresh.DisplayName, out var existing))
                {
                    existing.FileName = fresh.FileName;
                    existing.FullPath = fresh.FullPath;
                    existing.Version = fresh.Version;
                    existing.SizeFormatted = fresh.SizeFormatted;
                    existing.IsEnabled = fresh.IsEnabled;
                    updatedMods.Add(existing);
                }
                else
                {
                    updatedMods.Add(fresh);
                }
            }

            Mods.Clear();
            foreach (var m in updatedMods) Mods.Add(m);
            ApplyModFilter();

            OnPropertyChanged(nameof(ModsCount));
            OnPropertyChanged(nameof(HasMods));
            OnPropertyChanged(nameof(HasNoMods));

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

            OnPropertyChanged(nameof(ScreenshotsCount));
            OnPropertyChanged(nameof(HasScreenshots));
            OnPropertyChanged(nameof(HasNoScreenshots));
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
        string filter = SearchModText?.Trim() ?? string.Empty;
        var desired = (string.IsNullOrWhiteSpace(filter)
            ? Mods
            : Mods.Where(m => m.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                              m.FileName.Contains(filter, StringComparison.OrdinalIgnoreCase))).ToList();

        var desiredSet = new HashSet<ModItem>(desired);
        for (int i = FilteredMods.Count - 1; i >= 0; i--)
        {
            if (!desiredSet.Contains(FilteredMods[i]))
            {
                FilteredMods.RemoveAt(i);
            }
        }

        for (int i = 0; i < desired.Count; i++)
        {
            var item = desired[i];
            if (i < FilteredMods.Count && ReferenceEquals(FilteredMods[i], item))
            {
                continue;
            }

            int existingIdx = FilteredMods.IndexOf(item);
            if (existingIdx >= 0)
            {
                FilteredMods.Move(existingIdx, i);
            }
            else
            {
                FilteredMods.Insert(i, item);
            }
        }

        OnPropertyChanged(nameof(HasMods));
        OnPropertyChanged(nameof(HasNoMods));
    }

    private string GetGameDir()
    {
        var config = _configService.CurrentConfig;
        return _launchService.ResolveMinecraftDirectory(config.GameDir);
    }
}
