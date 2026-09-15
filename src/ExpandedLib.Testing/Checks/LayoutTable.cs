using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Definitions;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Testing;

/// <summary>
/// Reads a code-first multiblock layout's emitted table back into a per-cell block code. Every
/// entry point reads whatever <see cref="ExBlockDef.MultiblockLayout"/> emitted.
/// </summary>
public static class LayoutTable {
  /// <summary>The wanted block code at each structure-local cell, read from the generated
  /// <c>multiblockStructure</c> attribute.</summary>
  public static Dictionary<Vec3i, string> From(ExBlockDef def) {
    JObject structure = (JObject)
      def.ToJson()["attributes"]!["multiblockStructure"]!;

    var codeByNumber = ((JObject)structure["blockNumbers"]!)
      .Properties()
      .ToDictionary(p => (int)p.Value!, p => p.Name);

    var cells = new Dictionary<Vec3i, string>();
    foreach (JToken offset in (JArray)structure["offsets"]!)
      cells[
        new Vec3i((int)offset["x"]!, (int)offset["y"]!, (int)offset["z"]!)
      ] = codeByNumber[(int)offset["w"]!];
    return cells;
  }

  /// <summary>The wanted block code at each world-relative cell, rotated by
  /// <paramref name="angle"/> through vanilla <see cref="MultiblockStructure"/> the way the
  /// production block entity does at placement.</summary>
  public static Dictionary<Vec3i, string> Rotated(ExBlockDef def, int angle) {
    JObject json = (JObject)def.ToJson()["attributes"]!["multiblockStructure"]!;

    // Authored glyph per block number, read straight off the JSON; keeps a domainless or
    // wildcard code in its authored form.
    var codeByNumber = ((JObject)json["blockNumbers"]!)
      .Properties()
      .ToDictionary(p => (int)p.Value!, p => p.Name);

    MultiblockStructure structure = new JsonObject(
      json
    ).AsObject<MultiblockStructure>()!;
    structure.InitForUse(angle);

    var cells = new Dictionary<Vec3i, string>();
    foreach (BlockOffsetAndNumber o in structure.TransformedOffsets)
      cells[new Vec3i(o.X, o.Y, o.Z)] = codeByNumber[o.W];
    return cells;
  }
}
