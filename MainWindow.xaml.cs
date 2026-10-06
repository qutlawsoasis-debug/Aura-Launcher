using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using AuraLauncher.ViewModels;

namespace AuraLauncher;

/// <summary>
/// Code-behind главного окна для чистой анимации интерфейса (скользящее подчёркивание вкладок и параллакс).
/// Бизнес-логика остаётся строго в ViewModels.
/// </summary>
public partial class MainWindow : Window
{
    private bool _isLoaded;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = true;
        // Начальное позиционирование подчёркивания активной вкладки
        UpdateActiveTabUnderline(instant: true);
    }

    private void OnTabChecked(object sender, RoutedEventArgs e)
    {
        if (!_isLoaded) return;
        if (sender is RadioButton rb && rb.IsLoaded)
        {
            AnimateUnderline(rb, instant: false);
        }
    }

    private void UpdateActiveTabUnderline(bool instant)
    {
        RadioButton? active = TabOverview.IsChecked == true ? TabOverview :
                              TabWardrobe.IsChecked == true ? TabWardrobe :
                              TabLobby.IsChecked == true ? TabLobby :
                              TabSettings.IsChecked == true ? TabSettings : null;

        if (active != null)
        {
            AnimateUnderline(active, instant);
        }
    }

    private void AnimateUnderline(RadioButton targetTab, bool instant)
    {
        if (targetTab == null || NavTabsPanel == null || TabUnderline == null) return;

        try
        {
            var transform = targetTab.TransformToVisual(NavTabsPanel);
            var point = transform.Transform(new Point(0, 0));
            double targetX = point.X + 12; // отступ слева
            double targetWidth = Math.Max(16, targetTab.ActualWidth - 24);

            if (instant || targetTab.ActualWidth <= 0)
            {
                UnderlineTrans.X = targetX;
                TabUnderline.Width = targetWidth;
                return;
            }

            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var duration = TimeSpan.FromMilliseconds(220);

            var animX = new DoubleAnimation(targetX, duration) { EasingFunction = ease };
            var animW = new DoubleAnimation(targetWidth, duration) { EasingFunction = ease };

            UnderlineTrans.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, animX);
            TabUnderline.BeginAnimation(FrameworkElement.WidthProperty, animW);
        }
        catch
        {
            // fallback
        }
    }

    private void OnWindowMouseMove(object sender, MouseEventArgs e)
    {
        if (BgWorldTrans == null) return;

        if (DataContext is MainViewModel vm && vm.IsOverviewActive)
        {
            var pos = e.GetPosition(this);
            double width = Math.Max(1, ActualWidth);
            double height = Math.Max(1, ActualHeight);

            // Нормализация от -0.5 до +0.5
            double normX = (pos.X / width) - 0.5;
            double normY = (pos.Y / height) - 0.5;

            // Максимальное смещение: 8px (от -8px до +8px)
            double targetX = normX * 16.0;
            double targetY = normY * 16.0;

            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var animX = new DoubleAnimation(targetX, TimeSpan.FromMilliseconds(80)) { EasingFunction = ease };
            var animY = new DoubleAnimation(targetY, TimeSpan.FromMilliseconds(80)) { EasingFunction = ease };

            BgWorldTrans.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, animX);
            BgWorldTrans.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, animY);
        }
        else
        {
            if (BgWorldTrans.X != 0 || BgWorldTrans.Y != 0)
            {
                var animReset = new DoubleAnimation(0, TimeSpan.FromMilliseconds(150));
                BgWorldTrans.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, animReset);
                BgWorldTrans.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, animReset);
            }
        }
    }

    private System.Windows.Threading.DispatcherTimer? _sliderHideTimer;

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

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(180);

        var animWidth = new DoubleAnimation(open ? 82.0 : 0.0, duration) { EasingFunction = ease };
        var animOpacity = new DoubleAnimation(open ? 1.0 : 0.0, duration) { EasingFunction = ease };

        SliderBox.BeginAnimation(FrameworkElement.WidthProperty, animWidth);
        SliderBox.BeginAnimation(UIElement.OpacityProperty, animOpacity);
    }
}