using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace AuraLauncher.Views;

public partial class UpdateWindow : Window
{
    private DispatcherTimer? _stepTimer;
    private int _stepIndex = 0;
    private readonly Rectangle[] _dots;

    public UpdateWindow()
    {
        InitializeComponent();
        _dots = new[] { Dot0, Dot1, Dot2, Dot3, Dot4, Dot5, Dot6, Dot7 };

        Loaded += UpdateWindow_Loaded;
        Unloaded += UpdateWindow_Unloaded;
    }

    private void UpdateWindow_Loaded(object sender, RoutedEventArgs e)
    {
        StartSteppingAnimation();
    }

    private void UpdateWindow_Unloaded(object sender, RoutedEventArgs e)
    {
        StopSteppingAnimation();
    }

    public void StartSteppingAnimation()
    {
        StopSteppingAnimation();

        // 8 кадров за 1.2 с -> 1200 / 8 = 150 мс на шаг
        _stepTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(150)
        };
        _stepTimer.Tick += (s, e) =>
        {
            _stepIndex = (_stepIndex + 1) % 8;
            UpdateDotsBrightness(_stepIndex);
        };
        _stepTimer.Start();
        UpdateDotsBrightness(_stepIndex);
    }

    public void StopSteppingAnimation()
    {
        _stepTimer?.Stop();
        _stepTimer = null;
    }

    private void UpdateDotsBrightness(int activeHead)
    {
        // 8 квадратов по кругу: ступенчатая бегущая яркость в стиле блоков
        // activeHead = 1.0 (самый яркий), хвост затухает ступенчато
        for (int i = 0; i < 8; i++)
        {
            int dist = (activeHead - i + 8) % 8;
            double opacity = dist switch
            {
                0 => 1.0,
                1 => 0.75,
                2 => 0.50,
                3 => 0.30,
                _ => 0.12
            };
            _dots[i].Opacity = opacity;
        }
    }

    private void Border_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            try
            {
                DragMove();
            }
            catch { }
        }
    }
}
