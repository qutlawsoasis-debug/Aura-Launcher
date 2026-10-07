using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using AuraLauncher.ViewModels;

namespace AuraLauncher.Views;

public partial class UpdateWindow : Window
{
    private Storyboard? _spinnerStoryboard;

    public UpdateWindow()
    {
        InitializeComponent();
        Loaded += UpdateWindow_Loaded;
        DataContextChanged += UpdateWindow_DataContextChanged;
    }

    private void UpdateWindow_Loaded(object sender, RoutedEventArgs e)
    {
        StartSpinnerAnimation();
        if (DataContext is UpdateWindowViewModel vm)
        {
            UpdateProgressAnimation(vm);
        }
    }

    private void UpdateWindow_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is INotifyPropertyChanged oldVm)
        {
            oldVm.PropertyChanged -= Vm_PropertyChanged;
        }
        if (e.NewValue is UpdateWindowViewModel newVm)
        {
            newVm.PropertyChanged += Vm_PropertyChanged;
            UpdateProgressAnimation(newVm);
        }
    }

    private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (DataContext is UpdateWindowViewModel vm)
        {
            if (e.PropertyName == nameof(UpdateWindowViewModel.ProgressValue) ||
                e.PropertyName == nameof(UpdateWindowViewModel.HasDefiniteProgress) ||
                e.PropertyName == nameof(UpdateWindowViewModel.HasError))
            {
                UpdateProgressAnimation(vm);
            }
        }
    }

    private void StartSpinnerAnimation()
    {
        if (_spinnerStoryboard != null) return;

        var rotateAnim = new DoubleAnimation
        {
            From = 0.0,
            To = 360.0,
            Duration = TimeSpan.FromSeconds(1.0),
            RepeatBehavior = RepeatBehavior.Forever
        };

        Storyboard.SetTarget(rotateAnim, SpinnerRotate);
        Storyboard.SetTargetProperty(rotateAnim, new PropertyPath(System.Windows.Media.RotateTransform.AngleProperty));

        _spinnerStoryboard = new Storyboard();
        _spinnerStoryboard.Children.Add(rotateAnim);
        _spinnerStoryboard.Begin();
    }

    private void UpdateProgressAnimation(UpdateWindowViewModel vm)
    {
        if (DefiniteProgressBar == null) return;

        if (vm.HasError)
        {
            DefiniteProgressBar.BeginAnimation(FrameworkElement.WidthProperty, null);
            DefiniteProgressBar.Width = 0;
            return;
        }

        if (vm.HasDefiniteProgress)
        {
            double targetWidth = Math.Clamp((vm.ProgressValue / 100.0) * 220.0, 0, 220.0);
            var anim = new DoubleAnimation(targetWidth, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            DefiniteProgressBar.BeginAnimation(FrameworkElement.WidthProperty, anim);
        }
        else
        {
            DefiniteProgressBar.BeginAnimation(FrameworkElement.WidthProperty, null);
            DefiniteProgressBar.Width = 0;
        }
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
