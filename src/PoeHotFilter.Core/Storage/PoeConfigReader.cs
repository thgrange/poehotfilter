using System.Text.RegularExpressions;

namespace PoeHotFilter.Core.Storage;

/// <summary>
/// Reads which item filter PoE actually has selected, from production_Config.ini.
///
/// PoE writes the chosen filter under the [UI] section:
///   item_filter=Some Filter.filter
///   item_filter_loaded_successfully=Some Filter.filter
///
/// The second key only appears/updates once the filter parsed without errors, so we
/// prefer it when present (it's the filter the game is genuinely rendering with).
/// The value is a file name relative to the PoE folder.
/// </summary>
public static class PoeConfigReader
{
    private static readonly Regex SelectedRe =
        new(@"^\s*item_filter\s*=\s*(.+?)\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex LoadedRe =
        new(@"^\s*item_filter_loaded_successfully\s*=\s*(.+?)\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static string ConfigPath(string poeFolder) =>
        Path.Combine(poeFolder, "production_Config.ini");

    /// <summary>The active filter's full path, or null if none/unreadable.</summary>
    public static string? GetActiveFilterPath(string poeFolder)
    {
        var name = GetActiveFilterName(poeFolder);
        if (string.IsNullOrWhiteSpace(name))
            return null;

        // PoE stores the value with its extension already; guard if missing.
        if (!name.EndsWith(".filter", StringComparison.OrdinalIgnoreCase))
            name += ".filter";

        var full = Path.Combine(poeFolder, name);
        return File.Exists(full) ? full : null;
    }

    /// <summary>The active filter's file name as written in the config (prefers the "loaded" key).</summary>
    public static string? GetActiveFilterName(string poeFolder)
    {
        var configPath = ConfigPath(poeFolder);
        if (!File.Exists(configPath))
            return null;

        string? selected = null, loaded = null;
        try
        {
            // The file can be large; we only need the [UI] section's two keys.
            foreach (var line in File.ReadLines(configPath))
            {
                var lm = LoadedRe.Match(line);
                if (lm.Success) { loaded = lm.Groups[1].Value; continue; }
                var sm = SelectedRe.Match(line);
                if (sm.Success) selected = sm.Groups[1].Value;
            }
        }
        catch
        {
            return null;
        }

        // Prefer the successfully-loaded one; fall back to the selected one.
        var chosen = !string.IsNullOrWhiteSpace(loaded) ? loaded : selected;
        return string.IsNullOrWhiteSpace(chosen) ? null : chosen;
    }
}
