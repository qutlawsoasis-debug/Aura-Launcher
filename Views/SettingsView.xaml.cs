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
