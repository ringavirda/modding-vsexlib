using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using ExpandedLib.FastForwarding;
using ExpandedLib.Networks;
using ExpandedLib.Registries;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace ExpandedLib.Networks;

/// <summary>
/// Graph manager for all block networks: node add/remove, BFS fracture detection, and per-tick
/// dispatch. All type-specific state and logic live in the concrete <see cref="BlockNetwork"/>
/// subclasses.
/// </summary>
public class BlockNetworkModSystem : ModSystem {
  #region Graph storage
  private readonly Dictionary<Guid, BlockNetwork> _networks = [];
  private readonly Dictionary<BlockPos, Guid> _posToNetwork = [];
  private readonly Dictionary<NodeKey, Guid> _nodeToNetwork = [];

  /// <summary>The slots indexed for each claimed position, so a removal or move needs no world read.</summary>
  private readonly Dictionary<BlockPos, byte[]> _slotsAt = [];
  private readonly Dictionary<string, Func<BlockNetwork>> _factories = [];

  /// <summary>Networks whose connectivity could not be decided, mapped to their unreadable node positions.</summary>
  private readonly Dictionary<Guid, HashSet<BlockPos>> _unreadableNodes = [];

  private readonly Dictionary<string, string> _factoryOwners = [];

  /// <summary>Milliseconds between the server listener's fires; the finest cadence a network's <see cref="BlockNetwork.StepIntervalMs"/> resolves to.</summary>
  public const int ListenerIntervalMs = 200;

  private readonly Dictionary<string, int> _phaseMs = [];

  private readonly Dictionary<string, int> _sinceStepMs = [];

  /// <summary>Registers the factory for <paramref name="networkType"/>, replacing any earlier factory for the same type.
  /// A type already held by another assembly is replaced with a Warning naming both assemblies; use
  /// <see cref="ReplaceNetworkType"/> to replace on purpose.</summary>
  /// <param name="networkType">The network type key blocks name in their network behaviour.</param>
  /// <param name="factory">Builds a fresh network of that type.</param>
  /// <returns><c>true</c> when an earlier factory for the type was replaced.</returns>
  [MethodImpl(MethodImplOptions.NoInlining)]
  public bool RegisterNetworkType(
    string networkType,
    Func<BlockNetwork> factory
  ) =>
    RegisterNetworkType(
      networkType,
      factory,
      Assembly.GetCallingAssembly().GetName().Name ?? "unknown assembly"
    );

  internal bool RegisterNetworkType(
    string networkType,
    Func<BlockNetwork> factory,
    string owner
  ) {
    bool replaced = _factories.ContainsKey(networkType);
    if (
      replaced
      && _factoryOwners.TryGetValue(networkType, out string? previous)
      && previous != owner
    )
      RegistryLog.Replaced(
        "BlockNetworkModSystem",
        networkType,
        previous,
        owner
      );
    _factories[networkType] = factory;
    _factoryOwners[networkType] = owner;
    return replaced;
  }

  /// <summary>Replaces the factory for <paramref name="networkType"/> on purpose, logging nothing; the
  /// calling assembly becomes the type's owner. Adds the type when none is registered.</summary>
  /// <param name="networkType">The network type key blocks name in their network behaviour.</param>
  /// <param name="factory">Builds a fresh network of that type.</param>
  /// <returns><c>true</c> when an earlier factory for the type was replaced.</returns>
  [MethodImpl(MethodImplOptions.NoInlining)]
  public bool ReplaceNetworkType(string networkType, Func<BlockNetwork> factory) {
    bool replaced = _factories.ContainsKey(networkType);
    _factories[networkType] = factory;
    _factoryOwners[networkType] =
      Assembly.GetCallingAssembly().GetName().Name ?? "unknown assembly";
    return replaced;
  }

  /// <summary>Every network type a factory has been registered for.</summary>
  public IReadOnlyCollection<string> RegisteredNetworkTypes => _factories.Keys;

