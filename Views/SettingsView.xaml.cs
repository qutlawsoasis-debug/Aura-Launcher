using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace AuraLauncher.Views;

/// <summary>
/// Логика взаимодействия для SettingsView.xaml
/// </summary>
public partial class SettingsView : UserControl
{
    private bool _isLoaded;

    public SettingsView()
    {
        InitializeComponent();
        Loaded += SettingsView_Loaded;
        SizeChanged += (s, e) => UpdateStartModeUnderline(animated: false);
    }

    private void SettingsView_Loaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = true;
        Dispatcher.InvokeAsync(() => UpdateStartModeUnderline(animated: false), DispatcherPriority.Loaded);
    }

    private void OnStartModeChecked(object sender, RoutedEventArgs e)
    {
        UpdateStartModeUnderline(animated: _isLoaded);
    }

    private void UpdateStartModeUnderline(bool animated)
    {
        if (StartModeUnderline == null || StartModeWindowedRadio == null || StartModeMaximizedRadio == null) return;

        double w1 = StartModeWindowedRadio.ActualWidth;
        double w2 = StartModeMaximizedRadio.ActualWidth;
        if (w1 <= 0 || w2 <= 0) return;

        StartModeUnderline.Width = w1;

        bool isWindowed = StartModeWindowedRadio.IsChecked == true;
        double targetX = isWindowed ? 0 : (w1 + 28);
        double targetScale = isWindowed ? 1.0 : (w2 / w1);

        if (!animated)
        {
            StartModeUnderlineTrans.BeginAnimation(TranslateTransform.XProperty, null);
            StartModeUnderlineScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            StartModeUnderlineTrans.X = targetX;
            StartModeUnderlineScale.ScaleX = targetScale;
        }
        else
        {
            var animX = new DoubleAnimation(targetX, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            var animScale = new DoubleAnimation(targetScale, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            StartModeUnderlineTrans.BeginAnimation(TranslateTransform.XProperty, animX);
            StartModeUnderlineScale.BeginAnimation(ScaleTransform.ScaleXProperty, animScale);
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
