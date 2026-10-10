using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AuraLauncher.ViewModels;

namespace AuraLauncher.Views;

/// <summary>
/// Логика взаимодействия для SettingsView.xaml
/// </summary>
public partial class SettingsView : UserControl
{
    private string _previousRamText = "";
    private Storyboard? _savedToastStoryboard;
    private bool _isSliderAnimating = false;

    public SettingsView()
    {
        InitializeComponent();
        DataContextChanged += SettingsView_DataContextChanged;
    }

    private Storyboard? _presetAppliedStoryboard;

    private void SettingsView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is SettingsViewModel oldVm)
        {
            oldVm.ConfigSaved -= TriggerSavedToastAnimation;
            oldVm.PresetApplied -= TriggerPresetAppliedAnimation;
        }
        if (e.NewValue is SettingsViewModel newVm)
        {
            newVm.ConfigSaved += TriggerSavedToastAnimation;
            newVm.PresetApplied += TriggerPresetAppliedAnimation;
        }
    }

    private void TriggerPresetAppliedAnimation()
    {
        _presetAppliedStoryboard?.Stop();

        var sb = new Storyboard();

        var fadeIn = new DoubleAnimation
        {
            From = PresetAppliedToastText.Opacity,
            To = 1.0,
            Duration = TimeSpan.FromMilliseconds(250),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(fadeIn, PresetAppliedToastText);
        Storyboard.SetTargetProperty(fadeIn, new PropertyPath(UIElement.OpacityProperty));
        sb.Children.Add(fadeIn);

        var fadeOut = new DoubleAnimation
        {
            From = 1.0,
            To = 0.0,
            BeginTime = TimeSpan.FromMilliseconds(1750),
            Duration = TimeSpan.FromMilliseconds(400),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        Storyboard.SetTarget(fadeOut, PresetAppliedToastText);
        Storyboard.SetTargetProperty(fadeOut, new PropertyPath(UIElement.OpacityProperty));
        sb.Children.Add(fadeOut);

        _presetAppliedStoryboard = sb;
        sb.Begin();
    }

    private void TriggerSavedToastAnimation()
    {
        _savedToastStoryboard?.Stop();

        var sb = new Storyboard();

        // 1. Fade-in: Opacity 0 -> 1 over 250 ms (CubicEase EaseOut)
        var fadeIn = new DoubleAnimation
        {
            From = SavedToastText.Opacity,
            To = 1.0,
            Duration = TimeSpan.FromMilliseconds(250),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(fadeIn, SavedToastText);
        Storyboard.SetTargetProperty(fadeIn, new PropertyPath(UIElement.OpacityProperty));
        sb.Children.Add(fadeIn);

        // 2. Hold 1.5s (250ms + 1500ms = 1750ms), Fade-out: 1 -> 0 over 400 ms (CubicEase EaseIn)
        var fadeOut = new DoubleAnimation
        {
            From = 1.0,
            To = 0.0,
            BeginTime = TimeSpan.FromMilliseconds(1750),
            Duration = TimeSpan.FromMilliseconds(400),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        Storyboard.SetTarget(fadeOut, SavedToastText);
        Storyboard.SetTargetProperty(fadeOut, new PropertyPath(UIElement.OpacityProperty));
        sb.Children.Add(fadeOut);

        _savedToastStoryboard = sb;
        sb.Begin();
    }

    private void OnRamChipClick(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton btn && btn.Tag != null)
        {
            if (double.TryParse(btn.Tag.ToString(), out double targetGb))
            {
                AnimateRamSliderTo(targetGb);
            }
        }
    }

    private void OnRamSliderMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isSliderAnimating) return;
        double currentVal = RamSlider.Value;
        double targetGb = Math.Round(currentVal);
        if (targetGb < 2) targetGb = 2;
        if (targetGb > 16) targetGb = 16;
        AnimateRamSliderTo(targetGb);
    }

    private void AnimateRamSliderTo(double targetGb)
    {
        _isSliderAnimating = true;
        var anim = new DoubleAnimation
        {
            From = RamSlider.Value,
            To = targetGb,
            Duration = TimeSpan.FromMilliseconds(350),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };

        anim.Completed += (s, e) =>
        {
            RamSlider.Value = targetGb;
            _isSliderAnimating = false;
            if (DataContext is SettingsViewModel vm)
            {
                vm.RamGb = (int)Math.Round(targetGb);
            }
        };

        RamSlider.BeginAnimation(Slider.ValueProperty, anim);
    }

    private void OnRamGbTargetUpdated(object sender, System.Windows.Data.DataTransferEventArgs e)
    {
        if (sender is TextBlock tb)
        {
            string newText = tb.Text;
            if (!string.IsNullOrEmpty(_previousRamText) && _previousRamText != newText && OldRamNumberText != null)
            {
                OldRamNumberText.Text = _previousRamText;

                // Stop previous animations
                OldRamNumberText.BeginAnimation(UIElement.OpacityProperty, null);
                OldRamScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                OldRamScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                if (OldRamBlur != null)
                {
                    OldRamBlur.BeginAnimation(System.Windows.Media.Effects.BlurEffect.RadiusProperty, null);
                }

                // Old number: Opacity 1 -> 0, Scale 1 -> 0.85, Blur 0 -> 6 (300 ms)
                var oldOpacity = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(300))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                var oldScaleX = new DoubleAnimation(1.0, 0.85, TimeSpan.FromMilliseconds(300))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                var oldScaleY = new DoubleAnimation(1.0, 0.85, TimeSpan.FromMilliseconds(300))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                var oldBlur = new DoubleAnimation(0.0, 6.0, TimeSpan.FromMilliseconds(300))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };

                OldRamNumberText.BeginAnimation(UIElement.OpacityProperty, oldOpacity);
                OldRamScale.BeginAnimation(ScaleTransform.ScaleXProperty, oldScaleX);
                OldRamScale.BeginAnimation(ScaleTransform.ScaleYProperty, oldScaleY);
                if (OldRamBlur != null)
                {
                    OldRamBlur.BeginAnimation(System.Windows.Media.Effects.BlurEffect.RadiusProperty, oldBlur);
                }

                // New number: Opacity 0 -> 1, Scale 0.85 -> 1, Blur 6 -> 0 (300 ms)
                RamNumberText.BeginAnimation(UIElement.OpacityProperty, null);
                RamScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                RamScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                if (RamBlur != null)
                {
                    RamBlur.BeginAnimation(System.Windows.Media.Effects.BlurEffect.RadiusProperty, null);
                }

                var newOpacity = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(300))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                var newScaleX = new DoubleAnimation(0.85, 1.0, TimeSpan.FromMilliseconds(300))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                var newScaleY = new DoubleAnimation(0.85, 1.0, TimeSpan.FromMilliseconds(300))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                var newBlur = new DoubleAnimation(6.0, 0.0, TimeSpan.FromMilliseconds(300))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };

                RamNumberText.BeginAnimation(UIElement.OpacityProperty, newOpacity);
                RamScale.BeginAnimation(ScaleTransform.ScaleXProperty, newScaleX);
                RamScale.BeginAnimation(ScaleTransform.ScaleYProperty, newScaleY);
                if (RamBlur != null)
                {
                    RamBlur.BeginAnimation(System.Windows.Media.Effects.BlurEffect.RadiusProperty, newBlur);
                }
            }
            _previousRamText = newText;
        }
    }

    private void OnTextBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox tb)
        {
            tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            Keyboard.ClearFocus();
            e.Handled = true;
        }
    }
}
