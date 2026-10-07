using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using AuraLauncher.ViewModels;

namespace AuraLauncher.Views;

/// <summary>
/// Логика взаимодействия для OverviewView.xaml
/// Правая колонка в формате Stories: 3 слайда, 5-секундный прогресс, кроссфейд, пауза/возобновление.
/// </summary>
public partial class OverviewView : UserControl
{
    private UIElement[] _slides = Array.Empty<UIElement>();
    private ScaleTransform[] _progressScales = Array.Empty<ScaleTransform>();
    private int _currentSlideIndex = 0;

    private Storyboard? _progressStoryboard;
    private bool _isStoryboardPaused;
    private DateTime _lastResumeTime;
    private DateTime _slideStartTime;
    private TimeSpan _slideElapsedBeforePause = TimeSpan.Zero;

    private DispatcherTimer? _holdTimer;
    private bool _isHolding;
    private DateTime _mouseDownTime;

    private Window? _parentWindow;
    private MainViewModel? _mainViewModel;
    private bool _isInitialized;

    public OverviewView()
    {
        InitializeComponent();
        Loaded += OverviewView_Loaded;
        Unloaded += OverviewView_Unloaded;
        IsVisibleChanged += OverviewView_IsVisibleChanged;
    }

    private void OverviewView_Loaded(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized)
        {
            _slides = new UIElement[] { SlideScreenshots, SlideAchievements, SlideUpdates };
            _progressScales = new ScaleTransform[] { ProgressScale0, ProgressScale1, ProgressScale2 };
            _isInitialized = true;
        }

        AttachWindowAndViewModel();
        UpdateRailVisibility();

