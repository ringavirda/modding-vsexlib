using System;
using System.Collections.Generic;
using ExpandedLib.Industry.Casting;
using ExpandedLib.Registries;
using ExpandedLib.Testing;
using NSubstitute;
using Vintagestory.API.Common;
using Xunit;

namespace ExpandedLib.Tests.Casting;

/// <summary>Pattern recognition and load-time validation, and the shared registry's reset and
/// C# route. Shares the process-wide <see cref="CastingStationRegistry.Shared"/>.</summary>
[Collection("WorldState")]
public class CastingPatternTests : IDisposable {
  public void Dispose() => CastingStationRegistry.ResetForWorld();

  // Fails when IsPattern reads the first code part instead of the attribute.
  [Fact]
  public void A_collectible_of_another_domain_with_a_mold_attribute_is_a_pattern() {
    Assert.True(
      CastingPatterns.IsPattern(
        CastingTestData.Collectible(
          "othermod:castpiece-x",
          CastingTestData.Mold("testflask")
        )
      )
    );
    Assert.False(
      CastingPatterns.IsPattern(
        CastingTestData.Collectible("iiex:pattern-plain", null)
      )
    );
  }

  // Fails when Validate runs with no station kind registered.
  [Fact]
  public void Validation_with_no_station_kind_registered_reports_nothing() {
    var broken = CastingTestData.Collectible(
      "othermod:castpiece-x",
      CastingTestData.Mold("testflask")
    );

    Assert.Empty(
      CastingPatterns.Validate([broken], new CastingStationRegistry())
    );
  }

  [Fact]
  public void Validation_names_each_pattern_whose_station_is_not_registered() {
    CastingStationRegistry registry = CastingTestData.Registry(
      CastingTestData.TestFlaskFile
    );
    var good = CastingTestData.Collectible(
      "a:good",
      CastingTestData.Mold("testflask")
    );
    var bad = CastingTestData.Collectible(
      "a:bad",
      CastingTestData.Mold("nowhere")
    );

    List<string> errors = CastingPatterns.Validate([good, bad, null], registry);

    string error = Assert.Single(errors);
    Assert.StartsWith("a:bad: ", error);
    Assert.Contains("'nowhere'", error);
  }

  // Fails when a C# contribution is not re-applied by the load that follows a clear.
  [Fact]
  public void A_C_sharp_contributor_adds_a_kind_on_every_load() {
    CastingStationRegistry.Contributors.Register(_ =>
      CastingStationRegistry.Shared.Contribute(
        new CastingStationSet(
          1,
          [
            new CastingStationKind(
              "csflask",
              [MouldKind.Open],
              "c:base",
              "c:half",
              "c:sand"
            ),
          ]
        )
      )
    );
    var world = new TestWorld();
    var assets = Substitute.For<IAssetManager>();
    assets
      .GetMany(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>())
      .Returns(new List<IAsset>());
    ((ICoreAPI)world.Api).Assets.Returns(assets);

    CastingStationLoader.Load(world.Api);
    Assert.True(CastingStationRegistry.Shared.TryGet("csflask", out _));

    CastingStationRegistry.Shared.Clear();
    CastingStationLoader.Load(world.Api);
    Assert.True(CastingStationRegistry.Shared.TryGet("csflask", out _));
  }

  // Fails when IndustryModule.StartPre stops calling CastingStationRegistry.ResetForWorld.
  [Fact]
  public void A_server_load_start_empties_the_station_registry_and_its_contributors() {
    CastingStationRegistry.Shared.Contribute(
      new CastingStationSet(
        1,
        [
          new CastingStationKind(
            "leftover",
            [MouldKind.Open],
            "c:base",
            "c:half",
            "c:sand"
          ),
        ]
      )
    );
    CastingStationRegistry.Contributors.Register(_ => { });
    var world = new TestWorld();
    var system = new ExModuleModSystem();
    ReflectionHelpers.SetProperty(
      system,
      nameof(ModSystem.Mod),
      world.Mods.GetMod("exlib")!
    );

    system.StartPre(world.Api);

    Assert.Equal(0, CastingStationRegistry.Shared.Count);
    Assert.Equal(0, CastingStationRegistry.Contributors.Count);
  }
}
