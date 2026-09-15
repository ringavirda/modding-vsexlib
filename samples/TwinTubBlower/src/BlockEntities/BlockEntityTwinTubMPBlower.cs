using System.Text;
using ExpandedLib;
using ExpandedLib.Blocks;
using ExpandedLib.Helpers;
using ExpandedLib.Industry.Helpers;
using ExpandedLib.Industry.MechanicalPower;
using ExpandedLib.Industry.Pipes;
using ExpandedLib.Registries;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace TwinTubBlower.BlockEntities;

/// <summary>
/// Mechanically driven pair of bellows that pushes cold ambient air into the network it stands in. It is
/// a pipe node that generates rather than a machine feeding a neighbouring network: the block is a
/// <see cref="ExpandedLib.Industry.Pipes.BlockPipe"/> and produces into its own network, the way a fluid
/// intake does for water. Drive comes from a <see cref="BEBehaviorMPFillerPort"/> on the footprint's
/// upper-rear cell.
/// </summary>
[BlockEntityRegister]
public class BlockEntityTwinTubMPBlower : BlockEntityPipe, IRenderer {
  /// <summary>
  /// Structure-local cell hosting the mechanical-power port, in the block's north frame: the upper-rear
  /// cell of the 1x2x3 footprint. The port faces west relative to the placed rotation.
  /// </summary>
  private static readonly Vec3i MpPortCell = new(0, 1, 0);

  /// <summary>
  /// Shortest interval between bellows-note plays, at least as long as the "bellows" sample itself
  /// (measured ~1.41 s at 44.1 kHz), so a run of blow ticks never overlaps or cuts off the clip.
  /// </summary>
  private const long BellowsSoundIntervalMs = 1500;

  // Axle speed sampled on the last blow tick. Written server-side and serialized because the client
  // cannot read the port behaviour's live state and needs it for the HUD.
  [Persist("blowerSpeed")]
  private float _lastSpeed;

  /// <summary>The port cell as the server last saw it, for the readout: 0 no port hosted, 1 a port
  /// with no axle network, 2 a port on a turning network.</summary>
  [Persist("axleState")]
  private int _axleState;

  /// <summary>Whether the server saw every construction stage complete on its last tick.</summary>
  [Persist("builtOnServer")]
  private bool _builtOnServer;

  private long _blowTickId;
  private long _lastBellowsSoundMs;

  private ConstructedAnimator? _animator;

  /// <summary>The placed rotation, read from the block so the port lookup and the footprint agree.</summary>
  private int Angle =>
    (Block as Blocks.BlockTwinTubMPBlower)?.StructureAngle ?? 0;

  /// <summary>True once the player has finished all five construction stages. The blower has no
  /// mesh of its own before then - <see cref="ConstructedAnimator"/> draws the shape's own
  /// <c>Root/Base</c> group - and neither ticks, sounds nor leaks air.</summary>
  public bool IsConstructed => _animator?.IsConstructed ?? false;

  /// <summary>
  /// Renders nothing. Registered only to get a per-render-frame callback: the cycle clip must be
  /// pinned to the axle every frame or it visibly steps between locks, worse the more the free-running
  /// clip and the axle's own rate disagree. Runs before the opaque pass so the frame is already in
  /// step when the mesh is drawn.
  /// </summary>
  public double RenderOrder => 0.0;

  public int RenderRange => 24;

  public void OnRenderFrame(float dt, EnumRenderStage stage) =>
    LockCycleToAxle();

  public void Dispose() { }

  public override void Initialize(ICoreAPI api) {
    base.Initialize(api);
    // One blow per second, server-side. The network tick runs at the same interval, so air is produced
    // and then distributed in the same beat.
    if (api.Side == EnumAppSide.Server) {
      _blowTickId = RegisterGameTickListener(OnBlowTick, 1000);
      return;
    }

    // Resolved on both sides (IsConstructed gates the blow tick); it only builds and poses on the client.
    _animator = new ConstructedAnimator(this, () => Block.Code.Path);
    _animator.Initialize(ApplyPose);
    (api as ICoreClientAPI)?.Event.RegisterRenderer(
      this,
      EnumRenderStage.Before,
      "twintubblower-cycle"
    );
  }

  public override void OnBlockRemoved() {
    base.OnBlockRemoved();
    UnregisterTicks();
    _animator?.Dispose();
  }

  public override void OnBlockUnloaded() {
    base.OnBlockUnloaded();
    UnregisterTicks();
    _animator?.Dispose();
  }

