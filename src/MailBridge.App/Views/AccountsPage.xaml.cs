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

    private async void OnRemoveAccountClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not EmailAccount account)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Remove account?",
            Content = $"Remove '{account.DisplayName}' ({account.Username} on {account.Host})? "
                      + "Its stored password will be deleted and any scheduled backup for it will no longer run.",
            PrimaryButtonText = "Remove",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            ViewModel.RemoveAccountCommand.Execute(account);
        }
    }

    private Visibility ToVisibility(bool value) =>
        value ? Visibility.Visible : Visibility.Collapsed;
}
