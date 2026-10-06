using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AuraLauncher.ViewModels;

namespace AuraLauncher.Views;

public partial class UpdateWindow : Window
{
    private Storyboard? _slidingStoryboard;

    public UpdateWindow()
    {
        InitializeComponent();
        Loaded += UpdateWindow_Loaded;
        DataContextChanged += UpdateWindow_DataContextChanged;
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

    private void UpdateWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is UpdateWindowViewModel vm)
        {
            UpdateProgressAnimation(vm);
        }
        else
        {
            StartSlidingAnimation();
        }
    }

    private void UpdateProgressAnimation(UpdateWindowViewModel vm)
    {
        if (vm.HasError)
        {
            StopSlidingAnimation();
            DefiniteProgressBar.BeginAnimation(FrameworkElement.WidthProperty, null);
            DefiniteProgressBar.Width = 0;
            return;
        }

        if (vm.HasDefiniteProgress)
        {
            StopSlidingAnimation();
            double targetWidth = Math.Clamp((vm.ProgressValue / 100.0) * 440.0, 0, 440.0);
            
            // Плавное заполнение за 200 мс
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
            StartSlidingAnimation();
        }
    }

    private void StartSlidingAnimation()
    {
        if (_slidingStoryboard != null) return;

        // По линии скользит отрезок 120px, 2 с, EaseInOut, цикл
        SlidingChunk.Width = 120;
        var anim = new DoubleAnimation
        {
            From = -120.0,
            To = 440.0,
            Duration = TimeSpan.FromSeconds(2),
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };

        Storyboard.SetTarget(anim, ChunkTranslate);
        Storyboard.SetTargetProperty(anim, new PropertyPath(TranslateTransform.XProperty));

        _slidingStoryboard = new Storyboard();
        _slidingStoryboard.Children.Add(anim);
        _slidingStoryboard.Begin();
    }

    private void StopSlidingAnimation()
    {
        if (_slidingStoryboard != null)
        {
            _slidingStoryboard.Stop();
            _slidingStoryboard = null;
        }
    }

    private void Window_MouseDown(object sender, MouseButtonEventArgs e)
    {
        // Перетаскивание за любое место окна
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