  /// <summary>The registry of fast-forward members and the groups they form with the networks; lookups made while a member's tick runs are recorded as its couplings.</summary>
  internal FastForwardDriver FastForward { get; }

  public BlockNetworkModSystem() {
    FastForward = new FastForwardDriver(this);
  }

  /// <summary>Server world accessor, available to network instances during their tick. Null on the client.</summary>
  public IServerWorldAccessor? ServerWorld { get; private set; }

  public override void StartServerSide(ICoreServerAPI api) {
    ServerWorld = api.World;
    api.Event.RegisterGameTickListener(
      dt => ListenerTick(api.World.BlockAccessor, dt),
      ListenerIntervalMs
    );
  }

  /// <summary>Every live network instance. Server-side only; the client graph is empty.</summary>
  public IEnumerable<BlockNetwork> AllNetworks => _networks.Values;

  /// <summary>Returns the network that owns <paramref name="pos"/>, or <c>null</c>.</summary>
  public BlockNetwork? GetNetworkAt(BlockPos pos) {
    FastForward.Record(pos);
    return
      _posToNetwork.TryGetValue(pos, out Guid id)
      && _networks.TryGetValue(id, out var net)
      ? net
      : null;
  }

  /// <summary>Returns the network that owns the node <paramref name="key"/>, or <c>null</c>.</summary>
  public BlockNetwork? GetNetworkAt(NodeKey key) {
    FastForward.Record(key.Pos);
    return
      _nodeToNetwork.TryGetValue(key, out Guid id)
      && _networks.TryGetValue(id, out var net)
      ? net
      : null;
  }

  /// <summary>Returns the network across <paramref name="connectorFace"/> from <paramref name="connectorPos"/>, or null when the cell there has no reciprocating connector.</summary>
  public BlockNetwork? GetConnectedNetworkAcross(
    IBlockAccessor world,
    BlockPos connectorPos,
    BlockFacing connectorFace
  ) {
    BlockPos neighbourPos = connectorPos.AddCopy(connectorFace);
    return NetworkMembership.CouplesAt(
      world,
      neighbourPos,
      connectorFace.Opposite
    )
      ? GetNetworkAt(neighbourPos)
      : null;
  }

  /// <summary>Creates a network for <paramref name="networkType"/>, or null when no factory is registered.</summary>
  private BlockNetwork? TryCreateNetwork(string networkType) =>
    _factories.TryGetValue(networkType, out var factory) ? factory() : null;

  /// <summary>Registered network types, for diagnostics.</summary>
  private string RegisteredTypes() =>
    _factories.Count == 0
      ? "(none)"
      : string.Join(", ", _factories.Keys.Order());

  /// <summary>Creates a network of a type already known registered; throws if none is registered.</summary>
  private BlockNetwork CreateNetwork(string networkType) =>
    TryCreateNetwork(networkType)
    ?? throw new InvalidOperationException(
      $"No factory registered for network type '{networkType}', reached from an existing network of "
        + $"that type. Registered: {RegisteredTypes()}."
    );
  #endregion