  private void UnregisterTicks() {
    if (_blowTickId != 0) {
      UnregisterGameTickListener(_blowTickId);
      _blowTickId = 0;
    }
    // The renderer holds a reference to this block entity; leaving it registered keeps a removed
    // blower alive and still writing frames.
    (Api as ICoreClientAPI)?.Event.UnregisterRenderer(
      this,
      EnumRenderStage.Before
    );
  }

  /// <summary>
  /// Samples the axle and pushes one second of air into the network, scaled by
  /// <see cref="SpeedFraction"/>. Marks dirty only when the sampled speed changed. The bellows' note
  /// plays whenever they are working, whether or not the line had room for the air - several blowers
  /// sharing a line at its pressure ceiling all move, so all must be heard; a line with nowhere for the
  /// air to go blows it off at the outlet as well. A no-op before construction completes - an axle
  /// coupled to the port cell of an unbuilt blower turns nothing.
  /// </summary>
  private void OnBlowTick(float dt) {
    int axleState = AxleState();
    bool built = IsConstructed;
    if (axleState != _axleState || built != _builtOnServer) {
      _axleState = axleState;
      _builtOnServer = built;
      MarkDirty();
    }
    if (!built)
      return;

    float speed = PortSpeed();
    if (speed != _lastSpeed) {
      _lastSpeed = speed;
      MarkDirty();
    }
    if (SpeedFraction(speed) <= 0f)
      return;

    ExSounds.PlayThrottled(
      Api,
      Pos,
      ExSounds.Bellows,
      ref _lastBellowsSoundMs,
      BellowsSoundIntervalMs,
      0.5f,
      16f
    );
    if (
      ProduceAir(speed, dt) <= 0f
      && Block is Blocks.BlockTwinTubMPBlower block
    )
      ExParticles.GasLeak(Api.World, block.OutletCell(Pos), block.OutletFace);
  }

  /// <summary>
  /// Pushes <paramref name="dt"/> seconds of air into this blower's own network at axle speed
  /// <paramref name="speed"/>. Returns the litres actually produced, 0 when the bellows are below
  /// <see cref="TwinTubBlowerValues.TwinTubBlowerMinSpeed"/>, the line is at the pressure ceiling, or
  /// construction is unfinished. Public so the balance can be driven without a mechanical network for
  /// the port to read.
  /// </summary>
  public float ProduceAir(float speed, float dt) {
    if (!IsConstructed)
      return 0f;
    float fraction = SpeedFraction(speed);
    if (fraction <= 0f || dt <= 0f)
      return 0f;
    if (NetworkSystem?.GetNetworkAt(Pos) is not PipeNetwork net)
      return 0f;

    // Cold blast: the air enters at ambient temperature.
    // TryProduceGas reports only whether it accepted anything and clamps at the pressure ceiling, so the
    // litres that landed are the change in the pool, not the amount asked for.
    float before = net.State?.Volume ?? 0f;
    net.TryProduceGas(
      TwinTubBlowerValues.TwinTubBlowerOutputPerSecond * fraction * dt,
      AmbientTemperature,
      "Air",
      Api.World.BlockAccessor,
      maxOutputPressure: TwinTubBlowerValues.TwinTubBlowerMaxPressure
    );
    return GameMath.Max(0f, (net.State?.Volume ?? 0f) - before);
  }

  /// <summary>Ambient air temperature at the bellows in degrees Celsius, falling back to the configured
  /// world ambient where no climate is available.</summary>
  private float AmbientTemperature =>
    Api?.World?.BlockAccessor?.GetClimateAt(Pos)?.Temperature
    ?? ExlibValues.AmbientTemperature;

  /// <summary>
  /// Fraction of the rated output the bellows deliver at <paramref name="speed"/>: 0 at or below
  /// <see cref="TwinTubBlowerValues.TwinTubBlowerMinSpeed"/>, 1 at or above
  /// <see cref="TwinTubBlowerValues.TwinTubBlowerMaxSpeed"/>, linear between.
  /// </summary>
  public static float SpeedFraction(float speed) {
    float min = TwinTubBlowerValues.TwinTubBlowerMinSpeed;
    float max = TwinTubBlowerValues.TwinTubBlowerMaxSpeed;
    if (speed <= min)
      return 0f;
    if (max <= min)
      return 1f;
    return GameMath.Clamp((speed - min) / (max - min), 0f, 1f);
  }

  /// <summary>The mechanical-power port hosted on the footprint's upper-rear cell, or null before the
  /// filler block entity there has loaded.</summary>
  private BEBehaviorMPFillerPort? Port() {
    BlockPos cell = ExOrientation.GlobalPos(
      Pos,
      MpPortCell.X,
      MpPortCell.Y,
      MpPortCell.Z,
      Angle
    );
    return Api
      .World.BlockAccessor.GetBlockEntity(cell)
      ?.GetBehavior<BEBehaviorMPFillerPort>();
  }

