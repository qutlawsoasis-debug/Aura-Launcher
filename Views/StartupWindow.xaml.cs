using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using AuraLauncher.ViewModels;

namespace AuraLauncher.Views;

public partial class StartupWindow : Window
{
    public StartupWindow()
    {
        InitializeComponent();
        DataContextChanged += StartupWindow_DataContextChanged;
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

    private void UpdateProgress(double progress)
    {
        if (ProgressBarFill == null) return;
        double clamped = Math.Clamp(progress, 0.0, 100.0);
        double targetWidth = (clamped / 100.0) * 240.0;

        var anim = new DoubleAnimation(targetWidth, TimeSpan.FromMilliseconds(200))
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
