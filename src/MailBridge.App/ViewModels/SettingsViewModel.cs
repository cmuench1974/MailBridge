using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MailBridge.Core.Services;

namespace MailBridge.App.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsStore _settingsStore;

    public IReadOnlyList<string> ThemeOptions { get; } = new[] { "System", "Light", "Dark" };

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
        SelectedTheme = settings.Theme;
        DefaultBackupDirectory = settings.DefaultBackupDirectory;
        DefaultCompressToZip = settings.DefaultCompressToZip;
    }

    [RelayCommand]
    private void Save()
    {
        _settingsStore.Save(new Core.Models.AppSettings
        {
            Theme = SelectedTheme,
            DefaultBackupDirectory = DefaultBackupDirectory,
            DefaultCompressToZip = DefaultCompressToZip,
        });

        StatusText = "Settings saved.";
    }
}
