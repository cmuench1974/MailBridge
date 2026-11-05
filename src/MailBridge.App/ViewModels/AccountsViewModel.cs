using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MailBridge.App.Security;
using MailBridge.Core.Models;
using MailBridge.Core.Security;
using MailBridge.Core.Security.OAuth;
using MailBridge.Core.Services;

namespace MailBridge.App.ViewModels;

public partial class AccountsViewModel : ObservableObject
{
    /// <summary>How long the browser sign-in may take before MailBridge stops waiting for it.</summary>
    private static readonly TimeSpan SignInTimeout = TimeSpan.FromMinutes(5);

    private readonly ICredentialStore _credentialStore;
    private readonly AccountCredentialProvider _credentials;
    private readonly OAuthSignInFlow _signInFlow;
    private readonly AccountStore _accountStore;
    private readonly ImapConnectionService _imapConnectionService;

    /// <summary>Result of the last browser sign-in, kept until the account is saved or the form is reset.</summary>
    private OAuthTokens? _pendingSignIn;

    public ObservableCollection<EmailAccount> Accounts { get; } = new();

    /// <summary>"Manual entry" first, then one option per provider template.</summary>
    public IReadOnlyList<MailTemplateOption> TemplateOptions { get; } =
        MailProviderTemplates.All.Select(t => new MailTemplateOption(t)).Prepend(new MailTemplateOption(null)).ToList();

    [ObservableProperty]
    private MailTemplateOption? selectedTemplate;

    public string TemplateHint => SelectedTemplate?.Template is { } template
        ? Localization.Strings.Get($"accounts.templateHint.{template.Setup}")
        : string.Empty;

    public bool HasTemplateHint => SelectedTemplate?.Template is not null;

    /// <summary>The sign-in method of the form, decided by the provider template (manual entry = password).</summary>
    public AccountAuthMethod SelectedAuthMethod => SelectedTemplate?.Template?.AuthMethod ?? AccountAuthMethod.Password;

    public bool IsOAuth => SelectedAuthMethod == AccountAuthMethod.MicrosoftOAuth2;

    public bool IsPasswordAuth => !IsOAuth;

    public bool ShowKeepPasswordHint => IsEditing && IsPasswordAuth;

    /// <summary>False when this build has no Microsoft client ID; the sign-in button is disabled then.</summary>
    public bool IsOAuthAvailable => _credentials.MicrosoftOptions.IsConfigured;

    public string SignInButtonText => Localization.Strings.Get(
        EditingAccount?.AuthMethod == AccountAuthMethod.MicrosoftOAuth2 ? "accounts.signInAgain" : "accounts.signIn");

    public string OAuthHint => !IsOAuthAvailable
        ? Localization.Strings.Get("accounts.oauthNotConfigured")
        : EditingAccount?.AuthMethod == AccountAuthMethod.MicrosoftOAuth2 && _pendingSignIn is null
            ? Localization.Strings.Get("accounts.keepSignInHint")
            : string.Empty;

    public bool HasOAuthHint => OAuthHint.Length > 0;

    private EmailAccount? EditingAccount => EditingAccountId is { } id ? Accounts.FirstOrDefault(a => a.Id == id) : null;

    private bool _applyingTemplate;

    partial void OnSelectedTemplateChanged(MailTemplateOption? value)
    {
        OnPropertyChanged(nameof(TemplateHint));
        OnPropertyChanged(nameof(HasTemplateHint));
        OnAuthMethodChanged();

        if (value is null)
        {
            // The ComboBox clears its selection when its page unloads.
            SyncTemplateWithForm();
            return;
        }

        if (value.Template?.AuthMethod != AccountAuthMethod.MicrosoftOAuth2)
        {
            // A Microsoft sign-in only belongs to a Microsoft account.
            _pendingSignIn = null;
        }

        if (value.Template is { } template && !_applyingTemplate)
        {
            _applyingTemplate = true;
            Host = template.Host;
            Port = template.Port;
            _applyingTemplate = false;
        }
    }

