using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace AuraLauncher.Controls;

/// <summary>
/// Карусель историй на главной странице.
/// Чистая архитектура: AnimationClock для полосок прогресса, токен перехода для исключения гонок анимаций,
/// запуск строго из IsVisibleChanged, переключение только по Completed, пауза/продолжение без перескока.
/// </summary>
public partial class StoriesCarousel : UserControl
{
    public const double DurationSeconds = 6.0;
    public const double TransitionDurationMs = 350.0;
    public const double SlideOffsetPx = 24.0;

    private readonly StoriesCarouselState _state;
    private readonly ScaleTransform[] _indicatorScales;
    private AnimationClock? _activeProgressClock;
    private bool _isRunning = false;

    public StoriesCarousel()
    {
        InitializeComponent();

        _state = new StoriesCarouselState(new UserControl[]
        {
            SlideScreenshots,
            SlideAchievements,
            SlideWhatsNew
        });

        _indicatorScales = new[]
        {
            Indicator0Scale,
            Indicator1Scale,
            Indicator2Scale
        };

        IsVisibleChanged += StoriesCarousel_IsVisibleChanged;
        Unloaded += StoriesCarousel_Unloaded;
        MouseEnter += StoriesCarousel_MouseEnter;
        MouseLeave += StoriesCarousel_MouseLeave;
    }

