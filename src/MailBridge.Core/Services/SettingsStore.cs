using MailBridge.Core.Models;

namespace MailBridge.Core.Services;

/// <summary>
/// Persists the single <see cref="AppSettings"/> document shown on the
/// Settings page. Contains preferences only - never secrets.
/// </summary>
public sealed class SettingsStore
{
    private readonly string _path;

    public SettingsStore(string path)
    {
        _path = path;
    }

    public AppSettings Load() => JsonFileStore.LoadItem<AppSettings>(_path);

    public void Save(AppSettings settings) => JsonFileStore.SaveItem(_path, settings);
}
