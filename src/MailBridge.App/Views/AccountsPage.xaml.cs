using MailBridge.App.ViewModels;
using MailBridge.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MailBridge.App.Views;

public sealed partial class AccountsPage : Page
{
    public AccountsViewModel ViewModel => AppState.Accounts;

    public AccountsPage()
    {
        InitializeComponent();
    }

    private void OnEditAccountClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is EmailAccount account)
        {
            ViewModel.BeginEdit(account);
        }
    }

    private void OnRemoveAccountClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is EmailAccount account)
        {
            ViewModel.RemoveAccountCommand.Execute(account);
        }
    }

    private Visibility ToVisibility(bool value) =>
        value ? Visibility.Visible : Visibility.Collapsed;
}
