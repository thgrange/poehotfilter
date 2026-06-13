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

    [Fact]
    public void Hash_inside_a_quoted_value_is_not_stripped_as_a_comment()
    {
        const string filter = "Show\n    BaseType \"A#B\"\n    SetTextColor 1 2 3\n";

        var m = ActiveStyleResolver.Resolve(filter, Item(baseType: "A#B"));

        Assert.True(m.Matched);
        Assert.Equal(new FilterColor(1, 2, 3, 255), m.Text);
    }

    [Fact]
    public void Bare_show_block_with_no_conditions_matches_anything()
    {
        const string filter = "Show\n    SetTextColor 9 9 9\n";

        var m = ActiveStyleResolver.Resolve(filter, Item());

        Assert.True(m.Matched);
        Assert.Equal(new FilterColor(9, 9, 9, 255), m.Text);
    }

    [Fact]
    public void Minimal_block_is_treated_as_visible()
    {
        const string filter = "Minimal\n    BaseType \"Vaal Regalia\"\n    SetTextColor 7 7 7\n";

        var m = ActiveStyleResolver.Resolve(filter, Item());

        Assert.True(m.Matched);
        Assert.False(m.Hidden);
    }
}
