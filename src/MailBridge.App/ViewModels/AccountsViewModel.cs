using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MailBridge.Core.Models;
using MailBridge.Core.Security;

namespace MailBridge.App.ViewModels;

public partial class AccountsViewModel : ObservableObject
{
    private readonly ICredentialStore _credentialStore;

    public ObservableCollection<EmailAccount> Accounts { get; } = new();

    [ObservableProperty]
    private string displayName = string.Empty;

    [ObservableProperty]
    private string host = string.Empty;

    [ObservableProperty]
    private int port = 993;

    [ObservableProperty]
    private string username = string.Empty;

    [ObservableProperty]
    private string password = string.Empty;

    public AccountsViewModel(ICredentialStore credentialStore)
    {
        _credentialStore = credentialStore;
    }

    [RelayCommand]
    private void AddAccount()
    {
        if (string.IsNullOrWhiteSpace(Host) || string.IsNullOrWhiteSpace(Username))
        {
            return;
        }

        var credentialKey = $"MailBridge:{Host}:{Username}";
        var account = new EmailAccount
        {
            DisplayName = string.IsNullOrWhiteSpace(DisplayName) ? Username : DisplayName,
            Host = Host,
            Port = Port,
            Username = Username,
            CredentialKey = credentialKey,
        };

        if (!string.IsNullOrEmpty(Password))
        {
            _credentialStore.Save(credentialKey, Username, Password);
        }

        Accounts.Add(account);

        DisplayName = string.Empty;
        Host = string.Empty;
        Port = 993;
        Username = string.Empty;
        Password = string.Empty;
    }

    [RelayCommand]
    private void RemoveAccount(EmailAccount account)
    {
        _credentialStore.Remove(account.CredentialKey, account.Username);
        Accounts.Remove(account);
    }
}
