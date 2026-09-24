using System;
using System.Collections.Generic;
using ExpandedLib.Catalogues;
using ExpandedLib.Industry;
using ExpandedLib.Industry.Metals;
using ExpandedLib.Testing;
using NSubstitute;
using Vintagestory.API.Common;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>A singleplayer client's <c>AssetsFinalize</c> reads the catalogues its own server loaded
/// in the same process: it neither empties them while that server ticks nor refills them from its
/// own assets.</summary>
[Collection("WorldState")]
public class SingleplayerCatalogueTests : IDisposable {
  public void Dispose() {
    MetalRegistry.ResetForWorld();
    ExLiquids.ResetForWorld();
    MaterialRoleRegistry.ResetForWorld();
  }

  // Fails when IndustryModule.AssetsFinalize loads the metal catalogue on a singleplayer client.
  [Fact]
  public void A_singleplayer_client_finalize_leaves_the_servers_metals_whole() {
    var world = SingleplayerClient(
      () => !MetalRegistry.TryGet("sponly:ingot-sponly", out _),
      out Func<bool> missingDuringRead
    );
    MetalRegistry.Register(
      new MetalDef { Code = "sponly", MoltenItem = "sponly:ingot-sponly" }
    );

    new IndustryModule().AssetsFinalize(world.ClientApi);

    Assert.False(missingDuringRead());
    Assert.True(MetalRegistry.TryGet("sponly:ingot-sponly", out _));
  }

  // Fails when ExpandedLibModSystem.AssetsFinalize loads its catalogues on a singleplayer client.
  [Fact]
  public void A_singleplayer_client_finalize_leaves_the_servers_liquids_and_roles_whole() {
    var world = SingleplayerClient(
      () =>
        !ExLiquids.TryGet("sponly-brine", out _)
        || !MaterialRoleRegistry.IsRole(
          "flux",
          new AssetLocation("sponly:flux")
        ),
      out Func<bool> missingDuringRead
    );
    ExLiquids.Register(new LiquidDef { Code = "sponly-brine" });
    MaterialRoleRegistry.Register(
      new MaterialRoleDef { Role = "flux", Code = "sponly:flux" }
    );

    new ExpandedLibModSystem().AssetsFinalize(world.ClientApi);

    Assert.False(missingDuringRead());
    Assert.True(ExLiquids.TryGet("sponly-brine", out _));
    Assert.True(
      MaterialRoleRegistry.IsRole("flux", new AssetLocation("sponly:flux"))
    );
  }

  // A singleplayer client whose assets hold nothing of the server-only entries; every asset read
  // records whether a server tick at that instant would find one missing.
  private static TestWorld SingleplayerClient(
    Func<bool> missing,
    out Func<bool> missingDuringRead
  ) {
    var world = new TestWorld();
    ((ICoreAPI)world.ClientApi).ModLoader.Returns(world.Mods);
    world.ClientApi.IsSinglePlayer.Returns(true);
    bool seen = false;
    var assets = Substitute.For<IAssetManager>();
    assets
      .GetMany(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>())
      .Returns(_ => {
        seen |= missing();
        return new List<IAsset>();
      });
    ((ICoreAPI)world.ClientApi).Assets.Returns(assets);
    missingDuringRead = () => seen;
    return world;
  }
}
