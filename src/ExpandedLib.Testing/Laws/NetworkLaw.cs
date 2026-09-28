using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Checks;
using ExpandedLib.Networks;
using ExpandedLib.Structures;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Testing;

/// <summary>Two network members face to face agree on whether they join: the walk from each
/// (<see cref="BlockNetworkModSystem.GetConnectedNeighbors"/>, which asks both members through
/// <c>IsValidNetworkNeighbour</c>) reaches the other, or neither does.</summary>
public static class NetworkLaw {
  internal const string Name = "network";

  /// <summary>Finds every connector of the network members of <paramref name="domain"/> in
  /// <paramref name="world"/>, then stands each representative connector against each of the
  /// same network type on the opposite face and walks from both.</summary>
  /// <remarks>A member is a block whose signals name a network: a node, an entity's membership, a
  /// footprint port. Each variant is set through the accessor's <c>SetBlock</c> (a filler host
  /// then runs <see cref="Block.OnBlockPlaced"/>) and its cells resolved per network. Per network
  /// type, face and kind (the blocktype with its unplaced groups) the first connector stands for
  /// the rest. A pair whose second structure would overlap the first is skipped. A finding is a
  /// pair joined from one side only, and a placement or walk that throws. A pair that joins from
  /// both sides is a case, so a network whose members stop joining shows as fewer cases.</remarks>
  /// <param name="world">A world holding every variant of the blocks judged and the network types
  /// they name (<see cref="BlockLaws.Run"/> stands one).</param>
  /// <param name="domain">The domain whose blocks are judged; members of other domains in the
  /// world are met as partners, never judged.</param>
  /// <returns>The law's blocktypes, pairs joined from both sides and findings, each keyed by the
  /// variant code of the pair's first member.</returns>
  public static BlockLaws.Law Run(TestWorld world, string domain) {
    var findings = new List<string>();
    var sites = new BlockLaws.Sites();
    int blocks = 0,
      cases = 0;
    var connectors = new List<Connector>();
    foreach (
      string owner in world
        .World.Blocks.Select(b => b?.Code?.Domain)
        .OfType<string>()
        .Where(d => d != "game")
        .Distinct()
    )
      foreach (
        IGrouping<string, Block> type in BlockLaws.Blocktypes(world, owner)
      ) {
        Block[] members =
        [
          .. type.Where(b => BlockLaws.Signals(world, b).Networks.Count > 0),
      ];
        if (members.Length == 0)
          continue;
        if (owner == domain)
          blocks++;
        foreach (Block block in members)
          connectors.AddRange(
            Find(world, block, sites.Next(), owner == domain ? findings : null)
          );
      }

    Connector[] kept =
    [
      .. connectors
        .GroupBy(c => (c.Network, c.Face.Index, c.Kind))
        .Select(g => g.First()),
    ];
    foreach (Connector a in kept.Where(c => c.Variant.Code.Domain == domain))
      foreach (
        Connector b in kept.Where(b =>
          b.Network == a.Network && b.Face == a.Face.Opposite
        )
      )
        if (Walk(world, a, b, sites.Next(), findings))
          cases++;
    return new BlockLaws.Law(Name, blocks, cases, findings);
  }

  /// <summary>A connector a member's cell exposes on <see cref="Face"/> for
  /// <see cref="Network"/>; <see cref="Cell"/> and <see cref="Cells"/> are offsets from the
  /// variant's own cell.</summary>
  private sealed record Connector(
    Block Variant,
    string Kind,
    string Network,
    (int X, int Y, int Z) Cell,
    BlockFacing Face,
    (int X, int Y, int Z)[] Cells
  );