  /// <summary>The driving axle's speed, or 0 when no axle is coupled to the port cell.</summary>
  private float PortSpeed() =>
    Port() is { IsTurning: true } port ? port.Speed : 0f;

  #region Animation

  /// <summary>
  /// Holds one clip at a time: <c>cycle</c> while the bellows are working
  /// (<see cref="SpeedFraction"/> of <see cref="_lastSpeed"/> above 0), <c>idle</c> otherwise. One must
  /// always be active or the animator drops the mesh.
  /// </summary>
  private void ApplyPose() {
    bool blowing = SpeedFraction(_lastSpeed) > 0f;
    _animator?.Pose(util => {
      util.StopAnimation(blowing ? "idle" : "cycle");
      util.StartAnimation(
        new AnimationMetaData {
          Animation = blowing ? "cycle" : "idle",
          Code = blowing ? "cycle" : "idle",
          AnimationSpeed = 1f,
          EaseInSpeed = 10f,
          EaseOutSpeed = 5f,
        }.Init()
      );
    });
  }

  /// <summary>
  /// Pins the running <c>cycle</c> clip to the driving axle's angle, so the rod, the beam and the two
  /// tub pistons move in step with the shaft rather than at a merely proportional rate. Called every
  /// render frame from <see cref="OnRenderFrame"/>, not from a tick, or the clip free-runs between
  /// locks and visibly steps. A no-op while idle - <c>idle</c> has no cycle to lock.
  /// </summary>
  private void LockCycleToAxle() {
    if (SpeedFraction(_lastSpeed) <= 0f || Port() is not { } port)
      return;
    // Reversed: the clip turns its Axle element through +360 over the cycle, which runs against the
    // vanilla axle for a rising angle.
    MPAnim.LockFrameToAngle(
      _animator?.AnimUtil,
      "cycle",
      port.CurrentAngleRad,
      reverse: true
    );
  }

  #endregion

  /// <summary>
  /// Re-poses on the client when the loaded speed crosses the idle/blowing threshold. <c>_lastSpeed</c>
  /// itself round-trips through the tree already, via its <c>[Persist]</c> declaration; this override
  /// exists only for the side effect base has no hook for.
  /// </summary>
  public override void FromTreeAttributes(
    ITreeAttribute tree,
    IWorldAccessor worldForResolving
  ) {
    bool wasBlowing = SpeedFraction(_lastSpeed) > 0f;
    base.FromTreeAttributes(tree, worldForResolving);

    if (SpeedFraction(_lastSpeed) > 0f != wasBlowing)
      ApplyPose();
  }

  public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc) {
    // Pipe readout first (medium, throughput, pressure), then the bellows' own state.
    base.GetBlockInfo(forPlayer, dsc);

    dsc.AppendLine(AxleReadout());
    float fraction = SpeedFraction(_lastSpeed);
    dsc.AppendLine(
      fraction <= 0f
        ? Lang.Get("twintubblower:blower-info-idle")
        : Lang.Get(
          "twintubblower:blower-info-blowing",
          ExMeasure.FlowRate(
            TwinTubBlowerValues.TwinTubBlowerOutputPerSecond * fraction
          ),
          (int)(fraction * 100f)
        )
    );
  }

  /// <summary>
  /// What the port cell reports: no port hosted, a port with no axle network, or the network's
  /// speed. Read on the client from its own copy of the mechanical network, the same one the
  /// axle renders from.
  /// </summary>
  public string AxleReadout() {
    string axle = _axleState switch {
      0 => Lang.Get("twintubblower:blower-info-axle-noport"),
      1 => Lang.Get("twintubblower:blower-info-axle-uncoupled"),
      _ => Lang.Get("twintubblower:blower-info-axle-coupled", _lastSpeed.ToString("0.00")),
    };
    return axle
      + " "
      + Lang.Get(
        _builtOnServer
          ? "twintubblower:blower-info-built"
          : "twintubblower:blower-info-unbuilt"
      );
  }

  /// <summary>The port cell's state on this side: 0 no port hosted, 1 no axle network, 2 turning.</summary>
  private int AxleState() {
    BlockPos cell = ExOrientation.GlobalPos(
      Pos,
      MpPortCell.X,
      MpPortCell.Y,
      MpPortCell.Z,
      Angle
    );
    var port = Api
      ?.World?.BlockAccessor?.GetBlockEntity(cell)
      ?.GetBehavior<BEBehaviorMPFillerPort>();
    if (port == null)
      return 0;
    return port is { IsTurning: true } ? 2 : 1;
  }
}
