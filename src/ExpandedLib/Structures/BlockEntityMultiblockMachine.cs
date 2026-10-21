using System;
using ExpandedLib.FastForwarding;
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

#pragma warning disable CS0618, CS0672
    protected override int MaxAwayCatchupSteps => owner.MaxAwayCatchupSteps;

    protected override float AwayCatchupStepSeconds =>
      owner.AwayCatchupStepSeconds;
#pragma warning restore CS0618, CS0672

    protected override FastForwardMode FastForwardMode => owner.FastForwardMode;

    protected override double GameStepHours => owner.GameStepHours;

    protected override double MaxGameHoursPerTick => owner.MaxGameHoursPerTick;

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

  /// <summary>Longest slice, in game hours, one <see cref="OnProductionTick"/> call is handed on an Off machine; a longer
  /// span runs as several calls, only the first with the real <c>dt</c>. <c>0</c>, the default, hands the whole span to
  /// one call. Not read on a Replay machine.</summary>
  protected virtual double GameStepHours => 0;

  /// <summary>Most game hours one tick hands on an Off machine, default <c>24</c>; the rest of a longer span is
  /// dropped. Not read on a Replay machine.</summary>
  protected virtual double MaxGameHoursPerTick => 24;

  /// <summary>Whether the machine is a member of its fast-forward group, default the block's <c>fastForward</c>
  /// attribute (<c>"replay"</c>, or <c>"off"</c>). A Replay machine's tick stands aside while its group replays the
  /// game time its real ticks did not cover; an Off machine keeps the span of calendar time since its previous
  /// tick.</summary>
  protected virtual FastForwardMode FastForwardMode =>
    FastForwardModes.Of(Block);

  /// <summary>Calendar time, in game hours, the running <see cref="OnProductionTick"/> call covers: the call's
  /// <c>dt</c> over 120 on a Replay machine, the hours since the previous tick on an Off machine (<c>0</c> on its first
  /// tick, when the calendar moved back, and outside a tick).</summary>
  protected double TickGameHours => _process.TickGameHours;

  /// <summary>Bounded sub-ticks replayed to catch up game time spent unloaded. Read by nothing: the group's
  /// fast-forward replays that time.</summary>
  [Obsolete(
    "Away catch-up is replaced by the fast-forward replay; this is never read."
  )]
  protected virtual int MaxAwayCatchupSteps => 0;

  /// <summary>Sub-tick length (seconds) used while catching up. Read by nothing: the group's fast-forward replays
  /// that time.</summary>
  [Obsolete(
    "Away catch-up is replaced by the fast-forward replay; this is never read."
  )]
  protected virtual float AwayCatchupStepSeconds => ProductionTickMs / 1000f;

  // Persisted here, not by the behavior, to avoid a key collision in the flat tree.
  public override void ToTreeAttributes(ITreeAttribute tree) {
    base.ToTreeAttributes(tree);
    tree.SetDouble("pm_lastHours", _process.LastTickHours);
    _process.SaveFastForward(tree);
  }

  public override void FromTreeAttributes(
    ITreeAttribute tree,
    IWorldAccessor worldForResolving
  ) {
    base.FromTreeAttributes(tree, worldForResolving);
    _process.RestoreLastTickHours(tree.GetDouble("pm_lastHours", -1));
    _process.RestoreFastForward(tree);
  }

  #endregion

  /// <summary>Per-tick production logic; runs server-side only while
  /// <see cref="BlockEntityMultiblockStructure.CanRunProduction"/>.</summary>
  protected abstract void OnProductionTick(float dt);

  /// <summary>Runs in place of <see cref="OnProductionTick"/> while the machine is not operational. Default: no-op.</summary>
  protected virtual void OnIdleProductionTick(float dt) { }
}
