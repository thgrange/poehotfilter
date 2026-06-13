# Current-Appearance Pre-fill Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** When an item is captured, pre-fill the popup editor (colours, font, minimap icon) with the appearance the item currently has under the active loot filter, and flag when that item is currently hidden.

**Architecture:** A new pure Core evaluator (`ActiveStyleResolver`) parses the active filter's `Show`/`Hide` blocks in order and returns the first block whose conditions ALL match the captured item — conservatively skipping any block that carries a condition we can't evaluate from clipboard data. `LiveFilterService` exposes it; `Program.cs` ships the result to the web UI inside `CapturedItemMsg.current`; `app.js` pre-fills the editor controls and shows a "currently hidden" badge.

**Tech Stack:** C# / .NET 8 (Core + Photino), xUnit (existing test project), vanilla JS/HTML/CSS (WebView2 UI).

---

## Prerequisites

- **.NET SDK present** (`dotnet --version` → 9.0.117 on this machine; builds net8.0 fine).
- **Git is NOT initialized** in this workspace. The commit steps below assume a repo. Before
  starting, either run `git init` once at `C:\Users\scrii\Desktop\PoeHotFilter` so the commit
  steps work, or treat each "Commit" step as a no-op checkpoint. The build/test step in each task
  is the real verification gate.
- Per project memory `always-rebuild-relaunch-after-changes`: after frontend/C# changes, close the
  app, rebuild, relaunch — do this in Task 7's manual verification.

## File Structure

- **Create** `src/PoeHotFilter.Core/Filter/ActiveStyleResolver.cs` — the evaluator + `StyleMatch` record. Pure, no I/O. One responsibility: "given filter text + item, what does it look like now?"
- **Create** `src/PoeHotFilter.Core.Tests/ActiveStyleResolverTests.cs` — unit tests for the evaluator.
- **Modify** `src/PoeHotFilter.Core/LiveFilterService.cs` — add `CurrentStyleFor(ParsedItem)` (thin file-read wrapper, mirrors `LoadPresets`).
- **Modify** `src/PoeHotFilter.Photino/WebMessage.cs` — add `CurrentStyleDto` + `CapturedItemMsg.Current`.
- **Modify** `src/PoeHotFilter.Photino/Program.cs` — map `CurrentStyleFor` into the captured message at capture time.
- **Modify** `src/PoeHotFilter.Photino/wwwroot/app.js` — pre-fill controls from `item.current` in `openPopup`; toggle the badge.
- **Modify** `src/PoeHotFilter.Photino/wwwroot/index.html` — add the "currently hidden" badge element + CSS.

> **Deviation from spec §1:** the spec suggested factoring `FilterStyleExtractor`'s block parser out for reuse. `FilterStyleExtractor.Block` discards unrecognised condition lines, so it cannot represent "this block has a condition we can't evaluate" — which is exactly what conservative matching needs. We therefore give `ActiveStyleResolver` its own purpose-built parser and leave `FilterStyleExtractor` untouched. Less coupling, no risk to the presets feature.

> **Spec §6 note:** the test project already exists (`src/PoeHotFilter.Core.Tests`, xUnit 2.5.3, `Using Include="Xunit"`, references Core). We only add a test file — no new project.

---

## Task 1: `ActiveStyleResolver` — parsing, Show match, rarity fallback

**Files:**
- Create: `src/PoeHotFilter.Core/Filter/ActiveStyleResolver.cs`
- Test: `src/PoeHotFilter.Core.Tests/ActiveStyleResolverTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `src/PoeHotFilter.Core.Tests/ActiveStyleResolverTests.cs`:

```csharp
using PoeHotFilter.Core.Filter;
using PoeHotFilter.Core.Models;
using Xunit;

namespace PoeHotFilter.Core.Tests;

