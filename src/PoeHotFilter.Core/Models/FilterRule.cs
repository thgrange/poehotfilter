namespace PoeHotFilter.Core.Models;

/// <summary>Which rarity the rule targets. <c>Any</c> emits no Rarity condition.</summary>
public enum RarityFilter
{
    Any,
    Normal,
    Magic,
    Rare,
    Unique,

    /// <summary>Everything except Unique — emitted as <c>Rarity &lt;= Rare</c>.</summary>
    AnyNonUnique
}

/// <summary>Corrupted condition. <c>Any</c> emits nothing; Yes/No emit <c>Corrupted True/False</c>.</summary>
public enum CorruptedMode
{
    Any,
    Yes,
    No
}

/// <summary>Whether the rule shows (highlights) or hides the matching items.</summary>
public enum BlockAction
{
    Show,
    Hide
}

/// <summary>Minimap icon shapes supported by PoE filters.</summary>
public enum IconShape
{
    None,
    Circle, Diamond, Hexagon, Square, Star, Triangle,
    Cross, Moon, Raindrop, Kite, Pentagon, UpsideDownHouse
}

/// <summary>Minimap icon colours supported by PoE filters.</summary>
public enum IconColor
{
    Red, Green, Blue, Brown, White, Yellow, Cyan, Grey, Orange, Pink, Purple
}

/// <summary>How the item level condition should be emitted in the filter block.</summary>
public enum IlvlMatchMode
{
    /// <summary>No ItemLevel condition at all — matches the base type at any ilvl.</summary>
    Any,

    /// <summary><c>ItemLevel &gt;= N</c></summary>
    GreaterOrEqual,

    /// <summary><c>ItemLevel = N</c></summary>
    Exact
}

/// <summary>An RGBA colour (0-255 each). PoE filters use RGBA on SetTextColor/SetBorderColor/SetBackgroundColor.</summary>
public readonly record struct FilterColor(byte R, byte G, byte B, byte A = 255)
{
    public string ToFilterArgs() => $"{R} {G} {B} {A}";

    public static FilterColor FromHex(string hex)
    {
        hex = hex.TrimStart('#');
        byte r = Convert.ToByte(hex.Substring(0, 2), 16);
        byte g = Convert.ToByte(hex.Substring(2, 2), 16);
        byte b = Convert.ToByte(hex.Substring(4, 2), 16);
        byte a = hex.Length >= 8 ? Convert.ToByte(hex.Substring(6, 2), 16) : (byte)255;
        return new FilterColor(r, g, b, a);
    }

    public string ToHex() => $"#{R:X2}{G:X2}{B:X2}{A:X2}";
}

/// <summary>
/// A user-defined highlight rule. This is the source of truth (persisted to JSON);
/// the .filter file is regenerated from the full set of these.
/// </summary>
public sealed class FilterRule
{
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>The exact base type, e.g. "Vaal Regalia". This is always the match.</summary>
    public required string BaseType { get; set; }

    /// <summary>The PoE class, e.g. "Body Armours". Kept for display only; never emitted as a condition.</summary>
    public string? ItemClass { get; set; }

    /// <summary>True if the source item was a stackable currency (controls whether StackSize UI is offered).</summary>
    public bool Stackable { get; set; }

    /// <summary>True if the source item was a gem (drives GemLevel UI and suppresses the Rarity condition).</summary>
    public bool IsGem { get; set; }

    /// <summary>Optional rarity constraint, e.g. target only Unique Vaal Regalia.</summary>
    public RarityFilter Rarity { get; set; } = RarityFilter.Any;

    /// <summary>Optional corrupted constraint.</summary>
    public CorruptedMode Corrupted { get; set; } = CorruptedMode.Any;

    /// <summary>Show (highlight) or Hide the matching items.</summary>
    public BlockAction Action { get; set; } = BlockAction.Show;

    public IlvlMatchMode IlvlMode { get; set; } = IlvlMatchMode.Any;

    /// <summary>The ilvl threshold/value. Ignored when IlvlMode == Any.</summary>
    public int IlvlValue { get; set; }

    /// <summary>Quality match mode (emits <c>Quality</c>). Suppressed for currency. Reuses the ilvl Any/&gt;=/Exact semantics.</summary>
    public IlvlMatchMode QualityMode { get; set; } = IlvlMatchMode.Any;

    /// <summary>The quality threshold/value. Ignored when QualityMode == Any.</summary>
    public int QualityValue { get; set; }

    /// <summary>Gem level match mode (emits <c>GemLevel</c>). Only emitted for gems. Reuses the ilvl Any/&gt;=/Exact semantics.</summary>
    public IlvlMatchMode GemLevelMode { get; set; } = IlvlMatchMode.Any;

    /// <summary>The gem level threshold/value. Ignored when GemLevelMode == Any.</summary>
    public int GemLevelValue { get; set; }

    /// <summary>Stack-size lower bound (emits <c>StackSize &gt;= N</c>); 0 = no lower bound.</summary>
    public int StackMin { get; set; }

    /// <summary>Stack-size upper bound (emits <c>StackSize &lt;= N</c>); 0 = no upper bound.</summary>
    public int StackMax { get; set; }

    /// <summary>
    /// Cluster jewels only: the small passive node name granted by the jewel's enchant
    /// (emits <c>EnchantmentPassiveNode "X"</c>, e.g. "Aura Effect"). Null/empty = any enchant.
    /// </summary>
    public string? EnchantNode { get; set; }

    /// <summary>Cluster jewels only: match mode for the number of added passives (emits <c>EnchantmentPassiveNum</c>).</summary>
    public IlvlMatchMode PassiveNumMode { get; set; } = IlvlMatchMode.Any;

    /// <summary>The passives-count threshold/value. Ignored when <see cref="PassiveNumMode"/> == Any.</summary>
    public int PassiveNumValue { get; set; }

    public FilterColor TextColor { get; set; } = new(255, 255, 255);
    public FilterColor BorderColor { get; set; } = new(255, 255, 255);
    public FilterColor BackgroundColor { get; set; } = new(20, 20, 20);

    public int FontSize { get; set; } = 45;

    /// <summary>Minimap icon shape. <c>None</c> emits no MinimapIcon line.</summary>
    public IconShape IconShape { get; set; } = IconShape.None;

    /// <summary>Minimap icon colour (used only when <see cref="IconShape"/> != None).</summary>
    public IconColor IconColor { get; set; } = IconColor.White;

    /// <summary>Minimap icon size: 0 = large, 1 = medium, 2 = small.</summary>
    public int IconSize { get; set; } = 1;

    /// <summary>Built-in drop sound id 1-16; 0 = no sound (emits no PlayAlertSound).</summary>
    public int AlertSound { get; set; } = 0;

    /// <summary>Drop sound volume 0-300 (PoE range). Only used when <see cref="AlertSound"/> != 0.</summary>
    public int AlertVolume { get; set; } = 100;

    /// <summary>If false, the rule is kept in storage but not emitted to the filter.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>For display in the management list. Auto-filled from BaseType if empty.</summary>
    public string? Label { get; set; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public string DisplayLabel => string.IsNullOrWhiteSpace(Label) ? BaseType : Label!;
}
