#if GAME_GE_1_22
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using ExpandedLib.Helpers;
using ExpandedLib.Industry.MechanicalPower;
using ExpandedLib.Networks;
using ExpandedLib.Structures;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent.Mechanics;

namespace ExpandedLib.Testing;

/// <summary>A built scene as the <c>setup-scene/1</c> text the scene renderer reads: every
/// non-air cell with its setup id, role, principal, network and line keys, port and open faces,
/// then the networks and lines.</summary>
internal static class SetupSceneDump
{
  private sealed record Cell(BlockPos At, Block Block, bool Fluid);

  private sealed record Entry(
    Cell Cell,
    BlockEntityStructureFiller? Filler,
    List<BEBehaviorMPBase> Mechanical,
    string Role,
    string? Id,
    string? Owner,
    BlockPos? Principal,
    BlockNetwork? Net,
    string? Network,
    string? Line,
    string? Shape
  );

  /// <summary>The <c>setup-scene/1</c> text of <paramref name="scene"/>. Cells run by y, then z,
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
    Dictionary<string, List<string>> ids = [];
    Dictionary<string, int> lineMembers = [];
    List<BlockNetwork> networks = [];

    string? IdAt(BlockPos pos) =>
      scene
        .Placements.Where(p => p.Value.Cells.Contains(pos))
        .OrderBy(p => p.Value.Cells.Count)
        .ThenBy(p => p.Key, StringComparer.Ordinal)
        .Select(p => p.Key)
        .FirstOrDefault();

