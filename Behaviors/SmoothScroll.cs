using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AuraLauncher.Behaviors;

public static class SmoothScroll
{
    private const double PixelsPerWheelNotch = 100.0;
    private const double SmoothingSpeed = 22.0;
    private const double SnapThreshold = 0.4;

    private static readonly List<ScrollViewer> ActiveScrollViewers = new();
    private static bool _isRenderingHooked;

    private sealed class ScrollState
    {
        public double CurrentOffset;
        public double TargetOffset;
        public double LastRequestedOffset;
        public bool IsAnimating;
        public bool HasPendingInternalScroll;
        public long LastTimestamp;
    }

    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(SmoothScroll),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static readonly DependencyProperty SmoothVerticalOffsetProperty =
        DependencyProperty.RegisterAttached(
            "SmoothVerticalOffset",
            typeof(double),
            typeof(SmoothScroll),
            new PropertyMetadata(0.0, OnSmoothVerticalOffsetChanged));

    private static readonly DependencyProperty ScrollStateProperty =
        DependencyProperty.RegisterAttached(
            "ScrollState",
            typeof(ScrollState),
            typeof(SmoothScroll),
            new PropertyMetadata(null));

    public static bool GetIsEnabled(DependencyObject obj) => (bool)obj.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject obj, bool value) => obj.SetValue(IsEnabledProperty, value);

    public static double GetSmoothVerticalOffset(DependencyObject obj) => (double)obj.GetValue(SmoothVerticalOffsetProperty);
    public static void SetSmoothVerticalOffset(DependencyObject obj, double value) => obj.SetValue(SmoothVerticalOffsetProperty, value);

    public static double GetTargetVerticalOffset(ScrollViewer scrollViewer)
    {
        var state = GetOrCreateState(scrollViewer);
        return state.IsAnimating ? state.TargetOffset : scrollViewer.VerticalOffset;
    }

    private static ScrollState GetOrCreateState(ScrollViewer scrollViewer)
    {
        if (scrollViewer.GetValue(ScrollStateProperty) is not ScrollState state)
        {
            state = new ScrollState
            {
                CurrentOffset = scrollViewer.VerticalOffset,
                TargetOffset = scrollViewer.VerticalOffset,
                LastRequestedOffset = scrollViewer.VerticalOffset
            };
            scrollViewer.SetValue(ScrollStateProperty, state);
        }
        return state;
    }

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ScrollViewer scrollViewer) return;

        if ((bool)e.NewValue)
        {
            scrollViewer.PreviewMouseWheel += ScrollViewer_PreviewMouseWheel;
            scrollViewer.ScrollChanged += ScrollViewer_ScrollChanged;
            scrollViewer.Unloaded += ScrollViewer_Unloaded;
        }
        else
        {
            scrollViewer.PreviewMouseWheel -= ScrollViewer_PreviewMouseWheel;
            scrollViewer.ScrollChanged -= ScrollViewer_ScrollChanged;
            scrollViewer.Unloaded -= ScrollViewer_Unloaded;
            if (scrollViewer.GetValue(ScrollStateProperty) is ScrollState state)
            {
                StopAnimation(scrollViewer, state);
            }
        }
    }

    private static void OnSmoothVerticalOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ScrollViewer scrollViewer)
        {
            var state = GetOrCreateState(scrollViewer);
            double offset = (double)e.NewValue;
            state.CurrentOffset = offset;
            state.TargetOffset = offset;
            RequestInternalScroll(scrollViewer, state, offset);
        }
    }

    private static void RequestInternalScroll(ScrollViewer scrollViewer, ScrollState state, double offset)
    {
        double clamped = scrollViewer.ScrollableHeight > 0
            ? Math.Clamp(offset, 0.0, scrollViewer.ScrollableHeight)
            : Math.Max(0.0, offset);
        state.LastRequestedOffset = clamped;
        state.HasPendingInternalScroll = true;
        scrollViewer.ScrollToVerticalOffset(clamped);
    }

    private static void ScrollViewer_Unloaded(object sender, RoutedEventArgs e)
    {
        if (sender is ScrollViewer scrollViewer &&
            scrollViewer.GetValue(ScrollStateProperty) is ScrollState state)
        {
            StopAnimation(scrollViewer, state);
        }
    }

    private static void ScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer) return;
        if (scrollViewer.GetValue(ScrollStateProperty) is not ScrollState state) return;

        if (Math.Abs(e.VerticalChange) > 0.001)
        {
            if (state.HasPendingInternalScroll &&
                Math.Abs(scrollViewer.VerticalOffset - state.LastRequestedOffset) <= 1.0)
            {
                state.HasPendingInternalScroll = false;
                return;
            }

            state.HasPendingInternalScroll = false;
            state.CurrentOffset = scrollViewer.VerticalOffset;
            state.TargetOffset = scrollViewer.VerticalOffset;
            state.LastRequestedOffset = scrollViewer.VerticalOffset;
            StopAnimation(scrollViewer, state);
        }
        else if ((Math.Abs(e.ExtentHeightChange) > 0.001 || Math.Abs(e.ViewportHeightChange) > 0.001) &&
                 scrollViewer.ScrollableHeight >= 0)
        {
            state.TargetOffset = Math.Clamp(state.TargetOffset, 0.0, scrollViewer.ScrollableHeight);
        }
    }

    private static void ScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer) return;
        if (e.Handled) return;

        if (e.OriginalSource is DependencyObject source &&
            HasNestedScrollableViewer(source, scrollViewer, e.Delta))
        {
            return;
        }

        if (scrollViewer.ScrollableHeight <= 0) return;

        var state = GetOrCreateState(scrollViewer);
        if (!state.IsAnimating)
        {
            state.CurrentOffset = scrollViewer.VerticalOffset;
            state.TargetOffset = scrollViewer.VerticalOffset;
        }
        else
        {
            state.TargetOffset = Math.Clamp(state.TargetOffset, 0.0, scrollViewer.ScrollableHeight);
        }

        double scrollDelta = -(e.Delta / 120.0) * PixelsPerWheelNotch;
        state.TargetOffset = Math.Clamp(state.TargetOffset + scrollDelta, 0.0, scrollViewer.ScrollableHeight);

        StartAnimation(scrollViewer, state);
        e.Handled = true;
    }

    public static void ApplyWheelDelta(ScrollViewer scrollViewer, int mouseWheelDelta)
    {
        if (scrollViewer.ScrollableHeight <= 0) return;

        var state = GetOrCreateState(scrollViewer);
        if (!state.IsAnimating)
        {
            state.CurrentOffset = scrollViewer.VerticalOffset;
            state.TargetOffset = scrollViewer.VerticalOffset;
        }
        else
        {
            state.TargetOffset = Math.Clamp(state.TargetOffset, 0.0, scrollViewer.ScrollableHeight);
        }

        double scrollDelta = -(mouseWheelDelta / 120.0) * PixelsPerWheelNotch;
        state.TargetOffset = Math.Clamp(state.TargetOffset + scrollDelta, 0.0, scrollViewer.ScrollableHeight);
        StartAnimation(scrollViewer, state);
    }

    public static void StepForTesting(ScrollViewer scrollViewer, double dtSeconds)
    {
        if (scrollViewer.GetValue(ScrollStateProperty) is not ScrollState state || !state.IsAnimating)
        {
            return;
        }

        StepScrollViewer(scrollViewer, state, Math.Clamp(dtSeconds, 0.001, 0.05));
    }

    private static bool HasNestedScrollableViewer(DependencyObject current, ScrollViewer rootViewer, int wheelDelta)
    {
        while (current != null && !ReferenceEquals(current, rootViewer))
        {
            if (current is ScrollViewer nested && nested.ScrollableHeight > 0)
            {
                if (wheelDelta < 0 && nested.VerticalOffset < nested.ScrollableHeight) return true;
                if (wheelDelta > 0 && nested.VerticalOffset > 0) return true;
            }
            current = GetParentSafe(current);
        }
        return false;
    }

    private static DependencyObject? GetParentSafe(DependencyObject current)
    {
        if (current is Visual || current is System.Windows.Media.Media3D.Visual3D)
        {
            return VisualTreeHelper.GetParent(current);
        }
        if (current is FrameworkContentElement fce)
        {
            return fce.Parent;
        }
        return null;
    }

    private static void StartAnimation(ScrollViewer scrollViewer, ScrollState state)
    {
        state.LastTimestamp = Stopwatch.GetTimestamp();
        if (!state.IsAnimating)
        {
            state.IsAnimating = true;
            if (!ActiveScrollViewers.Contains(scrollViewer))
            {
                ActiveScrollViewers.Add(scrollViewer);
            }
        }

        if (!_isRenderingHooked && ActiveScrollViewers.Count > 0)
        {
            CompositionTarget.Rendering += OnCompositionTargetRendering;
            _isRenderingHooked = true;
        }
    }

    private static void StopAnimation(ScrollViewer scrollViewer, ScrollState state)
    {
        state.IsAnimating = false;
        ActiveScrollViewers.Remove(scrollViewer);

        if (_isRenderingHooked && ActiveScrollViewers.Count == 0)
        {
            CompositionTarget.Rendering -= OnCompositionTargetRendering;
            _isRenderingHooked = false;
        }
    }

    private static void OnCompositionTargetRendering(object? sender, EventArgs e)
    {
        long now = Stopwatch.GetTimestamp();

        for (int i = ActiveScrollViewers.Count - 1; i >= 0; i--)
        {
            var scrollViewer = ActiveScrollViewers[i];
            if (!scrollViewer.IsLoaded || scrollViewer.GetValue(ScrollStateProperty) is not ScrollState state || !state.IsAnimating)
            {
                if (scrollViewer.GetValue(ScrollStateProperty) is ScrollState s)
                {
                    s.IsAnimating = false;
                }
                ActiveScrollViewers.RemoveAt(i);
                continue;
            }

            double dt = (now - state.LastTimestamp) / (double)Stopwatch.Frequency;
            state.LastTimestamp = now;
            dt = Math.Clamp(dt, 0.001, 0.05);

            StepScrollViewer(scrollViewer, state, dt);
        }

        if (_isRenderingHooked && ActiveScrollViewers.Count == 0)
        {
            CompositionTarget.Rendering -= OnCompositionTargetRendering;
            _isRenderingHooked = false;
        }
    }

    private static void StepScrollViewer(ScrollViewer scrollViewer, ScrollState state, double dt)
    {
        if (scrollViewer.ScrollableHeight > 0)
        {
            state.TargetOffset = Math.Clamp(state.TargetOffset, 0.0, scrollViewer.ScrollableHeight);
        }

        double diff = state.TargetOffset - state.CurrentOffset;
        if (Math.Abs(diff) <= SnapThreshold)
        {
            state.CurrentOffset = state.TargetOffset;
            RequestInternalScroll(scrollViewer, state, state.CurrentOffset);
            StopAnimation(scrollViewer, state);
            return;
        }

        double factor = 1.0 - Math.Exp(-SmoothingSpeed * dt);
        state.CurrentOffset += diff * factor;
        RequestInternalScroll(scrollViewer, state, state.CurrentOffset);
    }
}
