using System;
using ExpandedLib.Networks;
using Vintagestory.API.Common;

namespace ExpandedLib.Industry.MechanicalPower;

/// <summary>Concrete <see cref="BlockNetwork"/> for the mechanical-energy system: one shared
/// reservoir that producers drive, storage nodes buffer, and consumers load.</summary>
public class MpEnergyNetwork : BlockNetwork {
  public override string NetworkType => "mpenergy";

  // FrictionCoeff is the windage coefficient, IdleTorque the standing-resistance floor, MaxSpeed
  // the burst speed w_max (rad/s).
  private static float FrictionCoeff => ExlibValues.MpFrictionCoeff;
  private static float IdleTorque => ExlibValues.MpIdleTorque;
  private static float MaxSpeed => ExlibValues.MpMaxSpeed;

  public MpEnergyNetwork(BlockNetworkModSystem system)
    : base(system) { }

  /// <summary>Live reservoir state, or <c>null</c> until a storage node ticks it. Backed by the base
  /// <see cref="BlockNetwork.State"/> so the typed accessor and base code share one object.</summary>
  public new MpEnergyNetworkState? State {
    get => base.State as MpEnergyNetworkState;
    private set => base.State = value;
  }

  public override void RestoreState(object? state) =>
    State = state as MpEnergyNetworkState;

  public override void InheritStateFrom(BlockNetwork source) {
    if (source is MpEnergyNetwork other)
      State = other.State;
  }

  #region Tick

  public override void OnTick(
    IBlockAccessor blockAccessor,
    float dt,
    BlockNetworkModSystem manager
  ) {
    // One walk of the node set: sums storage inertia, drive torque and load torque.
    float speed = State?.Speed ?? 0f;
    float inertia = 0f;
    float driveTorque = 0f;
    float loadTorque = 0f;
    bool reversed = false;

    foreach (var pos in Nodes) {
      var be = blockAccessor.GetBlockEntity(pos);
      if (be == null)
        continue;
      if (be is IMpEnergyStorage storage)
        inertia += Math.Max(0f, storage.Inertia);
      if (be is IMpEnergyProducer producer)
        driveTorque += Math.Max(0f, producer.DriveTorque(speed));
      if (be is IMpEnergyConsumer consumer)
        loadTorque += Math.Max(0f, consumer.LoadTorque(speed));
      // Any driver that knows its rotation sets the run's direction; the last one wins.
      if (be is IMpEnergyDirection { IsReversed: true })
        reversed = true;
    }

    // A run with no inertia has nothing to spin.
    if (inertia <= 0f) {
      if (State != null) {
        State = null;
        BroadcastUpdate(blockAccessor);
      }
      return;
    }

    State ??= new MpEnergyNetworkState();
    State.Inertia = inertia;
    State.Reversed = reversed;

    // Integrate the shaft: w += (T_drive - T_load - T_fric)/I * dt, clamped to [0, w_max].
    MpEnergyNetworkState.Step(
      State,
      dt,
      driveTorque,
      loadTorque,
      FrictionCoeff,
      IdleTorque,
      MaxSpeed
    );

    BroadcastUpdate(blockAccessor);
  }

  #endregion

  #region Merge / split

  public override void OnMerge(BlockNetwork other, IBlockAccessor world) {
    if (other is not MpEnergyNetwork o || o.State == null)
      return;
    if (State == null) {
      State = o.State;
      return;
    }
    // Pool the stored energy and inertia; the derived speed follows.
    State.Inertia += o.State.Inertia;
    State.StoredEnergy += o.State.StoredEnergy;
    float cap = MpEnergyNetworkState.CapacityFor(State.Inertia, MaxSpeed);
    if (State.StoredEnergy > cap)
      State.StoredEnergy = cap;
    State.Speed = MpEnergyNetworkState.DeriveSpeed(
      State.StoredEnergy,
      State.Inertia
    );
  }

  public override void OnSplitFragment(
    BlockNetwork original,
    IBlockAccessor world
  ) {
    if (
      original is not MpEnergyNetwork orig
      || orig.State == null
      || orig.State.Inertia <= 0f
    ) {
      State = null;
      return;
    }
    // Each fragment keeps a share of the stored energy proportional to its inertia.
    float fragInertia = FragmentInertia(world);
    if (fragInertia <= 0f) {
      State = null;
      return;
    }
    float share = orig.State.StoredEnergy * (fragInertia / orig.State.Inertia);
    State = new MpEnergyNetworkState {
      Inertia = fragInertia,
      StoredEnergy = share,
      Speed = MpEnergyNetworkState.DeriveSpeed(share, fragInertia),
    };
  }

  // Sums this fragment's storage inertia from its nodes.
  private float FragmentInertia(IBlockAccessor world) {
    float inertia = 0f;
    foreach (var pos in Nodes)
      if (world.GetBlockEntity(pos) is IMpEnergyStorage s)
        inertia += Math.Max(0f, s.Inertia);
    return inertia;
  }

  #endregion
}
