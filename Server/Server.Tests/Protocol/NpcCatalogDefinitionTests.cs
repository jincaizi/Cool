using Cool.SyncCore;
using Xunit;

namespace Cool.Protocol.Tests;

public class NpcCatalogDefinitionTests
{
    [Fact]
    public void Default_Catalog_Has_All_Contracts_And_Stable_Nonmutating_Hash()
    {
        var catalog = NpcCatalogDefaults.Create();
        Assert.Empty(NpcCatalogValidator.Validate(catalog));
        Assert.Equal(4, catalog.Templates.Count); Assert.Equal(6, catalog.Skills.Count);
        var hash = NpcCatalogHasher.Compute(catalog);
        catalog.Templates.Reverse(); catalog.Skills.Reverse(); catalog.Presentations.Reverse();
        catalog.ContentHash = "excluded";
        Assert.Equal(hash, NpcCatalogHasher.Compute(catalog));
        Assert.Equal(4, catalog.Templates[0].TemplateId);
        catalog.Templates[0].Collision.BodyRadius += 0.1f;
        Assert.NotEqual(hash, NpcCatalogHasher.Compute(catalog));
    }

    [Theory]
    [InlineData(0u, 1u)] [InlineData(1u, 0u)] [InlineData(2147483648u, 1u)]
    public void Invalid_Phase_Durations_Are_Rejected(uint windup, uint recovery)
    {
        var catalog = NpcCatalogDefaults.Create();
        catalog.Skills[0].WindupTicks = windup; catalog.Skills[0].RecoveryTicks = recovery;
        Assert.NotEmpty(NpcCatalogValidator.Validate(catalog));
    }

    [Fact]
    public void Active_Offsets_And_Total_Window_Must_Be_Valid()
    {
        var catalog = NpcCatalogDefaults.Create();
        catalog.Skills[0].ActiveOffsets.Clear(); Assert.NotEmpty(NpcCatalogValidator.Validate(catalog));
        catalog.Skills[0].ActiveOffsets.Add(1); Assert.NotEmpty(NpcCatalogValidator.Validate(catalog));
        catalog.Skills[0].ActiveOffsets[0] = 0; catalog.Skills[0].ActiveOffsets.Add(0);
        Assert.NotEmpty(NpcCatalogValidator.Validate(catalog));
        catalog.Skills[0].ActiveOffsets[1] = 2147483640u;
        Assert.NotEmpty(NpcCatalogValidator.Validate(catalog));
    }

    [Fact]
    public void Hash_Is_Culture_Independent_And_Binding_Order_Independent()
    {
        var catalog = NpcCatalogDefaults.Create();
        var hash = NpcCatalogHasher.Compute(catalog);
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("fr-FR");
            catalog.Templates[2].Skills.Reverse();
            Assert.Equal(hash, NpcCatalogHasher.Compute(catalog));
            catalog.Presentations[0].ActiveEnd += 0.01f;
            Assert.NotEqual(hash, NpcCatalogHasher.Compute(catalog));
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void Validation_Rejects_Invalid_Collision_References_And_Distance_Bands()
    {
        var catalog = NpcCatalogDefaults.Create(); catalog.Templates[0].Collision.SizeX = float.NaN;
        Assert.NotEmpty(NpcCatalogValidator.Validate(catalog));
        Assert.Throws<System.ArgumentException>(() => NpcCatalogHasher.Compute(catalog));
        catalog = NpcCatalogDefaults.Create(); catalog.Skills[0].PresentationId = -1;
        Assert.NotEmpty(NpcCatalogValidator.Validate(catalog));
        catalog = NpcCatalogDefaults.Create(); catalog.Templates[0].Skills[0].PreferredMinRange = 50;
        Assert.NotEmpty(NpcCatalogValidator.Validate(catalog));
    }

    [Fact]
    public void Tick_Comparisons_Wrap_And_Zero_Is_Valid()
    {
        Assert.True(NpcTickMath.IsAtOrAfter(0, uint.MaxValue));
        Assert.True(NpcTickMath.IsBefore(uint.MaxValue, 0));
        Assert.Equal(2u, NpcTickMath.Elapsed(1, uint.MaxValue));
        Assert.True(NpcTickMath.IsInWindow(0, uint.MaxValue, 2));
        Assert.False(NpcTickMath.IsInWindow(2, uint.MaxValue, 2));
    }
}
