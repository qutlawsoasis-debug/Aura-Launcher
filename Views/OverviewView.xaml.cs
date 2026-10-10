using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AuraLauncher.ViewModels;

namespace AuraLauncher.Views;

/// <summary>
/// Логика взаимодействия для OverviewView.xaml.
/// Правая колонка: карусель историй (Скриншот, Достижения, Что нового) с плавной анимацией 350 мс перехода слайдов, 6с зумом скриншота и автопропуском при отсутствии скриншотов.
/// </summary>
public partial class OverviewView : UserControl
{
    private Window? _parentWindow;
    private Storyboard? _activeProgressStoryboard;
    private Storyboard? _zoomStoryboard;
    private bool _isMouseOverStoriesRail = false;
    private int _currentSlideIndex = -1;

    public OverviewView()
    {
        InitializeComponent();
        Loaded += OverviewView_Loaded;
        Unloaded += OverviewView_Unloaded;
        IsVisibleChanged += OverviewView_IsVisibleChanged;
        SizeChanged += OverviewView_SizeChanged;
    }

    private void OverviewView_Loaded(object sender, RoutedEventArgs e)
    {
        AttachWindow();
        UpdateRailVisibility();

        if (IsVisible && StoriesRail != null && StoriesRail.Visibility == Visibility.Visible)
        {
            AnimateEntrance();
            RefreshData();
            StartCurrentStoryAnimation();
        }
    }

    private void OverviewView_Unloaded(object sender, RoutedEventArgs e)
    {
        DetachWindow();
        StopProgressAnimation();
        StopZoomAnimation();
    }

