using System.Windows.Controls;
using System.Windows.Input;
using AuraLauncher.ViewModels;

namespace AuraLauncher.Views;

public partial class FriendsView : UserControl
{
    public FriendsView()
    {
        InitializeComponent();
    }

    private void OnAddFriendCodeKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (DataContext is FriendsViewModel vm && vm.CanAddFriend)
            {
                vm.AddFriendCommand.Execute(null);
            }
        }
    }
}
