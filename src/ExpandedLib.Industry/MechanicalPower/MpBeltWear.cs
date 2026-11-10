using System;
using ExpandedLib.Registries;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace ExpandedLib.Industry.MechanicalPower;

/// <summary>
/// The belt law: what a belt between two pulleys carries, when it slips and how it wears. A pulley is size 1
/// (1 m across) or size 2 (2 m). A block entity or behaviour that owns a belt holds one part, fits it with the
/// pulley sizes, feeds it the slip power each step and saves it through a tree.
/// </summary>
public sealed class MpBeltWear {
  private const string WearKey = "mpBeltWear";
  private const string BrokenKey = "mpBeltBroken";
  private const float DaySeconds = 24f * 3600f;

  /// <summary>Raised once when <see cref="Break"/> first runs.</summary>
  public event Action? Snapped;

  /// <summary>Size of the driving pulley, 1 or 2; 1 until fitted.</summary>
  public int FromSize { get; private set; } = 1;

  /// <summary>Size of the driven pulley, 1 or 2; 1 until fitted.</summary>
  public int ToSize { get; private set; } = 1;

  /// <summary>Accumulated wear, 0 to 1. At 1 the belt is worn out.</summary>
  public float Wear { get; set; }

  /// <summary>True once <see cref="Break"/> has run; the owner carries nothing from then on.</summary>
  public bool Broken { get; private set; }

  /// <summary>True once <see cref="Wear"/> has reached 1.</summary>
  public bool WornOut => Wear >= 1f;

  /// <summary>Torque (N*m), at the driving pulley's shaft, the belt carries while slipping: <c>MpBeltSlipFraction</c> of its cap.</summary>
  public float SlipTorque =>
    ExlibValues.MpBeltSlipFraction * CapNm(FromSize, ToSize);

  /// <summary>Energy (J) the belt can slip before it wears out: its slip torque at <c>MpBeltRatedRadS</c> over one in-game day.</summary>
  public float LifeJ => SlipTorque * ExlibValues.MpBeltRatedRadS * DaySeconds;

  /// <summary>Sets the pulley sizes the belt runs between; wear and the broken flag are kept.</summary>
  /// <param name="fromSize">Size of the driving pulley, 1 or 2.</param>
  /// <param name="toSize">Size of the driven pulley, 1 or 2.</param>
  /// <exception cref="ArgumentException">A size is not 1 or 2.</exception>
  public void Fit(int fromSize, int toSize) {
    PulleyDiameter(fromSize);
    PulleyDiameter(toSize);
    FromSize = fromSize;
    ToSize = toSize;
  }

  /// <summary>Pulley diameter in metres: 1 for size 1, 2 for size 2.</summary>
  /// <exception cref="ArgumentException"><paramref name="size"/> is not 1 or 2.</exception>
  public static float PulleyDiameter(int size) =>
    size switch {
      1 => 1f,
      2 => 2f,
      _ => throw new ArgumentException(
        "A pulley is size 1 or 2.",
        nameof(size)
      ),
    };

  /// <summary>Torque (N*m), at the driving pulley's shaft, a belt carries before it slips: the tension the smaller grip allows, which the pulley it grips on sets, taken to the driving shaft.</summary>
  /// <param name="fromSize">Size of the driving pulley, 1 or 2.</param>
  /// <param name="toSize">Size of the driven pulley, 1 or 2.</param>
  /// <exception cref="ArgumentException">A size is not 1 or 2.</exception>
  public static float CapNm(int fromSize, int toSize) {
    float dFrom = PulleyDiameter(fromSize);
    float dTo = PulleyDiameter(toSize);
    return MathF.Min(GripNm(fromSize), GripNm(toSize) * dFrom / dTo);
  }

  private static float GripNm(int size) =>
    size == 2 ? ExlibValues.MpBeltCapSize2Nm : ExlibValues.MpBeltCapSize1Nm;

  /// <summary>Wears the belt by the energy it slipped; does nothing for no slip, no time or a belt with no life.</summary>
  /// <param name="slipPowerW">Power (W) the belt dissipated slipping.</param>
  /// <param name="hours">In-game hours the power was held over.</param>
  public void Advance(float slipPowerW, float hours) {
    if (slipPowerW <= 0f || hours <= 0f || LifeJ <= 0f)
      return;
    Wear = MathF.Min(1f, Wear + slipPowerW * hours * 3600f / LifeJ);
  }

  /// <summary>Marks the belt broken and raises <see cref="Snapped"/>; a belt already broken does nothing.</summary>
  public void Break() {
    if (Broken)
      return;
    Broken = true;
    Snapped?.Invoke();
  }

  /// <summary>Writes wear and the broken flag into <paramref name="tree"/>.</summary>
  public void Save(ITreeAttribute tree) {
    tree.SetFloat(WearKey, Wear);
    tree.SetBool(BrokenKey, Broken);
  }

  /// <summary>Reads wear and the broken flag from <paramref name="tree"/>; absent keys read as a new belt.</summary>
  public void Load(ITreeAttribute tree) {
    Wear = tree.GetFloat(WearKey);
    Broken = tree.GetBool(BrokenKey);
  }
}
