using System.Globalization;
using System.Text.RegularExpressions;
using PoeHotFilter.Core.Models;

namespace PoeHotFilter.Core.Filter;

/// <summary>A reusable appearance preset taken from an existing filter (or a built-in fallback).</summary>
public sealed class StylePreset
{
    public required string Name { get; init; }
    public FilterColor TextColor { get; init; }
    public FilterColor BorderColor { get; init; }
    public FilterColor BackgroundColor { get; init; }
    public int FontSize { get; init; } = 45;

    /// <summary>Minimap icon (None if the source block had no MinimapIcon).</summary>
    public IconShape IconShape { get; init; } = IconShape.None;
    public IconColor IconColor { get; init; } = IconColor.White;
    public int IconSize { get; init; } = 1;

    /// <summary>Where it came from — "Active filter" or "Built-in".</summary>
    public string Source { get; init; } = "";
}

/// <summary>
/// Produces a small, curated set of named presets the popup can offer. Each slot (Divine, Chaos,
/// Exalted, Rare 6-Link, Unique) is filled with the look the user's *active* filter already gives
/// to that thing — so "Divine" carries the styling their filter uses for a Divine Orb. If the
/// active filter has no matching rule, the slot falls back to a sensible built-in.
/// </summary>
public static class FilterStyleExtractor
{
    private static readonly Regex ColorRe =
        new(@"^\s*Set(Text|Border|Background)Color\s+(\d+)\s+(\d+)\s+(\d+)(?:\s+(\d+))?",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex FontRe =
        new(@"^\s*SetFontSize\s+(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex IconRe =
        new(@"^\s*MinimapIcon\s+(\d+)\s+(\w+)\s+(\w+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex BaseTypeRe =
        new(@"^\s*BaseType\b(.*)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex RarityRe =
        new(@"^\s*Rarity\b(.*)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex LinkedSocketsRe =
        new(@"^\s*LinkedSockets\s*(<=|>=|<|>|=)?\s*(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // ---- a parsed Show/Hide block: the conditions we care about + its style ----
    private sealed class Block
    {
        public readonly List<string> BaseTypes = new();
        public readonly List<string> Rarities = new();
        public (string Op, int Val)? LinkedSockets;
        public FilterColor? Text, Border, Bg;
        public int Font = 45;
        public IconShape IconShape = IconShape.None;
        public IconColor IconColor = IconColor.White;
        public int IconSize = 1;

        public bool HasStyle => Text is not null;
        public bool HasBaseType(string bt) =>
            BaseTypes.Any(x => x.Equals(bt, StringComparison.OrdinalIgnoreCase));
        public bool HasRarity(string r) =>
            Rarities.Any(x => x.Equals(r, StringComparison.OrdinalIgnoreCase));

        /// <summary>True for blocks that specifically target 6-links (6 satisfies, 5 does not).</summary>
        public bool Is6Link
        {
            get
            {
                if (LinkedSockets is null) return false;
                var (op, v) = LinkedSockets.Value;
                bool Sat(int n) => op switch
                {
                    "" or "=" => n == v,
                    ">=" => n >= v,
                    ">" => n > v,
                    "<=" => n <= v,
                    "<" => n < v,
                    _ => false
                };
                return Sat(6) && !Sat(5);
            }
        }
    }

    private static List<Block> ParseBlocks(string filterText)
    {
        var lines = filterText.Replace("\r\n", "\n").Split('\n');
        var blocks = new List<Block>();
        Block? cur = null;

        foreach (var raw in lines)
        {
            var trimmed = raw.TrimStart();

            if (trimmed.StartsWith("Show", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("Hide", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("Minimal", StringComparison.OrdinalIgnoreCase))
            {
                cur = new Block();
                blocks.Add(cur);
                continue;
            }
            if (cur is null) continue;

            var colm = ColorRe.Match(raw);
            if (colm.Success)
            {
                var color = ParseColor(colm);
                switch (colm.Groups[1].Value.ToLowerInvariant())
                {
                    case "text": cur.Text = color; break;
                    case "border": cur.Border = color; break;
                    case "background": cur.Bg = color; break;
                }
                continue;
            }

            var fm = FontRe.Match(raw);
            if (fm.Success) { cur.Font = int.Parse(fm.Groups[1].Value); continue; }

            var im = IconRe.Match(raw);
            if (im.Success)
            {
                cur.IconSize = int.Parse(im.Groups[1].Value);
                Enum.TryParse(im.Groups[2].Value, ignoreCase: true, out IconColor ic); cur.IconColor = ic;
                Enum.TryParse(im.Groups[3].Value, ignoreCase: true, out IconShape sh); cur.IconShape = sh;
                continue;
            }

            var bm = BaseTypeRe.Match(raw);
            if (bm.Success)
            {
                foreach (Match q in Regex.Matches(bm.Groups[1].Value, "\"([^\"]+)\""))
                    cur.BaseTypes.Add(q.Groups[1].Value);
                continue;
            }

            var rm = RarityRe.Match(raw);
            if (rm.Success)
            {
                foreach (Match t in Regex.Matches(rm.Groups[1].Value, @"\b(Normal|Magic|Rare|Unique)\b",
                             RegexOptions.IgnoreCase))
                    cur.Rarities.Add(t.Groups[1].Value);
                continue;
            }

            var lm = LinkedSocketsRe.Match(raw);
            if (lm.Success)
                cur.LinkedSockets = (lm.Groups[1].Value, int.Parse(lm.Groups[2].Value));
        }

        return blocks;
    }

    // ---- the curated slots: (display name, "does this block match?", built-in fallback) ----
    private sealed record Slot(string Name, Func<Block, bool> Match, StylePreset Fallback);

    private static readonly Slot[] Slots =
    {
        new("Divine", b => b.HasBaseType("Divine Orb"),
            new StylePreset { Name = "Divine", Source = "Built-in",
                TextColor = new(255,0,0,255), BorderColor = new(255,0,0,255), BackgroundColor = new(255,255,255,255),
                FontSize = 45, IconShape = IconShape.Star, IconColor = IconColor.Red, IconSize = 0 }),

        new("Chaos", b => b.HasBaseType("Chaos Orb"),
            new StylePreset { Name = "Chaos", Source = "Built-in",
                TextColor = new(210,178,135,255), BorderColor = new(201,162,75,255), BackgroundColor = new(26,26,26,255),
                FontSize = 45, IconShape = IconShape.Circle, IconColor = IconColor.Yellow, IconSize = 1 }),

        new("Exalted", b => b.HasBaseType("Exalted Orb"),
            new StylePreset { Name = "Exalted", Source = "Built-in",
                TextColor = new(240,200,140,255), BorderColor = new(240,200,140,255), BackgroundColor = new(40,28,12,255),
                FontSize = 45, IconShape = IconShape.Star, IconColor = IconColor.Yellow, IconSize = 0 }),

        new("Rare 6-Link", b => b.HasRarity("Rare") && b.Is6Link,
            new StylePreset { Name = "Rare 6-Link", Source = "Built-in",
                TextColor = new(255,255,119,255), BorderColor = new(255,255,119,255), BackgroundColor = new(0,0,0,220),
                FontSize = 40, IconShape = IconShape.Square, IconColor = IconColor.White, IconSize = 1 }),

        new("Unique", b => b.HasRarity("Unique") && b.BaseTypes.Count == 0,
            new StylePreset { Name = "Unique", Source = "Built-in",
                TextColor = new(175,96,37,255), BorderColor = new(175,96,37,255), BackgroundColor = new(0,0,0,230),
                FontSize = 38, IconShape = IconShape.Moon, IconColor = IconColor.Orange, IconSize = 1 }),
    };

    /// <summary>
    /// One preset per slot. Each is taken from the first matching block of the active filter
    /// (top of file = highest priority), or the slot's built-in fallback if nothing matches.
    /// </summary>
    public static IReadOnlyList<StylePreset> CuratedPresets(string? filterText)
    {
        var blocks = string.IsNullOrWhiteSpace(filterText)
            ? new List<Block>()
            : ParseBlocks(filterText);

        var result = new List<StylePreset>(Slots.Length);
        foreach (var slot in Slots)
        {
            var hit = blocks.FirstOrDefault(b => b.HasStyle && slot.Match(b));
            result.Add(hit is null ? slot.Fallback : ToPreset(slot.Name, hit));
        }
        return result;
    }

    /// <summary>The built-in fallbacks on their own (used when there is no active filter at all).</summary>
    public static IReadOnlyList<StylePreset> BuiltIns() => Slots.Select(s => s.Fallback).ToList();

    private static StylePreset ToPreset(string name, Block b) => new()
    {
        Name = name,
        TextColor = b.Text!.Value,
        BorderColor = b.Border ?? b.Text!.Value,
        BackgroundColor = b.Bg ?? new FilterColor(0, 0, 0, 255),
        FontSize = b.Font,
        IconShape = b.IconShape,
        IconColor = b.IconColor,
        IconSize = b.IconSize,
        Source = "Active filter"
    };

    private static FilterColor ParseColor(Match m)
    {
        byte r = byte.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
        byte g = byte.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
        byte b = byte.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture);
        byte a = m.Groups[5].Success ? byte.Parse(m.Groups[5].Value, CultureInfo.InvariantCulture) : (byte)255;
        return new FilterColor(r, g, b, a);
    }
}