    private void OnAuthMethodChanged()
    {
        OnPropertyChanged(nameof(SelectedAuthMethod));
        OnPropertyChanged(nameof(IsOAuth));
        OnPropertyChanged(nameof(IsPasswordAuth));
        OnPropertyChanged(nameof(ShowKeepPasswordHint));
        OnSignInStateChanged();
    }

    private void OnSignInStateChanged()
    {
        OnPropertyChanged(nameof(SignInButtonText));
        OnPropertyChanged(nameof(OAuthHint));
        OnPropertyChanged(nameof(HasOAuthHint));
    }

    partial void OnHostChanged(string value) => SyncTemplateWithForm();

    partial void OnPortChanged(int value) => SyncTemplateWithForm();

    /// <summary>Keeps the template selection in line with manually edited connection details.</summary>
    private void SyncTemplateWithForm()
    {
        if (_applyingTemplate)
        {
            return;
        }

        var match = MailProviderTemplates.Find(Host, Port);
        _applyingTemplate = true;
        SelectedTemplate = TemplateOptions.First(o => o.Template == match);
        _applyingTemplate = false;
    }

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
        OnPropertyChanged(nameof(ShowKeepPasswordHint));
        OnSignInStateChanged();
    }

    public AccountsViewModel(
        ICredentialStore credentialStore,
        AccountCredentialProvider credentials,
        OAuthSignInFlow signInFlow,
        AccountStore accountStore,
        ImapConnectionService imapConnectionService)
    {
        _credentialStore = credentialStore;
        _credentials = credentials;
        _signInFlow = signInFlow;
        _accountStore = accountStore;
        _imapConnectionService = imapConnectionService;
        SelectedTemplate = TemplateOptions[0];

        foreach (var account in _accountStore.Load())
        {
            Accounts.Add(account);
        }
    }

    private static string CredentialKeyFor(string host, string username) => $"MailBridge:{host}:{username}";

    [RelayCommand]
    private void SaveAccount()
    {
        if (string.IsNullOrWhiteSpace(Host) || string.IsNullOrWhiteSpace(Username))
        {
            ConnectionStatus = Localization.Strings.Get(IsOAuth ? "accounts.signInFirst" : "accounts.enterHostUser");
            return;
        }

        if (EditingAccountId is { } editingId)
        {
            UpdateAccount(editingId);
            return;
        }

        if (IsOAuth && _pendingSignIn is null)
        {
            ConnectionStatus = Localization.Strings.Get("accounts.signInFirst");
            return;
        }

        var account = new EmailAccount
        {
            DisplayName = string.IsNullOrWhiteSpace(DisplayName) ? Username : DisplayName,
            Host = Host,
            Port = Port,
            Username = Username,
            AuthMethod = SelectedAuthMethod,
            CredentialKey = CredentialKeyFor(Host, Username),
        };

        if (!TryStoreNewSecret(account))
        {
            return;
        }

        Accounts.Add(account);
        _accountStore.Save(Accounts);
        ResetForm();
    }

    /// <summary>Stores the typed password or the pending sign-in for the account, if there is one.</summary>
    private bool TryStoreNewSecret(EmailAccount account)
    {
        try
        {
            if (account.AuthMethod == AccountAuthMethod.MicrosoftOAuth2 && _pendingSignIn is { } signIn)
            {
                _credentials.SaveSignIn(account, signIn);
            }
            else if (account.AuthMethod == AccountAuthMethod.Password && !string.IsNullOrEmpty(Password))
            {
                _credentialStore.Save(account.CredentialKey, account.Username, Password);
            }

            return true;
        }
        catch (Exception ex)
        {
            ConnectionStatus = Localization.Strings.Get("accounts.signInFailed", ex.Message);
            return false;
        }
    }

    public void BeginEdit(EmailAccount account)
    {
        _pendingSignIn = null;
        EditingAccountId = account.Id;
        DisplayName = account.DisplayName;
        Host = account.Host;
        Port = account.Port;
        Username = account.Username;
        Password = string.Empty;
        ConnectionStatus = string.Empty;
        OnSignInStateChanged();
    }

    [RelayCommand]
    private void CancelEdit() => ResetForm();

    [RelayCommand(CanExecute = nameof(IsOAuthAvailable), IncludeCancelCommand = true)]
    private async Task SignInAsync(CancellationToken cancellationToken)
    {
        ConnectionStatus = Localization.Strings.Get("accounts.signingIn");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(SignInTimeout);

        try
        {
            var page = new BrowserPageText(
                "MailBridge",
                Localization.Strings.Get("oauth.pageSuccess"),
                Localization.Strings.Get("oauth.pageFailure"));
            var loginHint = string.IsNullOrWhiteSpace(Username) ? null : Username.Trim();

            var tokens = await _signInFlow.SignInAsync(
                _credentials.MicrosoftOptions, OAuthConfiguration.OpenInBrowserAsync, page, loginHint, timeout.Token);

            if (string.IsNullOrEmpty(tokens.RefreshToken))
            {
                ConnectionStatus = Localization.Strings.Get("accounts.signInFailed", Localization.Strings.Get("accounts.noRefreshToken"));
                return;
            }

            _pendingSignIn = tokens;
            if (string.IsNullOrWhiteSpace(Username) && tokens.Username is { } signedInAs)
            {
                Username = signedInAs;
            }

            ConnectionStatus = Localization.Strings.Get(IsEditing ? "accounts.signedInEdit" : "accounts.signedIn", tokens.Username ?? Username);
        }
        catch (OperationCanceledException)
        {
            ConnectionStatus = Localization.Strings.Get(cancellationToken.IsCancellationRequested ? "accounts.signInCancelled" : "accounts.signInTimedOut");
        }
        catch (Exception ex)
        {
            ConnectionStatus = Localization.Strings.Get("accounts.signInFailed", ex.Message);
        }
        finally
        {
            OnSignInStateChanged();
        }
    }

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        if (string.IsNullOrWhiteSpace(Host) || string.IsNullOrWhiteSpace(Username))
        {
            ConnectionStatus = Localization.Strings.Get(IsOAuth ? "accounts.signInFirst" : "accounts.enterHostUser");
            return;
        }

        ImapCredential credential;
        try
        {
            var resolved = await ResolveTestCredentialAsync();
            if (resolved is null)
            {
                return;
            }

            credential = resolved;
        }
        catch (CredentialUnavailableException ex)
        {
            ConnectionStatus = CredentialErrors.Describe(ex);
            return;
        }
        catch (Exception ex)
        {
            ConnectionStatus = Localization.Strings.Get("accounts.connFailed", ex.Message);
            return;
        }

        var testAccount = new EmailAccount
        {
            Host = Host,
            Port = Port,
            Username = Username,
            UseSsl = true,
            AuthMethod = SelectedAuthMethod,
        };

        ConnectionStatus = Localization.Strings.Get("accounts.connecting", Host, Port);
        try
        {
            using var client = await _imapConnectionService.ConnectAsync(testAccount, credential);
            var folders = await _imapConnectionService.GetAllFoldersAsync(client);
            ConnectionStatus = Localization.Strings.Get("accounts.connOk", Username, folders.Count);
        }
        catch (Exception ex)
        {
            ConnectionStatus = Localization.Strings.Get("accounts.connFailed", ex.Message);
        }
    }

    /// <summary>
    /// The secret to test with: what was just typed or signed in, else the stored one of the
    /// account being edited. Null (with a status message) when there is nothing to test with.
    /// </summary>
    private async Task<ImapCredential?> ResolveTestCredentialAsync()
    {
        var existing = EditingAccount;

        if (IsOAuth)
        {
            if (_pendingSignIn is { } signIn)
            {
                return ImapCredential.FromAccessToken(signIn.AccessToken);
            }

            if (existing?.AuthMethod == AccountAuthMethod.MicrosoftOAuth2)
            {
                return await _credentials.GetAsync(existing);
            }

            ConnectionStatus = Localization.Strings.Get("accounts.signInFirst");
            return null;
        }

        if (!string.IsNullOrEmpty(Password))
        {
            return ImapCredential.FromPassword(Password);
        }

        if (existing?.AuthMethod == AccountAuthMethod.Password &&
            _credentialStore.TryGet(existing.CredentialKey, existing.Username) is { } stored)
        {
            return ImapCredential.FromPassword(stored);
        }

        ConnectionStatus = Localization.Strings.Get("accounts.noPasswordTyped");
        return null;
    }

    private void UpdateAccount(Guid id)
    {
        var account = Accounts.FirstOrDefault(a => a.Id == id);
        if (account is null)
        {
            ResetForm();
            return;
        }

        var authMethod = SelectedAuthMethod;
        var newSignIn = authMethod == AccountAuthMethod.MicrosoftOAuth2 ? _pendingSignIn : null;
        var newPassword = authMethod == AccountAuthMethod.Password && !string.IsNullOrEmpty(Password) ? Password : null;
        var methodChanged = account.AuthMethod != authMethod;

        // A password cannot stand in for a Microsoft sign-in or vice versa.
        if (methodChanged && newSignIn is null && newPassword is null)
        {
            ConnectionStatus = Localization.Strings.Get(authMethod == AccountAuthMethod.Password ? "accounts.noPasswordTyped" : "accounts.signInFirst");
            return;
        }

        var updated = new EmailAccount
        {
            Id = account.Id,
            DisplayName = string.IsNullOrWhiteSpace(DisplayName) ? Username : DisplayName,
            Host = Host,
            Port = Port,
            UseSsl = account.UseSsl,
            Username = Username,
            AuthMethod = authMethod,
            CredentialKey = CredentialKeyFor(Host, Username),
        };
        var keyChanged = !string.Equals(updated.CredentialKey, account.CredentialKey, StringComparison.Ordinal);

        // Drop the cached access token of the old identity before a new sign-in caches its own.
        _credentials.Forget(account);

        // Store the new (or moved) secret first and remove the old entry only afterwards,
        // so a failure never leaves the account without any stored secret.
        var movedSecret = keyChanged && newSignIn is null && newPassword is null
            ? _credentialStore.TryGet(account.CredentialKey, account.Username)
            : null;
        if (movedSecret is not null)
        {
            _credentialStore.Save(updated.CredentialKey, updated.Username, movedSecret);
        }
        else if (!TryStoreNewSecret(updated))
        {
            return;
        }

        if (keyChanged && (newSignIn is not null || newPassword is not null || movedSecret is not null))
        {
            _credentialStore.Remove(account.CredentialKey, account.Username);
        }

        // Update the existing instance: other pages (Backup, Schedule) hold references to it.
        account.DisplayName = updated.DisplayName;
        account.Host = updated.Host;
        account.Port = updated.Port;
        account.Username = updated.Username;
        account.AuthMethod = updated.AuthMethod;
        account.CredentialKey = updated.CredentialKey;

        var index = Accounts.IndexOf(account);
        Accounts[index] = account;
        _accountStore.Save(Accounts);
        ResetForm();
    }

    private void ResetForm()
    {
        _pendingSignIn = null;
        EditingAccountId = null;
        DisplayName = string.Empty;
        Host = string.Empty;
        Port = 993;
        Username = string.Empty;
        Password = string.Empty;
        OnSignInStateChanged();
    }

    [RelayCommand]
    private void RemoveAccount(EmailAccount account)
    {
        _credentialStore.Remove(account.CredentialKey, account.Username);
        _credentials.Forget(account);
        Accounts.Remove(account);
        _accountStore.Save(Accounts);
    }
}
