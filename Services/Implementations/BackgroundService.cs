using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Packaging;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AuraLauncher.Services.Interfaces;

namespace AuraLauncher.Services.Implementations;

public class BackgroundService : IBackgroundService
{
    static BackgroundService()
    {
        if (!UriParser.IsKnownScheme("pack"))
        {
            _ = PackUriHelper.UriSchemePack;
        }
    }

    private readonly IConfigService? _configService;
    private readonly List<Uri> _backgroundUris = new();
    private readonly Random _random = new();
    private DispatcherTimer? _rotationTimer;
    private int _currentIndex = -1;
    private bool _autoRotationEnabled = true;

    public event EventHandler<BitmapImage>? BackgroundChanged;
    public BitmapImage? CurrentImage { get; private set; }

    public bool AutoRotationEnabled
    {
        get => _configService != null ? _configService.CurrentConfig.AutoRotateBackgrounds : _autoRotationEnabled;
        set
        {
            _autoRotationEnabled = value;
            if (_configService != null && _configService.CurrentConfig.AutoRotateBackgrounds != value)
            {
                _configService.CurrentConfig.AutoRotateBackgrounds = value;
                _ = _configService.SaveConfigAsync(_configService.CurrentConfig);
            }
        }
    }

    public int TotalCount => _backgroundUris.Count;

    public BackgroundService(IConfigService? configService = null)
    {
        _configService = configService;
        if (_configService != null)
        {
            _autoRotationEnabled = _configService.CurrentConfig.AutoRotateBackgrounds;
            _configService.ConfigChanged += (s, cfg) =>
            {
                _autoRotationEnabled = cfg.AutoRotateBackgrounds;
            };
        }
    }

    public void Initialize()
    {
        if (_configService != null)
        {
            _autoRotationEnabled = _configService.CurrentConfig.AutoRotateBackgrounds;
        }
        _backgroundUris.Clear();

        // 1. Built-in 15 atmospheric Minecraft backgrounds
        for (int i = 1; i <= 15; i++)
        {
            var uri = new Uri($"pack://application:,,,/Resources/Backgrounds/bg_{i}.jpg", UriKind.Absolute);
            _backgroundUris.Add(uri);
        }

        // 2. Optional custom backgrounds directory in .aura/backgrounds
        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var customDir = Path.Combine(appData, ".aura", "backgrounds");
            if (Directory.Exists(customDir))
            {
                var files = Directory.GetFiles(customDir, "*.*");
                foreach (var file in files)
                {
                    var ext = Path.GetExtension(file).ToLowerInvariant();
                    if (ext is ".jpg" or ".jpeg" or ".png" or ".webp" or ".bmp")
                    {
                        _backgroundUris.Add(new Uri(file, UriKind.Absolute));
                    }
                }
            }
        }
        catch { }

        // 3. Pick initial random background
        if (_backgroundUris.Count > 0)
        {
            _currentIndex = _random.Next(_backgroundUris.Count);
            CurrentImage = LoadBitmap(_backgroundUris[_currentIndex]);
        }

        // 4. Start 60-second rotation timer on UI dispatcher
        if (_rotationTimer == null)
        {
            try
            {
                _rotationTimer = new DispatcherTimer(DispatcherPriority.Background)
                {
                    Interval = TimeSpan.FromSeconds(60)
                };
                _rotationTimer.Tick += (s, e) =>
                {
                    if (AutoRotationEnabled)
                    {
                        NextBackground();
                    }
                };
                _rotationTimer.Start();
            }
            catch { }
        }
    }

    public void NextBackground()
    {
        if (_backgroundUris.Count <= 1) return;

        int nextIndex;
        do
        {
            nextIndex = _random.Next(_backgroundUris.Count);
        } while (nextIndex == _currentIndex && _backgroundUris.Count > 1);

        _currentIndex = nextIndex;
        var bmp = LoadBitmap(_backgroundUris[_currentIndex]);
        if (bmp != null)
        {
            CurrentImage = bmp;
            BackgroundChanged?.Invoke(this, bmp);
        }
    }

    private static BitmapImage? LoadBitmap(Uri uri)
    {
        try
        {
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.UriSource = uri;
            bi.DecodePixelWidth = 1920;
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.EndInit();
            bi.Freeze();
            return bi;
        }
        catch
        {
            return null;
        }
    }
}
