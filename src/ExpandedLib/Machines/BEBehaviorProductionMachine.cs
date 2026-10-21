using System;
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
    if (_productionTickId == 0 && Blockentity.Api?.Side == EnumAppSide.Server) {
      _productionTickId = Blockentity.RegisterGameTickListener(
        RunProductionTick,
        ProductionTickMs
      );
      if (!_pendingCatchup)
        StampNow();
    }
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

  /// <summary>Calendar time, in game hours, the tick now running covers: the hours since the previous tick, at most <see cref="MaxGameHoursPerTick"/>. 0 on a machine's first tick, when the calendar moved back, and outside a tick; a tick split by <see cref="GameStepHours"/> shows each call's share.</summary>
  public double TickGameHours { get; private set; }

  /// <summary>Longest slice, in game hours, one <see cref="OnProductionTick"/> call is handed; a longer span runs as several calls. 0, the default, hands the whole span to one call.</summary>
  protected virtual double GameStepHours => 0;

  /// <summary>Most game hours one tick hands on, default 24; the rest of a longer span is dropped and the stamp moves to now.</summary>
  protected virtual double MaxGameHoursPerTick => 24;

  private void StampNow() {
    if (Blockentity.Api?.World != null)
      _lastTickHours = GameTime.NowHours(Blockentity.Api.World);
  }

  private void RunProductionTick(float dt) {
    double span = SpanSinceStamp();

    // Replays the unloaded game-time gap ahead of this real tick.
    if (_pendingCatchup) {
      _pendingCatchup = false;
      TickGameHours = 0;
      RunAwayCatchup();
    }

    RunSpan(dt, span);
  }

  private double SpanSinceStamp() =>
    _lastTickHours < 0 || Blockentity.Api?.World == null
      ? 0
      : Math.Max(0, GameTime.NowHours(Blockentity.Api.World) - _lastTickHours);

  private void RunSpan(float dt, double span) {
    span = Math.Min(span, MaxGameHoursPerTick);
    double step = GameStepHours;
    try {
      if (span > 0 && step > 0) {
        bool first = true;
        GameTime.CatchUp(
          span,
          (float)step,
          (int)Math.Min(Math.Ceiling(span / step) + 1, int.MaxValue),
          hours => {
            TickGameHours = hours;
            RunOneTick(first ? dt : 0f);
            first = false;
          }
        );
      } else {
        TickGameHours = span;
        RunOneTick(dt);
      }
    } finally {
      TickGameHours = 0;
    }
    StampNow();
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
      GameTime.NowHours(Blockentity.Api.World)
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

  /// <summary>Runs one production tick as the registered listener would, including the game-hour span, its steps, the stamp, the <see cref="CanRunProduction"/> gate and the catch-up <c>dt</c> clamp. Never moves the calendar and runs no pending away replay.</summary>
  internal void DriveProductionTick(float dt) => RunSpan(dt, SpanSinceStamp());

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
