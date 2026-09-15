using System.Collections.Generic;
using ExpandedLib.Blocks;
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

  /// <summary>How many bounded sub-ticks a machine replays to catch up game time spent unloaded,
  /// capping the replay at <c>MaxAwayCatchupSteps x AwayCatchupStepSeconds</c> seconds; <c>0</c>
  /// disables away-catch-up.</summary>
  protected virtual int MaxAwayCatchupSteps => 0;

  /// <summary>Sub-tick length (seconds) used while catching up; defaults to one normal tick.</summary>
  protected virtual float AwayCatchupStepSeconds => ProductionTickMs / 1000f;

  // The process holds the last-tick stamp; this class persists it into the shared flat tree.
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