  private static IEnumerable<Connector> Find(
    TestWorld world,
    Block block,
    BlockPos at,
    List<string>? findings
  ) {
    if (!Stand(world, block, at, "placed", findings))
      return [];
    (int X, int Y, int Z)[] cells =
    [
      (0, 0, 0),
      .. BlockLaws
        .FillersOf(world, at)
        .Select(p => (p.X - at.X, p.Y - at.Y, p.Z - at.Z)),
    ];
    string kind = KindOf(block);
    var found = new List<Connector>();
    foreach (string network in BlockLaws.Signals(world, block).Networks)
      foreach ((int X, int Y, int Z) cell in cells) {
        BlockPos pos = at.AddCopy(cell.X, cell.Y, cell.Z);
        if (
          NetworkMembership.Resolve(world.Accessor, pos, network)
          is not { } member
        )
          continue;
        foreach (BlockFacing face in BlockFacing.ALLFACES)
          if (member.HasConnectorAt(world.Accessor, pos, face))
            found.Add(new Connector(block, kind, network, cell, face, cells));
      }
    return found;
  }

  /// <summary>Stands <paramref name="a"/> at <paramref name="at"/> and <paramref name="b"/> so
  /// its connector faces <paramref name="a"/>'s, and walks from both.</summary>
  /// <returns>Whether each walk reaches the other; false for a pair skipped, one whose placement
  /// threw, and one joined from one side only.</returns>
  private static bool Walk(
    TestWorld world,
    Connector a,
    Connector b,
    BlockPos at,
    List<string> findings
  ) {
    BlockPos from = at.AddCopy(a.Cell.X, a.Cell.Y, a.Cell.Z);
    BlockPos to = from.AddCopy(a.Face);
    BlockPos other = to.AddCopy(-b.Cell.X, -b.Cell.Y, -b.Cell.Z);
    if (
      b.Cells.Any(c =>
        a.Cells.Contains(
          (other.X + c.X - at.X, other.Y + c.Y - at.Y, other.Z + c.Z - at.Z)
        )
      )
    )
      return false;
    string pair =
      $"{a.Network} at {Offset(a.Cell)} across its {a.Face.Code} face and {b.Variant.Code} "
      + $"at {Offset(b.Cell)}";
    if (
      !Stand(world, a.Variant, at, $"paired {pair}", findings)
      || !Stand(world, b.Variant, other, $"paired {pair}", findings)
    )
      return false;
    try {
      bool there = Joins(world, from, to, a.Network),
        back = Joins(world, to, from, a.Network);
      if (there != back)
        findings.Add(
          there
            ? $"{a.Variant.Code} {pair}: its walk reaches the other, the other's does not reach it"
            : $"{a.Variant.Code} {pair}: the other's walk reaches it, its own does not reach the other"
        );
      return there && back;
    } catch (Exception e) {
      findings.Add(
        $"{a.Variant.Code} {pair}: the walk threw {BlockLaws.Describe(e)}"
      );
      return false;
    }
  }

  private static bool Joins(
    TestWorld world,
    BlockPos from,
    BlockPos to,
    string network
  ) =>
    world
      .Networks.GetConnectedNeighbors(world.Accessor, from, network)
      .Any(p => p.Equals(to));

  /// <summary>Sets <paramref name="block"/> at <paramref name="at"/>; a filler host then runs its
  /// <see cref="Block.OnBlockPlaced"/>. False, with a finding, when either throws.</summary>
  private static bool Stand(
    TestWorld world,
    Block block,
    BlockPos at,
    string what,
    List<string>? findings
  ) {
    try {
      world.Accessor.SetBlock(block.BlockId, at);
      if (block is IFillerHost)
        block.OnBlockPlaced(world.World, at, new ItemStack(block));
      return true;
    } catch (Exception e) {
      findings?.Add($"{block.Code} {what} threw {BlockLaws.Describe(e)}");
      return false;
    }
  }

  private static string KindOf(Block block) {
    string[] placed = [.. BlockSignals.PlacedGroupsOf(block)];
    return BlockLaws.TypeOf(block)
      + string.Concat(
        (block.Variant ?? Enumerable.Empty<KeyValuePair<string, string>>())
          .Where(p => !placed.Contains(p.Key))
          .Select(p => $"|{p.Key}={p.Value}")
      );
  }

  private static string Offset((int X, int Y, int Z) cell) =>
    $"({cell.X}, {cell.Y}, {cell.Z})";
}
