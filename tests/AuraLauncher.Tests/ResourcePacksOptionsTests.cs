using System;
using System.IO;
using System.Text;
using AuraLauncher.Services.Implementations;
using Xunit;

namespace AuraLauncher.Tests;

public class ResourcePacksOptionsTests : IDisposable
{
    private readonly string _testDir;

    public ResourcePacksOptionsTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "aura_rp_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, recursive: true);
            }
        }
        catch { }
    }

    [Fact]
    public void ParseAndFormatOptionsList_RoundTrip_PreservesValuesAndEscaping()
    {
        var rawLine = "resourcePacks:[\"vanilla\",\"fabric\",\"file/Ancient City Panorama V2.zip\",\"punchy:punchy\",\"file/Test\\\"Escaped.zip\"]";
        var list = FabricGameLaunchService.ParseOptionsList(rawLine);

        Assert.Equal(5, list.Count);
        Assert.Equal("vanilla", list[0]);
        Assert.Equal("fabric", list[1]);
        Assert.Equal("file/Ancient City Panorama V2.zip", list[2]);
        Assert.Equal("punchy:punchy", list[3]);
        Assert.Equal("file/Test\"Escaped.zip", list[4]);

        var formatted = FabricGameLaunchService.FormatOptionsList(list);
        var expectedFormatted = "[\"vanilla\",\"fabric\",\"file/Ancient City Panorama V2.zip\",\"punchy:punchy\",\"file/Test\\\"Escaped.zip\"]";
        Assert.Equal(expectedFormatted, formatted);
    }

    [Fact]
    public void EnsureDefaultResourcePacksEnabled_CreatesOptionsTxtIfMissing_AndEnablesAllPacks()
    {
        var rpDir = Path.Combine(_testDir, "resourcepacks");
        Directory.CreateDirectory(rpDir);
        File.WriteAllBytes(Path.Combine(rpDir, "Better-Leaves-9.6.zip"), new byte[] { 1, 2, 3 });
        File.WriteAllBytes(Path.Combine(rpDir, "Patrix_1.20_32x_basic.zip"), new byte[] { 4, 5, 6 });

        var folderPack = Path.Combine(rpDir, "FolderPack");
        Directory.CreateDirectory(folderPack);
        File.WriteAllText(Path.Combine(folderPack, "pack.mcmeta"), "{\"pack\":{\"pack_format\":15}}");

        FabricGameLaunchService.EnsureDefaultResourcePacksEnabled(_testDir);

        var optionsPath = Path.Combine(_testDir, "options.txt");
        Assert.True(File.Exists(optionsPath));

        var lines = File.ReadAllLines(optionsPath, Encoding.UTF8);
        var rpLine = Assert.Single(lines, l => l.StartsWith("resourcePacks:"));
        var incompLine = Assert.Single(lines, l => l.StartsWith("incompatibleResourcePacks:"));

        var rpList = FabricGameLaunchService.ParseOptionsList(rpLine);
        var incompList = FabricGameLaunchService.ParseOptionsList(incompLine);

        Assert.Contains("vanilla", rpList);
        Assert.Contains("fabric", rpList);
        Assert.Contains("file/Better-Leaves-9.6.zip", rpList);
        Assert.Contains("file/Patrix_1.20_32x_basic.zip", rpList);
        Assert.Contains("file/FolderPack", rpList);

        Assert.Contains("file/Better-Leaves-9.6.zip", incompList);
        Assert.Contains("file/Patrix_1.20_32x_basic.zip", incompList);
        Assert.Contains("file/FolderPack", incompList);
    }

    [Fact]
    public void EnsureDefaultResourcePacksEnabled_PreservesExistingOrderAndCustomPacks()
    {
        var rpDir = Path.Combine(_testDir, "resourcepacks");
        Directory.CreateDirectory(rpDir);
        File.WriteAllBytes(Path.Combine(rpDir, "ExistingPack.zip"), new byte[] { 1 });
        File.WriteAllBytes(Path.Combine(rpDir, "NewPack.zip"), new byte[] { 2 });

        var optionsPath = Path.Combine(_testDir, "options.txt");
        var initialContent = new[]
        {
            "version:3465",
            "resourcePacks:[\"custom_internal\",\"vanilla\",\"file/ExistingPack.zip\"]",
            "incompatibleResourcePacks:[\"custom_incompatible\"]"
        };
        File.WriteAllLines(optionsPath, initialContent, new UTF8Encoding(false));

        FabricGameLaunchService.EnsureDefaultResourcePacksEnabled(_testDir);

        var lines = File.ReadAllLines(optionsPath, Encoding.UTF8);
        var rpLine = Assert.Single(lines, l => l.StartsWith("resourcePacks:"));
        var incompLine = Assert.Single(lines, l => l.StartsWith("incompatibleResourcePacks:"));

        var rpList = FabricGameLaunchService.ParseOptionsList(rpLine);
        var incompList = FabricGameLaunchService.ParseOptionsList(incompLine);

        // Порядок "custom_internal" -> "vanilla" -> "fabric" сохранён
        Assert.Equal("custom_internal", rpList[0]);
        Assert.Equal("vanilla", rpList[1]);
        Assert.Equal("fabric", rpList[2]);
        Assert.Equal("file/ExistingPack.zip", rpList[3]);
        Assert.Equal("file/NewPack.zip", rpList[4]);

        Assert.Contains("custom_incompatible", incompList);
        Assert.Contains("file/ExistingPack.zip", incompList);
        Assert.Contains("file/NewPack.zip", incompList);
    }

    [Fact]
    public void EnsureDefaultResourcePacksEnabled_IsIdempotent()
    {
        var rpDir = Path.Combine(_testDir, "resourcepacks");
        Directory.CreateDirectory(rpDir);
        File.WriteAllBytes(Path.Combine(rpDir, "PackA.zip"), new byte[] { 1 });

        FabricGameLaunchService.EnsureDefaultResourcePacksEnabled(_testDir);
        var linesAfterFirst = File.ReadAllLines(Path.Combine(_testDir, "options.txt"), Encoding.UTF8);

        FabricGameLaunchService.EnsureDefaultResourcePacksEnabled(_testDir);
        var linesAfterSecond = File.ReadAllLines(Path.Combine(_testDir, "options.txt"), Encoding.UTF8);

        Assert.Equal(linesAfterFirst, linesAfterSecond);
    }

    [Fact]
    public void EnsureDefaultResourcePacksEnabled_RemovesDroppedPacksAndCleansOptionsTxt()
    {
        var rpDir = Path.Combine(_testDir, "resourcepacks");
        Directory.CreateDirectory(rpDir);

        var droppedFile = Path.Combine(rpDir, "Transparent-GUI-White-Border.zip");
        File.WriteAllBytes(droppedFile, new byte[] { 1, 2, 3 });

        var remainingFile = Path.Combine(rpDir, "Universal Bushy Leaves.zip");
        File.WriteAllBytes(remainingFile, new byte[] { 4, 5, 6 });

        var optionsPath = Path.Combine(_testDir, "options.txt");
        var initialContent = new[]
        {
            "version:3465",
            "resourcePacks:[\"vanilla\",\"fabric\",\"file/Universal Bushy Leaves.zip\",\"file/Transparent-GUI-White-Border.zip\"]",
            "incompatibleResourcePacks:[\"file/Universal Bushy Leaves.zip\",\"file/Transparent-GUI-White-Border.zip\"]"
        };
        File.WriteAllLines(optionsPath, initialContent, new UTF8Encoding(false));

        FabricGameLaunchService.EnsureDefaultResourcePacksEnabled(_testDir);

        // 1. Файл Transparent-GUI-White-Border.zip должен быть физически удалён
        Assert.False(File.Exists(droppedFile));
        Assert.True(File.Exists(remainingFile));

        // 2. В options.txt он должен отсутствовать в обоих списках
        var lines = File.ReadAllLines(optionsPath, Encoding.UTF8);
        var rpLine = Assert.Single(lines, l => l.StartsWith("resourcePacks:"));
        var incompLine = Assert.Single(lines, l => l.StartsWith("incompatibleResourcePacks:"));

        var rpList = FabricGameLaunchService.ParseOptionsList(rpLine);
        var incompList = FabricGameLaunchService.ParseOptionsList(incompLine);

        Assert.DoesNotContain("file/Transparent-GUI-White-Border.zip", rpList);
        Assert.DoesNotContain("Transparent-GUI-White-Border.zip", rpList);
        Assert.DoesNotContain("file/Transparent-GUI-White-Border.zip", incompList);
        Assert.DoesNotContain("Transparent-GUI-White-Border.zip", incompList);

        Assert.Contains("file/Universal Bushy Leaves.zip", rpList);
        Assert.Contains("file/Universal Bushy Leaves.zip", incompList);
    }
}