  #region Graph node manipulation
  /// <summary>
  /// Adds <paramref name="pos"/> to the network graph, merging adjacent networks
  /// of the same type as needed.
  /// </summary>
  /// <param name="broadcast">When <c>true</c> (default), immediately broadcasts state to clients.</param>
  public virtual void AddNode(
    IBlockAccessor world,
    BlockPos pos,
    string networkType,
    bool broadcast = true
  ) {
    FastForward.RaiseEpoch();
    var connectedNeighbors = GetConnectedNeighbors(world, pos, networkType)
      .ToList();

    var adjacentNetworks = connectedNeighbors
      .Where(_posToNetwork.ContainsKey)
      .Select(p => _networks[_posToNetwork[p]])
      .Distinct()
      .ToList();

    if (adjacentNetworks.Count == 0) {
      BlockNetwork? net = TryCreateNetwork(networkType);
      if (net == null) {
        // Reached only for an isolated node; a placement joining an existing run never invokes the factory.
        ServerWorld?.Logger.Error(
          "[exlib] Block network: '{0}' at {1} declares network type '{2}', which no mod registered. "
            + "The block is placed but joins no network. Registered types: {3}. "
            + "Call BlockNetworkModSystem.RegisterNetworkType from ModSystem.Start.",
          world.GetBlock(pos)?.Code?.ToString() ?? "unknown block",
          pos,
          networkType,
          RegisteredTypes()
        );
        return;
      }

      Claim(net, pos, SlotsOf(world, pos, networkType));
      _networks[net.Id] = net;
      net.OnTopologyChanged();
    } else {
      var primaryNet = adjacentNetworks[0];
      Claim(primaryNet, pos, SlotsOf(world, pos, networkType));

      for (int i = 1; i < adjacentNetworks.Count; i++) {
        var netToMerge = adjacentNetworks[i];
        if (!primaryNet.CanMerge(netToMerge, world))
          continue;

        foreach (var nPos in netToMerge.Nodes) {
          Claim(primaryNet, nPos, SlotsHeldAt(nPos));
        }

        // A suspended review carries over to the merged network.
        if (_unreadableNodes.TryGetValue(netToMerge.Id, out var pending)) {
          if (_unreadableNodes.TryGetValue(primaryNet.Id, out var carried))
            carried.UnionWith(pending);
          else
            _unreadableNodes[primaryNet.Id] = pending;
        }

        primaryNet.OnMerge(netToMerge, world);
        DissolveNetwork(netToMerge.Id);
      }

      primaryNet.OnTopologyChanged();
      if (broadcast)
        primaryNet.BroadcastUpdate(world);
    }
  }

  /// <summary>
  /// Removes <paramref name="pos"/> from the network graph, running BFS fracture
  /// detection and splitting the network if it disconnects.
  /// </summary>
  /// <param name="broadcast">When <c>true</c> (default), broadcasts the updated state to surviving fragments.</param>
  public virtual void RemoveNode(
    IBlockAccessor world,
    BlockPos pos,
    bool broadcast = true
  ) {
    if (!_posToNetwork.TryGetValue(pos, out Guid netId))
      return;
    if (!_networks.TryGetValue(netId, out BlockNetwork? network))
      return;

    network.Nodes.Remove(pos);
    _posToNetwork.Remove(pos);
    Unindex(network, pos);

    if (network.Nodes.Count == 0) {
      FastForward.RaiseEpoch();
      DissolveNetwork(netId);
      return;
    }

    ReviewConnectivity(world, netId, network, broadcast);
  }
  #endregion

  #region Connectivity review
  /// <summary>Decides whether <paramref name="network"/> is still one run; splits it, or defers the decision when part of it is behind an unloaded chunk.</summary>
  private void ReviewConnectivity(
    IBlockAccessor world,
    Guid netId,
    BlockNetwork network,
    bool broadcast
  ) {
    HashSet<BlockPos> visited = WalkFromAnyNode(world, network);

    if (visited.Count < network.Nodes.Count) {
      var unreadable = network
        .Nodes.Where(p => world.GetChunkAtBlockPos(p) == null)
        .ToHashSet();

      if (unreadable.Count > 0) {
        // Suspended, not answered: every node position comes from a placed block.
        _unreadableNodes[netId] = unreadable;
        Settle(world, network, broadcast);
        return;
      }

      _unreadableNodes.Remove(netId);
      Fracture(world, netId, network, broadcast);
      return;
    }

    _unreadableNodes.Remove(netId);
    Settle(world, network, broadcast);
  }

  /// <summary>Walks the graph from one node of <paramref name="network"/> and returns everything reached, restricted to nodes already in the set.</summary>
  private HashSet<BlockPos> WalkFromAnyNode(
    IBlockAccessor world,
    BlockNetwork network
  ) {
    BlockPos startNode = network.Nodes.First();
    var visited = new HashSet<BlockPos> { startNode };
    var queue = new Queue<BlockPos>();
    queue.Enqueue(startNode);

    while (queue.Count > 0) {
      BlockPos curr = queue.Dequeue();
      foreach (
        var adj in GetConnectedNeighbors(world, curr, network.NetworkType)
      ) {
        if (network.Nodes.Contains(adj) && visited.Add(adj))
          queue.Enqueue(adj);
      }
    }

    return visited;
  }

