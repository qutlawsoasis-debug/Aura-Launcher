using System;
using System.Windows.Media.Imaging;

namespace AuraLauncher.Services.Interfaces;

public interface IBackgroundService
{
    event EventHandler<BitmapImage>? BackgroundChanged;
    BitmapImage? CurrentImage { get; }
    void Initialize();
    void NextBackground();
    bool AutoRotationEnabled { get; set; }
    int TotalCount { get; }
}
