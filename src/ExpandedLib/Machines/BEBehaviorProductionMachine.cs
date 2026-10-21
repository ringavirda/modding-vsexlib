using System;
using ExpandedLib.FastForwarding;
using ExpandedLib.Helpers;
using ExpandedLib.Networks;
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
    if (api.Side == EnumAppSide.Server) {
      JoinGroup();
      if (AutoStartProduction)
        StartProductionTick();
    }
  }

  /// <summary>Registers the production tick (idempotent, server-side only).</summary>
  public void StartProductionTick() {
    if (_productionTickId == 0 && Blockentity.Api?.Side == EnumAppSide.Server) {
      _productionTickId = Blockentity.RegisterGameTickListener(
        RunProductionTick,
        ProductionTickMs
      );
      if (!_stampRestored && _member == null)
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

  #region Game time

  // Calendar time (game hours) of the last simulated tick; -1 on a fresh machine. A Replay machine's cursor lives in its fast-forward member once registered.
  private double _lastTickHours = -1;
  private bool _stampRestored;
  private FastForwardMember? _member;
  private FastForwardDriver? _driver;
  private readonly FastForwardRecord _record = new();

  /// <summary>Calendar time, in game hours, the machine is simulated up to: its group's replay cursor on a <see cref="FastForwardMode.Replay"/> machine, the time of its last tick on an Off machine, or -1 before the first.</summary>
  public double LastTickHours => _member?.CursorHours ?? _lastTickHours;

  /// <summary>Sets <paramref name="hours"/> as the calendar time the machine was simulated up to, as read back from a save; a Replay machine starts its cursor there when it initialises. Negative means none.</summary>
  public void RestoreLastTickHours(double hours) {
    _lastTickHours = hours;
    _stampRestored = hours >= 0;
  }

  /// <summary>How many bounded sub-ticks a machine replays to catch up the game time it spent unloaded. Read by nothing: the group's fast-forward replays that time.</summary>
  [Obsolete(
    "Away catch-up is replaced by the fast-forward replay; this is never read."
  )]
  protected virtual int MaxAwayCatchupSteps => 0;

  /// <summary>Sub-tick length, in seconds, used while catching up. Read by nothing: the group's fast-forward replays that time.</summary>
  [Obsolete(
    "Away catch-up is replaced by the fast-forward replay; this is never read."
  )]
  protected virtual float AwayCatchupStepSeconds => ProductionTickMs / 1000f;

  /// <summary>Whether the machine is a member of its fast-forward group, default the block's <c>fastForward</c> attribute (<c>"replay"</c>, or <c>"off"</c>). A Replay machine's live tick stands aside while its group replays; an Off machine keeps the span of calendar time since its previous tick.</summary>
  protected virtual FastForwardMode FastForwardMode =>
    FastForwardModes.Of(Blockentity.Block);

  /// <summary>Calendar time, in game hours, the tick now running covers. On a Replay machine the call's <c>dt</c> over 120, live and in a replay. On an Off machine the hours since the previous tick, at most <see cref="MaxGameHoursPerTick"/>; a tick split by <see cref="GameStepHours"/> shows each call's share. 0 on an Off machine's first tick, when the calendar moved back, and outside a tick.</summary>
  public double TickGameHours { get; private set; }

  /// <summary>Longest slice, in game hours, one <see cref="OnProductionTick"/> call is handed on an Off machine; a longer span runs as several calls. 0, the default, hands the whole span to one call. Not read on a Replay machine.</summary>
  protected virtual double GameStepHours => 0;

  /// <summary>Most game hours one tick hands on an Off machine, default 24; the rest of a longer span is dropped and the stamp moves to now. Not read on a Replay machine.</summary>
  protected virtual double MaxGameHoursPerTick => 24;

  private void StampNow() {
    if (Blockentity.Api?.World != null)
      _lastTickHours = GameTime.NowHours(Blockentity.Api.World);
  }

  private void JoinGroup() {
    if (
      FastForwardMode != FastForwardMode.Replay
      || Blockentity.Api?.ModLoader?.GetModSystem<BlockNetworkModSystem>()
        is not { } networks
    )
      return;
    _driver = networks.FastForward;
    _member = _driver.Register(
      Blockentity.Pos,
      _lastTickHours >= 0 ? _lastTickHours : null,
      step: RunMemberTick,
      intervalMs: ProductionTickMs,
      ticking: () => _productionTickId != 0,
      couplings: _record.Ports,
      peers: _record.Peers
    );
  }

  /// <summary>Writes the machine's coupling positions and group peers into <paramref name="tree"/> as <c>ff_ports</c> and <c>ff_peers</c>; a machine outside a group writes what it last held.</summary>
  internal void SaveFastForward(ITreeAttribute tree) {
    if (_driver != null && _member != null)
      _record.Capture(_driver, _member);
    _record.Write(tree);
  }

  /// <summary>Takes <c>ff_ports</c> and <c>ff_peers</c> from <paramref name="tree"/> for the machine to join its group with when it initialises.</summary>
  internal void RestoreFastForward(ITreeAttribute tree) => _record.Read(tree);

  private void LeaveGroup(bool removed) {
    if (_member == null || _driver == null)
      return;
    if (removed)
      _driver.Remove(Blockentity.Pos);
    else {
      _record.Capture(_driver, _member);
      _lastTickHours = _member.CursorHours;
      _driver.SetPresent(Blockentity.Pos, false);
    }
    _member = null;
    _driver = null;
  }

  private void RunProductionTick(float dt) {
    _stampRestored = false;
    if (_member == null || _driver == null) {
      RunSpan(dt, SpanSinceStamp());
      return;
    }
    if (_driver.IsHeld(Blockentity.Pos))
      return;
    using (FastForward.Step(_driver, Blockentity.Pos))
      RunMemberTick(dt);
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

  private void RunMemberTick(float dt) {
    dt = ClampTick(dt);
    TickGameHours = dt / GameTime.MachineSecondsPerHour;
    try {
      RunGated(dt);
    } finally {
      TickGameHours = 0;
    }
  }

  private float ClampTick(float dt) =>
    GameMath.Min(dt, ProductionTickMs / 1000f * MaxCatchupTickMultiple);

  private void RunOneTick(float dt) => RunGated(ClampTick(dt));

  private void RunGated(float dt) {
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

  /// <summary>Runs one production tick as the registered listener would, including the game-hour span or the Replay machine's <c>dt</c> hours, the stamp, the <see cref="CanRunProduction"/> gate and the catch-up <c>dt</c> clamp. Never moves the calendar, checks no hold and runs no replay.</summary>
  internal void DriveProductionTick(float dt) {
    if (_member != null)
      RunMemberTick(dt);
    else
      RunSpan(dt, SpanSinceStamp());
  }

  /// <summary>Runs <see cref="OnIdleProductionTick"/> directly, bypassing <see cref="CanRunProduction"/>.</summary>
  internal void DriveIdleTick(float dt) => OnIdleProductionTick(dt);

  /// <summary>Forgets the tick handle so a later start registers a fresh listener.</summary>
  public override void OnBlockRemoved() {
    base.OnBlockRemoved();
    StopProductionTick();
    LeaveGroup(removed: true);
  }

  public override void OnBlockUnloaded() {
    base.OnBlockUnloaded();
    StopProductionTick();
    LeaveGroup(removed: false);
  }
}