  /// <summary>Rebuilds each connected component of <paramref name="network"/> as a network of its own,
  /// each inheriting its proportional share of the original state.</summary>
  private void Fracture(
    IBlockAccessor world,
    Guid netId,
    BlockNetwork network,
    bool broadcast
  ) {
    FastForward.RaiseEpoch();
    var unassigned = new HashSet<BlockPos>(network.Nodes);
    DissolveNetwork(netId);

    while (unassigned.Count > 0) {
      BlockPos newStart = unassigned.First();
      BlockNetwork newNet = CreateNetwork(network.NetworkType);
      _networks[newNet.Id] = newNet;

      var bfsQueue = new Queue<BlockPos>();
      bfsQueue.Enqueue(newStart);
      unassigned.Remove(newStart);
      Claim(newNet, newStart, SlotsHeldAt(newStart));

      while (bfsQueue.Count > 0) {
        BlockPos curr = bfsQueue.Dequeue();
        foreach (
          var adj in GetConnectedNeighbors(world, curr, network.NetworkType)
        ) {
          if (unassigned.Contains(adj)) {
            unassigned.Remove(adj);
            Claim(newNet, adj, SlotsHeldAt(adj));
            bfsQueue.Enqueue(adj);
          }
        }
      }

      if (newNet.SupportsPartialSplit)
        newNet.OnSplitFragment(network, newNet.NodeKeys);
      else
        newNet.OnSplitFragment(network, world);
      newNet.OnTopologyChanged();

      if (broadcast)
        newNet.BroadcastUpdate(world);
    }
  }

  /// <summary>Leaves <paramref name="network"/> as one run and notifies it that its node set may have moved.</summary>
  private static void Settle(
    IBlockAccessor world,
    BlockNetwork network,
    bool broadcast
  ) {
    network.OnTopologyChanged();
    if (broadcast)
      network.BroadcastUpdate(world);
  }

  /// <summary>Adds <paramref name="pos"/> to <paramref name="net"/> with a node at each of <paramref name="slots"/>, replacing the position's earlier index entries.</summary>
  private void Claim(BlockNetwork net, BlockPos pos, byte[] slots) {
    net.Nodes.Add(pos);
    _posToNetwork[pos] = net.Id;
    _slotsAt[pos] = slots;
    foreach (byte slot in slots) {
      var key = new NodeKey(pos, slot);
      net.NodeKeys.Add(key);
      _nodeToNetwork[key] = net.Id;
    }
  }

  /// <summary>Drops the index entries of <paramref name="pos"/> held for <paramref name="net"/>.</summary>
  private void Unindex(BlockNetwork net, BlockPos pos) {
    if (!_slotsAt.Remove(pos, out byte[]? slots))
      return;
    foreach (byte slot in slots) {
      var key = new NodeKey(pos, slot);
      net.NodeKeys.Remove(key);
      _nodeToNetwork.Remove(key);
    }
  }

  private byte[] SlotsHeldAt(BlockPos pos) =>
    _slotsAt.TryGetValue(pos, out byte[]? slots) ? slots : [0];

  /// <summary>The slots the member at <paramref name="pos"/> holds nodes at, slot 0 alone when it has none or declares none.</summary>
  private static byte[] SlotsOf(
    IBlockAccessor world,
    BlockPos pos,
    string networkType
  ) {
    var slots = NetworkMembership
      .Resolve(world, pos, networkType)
      ?.Slots(world, pos);
    return slots == null || slots.Count == 0 ? [0] : [.. slots.Distinct()];
  }

