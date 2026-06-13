using System.Text;
using PoeHotFilter.Core.Models;

namespace PoeHotFilter.Core.Filter;

/// <summary>
/// Owns the two filesystem concerns:
///   1. Writing the managed .filter file (the one we regenerate).
///   2. Injecting a single <c>Import "..."</c> line at the TOP of the user's active filter,
///      so our rules take priority over everything (NeverSink etc.).
///
/// Why Import-at-top: PoE evaluates blocks top-to-bottom and the first match wins.
/// Import pulls the referenced file's blocks in at that position, and the import is
/// resolved at /reloaditemfilter time — so regenerating the managed file + reloading
/// is enough; we never have to touch the active filter again after the one-time inject.
/// </summary>
public sealed class FilterFileManager
{
    private const string ImportMarker = "# PoeHotFilter import (managed)";

    private readonly string _poeFolder;
    private readonly string _managedFileName;

    /// <param name="poeFolder">e.g. %USERPROFILE%\Documents\My Games\Path of Exile</param>
    /// <param name="managedFileName">e.g. "_PoeHotFilter.filter"</param>
    public FilterFileManager(string poeFolder, string managedFileName = "_PoeHotFilter.filter")
    {
        _poeFolder = poeFolder;
        _managedFileName = managedFileName;
    }

    public string ManagedFilePath => Path.Combine(_poeFolder, _managedFileName);

    /// <summary>True if the given filter already contains our managed Import marker.</summary>
    public bool IsImportPresent(string activeFilterPath)
    {
        try
        {
            return File.Exists(activeFilterPath) &&
                   File.ReadAllText(activeFilterPath).Contains(ImportMarker, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Regenerates the managed .filter file from the current rule set.</summary>
    public async Task WriteManagedFileAsync(IEnumerable<FilterRule> rules, CancellationToken ct = default)
    {
        var content = FilterBlockBuilder.BuildFile(rules);
        // PoE wants ANSI or UTF-8. UTF-8 without BOM is safest.
        await File.WriteAllTextAsync(ManagedFilePath, content, new UTF8Encoding(false), ct);
    }

    /// <summary>
    /// Ensures the active filter file imports our managed file as its first directive
    /// (after any existing leading comments). Idempotent: running twice does nothing the 2nd time.
    /// Returns true if the file was modified.
    /// </summary>
    public async Task<bool> EnsureImportInjectedAsync(string activeFilterPath, CancellationToken ct = default)
    {
        if (!File.Exists(activeFilterPath))
            throw new FileNotFoundException("Active filter not found.", activeFilterPath);

        var original = await File.ReadAllTextAsync(activeFilterPath, ct);
        if (original.Contains(ImportMarker, StringComparison.Ordinal))
            return false; // already injected

        // Import paths are relative to the PoE filter folder, so just the file name works.
        var importLine =
            $"{ImportMarker}\nImport \"{_managedFileName}\"\n\n";

        var updated = importLine + original;

        // Backup once before the first mutation.
        var backupPath = activeFilterPath + ".phf-backup";
        if (!File.Exists(backupPath))
            await File.WriteAllTextAsync(backupPath, original, ct);

        await File.WriteAllTextAsync(activeFilterPath, updated, new UTF8Encoding(false), ct);
        return true;
    }

    /// <summary>Removes the injected Import line (clean uninstall). Returns true if modified.</summary>
    public async Task<bool> RemoveImportAsync(string activeFilterPath, CancellationToken ct = default)
    {
        if (!File.Exists(activeFilterPath))
            return false;

        var lines = (await File.ReadAllLinesAsync(activeFilterPath, ct)).ToList();
        int markerIdx = lines.FindIndex(l => l.Contains(ImportMarker, StringComparison.Ordinal));
        if (markerIdx < 0)
            return false;

        // Remove the marker line, the following Import line, and an optional blank line.
        int removeCount = 1;
        if (markerIdx + 1 < lines.Count && lines[markerIdx + 1].TrimStart().StartsWith("Import", StringComparison.OrdinalIgnoreCase))
            removeCount++;
        if (markerIdx + removeCount < lines.Count && string.IsNullOrWhiteSpace(lines[markerIdx + removeCount]))
            removeCount++;

        lines.RemoveRange(markerIdx, removeCount);
        await File.WriteAllLinesAsync(activeFilterPath, lines, new UTF8Encoding(false), ct);
        return true;
    }
}