public class ActiveStyleResolverTests
{
    private static ParsedItem Item(
        string baseType = "Vaal Regalia", string itemClass = "Body Armours",
        string? rarity = "Rare", int? ilvl = 84, bool gem = false, bool stack = false,
        int? quality = null, int? gemLevel = null)
        => new()
        {
            BaseType = baseType, ItemClass = itemClass, Rarity = rarity, ItemLevel = ilvl,
            IsGem = gem, IsStackable = stack, Quality = quality, GemLevel = gemLevel
        };

    [Fact]
    public void Matches_block_by_basetype_substring_and_returns_its_style()
    {
        const string filter = """
            Show
                BaseType "Vaal Regalia"
                SetTextColor 10 20 30 200
                SetBorderColor 40 50 60
                SetBackgroundColor 5 5 5 255
                SetFontSize 40
                MinimapIcon 1 Red Star
            """;

        var m = ActiveStyleResolver.Resolve(filter, Item());

        Assert.True(m.Matched);
        Assert.False(m.Hidden);
        Assert.Equal("Active filter", m.Source);
        Assert.Equal(new FilterColor(10, 20, 30, 200), m.Text);
        Assert.Equal(new FilterColor(40, 50, 60, 255), m.Border);
        Assert.Equal(new FilterColor(5, 5, 5, 255), m.Background);
        Assert.Equal(40, m.FontSize);
        Assert.Equal(IconShape.Star, m.IconShape);
        Assert.Equal(IconColor.Red, m.IconColor);
        Assert.Equal(1, m.IconSize);
    }

    [Fact]
    public void Border_defaults_to_text_and_bg_defaults_to_black_when_absent()
    {
        const string filter = """
            Show
                BaseType "Vaal Regalia"
                SetTextColor 10 20 30
            """;

        var m = ActiveStyleResolver.Resolve(filter, Item());

        Assert.Equal(new FilterColor(10, 20, 30, 255), m.Text);
        Assert.Equal(new FilterColor(10, 20, 30, 255), m.Border);
        Assert.Equal(new FilterColor(0, 0, 0, 255), m.Background);
        Assert.Equal(IconShape.None, m.IconShape);
    }

    [Fact]
    public void Comments_and_blank_lines_do_not_disqualify_a_block()
    {
        const string filter = """
            Show # currency-ish
                # a leading comment
                BaseType "Vaal Regalia"

                SetTextColor 1 2 3
            """;

        var m = ActiveStyleResolver.Resolve(filter, Item());

        Assert.True(m.Matched);
        Assert.Equal(new FilterColor(1, 2, 3, 255), m.Text);
    }

    [Fact]
    public void No_match_falls_back_to_rarity_colour()
    {
        const string filter = """
            Show
                BaseType "Chaos Orb"
                SetTextColor 1 2 3
            """;

        var m = ActiveStyleResolver.Resolve(filter, Item(rarity: "Rare"));

        Assert.False(m.Matched);
        Assert.False(m.Hidden);
        Assert.Equal("Default (rarity)", m.Source);
        Assert.Equal(new FilterColor(255, 255, 119, 255), m.Text); // Rare yellow
    }

