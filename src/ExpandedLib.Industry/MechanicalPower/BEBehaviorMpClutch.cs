using System;
using ExpandedLib.Networks;
using ExpandedLib.Registries;
using Vintagestory.API.Common;

namespace ExpandedLib.Industry.MechanicalPower;

/// <summary>
/// A friction clutch between two runs of an "mpshaft" network. Engaging brings its clamp on from nothing to its
/// cap over <c>MpClutchRampS</c> seconds; it carries the clamp's torque while the two sides differ and locks when
/// they meet. Released, it joins nothing.
/// </summary>
[BlockEntityBehaviorRegister]
public class BEBehaviorMpClutch(BlockEntity blockentity)
  : BlockEntityBehavior(blockentity),
    IMpEnergyLink {
  private NodeKey _from;
  private NodeKey _to;
  private MpShaftSize _size = MpShaftSize.Main;
  private bool _connected;
  private bool _engaged;
  private float _rampS;

  public NodeKey From => _from;

  public NodeKey To => _to;

  public float Ratio => 1f;

  public int Sign => 1;

  public float Efficiency => 1f;

  /// <summary>True while the clutch is thrown in. Setting it true from false restarts the clamp from nothing.</summary>
  public bool Engaged {
    get => _connected && _engaged;
    set {
      if (value && !_engaged)
        _rampS = 0f;
      _engaged = value;
    }
  }

  /// <summary>Fraction of its cap the clamp has come on to, 0 to 1.</summary>
  public float Clamp =>
    MathF.Min(1f, _rampS / MathF.Max(ExlibValues.MpClutchRampS, 1e-3f));

  /// <summary>Torque (N*m) the clutch carries on a shaft of <paramref name="size"/> with the clamp fully on.</summary>
  public static float FullCapNm(MpShaftSize size) =>
    size == MpShaftSize.Line
      ? ExlibValues.MpClutchCapLineNm
      : ExlibValues.MpClutchCapMainNm;

  public float CapNm => FullCapNm(_size) * Clamp;

  public float SlipTorque(float relativeSpeed) => CapNm;

  /// <summary>Fits the clutch between two sides of a shaft, released.</summary>
  /// <param name="from">Port key of the driving side.</param>
  /// <param name="to">Port key of the driven side.</param>
  /// <param name="size">The shaft size the clutch is fitted to, which sets its cap.</param>
  public void Connect(NodeKey from, NodeKey to, MpShaftSize size) {
    _from = from;
    _to = to;
    _size = size;
    _connected = true;
  }

  public void Advance(float dt, float hours, float slipPowerW) {
    if (Engaged)
      _rampS += dt;
  }
}
