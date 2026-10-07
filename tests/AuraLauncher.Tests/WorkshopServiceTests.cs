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
}
