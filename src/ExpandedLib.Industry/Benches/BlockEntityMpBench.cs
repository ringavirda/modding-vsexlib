using System;
using ExpandedLib.Blocks;
using ExpandedLib.Industry.MechanicalPower;
using ExpandedLib.Machines;
using ExpandedLib.Networks;
using Vintagestory.API.Common;

namespace ExpandedLib.Industry.Benches;

/// <summary>
/// What every mechanical-energy bench has in common: a place on the mpshaft
/// run, a stroke clock, and reads that turn the live run into a speed and a torque. A stroke is
/// one bite: something goes under the tooling, the clock runs it down, and the products leave.
/// </summary>
public abstract class BlockEntityMpBench
  : BlockEntityMachineStation,
    IMpEnergyConsumer,
    IProductionReadiness {
  private readonly HostMembership _membership;

  protected BlockEntityMpBench() {
    // Added in the constructor: BlockEntity fans FromTreeAttributes and Initialize out over Behaviors.
    _membership = new HostMembership(this);
    Behaviors.Add(_membership);
    Behaviors.Add(new HostProcess(this));
  }

  /// <summary>The network manager this bench is registered with; the setter writes through for test injection.</summary>
  public BlockNetworkModSystem? NetworkSystem {
    get => _membership.NetworkSystem;
    protected set => _membership.NetworkSystem = value;
  }

  /// <summary>The bench's place on the mpshaft run; carries no state, reads the live network each tick.</summary>
  private sealed class HostMembership(BlockEntityMpBench owner)
    : BEBehaviorNetworkMember(owner) {
    public override string NetworkType {
      get => "mpshaft";
      protected set { }
    }
  }

  // The stroke advances on its own 250 ms clock, not the network's 1 s tick.
  private const int StrokeTickMs = 250;

  /// <summary>The stroke clock; reads the gate off the block entity each tick.</summary>
  // No away-catch-up: a stroke needs the run turning; nothing to replay after an absence.
  private sealed class HostProcess(BlockEntityMpBench owner)
    : BEBehaviorProductionMachine(owner) {
    protected override int ProductionTickMs => StrokeTickMs;

    protected override void OnProductionTick(float dt) =>
      owner.AdvanceStroke(dt);
  }

  #region Readiness

  /// <summary>Whether the bench has work this tick. A stroke is the only thing its clock advances.</summary>
  public bool IsReadyToProduce => IsStroking;

  /// <summary>Always <c>false</c>: an idle bench keeps its clock regardless.</summary>
  public bool StopsProductionWhenNotReady => false;

  #endregion

  #region The stroke

  // Seconds of stroke left; 0 means idle, persisted across reload.
  // The legacy key is the shear's original field name, kept as a fallback.
  [Persist(RemainingKey, Legacy = LegacyShearKey)]
  private float _remaining;

  /// <summary>Whether a stroke is under way.</summary>
  public bool IsStroking => _remaining > 0f;

  /// <summary>Seconds of stroke left, for the block info readout.</summary>
  public float Remaining => _remaining;

  /// <summary>Starts a stroke of <paramref name="seconds"/>; only arms the clock.</summary>
  protected void BeginStroke(float seconds) => _remaining = seconds;

  /// <summary>Abandons the stroke without completing it, leaving whatever is under the tooling to the
  /// caller.</summary>
  protected void AbandonStroke() => _remaining = 0f;

  /// <summary>Advances the stroke by <paramref name="dt"/> seconds; a stopped run holds the tooling in place.</summary>
  /// <returns>True when the stroke completed on this advance.</returns>
  public bool AdvanceStroke(float dt) {
    if (!IsStroking || Speed <= 0f)
      return false;

    _remaining -= dt;
    if (_remaining > 0f) {
      MarkDirty();
      return false;
    }
    _remaining = 0f;
    CompleteStroke();
    return true;
  }

  /// <summary>What the bench does when the clock runs out. Called with the stroke already cleared.</summary>
  protected abstract void CompleteStroke();

  #endregion

  #region Handing things back

  /// <summary>The stack a job's output code names, or null when it names nothing.</summary>
  protected ItemStack? Resolve(string code, int quantity) {
    var location = new AssetLocation(code);
    Item? item = Api!.World.GetItem(location);
    if (item != null)
      return new ItemStack(item, quantity);

    Block? block = Api.World.GetBlock(location);
    if (block != null)
      return new ItemStack(block, quantity);

    Api.Logger.Warning(
      "[exlib] {0} job output '{1}' names no item or block; the stroke produced nothing.",
      Block?.Code?.Path ?? "bench",
      code
    );
    return null;
  }

  /// <summary>Drops <paramref name="stack"/> at the bench.</summary>
  protected void Eject(ItemStack? stack) {
    if (stack == null || Api?.World == null || Pos == null)
      return;
    Api.World.SpawnItemEntity(stack, Pos.ToVec3d().Add(0.5, 1.0, 0.5));
  }

  #endregion

  #region Network

  private MpRunReading Run => _membership.ReadRun();

  /// <summary>Shaft speed of the run (rad/s) this tick, whichever way it turns; zero means at rest.</summary>
  protected float Speed => MathF.Abs(Run.SpeedRadS);

  /// <summary>N*m in one unit of a job's <c>minTorque</c>; override to scale the unit.</summary>
  protected virtual float JobTorqueNm => 1000f;

  /// <summary>Torque the rest of the run can still deliver to the bench, in the unit of a job's
  /// <c>minTorque</c> (<see cref="JobTorqueNm"/> N*m); zero when the run is stopped.</summary>
  protected float AvailableTorque =>
    Speed > 0f ? Run.AvailableTorqueNm / JobTorqueNm : 0f;

  /// <summary>Standing torque (N*m) of the bench's input shaft and fast pulley on the run.</summary>
  public abstract float StandingNm { get; }

  /// <summary>What the bench draws off the run: its stroke's demand while working, nothing while idle.</summary>
  public abstract float LoadTorque(float speed);

  #endregion

  #region Persistence

  private const string RemainingKey = "benchRemaining";
  private const string LegacyShearKey = "shearRemaining";

  #endregion
}
