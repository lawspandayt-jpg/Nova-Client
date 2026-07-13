using System.Windows.Controls;
using System.Windows.Input;
using NovaClient.Launcher.ViewModels;

namespace NovaClient.Launcher.Views;

public partial class LoginView : UserControl
{
    public LoginView()
    {
        InitializeComponent();
    }

    private void EmailBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if (DataContext is LoginViewModel vm && vm.ContinueCommand.CanExecute(null))
            vm.ContinueCommand.Execute(null);
    }
}
