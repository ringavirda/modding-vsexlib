using System;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace ExpandedLib.Scenarios;

/// <summary>The stand-in for hand smithing that a setup's <c>call</c> step names: the anvil's voxels
/// are set to a smithing recipe's and <c>CheckIfFinished</c> pays the output, in place of the
/// hammer blows a player strikes.</summary>
internal static class SmithingStandIn {
  /// <summary>The <c>method</c> a <c>call</c> step on a <c>BlockEntityAnvil</c> names for the stand-in;
  /// its one argument is the output code of a recipe the work item matches.</summary>
  internal const string Method = "Smith";

  /// <summary>What the game does instead, as a setup prints it.</summary>
  internal const string Text =
    "the anvil's voxels are set to the recipe's and CheckIfFinished runs; the game's player strikes the work item voxel by voxel";

  internal static bool Takes(object target, string method) =>
    method == Method && target is BlockEntityAnvil;

  /// <summary>Finishes the work item on <paramref name="anvil"/> as the recipe that makes
  /// <paramref name="output"/>; the output spawns at the anvil.</summary>
  /// <param name="anvil">An anvil holding a work item, placed by an <c>interact</c> with a hot
  /// workable stack.</param>
  /// <param name="output">The full code of the recipe's output, such as <c>iiex:boltblank</c>.</param>
  /// <exception cref="InvalidOperationException">The anvil holds no work item, no recipe the work item
  /// matches makes <paramref name="output"/>, or the voxels do not finish the recipe.</exception>
  internal static void Run(BlockEntityAnvil anvil, string output) {
    ItemStack work =
      anvil.WorkItemStack
      ?? throw new InvalidOperationException("the anvil holds no work item.");
    SmithingRecipe recipe =
      work.Collectible.GetCollectibleInterface<IAnvilWorkable>()
        .GetMatchingRecipes(work)
        .FirstOrDefault(r =>
          r.Output.ResolvedItemstack?.Collectible.Code.ToString() == output
        )
      ?? throw new InvalidOperationException(
        $"no smithing recipe the work item matches makes {output}."
      );
    var voxels = new byte[16, 6, 16];
    int layers = Math.Min(
      Math.Min(6, recipe.QuantityLayers),
      recipe.Voxels.GetLength(1)
    );
    for (int x = 0; x < 16; x++)
      for (int y = 0; y < layers; y++)
        for (int z = 0; z < 16; z++)
          if (recipe.Voxels[x, y, z])
            voxels[x, y, z] = (byte)EnumVoxelMaterial.Metal;
    anvil.Voxels = voxels;
    anvil.rotation = 0;
    anvil.SelectedRecipeId = recipe.RecipeId;
    anvil.CheckIfFinished(null);
    if (anvil.WorkItemStack != null)
      throw new InvalidOperationException(
        $"the recipe for {output} did not finish on the anvil."
      );
  }
}