    private void StoriesCarousel_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
        {
            StartCarousel();
        }
        else
        {
            StopCarousel();
        }
    }

    private void StoriesCarousel_Unloaded(object sender, RoutedEventArgs e)
    {
        StopCarousel();
    }

    private void StoriesCarousel_MouseEnter(object sender, MouseEventArgs e)
    {
        _state.IsPaused = true;
        _activeProgressClock?.Controller?.Pause();
    }

    private void StoriesCarousel_MouseLeave(object sender, MouseEventArgs e)
    {
        _state.IsPaused = false;
        _activeProgressClock?.Controller?.Resume();
    }

    public void StartCarousel()
    {
        if (_isRunning) return;
        _isRunning = true;

        _state.IsPaused = IsMouseOver;
        ResetSlidesToInitialState();
        StartProgressAnimation(_state.ActiveIndex);

        if (_state.Slides[_state.ActiveIndex] is ISlideLifecycle lifecycle)
        {
            lifecycle.OnEntered();
        }
    }

    public void StopCarousel()
    {
        _isRunning = false;
        StopProgressAnimation();
        ResetSlidesToInitialState();

        if (_state.ActiveIndex >= 0 && _state.ActiveIndex < _state.Slides.Count)
        {
            if (_state.Slides[_state.ActiveIndex] is ISlideLifecycle lifecycle)
            {
                lifecycle.OnExited();
            }
        }
    }

    private void OnIndicator0Click(object sender, RoutedEventArgs e) => OnIndicatorClicked(0);
    private void OnIndicator1Click(object sender, RoutedEventArgs e) => OnIndicatorClicked(1);
    private void OnIndicator2Click(object sender, RoutedEventArgs e) => OnIndicatorClicked(2);

    private void OnIndicatorClicked(int targetIndex)
    {
        if (targetIndex < 0 || targetIndex >= _state.Slides.Count) return;

        if (_state.ActiveIndex == targetIndex)
        {
            StartProgressAnimation(targetIndex);
            return;
        }

        int oldIndex = _state.ActiveIndex;
        TransitionToSlide(oldIndex, targetIndex);
    }

    private void AdvanceToNextSlide()
    {
        int oldIndex = _state.ActiveIndex;
        int nextIndex = (oldIndex + 1) % _state.Slides.Count;
        TransitionToSlide(oldIndex, nextIndex);
    }

    private void TransitionToSlide(int oldIndex, int newIndex)
    {
        long token = ++_state.TransitionToken;
        _state.ActiveIndex = newIndex;

        var oldSlide = _state.Slides[oldIndex];
        var newSlide = _state.Slides[newIndex];

        oldSlide.IsHitTestVisible = false;
        newSlide.IsHitTestVisible = true;

        Panel.SetZIndex(oldSlide, 0);
        Panel.SetZIndex(newSlide, 1);

        for (int i = 0; i < _state.Slides.Count; i++)
        {
            if (i != oldIndex && i != newIndex)
            {
                var other = _state.Slides[i];
                other.Visibility = Visibility.Collapsed;
                other.Opacity = 0.0;
                other.IsHitTestVisible = false;
            }
        }

        // 1. Анимация старого слайда: смещение влево на 24px + fade-out
        var oldTrans = GetOrCreateTranslate(oldSlide);
        oldTrans.BeginAnimation(TranslateTransform.XProperty, null);
        oldTrans.X = 0;
        var oldSlideAnim = new DoubleAnimation(0, -SlideOffsetPx, TimeSpan.FromMilliseconds(TransitionDurationMs))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        oldTrans.BeginAnimation(TranslateTransform.XProperty, oldSlideAnim);

        oldSlide.BeginAnimation(UIElement.OpacityProperty, null);
        oldSlide.Opacity = 1.0;
        var oldFadeAnim = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(TransitionDurationMs))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        oldFadeAnim.Completed += (s, e) =>
        {
            if (_state.TransitionToken != token) return;

            oldSlide.Visibility = Visibility.Collapsed;
            oldSlide.BeginAnimation(UIElement.OpacityProperty, null);
            oldSlide.Opacity = 1.0;
            oldTrans.BeginAnimation(TranslateTransform.XProperty, null);
            oldTrans.X = 0;
        };
        oldSlide.BeginAnimation(UIElement.OpacityProperty, oldFadeAnim);

        // 2. Анимация нового слайда: приезд справа (24px -> 0) + fade-in
        newSlide.Visibility = Visibility.Visible;
        var newTrans = GetOrCreateTranslate(newSlide);
        newTrans.BeginAnimation(TranslateTransform.XProperty, null);
        newTrans.X = SlideOffsetPx;
        var newSlideAnim = new DoubleAnimation(SlideOffsetPx, 0, TimeSpan.FromMilliseconds(TransitionDurationMs))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        newTrans.BeginAnimation(TranslateTransform.XProperty, newSlideAnim);

        newSlide.BeginAnimation(UIElement.OpacityProperty, null);
        newSlide.Opacity = 0.0;
        var newFadeAnim = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(TransitionDurationMs))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        newFadeAnim.Completed += (s, e) =>
        {
            if (_state.TransitionToken != token) return;

            newSlide.BeginAnimation(UIElement.OpacityProperty, null);
            newSlide.Opacity = 1.0;
            newTrans.BeginAnimation(TranslateTransform.XProperty, null);
            newTrans.X = 0;
        };
        newSlide.BeginAnimation(UIElement.OpacityProperty, newFadeAnim);

        if (oldSlide is ISlideLifecycle oldLifecycle)
        {
            oldLifecycle.OnExited();
        }
        if (newSlide is ISlideLifecycle newLifecycle)
        {
            newLifecycle.OnEntered();
        }

        StartProgressAnimation(newIndex);
    }

    private void StartProgressAnimation(int targetIndex)
    {
        StopProgressAnimation();

        for (int i = 0; i < _indicatorScales.Length; i++)
        {
            _indicatorScales[i].ApplyAnimationClock(ScaleTransform.ScaleXProperty, null);
            if (i < targetIndex)
            {
                _indicatorScales[i].ScaleX = 1.0;
            }
            else
            {
                _indicatorScales[i].ScaleX = 0.0;
            }
        }

        if (targetIndex < 0 || targetIndex >= _indicatorScales.Length) return;

        var targetScale = _indicatorScales[targetIndex];
        targetScale.ScaleX = 0.0;

        var anim = new DoubleAnimation
        {
            From = 0.0,
            To = 1.0,
            Duration = TimeSpan.FromSeconds(DurationSeconds),
            EasingFunction = null,
            FillBehavior = FillBehavior.HoldEnd
        };

        _activeProgressClock = anim.CreateClock();
        long token = _state.TransitionToken;
        _activeProgressClock.Completed += (s, e) =>
        {
            if (_state.TransitionToken != token) return;
            AdvanceToNextSlide();
        };

        targetScale.ApplyAnimationClock(ScaleTransform.ScaleXProperty, _activeProgressClock);

        if (_state.IsPaused)
        {
            _activeProgressClock.Controller?.Pause();
        }
    }

    private void StopProgressAnimation()
    {
        if (_activeProgressClock != null)
        {
            _activeProgressClock.Controller?.Stop();
            _activeProgressClock = null;
        }

        for (int i = 0; i < _indicatorScales.Length; i++)
        {
            _indicatorScales[i].ApplyAnimationClock(ScaleTransform.ScaleXProperty, null);
        }
    }

    private void ResetSlidesToInitialState()
    {
        _state.TransitionToken++;

        for (int i = 0; i < _state.Slides.Count; i++)
        {
            var slide = _state.Slides[i];
            slide.BeginAnimation(UIElement.OpacityProperty, null);
            var trans = GetOrCreateTranslate(slide);
            trans.BeginAnimation(TranslateTransform.XProperty, null);
            trans.X = 0;

            if (i == _state.ActiveIndex)
            {
                slide.Visibility = Visibility.Visible;
                slide.Opacity = 1.0;
                slide.IsHitTestVisible = true;
                Panel.SetZIndex(slide, 1);
            }
            else
            {
                slide.Visibility = Visibility.Collapsed;
                slide.Opacity = 0.0;
                slide.IsHitTestVisible = false;
                Panel.SetZIndex(slide, 0);
            }
        }

        for (int i = 0; i < _indicatorScales.Length; i++)
        {
            _indicatorScales[i].ApplyAnimationClock(ScaleTransform.ScaleXProperty, null);
            _indicatorScales[i].ScaleX = i < _state.ActiveIndex ? 1.0 : 0.0;
        }
    }

    private static TranslateTransform GetOrCreateTranslate(UIElement element)
    {
        if (element.RenderTransform is TranslateTransform tt)
        {
            return tt;
        }
        var newTt = new TranslateTransform(0, 0);
        element.RenderTransform = newTt;
        return newTt;
    }
}
