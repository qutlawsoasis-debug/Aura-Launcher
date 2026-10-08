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
            Assert.True(loadlist.Count > 0);

            var first = loadlist[0] as JsonObject;
            Assert.NotNull(first);
            Assert.Equal("AuraLobby", first["name"]?.GetValue<string>());
            Assert.Equal("CustomSkinAPI", first["type"]?.GetValue<string>());
            Assert.Equal("https://lobby-api.vercel.app/csl/", first["root"]?.GetValue<string>());
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }
}
