using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using AuraLauncher.ViewModels;

namespace AuraLauncher.Controls;

public partial class AchievementsSlide : UserControl, ISlideLifecycle
{
    public AchievementsSlide()
    {
        InitializeComponent();
    }

    public void OnEntered()
    {
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

    public void OnExited()
    {
        if (NextAchProgressBar != null)
        {
            NextAchProgressBar.BeginAnimation(FrameworkElement.WidthProperty, null);
        }
    }
}
