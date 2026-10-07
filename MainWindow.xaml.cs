using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AuraLauncher.Services.Interfaces;
using AuraLauncher.ViewModels;
using Hardcodet.Wpf.TaskbarNotification;
using Microsoft.Extensions.DependencyInjection;

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
    private TaskbarIcon? _trayIcon;

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
            SetupTrayIcon();

            vm.GameStarted += (s, proc) =>
            {
                Dispatcher.Invoke(() =>
                {
                    var cfgService = App.Services?.GetService<IConfigService>();
                    bool hideLauncher = cfgService?.CurrentConfig.HideLauncherWhilePlaying ?? true;
                    if (hideLauncher)
                    {
                        HideToTray();
                    }
                });
            };

            vm.GameExited += (s, exitCode) =>
            {
                Dispatcher.Invoke(() =>
                {
                    RestoreFromTray();
                });
            };

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
                else if (args.PropertyName == nameof(MainViewModel.IsUpdateBannerVisible))
                {
                    if (vm.IsUpdateBannerVisible)
                    {
                        AnimateToastEntrance();
                    }
                }
                else if (args.PropertyName == nameof(MainViewModel.IsInviteToastVisible))
                {
                    if (vm.IsInviteToastVisible)
                    {
                        AnimateInviteToastEntrance();
                    }
                }
                else if (args.PropertyName == nameof(MainViewModel.IsChangelogModalVisible))
                {
                    AnimateChangelogModal(vm.IsChangelogModalVisible);
                }
            };
            TransitionToTab(vm.CurrentTabName, animate: false);
            UpdateScreenOverlay(vm.IsOverviewActive);
            if (vm.IsUpdateBannerVisible)
            {
                AnimateToastEntrance();
            }
            if (vm.IsInviteToastVisible)
            {
                AnimateInviteToastEntrance();
            }
            if (vm.IsChangelogModalVisible)
            {
                AnimateChangelogModal(true);
            }

            var bgService = App.Services?.GetService<IBackgroundService>();
            if (bgService != null)
            {
                bgService.BackgroundChanged += (s, nextImg) =>
                {
                    Dispatcher.Invoke(() => AnimateBackgroundTransition(nextImg));
                };
                if (bgService.CurrentImage != null)
                {
                    BgImageCurrent.Source = bgService.CurrentImage;
                }
            }
        }
    }

    public void AnimateInviteToastEntrance()
    {
        if (InviteToastBorder == null || InviteToastTransform == null) return;

        InviteToastBorder.Opacity = 0.0;
        InviteToastTransform.X = 16.0;

        var sb = new Storyboard();
        var fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(240))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(fadeIn, InviteToastBorder);
        Storyboard.SetTargetProperty(fadeIn, new PropertyPath(UIElement.OpacityProperty));
        sb.Children.Add(fadeIn);

        var slideIn = new DoubleAnimation(16.0, 0.0, TimeSpan.FromMilliseconds(240))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(slideIn, InviteToastTransform);
        Storyboard.SetTargetProperty(slideIn, new PropertyPath(TranslateTransform.XProperty));
        sb.Children.Add(slideIn);

        sb.Begin();
    }

    public void AnimateToastEntrance()
    {
        if (UpdateToastBorder == null || ToastTransform == null) return;

        UpdateToastBorder.Opacity = 0.0;
        ToastTransform.X = 16.0;

        var sb = new Storyboard();
        var fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(240))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(fadeIn, UpdateToastBorder);
        Storyboard.SetTargetProperty(fadeIn, new PropertyPath(UIElement.OpacityProperty));
        sb.Children.Add(fadeIn);

        var slideIn = new DoubleAnimation(16.0, 0.0, TimeSpan.FromMilliseconds(240))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(slideIn, ToastTransform);
        Storyboard.SetTargetProperty(slideIn, new PropertyPath(TranslateTransform.XProperty));
        sb.Children.Add(slideIn);

        sb.Begin();
    }

    public void TransitionToTab(string tabName, bool animate = true)
    {
        FrameworkElement? targetView = tabName switch
        {
            "Lobby" => ViewLobby,
            "Friends" => ViewFriends,
            "Workshop" => ViewWorkshop,
            "Wardrobe" => ViewWardrobe,
            "Settings" => ViewSettings,
            _ => ViewOverview
        };

        if (targetView == null) return;

        // 1. Плавное скольжение оранжевой точки меню
        int targetIndex = tabName switch
        {
            "Lobby" => 1,
            "Friends" => 2,
            "Workshop" => 3,
            "Wardrobe" => 4,
            "Settings" => 5,
            _ => 0
        };
        double targetDotY = targetIndex * 46.0;

        if (NavIndicatorTrans != null)
        {
            if (!animate)
            {
                NavIndicatorTrans.BeginAnimation(TranslateTransform.YProperty, null);
                NavIndicatorTrans.Y = targetDotY;
            }
            else
            {
                double currentDotY = NavIndicatorTrans.Y;
                var dotAnim = new DoubleAnimation
                {
                    From = currentDotY,
                    To = targetDotY,
                    Duration = TimeSpan.FromMilliseconds(240),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                NavIndicatorTrans.BeginAnimation(TranslateTransform.YProperty, dotAnim, HandoffBehavior.SnapshotAndReplace);
            }
        }

        // Единый контроллер навигации: «последний запрос побеждает».
        if (_activeTransitionStoryboard != null)
        {
            _activeTransitionStoryboard.Stop();
            _activeTransitionStoryboard = null;
        }

        var allScreens = new FrameworkElement[] { ViewOverview, ViewLobby, ViewFriends, ViewWorkshop, ViewWardrobe, ViewSettings };
        var outgoingView = _currentActiveView;
        _currentActiveView = targetView;

        if (!animate || outgoingView == null || outgoingView == targetView)
        {
            foreach (var screen in allScreens)
            {
                if (screen == null) continue;
                if (screen != targetView)
                {
                    screen.BeginAnimation(UIElement.OpacityProperty, null);
                    screen.Opacity = 0.0;
                    screen.Visibility = Visibility.Collapsed;
                    screen.IsHitTestVisible = false;
                }
            }
            targetView.Visibility = Visibility.Visible;
            targetView.IsHitTestVisible = true;
            targetView.BeginAnimation(UIElement.OpacityProperty, null);
            targetView.Opacity = 1.0;
            if (targetView.RenderTransform is TranslateTransform tt)
            {
                tt.BeginAnimation(TranslateTransform.YProperty, null);
                tt.Y = 0.0;
            }
            return;
        }

        // 2. Бесшовный кроссфейд страниц без провалов и резких переключений:
        // Уходящий экран плавно растворяется (180 мс), не пропадая мгновенно
        outgoingView.IsHitTestVisible = false;
        var outFade = new DoubleAnimation
        {
            From = outgoingView.Opacity > 0.0 ? outgoingView.Opacity : 1.0,
            To = 0.0,
            Duration = TimeSpan.FromMilliseconds(180),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        outgoingView.BeginAnimation(UIElement.OpacityProperty, outFade);

        // Приходящий экран одновременно плавно проявляется (220 мс) со сдвигом на 6px
        targetView.Visibility = Visibility.Visible;
        targetView.IsHitTestVisible = true;

        double currentY = 6.0;
        if (targetView.RenderTransform is TranslateTransform inTrans)
        {
            currentY = inTrans.Y;
            if (currentY < 0.0 || currentY > 6.0) currentY = 6.0;
        }
        else
        {
            inTrans = new TranslateTransform(0, currentY);
            targetView.RenderTransform = inTrans;
        }

        var sb = new Storyboard();

        var inOpacityAnim = new DoubleAnimation
        {
            From = 0.0,
            To = 1.0,
            Duration = TimeSpan.FromMilliseconds(220),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(inOpacityAnim, targetView);
        Storyboard.SetTargetProperty(inOpacityAnim, new PropertyPath(UIElement.OpacityProperty));
        sb.Children.Add(inOpacityAnim);

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

    private void AnimateChangelogModal(bool isVisible)
    {
        if (ChangelogModalOverlay == null || ChangelogModalTrans == null) return;

        ChangelogModalOverlay.BeginAnimation(UIElement.OpacityProperty, null);
        ChangelogModalTrans.BeginAnimation(TranslateTransform.YProperty, null);

        if (isVisible)
        {
            ChangelogModalOverlay.Visibility = Visibility.Visible;
            ChangelogModalOverlay.IsHitTestVisible = true;

            var fadeAnim = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            var slideAnim = new DoubleAnimation(12.0, 0.0, TimeSpan.FromMilliseconds(240))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            ChangelogModalOverlay.BeginAnimation(UIElement.OpacityProperty, fadeAnim);
            ChangelogModalTrans.BeginAnimation(TranslateTransform.YProperty, slideAnim);
        }
        else
        {
            ChangelogModalOverlay.IsHitTestVisible = false;

            var fadeAnim = new DoubleAnimation(ChangelogModalOverlay.Opacity, 0.0, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            var slideAnim = new DoubleAnimation(ChangelogModalTrans.Y, 10.0, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };

            fadeAnim.Completed += (s, e) =>
            {
                if (!(DataContext is MainViewModel vm && vm.IsChangelogModalVisible))
                {
                    ChangelogModalOverlay.Visibility = Visibility.Collapsed;
                    ChangelogModalOverlay.Opacity = 0.0;
                }
            };

            ChangelogModalOverlay.BeginAnimation(UIElement.OpacityProperty, fadeAnim);
            ChangelogModalTrans.BeginAnimation(TranslateTransform.YProperty, slideAnim);
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
        var items = new[] { MenuBtnPlay, MenuBtnLobby, MenuBtnFriends, MenuBtnWorkshop, MenuBtnSkin, MenuBtnSettings };
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

        double targetWidth = open ? 190.0 : 0.0;
        double targetOpacity = open ? 1.0 : 0.0;
        var duration = TimeSpan.FromMilliseconds(180);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        var animW = new DoubleAnimation(targetWidth, duration) { EasingFunction = ease };
        var animO = new DoubleAnimation(targetOpacity, duration) { EasingFunction = ease };

        SliderBox.BeginAnimation(FrameworkElement.WidthProperty, animW);
        SliderBox.BeginAnimation(UIElement.OpacityProperty, animO);
    }

    private void SetupTrayIcon()
    {
        if (_trayIcon != null) return;

        try
        {
            _trayIcon = new TaskbarIcon
            {
                ToolTipText = "Aura Launcher",
                Visibility = Visibility.Collapsed
            };

            // Загрузка иконки из Resources/aura-icon.ico
            try
            {
                var iconUri = new Uri("pack://application:,,,/Resources/aura-icon.ico", UriKind.RelativeOrAbsolute);
                var streamInfo = System.Windows.Application.GetResourceStream(iconUri);
                if (streamInfo == null)
                {
                    iconUri = new Uri("pack://application:,,,/app_icon.ico", UriKind.RelativeOrAbsolute);
                    streamInfo = System.Windows.Application.GetResourceStream(iconUri);
                }

                if (streamInfo != null)
                {
                    using var stream = streamInfo.Stream;
                    _trayIcon.Icon = new System.Drawing.Icon(stream);
                }
            }
            catch
            {
                var localIco = Path.Combine(AppContext.BaseDirectory, "Resources", "aura-icon.ico");
                if (!File.Exists(localIco))
                {
                    localIco = Path.Combine(AppContext.BaseDirectory, "app_icon.ico");
                }

                if (File.Exists(localIco))
                {
                    _trayIcon.Icon = new System.Drawing.Icon(localIco);
                }
            }

            // Двойной клик по иконке открывает окно
            _trayIcon.TrayMouseDoubleClick += (s, e) =>
            {
                RestoreFromTray();
            };

            // Контекстное меню трея: «Открыть Aura» и «Выйти»
            var contextMenu = new ContextMenu();
            var openItem = new MenuItem { Header = "Открыть Aura" };
            openItem.Click += (s, e) => RestoreFromTray();

            var exitItem = new MenuItem { Header = "Выйти" };
            exitItem.Click += (s, e) =>
            {
                _trayIcon?.Dispose();
                _trayIcon = null;
                System.Windows.Application.Current.Shutdown();
            };

            contextMenu.Items.Add(openItem);
            contextMenu.Items.Add(new Separator());
            contextMenu.Items.Add(exitItem);

            _trayIcon.ContextMenu = contextMenu;
        }
        catch (Exception ex)
        {
            App.Log($"[TRAY: ERROR] Setup failed: {ex.Message}");
        }

        Closed += (s, e) =>
        {
            try
            {
                _trayIcon?.Dispose();
                _trayIcon = null;
            }
            catch { }
        };
    }

    public void HideToTray()
    {
        App.Log("[TRAY] HideToTray: Игра запущена, окно лаунчера скрыто, иконка в трее показана.");
        Hide();
        ShowInTaskbar = false;
        if (_trayIcon != null)
        {
            _trayIcon.Visibility = Visibility.Visible;
        }
    }

    public void RestoreFromTray()
    {
        App.Log("[TRAY] RestoreFromTray: Окно лаунчера возвращено на экран, иконка в трее скрыта.");
        if (_trayIcon != null)
        {
            _trayIcon.Visibility = Visibility.Collapsed;
        }

        Show();
        ShowInTaskbar = true;
        WindowState = WindowState.Normal;

        // Гарантированно выводим окно поверх остальных и активируем фокус
        Topmost = true;
        Topmost = false;
        Activate();
        Focus();
    }

    public void AnimateBackgroundTransition(System.Windows.Media.Imaging.BitmapImage nextImage)
    {
        if (BgImageCurrent == null || BgImageNext == null) return;

        if (BgImageCurrent.Source == null)
        {
            BgImageCurrent.Source = nextImage;
            return;
        }

        BgImageNext.Source = nextImage;
        BgImageNext.Opacity = 0.0;

        var anim = new DoubleAnimation
        {
            From = 0.0,
            To = 1.0,
            Duration = TimeSpan.FromMilliseconds(1200),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };

        anim.Completed += (s, e) =>
        {
            BgImageCurrent.Source = nextImage;
            BgImageNext.Opacity = 0.0;
            BgImageNext.Source = null;
        };

        BgImageNext.BeginAnimation(UIElement.OpacityProperty, anim);
    }
}