using System;
using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace AuraLauncher.Behaviors;

/// <summary>
/// Attached property to enable physics-based smooth scrolling on WPF ScrollViewer.
/// Replaces jerky 48px discrete jumps with a silky CubicEase interpolated animation.
/// </summary>
public static class SmoothScroll
{
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

    private static readonly DependencyProperty TargetOffsetProperty =
        DependencyProperty.RegisterAttached(
            "TargetOffset",
            typeof(double),
            typeof(SmoothScroll),
            new PropertyMetadata(0.0));

    public static bool GetIsEnabled(DependencyObject obj) => (bool)obj.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject obj, bool value) => obj.SetValue(IsEnabledProperty, value);

    public static double GetSmoothVerticalOffset(DependencyObject obj) => (double)obj.GetValue(SmoothVerticalOffsetProperty);
    public static void SetSmoothVerticalOffset(DependencyObject obj, double value) => obj.SetValue(SmoothVerticalOffsetProperty, value);

    private static double GetTargetOffset(DependencyObject obj) => (double)obj.GetValue(TargetOffsetProperty);
    private static void SetTargetOffset(DependencyObject obj, double value) => obj.SetValue(TargetOffsetProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ScrollViewer scrollViewer) return;

        if ((bool)e.NewValue)
        {
            scrollViewer.PreviewMouseWheel += ScrollViewer_PreviewMouseWheel;
        }
        else
        {
            scrollViewer.PreviewMouseWheel -= ScrollViewer_PreviewMouseWheel;
        }
    }

    private static void OnSmoothVerticalOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ScrollViewer scrollViewer)
        {
            scrollViewer.ScrollToVerticalOffset((double)e.NewValue);
        }
    }

    private static void ScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer) return;
        if (scrollViewer.ScrollableHeight <= 0) return;

        double currentTarget = GetTargetOffset(scrollViewer);
        double currentActual = scrollViewer.VerticalOffset;

        // If target is out of sync or far from current actual, reset target to actual
        if (Math.Abs(currentTarget - currentActual) > 300 || currentTarget < 0 || currentTarget > scrollViewer.ScrollableHeight)
        {
            currentTarget = currentActual;
        }

        // Standard delta is 120 per notch; scale to a comfortable 110px per wheel step
        double scrollDelta = -(e.Delta / 120.0) * 110.0;
        double newTarget = Math.Clamp(currentTarget + scrollDelta, 0, scrollViewer.ScrollableHeight);
        SetTargetOffset(scrollViewer, newTarget);

        var anim = new DoubleAnimation
        {
            From = currentActual,
            To = newTarget,
            Duration = TimeSpan.FromMilliseconds(220),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        SetSmoothVerticalOffset(scrollViewer, currentActual);
        scrollViewer.BeginAnimation(SmoothVerticalOffsetProperty, anim, HandoffBehavior.SnapshotAndReplace);

        e.Handled = true;
    }
}
