using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MailBridge.Core.Models;
using MailBridge.Core.Security;
using MailBridge.Core.Services;

namespace MailBridge.App.ViewModels;

public partial class AccountsViewModel : ObservableObject
{
    private readonly ICredentialStore _credentialStore;
    private readonly AccountStore _accountStore;
    private readonly ImapConnectionService _imapConnectionService;

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

    [ObservableProperty]
    private string connectionStatus = string.Empty;

    [ObservableProperty]
    private Guid? editingAccountId;

    public bool IsEditing => EditingAccountId is not null;

    public string FormTitle => IsEditing
        ? Localization.Strings.Get("accounts.saveChanges")
        : Localization.Strings.Get("accounts.add");

    partial void OnEditingAccountIdChanged(Guid? value)
    {
        OnPropertyChanged(nameof(IsEditing));
        OnPropertyChanged(nameof(FormTitle));
    }

    public AccountsViewModel(ICredentialStore credentialStore, AccountStore accountStore, ImapConnectionService imapConnectionService)
    {
        _credentialStore = credentialStore;
        _accountStore = accountStore;
        _imapConnectionService = imapConnectionService;

        foreach (var account in _accountStore.Load())
        {
            Accounts.Add(account);
        }
    }

    [RelayCommand]
    private void SaveAccount()
    {
        if (string.IsNullOrWhiteSpace(Host) || string.IsNullOrWhiteSpace(Username))
        {
            return;
        }

        if (EditingAccountId is { } editingId)
        {
            UpdateAccount(editingId);
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
        _accountStore.Save(Accounts);
        ResetForm();
    }

    public void BeginEdit(EmailAccount account)
    {
        EditingAccountId = account.Id;
        DisplayName = account.DisplayName;
        Host = account.Host;
        Port = account.Port;
        Username = account.Username;
        Password = string.Empty;
    }

    [RelayCommand]
    private void CancelEdit() => ResetForm();

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        if (string.IsNullOrWhiteSpace(Host) || string.IsNullOrWhiteSpace(Username))
        {
            ConnectionStatus = Localization.Strings.Get("accounts.enterHostUser");
            return;
        }

        var password = Password;
        if (string.IsNullOrEmpty(password) && EditingAccountId is { } editingId)
        {
            var existing = Accounts.FirstOrDefault(a => a.Id == editingId);
            password = existing is null ? null : _credentialStore.TryGet(existing.CredentialKey, existing.Username);
        }

        if (string.IsNullOrEmpty(password))
        {
            ConnectionStatus = Localization.Strings.Get("accounts.noPasswordTyped");
            return;
        }

        var testAccount = new EmailAccount
        {
            Host = Host,
            Port = Port,
            Username = Username,
            UseSsl = true,
        };

        ConnectionStatus = Localization.Strings.Get("accounts.connecting", Host, Port);
        try
        {
            using var client = await _imapConnectionService.ConnectAsync(testAccount, password);
            var folders = await _imapConnectionService.GetAllFoldersAsync(client);
            ConnectionStatus = Localization.Strings.Get("accounts.connOk", Username, folders.Count);
        }
        catch (Exception ex)
        {
            ConnectionStatus = Localization.Strings.Get("accounts.connFailed", ex.Message);
        }
    }

    private void UpdateAccount(Guid id)
    {
        var account = Accounts.FirstOrDefault(a => a.Id == id);
        if (account is null)
        {
            ResetForm();
            return;
        }

        var newKey = $"MailBridge:{Host}:{Username}";
        var keyChanged = !string.Equals(newKey, account.CredentialKey, StringComparison.Ordinal);
        var existingSecret = keyChanged
            ? _credentialStore.TryGet(account.CredentialKey, account.Username)
            : null;

        if (!string.IsNullOrEmpty(Password))
        {
            _credentialStore.Save(newKey, Username, Password);
        }
        else if (keyChanged && existingSecret is not null)
        {
            _credentialStore.Save(newKey, Username, existingSecret);
        }

        if (keyChanged && (existingSecret is not null || !string.IsNullOrEmpty(Password)))
        {
            _credentialStore.Remove(account.CredentialKey, account.Username);
        }

        account.DisplayName = string.IsNullOrWhiteSpace(DisplayName) ? Username : DisplayName;
        account.Host = Host;
        account.Port = Port;
        account.Username = Username;
        account.CredentialKey = newKey;

        var index = Accounts.IndexOf(account);
        Accounts[index] = account;
        _accountStore.Save(Accounts);
        ResetForm();
    }

    private void ResetForm()
    {
        EditingAccountId = null;
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
        _accountStore.Save(Accounts);
    }
}
