using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AuraLauncher.Models;
using AuraLauncher.ViewModels;

namespace AuraLauncher.Controls;

public partial class AchievementsSlide : UserControl, ISlideLifecycle
{
    private bool _isListView = true;

    public AchievementsSlide()
    {
        InitializeComponent();
    }

    public void OnEntered()
    {
        if (DataContext is OverviewViewModel ovm)
        {
            ovm.RefreshStoriesData();
        }
    }

    public void OnExited()
    {
        if (AchievementPopup != null)
        {
            AchievementPopup.IsOpen = false;
        }
    }

    private void OnToggleViewModeClick(object sender, RoutedEventArgs e)
    {
        _isListView = !_isListView;
        SwitchMode(_isListView);
    }

    private void SwitchMode(bool isList)
    {
        var fadeOut = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(125))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        var fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(125))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        UIElement outgoing = isList ? GridViewContainer : ListViewContainer;
        UIElement incoming = isList ? ListViewContainer : GridViewContainer;

        fadeOut.Completed += (s, ev) =>
        {
            outgoing.Visibility = Visibility.Collapsed;
            incoming.Opacity = 0.0;
            incoming.Visibility = Visibility.Visible;
            incoming.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        };

        outgoing.BeginAnimation(UIElement.OpacityProperty, fadeOut);

        ViewModeText.Text = isList ? "Сетка" : "Список";
        if (TryFindResource(isList ? "IconGrid" : "IconList") is Geometry geom)
        {
            ViewModeIcon.Data = geom;
        }
    }

    private void OnAchievementMouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement elem && elem.Tag is AchievementDisplayItem item)
        {
            PopupTitle.Text = item.Title;
            PopupDesc.Text = item.Description;
            PopupStatus.Text = item.StatusText;
            AchievementPopup.PlacementTarget = elem;
            AchievementPopup.IsOpen = true;
        }
    }

    private void OnAchievementMouseLeave(object sender, MouseEventArgs e)
    {
        if (AchievementPopup != null)
        {
            AchievementPopup.IsOpen = false;
        }
    }
}
