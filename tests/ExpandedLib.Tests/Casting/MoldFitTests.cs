using System;
using System.Collections.Generic;
using ExpandedLib.Industry.Casting;
using ExpandedLib.Registries;
using ExpandedLib.Testing;
using Newtonsoft.Json.Linq;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Util;
using Vintagestory.GameContent;
using Xunit;

namespace ExpandedLib.Tests.Casting;

/// <summary>Mould fits: the oversize list from JSON and C#, the <c>moldFit</c> block attribute, the
/// class test. Shares the process-wide <see cref="MoldFitRegistry.Shared"/>.</summary>
[Collection("WorldState")]
public class MoldFitTests : IDisposable {
  public void Dispose() => MoldFitRegistry.ResetForWorld();

  private const string AnvilList = """{ "schema": 1, "oversize": ["anvil"] }""";

  private static MoldFitRegistry Registry(params string[] files) {
    var registry = new MoldFitRegistry();
    var sources = new List<(string, string)>();
    foreach (string json in files)
      sources.Add(($"test:config/moldfits/f{sources.Count}.json", json));
    MoldFitLoader.Load(sources, registry);
    return registry;
  }

  private static BlockToolMold Mold(string tooltype, string? moldFit = null) =>
    new() {
      Code = new AssetLocation($"test:toolmold-{tooltype}"),
      Variant = new RelaxedReadOnlyDictionary<string, string>(
        new Dictionary<string, string> { ["tooltype"] = tooltype }
      ),
      Attributes = new JsonObject(
        moldFit is null
          ? new JObject()
          : new JObject { [MoldFits.AttributeKey] = moldFit }
      ),
    };

  // Fails when the oversize list is ignored.
  [Fact]
  public void A_tooltype_in_a_json_list_makes_its_mould_oversize() {
    MoldFitRegistry registry = Registry(AnvilList);

    Assert.True(MoldFits.IsOversize(Mold("anvil"), registry));
    Assert.False(MoldFits.FitsPedestal(Mold("anvil"), registry));
    Assert.False(MoldFits.IsOversize(Mold("ingot"), registry));
    Assert.True(MoldFits.FitsPedestal(Mold("ingot"), registry));
  }

  // Fails when the moldFit attribute is read after the list instead of before it.
  [Fact]
  public void A_pedestal_attribute_forces_a_listed_tooltype_small() {
    MoldFitRegistry registry = Registry(AnvilList);
    BlockToolMold mold = Mold("anvil", "pedestal");

    Assert.False(MoldFits.IsOversize(mold, registry));
    Assert.True(MoldFits.FitsPedestal(mold, registry));
  }

  // Fails when the oversize attribute is ignored for an unlisted tooltype.
  [Fact]
  public void An_oversize_attribute_makes_an_unlisted_mould_oversize() {
    MoldFitRegistry registry = Registry(AnvilList);

    Assert.True(MoldFits.IsOversize(Mold("ingot", "oversize"), registry));
  }

  // Fails when the BlockToolMold class test is dropped.
  [Fact]
  public void A_block_that_is_not_a_tool_mould_fits_neither() {
    MoldFitRegistry registry = Registry(AnvilList);
    var plain = new Block {
      Code = new AssetLocation("test:plain"),
      Variant = new RelaxedReadOnlyDictionary<string, string>(
        new Dictionary<string, string> { ["tooltype"] = "anvil" }
      ),
    };

    Assert.False(MoldFits.IsOversize(plain, registry));
    Assert.False(MoldFits.FitsPedestal(plain, registry));
    Assert.False(MoldFits.FitsPedestal(null, registry));
  }

  // Fails when a second file's list replaces the first's instead of joining it.
  [Fact]
  public void Lists_from_two_files_union() {
    MoldFitRegistry registry = Registry(
      AnvilList,
      """{ "schema": 1, "oversize": ["flywheel"] }"""
    );

    Assert.True(MoldFits.IsOversize(Mold("anvil"), registry));
    Assert.True(MoldFits.IsOversize(Mold("flywheel"), registry));
  }

  [Fact]
  public void A_file_with_an_unknown_key_is_reported_and_skipped() {
    var registry = new MoldFitRegistry();

    List<string> errors = MoldFitLoader.Load(
      [("test:config/moldfits/bad.json", """{ "oversized": ["anvil"] }""")],
      registry
    );

    Assert.Contains("oversized", Assert.Single(errors));
    Assert.Equal(0, registry.Count);
  }

  [Fact]
  public void A_tooltype_with_a_domain_is_refused() {
    var registry = new MoldFitRegistry();

    List<string> errors = MoldFitLoader.Load(
      [("test:config/moldfits/x.json", """{ "oversize": ["game:anvil"] }""")],
      registry
    );

    Assert.Contains("bare lowercase", Assert.Single(errors));
    Assert.Equal(0, registry.Count);
  }

  // Fails when a C# contribution is not re-applied by the load that follows a clear.
  [Fact]
  public void A_C_sharp_contributor_lists_a_tooltype_on_every_load() {
    MoldFitRegistry.Contributors.Register(_ =>
      MoldFitRegistry.Shared.Contribute(new MoldFitSet(1, ["csanvil"]))
    );
    var world = new TestWorld();
    var assets = Substitute.For<IAssetManager>();
    assets
      .GetMany(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>())
      .Returns(new List<IAsset>());
    ((ICoreAPI)world.Api).Assets.Returns(assets);

    MoldFitLoader.Load(world.Api);
    MoldFitRegistry.Shared.Clear();
    MoldFitLoader.Load(world.Api);

    Assert.True(MoldFits.IsOversize(Mold("csanvil")));
  }

  // Fails when IndustryModule.StartPre stops calling MoldFitRegistry.ResetForWorld.
  [Fact]
  public void A_server_load_start_empties_the_fit_registry_and_its_contributors() {
    MoldFitRegistry.Shared.Contribute(new MoldFitSet(1, ["leftover"]));
    MoldFitRegistry.Contributors.Register(_ => { });
    var world = new TestWorld();
    var system = new ExModuleModSystem();
    ReflectionHelpers.SetProperty(
      system,
      nameof(ModSystem.Mod),
      world.Mods.GetMod("exlib")!
    );

    system.StartPre(world.Api);

    Assert.Equal(0, MoldFitRegistry.Shared.Count);
    Assert.Equal(0, MoldFitRegistry.Contributors.Count);
  }
}
