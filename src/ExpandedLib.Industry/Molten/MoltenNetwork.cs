using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Networks;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Industry.Molten;

/// <summary>
/// Concrete <see cref="BlockNetwork"/> for the molten-canal system. Each node (an
/// <see cref="IMoltenCell"/>) owns its own metal; the network supplies connectivity plus the per-tick
/// driver that flows metal cell-to-cell by level equalisation and runs each cell's cooling.
/// </summary>
public class MoltenNetwork(BlockNetworkModSystem system) : BlockNetwork(system) {
  public override string NetworkType => "molten";

  // Resolved once from the first loaded node's BE (a network never moves between worlds).
  private IWorldAccessor? _world;

  private IWorldAccessor? GetWorld(IBlockAccessor blockAccessor) {
    if (_world != null)
      return _world;
    foreach (var pos in Nodes) {
      if (
        blockAccessor.GetBlockEntity(pos) is BlockEntity be
        && be.Api?.World != null
      )
        return _world = be.Api.World;
    }
    return null;
  }

  private static int ComparePos(BlockPos a, BlockPos b) {
    int c = a.X.CompareTo(b.X);
    if (c != 0)
      return c;
    c = a.Y.CompareTo(b.Y);
    return c != 0 ? c : a.Z.CompareTo(b.Z);
  }

  /// <summary>Drops the cached distance map: a joint closed or opened on a ring changes the order without changing the cells.</summary>
  public override void OnTopologyChanged() => _cachedDistFromStart = null;

  // Cached; the BFS re-runs only when the topology signature changes.
  private Dictionary<BlockPos, int>? _cachedDistFromStart;
  private (int Count, long PosHash, long StartHash) _cachedTopoSig;

  /// <summary>
  /// The cached distance-from-start map, rebuilt via <see cref="BuildDistanceFromStart"/> only when
  /// the topology signature has changed since the last computation.
  /// </summary>
  private Dictionary<BlockPos, int> GetDistanceFromStart(
    IBlockAccessor blockAccessor,
    List<(BlockPos Pos, IMoltenCell Cell)> cells
  ) {
    var sig = ComputeTopologySignature(cells);
    if (_cachedDistFromStart == null || sig != _cachedTopoSig) {
      _cachedDistFromStart = BuildDistanceFromStart(blockAccessor, cells);
      _cachedTopoSig = sig;
    }
    return _cachedDistFromStart;
  }

  /// <summary>Order-independent fingerprint of the cells that drive the distance map: cell count plus
  /// XOR-folded hashes of cell positions and flow-source positions.</summary>
  private static (int, long, long) ComputeTopologySignature(
    List<(BlockPos Pos, IMoltenCell Cell)> cells
  ) {
    long posHash = 0;
    long startHash = 0;
    foreach (var c in cells) {
      long h = unchecked(
        (long)((uint)c.Pos.GetHashCode() * 0x9E3779B97F4A7C15UL)
      );
      posHash ^= h;
      if (c.Cell.IsFlowSource)
        startHash ^= h;
    }
    return (cells.Count, posHash, startHash);
  }

  /// <summary>Multi-source BFS over the canal graph mapping each cell to its hop distance from the
  /// nearest flow source; cells unreachable from any source are absent from the map.</summary>
  private Dictionary<BlockPos, int> BuildDistanceFromStart(
    IBlockAccessor blockAccessor,
    List<(BlockPos Pos, IMoltenCell Cell)> cells
  ) {
    var dist = new Dictionary<BlockPos, int>(cells.Count);
    var queue = new Queue<BlockPos>();
    foreach (var c in cells)
      if (c.Cell.IsFlowSource) {
        dist[c.Pos] = 0;
        queue.Enqueue(c.Pos);
      }

    while (queue.Count > 0) {
      BlockPos cur = queue.Dequeue();
      int next = dist[cur] + 1;
      if (blockAccessor.GetBlock(cur) is not BlockNetworkNode node)
        continue;

      foreach (var face in BlockFacing.HORIZONTALS) {
        BlockPos npos = cur.AddCopy(face);
        if (!JointOpen(blockAccessor, cur, node, face, npos))
          continue;
        if (!Nodes.Contains(npos) || dist.ContainsKey(npos))
          continue;
        if (blockAccessor.GetBlockEntity(npos) is not IMoltenCell)
          continue;
        dist[npos] = next;
        queue.Enqueue(npos);
      }
    }
    return dist;
  }

