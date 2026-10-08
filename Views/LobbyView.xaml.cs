using System.Windows;
using System.Windows.Controls;
using AuraLauncher.ViewModels;

namespace AuraLauncher.Views;

/// <summary>
/// Логика взаимодействия для LobbyView.xaml
/// </summary>
public partial class LobbyView : UserControl
{
    public LobbyView()
    {
        InitializeComponent();
        Loaded += OnLobbyViewLoaded;
        IsVisibleChanged += OnLobbyViewVisibleChanged;
    }

    private void OnLobbyViewLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is LobbyViewModel vm)
        {
            vm.RefreshLocalPlayerModel();
        }
    }

    private void OnLobbyViewVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible && DataContext is LobbyViewModel vm)
        {
            vm.RefreshLocalPlayerModel();
        }
    }
}
