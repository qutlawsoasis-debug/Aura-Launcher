using System.Windows.Controls;
using System.Windows.Input;

namespace AuraLauncher.Views;

/// <summary>
/// Логика взаимодействия для SettingsView.xaml
/// </summary>
public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    private string _previousRamText = "";

    private void OnRamGbTargetUpdated(object sender, System.Windows.Data.DataTransferEventArgs e)
    {
        if (sender is TextBlock tb)
        {
            string newText = tb.Text;
            if (!string.IsNullOrEmpty(_previousRamText) && _previousRamText != newText && OldRamNumberText != null)
            {
                OldRamNumberText.Text = _previousRamText;

                OldRamNumberText.BeginAnimation(System.Windows.UIElement.OpacityProperty, null);
                OldRamTranslate.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, null);

                var oldOpacity = new System.Windows.Media.Animation.DoubleAnimation(1.0, 0.0, System.TimeSpan.FromMilliseconds(260))
                {
                    EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
                };
                var oldTranslate = new System.Windows.Media.Animation.DoubleAnimation(0.0, -20.0, System.TimeSpan.FromMilliseconds(260))
                {
                    EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
                };

                OldRamNumberText.BeginAnimation(System.Windows.UIElement.OpacityProperty, oldOpacity);
                OldRamTranslate.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, oldTranslate);

                RamNumberText.BeginAnimation(System.Windows.UIElement.OpacityProperty, null);
                RamTranslate.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, null);

                var newOpacity = new System.Windows.Media.Animation.DoubleAnimation(0.0, 1.0, System.TimeSpan.FromMilliseconds(260))
                {
                    EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
                };
                var newTranslate = new System.Windows.Media.Animation.DoubleAnimation(20.0, 0.0, System.TimeSpan.FromMilliseconds(260))
                {
                    EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
                };

                RamNumberText.BeginAnimation(System.Windows.UIElement.OpacityProperty, newOpacity);
                RamTranslate.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, newTranslate);
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
