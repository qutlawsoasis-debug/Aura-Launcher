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

        if (targetView == null) return;

        // Единый контроллер навигации: «последний запрос побеждает».
        // 1. При новом переключении все идущие Storyboard ОСТАНАВЛИВАТЬ (Stop, не ждать Completed).
        if (_activeTransitionStoryboard != null)
        {
            _activeTransitionStoryboard.Stop();
            _activeTransitionStoryboard = null;
        }

        var allScreens = new FrameworkElement[] { ViewOverview, ViewLobby, ViewWardrobe, ViewSettings };

        // 2. Уходящему экрану сразу ставить Opacity 0 и Visibility=Collapsed.
        // Все неактивные экраны: Visibility=Collapsed, IsHitTestVisible=False.
        foreach (var screen in allScreens)
        {
            if (screen == null) continue;
            if (screen != targetView)
            {
                screen.BeginAnimation(UIElement.OpacityProperty, null);
                screen.Opacity = 0.0;
                screen.Visibility = Visibility.Collapsed;
                screen.IsHitTestVisible = false;
                if (screen.RenderTransform is TranslateTransform tt)
                {
                    tt.BeginAnimation(TranslateTransform.YProperty, null);
                    tt.Y = 0.0;
                }
            }
        }

        _currentActiveView = targetView;

        // 3. Одновременно виден и кликабелен ровно один экран
        targetView.Visibility = Visibility.Visible;
        targetView.IsHitTestVisible = true;

        if (!animate)
        {
            targetView.BeginAnimation(UIElement.OpacityProperty, null);
            targetView.Opacity = 1.0;
            if (targetView.RenderTransform is TranslateTransform tt)
            {
                tt.BeginAnimation(TranslateTransform.YProperty, null);
                tt.Y = 0.0;
            }
            return;
        }

        // Входящий анимировать с текущего состояния
        double currentOpacity = targetView.Opacity;
        if (currentOpacity < 0.0 || currentOpacity >= 1.0) currentOpacity = 0.0;

        double currentY = 8.0;
        if (targetView.RenderTransform is TranslateTransform inTrans)
        {
            currentY = inTrans.Y;
            if (currentY < 0.0 || currentY > 8.0) currentY = 8.0;
        }
        else
        {
            inTrans = new TranslateTransform(0, currentY);
            targetView.RenderTransform = inTrans;
        }

        var sb = new Storyboard();

        // Приходящий экран: Opacity с текущего состояния -> 1 за 220 мс
        var inOpacityAnim = new DoubleAnimation
        {
            From = currentOpacity,
            To = 1.0,
            Duration = TimeSpan.FromMilliseconds(220),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(inOpacityAnim, targetView);
        Storyboard.SetTargetProperty(inOpacityAnim, new PropertyPath(UIElement.OpacityProperty));
        sb.Children.Add(inOpacityAnim);

        // Приходящий экран: TranslateTransform.Y с текущего состояния -> 0 за 220 мс
        var inSlideAnim = new DoubleAnimation
        {
            From = currentY,
            To = 0.0,
            Duration = TimeSpan.FromMilliseconds(220),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(inSlideAnim, targetView);
        Storyboard.SetTargetProperty(inSlideAnim, new PropertyPath("(UIElement.RenderTransform).(TranslateTransform.Y)"));
        sb.Children.Add(inSlideAnim);

        sb.Completed += (s, e) =>
        {
            if (_activeTransitionStoryboard == sb)
            {
                targetView.Opacity = 1.0;
                if (targetView.RenderTransform is TranslateTransform finishedTrans)
                {
                    finishedTrans.Y = 0.0;
                }
                foreach (var screen in allScreens)
                {
                    if (screen != null && screen != targetView)
                    {
                        screen.Visibility = Visibility.Collapsed;
                        screen.IsHitTestVisible = false;
                        screen.Opacity = 0.0;
                    }
                }
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