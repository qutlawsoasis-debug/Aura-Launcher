using System;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AuraLauncher.Services.Implementations;
using Xunit;

namespace AuraLauncher.Tests;

public class SkinServiceTests
{
    [Fact]
    [System.STAThread]
    public void ExtractHeadAvatarDirect_ValidSkin_ProducesHeadWithOverlay()
    {
        // Создаем тестовый 64x64 битмап
        // Базовая голова (8,8..15,15) красится красным
        // Слой шляпы (40,8..47,15) красится полупрозрачным синим поверх
        int stride = 64 * 4;
        byte[] pixels = new byte[64 * stride];

        // Базовая голова: красный цвет (B=0, G=0, R=255, A=255)
        for (int y = 8; y < 16; y++)
        {
            for (int x = 8; x < 16; x++)
            {
                int idx = (y * 64 + x) * 4;
                pixels[idx + 0] = 0;   // B
                pixels[idx + 1] = 0;   // G
                pixels[idx + 2] = 255; // R
                pixels[idx + 3] = 255; // A
            }
        }

        // Шляпа: зеленый оверлей на пикселе (0,0) головы (B=0, G=255, R=0, A=255)
        int hatIdx = (8 * 64 + 40) * 4;
        pixels[hatIdx + 0] = 0;
        pixels[hatIdx + 1] = 255;
        pixels[hatIdx + 2] = 0;
        pixels[hatIdx + 3] = 255;

        var sourceBmp = BitmapSource.Create(64, 64, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        var headAvatar = SkinService.ExtractHeadAvatarDirect(sourceBmp);

        Assert.NotNull(headAvatar);
        Assert.Equal(8, headAvatar.PixelWidth);
        Assert.Equal(8, headAvatar.PixelHeight);

        byte[] headPixels = new byte[8 * 8 * 4];
        headAvatar.CopyPixels(headPixels, 8 * 4, 0);

        // Пиксель (0,0) должен быть зеленым от слоя шляпы
        Assert.Equal(0, headPixels[0]);   // B
        Assert.Equal(255, headPixels[1]); // G
        Assert.Equal(0, headPixels[2]);   // R
        Assert.Equal(255, headPixels[3]); // A

        // Пиксель (1,0) должен остаться красным от базовой головы (шляпа там прозрачная)
        Assert.Equal(0, headPixels[4]);   // B
        Assert.Equal(0, headPixels[5]);   // G
        Assert.Equal(255, headPixels[6]); // R
        Assert.Equal(255, headPixels[7]); // A
    }

    [Fact]
    [System.STAThread]
    public void LoadDefaultSteveHead_ReturnsValidFrozenHead()
    {
        var head = SkinService.LoadDefaultSteveHead();
        Assert.NotNull(head);
        Assert.True(SkinService.IsDefaultSteveHead(head));
    }

    [Fact]
    public void EnsureCustomSkinLoaderConfig_WritesAuraLobbyAtTopAndBypassesCache()
    {
        var skinService = new SkinService();
        var tempDir = Path.Combine(Path.GetTempPath(), "AuraSkinTest_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempDir);
            skinService.EnsureCustomSkinLoaderConfig(tempDir);

            var cslJsonPath = Path.Combine(tempDir, "CustomSkinLoader", "CustomSkinLoader.json");
            Assert.True(File.Exists(cslJsonPath));

            var jsonText = File.ReadAllText(cslJsonPath);
            var node = JsonNode.Parse(jsonText) as JsonObject;
            Assert.NotNull(node);

            Assert.True(node["forceDisableCache"]?.GetValue<bool>());
            Assert.Equal(0, node["cacheExpiry"]?.GetValue<int>());
            Assert.True(node["enableCacheAutoClean"]?.GetValue<bool>());

            var loadlist = node["loadlist"] as JsonArray;
            Assert.NotNull(loadlist);
            Assert.Equal(2, loadlist.Count);

            var first = loadlist[0] as JsonObject;
            Assert.NotNull(first);
            Assert.Equal("AuraLobby", first["name"]?.GetValue<string>());
            Assert.Equal("CustomSkinAPI", first["type"]?.GetValue<string>());
            Assert.Equal("https://lobby-api.vercel.app/csl/", first["root"]?.GetValue<string>());

            var second = loadlist[1] as JsonObject;
            Assert.NotNull(second);
            Assert.Equal("LocalSkin", second["name"]?.GetValue<string>());
            Assert.Equal("Legacy", second["type"]?.GetValue<string>());
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    [System.STAThread]
    public void ValidateCapeFile_Valid64x32Cape_ReturnsSuccess()
    {
        var skinService = new SkinService();
        var tempFile = Path.Combine(Path.GetTempPath(), $"aura_test_cape_{Guid.NewGuid():N}.png");
        try
        {
            // Создаем тестовый PNG 64x32
            var bmp = BitmapSource.Create(64, 32, 96, 96, PixelFormats.Bgra32, null, new byte[64 * 32 * 4], 64 * 4);
            using (var fs = File.Create(tempFile))
            {
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(bmp));
                enc.Save(fs);
            }

            var res = skinService.ValidateCapeFile(tempFile);
            Assert.True(res.IsValid);
            Assert.Equal(64, res.Width);
            Assert.Equal(32, res.Height);
        }
        finally
        {
            try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
        }
    }

    [Fact]
    [System.STAThread]
    public void ValidateCapeFile_InvalidDimensions_ReturnsError()
    {
        var skinService = new SkinService();
        var tempFile = Path.Combine(Path.GetTempPath(), $"aura_test_cape_invalid_{Guid.NewGuid():N}.png");
        try
        {
            // Создаем тестовый PNG 100x100 (недопустимый)
            var bmp = BitmapSource.Create(100, 100, 96, 96, PixelFormats.Bgra32, null, new byte[100 * 100 * 4], 100 * 4);
            using (var fs = File.Create(tempFile))
            {
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(bmp));
                enc.Save(fs);
            }

            var res = skinService.ValidateCapeFile(tempFile);
            Assert.False(res.IsValid);
            Assert.Contains("Неподдерживаемый размер", res.ErrorMessage);
        }
        finally
        {
            try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
        }
    }

    [Theory]
    [InlineData(64, 32)]
    [InlineData(128, 64)]
    [InlineData(256, 128)]
    [InlineData(512, 256)]
    [InlineData(1024, 512)]
    [InlineData(22, 17)]
    [InlineData(64, 64)]
    [System.STAThread]
    public void ValidateCapeBytes_SupportedDimensions_ReturnsSuccess(int width, int height)
    {
        var skinService = new SkinService();
        var bmp = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, new byte[width * height * 4], width * 4);
        using var ms = new MemoryStream();
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        enc.Save(ms);
        byte[] bytes = ms.ToArray();

        var res = skinService.ValidateCapeBytes(bytes);
        Assert.True(res.IsValid);
        Assert.Equal(width, res.Width);
        Assert.Equal(height, res.Height);
    }

