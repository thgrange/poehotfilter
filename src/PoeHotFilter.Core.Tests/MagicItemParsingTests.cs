using PoeHotFilter.Core.Parsing;
using Xunit;

namespace PoeHotFilter.Core.Tests;

/// <summary>
/// Magic items bake a prefix and/or suffix into the single header name line
/// (e.g. "Sturdy Sapphire Ring of the Whelpling"). The parser must recover the
/// clean craftable base ("Sapphire Ring") so the generated BaseType actually matches in game.
/// </summary>
public class MagicItemParsingTests
{
    private const string MagicRing =
        "Item Class: Rings\r\n" +
        "Rarity: Magic\r\n" +
        "Sturdy Sapphire Ring of the Whelpling\r\n" +
        "--------\r\n" +
        "Requirements:\r\n" +
        "Level: 20\r\n" +
        "--------\r\n" +
        "Item Level: 45\r\n" +
        "--------\r\n" +
        "+20% to Cold Resistance\r\n";

    [Fact]
    public void Magic_item_resolves_clean_base_type_stripping_prefix_and_suffix()
    {
        var item = ItemParser.TryParse(MagicRing);

        Assert.NotNull(item);
        Assert.Equal("Magic", item!.Rarity);
        Assert.Equal("Sapphire Ring", item.BaseType);
    }

    [Fact]
    public void Magic_item_with_only_a_prefix_resolves_base_type()
    {
        const string clip =
            "Item Class: Body Armours\r\n" +
            "Rarity: Magic\r\n" +
            "Sturdy Vaal Regalia\r\n" +
            "--------\r\n" +
            "Item Level: 84\r\n";

        var item = ItemParser.TryParse(clip);

        Assert.NotNull(item);
        Assert.Equal("Vaal Regalia", item!.BaseType);
    }

    [Fact]
    public void Magic_item_with_only_a_suffix_resolves_base_type()
    {
        const string clip =
            "Item Class: Body Armours\r\n" +
            "Rarity: Magic\r\n" +
            "Vaal Regalia of the Whelpling\r\n" +
            "--------\r\n" +
            "Item Level: 84\r\n";

        var item = ItemParser.TryParse(clip);

        Assert.NotNull(item);
        Assert.Equal("Vaal Regalia", item!.BaseType);
    }

    [Fact]
    public void Rare_item_still_uses_the_two_line_base_type()
    {
        const string clip =
            "Item Class: Body Armours\r\n" +
            "Rarity: Rare\r\n" +
            "Dread Veil\r\n" +
            "Vaal Regalia\r\n" +
            "--------\r\n" +
            "Item Level: 84\r\n";

        var item = ItemParser.TryParse(clip);

        Assert.NotNull(item);
        Assert.Equal("Vaal Regalia", item!.BaseType);
        Assert.Equal("Dread Veil", item.Name);
    }

    [Fact]
    public void Unknown_magic_base_falls_back_to_raw_line_without_crashing()
    {
        const string clip =
            "Item Class: Whatever\r\n" +
            "Rarity: Magic\r\n" +
            "Totally Made Up Nonexistent Thing\r\n" +
            "--------\r\n" +
            "Item Level: 1\r\n";

        var item = ItemParser.TryParse(clip);

        Assert.NotNull(item);
        Assert.Equal("Totally Made Up Nonexistent Thing", item!.BaseType);
    }
}
