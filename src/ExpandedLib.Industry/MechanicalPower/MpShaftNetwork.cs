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
  private float _clockS;
  private readonly MpReadingGate _gate = new();

  /// <summary>The run's node store and joints.</summary>
  public MpShaftStore Store { get; private set; } = new();

  /// <summary>Reference speed of the run in rad/s, signed; a restored speed not yet taken into the store is mixed in by momentum.</summary>
  public float Speed {
    get {
      float own = Store.GroupCount > 0 ? Store.ReflectedInertia(0) : 0f;
      float live = Store.GroupCount > 0 ? Store.Speed(0) : 0f;
      float total = own + _pendingInertia;
      return total > 0f
        ? (own * live + _pendingInertia * _pendingSpeed) / total
        : live;
    }
  }

  public override void OnTick(
    IBlockAccessor world,
    float dt,
    BlockNetworkModSystem manager
  ) {
    if (dt <= 0f || NodeKeys.Any(k => world.GetChunkAtBlockPos(k.Pos) == null))
      return;
    Sync(world);
    foreach (var (key, node) in Store.Nodes) {
      var be = world.GetBlockEntity(key.Pos);
      float w = Store.NodeSpeed(node);
      Store.SetInertia(node, (be as IMpEnergyStorage)?.Inertia ?? 0f);
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

  // Takes a restored speed in by momentum before the nodes it stands for join the run.
  private void Sync(IBlockAccessor world) {
    if (Store.GroupCount == 0)
      Store.AddGroup();
    if (_pendingInertia > 0f) {
      Store.Mix(0, _pendingInertia, _pendingSpeed);
      _pendingInertia = 0f;
      _pendingSpeed = 0f;
    }
    foreach (var key in NodeKeys)
      if (!Store.Nodes.ContainsKey(key))
        Store.AddNode(key, 0, 0f, 0f);
  }

  public override void OnTopologyChanged() {
    if (Store.Nodes.Keys.All(NodeKeys.Contains))
      return;
    var kept = new MpShaftStore();
    kept.Take(Store, NodeKeys);
    Store = kept;
  }

  public override void OnMerge(BlockNetwork other, IBlockAccessor world) {
    var run = (MpShaftNetwork)other;
    Store.Absorb(run.Store);
    if (run._pendingInertia > 0f)
      RestoreState(new MpShaftRunState(run._pendingSpeed, run._pendingInertia));
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
    _pendingInertia = run._pendingInertia;
    _pendingSpeed = run._pendingSpeed;
  }

  public override void InheritStateFrom(BlockNetwork source) {
    if (source is MpShaftNetwork run)
      Store = run.Store;
  }

  /// <summary>Takes a saved run into this one by momentum: with a restore already waiting, or with the nodes already in the store, the speed becomes the mean weighted by inertia. Anything but an <see cref="MpShaftRunState"/> is ignored.</summary>
  public override void RestoreState(object? state) {
    if (state is not MpShaftRunState run || run.Inertia <= 0f)
      return;
    float total = _pendingInertia + run.Inertia;
    _pendingSpeed =
      (_pendingInertia * _pendingSpeed + run.Inertia * run.Speed) / total;
    _pendingInertia = total;
  }

  /// <summary>Writes the run as <paramref name="ownInertia"/> of it, for a flywheel's block entity to save.</summary>
  /// <param name="host">The flywheel's tree.</param>
  /// <param name="ownInertia">The flywheel's inertia in kg*m^2.</param>
  public void SaveRun(ITreeAttribute host, float ownInertia) =>
    MpShaftSave.WriteRun(host, new MpShaftRunState(Speed, ownInertia));

  /// <summary>Takes the run saved in <paramref name="host"/> into this one by <see cref="RestoreState"/>.</summary>
  /// <returns>False when the tree holds no readable run.</returns>
  public bool TryRestoreRun(ITreeAttribute host) {
    if (!MpShaftSave.TryReadRun(host, out var run))
      return false;
    RestoreState(run);
    return true;
  }
}
