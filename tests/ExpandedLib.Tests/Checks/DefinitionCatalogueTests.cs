using System.Collections.Generic;
using System.Reflection;
using ExpandedLib.Definitions;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary><see cref="DefinitionCatalogue.Resolves"/> over a planted block and item.</summary>
public class DefinitionCatalogueTests {
  public DefinitionCatalogueTests() => TestModDomain.Register();

  private const string Domain = "plantedcatalogue";
  private static readonly Assembly Here =
    typeof(DefinitionCatalogueTests).Assembly;

  /// <summary>Declares its block only under <see cref="Domain"/>, so other scans of this assembly
  /// never see it.</summary>
  private sealed class Gear : IExBlockDefProvider {
    public static IEnumerable<ExBlockDef> Definitions(string domain) =>
      domain == Domain
        ?
        [
          ExBlockDef
            .Create(domain, "gear")
            .VariantGroup("size", "small", "large"),
        ]
        : [];
  }

  /// <summary>Declares its item only under <see cref="Domain"/>.</summary>
  private sealed class Cog : IExItemDefProvider {
    public static IEnumerable<ExItemDef> Definitions(string domain) =>
      domain == Domain ? [ExItemDef.Create(domain, "cog")] : [];
  }

  private static bool Resolves(EnumItemClass type, string code) =>
    DefinitionCatalogue.Resolves(
      new JsonItemStack { Type = type, Code = new AssetLocation(code) },
      Domain,
      Here
    );

  [Fact]
  [PlantedDefect(
    typeof(DefinitionCatalogue),
    nameof(DefinitionCatalogue.Resolves)
  )]
  public void A_code_its_domain_never_registers_does_not_resolve() {
    Assert.False(Resolves(EnumItemClass.Block, "plantedcatalogue:gear-huge"));
    Assert.False(Resolves(EnumItemClass.Item, "plantedcatalogue:sprocket"));
  }

  [Fact]
  public void A_registered_variant_a_registered_item_and_a_foreign_code_resolve() {
    Assert.True(Resolves(EnumItemClass.Block, "plantedcatalogue:gear-small"));
    Assert.True(Resolves(EnumItemClass.Item, "plantedcatalogue:cog"));
    Assert.True(Resolves(EnumItemClass.Item, "game:ingot-iron"));
  }
}