  /// <summary>Multi-source BFS mapping each cell within <paramref name="radius"/> canal hops of an
  /// open mold to its hop distance from the nearest one; open molds map to 0 and every other cell is
  /// absent, so a radius of 0 or less draws nothing. A hop reaches a cell beside or above the nearer
  /// one, never below it, and never a sealed or solidified cell and never across a joint either end lacks a connector for.</summary>
  private Dictionary<BlockPos, int> BuildDistanceToOpenMold(
    IBlockAccessor blockAccessor,
    List<(BlockPos Pos, IMoltenCell Cell)> cells,
    int radius
  ) {
    var dist = new Dictionary<BlockPos, int>();
    var queue = new Queue<BlockPos>();
    foreach (var c in cells)
      if (c.Cell.IsOpenMold && !c.Cell.Sealed && !c.Cell.Solidified) {
        dist[c.Pos] = 0;
        queue.Enqueue(c.Pos);
      }

    while (queue.Count > 0) {
      BlockPos cur = queue.Dequeue();
      int next = dist[cur] + 1;
      if (
        next > radius
        || blockAccessor.GetBlock(cur) is not BlockNetworkNode node
        || blockAccessor.GetBlockEntity(cur) is not IMoltenCell curCell
      )
        continue;

      foreach (var face in BlockFacing.ALLFACES) {
        // Metal from the cell below cannot climb into this one.
        if (face == BlockFacing.DOWN)
          continue;
        BlockPos npos = cur.AddCopy(face);
        if (!JointOpen(blockAccessor, cur, node, face, npos))
          continue;
        if (!Nodes.Contains(npos) || dist.ContainsKey(npos))
          continue;
        if (
          blockAccessor.GetBlockEntity(npos) is not IMoltenCell nCell
          || nCell.Sealed
          || nCell.Solidified
        )
          continue;
        if (
          face == BlockFacing.UP
          && EdgeRules(curCell.FlowRules, nCell.FlowRules)?.HorizontalOnly
            == true
        )
          continue;
        dist[npos] = next;
        queue.Enqueue(npos);
      }
    }
    return dist;
  }

  /// <summary>Whether <paramref name="face"/> of the node at <paramref name="pos"/> is a joint: the node
  /// has a connector there and the block at <paramref name="npos"/>, when it is a network node, has
  /// one facing back. A neighbour that is not a network node does not close the joint.</summary>
  internal static bool JointOpen(
    IBlockAccessor blockAccessor,
    BlockPos pos,
    BlockNetworkNode node,
    BlockFacing face,
    BlockPos npos
  ) =>
    node.HasConnectorAt(blockAccessor, pos, face)
    && (
      blockAccessor.GetBlock(npos) is not BlockNetworkNode far
      || far.HasConnectorAt(blockAccessor, npos, face.Opposite)
    );

  /// <summary>Orders cells for the flow pass, greater distance from the source first; position breaks
  /// ties, and an unreachable cell counts as farthest.</summary>
  private static int CompareFlowOrder(
    (BlockPos Pos, IMoltenCell Cell) x,
    (BlockPos Pos, IMoltenCell Cell) y,
    Dictionary<BlockPos, int> dist
  ) {
    int dx = dist.TryGetValue(x.Pos, out int vx) ? vx : int.MaxValue;
    int dy = dist.TryGetValue(y.Pos, out int vy) ? vy : int.MaxValue;
    int c = dy.CompareTo(dx); // descending distance: farthest processed first
    return c != 0 ? c : ComparePos(x.Pos, y.Pos);
  }

