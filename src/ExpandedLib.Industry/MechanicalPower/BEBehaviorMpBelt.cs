using System;
using ExpandedLib.Networks;
using ExpandedLib.Registries;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace ExpandedLib.Industry.MechanicalPower;

/// <summary>
/// A belt between two pulleys of an "mpshaft" run, held by a block entity on the driving pulley's cell. It
/// carries up to its cap locked, then slips at <c>MpBeltSlipFraction</c> of it, and the energy it slips wears it
/// out: its life is the energy it slips at its slip torque and <c>MpBeltRatedRadS</c> over one in-game day.
/// </summary>
[BlockEntityBehaviorRegister]
public class BEBehaviorMpBelt(BlockEntity blockentity)
  : BlockEntityBehavior(blockentity),
    IMpEnergyLink {
  private const string WearKey = "mpBeltWear";
  private const float DaySeconds = 24f * 3600f;

  private NodeKey _from;
  private NodeKey _to;
  private MpShaftSize _fromPulley = MpShaftSize.Line;
  private MpShaftSize _toPulley = MpShaftSize.Line;
  private bool _connected;
  private int _sign = 1;

  /// <summary>Raised once when the belt wears out with shaft failure on; the owner drops the belt as scrap.</summary>
  public event Action? Snapped;

  /// <summary>Accumulated wear, 0 to 1; saved with the block. At 1 the belt is worn out.</summary>
  public float Wear { get; set; }

  /// <summary>True once the belt has snapped; it carries nothing from then on.</summary>
  public bool Broken { get; private set; }

  public NodeKey From => _from;

  public NodeKey To => _to;

  public float Ratio => PulleyDiameter(_toPulley) / PulleyDiameter(_fromPulley);

  public int Sign => _sign;

  public float Efficiency => 1f;

  public bool Engaged => _connected && !Broken;

  public float CapNm => CapFor(_fromPulley, _toPulley);

  public bool WornOut => Wear >= 1f;

  /// <summary>Fits the belt between two pulleys.</summary>
  /// <param name="from">Port key of the driving pulley's side.</param>
  /// <param name="to">Port key of the driven pulley's side.</param>
  /// <param name="fromPulley">Size of the pulley at <paramref name="from"/>: <see cref="MpShaftSize.Line"/> for size 1, <see cref="MpShaftSize.Main"/> for size 2.</param>
  /// <param name="toPulley">Size of the pulley at <paramref name="to"/>, the same two values.</param>
  /// <param name="crossed">True when the belt is crossed, so the driven pulley turns against the driving one.</param>
  /// <exception cref="ArgumentException">A pulley size is <see cref="MpShaftSize.None"/>.</exception>
  public void Connect(
    NodeKey from,
    NodeKey to,
    MpShaftSize fromPulley,
    MpShaftSize toPulley,
    bool crossed = false
  ) {
    PulleyDiameter(fromPulley);
    PulleyDiameter(toPulley);
    _from = from;
    _to = to;
    _fromPulley = fromPulley;
    _toPulley = toPulley;
    _sign = crossed ? -1 : 1;
    _connected = true;
  }

  /// <summary>Pulley diameter in metres: 1 for size 1, 2 for size 2.</summary>
  /// <exception cref="ArgumentException"><paramref name="size"/> is <see cref="MpShaftSize.None"/>.</exception>
  public static float PulleyDiameter(MpShaftSize size) =>
    size switch {
      MpShaftSize.Line => 1f,
      MpShaftSize.Main => 2f,
      _ => throw new ArgumentException(
        "A pulley is size 1 or 2.",
        nameof(size)
      ),
    };

  /// <summary>Torque (N*m), at the driving pulley's shaft, a belt carries before it slips: the tension the smaller grip allows, which the pulley it grips on sets, taken to the driving shaft.</summary>
  /// <param name="fromPulley">Size of the driving pulley.</param>
  /// <param name="toPulley">Size of the driven pulley.</param>
  /// <exception cref="ArgumentException">A pulley size is <see cref="MpShaftSize.None"/>.</exception>
  public static float CapFor(MpShaftSize fromPulley, MpShaftSize toPulley) {
    float dFrom = PulleyDiameter(fromPulley);
    float dTo = PulleyDiameter(toPulley);
    return MathF.Min(GripNm(fromPulley), GripNm(toPulley) * dFrom / dTo);
  }

  private static float GripNm(MpShaftSize pulley) =>
    pulley == MpShaftSize.Main
      ? ExlibValues.MpBeltCapSize2Nm
      : ExlibValues.MpBeltCapSize1Nm;

  public float SlipTorque(float relativeSpeed) =>
    ExlibValues.MpBeltSlipFraction * CapNm;

  /// <summary>Energy (J) the belt can slip before it wears out: its slip torque at <c>MpBeltRatedRadS</c> over one in-game day.</summary>
  public float LifeJ =>
    SlipTorque(0f) * ExlibValues.MpBeltRatedRadS * DaySeconds;

  public void Advance(float dt, float hours, float slipPowerW) {
    if (slipPowerW <= 0f || hours <= 0f || LifeJ <= 0f)
      return;
    Wear = MathF.Min(1f, Wear + slipPowerW * hours * 3600f / LifeJ);
  }

  public void Break() {
    if (Broken)
      return;
    Broken = true;
    Snapped?.Invoke();
  }

  public override void ToTreeAttributes(ITreeAttribute tree) {
    base.ToTreeAttributes(tree);
    tree.SetFloat(WearKey, Wear);
  }

  public override void FromTreeAttributes(
    ITreeAttribute tree,
    IWorldAccessor worldAccessForResolve
  ) {
    base.FromTreeAttributes(tree, worldAccessForResolve);
    Wear = tree.GetFloat(WearKey);
  }
}
