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
/// <c>IsValidNetworkNeighbour</c>) reaches the other, or neither does. Each face a port declares
/// couples to some member (<see cref="RunPorts"/>).</summary>
public static class NetworkLaw {
  internal const string Name = "network";

  internal const string PortName = "network port";

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
    (int blocks, Connector[] kept) = Members(world, domain, sites, findings);
    int cases = 0;
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

  /// <summary>Stands each face a port of <paramref name="domain"/> declares against each network
  /// member facing it, and counts the pairs the graph couples.</summary>
  /// <remarks>A port is an <see cref="INetworkConnector"/> block, neither a
  /// <see cref="BlockNetworkNode"/> nor a structure filler, that answers for its own cell. Its
  /// faces are read through <c>INetworkMember.HasConnectorAt</c> as <see cref="Run"/> reads a
  /// member's, the first per network type, face and kind standing for the rest; its partners are
  /// the connectors <see cref="Run"/> finds that are no port's. A pair couples when
  /// <see cref="BlockNetworkModSystem.GetConnectedNeighbors"/> from the member reaches the port. A
  /// finding is a placement or walk that throws, and a port face no member couples to.</remarks>
  /// <param name="world">As for <see cref="Run"/>.</param>
  /// <param name="domain">The domain whose ports are judged; members of every domain but
  /// <c>game</c> are their partners.</param>
  /// <returns>The law <c>network port</c>: port blocktypes, pairs coupled and findings, each keyed
  /// by the port's variant code.</returns>
  public static BlockLaws.Law RunPorts(TestWorld world, string domain) {
    var findings = new List<string>();
    var sites = new BlockLaws.Sites();
    Connector[] members =
    [
      .. Members(world, domain, sites, null).Kept.Where(c => !c.Port),
    ];
    int blocks = 0,
      cases = 0;
    var ports = new List<Connector>();
    foreach (
      IGrouping<string, Block> type in BlockLaws.Blocktypes(world, domain)
    ) {
      Block[] declared = [.. type.Where(IsPort)];
      if (declared.Length == 0)
        continue;
      blocks++;
      foreach (Block block in declared)
        ports.AddRange(FindPort(world, block, sites.Next(), findings));
    }
    foreach (
      Connector port in ports
        .GroupBy(c => (c.Network, c.Face.Index, c.Kind))
        .Select(g => g.First())
    ) {
      int coupled = 0;
      foreach (
        Connector member in members.Where(m =>
          m.Network == port.Network && m.Face == port.Face.Opposite
        )
      )
        if (Couple(world, port, member, sites.Next(), findings))
          coupled++;
      if (coupled == 0)
        findings.Add(
          $"{port.Variant.Code} {port.Network} port on its {port.Face.Code} face: no member "
            + "couples to it"
        );
      cases += coupled;
    }
    return new BlockLaws.Law(PortName, blocks, cases, findings);
  }

  /// <summary>The network members of <paramref name="domain"/>'s blocktypes counted, and the
  /// first connector per network type, face and kind among the members of every domain but
  /// <c>game</c>.</summary>
  /// <param name="findings">Where a throwing placement of a <paramref name="domain"/> member goes;
  /// null drops it.</param>
  private static (int Blocks, Connector[] Kept) Members(
    TestWorld world,
    string domain,
    BlockLaws.Sites sites,
    List<string>? findings
  ) {
    int blocks = 0;
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
    return (
      blocks,
      [
        .. connectors
          .GroupBy(c => (c.Network, c.Face.Index, c.Kind))
          .Select(g => g.First()),
      ]
    );
  }

  /// <summary>Whether <paramref name="block"/> is a port: an <see cref="INetworkConnector"/> that is
  /// neither a graph node nor a structure filler.</summary>
  private static bool IsPort(Block block) =>
    block
      is INetworkConnector
        and not BlockNetworkNode
        and not BlockStructureFiller;

  /// <summary>A connector a member's cell exposes on <see cref="Face"/> for
  /// <see cref="Network"/>; <see cref="Cell"/> and <see cref="Cells"/> are offsets from the
  /// variant's own cell. <see cref="Port"/> is set when a port answers for the cell
  /// itself.</summary>
  private sealed record Connector(
    Block Variant,
    string Kind,
    string Network,
    (int X, int Y, int Z) Cell,
    BlockFacing Face,
    (int X, int Y, int Z)[] Cells,
    bool Port = false
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
        bool port = member is Block own && IsPort(own);
        foreach (BlockFacing face in BlockFacing.ALLFACES)
          if (member.HasConnectorAt(world.Accessor, pos, face))
            found.Add(
              new Connector(block, kind, network, cell, face, cells, port)
            );
      }
    return found;
  }

  /// <summary>The faces <paramref name="block"/>, a port, declares at its own cell when set at
  /// <paramref name="at"/>; none when a membership of its entity answers for the cell
  /// instead.</summary>
  private static IEnumerable<Connector> FindPort(
    TestWorld world,
    Block block,
    BlockPos at,
    List<string> findings
  ) {
    if (!Stand(world, block, at, "placed", findings))
      return [];
    string network = ((INetworkMember)block).NetworkTypeAt(world.Accessor, at);
    if (
      NetworkMembership.Resolve(world.Accessor, at, network) is not { } resolved
      || !ReferenceEquals(resolved, block)
    )
      return [];
    (int X, int Y, int Z)[] cells =
    [
      (0, 0, 0),
      .. BlockLaws
        .FillersOf(world, at)
        .Select(p => (p.X - at.X, p.Y - at.Y, p.Z - at.Z)),
    ];
    string kind = KindOf(block);
    return
    [
      .. BlockFacing
        .ALLFACES.Where(face =>
          resolved.HasConnectorAt(world.Accessor, at, face)
        )
        .Select(face => new Connector(
          block,
          kind,
          network,
          (0, 0, 0),
          face,
          cells,
          true
        )),
    ];
  }

  /// <summary>Stands <paramref name="port"/> at <paramref name="at"/> and
  /// <paramref name="member"/> so its connector faces the port's, and walks from the
  /// member.</summary>
  /// <returns>Whether the member's walk reaches the port; false for a pair skipped and one whose
  /// placement or walk threw.</returns>
  private static bool Couple(
    TestWorld world,
    Connector port,
    Connector member,
    BlockPos at,
    List<string> findings
  ) {
    BlockPos across = at.AddCopy(port.Face);
    BlockPos other = across.AddCopy(
      -member.Cell.X,
      -member.Cell.Y,
      -member.Cell.Z
    );
    if (
      member.Cells.Any(c =>
        port.Cells.Contains(
          (other.X + c.X - at.X, other.Y + c.Y - at.Y, other.Z + c.Z - at.Z)
        )
      )
    )
      return false;
    string pair =
      $"{port.Network} port on its {port.Face.Code} face and {member.Variant.Code} at "
      + $"{Offset(member.Cell)}";
    if (
      !Stand(world, port.Variant, at, $"paired {pair}", findings)
      || !Stand(world, member.Variant, other, $"paired {pair}", findings)
    )
      return false;
    try {
      return Joins(world, across, at, port.Network);
    } catch (Exception e) {
      findings.Add(
        $"{port.Variant.Code} {pair}: the walk threw {BlockLaws.Describe(e)}"
      );
      return false;
    }
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
