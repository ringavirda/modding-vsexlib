#if GAME_GE_1_22
using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Definitions;
using ExpandedLib.Networks;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace ExpandedLib.Scenarios;

/// <summary>The choice of a run cell's block among the members of its family by the faces drawn
/// for the cell, shared by the setup harness and the live runner's layout walk.</summary>
internal static class SetupRunFamily
{
  /// <summary>The wildcards of the family of <paramref name="code"/>: the ones
  /// <paramref name="declared"/> lists, or its own code with its orientation or side group
  /// opened.</summary>
  /// <exception cref="InvalidOperationException">The code is not registered, or has both an
  /// orientation and a side group and declares no family.</exception>
  public static IReadOnlyList<string> Wildcards(
    IWorldAccessor world,
    IReadOnlyList<string>? declared,
    string code
  )
  {
    if (declared != null)
      return declared;
    Block block =
      world.GetBlock(new AssetLocation(code))
      ?? throw new InvalidOperationException(
        $"\"{code}\" is not registered by the load."
      );
    string[] groups =
    [
      .. new[] { "orientation", "side" }.Where(block.Variant.ContainsKey),
    ];
    if (groups.Length > 1)
      throw new InvalidOperationException(
        $"{code} has both an orientation and a side group, so the run needs \"family\"."
      );
    return
    [
      groups.Length == 0
        ? code
        : block.CodeWithVariant(groups[0], "*").ToString(),
    ];
  }

  /// <summary>Every loaded network connector matching one of <paramref name="wildcards"/>, with the
  /// letters of the faces it connects on.</summary>
  /// <exception cref="InvalidOperationException">No block matches, or the matches are of more than
  /// one network type.</exception>
  public static IReadOnlyList<(Block Block, string Faces)> Members(
    IWorldAccessor world,
    IReadOnlyList<string> wildcards
  )
  {
    (Block Block, string Faces)[] blocks =
    [
      .. world
        .Blocks.Where(b =>
          b.Id != 0
          && b is INetworkConnector
          && wildcards.Any(w => WildcardUtil.Match(w, b.Code.ToString()))
        )
        .Select(b => (b, FacesOf((INetworkConnector)b))),
    ];
    string[] types =
    [
      .. blocks
        .Select(b => ((INetworkConnector)b.Block).NetworkType)
        .Distinct(),
    ];
    if (types.Length != 1)
      throw new InvalidOperationException(
        types.Length == 0
          ? $"no loaded block takes the family {string.Join(", ", wildcards)}."
          : $"the family {string.Join(", ", wildcards)} holds blocks of more than one network type, {string.Join(" and ", types)}."
      );
    return blocks;
  }

  /// <summary>The letters of the faces <paramref name="block"/> connects on, in
  /// <see cref="BlockFacing.ALLFACES"/> order.</summary>
  public static string FacesOf(INetworkConnector block) =>
    Letters(BlockFacing.ALLFACES.Where(block.HasConnectorAt));

  /// <summary>The letters of <paramref name="faces"/> in <see cref="BlockFacing.ALLFACES"/>
  /// order.</summary>
  public static string Letters(IEnumerable<BlockFacing> faces) =>
    string.Concat(
      BlockFacing.ALLFACES.Where(faces.Contains).Select(f => f.Code[0])
    );

  /// <summary>The faces a run cell is drawn with: its joins and opens, and each face toward a drawn
  /// block entry of the same network type that has a connector toward the cell.</summary>
  /// <param name="layout">The drawing whose cells are read.</param>
  /// <param name="cell">The run cell whose faces are wanted.</param>
  /// <param name="type">The network type a neighbour must share to add a face.</param>
  /// <param name="resolve">The block a legend code names; what it throws for an unknown code passes
  /// through.</param>
  /// <param name="accessor">Reads the block already placed at a neighbour's position.</param>
  /// <param name="absolute">Maps a layout cell to the position the accessor reads it at.</param>
  /// <returns>The faces: the cell's joins and opens, then each toward a drawn block that connects
  /// back.</returns>
  /// <exception cref="InvalidCastException">A placed connector toward the cell is not an
  /// <see cref="INetworkMember"/>.</exception>
  public static HashSet<BlockFacing> DrawnFaces(
    SetupLayout layout,
    LayoutRunCell cell,
    string type,
    System.Func<string, Block> resolve,
    IBlockAccessor accessor,
    System.Func<BlockPos, BlockPos> absolute
  )
  {
    var drawn = layout.Cells.ToDictionary(c => (c.X, c.Y, c.Z));
    var faces = new HashSet<BlockFacing>(cell.Joins.Concat(cell.Opens));
    foreach (BlockFacing face in BlockFacing.ALLFACES)
    {
      if (faces.Contains(face))
        continue;
      if (
        !drawn.TryGetValue(
          (
            cell.Cell.X + face.Normali.X,
            cell.Cell.Y + face.Normali.Y,
            cell.Cell.Z + face.Normali.Z
          ),
          out LayoutCell next
        )
      )
        continue;
      LayoutEntry neighbour = layout.EntryOf(next);
      if (neighbour.Kind != LayoutEntryKind.Block)
        continue;
      BlockPos at = absolute(new BlockPos(next.X, next.Y, next.Z));
      if (
        resolve(neighbour.Code!) is INetworkConnector connector
        && connector.NetworkType == type
        && (
          ReferenceEquals(accessor.GetBlock(at), connector)
            ? ((INetworkMember)connector).HasConnectorAt(
              accessor,
              at,
              face.Opposite
            )
            : connector.HasConnectorAt(face.Opposite)
        )
      )
        faces.Add(face);
    }
    return faces;
  }

  /// <summary>The one member of <paramref name="family"/> whose faces are
  /// <paramref name="faces"/>.</summary>
  /// <param name="cell">The cell as its failure names it.</param>
  /// <exception cref="InvalidOperationException">No member has the faces, or more than one
  /// does.</exception>
  public static Block Choose(
    IReadOnlyList<(Block Block, string Faces)> family,
    IEnumerable<BlockFacing> faces,
    string cell
  )
  {
    string wanted = Letters(faces);
    Block[] fits =
    [
      .. family.Where(f => f.Faces == wanted).Select(f => f.Block),
    ];
    if (fits.Length != 1)
      throw new InvalidOperationException(
        fits.Length == 0
          ? $"cell {cell} needs a block with the faces {wanted}, and none of {string.Join(", ", family.Select(f => f.Block.Code))} has them."
          : $"cell {cell} needs a block with the faces {wanted}, and {string.Join(" and ", fits.Select(b => b.Code.ToString()))} both have them."
      );
    return fits[0];
  }
}
#endif
