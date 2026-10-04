using System.Windows;

namespace AuraLauncher;

/// <summary>
/// Чистый code-behind главного окна без бизнес-логики (Zero Code-Behind).
/// Все взаимодействия осуществляются через Data Binding и команды MainViewModel.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }
}