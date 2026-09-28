using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MailBridge.App.Localization;
using MailBridge.Core.Services;

namespace MailBridge.App.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsStore _settingsStore;

    public IReadOnlyList<string> ThemeOptions { get; } = new[] { "System", "Light", "Dark" };

    public IReadOnlyList<string> LanguageOptions { get; } = new[] { "English", "Deutsch" };

    [ObservableProperty]
    private string selectedLanguage = "English";

    public string SelectedLanguageCode => Localization.Strings.DisplayNameToCode(SelectedLanguage);

    /// <summary>Raised after settings were persisted, so the shell can
    /// apply language (and theme) changes without an app restart.</summary>
    public event EventHandler? Saved;

    [ObservableProperty]
    private string selectedTheme = "System";

    [ObservableProperty]
    private string defaultBackupDirectory = string.Empty;

    [ObservableProperty]
    private bool defaultCompressToZip = true;

    [ObservableProperty]
    private string statusText = string.Empty;

    public SettingsViewModel(SettingsStore settingsStore)
    {
        _settingsStore = settingsStore;

        var settings = _settingsStore.Load();
        SelectedLanguage = Localization.Strings.LanguageDisplayName(settings.Language);
        SelectedTheme = settings.Theme;
        DefaultBackupDirectory = settings.DefaultBackupDirectory;
        DefaultCompressToZip = settings.DefaultCompressToZip;
    }

    [RelayCommand]
    private void Save()
    {
        Strings.SetLanguage(SelectedLanguageCode);

        _settingsStore.Save(new Core.Models.AppSettings
        {
            Language = SelectedLanguageCode,
            Theme = SelectedTheme,
            DefaultBackupDirectory = DefaultBackupDirectory,
            DefaultCompressToZip = DefaultCompressToZip,
        });

        StatusText = Localization.Strings.Get("settings.saved");
        Saved?.Invoke(this, EventArgs.Empty);
    }
}
