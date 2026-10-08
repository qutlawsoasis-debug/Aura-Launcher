using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace AuraLauncher.Behaviors;

public static class SmoothToggle
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(SmoothToggle),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static readonly DependencyProperty UncheckedLeftProperty =
        DependencyProperty.RegisterAttached(
            "UncheckedLeft",
            typeof(double),
            typeof(SmoothToggle),
            new PropertyMetadata(3.0));

    public static readonly DependencyProperty CheckedLeftProperty =
        DependencyProperty.RegisterAttached(
            "CheckedLeft",
            typeof(double),
            typeof(SmoothToggle),
            new PropertyMetadata(21.0));

    private static readonly DependencyProperty StateTrackerProperty =
        DependencyProperty.RegisterAttached(
            "StateTracker",
            typeof(ToggleStateTracker),
            typeof(SmoothToggle),
            new PropertyMetadata(null));

    private static readonly IEasingFunction SlideEase = CreateFrozenEase();

    private static IEasingFunction CreateFrozenEase()
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        ease.Freeze();
        return ease;
    }

    public static bool GetIsEnabled(DependencyObject obj) => (bool)obj.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject obj, bool value) => obj.SetValue(IsEnabledProperty, value);

    public static double GetUncheckedLeft(DependencyObject obj) => (double)obj.GetValue(UncheckedLeftProperty);
    public static void SetUncheckedLeft(DependencyObject obj, double value) => obj.SetValue(UncheckedLeftProperty, value);

    public static double GetCheckedLeft(DependencyObject obj) => (double)obj.GetValue(CheckedLeftProperty);
    public static void SetCheckedLeft(DependencyObject obj, double value) => obj.SetValue(CheckedLeftProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not CheckBox cb) return;

        if (e.NewValue is true)
        {
            var tracker = new ToggleStateTracker(cb);
            cb.SetValue(StateTrackerProperty, tracker);
            tracker.Attach();
        }
        else if (cb.GetValue(StateTrackerProperty) is ToggleStateTracker existing)
        {
            existing.Detach();
            cb.ClearValue(StateTrackerProperty);
        }
    }

    private sealed class ToggleStateTracker
    {
        private static readonly Duration AnimDuration = new(TimeSpan.FromMilliseconds(180));

        private readonly CheckBox _checkBox;
        private object? _lastDataContext;
        private bool? _lastChecked;
        private bool _suppressAnimation;

        public ToggleStateTracker(CheckBox checkBox)
        {
            _checkBox = checkBox;
        }

        public void Attach()
        {
            _checkBox.Loaded += OnLoaded;
            _checkBox.DataContextChanged += OnDataContextChanged;
            _checkBox.Checked += OnCheckedChanged;
            _checkBox.Unchecked += OnCheckedChanged;

            if (_checkBox.IsLoaded)
            {
                _lastDataContext = _checkBox.DataContext;
                _lastChecked = _checkBox.IsChecked == true;
                ClearAnimations();
            }
        }

        public void Detach()
        {
            _checkBox.Loaded -= OnLoaded;
            _checkBox.DataContextChanged -= OnDataContextChanged;
            _checkBox.Checked -= OnCheckedChanged;
            _checkBox.Unchecked -= OnCheckedChanged;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _lastDataContext = _checkBox.DataContext;
            _lastChecked = _checkBox.IsChecked == true;
            ClearAnimations();
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            _suppressAnimation = true;
            try
            {
                _lastDataContext = e.NewValue;
                _lastChecked = _checkBox.IsChecked == true;
                ClearAnimations();
            }
            finally
            {
                _suppressAnimation = false;
            }
        }

        private void OnCheckedChanged(object sender, RoutedEventArgs e)
        {
            bool isChecked = _checkBox.IsChecked == true;

            if (!_checkBox.IsLoaded || _suppressAnimation || !ReferenceEquals(_lastDataContext, _checkBox.DataContext))
            {
                _lastDataContext = _checkBox.DataContext;
                _lastChecked = isChecked;
                ClearAnimations();
                return;
            }

            if (_lastChecked == isChecked)
            {
                return;
            }

            _lastChecked = isChecked;
            AnimateToState(isChecked);
        }

        private void ClearAnimations()
        {
            if (_checkBox.Template == null) return;

            if (_checkBox.Template.FindName("Thumb", _checkBox) is FrameworkElement thumb)
            {
                thumb.BeginAnimation(FrameworkElement.MarginProperty, null);
            }

            if (_checkBox.Template.FindName("ActiveTrack", _checkBox) is UIElement activeTrack)
            {
                activeTrack.BeginAnimation(UIElement.OpacityProperty, null);
            }

            if (_checkBox.Template.FindName("ThumbActive", _checkBox) is UIElement thumbActive)
            {
                thumbActive.BeginAnimation(UIElement.OpacityProperty, null);
            }
        }

        private void AnimateToState(bool isChecked)
        {
            _checkBox.ApplyTemplate();
            if (_checkBox.Template == null) return;

            double uncheckedLeft = GetUncheckedLeft(_checkBox);
            double checkedLeft = GetCheckedLeft(_checkBox);
            double fromLeft = isChecked ? uncheckedLeft : checkedLeft;
            double toLeft = isChecked ? checkedLeft : uncheckedLeft;
            double fromOpacity = isChecked ? 0.0 : 1.0;
            double toOpacity = isChecked ? 1.0 : 0.0;

            if (_checkBox.Template.FindName("Thumb", _checkBox) is FrameworkElement thumb)
            {
                double currentLeft = thumb.Margin.Left;
                if (double.IsNaN(currentLeft) || Math.Abs(currentLeft - toLeft) < 0.01)
                {
                    currentLeft = fromLeft;
                }

                var marginAnim = new ThicknessAnimation
                {
                    From = new Thickness(currentLeft, 0, 0, 0),
                    To = new Thickness(toLeft, 0, 0, 0),
                    Duration = AnimDuration,
                    EasingFunction = SlideEase
                };
                thumb.BeginAnimation(FrameworkElement.MarginProperty, marginAnim);
            }

            if (_checkBox.Template.FindName("ActiveTrack", _checkBox) is UIElement activeTrack)
            {
                double currentOp = activeTrack.Opacity;
                if (double.IsNaN(currentOp) || Math.Abs(currentOp - toOpacity) < 0.01)
                {
                    currentOp = fromOpacity;
                }

                var fadeAnim = new DoubleAnimation
                {
                    From = currentOp,
                    To = toOpacity,
                    Duration = AnimDuration,
                    EasingFunction = SlideEase
                };
                activeTrack.BeginAnimation(UIElement.OpacityProperty, fadeAnim);
            }

            if (_checkBox.Template.FindName("ThumbActive", _checkBox) is UIElement thumbActive)
            {
                double currentOp = thumbActive.Opacity;
                if (double.IsNaN(currentOp) || Math.Abs(currentOp - toOpacity) < 0.01)
                {
                    currentOp = fromOpacity;
                }

                var fadeAnim = new DoubleAnimation
                {
                    From = currentOp,
                    To = toOpacity,
                    Duration = AnimDuration,
                    EasingFunction = SlideEase
                };
                thumbActive.BeginAnimation(UIElement.OpacityProperty, fadeAnim);
            }
        }
    }
}
