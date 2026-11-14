using System;
using System.IO;
using Cool.SyncCore;
using KcpServer.Config;
using Xunit;

namespace Cool.SyncCore.Tests;

public class ElementalCatalogValidatorTests
{
    [Fact]
    public void Default_Catalog_Is_Valid()
    {
        var catalog = ElementalCatalogDefaults.Create();
        Assert.Empty(ElementalCatalogValidator.Validate(catalog));
        Assert.Contains(catalog.Weapons, w => w.TemplateId == ElementalCatalogDefaults.DefaultSwordTemplateId);
        Assert.Contains(catalog.Weapons, w => w.ElementId != 0);
    }

    [Fact]
    public void Rejects_Zero_BaseAttack()
    {
        var catalog = ElementalCatalogDefaults.Create();
        catalog.Weapons[0].BaseAttack = 0f;
        Assert.Contains("weapon stats out of range", ElementalCatalogValidator.Validate(catalog));
    }

    [Fact]
    public void Rejects_Duplicate_Status_Group_And_Bad_Duration()
    {
        var catalog = ElementalCatalogDefaults.Create();
        catalog.Statuses.Add(new StatusEffectDefinition
        {
            EffectId = catalog.Statuses[0].EffectId,
            EffectKind = 1,
            StackGroupId = 1,
            DurationSeconds = 0f
        });
        var errors = ElementalCatalogValidator.Validate(catalog);
        Assert.Contains("status duplicate/invalid id", errors);
        Assert.Contains("status duration out of range", errors);
    }

    [Fact]
    public void Shipped_Json_Matches_Defaults_And_Fingerprint()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Config", "elemental_catalog.json");
        Assert.True(File.Exists(path), path);

        var loaded = ConfigLoader.LoadElementalCatalog(path);
        Assert.Empty(ElementalCatalogValidator.Validate(loaded));
        Assert.Equal(ElementalCatalogDefaults.Create().Weapons.Count, loaded.Weapons.Count);
        Assert.Equal(ElementalCatalogHasher.Compute(ElementalCatalogDefaults.Create()),
            ElementalCatalogHasher.Compute(loaded));
    }

    [Fact]
    public void CombatRules_10002_Matches_Default_SwordQi_Geometry()
    {
        Assert.True(CombatRules.TryGet(10002, out var entry));
        var spec = entry.Projectile;
        Assert.NotNull(spec);
        var g = ElementalCatalogDefaults.Create().SwordQi;
        Assert.Equal(g.Length, spec!.Length);
        Assert.Equal(g.HalfWidth, spec.HalfWidth);
        Assert.Equal(g.Stages, spec.Stages);
        Assert.Equal(g.Speed, spec.Speed);
        Assert.Equal(g.Length / g.Speed, spec.TotalFlightSeconds, 3);
    }
}