        if (IsVisible && StoriesRail != null && StoriesRail.Visibility == Visibility.Visible)
        {
            AnimateEntrance();
            RefreshDataAndRestart();
        }
    }

    private void OverviewView_Unloaded(object sender, RoutedEventArgs e)
    {
        DetachWindowAndViewModel();
        StopProgressStoryboard();
        _holdTimer?.Stop();
    }

    private void OverviewView_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
        {
            UpdateRailVisibility();
            if (StoriesRail != null && StoriesRail.Visibility == Visibility.Visible)
            {
                AnimateEntrance();
                RefreshDataAndRestart();
            }
        }
        else
        {
            PausePlayback();
        }
    }

    private void RefreshDataAndRestart()
    {
        if (DataContext is OverviewViewModel ovm)
        {
            ovm.RefreshStoriesData();
        }

        GoToSlide(0, crossfade: false);
    }

    private void AnimateEntrance()
    {
        if (StoriesRail == null || StoriesRailTranslate == null) return;

        StoriesRail.BeginAnimation(UIElement.OpacityProperty, null);
        StoriesRailTranslate.BeginAnimation(TranslateTransform.XProperty, null);

        StoriesRail.Opacity = 0.0;
        StoriesRailTranslate.X = 24.0;

        var opacityAnim = new DoubleAnimation
        {
            From = 0.0,
            To = 1.0,
            Duration = TimeSpan.FromMilliseconds(240),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        var translateAnim = new DoubleAnimation
        {
            From = 24.0,
            To = 0.0,
            Duration = TimeSpan.FromMilliseconds(240),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        StoriesRail.BeginAnimation(UIElement.OpacityProperty, opacityAnim);
        StoriesRailTranslate.BeginAnimation(TranslateTransform.XProperty, translateAnim);
    }

    private void AttachWindowAndViewModel()
    {
        _parentWindow = Window.GetWindow(this);
        if (_parentWindow != null)
        {
            _parentWindow.Activated -= Window_Activated;
            _parentWindow.Activated += Window_Activated;

            _parentWindow.Deactivated -= Window_Deactivated;
            _parentWindow.Deactivated += Window_Deactivated;

            _parentWindow.StateChanged -= Window_StateChanged;
            _parentWindow.StateChanged += Window_StateChanged;

            _parentWindow.IsVisibleChanged -= Window_IsVisibleChanged;
            _parentWindow.IsVisibleChanged += Window_IsVisibleChanged;

            _parentWindow.SizeChanged -= Window_SizeChanged;
            _parentWindow.SizeChanged += Window_SizeChanged;

            if (_parentWindow.DataContext is MainViewModel mvm)
            {
                if (_mainViewModel != mvm)
                {
                    if (_mainViewModel != null)
                    {
                        _mainViewModel.PropertyChanged -= MainViewModel_PropertyChanged;
                    }
                    _mainViewModel = mvm;
                    _mainViewModel.PropertyChanged += MainViewModel_PropertyChanged;
                }
            }
        }
    }

    private void DetachWindowAndViewModel()
    {
        if (_parentWindow != null)
        {
            _parentWindow.Activated -= Window_Activated;
            _parentWindow.Deactivated -= Window_Deactivated;
            _parentWindow.StateChanged -= Window_StateChanged;
            _parentWindow.IsVisibleChanged -= Window_IsVisibleChanged;
            _parentWindow.SizeChanged -= Window_SizeChanged;
            _parentWindow = null;
        }

        if (_mainViewModel != null)
        {
            _mainViewModel.PropertyChanged -= MainViewModel_PropertyChanged;
            _mainViewModel = null;
        }
    }

    private void Window_Activated(object? sender, EventArgs e) => UpdatePlaybackState();
    private void Window_Deactivated(object? sender, EventArgs e) => UpdatePlaybackState();
    private void Window_StateChanged(object? sender, EventArgs e) => UpdatePlaybackState();
    private void Window_IsVisibleChanged(object? sender, DependencyPropertyChangedEventArgs e) => UpdatePlaybackState();

    private void Window_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        UpdateRailVisibility();
    }

    private void MainViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsChangelogModalVisible) ||
            e.PropertyName == nameof(MainViewModel.IsSendReportModalVisible) ||
            e.PropertyName == nameof(MainViewModel.IsReportSuccessModalVisible))
        {
            UpdatePlaybackState();
        }
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
                PausePlayback();
            }
        }
        else
        {
            if (StoriesRail.Visibility != Visibility.Visible)
            {
                StoriesRail.Visibility = Visibility.Visible;
                UpdatePlaybackState();
            }
        }
    }

    private bool ShouldBePlaying()
    {
        if (_isHolding) return false;
        if (!IsVisible) return false;
        if (StoriesRail == null || StoriesRail.Visibility != Visibility.Visible) return false;

        if (_parentWindow == null)
        {
            _parentWindow = Window.GetWindow(this);
        }
        if (_parentWindow == null) return false;

        if (!_parentWindow.IsActive && !_parentWindow.IsFocused) return false;
        if (_parentWindow.WindowState == WindowState.Minimized || !_parentWindow.IsVisible) return false;

        if (_mainViewModel != null)
        {
            if (_mainViewModel.IsChangelogModalVisible ||
                _mainViewModel.IsSendReportModalVisible ||
                _mainViewModel.IsReportSuccessModalVisible)
            {
                return false;
            }
        }

        return true;
    }

    private void UpdatePlaybackState()
    {
        if (ShouldBePlaying())
        {
            ResumePlayback();
        }
        else
        {
            PausePlayback();
        }
    }

    private void GoToSlide(int targetIndex, bool crossfade = true)
    {
        if (!_isInitialized || _slides.Length == 0 || _progressScales.Length == 0) return;

        targetIndex = (targetIndex % 3 + 3) % 3;
        int previousIndex = _currentSlideIndex;
        _currentSlideIndex = targetIndex;

        // 1. Останавливаем текущий Storyboard прогресса
        StopProgressStoryboard();

        // 2. Ставим шкалы прогресса: прошедшие 100%, текущая 0%, будущие 0% мгновенно без анимации
        for (int i = 0; i < 3; i++)
        {
            _progressScales[i].BeginAnimation(ScaleTransform.ScaleXProperty, null);
            if (i < targetIndex)
            {
                _progressScales[i].ScaleX = 1.0;
            }
            else
            {
                _progressScales[i].ScaleX = 0.0;
            }
        }

        // 3. Кроссфейд слайдов
        for (int i = 0; i < 3; i++)
        {
            _slides[i].BeginAnimation(UIElement.OpacityProperty, null);
            if (i == targetIndex)
            {
                _slides[i].IsHitTestVisible = true;
                if (crossfade && previousIndex != targetIndex)
                {
                    var fadeIn = new DoubleAnimation
                    {
                        From = _slides[i].Opacity,
                        To = 1.0,
                        Duration = TimeSpan.FromMilliseconds(250)
                    };
                    _slides[i].BeginAnimation(UIElement.OpacityProperty, fadeIn);
                }
                else
                {
                    _slides[i].Opacity = 1.0;
                }
            }
            else if (i == previousIndex && crossfade)
            {
                _slides[i].IsHitTestVisible = false;
                var fadeOut = new DoubleAnimation
                {
                    From = _slides[i].Opacity,
                    To = 0.0,
                    Duration = TimeSpan.FromMilliseconds(250)
                };
                _slides[i].BeginAnimation(UIElement.OpacityProperty, fadeOut);
            }
            else
            {
                _slides[i].IsHitTestVisible = false;
                _slides[i].Opacity = 0.0;
            }
        }

        // 4. Сброс таймера
        _slideStartTime = DateTime.UtcNow;
        _lastResumeTime = DateTime.UtcNow;
        _slideElapsedBeforePause = TimeSpan.Zero;

        // 5. Запуск Storyboard на 5 сек
        StartProgressStoryboard(targetIndex);

        // 6. Проверка условий автопаузы
        if (!ShouldBePlaying())
        {
            PausePlayback();
        }
    }

    private void StartProgressStoryboard(int slideIndex)
    {
        StopProgressStoryboard();

        var targetScale = _progressScales[slideIndex];
        targetScale.ScaleX = 0.0;

        var anim = new DoubleAnimation
        {
            From = 0.0,
            To = 1.0,
            Duration = TimeSpan.FromSeconds(5),
            FillBehavior = FillBehavior.HoldEnd
        };

        Storyboard.SetTarget(anim, targetScale);
        Storyboard.SetTargetProperty(anim, new PropertyPath(ScaleTransform.ScaleXProperty));

        _progressStoryboard = new Storyboard();
        _progressStoryboard.Children.Add(anim);

        int capturedIndex = slideIndex;
        _progressStoryboard.Completed += (s, e) =>
        {
            if (_currentSlideIndex == capturedIndex)
            {
                GoToSlide((_currentSlideIndex + 1) % 3, crossfade: true);
            }
        };

        _isStoryboardPaused = false;
        _progressStoryboard.Begin(this, isControllable: true);
    }

    private void StopProgressStoryboard()
    {
        if (_progressStoryboard != null)
        {
            _progressStoryboard.Stop(this);
            _progressStoryboard = null;
            _isStoryboardPaused = false;
        }
    }

    private void PausePlayback()
    {
        if (_progressStoryboard != null && !_isStoryboardPaused)
        {
            _progressStoryboard.Pause(this);
            _isStoryboardPaused = true;
            _slideElapsedBeforePause += (DateTime.UtcNow - _lastResumeTime);
        }
    }

    private void ResumePlayback()
    {
        if (_progressStoryboard != null && _isStoryboardPaused && ShouldBePlaying())
        {
            _progressStoryboard.Resume(this);
            _isStoryboardPaused = false;
            _lastResumeTime = DateTime.UtcNow;
        }
    }

    private double GetSlideElapsedSeconds()
    {
        if (_isStoryboardPaused)
        {
            return _slideElapsedBeforePause.TotalSeconds;
        }
        return (_slideElapsedBeforePause + (DateTime.UtcNow - _lastResumeTime)).TotalSeconds;
    }

    private void HandleRightZoneClick()
    {
        GoToSlide((_currentSlideIndex + 1) % 3, crossfade: true);
    }

    private void HandleLeftZoneClick()
    {
        double elapsed = GetSlideElapsedSeconds();
        if (elapsed > 1.0)
        {
            // Перезапуск текущего слайда с 0%
            GoToSlide(_currentSlideIndex, crossfade: false);
        }
        else
        {
            if (_currentSlideIndex > 0)
            {
                GoToSlide(_currentSlideIndex - 1, crossfade: true);
            }
            else
            {
                GoToSlide(0, crossfade: false);
            }
        }
    }

    private void StoriesRail_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (StoriesRail == null || StoriesRail.Visibility != Visibility.Visible) return;

        _mouseDownTime = DateTime.UtcNow;
        _isHolding = false;

        _holdTimer?.Stop();
        _holdTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };
        _holdTimer.Tick += (s, ev) =>
        {
            _holdTimer?.Stop();
            _isHolding = true;
            PausePlayback();
        };
        _holdTimer.Start();

        if (sender is UIElement el)
        {
            el.CaptureMouse();
        }
    }

    private void StoriesRail_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is UIElement el && el.IsMouseCaptured)
        {
            el.ReleaseMouseCapture();
        }

        _holdTimer?.Stop();
        var duration = (DateTime.UtcNow - _mouseDownTime).TotalMilliseconds;

        if (_isHolding || duration > 200)
        {
            _isHolding = false;
            ResumePlayback();
            return;
        }

        _isHolding = false;

        var clickPos = e.GetPosition(sender as IInputElement);
        double totalWidth = (sender as FrameworkElement)?.ActualWidth ?? 320.0;
        if (totalWidth <= 0) totalWidth = 320.0;

        double ratio = clickPos.X / totalWidth;
        if (ratio < 0.3)
        {
            HandleLeftZoneClick();
        }
        else
        {
            HandleRightZoneClick();
        }
    }

    private void StoriesRail_LostMouseCapture(object sender, MouseEventArgs e)
    {
        _holdTimer?.Stop();
        if (_isHolding)
        {
            _isHolding = false;
            ResumePlayback();
        }
    }
}
