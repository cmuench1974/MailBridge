using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MailBridge.App.Security;
using MailBridge.Core.Models;
using MailBridge.Core.Security;
using MailBridge.Core.Security.OAuth;
using MailBridge.Core.Services;
using Strings = MailBridge.App.Localization.Strings;

namespace MailBridge.App.ViewModels;

public partial class AccountsViewModel : ObservableObject
{
    /// <summary>How long the browser sign-in may take before MailBridge stops waiting for it.</summary>
    private static readonly TimeSpan SignInTimeout = TimeSpan.FromMinutes(5);

    private static readonly AccountAuthMethod[] PasswordOnly = { AccountAuthMethod.Password };

    private readonly ICredentialStore _credentialStore;
    private readonly AccountCredentialProvider _credentials;
    private readonly OAuthSignInFlow _signInFlow;
    private readonly AccountStore _accountStore;
    private readonly ImapConnectionService _imapConnectionService;

    /// <summary>Result of the last browser sign-in, kept until the account is saved or the form is reset.</summary>
    private OAuthTokens? _pendingSignIn;

    /// <summary>The template <see cref="SelectedAuthMethod"/> was chosen for; a different template resets it.</summary>
    private MailTemplateOption? _authMethodChosenFor;

    public ObservableCollection<EmailAccount> Accounts { get; } = new();

    /// <summary>"Manual entry" first, then one option per provider template.</summary>
    public IReadOnlyList<MailTemplateOption> TemplateOptions { get; } =
        MailProviderTemplates.All.Select(t => new MailTemplateOption(t)).Prepend(new MailTemplateOption(null)).ToList();

    [ObservableProperty]
    private MailTemplateOption? selectedTemplate;

    [ObservableProperty]
    private AccountAuthMethod selectedAuthMethod = AccountAuthMethod.Password;

    private IReadOnlyList<AccountAuthMethod> TemplateAuthMethods => SelectedTemplate?.Template?.AuthMethods ?? PasswordOnly;

    /// <summary>Sign-in methods the selected provider accepts (manual entry: password only).</summary>
    public IReadOnlyList<AuthMethodOption> AuthMethodOptions => TemplateAuthMethods.Select(AuthMethodOption.For).ToList();

    public bool HasAuthMethodChoice => TemplateAuthMethods.Count > 1;

    public AuthMethodOption? SelectedAuthMethodOption
    {
        get => AuthMethodOption.For(SelectedAuthMethod);
        set
        {
            // While its item list is being replaced the ComboBox reports null or writes back an
            // entry of the previous template; only accept methods the current template offers.
            if (value is not null && TemplateAuthMethods.Contains(value.Method))
            {
                SelectedAuthMethod = value.Method;
            }
        }
    }

    public string TemplateHint => SelectedTemplate?.Template is { } template
        ? IsOAuth
            ? Strings.Get($"accounts.authHint.{SelectedAuthMethod}")
            : Strings.Get($"accounts.templateHint.{template.Setup}")
        : string.Empty;

    public bool HasTemplateHint => SelectedTemplate?.Template is not null;

    public bool IsOAuth => SelectedAuthMethod != AccountAuthMethod.Password;

    public bool IsPasswordAuth => !IsOAuth;

    public bool ShowKeepPasswordHint => IsEditing && IsPasswordAuth;

    /// <summary>False for passwords and for OAuth2 providers this build has no client ID for.</summary>
    public bool IsOAuthAvailable => IsOAuth && _credentials.IsAvailable(SelectedAuthMethod);

    private string ProviderName => CredentialErrors.ProviderName(SelectedAuthMethod);

    /// <summary>True when the account being edited already has a stored sign-in of the selected kind.</summary>
    private bool EditingHasStoredSignIn => IsOAuth && EditingAccount?.AuthMethod == SelectedAuthMethod;

    public string SignInButtonText => EditingHasStoredSignIn
        ? Strings.Get("accounts.signInAgain")
        : Strings.Get("accounts.signIn", ProviderName);

    public string OAuthHint => !IsOAuth
        ? string.Empty
        : !IsOAuthAvailable
            ? CredentialErrors.NotConfigured(SelectedAuthMethod)
            : EditingHasStoredSignIn && _pendingSignIn is null
                ? Strings.Get("accounts.keepSignInHint", ProviderName)
                : string.Empty;

    public bool HasOAuthHint => OAuthHint.Length > 0;

    private EmailAccount? EditingAccount => EditingAccountId is { } id ? Accounts.FirstOrDefault(a => a.Id == id) : null;

    private bool _applyingTemplate;

    partial void OnSelectedTemplateChanged(MailTemplateOption? value)
    {
        if (value is null)
        {
            // The ComboBox clears its selection when its page unloads.
            SyncTemplateWithForm();
            return;
        }

        // Replace the method list before the selection, so the ComboBox never has to show a
        // method that is not in its current list.
        OnPropertyChanged(nameof(AuthMethodOptions));
        OnPropertyChanged(nameof(HasAuthMethodChoice));

        if (!ReferenceEquals(value, _authMethodChosenFor))
        {
            _authMethodChosenFor = value;
            SelectedAuthMethod = DefaultAuthMethod(TemplateAuthMethods);
        }

        OnPropertyChanged(nameof(SelectedAuthMethodOption));
        OnPropertyChanged(nameof(TemplateHint));
        OnPropertyChanged(nameof(HasTemplateHint));

        if (value.Template is { } template && !_applyingTemplate)
        {
            _applyingTemplate = true;
            Host = template.Host;
            Port = template.Port;
            _applyingTemplate = false;
        }
    }

