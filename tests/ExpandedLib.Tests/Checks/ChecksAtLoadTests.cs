using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Checks;
using ExpandedLib.Testing;
using NSubstitute;
using Vintagestory.API.Common;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary><see cref="ExpandedLibModSystem.AssetsFinalize"/>'s run of the content checks, which a
/// client never makes: it receives no recipes, a server asset category.</summary>
[Collection("WorldState")]
public sealed class ChecksAtLoadTests : System.IDisposable {
  public ChecksAtLoadTests() => ExlibChecks.ClearDeclarations();

  public void Dispose() => ExlibChecks.ClearDeclarations();

  // Fails when AssetsFinalize runs the content checks on a client.
  [Fact]
  public void A_client_load_runs_no_check_and_logs_no_unused_exemption() {
    using var world = new TestWorld();
    ((ICoreAPI)world.ClientApi).ModLoader.Returns(world.Mods);
    world.ClientApi.IsSinglePlayer.Returns(true);
    var assets = Substitute.For<IAssetManager>();
    assets
      .GetMany(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>())
      .Returns(_ => new List<IAsset>());
    ((ICoreAPI)world.ClientApi).Assets.Returns(assets);
    ExlibChecks.Exempt(
      "exlib",
      "GridRecipeCollision",
      "exlib:recipes/grid/planted.json#0",
      "planted recipe the client never receives"
    );
    Assert.Contains(
      ExlibChecks.All(world.ClientApi),
      r =>
        r.Check == "Exempt" && r.Errors.Any(e => e.Contains("planted.json#0"))
    );

    new ExpandedLibModSystem().AssetsFinalize(world.ClientApi);

    Assert.DoesNotContain(
      world.Log.Entries,
      e => e.Message.Contains("[exlib]")
    );
  }
}
