using System;
using System.Collections.Generic;
using ExpandedLib.Blocks;
using ExpandedLib.FastForwarding;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace ExpandedLib.Machines;

/// <summary>
/// Base for a block entity whose sole purpose is periodic server-side production work. A machine
/// that is also something else hosts a <see cref="BEBehaviorProductionMachine"/> of its own instead.
/// </summary>
public abstract class BlockEntityProductionMachine
  : BlockEntity,
    IProductionReadiness {
  private readonly HostProcess _process;
  private ExBlockState? _state;

  /// <summary>This machine's declared fields, built on first use.</summary>
  protected ExBlockState Persisted =>
    BlockEntityStateHost.GetOrCreate(this, ref _state, DeclareState);

  /// <summary>Declares the fields this machine persists beyond the away-catch-up stamp above; called
  /// once, lazily.</summary>
  protected virtual void DeclareState(ExBlockState state) { }

  protected BlockEntityProductionMachine() {
    // Must be added here: Behaviors fans out both FromTreeAttributes and Initialize.
    _process = new HostProcess(this);
    Behaviors.Add(_process);
  }

  /// <summary>The machine's production process; reads its answers from the owning block entity.</summary>
  private sealed class HostProcess(BlockEntityProductionMachine owner)
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

  /// <summary>Whether the machine is operational this tick; <c>false</c> routes the tick to
  /// <see cref="OnIdleProductionTick"/>, and every readiness publisher must agree.</summary>
  protected abstract bool CanRunProduction { get; }

  /// <summary>This machine's own readiness answer, not overridable; a subclass states its gate in
  /// <see cref="CanRunProduction"/>.</summary>
  public bool IsReadyToProduce => CanRunProduction;

  /// <summary>Whether losing readiness also unregisters the production tick; override this, not
  /// <see cref="CanRunProduction"/>, to keep running while un-ready.</summary>
  public virtual bool StopsProductionWhenNotReady => true;

  /// <summary>Whether the process registers the production tick as soon as the machine loads; false
  /// lets a subclass drive <see cref="StartProductionTick"/> and <see cref="StopProductionTick"/>
  /// itself.</summary>
  protected virtual bool AutoStartProduction => true;

  /// <summary>Registers the production tick (idempotent, server-side only).</summary>
  protected void StartProductionTick() => _process.StartProductionTick();

  /// <summary>Unregisters the production tick.</summary>
  protected void StopProductionTick() => _process.StopProductionTick();

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

  /// <summary>How many bounded sub-ticks a machine replays to catch up game time spent unloaded. Read by
  /// nothing: the group's fast-forward replays that time.</summary>
  [Obsolete(
    "Away catch-up is replaced by the fast-forward replay; this is never read."
  )]
  protected virtual int MaxAwayCatchupSteps => 0;

  /// <summary>Sub-tick length (seconds) used while catching up. Read by nothing: the group's fast-forward
  /// replays that time.</summary>
  [Obsolete(
    "Away catch-up is replaced by the fast-forward replay; this is never read."
  )]
  protected virtual float AwayCatchupStepSeconds => ProductionTickMs / 1000f;

  // The process holds the cursor; this class persists it into the shared flat tree.
  public override void ToTreeAttributes(ITreeAttribute tree) {
    base.ToTreeAttributes(tree);
    tree.SetDouble("pm_lastHours", _process.LastTickHours);
    Persisted.ToTree(tree);
  }

  public override void FromTreeAttributes(
    ITreeAttribute tree,
    IWorldAccessor worldForResolving
  ) {
    base.FromTreeAttributes(tree, worldForResolving);
    _process.RestoreLastTickHours(tree.GetDouble("pm_lastHours", -1));
    Persisted.FromTree(tree, worldForResolving);
  }

  public override void OnStoreCollectibleMappings(
    Dictionary<int, AssetLocation> blockIdMapping,
    Dictionary<int, AssetLocation> itemIdMapping
  ) {
    base.OnStoreCollectibleMappings(blockIdMapping, itemIdMapping);
    Persisted.StoreCollectibleMappings(
      Api.World,
      blockIdMapping,
      itemIdMapping
    );
  }

  public override void OnLoadCollectibleMappings(
    IWorldAccessor worldForResolve,
    Dictionary<int, AssetLocation> oldBlockIdMapping,
    Dictionary<int, AssetLocation> oldItemIdMapping,
    int schematicSeed,
    bool resolveImports
  ) {
    base.OnLoadCollectibleMappings(
      worldForResolve,
      oldBlockIdMapping,
      oldItemIdMapping,
      schematicSeed,
      resolveImports
    );
    Persisted.LoadCollectibleMappings(
      worldForResolve,
      oldBlockIdMapping,
      oldItemIdMapping
    );
  }

  #endregion

  /// <summary>Per-tick production logic; runs server-side only while <see cref="CanRunProduction"/>.</summary>
  protected abstract void OnProductionTick(float dt);

  /// <summary>Runs in place of <see cref="OnProductionTick"/> while the machine is not operational.</summary>
  protected virtual void OnIdleProductionTick(float dt) { }

  /// <summary>Test seam: runs one production tick as the registered listener does, gate included.</summary>
  internal void DriveProductionTick(float dt) =>
    _process.DriveProductionTick(dt);

  /// <summary>Test seam: runs the idle-tick path directly, bypassing the readiness gate.</summary>
  internal void DriveIdleTick(float dt) => _process.DriveIdleTick(dt);
}
