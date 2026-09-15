using System.Text;
using ExpandedLib;
using ExpandedLib.Blocks;
using ExpandedLib.Helpers;
using ExpandedLib.Industry.Helpers;
using ExpandedLib.Industry.MechanicalPower;
using ExpandedLib.Industry.Pipes;
using ExpandedLib.Networks;
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
public class BlockEntityTwinTubMPBlower : BlockEntityPipe {
  /// <summary>
  /// Structure-local cell hosting the mechanical-power port, in the block's north frame: the upper-rear
  /// cell of the 1x2x3 footprint. The port faces west relative to the placed rotation.
  /// </summary>
  private static readonly Vec3i MpPortCell = new(0, 1, 0);

  // Axle speed sampled on the last blow tick. Written server-side and serialized because the client
  // cannot read the port behaviour's live state and needs it for the HUD.
  [Persist("blowerSpeed")]
  private float _lastSpeed;

  private long _blowTickId;
  private long _lockTickId;

  private ToggleAnimator? _anim;

  /// <summary>The placed rotation, read from the block so the port lookup and the footprint agree.</summary>
  private int Angle =>
    (Block as Blocks.BlockTwinTubMPBlower)?.StructureAngle ?? 0;

  public override void Initialize(ICoreAPI api) {
    base.Initialize(api);
    // One blow per second, server-side. The network tick runs at the same interval, so air is produced
    // and then distributed in the same beat.
    if (api.Side == EnumAppSide.Server) {
      _blowTickId = RegisterGameTickListener(OnBlowTick, 1000);
      return;
    }

    _anim = new ToggleAnimator(this, BuildAnimator);
    _anim.Initialize(ApplyPose);
    // Four updates a second, fast enough that pinning the clip to the axle does not show as stepping.
    _lockTickId = RegisterGameTickListener(OnLockTick, 250);
  }

  public override void OnBlockRemoved() {
    base.OnBlockRemoved();
    UnregisterTicks();
  }

  public override void OnBlockUnloaded() {
    base.OnBlockUnloaded();
    UnregisterTicks();
  }

  private void UnregisterTicks() {
    if (_blowTickId != 0) {
      UnregisterGameTickListener(_blowTickId);
      _blowTickId = 0;
    }
    if (_lockTickId != 0) {
      UnregisterGameTickListener(_lockTickId);
      _lockTickId = 0;
    }
  }

  /// <summary>
  /// Samples the axle and pushes one second of air into the network, scaled by
  /// <see cref="SpeedFraction"/>. Marks dirty only when the sampled speed changed. A working line
  /// blows with the bellows' note; a line with nowhere for the air to go blows it off at the outlet
  /// instead.
  /// </summary>
  private void OnBlowTick(float dt) {
    float speed = PortSpeed();
    if (speed != _lastSpeed) {
      _lastSpeed = speed;
      MarkDirty();
    }
    if (SpeedFraction(speed) <= 0f)
      return;

    if (ProduceAir(speed, dt) > 0f)
      ExSounds.PlayLocal(Api.World, Pos, ExSounds.Bellows, 0.5f, 16f);
    else if (
      (Block as BlockNetworkNode)?.GetConnectorFaces() is { Length: > 0 } faces
    )
      ExParticles.GasLeak(Api.World, Pos, faces[0]);
  }

  /// <summary>
  /// Pushes <paramref name="dt"/> seconds of air into this blower's own network at axle speed
  /// <paramref name="speed"/>. Returns the litres actually produced, 0 when the bellows are below
  /// <see cref="TwinTubBlowerValues.TwinTubBlowerMinSpeed"/> or the line is at the pressure ceiling.
  /// Public so the balance can be driven without a mechanical network for the port to read.
  /// </summary>
  public float ProduceAir(float speed, float dt) {
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

  /// <summary>Builds the mesh and animator against this block's own shape, north-frame rotation
  /// applied through <see cref="Vintagestory.API.Common.Block.Shape"/>'s <c>rotateY</c>.</summary>
  private void BuildAnimator(BEBehaviorAnimatable animatable) {
    MeshData mesh = animatable.animUtil.CreateMesh(
      Block.Code.Path,
      null,
      out Shape shape,
      null
    );
    animatable.animUtil.InitializeAnimator(
      Block.Code.Path,
      mesh,
      shape,
      new Vec3f(0, Block.Shape.rotateY, 0)
    );
  }

  /// <summary>
  /// Holds one clip at a time: <c>cycle</c> while the bellows are working
  /// (<see cref="SpeedFraction"/> of <see cref="_lastSpeed"/> above 0), <c>idle</c> otherwise. One must
  /// always be active or the animator drops the mesh.
  /// </summary>
  private void ApplyPose() {
    bool blowing = SpeedFraction(_lastSpeed) > 0f;
    _anim?.Pose(util => {
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
  /// tub pistons move in step with the shaft rather than at a merely proportional rate. A no-op while
  /// idle - <c>idle</c> has no cycle to lock.
  /// </summary>
  private void OnLockTick(float dt) {
    if (SpeedFraction(_lastSpeed) <= 0f || Port() is not { } port)
      return;
    // Reversed: the clip turns its Axle element through +360 over the cycle, which runs against the
    // vanilla axle for a rising angle.
    MPAnim.LockFrameToAngle(
      _anim?.AnimUtil,
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
}
