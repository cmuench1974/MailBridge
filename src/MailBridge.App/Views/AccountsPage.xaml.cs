using MailBridge.App.Localization;
using MailBridge.App.ViewModels;
using MailBridge.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MailBridge.App.Views;

public sealed partial class AccountsPage : Page
{
    public string L(string key) => Localization.Strings.Get(key);
    public AccountsViewModel ViewModel => AppState.Accounts;

    public AccountsPage()
    {
        Resources["EditLabel"] = Strings.Get("accounts.edit");
        Resources["RemoveLabel"] = Strings.Get("accounts.remove");

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
            Title = Strings.Get("accounts.removeTitle"),
            Content = Strings.Get(
                "accounts.removeBody",
                account.DisplayName, account.Username, account.Host),
            PrimaryButtonText = Strings.Get("accounts.remove"),
            CloseButtonText = Strings.Get("common.cancel"),
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
