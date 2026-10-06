using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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
    private FrameworkElement? _currentActiveView;
    private Storyboard? _activeTransitionStoryboard;

    public MainWindow()
    {
        InitializeComponent();
        _currentActiveView = ViewOverview;
        Loaded += MainWindow_Loaded;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.PropertyChanged += (s, args) =>
            {
                if (args.PropertyName == nameof(MainViewModel.CurrentTabName))
                {
                    TransitionToTab(vm.CurrentTabName, animate: true);
                    UpdateScreenOverlay(vm.IsOverviewActive);
                }
                else if (args.PropertyName == nameof(MainViewModel.IsOverviewActive))
                {
                    UpdateScreenOverlay(vm.IsOverviewActive);
                }
            };
            TransitionToTab(vm.CurrentTabName, animate: false);
            UpdateScreenOverlay(vm.IsOverviewActive);
        }
    }

    public void TransitionToTab(string tabName, bool animate = true)
    {
        FrameworkElement? targetView = tabName switch
        {
            "Lobby" => ViewLobby,
            "Wardrobe" => ViewWardrobe,
            "Settings" => ViewSettings,
            _ => ViewOverview
        };

        if (targetView == null || targetView == _currentActiveView) return;

        var outgoingView = _currentActiveView;
        _currentActiveView = targetView;

        _activeTransitionStoryboard?.Stop();
        _activeTransitionStoryboard = null;

        if (!animate || outgoingView == null)
        {
            if (outgoingView != null)
            {
                outgoingView.Visibility = Visibility.Collapsed;
                outgoingView.Opacity = 0.0;
            }
            targetView.Visibility = Visibility.Visible;
            targetView.Opacity = 1.0;
            if (targetView.RenderTransform is TranslateTransform tt)
            {
                tt.Y = 0.0;
            }
            return;
        }

        // Честный Storyboard перехода:
        // - Уходящий экран: Opacity 1 -> 0 за 100 мс.
        // - Приходящий экран: Opacity 0 -> 1 за 220 мс, TranslateTransform.Y 8 -> 0 за 220 мс (CubicEase EaseOut).
        // - Visibility приходящего включать ДО анимации, уходящего — скрывать (Collapsed) по Completed.
        // - Анимировать СТРОГО Opacity и RenderTransform (TranslateTransform).
        targetView.Visibility = Visibility.Visible;
        targetView.Opacity = 0.0;
        if (targetView.RenderTransform is TranslateTransform inTrans)
        {
            inTrans.Y = 8.0;
        }

        var sb = new Storyboard();

        // Уходящий: Opacity 1 -> 0 за 100 мс
        var outAnim = new DoubleAnimation
        {
            To = 0.0,
            Duration = TimeSpan.FromMilliseconds(100)
        };
        Storyboard.SetTarget(outAnim, outgoingView);
        Storyboard.SetTargetProperty(outAnim, new PropertyPath(UIElement.OpacityProperty));
        sb.Children.Add(outAnim);

        // Приходящий: Opacity 0 -> 1 за 220 мс (с задержкой 100 мс)
        var inOpacityAnim = new DoubleAnimation
        {
            From = 0.0,
            To = 1.0,
            Duration = TimeSpan.FromMilliseconds(220),
            BeginTime = TimeSpan.FromMilliseconds(100),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(inOpacityAnim, targetView);
        Storyboard.SetTargetProperty(inOpacityAnim, new PropertyPath(UIElement.OpacityProperty));
        sb.Children.Add(inOpacityAnim);

        // Приходящий: TranslateTransform.Y 8 -> 0 за 220 мс (с задержкой 100 мс)
        var inSlideAnim = new DoubleAnimation
        {
            From = 8.0,
            To = 0.0,
            Duration = TimeSpan.FromMilliseconds(220),
            BeginTime = TimeSpan.FromMilliseconds(100),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(inSlideAnim, targetView);
        Storyboard.SetTargetProperty(inSlideAnim, new PropertyPath("(UIElement.RenderTransform).(TranslateTransform.Y)"));
        sb.Children.Add(inSlideAnim);

        sb.Completed += (s, e) =>
        {
            outgoingView.Visibility = Visibility.Collapsed;
            outgoingView.Opacity = 0.0;
            targetView.Opacity = 1.0;
            if (targetView.RenderTransform is TranslateTransform finishedTrans)
            {
                finishedTrans.Y = 0.0;
            }
            if (_activeTransitionStoryboard == sb)
            {
                _activeTransitionStoryboard = null;
            }
        };

        _activeTransitionStoryboard = sb;
        sb.Begin();
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