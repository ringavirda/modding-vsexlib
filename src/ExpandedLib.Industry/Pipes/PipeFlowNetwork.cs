using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Networks;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Industry.Pipes;

/// <summary>
/// The physical pipe network, type "pipeflow": a gas run whose nodes, edges and implicit step live in a
/// <see cref="PipeFlowStore"/>. Machines declare into it and read it back through <see cref="FlowPort"/>.
/// </summary>
internal sealed class PipeFlowNetwork(BlockNetworkModSystem system)
  : BlockNetwork(system) {
  public override string NetworkType => "pipeflow";

  private const double DefaultAmbientC = 20;
  private const double KelvinOffset = 273.15;
  private const double SteamConstant = 461.5;
  private const double WallJPerKgK = 460;

  public override int StepIntervalMs => 200;

  /// <summary>The run's node store and edges.</summary>
  public PipeFlowStore Store { get; } = new();

  /// <summary>What machines declared at each port node and what has accrued there since the last read.</summary>
  internal Dictionary<NodeKey, PortState> Ports { get; } = [];

  /// <summary>Couplings that draw from a node of this run.</summary>
  internal List<FlowCoupling> Couplings { get; } = [];

  /// <summary>Couplings that deliver into a node of this run.</summary>
  internal List<FlowCoupling> Inbound { get; } = [];

  private readonly Dictionary<NodeKey, byte> _savedDamage = [];

  private readonly HashSet<NodeKey> _built = [];

  private bool _dirty;

  /// <summary>The run's grade, 0..1: heating value relative to rich producer gas, mixed by mass from the sources of a <see cref="FlowMedium.Fuel"/> run; 0 on any other.</summary>
  public double Grade { get; private set; }

  public override void OnTick(
    IBlockAccessor world,
    float dt,
    BlockNetworkModSystem manager
  ) {
    if (dt <= 0)
      return;
    if (_dirty) {
      _dirty = false;
      Reconcile(world, manager);
    }
    foreach (var (key, index) in Store.Nodes)
      Store.SetFrozen(index, world.GetChunkAtBlockPos(key.Pos) == null);
    if (_savedDamage.Count > 0) {
      Store.RestoreDamage(_savedDamage);
      _savedDamage.Clear();
    }
    ApplyPorts(dt);
    ApplyInbound(dt);
    DrawCouplings(manager);
    Store.Step(dt);
    foreach (var (key, state) in Ports)
      if (Store.Nodes.TryGetValue(key, out int node))
        state.Accrue(Store, node, dt);
    foreach (var coupling in Couplings)
      if (Store.Nodes.TryGetValue(coupling.From, out int node))
        coupling.Book(Store.CoupledDrawnKg(node), Store.TemperatureC(node));
  }

  /// <inheritdoc/>
  public override void OnTopologyChanged() => _dirty = true;

  private void Reconcile(IBlockAccessor world, BlockNetworkModSystem manager) {
    var gone = _built.Where(key => !Nodes.Contains(key.Pos)).ToHashSet();
    if (gone.Count > 0) {
      Store.Drop(gone);
      _built.ExceptWith(gone);
    }
    double reference = manager.ServerWorld?.SeaLevel ?? 0;
    foreach (var key in NodeKeys)
      if (
        key.Slot == 0
        && !Store.Nodes.ContainsKey(key)
        && CellAt(world, key.Pos) is { Bore: { } bore } cell
      ) {
        BuildNode(world, key, cell, bore, reference);
        _built.Add(key);
      }
    foreach (var key in _built) {
      if (CellAt(world, key.Pos)?.Bore is not { } bore)
        continue;
      LayEdges(world, manager, key, bore);
      ShapeOpenings(world, manager, key, bore, reference);
    }
  }

  private static BEBehaviorPipeFlowCell? CellAt(
    IBlockAccessor world,
    BlockPos pos
  ) => world.GetBlockEntity(pos)?.GetBehavior<BEBehaviorPipeFlowCell>();

  private void BuildNode(
    IBlockAccessor world,
    NodeKey key,
    BEBehaviorPipeFlowCell cell,
    FlowBore bore,
    double referenceM
  ) {
    double boreM = bore.BoreMetres;
    double volume = Math.PI / 4 * boreM * boreM;
    double height = key.Pos.Y + 0.5;
    double ambient =
      world.GetClimateAt(key.Pos, EnumGetClimateMode.NowValues)?.Temperature
      ?? DefaultAmbientC;
    bool water = Medium == FlowMedium.Water;
    double constant = water ? SteamConstant : GasOrifice.AirConstant;
    PipeWall? wall =
      bore.WallKgPerMetre > 0
        ? new PipeWall(
          bore.WallKgPerMetre * WallJPerKgK,
          Math.PI * boreM,
          Math.PI * (boreM + 2 * bore.WallMetres)
        )
        : null;
    ITreeAttribute? entry = cell.TakeLoaded();
    if (
      entry == null
      || !RestoreNode(key, entry, volume, height, constant, wall, water)
    ) {
      double airKg =
        PipeFlowStore.AmbientPressurePa(height, referenceM)
        * volume
        / (GasOrifice.AirConstant * (ambient + KelvinOffset));
      int node = water
        ? Store.AddNode(
          key,
          volume,
          height,
          0,
          ambient,
          constant,
          0,
          airKg,
          true
        )
        : Store.AddNode(key, volume, height, airKg, ambient, constant);
      if (wall is { } fitted)
        Store.SetWall(node, fitted, ambient);
    }
    Store.SetAmbient(Store.IndexOf(key), ambient);
  }

  private void LayEdges(
    IBlockAccessor world,
    BlockNetworkModSystem manager,
    NodeKey key,
    FlowBore bore
  ) {
    int a = Store.IndexOf(key);
    foreach (
      var pos in manager.GetConnectedNeighbors(world, key.Pos, NetworkType)
    ) {
      if (
        !Store.Nodes.TryGetValue(NodeKey.Centre(pos), out int b)
        || Store.EdgeBetween(a, b) >= 0
      )
        continue;
      FlowBore other = CellAt(world, pos)?.Bore ?? bore;
      FlowBore weaker = other.WorkingAtm < bore.WorkingAtm ? other : bore;
      int edge = Store.AddEdge(
        a,
        b,
        Math.Min(bore.BoreMetres, other.BoreMetres),
        1,
        weaker.Friction,
        FlowPipeClass.Cast
      );
      Store.SetRating(edge, weaker);
    }
  }

  private void ShapeOpenings(
    IBlockAccessor world,
    BlockNetworkModSystem manager,
    NodeKey key,
    FlowBore bore,
    double referenceM
  ) {
    if (
      NetworkMembership.Resolve(world, key.Pos, NetworkType) is not { } member
    )
      return;
    int node = Store.IndexOf(key);
    var gas = Medium == FlowMedium.Water ? FlowGas.Steam : FlowGas.Air;
    double open = Math.PI / 4 * bore.BoreMetres * bore.BoreMetres;
    foreach (var face in BlockFacing.ALLFACES) {
      if (!member.HasConnectorAt(world, key.Pos, face))
        continue;
      byte tag = (byte)(1 + face.Index);
      int opening = Store.FindOpening(node, tag);
      if (opening < 0)
        opening = Store.AddAtmosphereOpening(
          node,
          0,
          gas,
          referenceM,
          false,
          tag
        );
      BlockPos across = key.Pos.AddCopy(face);
      bool shut =
        world.GetChunkAtBlockPos(across) == null
        || Store.Nodes.ContainsKey(NodeKey.Centre(across));
      Store.SetOpeningArea(opening, shut ? 0 : open);
    }
  }

  private void ApplyPorts(double dt) {
    foreach (var (key, state) in Ports) {
      if (!Store.Nodes.TryGetValue(key, out int node))
        continue;
      Store.SetHeatWatts(node, state.HeatWatts);
      double rate = state.SourceKgPerSecond;
      if (rate > 0 && !Claim(state.SourceMedium))
        rate = 0;
      if (rate > 0 && Medium == FlowMedium.Fuel) {
        double gas = Store.TotalGasKg();
        double added = rate * dt;
        Grade = (Grade * gas + state.SourceGrade * added) / (gas + added);
      }
      if (rate != Store.DeclaredSource(node) || state.SourceDirty)
        Store.SetSource(node, rate, state.SourceTemperatureC);
      state.SourceDirty = false;
    }
  }

  private bool Claim(FlowMedium medium) {
    if (medium == FlowMedium.Ambient)
      return true;
    if (Medium == FlowMedium.Ambient)
      Medium = medium;
    return Medium == medium;
  }

  private void ApplyInbound(double dt) {
    var kg = new Dictionary<int, (double Kg, double KgC)>();
    foreach (var coupling in Inbound) {
      if (
        !Store.Nodes.TryGetValue(coupling.To, out int node)
        || Store.IsHeld(node)
      )
        continue;
      var (taken, temperatureC) = coupling.Take();
      if (taken <= 0)
        continue;
      var (sum, sumC) = kg.GetValueOrDefault(node);
      kg[node] = (sum + taken, sumC + taken * temperatureC);
    }
    foreach (var (_, node) in Store.Nodes)
      if (!kg.ContainsKey(node) && Store.CoupledInflowKgPerSecond(node) != 0)
        Store.SetCoupledInflow(node, 0);
    foreach (var (node, (sum, sumC)) in kg)
      Store.SetCoupledInflow(node, sum / dt, sumC / sum);
  }

  private void DrawCouplings(BlockNetworkModSystem manager) {
    var draws = new Dictionary<int, double>();
    foreach (var coupling in Couplings) {
      if (!Store.Nodes.TryGetValue(coupling.From, out int node))
        continue;
      double rate = 0;
      if (
        manager.GetNetworkAt(coupling.To) is PipeFlowNetwork receiver
        && receiver.Store.Nodes.TryGetValue(coupling.To, out int target)
      ) {
        if (!receiver.Inbound.Contains(coupling))
          receiver.Inbound.Add(coupling);
        double downstream = receiver.Store.PublishedPressurePa(target);
        double upstream = Store.PressurePa(node);
        if (
          downstream > 0
          && upstream > downstream
          && !receiver.Store.IsHeld(target)
        )
          rate = GasOrifice.Flow(
            upstream,
            downstream,
            Store.TemperatureC(node) + 273.15,
            Store.GasConstant(node),
            coupling.Gas,
            coupling.AreaM2
          );
      }
      draws[node] = draws.GetValueOrDefault(node) + rate;
    }
    foreach (var (node, rate) in draws)
      Store.SetCoupledDraw(node, rate);
  }

  /// <summary>Writes the live store's entry for <paramref name="key"/> into <paramref name="host"/>, never the published display values.</summary>
  /// <exception cref="System.Collections.Generic.KeyNotFoundException">The store holds no such node.</exception>
  public void SaveNode(NodeKey key, ITreeAttribute host) {
    int node = Store.IndexOf(key);
    PipeFlowSave.Write(
      host,
      key.Slot,
      new PipeNodeSave(
        Store.GasKg(node),
        Store.AirKg(node),
        Store.LiquidKg(node),
        Store.TemperatureC(node),
        Store.WallTemperatureC(node),
        0,
        Store.DamageOf(node)
      )
    );
  }

  /// <summary>Adds the node saved in <paramref name="host"/> at the slot of <paramref name="key"/> to the store, unless the store already holds it: while the run lives its store is authoritative and a saved entry is stale.</summary>
  /// <param name="key">The node's identity.</param>
  /// <param name="host">The host block entity's tree.</param>
  /// <param name="volumeM3">Void volume in m3, above zero.</param>
  /// <param name="heightM">Height of the node in m.</param>
  /// <param name="gasConstant">Specific gas constant of the run's gas in J/(kg K), above zero; steam's by default.</param>
  /// <param name="wall">The node's wall, restored at its saved temperature; none leaves the node wall-less.</param>
  /// <param name="water">True for a node of a water run; a saved node holding air is restored as one whatever this says.</param>
  /// <returns>True when the node was added; false when the store holds it or the host has no readable entry for the slot.</returns>
  public bool RestoreNode(
    NodeKey key,
    ITreeAttribute host,
    double volumeM3,
    double heightM,
    double gasConstant = 461.5,
    PipeWall? wall = null,
    bool water = false
  ) {
    if (Store.Nodes.ContainsKey(key))
      return false;
    if (!PipeFlowSave.TryRead(host, key.Slot, out var save))
      return false;
    int node = Store.AddNode(
      key,
      volumeM3,
      heightM,
      save.GasKg,
      save.TemperatureC,
      gasConstant,
      save.LiquidKg,
      save.AirKg,
      water || save.AirKg > 0
    );
    if (wall is { } fitted)
      Store.SetWall(node, fitted, save.WallTemperatureC);
    if (save.Damage != 0)
      _savedDamage[key] = save.Damage;
    return true;
  }

  public override void OnTickPublished(
    IBlockAccessor world,
    BlockNetworkModSystem manager
  ) {
    Store.Publish();
    foreach (var coupling in Couplings)
      coupling.Publish();
  }

  /// <summary>What the run carries; <see cref="FlowMedium.Ambient"/> until a source gives it one.</summary>
  public FlowMedium Medium { get; set; } = FlowMedium.Ambient;

  public override bool SupportsPartialSplit => true;

  /// <summary>Whether the runs carry equal media or either is ambient; any other pair is refused and stays apart.</summary>
  public override bool CanMerge(BlockNetwork other, IBlockAccessor world) =>
    other is PipeFlowNetwork run
    && (
      Medium == run.Medium
      || Medium == FlowMedium.Ambient
      || run.Medium == FlowMedium.Ambient
    );

  public override void OnMerge(BlockNetwork other, IBlockAccessor world) {
    var run = (PipeFlowNetwork)other;
    Store.Absorb(run.Store);
    foreach (var (key, state) in run.Ports)
      Ports[key] = state;
    Couplings.AddRange(run.Couplings);
    Inbound.AddRange(run.Inbound);
    foreach (var (key, code) in run._savedDamage)
      _savedDamage[key] = code;
    _built.UnionWith(run._built);
    _dirty = true;
    Grade = MixedGrade(run);
    if (Medium == FlowMedium.Ambient)
      Medium = run.Medium;
  }

  public override void OnSplitFragment(
    BlockNetwork original,
    IBlockAccessor world
  ) => throw new NotSupportedException("A pipeflow run splits by node keys.");

  public override void OnSplitFragment(
    BlockNetwork original,
    IReadOnlySet<NodeKey> fragmentKeys
  ) {
    var run = (PipeFlowNetwork)original;
    Store.Take(run.Store, fragmentKeys);
    Medium = run.Medium;
    Grade = run.Grade;
    foreach (var key in fragmentKeys)
      if (run.Ports.Remove(key, out var state))
        Ports[key] = state;
    Couplings.AddRange(run.Couplings.Where(c => fragmentKeys.Contains(c.From)));
    run.Couplings.RemoveAll(c => fragmentKeys.Contains(c.From));
    Inbound.AddRange(run.Inbound.Where(c => fragmentKeys.Contains(c.To)));
    run.Inbound.RemoveAll(c => fragmentKeys.Contains(c.To));
    foreach (var key in fragmentKeys)
      if (run._built.Remove(key))
        _built.Add(key);
    foreach (var key in fragmentKeys)
      if (run._savedDamage.Remove(key, out byte code))
        _savedDamage[key] = code;
  }

  private double MixedGrade(PipeFlowNetwork other) {
    double total = Store.TotalGasKg();
    double theirs = other.Store.TotalGasKg();
    return total > 0
      ? (Grade * (total - theirs) + other.Grade * theirs) / total
      : Math.Max(Grade, other.Grade);
  }
}
