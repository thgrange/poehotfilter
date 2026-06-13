namespace PoeHotFilter.Core.Storage;

/// <summary>Helpers to locate PoE's filter folder and enumerate existing .filter files.</summary>
public static class PoePaths
{
    /// <summary>
    /// The standard location: %USERPROFILE%\Documents\My Games\Path of Exile.
    /// Returns null if it doesn't exist (e.g. PoE not installed, or custom Documents path).
    /// </summary>
    public static string? DefaultFilterFolder()
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var path = Path.Combine(documents, "My Games", "Path of Exile");
        return Directory.Exists(path) ? path : null;
    }

    /// <summary>Lists the .filter files (excluding our own managed file) the user could be running.</summary>
    public static IReadOnlyList<string> ListUserFilters(string folder, string managedFileName)
    {
        if (!Directory.Exists(folder))
            return Array.Empty<string>();

        return Directory.GetFiles(folder, "*.filter")
            .Where(f => !Path.GetFileName(f).Equals(managedFileName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(Path.GetFileName)
            .ToList();
    }

    /// <summary>The in-game filter name (no extension) — what /itemfilter expects.</summary>
    public static string FilterNameFromPath(string filterPath) =>
        Path.GetFileNameWithoutExtension(filterPath);
}
