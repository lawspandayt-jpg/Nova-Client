using System.Windows.Controls;
using System.Windows.Input;
using NovaClient.Launcher.ViewModels;

namespace NovaClient.Launcher.Views;

public partial class HomeView : UserControl
{
    public HomeView()
    {
        InitializeComponent();
    }

    private void AddFriendBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if (DataContext is HomeViewModel vm && vm.AddFriendCommand.CanExecute(null))
            vm.AddFriendCommand.Execute(null);
    }
}
