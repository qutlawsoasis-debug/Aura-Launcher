using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace AuraLauncher.Views;

/// <summary>
/// Логика взаимодействия для OverviewView.xaml
/// </summary>
public partial class OverviewView : UserControl
{
    public OverviewView()
    {
        InitializeComponent();
        Loaded += (s, e) => AnimateFeedEntrance();
        IsVisibleChanged += (s, e) =>
        {
            if (IsVisible)
            {
                AnimateFeedEntrance();
            }
        };
    }

    public void AnimateFeedEntrance()
    {
        var blocks = new List<UIElement?> { FeedBlockScreenshot, FeedBlockAchievement, FeedBlockWhatsNew };
        int visibleIndex = 0;

        foreach (var block in blocks)
        {
            if (block == null || block.Visibility != Visibility.Visible)
            {
                continue;
            }

            int delayMs = visibleIndex * 60;
            visibleIndex++;

            if (block.RenderTransform is not TranslateTransform tt)
            {
                tt = new TranslateTransform(16, 0);
                block.RenderTransform = tt;
            }

            block.Opacity = 0;
            tt.X = 16;

            var opacityAnim = new DoubleAnimation
            {
                From = 0.0,
                To = 1.0,
                Duration = TimeSpan.FromMilliseconds(220),
                BeginTime = TimeSpan.FromMilliseconds(delayMs),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            var slideAnim = new DoubleAnimation
            {
                From = 16.0,
                To = 0.0,
                Duration = TimeSpan.FromMilliseconds(220),
                BeginTime = TimeSpan.FromMilliseconds(delayMs),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            block.BeginAnimation(UIElement.OpacityProperty, opacityAnim);
            tt.BeginAnimation(TranslateTransform.XProperty, slideAnim);
        }
    }

    private void ScreenshotImage_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is UIElement el && e.NewSize.Width > 0 && e.NewSize.Height > 0)
        {
            el.Clip = new RectangleGeometry(new Rect(0, 0, e.NewSize.Width, e.NewSize.Height), 3, 3);
        }
    }
}
