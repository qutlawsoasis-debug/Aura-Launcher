using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using AuraLauncher.ViewModels;

namespace AuraLauncher.Views;

/// <summary>
/// Логика взаимодействия для OverviewView.xaml.
/// Правая колонка растянута сверху вниз и отображает 3–4 карточки с картинками в зависимости от разрешения экрана.
/// </summary>
public partial class OverviewView : UserControl
{
    private Window? _parentWindow;

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
        }
    }

    private void OverviewView_Unloaded(object sender, RoutedEventArgs e)
    {
        DetachWindow();
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
            }
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

    private void AnimateEntrance()
    {
        if (StoriesRail == null || StoriesRailTranslate == null) return;

        StoriesRail.BeginAnimation(UIElement.OpacityProperty, null);
        StoriesRailTranslate.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, null);

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
        StoriesRailTranslate.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, translateAnim);
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

        double availableHeight = ActualHeight > 0 ? ActualHeight : Math.Max(0, _parentWindow.ActualHeight - 108);
        bool showFourthCard = availableHeight >= 740;

        if (CardNextGoalImageRow != null &&
            CardNextGoalTextRow != null &&
            CardNextGoalDividerRow != null &&
            CardNextGoalImage != null &&
            CardNextGoalText != null &&
            CardNextGoalDivider != null)
        {
            if (showFourthCard)
            {
                CardNextGoalImageRow.MinHeight = 64;
                CardNextGoalImageRow.Height = new GridLength(1, GridUnitType.Star);
                CardNextGoalTextRow.Height = GridLength.Auto;
                CardNextGoalDividerRow.Height = GridLength.Auto;
                CardNextGoalImage.Visibility = Visibility.Visible;
                CardNextGoalText.Visibility = Visibility.Visible;
                CardNextGoalDivider.Visibility = Visibility.Visible;
            }
            else
            {
                CardNextGoalImageRow.MinHeight = 0;
                CardNextGoalImageRow.Height = new GridLength(0);
                CardNextGoalTextRow.Height = new GridLength(0);
                CardNextGoalDividerRow.Height = new GridLength(0);
                CardNextGoalImage.Visibility = Visibility.Collapsed;
                CardNextGoalText.Visibility = Visibility.Collapsed;
                CardNextGoalDivider.Visibility = Visibility.Collapsed;
            }
        }
    }
}
