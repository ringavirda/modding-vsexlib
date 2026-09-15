using ExpandedLib.Machines;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace ExpandedLib.Structures;

/// <summary>
/// A <see cref="BlockEntityMultiblockStructure"/> that also runs a server-side production tick, hosted
/// via a <see cref="BEBehaviorProductionMachine"/>.
/// </summary>
public abstract class BlockEntityMultiblockMachine
  : BlockEntityMultiblockStructure {
  private readonly HostProcess _process;

  protected BlockEntityMultiblockMachine() {
    // Must run before FromTreeAttributes and Initialize read it.
    _process = new HostProcess(this);
    Behaviors.Add(_process);
  }

  /// <summary>The machine's production process; reads its answers from the owning block entity.</summary>
  private sealed class HostProcess(BlockEntityMultiblockMachine owner)
    : BEBehaviorProductionMachine(owner) {
    protected override int ProductionTickMs => owner.ProductionTickMs;

    protected override bool AutoStartProduction => owner.AutoStartProduction;

    protected override int MaxAwayCatchupSteps => owner.MaxAwayCatchupSteps;

    protected override float AwayCatchupStepSeconds =>
      owner.AwayCatchupStepSeconds;

    protected override void OnProductionTick(float dt) =>
      owner.OnProductionTick(dt);

    protected override void OnIdleProductionTick(float dt) =>
      owner.OnIdleProductionTick(dt);
  }

  /// <summary>Interval (ms) of the production tick.</summary>
  protected virtual int ProductionTickMs => 1000;

  /// <summary>Whether the production tick registers as soon as the machine loads.</summary>
  protected virtual bool AutoStartProduction => StructureComplete;

  #region Away catch-up (game time)

  /// <summary>Bounded sub-ticks replayed to catch up game time spent unloaded; 0 disables catch-up.</summary>
  protected virtual int MaxAwayCatchupSteps => 0;

  /// <summary>Sub-tick length (seconds) used while catching up.</summary>
  protected virtual float AwayCatchupStepSeconds => ProductionTickMs / 1000f;

  // Persisted here, not by the behavior, to avoid a key collision in the flat tree.
  public override void ToTreeAttributes(ITreeAttribute tree) {
    base.ToTreeAttributes(tree);
    tree.SetDouble("pm_lastHours", _process.LastTickHours);
  }

  public override void FromTreeAttributes(
    ITreeAttribute tree,
    IWorldAccessor worldForResolving
  ) {
    base.FromTreeAttributes(tree, worldForResolving);
    _process.RestoreLastTickHours(tree.GetDouble("pm_lastHours", -1));
  }

  #endregion

  /// <summary>Per-tick production logic; runs server-side only while
  /// <see cref="BlockEntityMultiblockStructure.CanRunProduction"/>.</summary>
  protected abstract void OnProductionTick(float dt);

  /// <summary>Runs in place of <see cref="OnProductionTick"/> while the machine is not operational. Default: no-op.</summary>
  protected virtual void OnIdleProductionTick(float dt) { }
}
