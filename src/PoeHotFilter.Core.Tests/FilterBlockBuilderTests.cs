using PoeHotFilter.Core.Filter;
using PoeHotFilter.Core.Models;
using Xunit;

namespace PoeHotFilter.Core.Tests;

public class FilterBlockBuilderTests
{
    [Fact]
    public void Cluster_jewel_rule_emits_enchant_node_and_passive_count()
    {
        var rule = new FilterRule
        {
            BaseType = "Medium Cluster Jewel",
            EnchantNode = "Aura Effect",
            PassiveNumMode = IlvlMatchMode.GreaterOrEqual,
            PassiveNumValue = 4
        };

        var block = FilterBlockBuilder.BuildBlock(rule);

        Assert.Contains("EnchantmentPassiveNode \"Aura Effect\"", block);
        Assert.Contains("EnchantmentPassiveNum >= 4", block);
    }

    [Fact]
    public void Rule_without_enchant_emits_no_cluster_conditions()
    {
        var rule = new FilterRule { BaseType = "Medium Cluster Jewel" };

        var block = FilterBlockBuilder.BuildBlock(rule);

        Assert.DoesNotContain("EnchantmentPassiveNode", block);
        Assert.DoesNotContain("EnchantmentPassiveNum", block);
    }

    [Fact]
    public void Exact_passive_count_emits_equals()
    {
        var rule = new FilterRule
        {
            BaseType = "Large Cluster Jewel",
            PassiveNumMode = IlvlMatchMode.Exact,
            PassiveNumValue = 8
        };

        var block = FilterBlockBuilder.BuildBlock(rule);

        Assert.Contains("EnchantmentPassiveNum = 8", block);
    }
}
