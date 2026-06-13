using System.Reflection;
using System.Text.Json;

namespace PoeHotFilter.Core.Parsing;

/// <summary>
/// The set of craftable base-type names (loaded once from the embedded <c>items_min.json</c>),
/// used to recover the clean base of a Magic item whose name has prefix/suffix affixes baked in.
///
/// Mirrors Awakened PoE Trade's <c>magicBasetype</c>: generate every contiguous word-substring
/// of the affixed name, keep the ones that are known craftable bases, and pick the longest.
/// </summary>
public static class BaseTypeCatalog
{
    // Categories in items_min.json that can NEVER roll as Magic (no prefix/suffix), so they must
    // not pollute the lookup dictionary — otherwise an affix word could spuriously match e.g. a
    // currency or divination-card name. Everything else (equipment, jewels, flasks, maps, …) stays.
    private static readonly HashSet<string> NonMagicableCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "Currency", "Fragments", "Essences", "Fossils", "Oils", "Catalysts",
        "Delirium Orbs", "Breach/Splinters", "Vials", "Divination Cards", "Gems",
    };

    private static readonly HashSet<string> Bases = LoadBases();

    /// <summary>Number of known craftable bases — exposed for diagnostics/tests.</summary>
    public static int Count => Bases.Count;

    /// <summary>True if <paramref name="name"/> is exactly a known craftable base type.</summary>
    public static bool IsKnownBase(string name) => Bases.Contains(name);

    /// <summary>
    /// Recovers the clean base type from a Magic item's affixed name
    /// (e.g. "Sturdy Sapphire Ring of the Whelpling" -> "Sapphire Ring").
    /// Returns null if no known base is found, so callers can fall back to the raw line.
    /// </summary>
    public static string? ResolveBaseType(string? affixedName)
    {
        if (string.IsNullOrWhiteSpace(affixedName))
            return null;

        var words = affixedName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string? best = null;

        // All contiguous word-substrings; keep the longest one that is a known base.
        for (int start = 0; start < words.Length; start++)
        {
            for (int len = 1; start + len <= words.Length; len++)
            {
                var candidate = string.Join(' ', words, start, len);
                if (Bases.Contains(candidate) && candidate.Length > (best?.Length ?? 0))
                    best = candidate;
            }
        }

        return best;
    }

    private static HashSet<string> LoadBases()
    {
        var bases = new HashSet<string>(StringComparer.Ordinal);

        var asm = Assembly.GetExecutingAssembly();
        var resourceName = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("items_min.json", StringComparison.OrdinalIgnoreCase));
        if (resourceName is null)
            return bases; // No dictionary embedded — magic resolution degrades to raw-line fallback.

        using var stream = asm.GetManifestResourceStream(resourceName);
        if (stream is null)
            return bases;

        using var doc = JsonDocument.Parse(stream);
        foreach (var category in doc.RootElement.EnumerateObject())
        {
            if (NonMagicableCategories.Contains(category.Name))
                continue;
            if (category.Value.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var entry in category.Value.EnumerateArray())
            {
                if (entry.TryGetProperty("n", out var nameProp) &&
                    nameProp.GetString() is { Length: > 0 } name)
                {
                    bases.Add(name);
                }
            }
        }

        return bases;
    }
}
