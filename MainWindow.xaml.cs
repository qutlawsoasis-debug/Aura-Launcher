using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using AuraLauncher.ViewModels;

namespace AuraLauncher;

/// <summary>
/// Code-behind главного окна для анимации складного сайдбара и регулятора громкости.
/// Параллакс полностью устранён (фон статичен).
/// Бизнес-логика строго в ViewModels.
/// </summary>
public partial class MainWindow : Window
{
    private System.Windows.Threading.DispatcherTimer? _sliderHideTimer;

    public MainWindow()
    {
        InitializeComponent();
    }

    private void Sidebar_MouseEnter(object sender, MouseEventArgs e)
    {
        AnimateSidebar(expanded: true);
    }

    private void Sidebar_MouseLeave(object sender, MouseEventArgs e)
    {
        AnimateSidebar(expanded: false);
    }

    public void AnimateSidebar(bool expanded)
    {
        if (SidebarBorder == null) return;
        double targetWidth = expanded ? 200.0 : 72.0;

        var anim = new DoubleAnimation(targetWidth, TimeSpan.FromMilliseconds(200))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        SidebarBorder.BeginAnimation(FrameworkElement.WidthProperty, anim);
    }

    private void SoundControl_MouseEnter(object sender, MouseEventArgs e)
    {
        _sliderHideTimer?.Stop();
        AnimateSlider(open: true);
    }

    private void SoundControl_MouseLeave(object sender, MouseEventArgs e)
    {
        _sliderHideTimer?.Stop();
        _sliderHideTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(400)
        };
        _sliderHideTimer.Tick += (s, args) =>
        {
            _sliderHideTimer.Stop();
            if (SoundControlRoot != null && !SoundControlRoot.IsMouseOver)
            {
                AnimateSlider(open: false);
            }
        };
        _sliderHideTimer.Start();
    }

    private void AnimateSlider(bool open)
    {
        if (SliderBox == null) return;

        double targetWidth = open ? 82.0 : 0.0;
        double targetOpacity = open ? 1.0 : 0.0;
        var duration = TimeSpan.FromMilliseconds(180);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        var animW = new DoubleAnimation(targetWidth, duration) { EasingFunction = ease };
        var animO = new DoubleAnimation(targetOpacity, duration) { EasingFunction = ease };

        SliderBox.BeginAnimation(FrameworkElement.WidthProperty, animW);
        SliderBox.BeginAnimation(UIElement.OpacityProperty, animO);
    }
}