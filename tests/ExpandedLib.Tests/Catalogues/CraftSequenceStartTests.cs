#if GAME_GE_1_22
using ExpandedLib.Catalogues;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>The grid gate as <see cref="ExpandedLibModSystem"/> registers it. Runs alone: the system
/// patches the game process-wide.</summary>
[Collection(ExHarmonyCollection.Name)]
public class CraftSequenceStartTests
{
  // Fails when the mod system's Start does not register the CraftSequenceGate.
  [Fact]
  public void Starting_exlib_turns_a_sequenced_recipe_away_from_an_unmarked_grid()
  {
    CraftSequenceRegistry.Shared.Clear();
    using var world = new TestWorld();
    var exlib = new ExpandedLibModSystem();
    ReflectionHelpers.SetProperty(
      exlib,
      nameof(ModSystem.Mod),
      world.Mods.GetMod("exlib")!
    );
    try
    {
      CraftSequenceLoader.Load([
        (
          "iiex:config/craftsequences/w.json",
          """{ "machine": "workbench", "sequences": [ { "recipe": "iiex:shaft hand", "steps": [ { "tool": "game:chisel-*" } ] } ] }"""
        ),
      ]);
      var sequenced = new GridRecipe
      {
        Name = new AssetLocation("iiex:shaft hand"),
        Width = 1,
        Height = 1,
      };

      exlib.Start(world.Api);

      Assert.False(
        world.Api.Event.TriggerMatchesRecipe(null!, sequenced, [], 3)
      );
    }
    finally
    {
      exlib.Dispose();
      CraftSequenceRegistry.Shared.Clear();
    }
  }
}
#endif