  /// <summary>Drops <paramref name="netId"/> and anything the graph remembers about it.</summary>
  private void DissolveNetwork(Guid netId) {
    _networks.Remove(netId);
    _unreadableNodes.Remove(netId);
  }
  #endregion

  /// <summary>Rebuilds the network rooted at <paramref name="rootPos"/> via BFS, replacing overlapping entries and preserving state from the old root network.</summary>
  public BlockNetwork? RebuildFromRoot(
    IBlockAccessor world,
    BlockPos rootPos,
    string networkType,
    bool broadcast = true
  ) {
    if (NetworkMembership.Resolve(world, rootPos, networkType) == null)
      return null;

    var reachable = new HashSet<BlockPos>();
    var bfsQueue = new Queue<BlockPos>();
    reachable.Add(rootPos);
    bfsQueue.Enqueue(rootPos);

    while (bfsQueue.Count > 0) {
      var curr = bfsQueue.Dequeue();
      foreach (var neighbor in GetConnectedNeighbors(world, curr, networkType)) {
        if (reachable.Add(neighbor))
          bfsQueue.Enqueue(neighbor);
      }
    }

    var oldNetIds = new HashSet<Guid>();
    foreach (var pos in reachable) {
      if (_posToNetwork.TryGetValue(pos, out Guid id))
        oldNetIds.Add(id);
    }

    _posToNetwork.TryGetValue(rootPos, out Guid rootOldId);
    BlockNetwork? rootOldNet =
      rootOldId != default && _networks.TryGetValue(rootOldId, out var ron)
        ? ron
        : null;

    foreach (var id in oldNetIds) {
      if (_networks.TryGetValue(id, out var oldNet)) {
        foreach (var p in oldNet.Nodes) {
          _posToNetwork.Remove(p);
          Unindex(oldNet, p);
        }
        DissolveNetwork(id);
      }
    }

    var newNet = CreateNetwork(networkType);
    newNet.RootPos = rootPos.Copy();

    if (rootOldNet != null)
      newNet.InheritStateFrom(rootOldNet);

    foreach (var pos in reachable) {
      Claim(newNet, pos, SlotsOf(world, pos, networkType));
    }

    _networks[newNet.Id] = newNet;
    newNet.OnTopologyChanged();

    if (broadcast)
      newNet.BroadcastUpdate(world);

    return newNet;
  }

  #region Tick
  /// <summary>Advances server time by <paramref name="dt"/> and steps every live network of each type whose
  /// <see cref="BlockNetwork.StepIntervalMs"/> (read from the type's first live network) has accrued, as the server
  /// listener does every <see cref="ListenerIntervalMs"/>. Time accrues per type and keeps its overshoot. A stepped
  /// network gets <see cref="BlockNetwork.OnTick"/> with the seconds since its type last stepped (at most twice the
  /// interval), then <see cref="BlockNetwork.OnTickPublished"/> once every due network has stepped.</summary>
  /// <param name="blockAccessor">The accessor the networks read and write through.</param>
  /// <param name="dt">Seconds since the previous call, normally <see cref="ListenerIntervalMs"/> / 1000.</param>
  public void ListenerTick(IBlockAccessor blockAccessor, float dt) {
    int elapsedMs = (int)Math.Round(dt * 1000f);
    ResumeSuspendedReviews(blockAccessor);
    var live = _networks.Values.ToList();
    var types = live.GroupBy(n => n.NetworkType).ToList();
    foreach (string stale in _phaseMs.Keys.ToList())
      if (!types.Any(g => g.Key == stale)) {
        _phaseMs.Remove(stale);
        _sinceStepMs.Remove(stale);
      }

    var due = new List<(BlockNetwork Network, int AccruedMs)>();
    foreach (var group in types) {
      string type = group.Key;
      int interval = Math.Max(1, group.First().StepIntervalMs);
      int phase = _phaseMs.GetValueOrDefault(type) + elapsedMs;
      int since = _sinceStepMs.GetValueOrDefault(type) + elapsedMs;
      if (phase >= interval) {
        foreach (var network in group)
          due.Add((network, Math.Min(since, 2 * interval)));
        phase = Math.Min(phase - interval, interval - 1);
        since = 0;
      }
      _phaseMs[type] = phase;
      _sinceStepMs[type] = since;
    }

    Step(blockAccessor, due);
    FastForward.Advance(dt);
  }

