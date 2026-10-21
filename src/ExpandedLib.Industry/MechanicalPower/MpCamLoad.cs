using System;

namespace ExpandedLib.Industry.MechanicalPower;

/// <summary>A load of evenly spaced cam lifts on the shaft: each lift costs a fixed energy spread
/// over its arc, so a step of any length pays exactly the lifts it turns through. Pure arithmetic;
/// the caller owns the phase.</summary>
/// <param name="EnergyJ">Energy one lift takes from the run (J); above 0.</param>
/// <param name="LiftsPerTurn">Lifts in one turn of the shaft, spaced evenly from phase 0; at least 1.</param>
/// <param name="LiftArc">Angle one lift is drawn over (rad); above 0 and no more than the spacing
/// <c>2 pi / LiftsPerTurn</c>. A lift starts at <c>k * spacing</c> and its drop is at the end of the
/// arc.</param>
public readonly record struct MpCamLoad(
  float EnergyJ,
  int LiftsPerTurn,
  float LiftArc
) {
  private const double Turn = 2 * Math.PI;

  /// <summary>Torque during a lift (N*m): the lift's energy over its arc.</summary>
  public float PeakTorque => LiftArc > 0f ? EnergyJ / LiftArc : 0f;

  /// <summary>Torque averaged over a whole turn (N*m): <c>energy * lifts / 2 pi</c>.</summary>
  public float MeanTorque() => (float)(EnergyJ * LiftsPerTurn / Turn);

  /// <summary>Torque averaged over the angle the coming step turns (N*m): the lift radians inside
  /// <c>[phase, phase + speed * dt]</c> times the peak torque, over that angle.</summary>
  /// <param name="phase">Shaft angle at the step's start (rad), any real; wrapped by turns.</param>
  /// <param name="speed">Shaft speed (rad/s), unsigned.</param>
  /// <param name="dt">Step length (s).</param>
  /// <returns>The step's mean torque; the torque at <paramref name="phase"/> when the step turns no
  /// angle; 0 when the load has no lifts or arc.</returns>
  public float StepTorque(float phase, float speed, float dt) {
    if (LiftsPerTurn < 1 || LiftArc <= 0f || EnergyJ <= 0f) {
      return 0f;
    }
    double span = (double)speed * dt;
    if (span <= 0) {
      return LiftRadiansTo(phase + 1e-9) - LiftRadiansTo(phase) > 0
        ? PeakTorque
        : 0f;
    }
    double lifted = LiftRadiansTo(phase + span) - LiftRadiansTo(phase);
    return (float)(lifted * PeakTorque / span);
  }

  /// <summary>Drops a phase advance passes: lift ends in <c>(phase, phase + dphase]</c>.</summary>
  /// <param name="phase">Shaft angle before the advance (rad).</param>
  /// <param name="dphase">Angle turned (rad); 0 or less crosses none.</param>
  /// <returns>The count, which exceeds 1 when the advance spans several lifts.</returns>
  public int DropsCrossed(float phase, float dphase) {
    if (LiftsPerTurn < 1 || dphase <= 0f) {
      return 0;
    }
    double spacing = Turn / LiftsPerTurn;
    double arc = LiftArc;
    double end = (double)phase + dphase;
    return (int)(
      Math.Floor((end - arc) / spacing) - Math.Floor((phase - arc) / spacing)
    );
  }

  private double LiftRadiansTo(double x) {
    double spacing = Turn / LiftsPerTurn;
    double turns = Math.Floor(x / Turn);
    double rest = x - turns * Turn;
    double total = turns * LiftsPerTurn * LiftArc;
    for (int k = 0; k < LiftsPerTurn; k++) {
      total += Math.Clamp(rest - k * spacing, 0, LiftArc);
    }
    return total;
  }
}
