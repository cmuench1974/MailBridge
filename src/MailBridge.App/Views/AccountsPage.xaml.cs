using MailBridge.App.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace MailBridge.App.Views;

public sealed partial class AccountsPage : Page
{
    public AccountsViewModel ViewModel => AppState.Accounts;

    public AccountsPage()
    {
        InitializeComponent();
    }
}
