using System.Text.Json;

namespace MailBridge.Core.Services;

/// <summary>
/// Tiny generic helper to load/save a list of records as indented JSON.
/// Used for account metadata and backup schedules - both are small,
/// human-inspectable files with no concurrent-writer concerns (single
/// desktop app, single user).
/// </summary>
public static class JsonFileStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static List<T> Load<T>(string path)
    {
        if (!File.Exists(path))
        {
            return new List<T>();
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<List<T>>(json, Options) ?? new List<T>();
    }

    public static void Save<T>(string path, List<T> items)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(items, Options);
        File.WriteAllText(path, json);
    }
}
