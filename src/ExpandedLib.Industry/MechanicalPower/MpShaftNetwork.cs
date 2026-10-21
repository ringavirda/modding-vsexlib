using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Networks;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace ExpandedLib.Industry.MechanicalPower;

/// <summary>
/// The physical shaft network, type "mpshaft": one rigid run whose nodes, joints and implicit step live in a
/// <see cref="MpShaftStore"/>. Runs merge by momentum, a split keeps the run's speed, and a run with a node in an
/// unloaded chunk holds still whole.
/// </summary>
internal sealed class MpShaftNetwork(BlockNetworkModSystem system)
  : BlockNetwork(system) {
  public override string NetworkType => "mpshaft";

  public override int StepIntervalMs => 200;

  public override bool SupportsPartialSplit => true;

  private float _pendingInertia;
  private float _pendingSpeed;
  private readonly Dictionary<NodeKey, MpShaftRunState> _pending = [];
  private float _clockS;
  private readonly MpReadingGate _gate = new();
  private double _lastHours = -1d;

  /// <summary>Overrides <c>MpShaftFailure</c> for this run when set.</summary>
  internal bool? FailureOverride { get; set; }

  /// <summary>The run's node store and joints.</summary>
  public MpShaftStore Store { get; private set; } = new();

  /// <summary>Reference speed of the run in rad/s, signed; a restored speed not yet taken into the store is mixed in by momentum.</summary>
  public float Speed {
    get {
      float own = Store.GroupCount > 0 ? Store.ReflectedInertia(0) : 0f;
      float live = Store.GroupCount > 0 ? Store.Speed(0) : 0f;
      var (pendingInertia, pendingMomentum) = PendingTotals();
      float total = own + pendingInertia;
      return total > 0f ? (own * live + pendingMomentum) / total : live;
    }
  }

  private (float Inertia, float Momentum) PendingTotals() {
    float inertia = _pendingInertia;
    float momentum = _pendingInertia * _pendingSpeed;
    foreach (var run in _pending.Values) {
      inertia += run.Inertia;
      momentum += run.Inertia * run.Speed;
    }
    return (inertia, momentum);
  }

  public override void OnTick(
    IBlockAccessor world,
    float dt,
    BlockNetworkModSystem manager
  ) {
    double now = manager.ServerWorld?.Calendar?.TotalHours ?? -1d;
    float hours =
      now >= 0d && _lastHours >= 0d ? (float)(now - _lastHours) : 0f;
    _lastHours = now;
    if (dt <= 0f || NodeKeys.Any(k => world.GetChunkAtBlockPos(k.Pos) == null))
      return;
    Sync(world);
    foreach (var (key, node) in Store.Nodes) {
      var be = world.GetBlockEntity(key.Pos);
      float inertia = (be as IMpEnergyStorage)?.Inertia ?? 0f;
      float rise = inertia - Store.InertiaOf(node);
      if (rise > 0f) {
        float n = Store.RatioOf(node);
        Store.Mix(Store.GroupOf(node), rise * n * n, 0f);
      }
      Store.SetInertia(node, inertia);
      float w = Store.NodeSpeed(node);
      Store.SetStanding(
        node,
        ((be as IMpEnergySegment)?.StandingNm ?? 0f)
          + ((be as IMpEnergyConsumer)?.StandingNm ?? 0f)
      );
      Store.SetRating(node, (be as IMpEnergySegment)?.RatingNm ?? 0f);
      var producer = be as IMpEnergyProducer;
      Store.SetDrive(
        node,
        producer?.DriveTorque(w) ?? 0f,
        producer?.DriveSlope(w) ?? 0f
      );
      var consumer = be as IMpEnergyConsumer;
      Store.SetLoad(
        node,
        consumer?.LoadTorque(w) ?? 0f,
        consumer?.LoadSlope(w) ?? 0f
      );
    }
    Store.Step(dt, float.PositiveInfinity);
    Store.SolveTorquePath();
    AccrueStrain(world, MathF.Max(0f, hours));
    _clockS += dt;
    foreach (var (key, node) in Store.Nodes) {
      if (world.GetBlockEntity(key.Pos) is not IMpEnergySegment segment)
        continue;
      var reading = new MpSegmentReading(
        Store.NodeTorque(node),
        segment.RatingNm,
        segment.Strain,
        Store.NodeSpeed(node),
        segment.Size
      );
      if (_gate.TryAdmit(key, reading, _clockS))
        segment.StoreReading(reading);
    }
    State = new MpShaftRunState(
      Store.GroupCount > 0 ? Store.Speed(0) : 0f,
      Store.GroupCount > 0 ? Store.ReflectedInertia(0) : 0f
    );
    BroadcastUpdate(world);
  }

  private void AccrueStrain(IBlockAccessor world, float hours) {
    bool failure = FailureOverride ?? ExlibValues.MpShaftFailure;
    float tau = ExlibValues.MpStrainTauHours;
    List<Action>? failed = null;
    foreach (var (key, node) in Store.Nodes) {
      var be = world.GetBlockEntity(key.Pos);
      if (be is IMpEnergySegment segment) {
        float over = Store.NodeLoad(node) - 1f;
        if (over > 0f)
          segment.Strain = MathF.Min(1f, segment.Strain + over * hours / tau);
        float ultimate = segment.UltimateNm;
        if (ultimate > 0f && Store.NodeTorque(node) >= ultimate)
          segment.Strain = 1f;
        if (failure && segment.Strain >= 1f)
          (failed ??= []).Add(segment.Part);
      }
      if (be is IMpEnergyRim { RimLimitRadS: > 0f } rim) {
        float over = MathF.Abs(Store.NodeSpeed(node)) / rim.RimLimitRadS - 1f;
        if (over > 0f)
          rim.RimStrain = MathF.Min(1f, rim.RimStrain + over * hours / tau);
        if (failure && rim.RimStrain >= 1f)
          (failed ??= []).Add(rim.Burst);
      }
    }
    if (failed != null)
      foreach (var fail in failed)
        fail();
  }

  // Nodes entering the run bring their inertia in at rest unless a restore stands for it, and the restored speeds join by momentum, before the nodes do.
  private void Sync(IBlockAccessor world) {
    if (Store.GroupCount == 0)
      Store.AddGroup();
    var entering = new List<(NodeKey Key, float Inertia)>();
    float enteringInertia = 0f;
    float restoredInertia = _pendingInertia;
    foreach (var key in NodeKeys) {
      if (Store.Nodes.ContainsKey(key))
        continue;
      float inertia =
        (world.GetBlockEntity(key.Pos) as IMpEnergyStorage)?.Inertia ?? 0f;
      entering.Add((key, inertia));
      enteringInertia += inertia;
      if (_pending.TryGetValue(key, out var run))
        restoredInertia += run.Inertia;
    }
    var (pendingInertia, pendingMomentum) = PendingTotals();
    float mixed =
      pendingInertia + MathF.Max(0f, enteringInertia - restoredInertia);
    if (mixed > 0f)
      Store.Mix(0, mixed, pendingMomentum / mixed);
    _pendingInertia = 0f;
    _pendingSpeed = 0f;
    _pending.Clear();
    foreach (var (key, inertia) in entering)
      Store.AddNode(key, 0, inertia, 0f);
  }

  public override void OnTopologyChanged() {
    _gate.Retain(NodeKeys);
    foreach (
      var key in _pending.Keys.Where(k => !NodeKeys.Contains(k)).ToList()
    )
      _pending.Remove(key);
    if (Store.Nodes.Keys.All(NodeKeys.Contains))
      return;
    var kept = new MpShaftStore();
    kept.Take(Store, NodeKeys);
    Store = kept;
  }

  public override void OnMerge(BlockNetwork other, IBlockAccessor world) {
    var run = (MpShaftNetwork)other;
    Store.Absorb(run.Store);
    RestoreState(new MpShaftRunState(run._pendingSpeed, run._pendingInertia));
    foreach (var (key, saved) in run._pending)
      RestoreState(key, saved);
  }

  public override void OnSplitFragment(
    BlockNetwork original,
    IBlockAccessor world
  ) => throw new NotSupportedException("An mpshaft run splits by node keys.");

  public override void OnSplitFragment(
    BlockNetwork original,
    IReadOnlySet<NodeKey> fragmentKeys
  ) {
    var run = (MpShaftNetwork)original;
    Store.Take(run.Store, fragmentKeys);
    foreach (var (key, saved) in run._pending)
      if (fragmentKeys.Contains(key))
        _pending[key] = saved;
    if (run._pendingInertia > 0f) {
      _pendingInertia = run._pendingInertia;
      _pendingSpeed = run._pendingSpeed;
      run._pendingInertia = 0f;
      run._pendingSpeed = 0f;
    }
  }

  public override void InheritStateFrom(BlockNetwork source) {
    if (source is not MpShaftNetwork run)
      return;
    Store = run.Store;
    foreach (var (key, saved) in run._pending)
      RestoreState(key, saved);
    RestoreState(new MpShaftRunState(run._pendingSpeed, run._pendingInertia));
  }

  /// <summary>Takes a saved run into this one by momentum: with a restore already waiting, or with the nodes already in the store, the speed becomes the mean weighted by inertia. Anything but an <see cref="MpShaftRunState"/> is ignored. The restore belongs to no node, so a split hands it whole to the first fragment taken.</summary>
  public override void RestoreState(object? state) {
    if (state is not MpShaftRunState run || run.Inertia <= 0f)
      return;
    float total = _pendingInertia + run.Inertia;
    _pendingSpeed =
      (_pendingInertia * _pendingSpeed + run.Inertia * run.Speed) / total;
    _pendingInertia = total;
  }

  /// <summary>Takes a saved run into this one on behalf of node <paramref name="key"/>, by momentum with any restore already waiting for that node. A split hands it to the fragment holding the node, and removing the node drops it.</summary>
  /// <param name="key">The flywheel node whose block entity saved the run.</param>
  /// <param name="run">The saved run; ignored when its inertia is zero or less.</param>
  public void RestoreState(NodeKey key, MpShaftRunState run) {
    if (run.Inertia <= 0f)
      return;
    if (_pending.TryGetValue(key, out var waiting)) {
      float total = waiting.Inertia + run.Inertia;
      run = new MpShaftRunState(
        (waiting.Inertia * waiting.Speed + run.Inertia * run.Speed) / total,
        total
      );
    }
    _pending[key] = run;
  }

  /// <summary>Writes the run as <paramref name="ownInertia"/> of it, for a flywheel's block entity to save.</summary>
  /// <param name="host">The flywheel's tree.</param>
  /// <param name="ownInertia">The flywheel's inertia in kg*m^2.</param>
  public void SaveRun(ITreeAttribute host, float ownInertia) =>
    MpShaftSave.WriteRun(host, new MpShaftRunState(Speed, ownInertia));

  /// <summary>Takes the run saved in <paramref name="host"/> into this one for node <paramref name="key"/> by <see cref="RestoreState(NodeKey, MpShaftRunState)"/>.</summary>
  /// <param name="key">The flywheel node whose block entity holds <paramref name="host"/>.</param>
  /// <param name="host">The flywheel's tree.</param>
  /// <returns>False when the tree holds no readable run.</returns>
  public bool TryRestoreRun(NodeKey key, ITreeAttribute host) {
    if (!MpShaftSave.TryReadRun(host, out var run))
      return false;
    RestoreState(key, run);
    return true;
  }
}
