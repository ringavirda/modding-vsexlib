using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace ExpandedLib.Blocks;

/// <summary>One row of a block's <c>attributes.controls</c> table.</summary>
/// <param name="Id">The control id, non-empty, unique in the block.</param>
/// <param name="Kind">What the control is (door, instrument, valve, and so on), as the plan names it.</param>
/// <param name="AlsoIds">Ids a click on this control's box may also act as, in the order they are tried.</param>
/// <param name="Stage">The construction stage that builds the control's part; 0 when it stands from the
/// start. The control is hidden while the stage is above the completed one.</param>
public sealed record BlockControl(
  string Id,
  string Kind,
  string[] AlsoIds,
  int Stage = 0
);

/// <summary>Reads a block's <c>attributes.controls</c> table.</summary>
public static class BlockControls {
  private static readonly ConditionalWeakTable<
    Block,
    IReadOnlyList<BlockControl>
  > Tables = new();

  /// <summary>The <c>controls</c> rows of <paramref name="block"/>'s attributes, parsed once per block;
  /// the list is shared and is not changed by callers. Empty when the block has no table.</summary>
  public static IReadOnlyList<BlockControl> Of(Block block) =>
    Tables.GetValue(block, Parse);

  /// <summary>The row named <paramref name="id"/> in <paramref name="block"/>'s table, or null.</summary>
  public static BlockControl? Find(Block block, string id) =>
    Of(block).FirstOrDefault(c => c.Id == id);

  private static IReadOnlyList<BlockControl> Parse(Block block) {
    JsonObject? node = block.Attributes?["controls"];
    if (node == null || !node.Exists)
      return [];
    var rows = new List<BlockControl>();
    foreach (JsonObject row in node.AsArray() ?? [])
      rows.Add(
        new BlockControl(
          row["id"].AsString() ?? "",
          row["kind"].AsString() ?? "",
          row["alsoIds"].AsArray<string>()?.OfType<string>().ToArray() ?? [],
          row["stage"].AsInt(0)
        )
      );
    return rows;
  }
}
