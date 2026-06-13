using System.Globalization;
using System.Text.RegularExpressions;
using PoeHotFilter.Core.Models;

namespace PoeHotFilter.Core.Filter;

/// <summary>The appearance an item currently has under the active filter (or a rarity-default fallback).</summary>
public sealed record StyleMatch(
    bool Matched,
    bool Hidden,
    FilterColor Text,
    FilterColor Border,
    FilterColor Background,
    int FontSize,
    IconShape IconShape,
    IconColor IconColor,
    int IconSize,
    string Source);

/// <summary>
/// Evaluates the active filter against a captured item to find the appearance it currently has.
/// Conservative: a block only matches if EVERY condition it carries is one we can evaluate from
/// clipboard data AND passes. Any unrecognised condition disqualifies the block (it is skipped),
/// so we never claim a "special" look that depends on data we don't have (sockets, corrupted, …).
/// The active filter's managed `Import` line is opaque, so our own injected rules are not considered.
/// </summary>
public static class ActiveStyleResolver
{
    // Lines that never affect matching (styling/effects/flow).
    private static readonly HashSet<string> StyleKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "SetTextColor", "SetBorderColor", "SetBackgroundColor", "SetFontSize",
        "MinimapIcon", "PlayAlertSound", "PlayAlertSoundPositional", "CustomAlertSound",
        "CustomAlertSoundOptional", "PlayEffect", "DisableDropSound", "EnableDropSound", "Continue"
    };

    // The only conditions we can evaluate from a captured item.
    private static readonly HashSet<string> EvaluatedConditions = new(StringComparer.OrdinalIgnoreCase)
    {
        "Class", "BaseType", "Rarity", "ItemLevel", "Quality", "GemLevel"
    };

    private static readonly Dictionary<string, int> RarityOrder = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Normal"] = 0, ["Magic"] = 1, ["Rare"] = 2, ["Unique"] = 3
    };

    private static readonly Regex ColorArgs =
        new(@"(\d+)\s+(\d+)\s+(\d+)(?:\s+(\d+))?", RegexOptions.Compiled);
    // MinimapIcon <size> <Color> <Shape>  (group 1 = size, 2 = colour, 3 = shape)
    private static readonly Regex IconArgs =
        new(@"MinimapIcon\s+(-?\d+)\s+(\w+)\s+(\w+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static StyleMatch Resolve(string? filterText, ParsedItem item)
    {
        if (!string.IsNullOrWhiteSpace(filterText))
        {
            foreach (var block in ParseBlocks(filterText!))
            {
                if (block.Disqualified) continue;
                if (block.Conditions.All(c => c(item)))
                    return Build(block, item);
            }
        }
        return Fallback(item, matched: false, hidden: false, source: "Default (rarity)");
    }

    private sealed class Block
    {
        public bool Hidden;
        public bool Disqualified;
        public readonly List<Func<ParsedItem, bool>> Conditions = new();
        public FilterColor? Text, Border, Bg;
        public int? Font;
        public bool HasIcon;
        public IconShape IconShape = IconShape.None;
        public IconColor IconColor = IconColor.White;
        public int IconSize = 1;
    }

    private static List<Block> ParseBlocks(string filterText)
    {
        var lines = filterText.Replace("\r\n", "\n").Split('\n');
        var blocks = new List<Block>();
        Block? cur = null;

        foreach (var raw in lines)
        {
            var line = StripComment(raw).Trim();
            if (line.Length == 0) continue;

            var kw = FirstToken(line);

            // Block starters. This app targets PoE 1, where `Minimal` is not a real keyword;
            // we treat it as a visible (Show-like) block — only `Hide` counts as hidden.
            if (kw.Equals("Show", StringComparison.OrdinalIgnoreCase) ||
                kw.Equals("Hide", StringComparison.OrdinalIgnoreCase) ||
                kw.Equals("Minimal", StringComparison.OrdinalIgnoreCase))
            {
                cur = new Block { Hidden = kw.Equals("Hide", StringComparison.OrdinalIgnoreCase) };
                blocks.Add(cur);
                continue;
            }
            if (cur is null) continue;

            if (StyleKeywords.Contains(kw)) { CaptureStyle(cur, kw, line); continue; }
            if (EvaluatedConditions.Contains(kw)) { AddCondition(cur, kw, line); continue; }

            // Unknown condition keyword -> we cannot prove this block matches.
            cur.Disqualified = true;
        }
        return blocks;
    }

    // Strip a trailing `# comment`, but ignore a '#' that sits inside a quoted value
    // (e.g. BaseType "A#B") so we don't truncate a real condition into garbage.
    private static string StripComment(string line)
    {
        bool inQuote = false;
        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] == '"') inQuote = !inQuote;
            else if (line[i] == '#' && !inQuote) return line.Substring(0, i);
        }
        return line;
    }

    private static string FirstToken(string line)
    {
        int sp = line.IndexOfAny(new[] { ' ', '\t' });
        return sp < 0 ? line : line.Substring(0, sp);
    }

    // ---- styling ----

    private static void CaptureStyle(Block b, string kw, string line)
    {
        switch (kw.ToLowerInvariant())
        {
            case "settextcolor": b.Text = ParseColor(line); break;
            case "setbordercolor": b.Border = ParseColor(line); break;
            case "setbackgroundcolor": b.Bg = ParseColor(line); break;
            case "setfontsize":
                var fm = Regex.Match(line, @"SetFontSize\s+(\d+)", RegexOptions.IgnoreCase);
                if (fm.Success) b.Font = int.Parse(fm.Groups[1].Value, CultureInfo.InvariantCulture);
                break;
            case "minimapicon":
                var im = IconArgs.Match(line);
                if (im.Success)
                {
                    b.IconSize = int.Parse(im.Groups[1].Value, CultureInfo.InvariantCulture);
                    if (Enum.TryParse(im.Groups[2].Value, ignoreCase: true, out IconColor ic)) b.IconColor = ic;
                    if (Enum.TryParse(im.Groups[3].Value, ignoreCase: true, out IconShape sh)) b.IconShape = sh;
                    b.HasIcon = b.IconSize >= 0 && b.IconShape != IconShape.None;
                }
                break;
        }
    }

    private static FilterColor ParseColor(string line)
    {
        var m = ColorArgs.Match(line);
        if (!m.Success) return new FilterColor(255, 255, 255, 255);
        byte C(int i) => (byte)Math.Clamp(int.Parse(m.Groups[i].Value, CultureInfo.InvariantCulture), 0, 255);
        byte a = m.Groups[4].Success ? C(4) : (byte)255;
        return new FilterColor(C(1), C(2), C(3), a);
    }

    // ---- conditions ----

    private static void AddCondition(Block b, string kw, string line)
    {
        var rest = line.Substring(kw.Length).Trim();
        switch (kw.ToLowerInvariant())
        {
            case "class": b.Conditions.Add(StringCondition(rest, i => i.ItemClass)); break;
            case "basetype": b.Conditions.Add(StringCondition(rest, i => i.BaseType)); break;
            case "rarity": b.Conditions.Add(RarityCondition(rest)); break;
            case "itemlevel": b.Conditions.Add(NumericCondition(rest, i => i.ItemLevel)); break;
            case "quality": b.Conditions.Add(NumericCondition(rest, i => i.Quality)); break;
            case "gemlevel": b.Conditions.Add(NumericCondition(rest, i => i.GemLevel)); break;
        }
    }

    private static Func<ParsedItem, bool> StringCondition(string rest, Func<ParsedItem, string?> sel)
    {
        bool exact = false;
        if (rest.StartsWith("==")) { exact = true; rest = rest.Substring(2).Trim(); }
        else if (rest.StartsWith("=")) { exact = true; rest = rest.Substring(1).Trim(); }
        var values = ExtractValues(rest);
        return item =>
        {
            var hay = sel(item);
            if (string.IsNullOrEmpty(hay)) return false;
            return exact
                ? values.Any(v => hay.Equals(v, StringComparison.OrdinalIgnoreCase))
                : values.Any(v => hay.Contains(v, StringComparison.OrdinalIgnoreCase));
        };
    }

    private static Func<ParsedItem, bool> RarityCondition(string rest)
    {
        var (op, remainder) = SplitOperator(rest);
        var values = ExtractValues(remainder);
        return item =>
        {
            if (item.Rarity is null || !RarityOrder.TryGetValue(item.Rarity, out var iv)) return false;
            if (op is "" or "=" or "==")
                return values.Any(v => v.Equals(item.Rarity, StringComparison.OrdinalIgnoreCase));
            if (values.Count == 0 || !RarityOrder.TryGetValue(values[0], out var tv)) return false;
            return Compare(iv, op, tv);
        };
    }

    private static Func<ParsedItem, bool> NumericCondition(string rest, Func<ParsedItem, int?> sel)
    {
        var (op, remainder) = SplitOperator(rest);
        var first = remainder.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (first is null || !int.TryParse(first, NumberStyles.Integer, CultureInfo.InvariantCulture, out var target))
            return _ => false;
        var o = op == "" ? "=" : op;
        return item =>
        {
            var v = sel(item);
            return v is not null && Compare(v.Value, o, target);
        };
    }

    private static List<string> ExtractValues(string rest)
    {
        var vals = new List<string>();
        var matches = Regex.Matches(rest, "\"([^\"]+)\"");
        if (matches.Count > 0)
            foreach (Match q in matches) vals.Add(q.Groups[1].Value);
        else
            vals.AddRange(rest.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries));
        return vals;
    }

    private static (string op, string rest) SplitOperator(string s)
    {
        s = s.Trim();
        foreach (var op in new[] { "<=", ">=", "==", "<", ">", "=" })
            if (s.StartsWith(op)) return (op, s.Substring(op.Length).Trim());
        return ("", s);
    }

    private static bool Compare(int a, string op, int b) => op switch
    {
        "<=" => a <= b,
        ">=" => a >= b,
        "<" => a < b,
        ">" => a > b,
        "=" or "==" or "" => a == b,
        _ => false
    };

    // ---- result building ----

    private static StyleMatch Build(Block b, ParsedItem item)
    {
        if (b.Hidden)
            return Fallback(item, matched: true, hidden: true, source: "Hidden in filter");

        var text = b.Text ?? RarityColor(item);
        var border = b.Border ?? text;
        var bg = b.Bg ?? new FilterColor(0, 0, 0, 255);
        var font = b.Font ?? 32;
        return new StyleMatch(
            Matched: true, Hidden: false,
            Text: text, Border: border, Background: bg, FontSize: font,
            IconShape: b.HasIcon ? b.IconShape : IconShape.None,
            IconColor: b.HasIcon ? b.IconColor : IconColor.White,
            IconSize: b.HasIcon ? b.IconSize : 1,
            Source: "Active filter");
    }

    private static StyleMatch Fallback(ParsedItem item, bool matched, bool hidden, string source)
    {
        var text = RarityColor(item);
        return new StyleMatch(
            Matched: matched, Hidden: hidden,
            Text: text,
            Border: new FilterColor(text.R, text.G, text.B, 0), // transparent
            Background: new FilterColor(0, 0, 0, 255),
            FontSize: 32,
            IconShape: IconShape.None, IconColor: IconColor.White, IconSize: 1,
            Source: source);
    }

    private static FilterColor RarityColor(ParsedItem item)
    {
        if (item.IsGem) return new FilterColor(27, 162, 155);          // gem cyan
        if (item.Rarity is null)
            return item.IsStackable
                ? new FilterColor(170, 158, 130)                      // currency tan
                : new FilterColor(200, 200, 200);                     // unknown -> normal
        return item.Rarity.ToLowerInvariant() switch
        {
            "magic" => new FilterColor(136, 136, 255),
            "rare" => new FilterColor(255, 255, 119),
            "unique" => new FilterColor(175, 96, 37),
            _ => new FilterColor(200, 200, 200),                      // normal
        };
    }
}