    private void OverviewView_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
        {
            UpdateRailVisibility();
            if (StoriesRail != null && StoriesRail.Visibility == Visibility.Visible)
            {
                AnimateEntrance();
                RefreshData();
                StartCurrentStoryAnimation();
            }
        }
        else
        {
            StopProgressAnimation();
            StopZoomAnimation();
        }
    }

    private void OverviewView_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateRailVisibility();
    }

    private void RefreshData()
    {
        if (DataContext is OverviewViewModel ovm)
        {
            ovm.RefreshStoriesData();
        }
    }

    private void StartCurrentStoryAnimation()
    {
        if (DataContext is OverviewViewModel ovm)
        {
            if (ovm.ActiveStoryIndex == 0 && !ovm.HasLatestScreenshot)
            {
                ovm.ActiveStoryIndex = 1;
            }
            SwitchToStoryInternal(ovm.ActiveStoryIndex, isInitial: true);
        }
    }

    private void StartProgressAnimation(int storyIndex)
    {
        StopProgressAnimation();

        if (Story0Scale != null) Story0Scale.ScaleX = storyIndex > 0 ? 1.0 : 0.0;
        if (Story1Scale != null) Story1Scale.ScaleX = storyIndex > 1 ? 1.0 : 0.0;
        if (Story2Scale != null) Story2Scale.ScaleX = storyIndex > 2 ? 1.0 : 0.0;

        ScaleTransform? targetScale = storyIndex switch
        {
            0 => Story0Scale,
            1 => Story1Scale,
            2 => Story2Scale,
            _ => null
        };

        if (targetScale == null) return;

        targetScale.ScaleX = 0.0;

        var fillAnimation = new DoubleAnimation
        {
            From = 0.0,
            To = 1.0,
            Duration = TimeSpan.FromSeconds(6),
            EasingFunction = null // Линейное заполнение слева направо
        };

        Storyboard.SetTarget(fillAnimation, targetScale);
        Storyboard.SetTargetProperty(fillAnimation, new PropertyPath(ScaleTransform.ScaleXProperty));

        _activeProgressStoryboard = new Storyboard();
        _activeProgressStoryboard.Children.Add(fillAnimation);
        _activeProgressStoryboard.Completed += OnStoryAnimationCompleted;

        _activeProgressStoryboard.Begin(this, isControllable: true);

        if (_isMouseOverStoriesRail)
        {
            _activeProgressStoryboard.Pause(this);
        }
    }

    private void OnStoryAnimationCompleted(object? sender, EventArgs e)
    {
        if (DataContext is OverviewViewModel ovm)
        {
            int nextIndex = (ovm.ActiveStoryIndex + 1) % 3;
            if (nextIndex == 0 && !ovm.HasLatestScreenshot)
            {
                nextIndex = 1;
            }
            SwitchToStoryInternal(nextIndex, isInitial: false);
        }
    }

    private void StopProgressAnimation()
    {
        if (_activeProgressStoryboard != null)
        {
            _activeProgressStoryboard.Completed -= OnStoryAnimationCompleted;
            _activeProgressStoryboard.Stop(this);
            _activeProgressStoryboard = null;
        }
    }

    private void StopZoomAnimation()
    {
        if (_zoomStoryboard != null)
        {
            _zoomStoryboard.Stop(this);
            _zoomStoryboard = null;
        }
    }

    private void OnStoryBar0Click(object sender, RoutedEventArgs e) => SwitchToStory(0);
    private void OnStoryBar1Click(object sender, RoutedEventArgs e) => SwitchToStory(1);
    private void OnStoryBar2Click(object sender, RoutedEventArgs e) => SwitchToStory(2);

    private void SwitchToStory(int index)
    {
        if (DataContext is OverviewViewModel ovm)
        {
            if (index == 0 && !ovm.HasLatestScreenshot) return;
            SwitchToStoryInternal(index, isInitial: false);
        }
    }

    private void SwitchToStoryInternal(int targetIndex, bool isInitial)
    {
        if (DataContext is OverviewViewModel ovm)
        {
            int oldIndex = ovm.ActiveStoryIndex;
            ovm.ActiveStoryIndex = targetIndex;

            if (!isInitial && oldIndex != targetIndex)
            {
                AnimateSlideTransition(oldIndex, targetIndex);
            }
            else
            {
                TriggerSlideEntranceEffects(targetIndex);
            }

            _currentSlideIndex = targetIndex;
            StartProgressAnimation(targetIndex);
        }
    }

    private void AnimateSlideTransition(int oldIndex, int targetIndex)
    {
        Grid? oldSlide = GetSlideGrid(oldIndex);
        Grid? newSlide = GetSlideGrid(targetIndex);

        if (oldSlide != null)
        {
            var oldTranslate = oldSlide.RenderTransform as TranslateTransform;
            if (oldTranslate != null)
            {
                oldTranslate.BeginAnimation(TranslateTransform.XProperty, null);
                var animX = new DoubleAnimation(0, -24, TimeSpan.FromMilliseconds(350))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                oldTranslate.BeginAnimation(TranslateTransform.XProperty, animX);
            }

            oldSlide.BeginAnimation(UIElement.OpacityProperty, null);
            var animOpacity = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(350))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            oldSlide.BeginAnimation(UIElement.OpacityProperty, animOpacity);
        }

        if (newSlide != null)
        {
            newSlide.Visibility = Visibility.Visible;
            var newTranslate = newSlide.RenderTransform as TranslateTransform;
            if (newTranslate != null)
            {
                newTranslate.BeginAnimation(TranslateTransform.XProperty, null);
                newTranslate.X = 24;
                var animX = new DoubleAnimation(24, 0, TimeSpan.FromMilliseconds(350))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                newTranslate.BeginAnimation(TranslateTransform.XProperty, animX);
            }

            newSlide.BeginAnimation(UIElement.OpacityProperty, null);
            newSlide.Opacity = 0.0;
            var animOpacity = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(350))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            newSlide.BeginAnimation(UIElement.OpacityProperty, animOpacity);

            TriggerSlideEntranceEffects(targetIndex);
        }
    }

    private Grid? GetSlideGrid(int index) => index switch
    {
        0 => Slide0View,
        1 => Slide1View,
        2 => Slide2View,
        _ => null
    };

    private void TriggerSlideEntranceEffects(int slideIndex)
    {
        if (slideIndex == 0)
        {
            // Medленный зум 1.0 -> 1.06 за 6 сек на Слайде 0
            if (ScreenshotZoomTransform != null)
            {
                ScreenshotZoomTransform.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                ScreenshotZoomTransform.BeginAnimation(ScaleTransform.ScaleYProperty, null);

                var zoomX = new DoubleAnimation(1.0, 1.06, TimeSpan.FromSeconds(6));
                var zoomY = new DoubleAnimation(1.0, 1.06, TimeSpan.FromSeconds(6));

                ScreenshotZoomTransform.BeginAnimation(ScaleTransform.ScaleXProperty, zoomX);
                ScreenshotZoomTransform.BeginAnimation(ScaleTransform.ScaleYProperty, zoomY);
            }
        }
        else if (slideIndex == 1)
        {
            // Анимация заполнения толстой полосы прогресса "Следующая цель" (500 мс)
            if (NextAchProgressBar != null && DataContext is OverviewViewModel ovm)
            {
                NextAchProgressBar.BeginAnimation(FrameworkElement.WidthProperty, null);
                double parentWidth = 260.0;
                double targetWidth = Math.Clamp(ovm.NextAchievementRatio * parentWidth, 12.0, parentWidth);

                var fillAnim = new DoubleAnimation(0.0, targetWidth, TimeSpan.FromMilliseconds(500))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                NextAchProgressBar.BeginAnimation(FrameworkElement.WidthProperty, fillAnim);
            }
        }
    }

    private void StoriesRail_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _isMouseOverStoriesRail = true;
        _activeProgressStoryboard?.Pause(this);
    }

    private void StoriesRail_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _isMouseOverStoriesRail = false;
        _activeProgressStoryboard?.Resume(this);
    }

    private void AnimateEntrance()
    {
        if (StoriesRail == null) return;

        StoriesRail.BeginAnimation(UIElement.OpacityProperty, null);
        StoriesRail.Opacity = 0.0;

        var opacityAnim = new DoubleAnimation
        {
            From = 0.0,
            To = 1.0,
            Duration = TimeSpan.FromMilliseconds(240),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        StoriesRail.BeginAnimation(UIElement.OpacityProperty, opacityAnim);
    }

    private void AttachWindow()
    {
        _parentWindow = Window.GetWindow(this);
        if (_parentWindow != null)
        {
            _parentWindow.SizeChanged -= Window_SizeChanged;
            _parentWindow.SizeChanged += Window_SizeChanged;
        }
    }

    private void DetachWindow()
    {
        if (_parentWindow != null)
        {
            _parentWindow.SizeChanged -= Window_SizeChanged;
            _parentWindow = null;
        }
    }

    private void Window_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        UpdateRailVisibility();
    }

    private void UpdateRailVisibility()
    {
        if (_parentWindow == null)
        {
            _parentWindow = Window.GetWindow(this);
        }
        if (_parentWindow == null || StoriesRail == null) return;

        if (_parentWindow.ActualWidth < 1100)
        {
            if (StoriesRail.Visibility != Visibility.Collapsed)
            {
                StoriesRail.Visibility = Visibility.Collapsed;
            }
            return;
        }

        if (StoriesRail.Visibility != Visibility.Visible)
        {
            StoriesRail.Visibility = Visibility.Visible;
        }
    }
}