    [Theory]
    [InlineData(1025, 512)]
    [InlineData(1024, 513)]
    [InlineData(2048, 1024)]
    [InlineData(100, 100)]
    [InlineData(500, 250)]
    [System.STAThread]
    public void ValidateCapeBytes_UnsupportedDimensions_ReturnsError(int width, int height)
    {
        var skinService = new SkinService();
        var bmp = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, new byte[width * height * 4], width * 4);
        using var ms = new MemoryStream();
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        enc.Save(ms);
        byte[] bytes = ms.ToArray();

        var res = skinService.ValidateCapeBytes(bytes);
        Assert.False(res.IsValid);
        Assert.Contains("Неподдерживаемый размер", res.ErrorMessage);
    }

    [Fact]
    [System.STAThread]
    public void ValidateCapeFile_Synthetic1024x512HDCape_ValidatesSuccessfully()
    {
        var skinService = new SkinService();
        var tempFile = Path.Combine(Path.GetTempPath(), $"aura_test_cape_hd1024_{Guid.NewGuid():N}.png");
        try
        {
            var bmp = BitmapSource.Create(1024, 512, 96, 96, PixelFormats.Bgra32, null, new byte[1024 * 512 * 4], 1024 * 4);
            using (var fs = File.Create(tempFile))
            {
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(bmp));
                enc.Save(fs);
            }

            var res = skinService.ValidateCapeFile(tempFile);
            Assert.True(res.IsValid);
            Assert.Equal(1024, res.Width);
            Assert.Equal(512, res.Height);
            Assert.Null(res.ErrorMessage);
        }
        finally
        {
            try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
        }
    }

    [Fact]
    [System.STAThread]
    public async Task SaveToManagedStorageAsync_SameContent_ProducesSamePathAndHash()
    {
        var customTempDir = Path.Combine(Path.GetTempPath(), "AuraProfileTest_" + Guid.NewGuid().ToString("N"));
        var skinService = new SkinService(null, customTempDir);

        var bmp = BitmapSource.Create(64, 32, 96, 96, PixelFormats.Bgra32, null, new byte[64 * 32 * 4], 64 * 4);
        byte[] testBytes;
        using (var ms = new MemoryStream())
        {
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            enc.Save(ms);
            testBytes = ms.ToArray();
        }

        var (path1, hash1) = await skinService.SaveToManagedStorageAsync(testBytes, "cape", "test_cape.png");
        var (path2, hash2) = await skinService.SaveToManagedStorageAsync(testBytes, "cape", "another_name.png");

        Assert.Equal(hash1, hash2);
        Assert.Equal(path1, path2);
        Assert.True(File.Exists(path1));
        Assert.StartsWith(Path.GetFullPath(customTempDir), Path.GetFullPath(path1));

        try { Directory.Delete(customTempDir, recursive: true); } catch { }
    }

    [Fact]
    public async Task SyncCapeToGameAsync_SyncsAndRemovesLocalSkinFile()
    {
        var skinService = new SkinService();
        var tempGameDir = Path.Combine(Path.GetTempPath(), "AuraGameTest_" + Guid.NewGuid().ToString("N"));
        var tempCapeFile = Path.Combine(Path.GetTempPath(), $"aura_cape_{Guid.NewGuid():N}.png");
        try
        {
            await File.WriteAllBytesAsync(tempCapeFile, new byte[] { 1, 2, 3 });

            await skinService.SyncCapeToGameAsync(tempCapeFile, "TestNick", tempGameDir);

            var expectedTarget = Path.Combine(tempGameDir, "CustomSkinLoader", "LocalSkin", "capes", "TestNick.png");
            Assert.True(File.Exists(expectedTarget));

            // Проверяем удаление при сбросе плаща
            await skinService.SyncCapeToGameAsync(null, "TestNick", tempGameDir);
            Assert.False(File.Exists(expectedTarget));
        }
        finally
        {
            try { Directory.Delete(tempGameDir, recursive: true); } catch { }
            try { if (File.Exists(tempCapeFile)) File.Delete(tempCapeFile); } catch { }
        }
    }

    [Fact]
    public async Task SyncCapeToGameAsync_MultiInstanceSync_SyncsBothInstances()
    {
        var skinService = new SkinService();
        var rootGameDir = Path.Combine(Path.GetTempPath(), "AuraMultiInstTest_" + Guid.NewGuid().ToString("N"));
        var inst1201 = Path.Combine(rootGameDir, "instances", "1.20.1");
        var inst1211 = Path.Combine(rootGameDir, "instances", "1.21.1");
        Directory.CreateDirectory(inst1201);
        Directory.CreateDirectory(inst1211);

        var tempCapeFile = Path.Combine(Path.GetTempPath(), $"aura_cape_{Guid.NewGuid():N}.png");
        try
        {
            await File.WriteAllBytesAsync(tempCapeFile, new byte[] { 0xAA, 0xBB });

            await skinService.SyncCapeToGameAsync(tempCapeFile, "PlayerOne", rootGameDir);

            var file1201 = Path.Combine(inst1201, "CustomSkinLoader", "LocalSkin", "capes", "PlayerOne.png");
            var file1211 = Path.Combine(inst1211, "CustomSkinLoader", "LocalSkin", "capes", "PlayerOne.png");
            Assert.True(File.Exists(file1201));
            Assert.True(File.Exists(file1211));
        }
        finally
        {
            try { Directory.Delete(rootGameDir, recursive: true); } catch { }
            try { if (File.Exists(tempCapeFile)) File.Delete(tempCapeFile); } catch { }
        }
    }

    [Fact]
    public async Task SyncCapeToGameAsync_InvalidNickname_ThrowsArgumentException()
    {
        var skinService = new SkinService();
        var tempGameDir = Path.Combine(Path.GetTempPath(), "AuraGameTest_" + Guid.NewGuid().ToString("N"));
        try
        {
            await Assert.ThrowsAsync<ArgumentException>(() =>
                skinService.SyncCapeToGameAsync(null, "../evil_nick", tempGameDir));

            await Assert.ThrowsAsync<ArgumentException>(() =>
                skinService.SyncCapeToGameAsync(null, "bad/slash", tempGameDir));
        }
        finally
        {
            try { Directory.Delete(tempGameDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void TryResolveWardrobeItemPath_MaliciousRelativePath_IsRejected()
    {
        var customTempDir = Path.Combine(Path.GetTempPath(), "AuraProfileSecTest_" + Guid.NewGuid().ToString("N"));
        var skinService = new SkinService(null, customTempDir);

        try
        {
            var item = new AuraLauncher.Models.WardrobeItem
            {
                Id = "cape_invalid",
                Type = "cape",
                RelativePath = @"..\..\..\Windows\System32\drivers\etc\hosts"
            };

            bool resolved = skinService.TryResolveWardrobeItemPath(item, out string fullPath);
            Assert.False(resolved);
            Assert.Empty(fullPath);
        }
        finally
        {
            try { Directory.Delete(customTempDir, recursive: true); } catch { }
        }
    }
}