  /// <summary>Steps every live network once with <paramref name="dt"/> seconds, whatever its
  /// <see cref="BlockNetwork.StepIntervalMs"/> and the time accrued, capped at twice the interval; resumes any
  /// suspended connectivity review first and calls <see cref="BlockNetwork.OnTickPublished"/> afterwards. For callers
  /// that drive the graph themselves; the server listener goes through <see cref="ListenerTick"/>.</summary>
  /// <param name="blockAccessor">The accessor the networks read and write through.</param>
  /// <param name="dt">Seconds the step covers.</param>
  public void ServerTick(IBlockAccessor blockAccessor, float dt) {
    ResumeSuspendedReviews(blockAccessor);
    int ms = (int)Math.Round(dt * 1000f);
    Step(
      blockAccessor,
      [
        .. _networks.Values.Select(n =>
          (n, Math.Min(ms, 2 * Math.Max(1, n.StepIntervalMs)))
        ),
      ]
    );
    FastForward.Advance(dt);
  }

  private void Step(
    IBlockAccessor blockAccessor,
    List<(BlockNetwork Network, int AccruedMs)> due
  ) {
    foreach (var (network, accruedMs) in due)
      network.OnTick(blockAccessor, accruedMs / 1000f, this);
    foreach (var (network, _) in due)
      if (_networks.ContainsKey(network.Id))
        network.OnTickPublished(blockAccessor, this);
  }

  /// <summary>Re-decides connectivity for every network a missing chunk left suspended, once one of its unreadable cells is readable again.</summary>
  private void ResumeSuspendedReviews(IBlockAccessor world) {
    if (_unreadableNodes.Count == 0)
      return;

    // Snapshot: a review rewrites both dictionaries mid-loop.
    var ready = _unreadableNodes
      .Where(e => e.Value.Any(p => world.GetChunkAtBlockPos(p) != null))
      .Select(e => e.Key)
      .ToList();

    foreach (Guid netId in ready) {
      if (_networks.TryGetValue(netId, out BlockNetwork? network))
        ReviewConnectivity(world, netId, network, broadcast: true);
      else
        _unreadableNodes.Remove(netId);
    }
  }
  #endregion

  #region Public utilities
  /// <summary>Returns the connector faces on <paramref name="pos"/> that have no valid network neighbour, as seen by <paramref name="member"/>. A face toward an unloaded chunk is not listed: the cell beyond is unseen, not open.</summary>
  public BlockFacing[] GetOpenConnectorFaces(
    IBlockAccessor world,
    BlockPos pos,
    INetworkMember member
  ) {
    var open = new List<BlockFacing>();
    foreach (var face in BlockFacing.ALLFACES) {
      if (!member.HasConnectorAt(world, pos, face))
        continue;

      BlockPos nPos = pos.AddCopy(face);
      if (world.GetChunkAtBlockPos(nPos) == null)
        continue;
      Block nBlock = world.GetBlock(nPos);

      bool connected =
        IsValidNetworkNeighbour(world, pos, member, nBlock, nPos, face)
        || SealsAgainst(world, pos, nBlock, face);
      if (!connected)
        open.Add(face);
    }
    return open.Count == 0 ? [] : open.ToArray();
  }

