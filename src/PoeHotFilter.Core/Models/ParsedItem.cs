namespace PoeHotFilter.Core.Models;

/// <summary>
/// The relevant pieces of an item copied from PoE via Ctrl+C.
/// We only keep what we need to build a filter block.
/// </summary>
public sealed record ParsedItem
{
    /// <summary>e.g. "Body Armours" — maps to the filter's <c>Class</c> condition.</summary>
    public required string ItemClass { get; init; }

    /// <summary>e.g. "Vaal Regalia" — maps to the filter's <c>BaseType</c> condition.</summary>
    public required string BaseType { get; init; }

    /// <summary>e.g. "Rare", "Unique", "Magic", "Normal". May be null for currency/cards.</summary>
    public string? Rarity { get; init; }

    /// <summary>The display name for rares/uniques (line above the base type). Null otherwise.</summary>
    public string? Name { get; init; }

    /// <summary>Item Level if present in the clipboard text. Null for items that don't report it (e.g. gems, currency).</summary>
    public int? ItemLevel { get; init; }

    /// <summary>True if the clipboard reported a "Stack Size:" line — i.e. a stackable currency/fragment/etc.</summary>
    public bool IsStackable { get; init; }

    /// <summary>True if the item is a skill/support gem (Rarity line reads "Gem").</summary>
    public bool IsGem { get; init; }

    /// <summary>Quality percentage if the clipboard reported a "Quality: +N%" line. Null otherwise.</summary>
    public int? Quality { get; init; }

    /// <summary>Gem level (the "Level: N" line) — only populated for gems. Null otherwise.</summary>
    public int? GemLevel { get; init; }

    /// <summary>Raw clipboard text, kept for debugging / re-parsing.</summary>
    public string RawText { get; init; } = string.Empty;
}
