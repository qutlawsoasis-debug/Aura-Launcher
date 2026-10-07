using System;
using System.Windows.Media.Imaging;
using AuraLauncher.Core;

namespace AuraLauncher.Models;

public class ScreenshotItem : ObservableObject
{
    public string FileName { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public DateTime CreatedDate { get; set; } = DateTime.MinValue;
    public string SizeFormatted { get; set; } = string.Empty;
    public BitmapSource? Thumbnail { get; set; }
}