    /// <summary>The provider's preferred method that works with this build, else its first one.</summary>
    private AccountAuthMethod DefaultAuthMethod(IReadOnlyList<AccountAuthMethod> methods)
    {
        foreach (var method in methods)
        {
            if (_credentials.IsAvailable(method))
            {
                return method;
            }
        }

        return methods[0];
    }

    partial void OnSelectedAuthMethodChanged(AccountAuthMethod value)
    {
        // A sign-in belongs to the provider it was made with.
        _pendingSignIn = null;

        OnPropertyChanged(nameof(SelectedAuthMethodOption));
        OnPropertyChanged(nameof(IsOAuth));
        OnPropertyChanged(nameof(IsPasswordAuth));
        OnPropertyChanged(nameof(ShowKeepPasswordHint));
        OnPropertyChanged(nameof(IsOAuthAvailable));
        OnPropertyChanged(nameof(TemplateHint));
        OnSignInStateChanged();
        SignInCommand.NotifyCanExecuteChanged();
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
        ? Strings.Get("accounts.saveChanges")
        : Strings.Get("accounts.add");

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
            ConnectionStatus = IsOAuth ? Strings.Get("accounts.signInFirst", ProviderName) : Strings.Get("accounts.enterHostUser");
            return;
        }

        if (EditingAccountId is { } editingId)
        {
            UpdateAccount(editingId);
            return;
        }

        if (IsOAuth && _pendingSignIn is null)
        {
            ConnectionStatus = Strings.Get("accounts.signInFirst", ProviderName);
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
            if (account.AuthMethod != AccountAuthMethod.Password && _pendingSignIn is { } signIn)
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
            ConnectionStatus = Strings.Get("accounts.saveFailed", ex.Message);
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

        // Host/port picked the template and its default method; restore the account's own.
        if (TemplateAuthMethods.Contains(account.AuthMethod))
        {
            SelectedAuthMethod = account.AuthMethod;
        }

        OnSignInStateChanged();
    }

    [RelayCommand]
    private void CancelEdit() => ResetForm();

    [RelayCommand(CanExecute = nameof(IsOAuthAvailable), IncludeCancelCommand = true)]
    private async Task SignInAsync(CancellationToken cancellationToken)
    {
        var method = SelectedAuthMethod;
        var providerName = ProviderName;
        if (_credentials.GetOAuthOptions(method) is not { IsConfigured: true } options)
        {
            ConnectionStatus = CredentialErrors.NotConfigured(method);
            return;
        }

        ConnectionStatus = Strings.Get("accounts.signingIn");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(SignInTimeout);

        try
        {
            var page = new BrowserPageText("MailBridge", Strings.Get("oauth.pageSuccess"), Strings.Get("oauth.pageFailure"));
            var loginHint = string.IsNullOrWhiteSpace(Username) ? null : Username.Trim();

            var tokens = await _signInFlow.SignInAsync(options, OAuthConfiguration.OpenInBrowserAsync, page, loginHint, timeout.Token);

            if (method != SelectedAuthMethod)
            {
                // The user switched the sign-in method while the browser was open.
                return;
            }

            if (string.IsNullOrEmpty(tokens.RefreshToken))
            {
                ConnectionStatus = Strings.Get("accounts.signInFailed", providerName, Strings.Get("accounts.noRefreshToken", providerName));
                return;
            }

            _pendingSignIn = tokens;
            if (string.IsNullOrWhiteSpace(Username) && tokens.Username is { } signedInAs)
            {
                Username = signedInAs;
            }

            ConnectionStatus = Strings.Get(IsEditing ? "accounts.signedInEdit" : "accounts.signedIn", tokens.Username ?? Username);
        }
        catch (OperationCanceledException)
        {
            ConnectionStatus = Strings.Get(cancellationToken.IsCancellationRequested ? "accounts.signInCancelled" : "accounts.signInTimedOut");
        }
        catch (Exception ex)
        {
            ConnectionStatus = Strings.Get("accounts.signInFailed", providerName, ex.Message);
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
            ConnectionStatus = IsOAuth ? Strings.Get("accounts.signInFirst", ProviderName) : Strings.Get("accounts.enterHostUser");
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
            ConnectionStatus = Strings.Get("accounts.connFailed", ex.Message);
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

        ConnectionStatus = Strings.Get("accounts.connecting", Host, Port);
        try
        {
            using var client = await _imapConnectionService.ConnectAsync(testAccount, credential);
            var folders = await _imapConnectionService.GetAllFoldersAsync(client);
            ConnectionStatus = Strings.Get("accounts.connOk", Username, folders.Count);
        }
        catch (Exception ex)
        {
            ConnectionStatus = Strings.Get("accounts.connFailed", ex.Message);
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

            if (existing is not null && existing.AuthMethod == SelectedAuthMethod)
            {
                return await _credentials.GetAsync(existing);
            }

            ConnectionStatus = Strings.Get("accounts.signInFirst", ProviderName);
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

        ConnectionStatus = Strings.Get("accounts.noPasswordTyped");
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
        var newSignIn = authMethod != AccountAuthMethod.Password ? _pendingSignIn : null;
        var newPassword = authMethod == AccountAuthMethod.Password && !string.IsNullOrEmpty(Password) ? Password : null;
        var methodChanged = account.AuthMethod != authMethod;

        // A password cannot stand in for a sign-in, nor one provider's sign-in for another's.
        if (methodChanged && newSignIn is null && newPassword is null)
        {
            ConnectionStatus = authMethod == AccountAuthMethod.Password
                ? Strings.Get("accounts.noPasswordTyped")
                : Strings.Get("accounts.signInFirst", ProviderName);
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
