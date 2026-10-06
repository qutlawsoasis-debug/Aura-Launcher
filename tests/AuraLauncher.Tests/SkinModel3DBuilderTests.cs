using System.Windows.Media;
using System.Windows.Media.Imaging;
using AuraLauncher.Core;
using Xunit;

namespace AuraLauncher.Tests;

public class SkinModel3DBuilderTests
{
    [Fact]
    [System.STAThread]
    public void BuildPlayerModel_64x64_Regular_BuildsValidModel()
    {
        // 64x64 bitmap
        var bmp = new WriteableBitmap(64, 64, 96, 96, PixelFormats.Bgra32, null);
        var model = SkinModel3DBuilder.BuildPlayerModel(bmp, isSlim: false);

        Assert.NotNull(model);
        Assert.True(model.Children.Count >= 2); // lights + base + overlay
    }

    [Fact]
    [System.STAThread]
    public void BuildPlayerModel_64x64_Slim_BuildsValidModel()
    {
        var bmp = new WriteableBitmap(64, 64, 96, 96, PixelFormats.Bgra32, null);
        var model = SkinModel3DBuilder.BuildPlayerModel(bmp, isSlim: true);

        Assert.NotNull(model);
        Assert.True(model.Children.Count >= 2);
    }

    [Fact]
    [System.STAThread]
    public void BuildPlayerModel_RealSkinFile_BuildsValidModel()
    {
        var skinService = new AuraLauncher.Services.Implementations.SkinService();
        string testPath = @"C:\Users\magne\Downloads\d2dcfefe4f651f38.png";
        if (System.IO.File.Exists(testPath))
        {
            var bmp = skinService.LoadSkinImage(testPath) as BitmapSource;
            Assert.NotNull(bmp);
            var model = SkinModel3DBuilder.BuildPlayerModel(bmp, isSlim: false);
            Assert.NotNull(model);
        }
    }
}
