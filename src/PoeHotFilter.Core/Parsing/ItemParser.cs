using System.Globalization;
using PoeHotFilter.Core.Models;

namespace PoeHotFilter.Core.Parsing;

/// <summary>
/// Parses the text PoE puts on the clipboard when you hover an item and press Ctrl+C.
///
/// The format is a series of sections separated by a line of dashes ("--------").
/// The first section is always the header:
///
///     Item Class: Body Armours
///     Rarity: Rare
///     Dread Veil          (rare/unique display name — only present for Rare/Unique)
///     Vaal Regalia        (the base type — always the last line of the header)
///
/// "Item Level: N" appears in its own section later, when the game reports it.
/// </summary>
public static class ItemParser
{
    private const string ItemClassPrefix = "Item Class:";
    private const string RarityPrefix = "Rarity:";
    private const string ItemLevelPrefix = "Item Level:";
    private const string StackSizePrefix = "Stack Size:";
    private const string QualityPrefix = "Quality:";
    private const string LevelPrefix = "Level:";

    /// <summary>
    /// Returns null if the clipboard text doesn't look like a PoE item
    /// (e.g. it's a chat message, a trade link, or empty).
    /// </summary>
    public static ParsedItem? TryParse(string? clipboard)
    {
        if (string.IsNullOrWhiteSpace(clipboard))
            return null;

        // Normalise line endings; PoE uses \r\n.
        var text = clipboard.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = text.Split('\n');

        // A real item always starts with "Item Class:". This is our cheap sanity gate.
        var classLine = lines.FirstOrDefault(l => l.StartsWith(ItemClassPrefix, StringComparison.OrdinalIgnoreCase));
        if (classLine is null)
            return null;

        string itemClass = classLine[ItemClassPrefix.Length..].Trim();

        string? rarity = lines
            .FirstOrDefault(l => l.StartsWith(RarityPrefix, StringComparison.OrdinalIgnoreCase))
            ?[RarityPrefix.Length..]
            .Trim();

        int? itemLevel = ParseItemLevel(lines);

        bool isStackable = lines.Any(l => l.StartsWith(StackSizePrefix, StringComparison.OrdinalIgnoreCase));
        bool isGem = string.Equals(rarity, "Gem", StringComparison.OrdinalIgnoreCase);

        int? quality = ParseQuality(lines);
        // "Level: N" appears both as a gem's level AND as a gear's level requirement, so only
        // trust it as a gem level when the item really is a gem.
        int? gemLevel = isGem ? ParseLevel(lines) : null;

        var (name, baseType) = ParseNameAndBaseType(text, rarity);
        if (string.IsNullOrWhiteSpace(baseType))
            return null;

        return new ParsedItem
        {
            ItemClass = itemClass,
            BaseType = baseType,
            Name = name,
            Rarity = rarity,
            ItemLevel = itemLevel,
            IsStackable = isStackable,
            IsGem = isGem,
            Quality = quality,
            GemLevel = gemLevel,
            RawText = clipboard
        };
    }

    /// <summary>Parses "Quality: +20% (augmented)" -> 20. Null if absent/unparseable.</summary>
    private static int? ParseQuality(IEnumerable<string> lines)
    {
        var line = lines.FirstOrDefault(l => l.StartsWith(QualityPrefix, StringComparison.OrdinalIgnoreCase));
        if (line is null)
            return null;

        // Strip the prefix, the leading '+', the '%' and any trailing "(augmented)".
        var digits = new string(line[QualityPrefix.Length..].Where(char.IsDigit).ToArray());
        return int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var q) ? q : null;
    }

    /// <summary>Parses a gem's "Level: 20 (Max)" -> 20. Null if absent/unparseable.</summary>
    private static int? ParseLevel(IEnumerable<string> lines)
    {
        var line = lines.FirstOrDefault(l => l.StartsWith(LevelPrefix, StringComparison.OrdinalIgnoreCase));
        if (line is null)
            return null;

        var token = line[LevelPrefix.Length..].Trim().Split(' ', '(')[0];
        return int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var lvl) ? lvl : null;
    }

    private static int? ParseItemLevel(IEnumerable<string> lines)
    {
        var line = lines.FirstOrDefault(l => l.StartsWith(ItemLevelPrefix, StringComparison.OrdinalIgnoreCase));
        if (line is null)
            return null;

        var value = line[ItemLevelPrefix.Length..].Trim();
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ilvl)
            ? ilvl
            : null;
    }

    /// <summary>
    /// The header is the first dash-delimited section. Its last non-prefixed line is the base type.
    /// For Rare/Unique there are two name lines: [display name] then [base type].
    /// For Normal/Magic there's typically a single line which is the base type
    /// (Magic items prepend/append affixes to the name, but the base type still needs
    /// special handling — see note below).
    /// </summary>
    private static (string? name, string baseType) ParseNameAndBaseType(string text, string? rarity)
    {
        var firstSection = text.Split("--------")[0];
        var headerLines = firstSection
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .Where(l => !l.StartsWith(ItemClassPrefix, StringComparison.OrdinalIgnoreCase))
            .Where(l => !l.StartsWith(RarityPrefix, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (headerLines.Count == 0)
            return (null, string.Empty);

        bool isNamed = rarity is "Rare" or "Unique";

        if (isNamed && headerLines.Count >= 2)
        {
            // First line = display name, last line = base type.
            return (headerLines[0], headerLines[^1]);
        }

        var rawBase = headerLines[^1];

        // Magic (and, defensively, Normal) items bake prefix/suffix affixes into this single line
        // (e.g. "Sturdy Vaal Regalia of the Whelpling"). Recover the clean craftable base via the
        // base-type dictionary; fall back to the raw line if it isn't a known base. Currency, gems
        // and cards aren't affixed, so their single line is already the base — leave them untouched.
        if (rarity is "Magic" or "Normal")
        {
            var resolved = BaseTypeCatalog.ResolveBaseType(rawBase);
            if (resolved is not null)
                return (null, resolved);
        }

        return (null, rawBase);
    }
}