  /// <summary>Returns positions graph-connected to <paramref name="pos"/> on <paramref name="networkType"/>: matching connector, same network type, not broken.</summary>
  public IEnumerable<BlockPos> GetConnectedNeighbors(
    IBlockAccessor world,
    BlockPos pos,
    string networkType
  ) {
    INetworkMember? source = NetworkMembership.Resolve(world, pos, networkType);
    if (source == null || !CouplesFrom(world, pos, source))
      yield break;

    foreach (var face in BlockFacing.ALLFACES) {
      if (source.HasConnectorAt(world, pos, face)) {
        BlockPos neighborPos = pos.AddCopy(face);
        if (
          IsValidNetworkNeighbour(
            world,
            pos,
            source,
            world.GetBlock(neighborPos),
            neighborPos,
            face
          )
        )
          yield return neighborPos;
      }
    }
  }

  /// <summary>Whether the cell at <paramref name="pos"/> continues the run: not a fixed endpoint and not severed.</summary>
  private static bool CouplesFrom(
    IBlockAccessor world,
    BlockPos pos,
    INetworkMember member
  ) => !member.IsNetworkEndPoint && !member.IsConnectionBroken(world, pos);

  /// <summary>Whether the block at <paramref name="pos"/> treats a non-network neighbour on <paramref name="face"/> as sealed.</summary>
  private static bool SealsAgainst(
    IBlockAccessor world,
    BlockPos pos,
    Block neighbour,
    BlockFacing face
  ) =>
    world.GetBlock(pos) is BlockNetworkNode node
    && node.IsValidNonNetworkConnection(neighbour, face);

  /// <summary>Whether the cell across <paramref name="facing"/> joins <paramref name="source"/>.</summary>
  private static bool IsValidNetworkNeighbour(
    IBlockAccessor world,
    BlockPos sourcePos,
    INetworkMember source,
    Block neighbourBlock,
    BlockPos neighbourPos,
    BlockFacing facing
  ) {
    INetworkMember? neighbour = NetworkMembership.Resolve(
      world,
      neighbourPos,
      source.NetworkTypeAt(world, sourcePos)
    );
    if (
      neighbour == null
      || !neighbour.HasConnectorAt(world, neighbourPos, facing.Opposite)
    )
      return false;

    // Matching connectors and type do not imply the two physically couple; see INetworkMember.AcceptsNeighbour.
    if (
      !source.AcceptsNeighbour(neighbourBlock)
      || !neighbour.AcceptsNeighbour(world.GetBlock(sourcePos))
    )
      return false;

    return CouplesFrom(world, neighbourPos, neighbour);
  }

  /// <summary>Maps an orientation string of single-letter side codes ("ns", "we", "nsewud") to the faces it names, dropping unknown or repeated letters.</summary>
  public static BlockFacing[] SidesToFaces(string? orientation) =>
    [
      .. (orientation ?? "")
        .Select(c => SideToFace(c.ToString()))
        .OfType<BlockFacing>()
        .Distinct(),
    ];

  /// <summary>Maps a single-char side code ("n","s","e","w","u","d") to its <see cref="BlockFacing"/>.</summary>
  public static BlockFacing? SideToFace(string? side) =>
    side switch {
      "n" => BlockFacing.NORTH,
      "s" => BlockFacing.SOUTH,
      "e" => BlockFacing.EAST,
      "w" => BlockFacing.WEST,
      "u" => BlockFacing.UP,
      "d" => BlockFacing.DOWN,
      _ => null,
    };

  /// <summary>Returns <c>true</c> when <paramref name="neighbour"/> is a network block of type <paramref name="id"/>.</summary>
  public static bool IsCompatibleNetworkBlock(Block neighbour, string id) =>
    neighbour is INetworkConnector connector && connector.NetworkType == id;

  /// <summary>Position-aware compatibility: consults the connector's per-cell network type.</summary>
  public static bool IsCompatibleNetworkBlockAt(
    IBlockAccessor world,
    BlockPos pos,
    Block neighbour,
    string id
  ) =>
    neighbour is INetworkConnector connector
    && connector.NetworkTypeAt(world, pos) == id;

  public override void Dispose() {
    _networks.Clear();
    _posToNetwork.Clear();
    _nodeToNetwork.Clear();
    _slotsAt.Clear();
    _unreadableNodes.Clear();
    base.Dispose();
  }
  #endregion
}
