using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media.Animation;
using AuraLauncher.ViewModels;

namespace AuraLauncher.Views;

public partial class StartupWindow : Window
{
    private string _previousStatusText = "";

    public StartupWindow()
    {
        InitializeComponent();
        DataContextChanged += StartupWindow_DataContextChanged;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // 1. Плавное появление логотипа "Aura" (fade-in + подъём на 10px, 500 мс)
        AuraLogoHost.Opacity = 0.0;
        AuraLogoTranslate.Y = 10.0;

        var logoFade = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(500))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        var logoRise = new DoubleAnimation(10.0, 0.0, TimeSpan.FromMilliseconds(500))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        AuraLogoHost.BeginAnimation(UIElement.OpacityProperty, logoFade);
        AuraLogoTranslate.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, logoRise);
    }

    private void StartupWindow_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is INotifyPropertyChanged oldVm)
        {
            oldVm.PropertyChanged -= Vm_PropertyChanged;
        }
        if (e.NewValue is StartupWindowViewModel newVm)
        {
            newVm.PropertyChanged += Vm_PropertyChanged;
            UpdateProgress(newVm.ProgressValue);
        }
    }

    private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (DataContext is StartupWindowViewModel vm)
        {
            if (e.PropertyName == nameof(StartupWindowViewModel.ProgressValue))
            {
                UpdateProgress(vm.ProgressValue);
            }
        }
    }

    private void OnStatusTextTargetUpdated(object sender, DataTransferEventArgs e)
    {
        if (sender is TextBlock tb)
        {
            string newText = tb.Text;
            if (!string.IsNullOrEmpty(_previousStatusText) && _previousStatusText != newText && OldStatusTextBlock != null)
            {
                OldStatusTextBlock.Text = _previousStatusText;

                OldStatusTextBlock.BeginAnimation(UIElement.OpacityProperty, null);
                StatusTextBlock.BeginAnimation(UIElement.OpacityProperty, null);

                // 3. Смена текста статуса через crossfade 200 мс
                var fadeOut = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(200))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                var fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(200))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };

                OldStatusTextBlock.BeginAnimation(UIElement.OpacityProperty, fadeOut);
                StatusTextBlock.BeginAnimation(UIElement.OpacityProperty, fadeIn);
            }
            _previousStatusText = newText;
        }
    }

    private void UpdateProgress(double progress)
    {
        if (ProgressBarFill == null) return;
        double clamped = Math.Clamp(progress, 0.0, 100.0);
        double trackWidth = ProgressTrack != null && ProgressTrack.ActualWidth > 0
            ? ProgressTrack.ActualWidth
            : 258.0;
        double targetWidth = (clamped / 100.0) * trackWidth;

        double currentWidth = ProgressBarFill.ActualWidth;
        if (double.IsNaN(currentWidth) || currentWidth < 0) currentWidth = 0.0;

        var anim = new DoubleAnimation(currentWidth, targetWidth, TimeSpan.FromMilliseconds(200))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        ProgressBarFill.BeginAnimation(FrameworkElement.WidthProperty, anim);
    }

    private void Window_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            try
            {
                DragMove();
            }
            catch { }
        }
    }
}