  #region Tick - flow + cooling
  public override void OnTick(
    IBlockAccessor blockAccessor,
    float dt,
    BlockNetworkModSystem manager
  ) {
    var world = GetWorld(blockAccessor);
    if (world == null)
      return;

    var cells = new List<(BlockPos Pos, IMoltenCell Cell)>(Nodes.Count);
    foreach (var pos in Nodes)
      if (blockAccessor.GetBlockEntity(pos) is IMoltenCell c)
        cells.Add((pos, c));
    if (cells.Count == 0)
      return;

    // Farthest-first order picks which end drives each edge; the two amounts set which way metal
    // moves.
    var distFromStart = GetDistanceFromStart(blockAccessor, cells);
    var distToMold = BuildDistanceToOpenMold(
      blockAccessor,
      cells,
      ExlibValues.MoltenMoldDrawRadius
    );
    cells.Sort((x, y) => CompareFlowOrder(x, y, distFromStart));
    foreach (var c in cells)
      c.Cell.EnsureMetalStack(world);

    int defaultFlow = ExlibValues.MoltenFlowRate;
    var shares = new Dictionary<BlockPos, Dictionary<BlockPos, int>>();
    foreach (var a in cells) {
      if (
        a.Cell.Sealed
        || a.Cell.Solidified
        || blockAccessor.GetBlock(a.Pos) is not BlockNetworkNode aNode
      )
        continue;

      // ALLFACES, not HORIZONTALS: vertical neighbours also exchange metal; the connector test gates
      // every face at both ends.
      foreach (var face in BlockFacing.ALLFACES) {
        BlockPos npos = a.Pos.AddCopy(face);
        if (!JointOpen(blockAccessor, a.Pos, aNode, face, npos))
          continue;
        if (!Nodes.Contains(npos))
          continue;
        if (blockAccessor.GetBlockEntity(npos) is not IMoltenCell bCell)
          continue;
        var b = (Pos: npos, Cell: bCell);
        if (b.Cell.Sealed || b.Cell.Solidified)
          continue;
        var rules = EdgeRules(a.Cell.FlowRules, b.Cell.FlowRules);
        int maxFlow = rules?.FlowRate ?? defaultFlow;

        // A vertical edge is downhill only: driven from the upper cell (face DOWN); the reverse face is
        // skipped to avoid pumping metal uphill.
        if (face.Axis == EnumAxis.Y) {
          if (face != BlockFacing.DOWN || rules?.HorizontalOnly == true)
            continue;
          FlowEdge(
            a,
            b,
            maxFlow,
            rules,
            distFromStart,
            distToMold,
            world,
            downhillOnly: true
          );
          continue;
        }

        // Drive each undirected edge exactly once, from the cell farther from the source.
        if (CompareFlowOrder(a, b, distFromStart) >= 0)
          continue;

        int? share = null;
        if (DistanceOf(distFromStart, b.Pos) < DistanceOf(distFromStart, a.Pos)) {
          if (
            !shares.TryGetValue(b.Pos, out var armShares)
            && b.Cell.CellAmount > a.Cell.CellAmount
          )
            shares[b.Pos] = armShares = ShareSpare(
              blockAccessor,
              b,
              distFromStart,
              distToMold
            );
          if (
            armShares != null
            && armShares.TryGetValue(a.Pos, out int armShare)
          )
            share = armShare;
        }
        FlowEdge(
          a,
          b,
          maxFlow,
          rules,
          distFromStart,
          distToMold,
          world,
          share: share
        );
      }
    }

    // Thermal pass: cool / solidify each cell.
    foreach (var c in cells)
      c.Cell.UpdateThermal(world);
  }

  /// <summary>The level neighbours <paramref name="giver"/> pushes on to: farther than it from the
  /// source, of its fall, neither sealed, solidified nor a drain, with room, holding less than the
  /// giver and no other metal, on a connection that neither conveys nor runs toward an open mold.
  /// In flow order; empty when none qualifies.</summary>
  private List<(BlockPos Pos, IMoltenCell Cell)> PushOnArms(
    IBlockAccessor blockAccessor,
    (BlockPos Pos, IMoltenCell Cell) giver,
    Dictionary<BlockPos, int> distFromStart,
    Dictionary<BlockPos, int> distToMold
  ) {
    var arms = new List<(BlockPos Pos, IMoltenCell Cell)>();
    if (blockAccessor.GetBlock(giver.Pos) is not BlockNetworkNode gNode)
      return arms;
    int giverDist = DistanceOf(distFromStart, giver.Pos);
    bool giverInDraw = distToMold.TryGetValue(giver.Pos, out int giverToMold);
    foreach (var face in BlockFacing.HORIZONTALS) {
      BlockPos npos = giver.Pos.AddCopy(face);
      if (
        !JointOpen(blockAccessor, giver.Pos, gNode, face, npos)
        || !Nodes.Contains(npos)
        || blockAccessor.GetBlockEntity(npos) is not IMoltenCell x
      )
        continue;
      if (
        x.Sealed
        || x.Solidified
        || x.AcceptsSubMinimumFlow
        || x.Fall != giver.Cell.Fall
        || x.CellAmount >= x.MaxUnitCapacity
        || x.CellAmount >= giver.Cell.CellAmount
        || (x.CellAmount > 0 && x.CellMetalType != giver.Cell.CellMetalType)
        || DistanceOf(distFromStart, npos) <= giverDist
      )
        continue;
      if (
        giverInDraw
        && distToMold.TryGetValue(npos, out int armToMold)
        && armToMold != giverToMold
      )
        continue;
      if (EdgeRules(giver.Cell.FlowRules, x.FlowRules)?.Conveys == true)
        continue;
      arms.Add((npos, x));
    }
    arms.Sort((p, q) => CompareFlowOrder(p, q, distFromStart));
    return arms;
  }