    List<Entry> entries = [];
    foreach (Cell cell in cells)
    {
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

      BlockNetwork? net = cell.Fluid ? null : world.NetworkAt(cell.At);
      string? network = null;
      if (net != null)
      {
        if (!networkKeys.TryGetValue(net, out network))
        {
          network = networkKeys[net] = NetworkKey(networkKeys.Count);
          networks.Add(net);
          ids[network] = [];
        }
        Add(ids[network], owner);
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
          ids[line] = [];
          lineMembers[line] = 0;
        }
        Add(ids[line], owner);
        lineMembers[line] += mechanical.Count(b => b.Network == mech);
      }
      entries.Add(
        new Entry(
          cell,
          filler,
          mechanical,
          role,
          id,
          owner,
          principal,
          net,
          network,
          line,
          (entity as IDrawsShape)?.DrawnShape.ToString()
        )
      );
    }
    Dictionary<BlockPos, string> lineAt = entries
      .Where(e => e.Line != null)
      .ToDictionary(e => e.Cell.At, e => e.Line!);

    StringBuilder text = new();
    text.Append("{\"format\": \"setup-scene/1\", \"setup\": ")
      .Append(Quote(setup))
      .Append(", \"variant\": ")
      .Append(variant == null ? "null" : Quote(variant))
      .Append(", \"game\": ")
      .Append(Quote(GameVersion.ShortGameVersion))
      .Append(",\n \"cells\": [\n");
    for (int i = 0; i < entries.Count; i++)
    {
      Entry e = entries[i];
      List<string> parts =
      [
        "\"at\": " + Position(e.Cell.At),
        "\"code\": " + Quote(e.Cell.Block.Code.ToString()),
      ];
      if (e.Shape != null)
        parts.Add("\"shape\": " + Quote(e.Shape));
      if (e.Id != null)
        parts.Add("\"id\": " + Quote(e.Id));
      parts.Add("\"role\": " + Quote(e.Role));
      if (e.Principal != null)
        parts.Add("\"principal\": " + Position(e.Principal));
      if (e.Network != null)
        parts.Add("\"network\": " + Quote(e.Network));
      if (e.Line != null)
        parts.Add("\"line\": " + Quote(e.Line));
      if (e.Role == "port")
      {
        (BlockFacing? face, string type, string? joins) = PortOf(
          e,
          world,
          networkKeys,
          lineAt
        );
        Add(joins == null ? [] : ids[joins], e.Owner);
        parts.Add(
          "\"port\": {\"face\": "
            + (face == null ? "null" : Quote(face.Code[0].ToString()))
            + ", \"type\": "
            + Quote(type)
            + ", \"joins\": "
            + (joins == null ? "null" : Quote(joins))
            + "}"
        );
      }
      if (!e.Cell.Fluid && e.Filler?.PortFace == null)
      {
        List<string> open = OpenFaces(world, e.Cell.At, e.Net?.NetworkType);
        if (open.Count > 0)
          parts.Add("\"open\": " + Strings(open));
      }
      text.Append("  {")
        .Append(string.Join(", ", parts))
        .Append(i < entries.Count - 1 ? "},\n" : "}\n");
    }
    text.Append(" ],\n \"networks\": ")
      .Append(
        Entries(
          networks.Select(n =>
          {
            string key = networkKeys[n];
            return $"{{\"key\": {Quote(key)}, \"type\": {Quote(n.NetworkType)}, "
              + $"\"nodes\": {n.Nodes.Count}, \"ids\": {Strings(ids[key])}}}";
          })
        )
      )
      .Append(",\n \"lines\": ")
      .Append(
        Entries(
          lineKeys.Values.Select(key =>
            $"{{\"key\": {Quote(key)}, \"members\": {lineMembers[key]}, "
            + $"\"ids\": {Strings(ids[key])}}}"
          )
        )
      )
      .Append("}\n");
    return text.ToString();
  }

  private static (BlockFacing? Face, string Type, string? Joins) PortOf(
    Entry entry,
    TestWorld world,
    Dictionary<BlockNetwork, string> networkKeys,
    Dictionary<BlockPos, string> lineAt
  )
  {
    BlockPos at = entry.Cell.At;
    if (entry.Filler?.PortFace != null)
    {
      BlockFacing? face = ExOrientation.FacingFromSide(entry.Filler.PortFace);
      string type = entry.Filler.PortNetworkType ?? "";
      if (face == null)
        return (null, type, null);
      BlockPos faced = at.AddCopy(face);
      BlockNetwork? net = world.NetworkAt(faced);
      bool joins =
        net != null
        && net.NetworkType == type
        && NetworkMembership
          .Resolve(world.Accessor, faced, type)
          ?.HasConnectorAt(world.Accessor, faced, face.Opposite) == true;
      return (face, type, joins ? networkKeys[net!] : null);
    }
    BlockFacing? mechanicalFace = entry
      .Mechanical.OfType<BEBehaviorMPFillerPort>()
      .FirstOrDefault()
      ?.PortFacing;
    string? joined =
      mechanicalFace == null ? entry.Line
      : lineAt.TryGetValue(at.AddCopy(mechanicalFace), out string? key)
      && key == entry.Line
        ? key
      : null;
    return (mechanicalFace, "mechanical", joined);
  }

  private static List<string> OpenFaces(
    TestWorld world,
    BlockPos at,
    string? networkType
  )
  {
    List<(INetworkMember Member, string Type)> members = [];
    foreach (
      string type in new[] { networkType }
        .Concat(
          NetworkMembership
            .MembersOf(world.GetBlockEntity(at))
            .Select(m => m.NetworkTypeAt(world.Accessor, at))
        )
        .Append(
          (world.GetBlock(at) as INetworkConnector)?.NetworkTypeAt(
            world.Accessor,
            at
          )
        )
        .OfType<string>()
        .Where(t => t.Length > 0)
        .Distinct()
    )
      if (NetworkMembership.Resolve(world.Accessor, at, type) is { } member)
        members.Add((member, type));
    return
    [
      .. BlockFacing
        .ALLFACES.Where(f =>
          members.Any(m =>
          {
            if (!m.Member.HasConnectorAt(world.Accessor, at, f))
              return false;
            BlockPos next = at.AddCopy(f);
            if (
              world.GetBlock(at) is BlockNetworkNode node
              && node.IsValidNonNetworkConnection(world.GetBlock(next), f)
            )
              return false;
            return NetworkMembership
                .Resolve(world.Accessor, next, m.Type)
                ?.HasConnectorAt(world.Accessor, next, f.Opposite) != true;
          })
        )
        .Select(f => f.Code[0].ToString()),
    ];
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
