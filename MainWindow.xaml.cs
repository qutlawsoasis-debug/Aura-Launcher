using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using AuraLauncher.ViewModels;

namespace AuraLauncher;

/// <summary>
/// Code-behind главного окна.
/// Меню (Oswald 40px, 8px сдвиг, затемнение остальных пунктов до 0.42).
/// Регулятор громкости выезжает влево на 112px.
/// Затемнение других экранов rgba(6,14,18,0.66) 300ms fade-in.
/// Контентная область неподвижна при наведении меню.
/// </summary>
public partial class MainWindow : Window
{
    private System.Windows.Threading.DispatcherTimer? _sliderHideTimer;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.PropertyChanged += (s, args) =>
            {
                if (args.PropertyName == nameof(MainViewModel.CurrentTabName) ||
                    args.PropertyName == nameof(MainViewModel.IsOverviewActive))
                {
                    UpdateScreenOverlay(vm.IsOverviewActive);
                }
            };
            UpdateScreenOverlay(vm.IsOverviewActive);
        }
    }

    private void UpdateScreenOverlay(bool isOverview)
    {
        if (OtherScreensOverlay == null) return;

        double targetOpacity = isOverview ? 0.0 : 1.0;
        var anim = new DoubleAnimation(targetOpacity, TimeSpan.FromMilliseconds(300))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        OtherScreensOverlay.BeginAnimation(UIElement.OpacityProperty, anim);
    }

    private void MenuItem_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement hoveredItem)
        {
            AnimateMenuDimming(hoveredItem, isHovered: true);
        }
    }

    private void MenuItem_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement hoveredItem)
        {
            AnimateMenuDimming(hoveredItem, isHovered: false);
        }
    }

    /// <summary>
    /// При наведении на пункт меню: остальные слова опускаются до opacity 0.42.
    /// Контент не сдвигается.
    /// </summary>
    public void AnimateMenuDimming(FrameworkElement activeItem, bool isHovered)
    {
        var items = new[] { MenuBtnPlay, MenuBtnLobby, MenuBtnSkin, MenuBtnSettings };
        var duration = TimeSpan.FromMilliseconds(160);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        foreach (var item in items)
        {
            if (item == null) continue;
            double targetOpacity = 1.0;
            if (isHovered && item != activeItem)
            {
                targetOpacity = 0.42;
            }

            var anim = new DoubleAnimation(targetOpacity, duration) { EasingFunction = ease };
            item.BeginAnimation(UIElement.OpacityProperty, anim);
        }
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

    public void AnimateSlider(bool open)
    {
        if (SliderBox == null) return;

        double targetWidth = open ? 120.0 : 0.0;
        double targetOpacity = open ? 1.0 : 0.0;
        var duration = TimeSpan.FromMilliseconds(180);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        var animW = new DoubleAnimation(targetWidth, duration) { EasingFunction = ease };
        var animO = new DoubleAnimation(targetOpacity, duration) { EasingFunction = ease };

        SliderBox.BeginAnimation(FrameworkElement.WidthProperty, animW);
        SliderBox.BeginAnimation(UIElement.OpacityProperty, animO);
    }
}