  /// <summary>Divides the spare of <paramref name="giver"/> over its <see cref="PushOnArms"/>, each
  /// arm taking at most what it would take alone and the rest sharing evenly; units left by the
  /// integer division go one at a time to the arms in flow order. Keyed by arm position; empty when
  /// the giver has no arm.</summary>
  private Dictionary<BlockPos, int> ShareSpare(
    IBlockAccessor blockAccessor,
    (BlockPos Pos, IMoltenCell Cell) giver,
    Dictionary<BlockPos, int> distFromStart,
    Dictionary<BlockPos, int> distToMold
  ) {
    var arms = PushOnArms(blockAccessor, giver, distFromStart, distToMold);
    var shares = new Dictionary<BlockPos, int>();
    if (arms.Count == 0)
      return shares;
    int held = giver.Cell.CellAmount;
    int spare = held - ExlibValues.MoltenMinFlowAmount;
    var demand = new int[arms.Count];
    for (int i = 0; i < arms.Count; i++) {
      var x = arms[i].Cell;
      int maxFlow =
        EdgeRules(giver.Cell.FlowRules, x.FlowRules)?.FlowRate
        ?? ExlibValues.MoltenFlowRate;
      demand[i] = Math.Max(
        0,
        Math.Min(
          Math.Min(x.MaxUnitCapacity - x.CellAmount, maxFlow),
          held - Math.Max(x.CellAmount, ExlibValues.MoltenMinFlowAmount)
        )
      );
    }
    var given = new int[arms.Count];
    var byDemand = Enumerable
      .Range(0, arms.Count)
      .OrderBy(i => demand[i])
      .ToArray();
    int total = Math.Min(spare, demand.Sum());
    int remaining = total;
    for (int k = 0; k < byDemand.Length; k++) {
      int share = remaining / (byDemand.Length - k);
      if (demand[byDemand[k]] <= share) {
        given[byDemand[k]] = demand[byDemand[k]];
        remaining -= given[byDemand[k]];
        continue;
      }
      for (; k < byDemand.Length; k++)
        given[byDemand[k]] = share;
    }
    int left = total - given.Sum();
    for (int i = 0; i < arms.Count && left > 0; i++) {
      int more = Math.Min(demand[i] - given[i], left);
      given[i] += more;
      left -= more;
    }
    for (int i = 0; i < arms.Count; i++)
      shares[arms[i].Pos] = given[i];
    return shares;
  }

  /// <summary>The rules of a connection whose two cells both return rules, or null when either
  /// returns none.</summary>
  private static MoltenFlowRules? EdgeRules(
    MoltenFlowRules? a,
    MoltenFlowRules? b
  ) {
    if (a is not MoltenFlowRules x || b is not MoltenFlowRules y)
      return null;
    return new MoltenFlowRules(
      Math.Min(x.FlowRate, y.FlowRate),
      Math.Max(x.MinFlowGap, y.MinFlowGap),
      x.Conveys && y.Conveys,
      x.HorizontalOnly || y.HorizontalOnly
    );
  }

  /// <summary>The cell's hop distance from the nearest flow source, or
  /// <see cref="int.MaxValue"/> for a cell no source reaches.</summary>
  private static int DistanceOf(
    Dictionary<BlockPos, int> distFromStart,
    BlockPos pos
  ) => distFromStart.TryGetValue(pos, out int d) ? d : int.MaxValue;

