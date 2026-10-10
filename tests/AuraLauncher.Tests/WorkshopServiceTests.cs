using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AuraLauncher.Models;
using AuraLauncher.Services.Implementations;
using AuraLauncher.ViewModels;
using Xunit;

namespace AuraLauncher.Tests;

public class WorkshopServiceTests : IDisposable
{
    private readonly string _tempDir;

    public WorkshopServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "AuraWorkshopTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch { }
    }

    [Fact]
    public void ToggleMod_RenamesJarToDisabled_AndBack()
    {
        var service = new WorkshopService();
        string modsDir = Path.Combine(_tempDir, "mods");
        Directory.CreateDirectory(modsDir);

        string testModPath = Path.Combine(modsDir, "Iris-1.7.0.jar");
        File.WriteAllText(testModPath, "dummy jar content");

        var mod = new ModItem
        {
            FileName = "Iris-1.7.0.jar",
            FullPath = testModPath,
            DisplayName = "Iris",
            IsEnabled = true
        };

        // Disable
        bool disabledSuccess = service.ToggleMod(mod, false);
        Assert.True(disabledSuccess);
        Assert.False(mod.IsEnabled);
        Assert.EndsWith(".disabled", mod.FullPath);
        Assert.True(File.Exists(mod.FullPath));
        Assert.False(File.Exists(testModPath));

        // Re-enable
        bool enableSuccess = service.ToggleMod(mod, true);
        Assert.True(enableSuccess);
        Assert.True(mod.IsEnabled);
        Assert.EndsWith(".jar", mod.FullPath);
        Assert.True(File.Exists(mod.FullPath));
    }

    [Fact]
    public void SetActiveShaderPack_WritesIrisPropertiesAndOptionsShaders()
    {
        var service = new WorkshopService();
        string shaderName = "ComplementaryReimagined_r5.1.1.zip";

        service.SetActiveShaderPack(_tempDir, shaderName);

        string irisProps = Path.Combine(_tempDir, "config", "iris.properties");
        Assert.True(File.Exists(irisProps));
        string irisContent = File.ReadAllText(irisProps);
        Assert.Contains($"shaderPack={shaderName}", irisContent);

        string optShaders = Path.Combine(_tempDir, "optionsshaders.txt");
        Assert.True(File.Exists(optShaders));
        string optContent = File.ReadAllText(optShaders);
        Assert.Contains($"currentShaderPacks={shaderName}", optContent);
    }

    [Fact]
    public void SetActiveShaderPack_OFF_DisablesShaders()
    {
        var service = new WorkshopService();

        service.SetActiveShaderPack(_tempDir, "OFF");

        string irisProps = Path.Combine(_tempDir, "config", "iris.properties");
        Assert.True(File.Exists(irisProps));
        string irisContent = File.ReadAllText(irisProps);
        Assert.Contains("shaderPack=OFF", irisContent);
        Assert.Contains("enableShaders=false", irisContent);

        string optShaders = Path.Combine(_tempDir, "optionsshaders.txt");
        Assert.True(File.Exists(optShaders));
        string optContent = File.ReadAllText(optShaders);
        Assert.Contains("currentShaderPacks=OFF", optContent);
    }

    [Fact]
    public async Task GetShaderPacksAsync_IncludesNonePack_AndDiscoveredFiles()
    {
        var service = new WorkshopService();
        string shDir = Path.Combine(_tempDir, "shaderpacks");
        Directory.CreateDirectory(shDir);
        File.WriteAllText(Path.Combine(shDir, "BSL_v8.2.zip"), "dummy");
        File.WriteAllText(Path.Combine(shDir, "MakeUp-UltraFast.zip"), "dummy");

        var shaders = await service.GetShaderPacksAsync(_tempDir);

        Assert.Contains(shaders, s => s.FileName == "" && s.Name == "Без шейдеров");
        Assert.Contains(shaders, s => s.FileName == "BSL_v8.2.zip");
        Assert.Contains(shaders, s => s.FileName == "MakeUp-UltraFast.zip");
    }

    [Fact]
    public async Task CreateWorldBackupAsync_CreatesValidZipFile()
    {
        var service = new WorkshopService();
        string worldDir = Path.Combine(_tempDir, "saves", "SurvivalWorld");
        Directory.CreateDirectory(worldDir);
        File.WriteAllText(Path.Combine(worldDir, "level.dat"), "mock level data");

        var world = new WorldSaveItem
        {
            FolderName = "SurvivalWorld",
            DisplayName = "Survival World",
            FolderPath = worldDir
        };

        string backupPath = await service.CreateWorldBackupAsync(world);

        Assert.True(File.Exists(backupPath));
        Assert.EndsWith(".zip", backupPath);
        Assert.Contains("Survival World", Path.GetFileName(backupPath));
    }

    [Fact]
    public void DeleteScreenshot_DeletesFile()
    {
        var service = new WorkshopService();
        string scDir = Path.Combine(_tempDir, "screenshots");
        Directory.CreateDirectory(scDir);
        string scFile = Path.Combine(scDir, "2026-10-07_12.00.00.png");
        File.WriteAllText(scFile, "mock image");

        var item = new ScreenshotItem
        {
            FileName = "2026-10-07_12.00.00.png",
            FullPath = scFile
        };

        bool deleted = service.DeleteScreenshot(item);
        Assert.True(deleted);
        Assert.False(File.Exists(scFile));
    }

    [Fact]
    public async Task ToggleModCommand_UpdatesModInPlace_WithoutResettingFilteredMods()
    {
        var service = new WorkshopService();
        string modsDir = Path.Combine(_tempDir, "mods");
        Directory.CreateDirectory(modsDir);
        File.WriteAllText(Path.Combine(modsDir, "Alpha-1.0.jar"), "a");
        File.WriteAllText(Path.Combine(modsDir, "Beta-2.0.jar"), "b");

        var configService = new TestConfigService(_tempDir, PackUpdateService.DefaultPackRepo);
        var launchService = new FabricGameLaunchService();
        var vm = new WorkshopViewModel(service, configService, launchService);

        await vm.RefreshAllAsync();
        Assert.Equal(2, vm.FilteredMods.Count);

        int collectionChanges = 0;
        vm.FilteredMods.CollectionChanged += (_, _) => collectionChanges++;

        var firstMod = vm.FilteredMods[0];
        bool fileNameNotified = false;
        firstMod.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ModItem.FileName))
            {
                fileNameNotified = true;
            }
        };

        // Simulate CheckBox two-way binding + Command execution
        firstMod.IsEnabled = false;
        vm.ToggleModCommand.Execute(firstMod);

        Assert.Equal(0, collectionChanges);
        Assert.True(fileNameNotified);
        Assert.EndsWith(".disabled", firstMod.FileName);
        Assert.True(vm.FilteredMods[1].IsEnabled);
    }

    [Fact]
    public async Task ExportAndImportWorldZip_RoundTripsWorldAndCreatesAutoBackup()
    {
        var service = new WorkshopService();
        string worldDir = Path.Combine(_tempDir, "saves", "CoopWorld");
        Directory.CreateDirectory(Path.Combine(worldDir, "region"));
        File.WriteAllText(Path.Combine(worldDir, "level.dat"), "level-content");
        File.WriteAllText(Path.Combine(worldDir, "region", "r.0.0.mca"), "chunk-data");

        var worldItem = new WorldSaveItem
        {
            FolderName = "CoopWorld",
            DisplayName = "Coop World",
            FolderPath = worldDir
        };

        string exportZip = Path.Combine(_tempDir, "ExportedCoop.zip");
        await service.ExportWorldToZipAsync(worldItem, exportZip);
        Assert.True(File.Exists(exportZip));

        string importedFolderName = await service.ImportWorldFromZipAsync(_tempDir, exportZip);
        string importedPath = Path.Combine(_tempDir, "saves", importedFolderName);
        Assert.True(Directory.Exists(importedPath));
        Assert.True(File.Exists(Path.Combine(importedPath, "level.dat")));
        Assert.True(File.Exists(Path.Combine(importedPath, "region", "r.0.0.mca")));

        await service.CreateAutoBackupLatestWorldAsync(_tempDir, maxAutoBackupsPerWorld: 2);
        var autoBackups = Directory.GetFiles(Path.Combine(_tempDir, "backups", "AuraBackups"), "*_auto_*.zip");
        Assert.NotEmpty(autoBackups);
    }

    [Fact]
    public void GameCrashAnalyzer_DetectsOutOfMemoryAndGraphicsPresetsWriteOptions()
    {
        string logsDir = Path.Combine(_tempDir, "logs");
        Directory.CreateDirectory(logsDir);
        File.WriteAllText(Path.Combine(logsDir, "latest.log"), "[main/ERROR]: java.lang.OutOfMemoryError: Java heap space");

        var crash = GameCrashAnalyzer.AnalyzeCrash(_tempDir, 1);
        Assert.Contains("памяти", crash.Title, StringComparison.OrdinalIgnoreCase);

        FabricGameLaunchService.ApplyGraphicsPreset(_tempDir, "Low");
        string optionsText = File.ReadAllText(Path.Combine(_tempDir, "options.txt"));
        Assert.Contains("renderDistance:12", optionsText);
        Assert.Contains("simulationDistance:6", optionsText);

        FabricGameLaunchService.ApplyGraphicsPreset(_tempDir, "Ultra");
        optionsText = File.ReadAllText(Path.Combine(_tempDir, "options.txt"));
        Assert.Contains("renderDistance:32", optionsText);
        Assert.Contains("simulationDistance:12", optionsText);
    }

    [Fact]
    public void ApplyPreset_HighAndUltra_WritesOptionsAndShaderOptions()
    {
        string dirHigh = Path.Combine(_tempDir, "game_high");
        Directory.CreateDirectory(Path.Combine(dirHigh, "shaderpacks"));
        File.WriteAllText(Path.Combine(dirHigh, "shaderpacks", "photon_v1.3b.zip"), "dummy photon");

        FabricGameLaunchService.ApplyGraphicsPreset(dirHigh, "High");

        Assert.True(File.Exists(Path.Combine(dirHigh, "options.txt")));
        Assert.True(File.Exists(Path.Combine(dirHigh, "shaderpacks", "photon_v1.3b.zip.txt")));
        string highOptions = File.ReadAllText(Path.Combine(dirHigh, "options.txt"));
        string highShaderTxt = File.ReadAllText(Path.Combine(dirHigh, "shaderpacks", "photon_v1.3b.zip.txt"));
        Assert.Contains("renderDistance:24", highOptions);
        Assert.Contains("simulationDistance:10", highOptions);
        Assert.Contains("profile=high", highShaderTxt);

        string dirUltra = Path.Combine(_tempDir, "game_ultra");
        Directory.CreateDirectory(Path.Combine(dirUltra, "shaderpacks"));
        File.WriteAllText(Path.Combine(dirUltra, "shaderpacks", "ComplementaryReimagined_r5.9.3.zip"), "dummy complementary");

        FabricGameLaunchService.ApplyGraphicsPreset(dirUltra, "Ultra");

        Assert.True(File.Exists(Path.Combine(dirUltra, "options.txt")));
        Assert.True(File.Exists(Path.Combine(dirUltra, "shaderpacks", "ComplementaryReimagined_r5.9.3.zip.txt")));
        string ultraOptions = File.ReadAllText(Path.Combine(dirUltra, "options.txt"));
        string ultraShaderTxt = File.ReadAllText(Path.Combine(dirUltra, "shaderpacks", "ComplementaryReimagined_r5.9.3.zip.txt"));
        Assert.Contains("renderDistance:32", ultraOptions);
        Assert.Contains("simulationDistance:12", ultraOptions);
        Assert.Contains("profile=ULTRA", ultraShaderTxt);
        Assert.DoesNotContain("EUPHORIA_PATCHES_MOD_INSTALLED", ultraShaderTxt);

        string dump = $"=== HIGH OPTIONS ===\n{highOptions}\n=== HIGH SHADER TXT ===\n{highShaderTxt}\n=== ULTRA OPTIONS ===\n{ultraOptions}\n=== ULTRA SHADER TXT ===\n{ultraShaderTxt}\n=== HIGH LOG ===\n{File.ReadAllText(Path.Combine(dirHigh, "logs", "launcher-game.log"))}\n=== ULTRA LOG ===\n{File.ReadAllText(Path.Combine(dirUltra, "logs", "launcher-game.log"))}\n";
        File.WriteAllText(Path.Combine(_tempDir, "dump.txt"), dump);
        File.WriteAllText("ApplyPreset_Dump.txt", dump);
    }
}
