#if GAME_GE_1_22
using System.Linq;
using System.Reflection;
using ExpandedLib.Catalogues;
using ExpandedLib.Machines;
using ExpandedLib.Testing;
using HarmonyLib;
using Vintagestory.API.Common;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>The patch that applies the craft-sequence gate inside <see cref="GridRecipe.Matches"/>, so a
/// handler subscribed after the gate cannot answer true over it.</summary>
[Collection(ExHarmonyCollection.Name)]
public sealed class CraftSequenceGatePatchTests
{
  private const string Frame = """
    {
      "schema": 1,
      "machine": "workbench",
      "sequences": [
        { "recipe": "iiex:frame", "steps": [ { "tool": "game:hammer-*", "seconds": 2 } ] }
      ]
    }
    """;

  private static GridRecipe Recipe(string name) =>
    new() { Name = new AssetLocation(name) };

  private static bool Refused(GridRecipe recipe)
  {
    bool result = true;
    bool runs = CraftSequenceGatePatch.RefuseSequenced(recipe, ref result);
    Assert.Equal(runs, result);
    return !runs;
  }

  // Fails when the class loses its [HarmonyPatch] attributes: nothing is then patched in GridRecipe.Matches.
  [Fact]
  public void The_gate_is_patched_into_the_game_matcher()
  {
    using var fixture = new HarmonyFixture(
      "exlibtest.gatepatch",
      typeof(CraftSequenceGatePatch).Assembly
    );

    MethodInfo matches = typeof(GridRecipe).GetMethod(
      nameof(GridRecipe.Matches)
    )!;
    Patches info = Harmony.GetPatchInfo(matches);

    Assert.Contains(
      info.Prefixes,
      p =>
        p.owner == "exlibtest.gatepatch"
        && p.PatchMethod.Name == nameof(CraftSequenceGatePatch.RefuseSequenced)
    );
  }

  // Fails when the prefix lets a sequenced recipe through unmarked, or refuses a plain one or the machine's own.
  [Fact]
  public void The_prefix_refuses_a_sequenced_recipe_off_its_machine_only()
  {
    CraftSequenceRegistry.Shared.Clear();
    try
    {
      CraftSequenceLoader.Load([("iiex:config/craftsequences/w.json", Frame)]);

      Assert.True(Refused(Recipe("iiex:frame")));
      Assert.False(Refused(Recipe("iiex:plain")));
      using (CraftSequenceGate.At("workbench"))
        Assert.False(Refused(Recipe("iiex:frame")));
      using (CraftSequenceGate.At("lathe"))
        Assert.True(Refused(Recipe("iiex:frame")));
    }
    finally
    {
      CraftSequenceRegistry.Shared.Clear();
    }
  }
}
#endif