  /// <summary>Moves metal across one connection, capped at <paramref name="maxFlow"/> units: from the
  /// cell of smaller <see cref="IMoltenCell.Fall"/> when the falls differ, and from a downhill edge's
  /// upper cell of larger fall, handing over its whole charge, else toward the nearer end
  /// in <paramref name="distToMold"/>, else by <paramref name="rules"/> or the defaults;
  /// <paramref name="downhillOnly"/> makes the edge one-way from <paramref name="a"/>.</summary>
  /// <param name="share">What <see cref="ShareSpare"/> allotted the receiver of a level giver pushing
  /// on to several arms, or null; the nearer cell then gives, whichever holds more now, at most this
  /// and keeping its skin, even when the two are level or closer than the flow gap.</param>
  private static void FlowEdge(
    (BlockPos Pos, IMoltenCell Cell) a,
    (BlockPos Pos, IMoltenCell Cell) b,
    int maxFlow,
    MoltenFlowRules? rules,
    Dictionary<BlockPos, int> distFromStart,
    Dictionary<BlockPos, int> distToMold,
    IWorldAccessor world,
    bool downhillOnly = false,
    int? share = null
  ) {
    var aCap = a.Cell.MaxUnitCapacity;
    var bCap = b.Cell.MaxUnitCapacity;
    if (aCap <= 0 || bCap <= 0)
      return;

    if (!downhillOnly && a.Cell.Fall != b.Cell.Fall) {
      var upper = a.Cell.Fall < b.Cell.Fall ? a : b;
      var lower = a.Cell.Fall < b.Cell.Fall ? b : a;
      Transfer(upper, lower, Math.Min(upper.Cell.CellAmount, maxFlow), world);
      return;
    }

    if (downhillOnly && a.Cell.Fall > b.Cell.Fall) {
      Transfer(a, b, Math.Min(a.Cell.CellAmount, maxFlow), world);
      return;
    }

    var diff = Math.Abs(a.Cell.CellAmount - b.Cell.CellAmount);
    if (diff == 0 && share is null)
      return;

    if (
      !downhillOnly
      && distToMold.TryGetValue(a.Pos, out int aToMold)
      && distToMold.TryGetValue(b.Pos, out int bToMold)
      && aToMold != bToMold
    ) {
      var from = aToMold > bToMold ? a : b;
      var to = aToMold > bToMold ? b : a;
      if (from.Cell.CellAmount < to.Cell.CellAmount)
        return;
      Transfer(from, to, Math.Min(diff, maxFlow), world);
      return;
    }

    bool aIsGiver = share is null && a.Cell.CellAmount > b.Cell.CellAmount;
    // A downhill edge runs one way only: nothing moves when the lower cell is the fuller one.
    if (downhillOnly && !aIsGiver)
      return;
    var giver = aIsGiver ? a : b;
    var receiver = aIsGiver ? b : a;

    // A drain or downhill edge takes the whole difference; a level giver nearer the start keeps a skin, or what the farther cell holds if that is more.
    bool drain = receiver.Cell.AcceptsSubMinimumFlow;
    bool whole = drain || downhillOnly;
    if (rules is MoltenFlowRules r) {
      // The floor is on the gap, never on the halved step.
      if (!drain && share is null && diff < r.MinFlowGap)
        return;
      whole |=
        r.Conveys
        && DistanceOf(distFromStart, receiver.Pos)
          > DistanceOf(distFromStart, giver.Pos);
    }
    int step;
    if (whole)
      step = diff;
    else if (
      DistanceOf(distFromStart, receiver.Pos)
      > DistanceOf(distFromStart, giver.Pos)
    ) {
      int spare = giver.Cell.CellAmount - ExlibValues.MoltenMinFlowAmount;
      step = share is int s
        ? Math.Min(s, spare)
        : Math.Min(
          giver.Cell.CellAmount
            - Math.Max(
              receiver.Cell.CellAmount,
              ExlibValues.MoltenMinFlowAmount
            ),
          spare
        );
    } else
      step = diff / 2;
    Transfer(giver, receiver, step > maxFlow ? maxFlow : step, world);
  }

  /// <summary>Moves up to <paramref name="amount"/> units from <paramref name="giver"/> into
  /// <paramref name="receiver"/>; a receiver holding another metal takes none.</summary>
  private static void Transfer(
    (BlockPos Pos, IMoltenCell Cell) giver,
    (BlockPos Pos, IMoltenCell Cell) receiver,
    int amount,
    IWorldAccessor world
  ) {
    if (amount <= 0 || giver.Cell.CellAmount <= 0)
      return;
    // Different metals sit side by side without mixing.
    if (
      receiver.Cell.CellAmount > 0
      && receiver.Cell.CellMetalType != giver.Cell.CellMetalType
    )
      return;

    var accepted = receiver.Cell.PushMetalRaw(
      amount,
      giver.Cell.CellMetalType,
      giver.Cell.CellTemperature,
      world
    );
    if (accepted > 0f)
      giver.Cell.DrainMetal(accepted);
  }
  #endregion

  #region Graph lifecycle (cells own their metal, so these are trivial)
  public override bool CanMerge(BlockNetwork other, IBlockAccessor world) =>
    other is MoltenNetwork;

  public override void OnMerge(BlockNetwork other, IBlockAccessor world) { }

  public override void OnSplitFragment(
    BlockNetwork original,
    IBlockAccessor world
  ) { }
  #endregion
}
