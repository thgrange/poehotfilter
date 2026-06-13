using System.Text.Json;

namespace PoeHotFilter.Core.Storage;

/// <summary>A serialisable hotkey: a main key name plus modifier flags.</summary>
public sealed class HotkeyConfig
{
    /// <summary>SharpHook KeyCode name without the "Vc" prefix, e.g. "A", "F5", "D". </summary>
    public string Key { get; set; } = "B";
    public bool Ctrl { get; set; } = true;
    public bool Alt { get; set; }
    public bool Shift { get; set; }

    public string ToDisplayString()
    {
        var parts = new List<string>();
        if (Ctrl) parts.Add("Ctrl");
        if (Alt) parts.Add("Alt");
        if (Shift) parts.Add("Shift");
        parts.Add(Key);
        return string.Join(" + ", parts);
    }
}

public sealed class AppSettings
{
    /// <summary>Capture-the-hovered-item hotkey (default Ctrl + B).</summary>
    public HotkeyConfig Hotkey { get; set; } = new();

    /// <summary>Open the "custom filters" management list (default Ctrl + S).</summary>
    public HotkeyConfig ListHotkey { get; set; } = new() { Key = "S", Ctrl = true };

    /// <summary>Absolute path of the active filter we inject our Import into. Null = auto-pick.</summary>
    public string? ActiveFilterPath { get; set; }

    /// <summary>Override for the PoE folder if auto-detection fails.</summary>
    public string? PoeFolderOverride { get; set; }

    /// <summary>Whether to send /reloaditemfilter automatically after each change.</summary>
    public bool AutoReload { get; set; } = true;

    /// <summary>
    /// When true (default), the app follows whatever filter you select in-game: it re-injects
    /// its Import and refreshes presets on every switch/re-export. When false, it stays locked
    /// to <see cref="ActiveFilterPath"/>.
    /// </summary>
    public bool FollowInGameFilter { get; set; } = true;
}

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path;

    public SettingsStore(string path) => _path = path;

    public static SettingsStore Default()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PoeHotFilter");
        Directory.CreateDirectory(dir);
        return new SettingsStore(Path.Combine(dir, "settings.json"));
    }

    public AppSettings Load()
    {
        if (!File.Exists(_path))
            return new AppSettings();
        try
        {
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path), JsonOptions)
                   ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(tmp, _path, overwrite: true);
    }
}
