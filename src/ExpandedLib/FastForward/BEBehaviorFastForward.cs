using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Networks;
using ExpandedLib.Registries;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace ExpandedLib.FastForwarding;

/// <summary>Makes a block entity's server tick a member of its fast-forward group: the tick stands aside while the group replays or is frozen, and the replay steps it for the game time its real ticks did not cover. The member's cursor, coupling positions and peers are saved with the block entity as <c>ff_cursor</c>, <c>ff_ports</c> and <c>ff_peers</c>.</summary>
/// <remarks>Registered as <c>exlib.FastForward</c>. A block entity that registers its own tick adds this behaviour in its constructor with <see cref="BEBehaviorFastForward(BlockEntity, Action{float}, int)"/>, in place of <c>RegisterGameTickListener</c>. Declared in JSON, the behaviour takes its tick, interval and couplings from the <see cref="IFastForwardMember"/> the block entity or one of its behaviours implements. With neither it does nothing.</remarks>
[BlockEntityBehaviorRegister("FastForward")]
public class BEBehaviorFastForward : BlockEntityBehavior {
  private const string CursorKey = "ff_cursor";

  private readonly Action<float>? _tick;
  private readonly int _intervalMs;
  private readonly FastForwardRecord _record = new();
  private double _savedCursor = -1;
  private long _tickId;
  private FastForwardDriver? _driver;
  private FastForwardMember? _member;
  private Action<float>? _step;

  /// <summary>Declared in a block's JSON; the tick comes from an <see cref="IFastForwardMember"/> on the block entity.</summary>
  public BEBehaviorFastForward(BlockEntity blockentity)
    : base(blockentity) { }

  /// <summary>Added by a block entity that registers its own tick.</summary>
  /// <param name="blockentity">The owning block entity.</param>
  /// <param name="tick">The live tick; called with the seconds since its previous call.</param>
  /// <param name="intervalMs">The tick's interval in milliseconds, at least 1.</param>
  public BEBehaviorFastForward(
    BlockEntity blockentity,
    Action<float> tick,
    int intervalMs
  )
    : base(blockentity) {
    _tick = tick;
    _intervalMs = Math.Max(1, intervalMs);
  }

  private IFastForwardMember? Source =>
    Blockentity as IFastForwardMember
    ?? Blockentity.Behaviors.OfType<IFastForwardMember>().FirstOrDefault();

  /// <inheritdoc/>
  public override void Initialize(ICoreAPI api, JsonObject properties) {
    base.Initialize(api, properties);
    if (api.Side != EnumAppSide.Server)
      return;
    IFastForwardMember? source = Source;
    _step = _tick;
    if (_step == null && source != null)
      _step = source.FastForwardStep;
    if (_step == null)
      return;
    int interval = Math.Max(
      1,
      _tick != null ? _intervalMs : source!.FastForwardIntervalMs
    );

    if (api.ModLoader.GetModSystem<BlockNetworkModSystem>() is { } networks) {
      _driver = networks.FastForward;
      _member = _driver.Register(
        Blockentity.Pos,
        _savedCursor >= 0 ? _savedCursor : null,
        _record.Ports,
        _step,
        interval,
        () => _tickId != 0,
        _record.Peers,
        () => Source?.FastForwardCouplings ?? []
      );
    }
    _tickId = Blockentity.RegisterGameTickListener(RunTick, interval);
  }

  private void RunTick(float dt) {
    if (_driver == null || _member == null) {
      _step!(dt);
      return;
    }
    if (_driver.IsHeld(Blockentity.Pos))
      return;
    using (FastForward.Step(_driver, Blockentity.Pos))
      _step!(dt);
  }

  /// <inheritdoc/>
  public override void ToTreeAttributes(ITreeAttribute tree) {
    base.ToTreeAttributes(tree);
    if (_driver != null && _member != null) {
      _record.Capture(_driver, _member);
      _savedCursor = _member.CursorHours;
    }
    tree.SetDouble(CursorKey, _savedCursor);
    _record.Write(tree);
  }

  /// <inheritdoc/>
  public override void FromTreeAttributes(
    ITreeAttribute tree,
    IWorldAccessor worldAccessForResolve
  ) {
    base.FromTreeAttributes(tree, worldAccessForResolve);
    _savedCursor = tree.GetDouble(CursorKey, -1);
    _record.Read(tree);
  }

  /// <inheritdoc/>
  public override void OnBlockRemoved() {
    base.OnBlockRemoved();
    Leave(removed: true);
  }

  /// <inheritdoc/>
  public override void OnBlockUnloaded() {
    base.OnBlockUnloaded();
    Leave(removed: false);
  }

  private void Leave(bool removed) {
    if (_tickId != 0) {
      Blockentity.UnregisterGameTickListener(_tickId);
      _tickId = 0;
    }
    if (_driver == null || _member == null)
      return;
    if (removed)
      _driver.Remove(Blockentity.Pos);
    else {
      _record.Capture(_driver, _member);
      _savedCursor = _member.CursorHours;
      _driver.SetPresent(Blockentity.Pos, false);
    }
    _member = null;
    _driver = null;
  }
}
