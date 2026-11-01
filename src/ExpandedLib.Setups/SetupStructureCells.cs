using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Structures;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Scenarios;

/// <summary>The cells a multiblock's authored layout takes in the world.</summary>
internal static class SetupStructureCells {
  /// <summary>Every cell of the layout turned to <paramref name="angle"/>: its world position and
  /// the, possibly wildcard, code it wants. Oriented parts turn with the structure; other codes keep
  /// their authored form (<see cref="MultiblockFacings"/>).</summary>
  /// <param name="anchor">The placed block entity the layout is offset from.</param>
  /// <param name="attributes">The block's attributes, holding <c>multiblockStructure</c> and
  /// <c>blockNumbers</c>.</param>
  /// <param name="angle">The angle the layout is turned to, in degrees: north 0, west 90, south
  /// 180, east 270.</param>
  public static IReadOnlyList<(BlockPos Pos, string Wanted)> Of(
    BlockEntityMultiblockStructure anchor,
    JObject attributes,
    int angle
  ) {
    var layout = (JObject)attributes["multiblockStructure"]!;
    // Authored glyph per block number, read straight off the JSON to avoid AssetLocation re-domaining.
    var codeByNumber = ((JObject)layout["blockNumbers"]!)
      .Properties()
      .ToDictionary(p => (int)p.Value!, p => p.Name);

    MultiblockStructure structure = new JsonObject(
      layout
    ).AsObject<MultiblockStructure>()!;
    structure.InitForUse(angle);

    MultiblockFacings facings = MultiblockFacings.FromAttributes(
      new JsonObject(attributes)
    );

    var cells = new List<(BlockPos, string)>();
    foreach (BlockOffsetAndNumber offset in structure.TransformedOffsets!)
      cells.Add(
        (
          anchor.Pos.AddCopy(offset.X, offset.Y, offset.Z),
          Demand(facings, codeByNumber[offset.W], angle)
        )
      );
    return cells;
  }

  private static string Demand(
    MultiblockFacings facings,
    string authored,
    int angle
  ) {
    if (facings.IsEmpty)
      return authored;

    var code = new AssetLocation(authored);
    AssetLocation rotated = facings.Rotate(code, angle);
    return rotated.Equals(code) ? authored : rotated.ToString();
  }
}
