using System.Text;
using PoeHotFilter.Core.Models;

namespace PoeHotFilter.Core.Filter;

/// <summary>Renders <see cref="FilterRule"/>s into the text of a .filter file.</summary>
public static class FilterBlockBuilder
{
    public const string SectionHeader =
        "#===============================================================\n" +
        "# Managed by PoeHotFilter — DO NOT EDIT BY HAND.\n" +
        "# This file is regenerated from the app's rule store on every change.\n" +
        "#===============================================================\n";

    /// <summary>Builds the full content of the imported .filter file from all enabled rules.</summary>
    public static string BuildFile(IEnumerable<FilterRule> rules)
    {
        var sb = new StringBuilder();
        sb.Append(SectionHeader);
        sb.Append('\n');

        foreach (var rule in rules.Where(r => r.Enabled))
        {
            sb.Append(BuildBlock(rule));
            sb.Append('\n');
        }

        return sb.ToString();
    }

    /// <summary>Builds a single Show block. Quoting BaseType/Class guards against names with spaces.</summary>
    public static string BuildBlock(FilterRule rule)
    {
        var sb = new StringBuilder();

        // Item-type drives which conditions make sense:
        //  - currency (stackable): no Rarity, no Corrupted, no Quality (ItemLevel is already off for these).
        //  - gem: no Rarity (gems have rarity "Gem"); GemLevel + Quality apply, Corrupted still applies.
        //  - everything else: Rarity/Corrupted/ItemLevel as before, plus Quality.
        bool isCurrency = rule.Stackable;
        bool isGem = rule.IsGem;

        sb.Append("# ").Append(rule.DisplayLabel).Append("  (id: ").Append(rule.Id).Append(")\n");
        sb.Append(rule.Action == BlockAction.Hide ? "Hide\n" : "Show\n");
        sb.Append("    BaseType \"").Append(Escape(rule.BaseType)).Append("\"\n");

        if (!isCurrency && !isGem)
        {
            switch (rule.Rarity)
            {
                case RarityFilter.AnyNonUnique:
                    sb.Append("    Rarity <= Rare\n"); // Normal/Magic/Rare, excludes Unique
                    break;
                case RarityFilter.Any:
                    break;
                default:
                    sb.Append("    Rarity ").Append(rule.Rarity).Append('\n');
                    break;
            }
        }

        if (!isCurrency)
        {
            switch (rule.Corrupted)
            {
                case CorruptedMode.Yes: sb.Append("    Corrupted True\n"); break;
                case CorruptedMode.No: sb.Append("    Corrupted False\n"); break;
                case CorruptedMode.Any:
                default: break;
            }
        }

        AppendThreshold(sb, "ItemLevel", rule.IlvlMode, rule.IlvlValue);

        if (isGem)
            AppendThreshold(sb, "GemLevel", rule.GemLevelMode, rule.GemLevelValue);

        // Quality applies to gems and ordinary gear, but never to currency.
        if (!isCurrency)
            AppendThreshold(sb, "Quality", rule.QualityMode, rule.QualityValue);

        if (rule.StackMin > 0) sb.Append("    StackSize >= ").Append(rule.StackMin).Append('\n');
        if (rule.StackMax > 0) sb.Append("    StackSize <= ").Append(rule.StackMax).Append('\n');

        // Cluster jewels: enchant type (small passive node name) + number of added passives.
        if (!string.IsNullOrWhiteSpace(rule.EnchantNode))
            sb.Append("    EnchantmentPassiveNode \"").Append(Escape(rule.EnchantNode!)).Append("\"\n");
        AppendThreshold(sb, "EnchantmentPassiveNum", rule.PassiveNumMode, rule.PassiveNumValue);

        // A Hide block needs no styling — the item is suppressed, so skip colours/icon/sound.
        if (rule.Action == BlockAction.Hide)
            return sb.ToString();

        sb.Append("    SetTextColor ").Append(rule.TextColor.ToFilterArgs()).Append('\n');
        sb.Append("    SetBorderColor ").Append(rule.BorderColor.ToFilterArgs()).Append('\n');
        sb.Append("    SetBackgroundColor ").Append(rule.BackgroundColor.ToFilterArgs()).Append('\n');
        sb.Append("    SetFontSize ").Append(rule.FontSize).Append('\n');

        if (rule.IconShape != IconShape.None)
        {
            int size = Math.Clamp(rule.IconSize, 0, 2);
            sb.Append("    MinimapIcon ").Append(size).Append(' ')
              .Append(rule.IconColor).Append(' ')
              .Append(rule.IconShape).Append('\n');
        }

        if (rule.AlertSound is >= 1 and <= 16)
        {
            int vol = Math.Clamp(rule.AlertVolume, 0, 300);
            sb.Append("    PlayAlertSound ").Append(rule.AlertSound).Append(' ').Append(vol).Append('\n');
        }

        return sb.ToString();
    }

    /// <summary>Emits "<keyword> >= N" / "<keyword> = N" for a Any/&gt;=/Exact threshold; nothing for Any.</summary>
    private static void AppendThreshold(StringBuilder sb, string keyword, IlvlMatchMode mode, int value)
    {
        switch (mode)
        {
            case IlvlMatchMode.GreaterOrEqual:
                sb.Append("    ").Append(keyword).Append(" >= ").Append(value).Append('\n');
                break;
            case IlvlMatchMode.Exact:
                sb.Append("    ").Append(keyword).Append(" = ").Append(value).Append('\n');
                break;
            case IlvlMatchMode.Any:
            default:
                break;
        }
    }

    // PoE filter strings don't support escaping per se; quotes inside names don't occur in practice.
    // We strip stray quotes defensively.
    private static string Escape(string value) => value.Replace("\"", string.Empty);
}
