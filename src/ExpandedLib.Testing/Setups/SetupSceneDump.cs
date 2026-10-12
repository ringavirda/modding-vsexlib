#if GAME_GE_1_22
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using ExpandedLib.Networks;
using ExpandedLib.Structures;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent.Mechanics;

namespace ExpandedLib.Testing;

/// <summary>A built scene as the <c>setup-scene/1</c> text the scene renderer reads: every
/// non-air cell with its setup id, role, network key and line key, then the networks and
/// lines.</summary>
internal static class SetupSceneDump
{
  private sealed record Cell(BlockPos At, Block Block, bool Fluid);

  /// <summary>Writes <paramref name="scene"/> as <c>setup-scene/1</c>. Cells run by y, then z,
  /// then x, a solid entry before a fluid one at the same cell. Network keys run A, B, ...
  /// Z, AA, AB, ... and line keys M1, M2, ..., each given in the order of the key's first
  /// cell.</summary>
  /// <param name="scene">A built scene.</param>
  /// <param name="setup">The setup's id.</param>
  /// <param name="variant">The variant's name, or null for the main case.</param>
  /// <returns>The file's text, lines ending in <c>\n</c>.</returns>
  public static string Of(SetupScene scene, string setup, string? variant)
  {
    TestWorld world = scene.World;
    List<Cell> cells =
    [
      .. world
        .SolidBlocks.Where(kv => kv.Value.Id != 0)
        .Select(kv => new Cell(kv.Key, kv.Value, false))
        .Concat(
          world
            .FluidBlocks.Where(kv => kv.Value.Id != 0)
            .Select(kv => new Cell(kv.Key, kv.Value, true))
        )
        .OrderBy(c => c.At.Y)
        .ThenBy(c => c.At.Z)
        .ThenBy(c => c.At.X)
        .ThenBy(c => c.Fluid),
    ];
    HashSet<BlockPos> principals =
    [
      .. world
        .BlockEntities.Select(kv => kv.Value)
        .OfType<BlockEntityStructureFiller>()
        .Select(f => f.Principal)
        .OfType<BlockPos>(),
    ];
    Dictionary<BlockNetwork, string> networkKeys = new(
      ReferenceEqualityComparer.Instance
    );
    Dictionary<MechanicalNetwork, string> lineKeys = new(
      ReferenceEqualityComparer.Instance
    );
    Dictionary<string, List<string>> networkIds = [];
    Dictionary<string, List<string>> lineIds = [];
    Dictionary<string, int> lineMembers = [];
    List<BlockNetwork> networks = [];

    string? IdAt(BlockPos pos) =>
      scene
        .Placements.Where(p => p.Value.Cells.Contains(pos))
        .OrderBy(p => p.Value.Cells.Count)
        .ThenBy(p => p.Key, StringComparer.Ordinal)
        .Select(p => p.Key)
        .FirstOrDefault();

    StringBuilder text = new();
    text.Append("{\"format\": \"setup-scene/1\", \"setup\": ")
      .Append(Quote(setup))
      .Append(", \"variant\": ")
      .Append(variant == null ? "null" : Quote(variant))
      .Append(", \"game\": ")
      .Append(Quote(GameVersion.ShortGameVersion))
      .Append(",\n \"cells\": [\n");
    for (int i = 0; i < cells.Count; i++)
    {
      Cell cell = cells[i];
      BlockEntity? entity = cell.Fluid ? null : world.GetBlockEntity(cell.At);
      BlockEntityStructureFiller? filler = entity as BlockEntityStructureFiller;
      List<BEBehaviorMPBase> mechanical =
      [
        .. entity?.Behaviors.OfType<BEBehaviorMPBase>() ?? [],
      ];
      string role;
      if (cell.Fluid)
        role = "fluid";
      else if (
        principals.Contains(cell.At)
        || entity is BlockEntityMultiblockStructure
      )
        role = "core";
      else if (filler == null && cell.Block is not BlockStructureFiller)
        role = "block";
      else
        role =
          filler?.PortFace != null || mechanical.Count > 0 ? "port" : "filler";
      BlockPos? principal = filler?.Principal;
      string? id =
        cell.Fluid || role is "filler" or "port" ? null : IdAt(cell.At);
      string? owner = id ?? (principal == null ? null : IdAt(principal));

      string? network = null;
      if (!cell.Fluid && world.NetworkAt(cell.At) is { } net)
      {
        if (!networkKeys.TryGetValue(net, out network))
        {
          network = networkKeys[net] = NetworkKey(networkKeys.Count);
          networks.Add(net);
          networkIds[network] = [];
        }
        Add(networkIds[network], owner);
      }
      string? line = null;
      if (
        mechanical.Select(b => b.Network).FirstOrDefault(n => n != null) is
        { } mech
      )
      {
        if (!lineKeys.TryGetValue(mech, out line))
        {
          line = lineKeys[mech] = "M" + (lineKeys.Count + 1);
          lineIds[line] = [];
          lineMembers[line] = 0;
        }
        Add(lineIds[line], owner);
        lineMembers[line] += mechanical.Count(b => b.Network == mech);
      }

      List<string> parts =
      [
        "\"at\": " + Position(cell.At),
        "\"code\": " + Quote(cell.Block.Code.ToString()),
      ];
      if (id != null)
        parts.Add("\"id\": " + Quote(id));
      parts.Add("\"role\": " + Quote(role));
      if (principal != null)
        parts.Add("\"principal\": " + Position(principal));
      if (network != null)
        parts.Add("\"network\": " + Quote(network));
      if (line != null)
        parts.Add("\"line\": " + Quote(line));
      text.Append("  {")
        .Append(string.Join(", ", parts))
        .Append(i < cells.Count - 1 ? "},\n" : "}\n");
    }
    text.Append(" ],\n \"networks\": ")
      .Append(
        Entries(
          networks.Select(n =>
          {
            string key = networkKeys[n];
            return $"{{\"key\": {Quote(key)}, \"type\": {Quote(n.NetworkType)}, "
              + $"\"nodes\": {n.Nodes.Count}, \"ids\": {Strings(networkIds[key])}}}";
          })
        )
      )
      .Append(",\n \"lines\": ")
      .Append(
        Entries(
          lineIds.Select(l =>
            $"{{\"key\": {Quote(l.Key)}, \"members\": {lineMembers[l.Key]}, "
            + $"\"ids\": {Strings(l.Value)}}}"
          )
        )
      )
      .Append("}\n");
    return text.ToString();
  }

  private static void Add(List<string> ids, string? id)
  {
    if (id != null && !ids.Contains(id))
      ids.Add(id);
  }

  private static string NetworkKey(int index)
  {
    string key = "";
    for (int n = index + 1; n > 0; n = (n - 1) / 26)
      key = (char)('A' + (n - 1) % 26) + key;
    return key;
  }

  private static string Entries(IEnumerable<string> entries)
  {
    List<string> list = [.. entries];
    return list.Count == 0
      ? "[]"
      : "[\n  " + string.Join(",\n  ", list) + "\n ]";
  }

  private static string Quote(string value) => JsonSerializer.Serialize(value);

  private static string Strings(IEnumerable<string> values) =>
    "[" + string.Join(", ", values.Select(Quote)) + "]";

  private static string Position(BlockPos pos) => $"[{pos.X},{pos.Y},{pos.Z}]";
}
#endif
