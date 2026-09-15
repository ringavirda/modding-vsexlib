using ExpandedLib.Helpers;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Machines;

/// <summary>The periodic work a machine does: a server-side tick on a fixed interval, gated each time by <see cref="CanRunProduction"/>.</summary>
public abstract class BEBehaviorProductionMachine(BlockEntity blockentity)
  : BlockEntityBehavior(blockentity) {
  // Tick listener handle; 0 means no tick is registered.
  private long _productionTickId;

  /// <summary>Interval, in milliseconds, of the production tick.</summary>
  protected virtual int ProductionTickMs => 1000;

  /// <summary>Whether the machine is in an operational state this tick; false routes the tick to <see cref="OnIdleProductionTick"/>.</summary>
  protected virtual bool CanRunProduction =>
    ProductionReadiness.IsReady(Blockentity);

  /// <summary>Whether <see cref="Initialize"/> should register the production tick immediately; default true.</summary>
  protected virtual bool AutoStartProduction => true;

  public override void Initialize(ICoreAPI api, JsonObject properties) {
    base.Initialize(api, properties);
    if (api.Side == EnumAppSide.Server && AutoStartProduction)
      StartProductionTick();
  }

  /// <summary>Registers the production tick (idempotent, server-side only).</summary>
  public void StartProductionTick() {
    if (_productionTickId == 0 && Blockentity.Api?.Side == EnumAppSide.Server)
      _productionTickId = Blockentity.RegisterGameTickListener(
        RunProductionTick,
        ProductionTickMs
      );
  }

  /// <summary>Unregisters the production tick.</summary>
  public void StopProductionTick() {
    if (_productionTickId != 0) {
      Blockentity.UnregisterGameTickListener(_productionTickId);
      _productionTickId = 0;
    }
  }

  // Upper bound on a single production dt, as a multiple of the tick interval.
  private const float MaxCatchupTickMultiple = 2f;

  #region Away catch-up (game time)

  // Calendar time (game hours) of the last simulated tick; -1 on a fresh machine.
  private double _lastTickHours = -1;
  private bool _pendingCatchup;

  /// <summary>Calendar time, in game hours, of the last simulated tick, or -1 before the first one.</summary>
  public double LastTickHours => _lastTickHours;

  /// <summary>Sets <paramref name="hours"/> as the calendar time of the last simulated tick, as read back from a save.</summary>
  public void RestoreLastTickHours(double hours) {
    _lastTickHours = hours;
    _pendingCatchup = hours >= 0;
  }

  /// <summary>How many bounded sub-ticks a machine replays to catch up the game time it spent unloaded; default 0 disables away-catch-up.</summary>
  protected virtual int MaxAwayCatchupSteps => 0;

  /// <summary>Sub-tick length, in seconds, used while catching up; defaults to one normal tick.</summary>
  protected virtual float AwayCatchupStepSeconds => ProductionTickMs / 1000f;

  private void RunProductionTick(float dt) {
    // Replays the unloaded game-time gap ahead of this real tick.
    if (_pendingCatchup) {
      _pendingCatchup = false;
      RunAwayCatchup();
    }

    RunOneTick(dt);
    if (Blockentity.Api?.World != null)
      _lastTickHours = Blockentity.Api.World.Calendar.TotalHours;
  }

  private void RunAwayCatchup() {
    if (
      MaxAwayCatchupSteps <= 0
      || _lastTickHours < 0
      || Blockentity.Api?.World == null
    )
      return;

    double away = GameTime.SecondsBetween(
      _lastTickHours,
      Blockentity.Api.World.Calendar.TotalHours
    );
    GameTime.CatchUp(
      away,
      AwayCatchupStepSeconds,
      MaxAwayCatchupSteps,
      RunOneTick
    );
  }

  private void RunOneTick(float dt) {
    dt = GameMath.Min(dt, ProductionTickMs / 1000f * MaxCatchupTickMultiple);
    if (CanRunProduction)
      OnProductionTick(dt);
    else
      OnIdleProductionTick(dt);
  }

  #endregion

  /// <summary>Per-tick production logic; runs server-side only while <see cref="CanRunProduction"/>.</summary>
  protected abstract void OnProductionTick(float dt);

  /// <summary>Runs in place of <see cref="OnProductionTick"/> while the machine is not operational. Default: no-op.</summary>
  protected virtual void OnIdleProductionTick(float dt) { }

  /// <summary>Runs one production tick exactly as the registered listener would, including the <see cref="CanRunProduction"/> gate and the catch-up <c>dt</c> clamp.</summary>
  internal void DriveProductionTick(float dt) => RunOneTick(dt);

  /// <summary>Runs <see cref="OnIdleProductionTick"/> directly, bypassing <see cref="CanRunProduction"/>.</summary>
  internal void DriveIdleTick(float dt) => OnIdleProductionTick(dt);

  /// <summary>Forgets the tick handle so a later start registers a fresh listener.</summary>
  public override void OnBlockRemoved() {
    base.OnBlockRemoved();
    StopProductionTick();
  }

  public override void OnBlockUnloaded() {
    base.OnBlockUnloaded();
    StopProductionTick();
  }
}