    [Fact]
    public void Null_or_empty_filter_falls_back_to_rarity_colour()
    {
        var m = ActiveStyleResolver.Resolve(null, Item(rarity: "Unique"));

        Assert.False(m.Matched);
        Assert.Equal("Default (rarity)", m.Source);
        Assert.Equal(new FilterColor(175, 96, 37, 255), m.Text); // Unique orange
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/PoeHotFilter.Core.Tests/PoeHotFilter.Core.Tests.csproj`
Expected: FAIL — `ActiveStyleResolver` / `StyleMatch` do not exist (compile error).

- [ ] **Step 3: Implement `ActiveStyleResolver`**

Create `src/PoeHotFilter.Core/Filter/ActiveStyleResolver.cs`:

```csharp
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

    private static string StripComment(string line)
    {
        int h = line.IndexOf('#');
        return h >= 0 ? line.Substring(0, h) : line;
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
                var fm = Regex.Match(line, @"(\d+)");
                if (fm.Success) b.Font = int.Parse(fm.Value, CultureInfo.InvariantCulture);
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
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test src/PoeHotFilter.Core.Tests/PoeHotFilter.Core.Tests.csproj`
Expected: PASS (all 5 Task-1 tests green; existing parser tests still green).

- [ ] **Step 5: Commit**

```bash
git add src/PoeHotFilter.Core/Filter/ActiveStyleResolver.cs src/PoeHotFilter.Core.Tests/ActiveStyleResolverTests.cs
git commit -m "feat(core): add ActiveStyleResolver for current-item appearance"
```

---

## Task 2: Conditions, conservative skip, first-match, Hide

**Files:**
- Modify: `src/PoeHotFilter.Core.Tests/ActiveStyleResolverTests.cs` (add tests)

These behaviours are already implemented in Task 1's code. This task verifies the matching
semantics with targeted tests (TDD-by-coverage: they should pass against Task 1's implementation;
if any fails, fix `ActiveStyleResolver` before committing).

- [ ] **Step 1: Add the tests**

Append to `src/PoeHotFilter.Core.Tests/ActiveStyleResolverTests.cs` (inside the class):

```csharp
    [Fact]
    public void Exact_basetype_does_not_substring_match()
    {
        const string filter = """
            Show
                BaseType == "Regalia"
                SetTextColor 1 1 1
            """;

        // "Vaal Regalia" contains "Regalia" but == requires exact equality -> no match.
        var m = ActiveStyleResolver.Resolve(filter, Item(baseType: "Vaal Regalia"));

        Assert.False(m.Matched);
    }

    [Fact]
    public void Class_matches_by_substring()
    {
        const string filter = """
            Show
                Class "Armour"
                SetTextColor 9 9 9
            """;

        var m = ActiveStyleResolver.Resolve(filter, Item(itemClass: "Body Armours"));

        Assert.True(m.Matched);
        Assert.Equal(new FilterColor(9, 9, 9, 255), m.Text);
    }

    [Fact]
    public void Rarity_list_membership_matches()
    {
        const string filter = """
            Show
                Rarity Normal Magic
                SetTextColor 2 2 2
            """;

        Assert.True(ActiveStyleResolver.Resolve(filter, Item(rarity: "Magic")).Matched);
        Assert.False(ActiveStyleResolver.Resolve(filter, Item(rarity: "Rare")).Matched);
    }

    [Fact]
    public void Rarity_operator_form_matches()
    {
        const string filter = """
            Show
                Rarity <= Magic
                SetTextColor 3 3 3
            """;

        Assert.True(ActiveStyleResolver.Resolve(filter, Item(rarity: "Normal")).Matched);
        Assert.True(ActiveStyleResolver.Resolve(filter, Item(rarity: "Magic")).Matched);
        Assert.False(ActiveStyleResolver.Resolve(filter, Item(rarity: "Rare")).Matched);
    }

    [Fact]
    public void Itemlevel_operator_is_respected()
    {
        const string filter = """
            Show
                BaseType "Vaal Regalia"
                ItemLevel >= 84
                SetTextColor 4 4 4
            """;

        Assert.True(ActiveStyleResolver.Resolve(filter, Item(ilvl: 84)).Matched);
        Assert.False(ActiveStyleResolver.Resolve(filter, Item(ilvl: 80)).Matched);
    }

    [Fact]
    public void Missing_numeric_data_fails_that_condition()
    {
        const string filter = """
            Show
                BaseType "Vaal Regalia"
                Quality >= 1
                SetTextColor 5 5 5
            Show
                BaseType "Vaal Regalia"
                SetTextColor 6 6 6
            """;

        // Item has no Quality -> first block fails, falls through to the generic second block.
        var m = ActiveStyleResolver.Resolve(filter, Item(quality: null));

        Assert.True(m.Matched);
        Assert.Equal(new FilterColor(6, 6, 6, 255), m.Text);
    }

    [Fact]
    public void Unknown_condition_disqualifies_block_even_if_others_pass()
    {
        const string filter = """
            Show
                BaseType "Vaal Regalia"
                Sockets 6
                SetTextColor 7 7 7
            Show
                BaseType "Vaal Regalia"
                SetTextColor 8 8 8
            """;

        // First block matches on BaseType but carries an unevaluable Sockets condition -> skipped.
        var m = ActiveStyleResolver.Resolve(filter, Item());

        Assert.True(m.Matched);
        Assert.Equal(new FilterColor(8, 8, 8, 255), m.Text);
    }

    [Fact]
    public void First_matching_block_wins()
    {
        const string filter = """
            Show
                Class "Body Armours"
                SetTextColor 100 0 0
            Show
                BaseType "Vaal Regalia"
                SetTextColor 0 100 0
            """;

        var m = ActiveStyleResolver.Resolve(filter, Item());

        Assert.Equal(new FilterColor(100, 0, 0, 255), m.Text); // top block wins
    }

    [Fact]
    public void Hide_block_match_reports_hidden_with_rarity_colour()
    {
        const string filter = """
            Hide
                BaseType "Vaal Regalia"
                Rarity Rare
            """;

        var m = ActiveStyleResolver.Resolve(filter, Item(rarity: "Rare"));

        Assert.True(m.Matched);
        Assert.True(m.Hidden);
        Assert.Equal("Hidden in filter", m.Source);
        Assert.Equal(new FilterColor(255, 255, 119, 255), m.Text); // Rare yellow
        Assert.Equal(IconShape.None, m.IconShape);
    }
```

- [ ] **Step 2: Run tests**

Run: `dotnet test src/PoeHotFilter.Core.Tests/PoeHotFilter.Core.Tests.csproj`
Expected: PASS. If any fail, fix `ActiveStyleResolver.cs` to satisfy the test, then re-run.

- [ ] **Step 3: Commit**

```bash
git add src/PoeHotFilter.Core.Tests/ActiveStyleResolverTests.cs
git commit -m "test(core): cover ActiveStyleResolver matching semantics"
```

---

## Task 3: Expose `CurrentStyleFor` on `LiveFilterService`

**Files:**
- Modify: `src/PoeHotFilter.Core/LiveFilterService.cs`

No unit test: this is a thin file-read wrapper over the already-tested `ActiveStyleResolver`
(constructing `LiveFilterService` needs `RuleStore`/`FilterFileManager`/`IGameController` and does
I/O — not worth a unit test). Verified by the build + Task 7 manual run.

- [ ] **Step 1: Add the method**

In `src/PoeHotFilter.Core/LiveFilterService.cs`, immediately after the `CaptureHoveredItem()`
method (around line 108), add:

```csharp
    /// <summary>
    /// The appearance the given item currently has under the active filter, used to pre-fill the
    /// editor. Reads the active filter file (the managed Import line is opaque, so our own rules are
    /// not considered). Falls back to a rarity-default look if the filter is missing/locked.
    /// </summary>
    public StyleMatch CurrentStyleFor(ParsedItem item)
    {
        string? text = null;
        try
        {
            if (ActiveFilterPath is not null && File.Exists(ActiveFilterPath))
                text = File.ReadAllText(ActiveFilterPath);
        }
        catch { /* locked/malformed filter -> rarity fallback */ }
        return ActiveStyleResolver.Resolve(text, item);
    }
```

(`PoeHotFilter.Core.Filter` is already imported at the top of the file, so `StyleMatch` /
`ActiveStyleResolver` resolve without a new `using`.)

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build src/PoeHotFilter.Core/PoeHotFilter.Core.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add src/PoeHotFilter.Core/LiveFilterService.cs
git commit -m "feat(core): expose CurrentStyleFor on LiveFilterService"
```

---

## Task 4: `CurrentStyleDto` + `CapturedItemMsg.Current`

**Files:**
- Modify: `src/PoeHotFilter.Photino/WebMessage.cs`

- [ ] **Step 1: Add the DTO and field**

In `src/PoeHotFilter.Photino/WebMessage.cs`, add this class after `CapturedItemMsg` (end of file):

```csharp
/// <summary>The item's current appearance under the active filter, used to pre-fill the editor.</summary>
public sealed class CurrentStyleDto
{
    // Colours as int[4] (R,G,B,A). NOT byte[] — System.Text.Json serializes byte[] as base64,
    // which breaks the JS side (see PushPresets and the photino-bridge-serialization-quirks note).
    public int[] Text { get; set; } = { 255, 255, 255, 255 };
    public int[] Border { get; set; } = { 255, 255, 255, 255 };
    public int[] Background { get; set; } = { 0, 0, 0, 255 };
    public int FontSize { get; set; } = 45;
    public string IconShape { get; set; } = "None";
    public string IconColor { get; set; } = "White";
    public int IconSize { get; set; } = 1;
    public bool Hidden { get; set; }
    public string Source { get; set; } = "";
}
```

Then add this property inside `CapturedItemMsg` (after `GemLevel`):

```csharp
    /// <summary>Current look under the active filter (pre-fills the editor). Null if unavailable.</summary>
    public CurrentStyleDto? Current { get; set; }
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build src/PoeHotFilter.Photino/PoeHotFilter.Photino.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add src/PoeHotFilter.Photino/WebMessage.cs
git commit -m "feat(photino): add CurrentStyleDto to captured item message"
```

---

## Task 5: Map the current style at capture time

**Files:**
- Modify: `src/PoeHotFilter.Photino/Program.cs:251-263`

- [ ] **Step 1: Compute and attach the current style**

In `src/PoeHotFilter.Photino/Program.cs`, inside the capture handler, replace the
`var msg = new CapturedItemMsg { ... };` initializer (lines ~251-262) so it also sets `Current`.
The new code, inserted right before `var msg = new CapturedItemMsg`:

```csharp
            var cur = _service.CurrentStyleFor(item);
            static int[] Rgba(FilterColor c) => new[] { (int)c.R, c.G, c.B, c.A };

            var msg = new CapturedItemMsg
            {
                BaseType = item.BaseType,
                ItemClass = item.ItemClass,
                Name = item.Name,
                Rarity = item.Rarity,
                ItemLevel = item.ItemLevel,
                Stackable = item.IsStackable,
                IsGem = item.IsGem,
                Quality = item.Quality,
                GemLevel = item.GemLevel,
                Current = new CurrentStyleDto
                {
                    Text = Rgba(cur.Text),
                    Border = Rgba(cur.Border),
                    Background = Rgba(cur.Background),
                    FontSize = cur.FontSize,
                    IconShape = cur.IconShape.ToString(),
                    IconColor = cur.IconColor.ToString(),
                    IconSize = cur.IconSize,
                    Hidden = cur.Hidden,
                    Source = cur.Source
                }
            };
```

(`FilterColor` and `StyleMatch` come from `PoeHotFilter.Core.Models` / `.Filter`, both already
used in `Program.cs`. If the compiler reports `FilterColor` not found, add
`using PoeHotFilter.Core.Models;` and `using PoeHotFilter.Core.Filter;` at the top.)

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build src/PoeHotFilter.Photino/PoeHotFilter.Photino.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add src/PoeHotFilter.Photino/Program.cs
git commit -m "feat(photino): attach current item style on capture"
```

---

## Task 6: Pre-fill the editor + "currently hidden" badge

**Files:**
- Modify: `src/PoeHotFilter.Photino/wwwroot/index.html` (markup ~line 101 + CSS ~line 53)
- Modify: `src/PoeHotFilter.Photino/wwwroot/app.js` (`openPopup`, ~line 167-190)

- [ ] **Step 1: Add the badge element and CSS**

In `src/PoeHotFilter.Photino/wwwroot/index.html`, replace the detected line (line 101):

```html
      <div class="detected" id="hDet"></div>
```

with:

```html
      <div class="detected" id="hDet"><span id="hHidden" class="hidden-badge" style="display:none;">CURRENTLY HIDDEN</span></div>
```

Then add this CSS rule right after the `.detected` rule (line 53):

```css
  .hidden-badge{ display:inline-block; margin-left:8px; padding:1px 6px; border-radius:3px;
                 background:#5a1a1a; color:#ffb4b4; font-size:10px; letter-spacing:.4px;
                 vertical-align:middle; }
```

- [ ] **Step 2: Pre-fill controls from `item.current` in `openPopup`**

In `src/PoeHotFilter.Photino/wwwroot/app.js`, in `openPopup(item)`, insert the following block
immediately before the final `render();` call (the line after
`$('btnCancel').textContent = 'Cancel';`, ~line 189):

```javascript
  // Pre-fill the editor with the item's current look under the active filter (if provided).
  if(item.current){
    const c=item.current;
    $('textColor').value=toHex(c.text);   $('textA').value=c.text[3]??255;
    $('borderColor').value=toHex(c.border); $('borderA').value=c.border[3]??255;
    $('bgColor').value=toHex(c.bg ?? c.background); $('bgA').value=(c.bg ?? c.background)[3]??255;
    $('fontSize').value=c.fontSize;
    $('iconShape').value=c.iconShape||'None';
    $('iconColor').value=c.iconColor||'White';
    $('iconSize').value=c.iconSize??1;
  }
  $('hHidden').style.display = (item.current && item.current.hidden) ? '' : 'none';
```

> Note: the C# DTO serializes the background field as `background` (camelCase of `Background`).
> The `c.bg ?? c.background` guards both spellings so the code is robust if the field name changes.

- [ ] **Step 3: Build, run, and manually verify (rebuild & relaunch per project memory)**

Close the app if running, then:

Run: `dotnet build PoeHotFilter.sln`
Expected: Build succeeded.

Run: `dotnet run --project src/PoeHotFilter.Photino`
Then in PoE (or with a filter set as active): hover an item, press the hotkey. Expected:
- The popup opens with the colour pickers / font / minimap icon **already set** to the item's
  current look under the active filter (not the old white defaults).
- The IN-GAME PREVIEW reflects that look immediately, before any edit.
- For an item the filter hides, the **CURRENTLY HIDDEN** badge appears next to DETECTED ILVL.

- [ ] **Step 4: Commit**

```bash
git add src/PoeHotFilter.Photino/wwwroot/index.html src/PoeHotFilter.Photino/wwwroot/app.js
git commit -m "feat(ui): pre-fill editor with current look + currently-hidden badge"
```

---

## Task 7: Full build + test gate

**Files:** none (verification only)

- [ ] **Step 1: Run the whole test suite**

Run: `dotnet test PoeHotFilter.sln`
Expected: PASS — existing parser tests + all new `ActiveStyleResolver` tests green.

- [ ] **Step 2: Full solution build**

Run: `dotnet build PoeHotFilter.sln`
Expected: Build succeeded, 0 errors, 0 warnings introduced by this change.

- [ ] **Step 3: Final manual smoke (if not already done in Task 6)**

Capture a few items of different rarities (normal/magic/rare/unique), a currency, and a gem.
Confirm each pre-fills with a plausible current look and the preview matches. Confirm a hidden
item shows the badge.

---

## Self-Review

- **Spec coverage:** §1 resolver → Task 1/2; §2 `StyleMatch` + fallback colours → Task 1; §3
  `CurrentStyleFor` → Task 3; §4 transport DTO → Task 4/5; §5 frontend pre-fill + badge → Task 6;
  §6 tests → Task 1/2 (existing project, not a new one — noted). Playground untouched (§ Portée).
- **Hide handling** (spec decision: rarity colour + signal) → Task 2 test + Task 6 badge.
- **Conservative matching** (spec decision) → Task 2 `Unknown_condition_disqualifies_block`.
- **Type consistency:** `StyleMatch` fields (Matched/Hidden/Text/Border/Background/FontSize/
  IconShape/IconColor/IconSize/Source) are used identically in Resolver, `CurrentStyleFor`, and
  the Program.cs mapping. DTO uses `Background`; the JS reads `c.bg ?? c.background` defensively.
- **No placeholders:** every code step contains full code; every run step has an expected result.
