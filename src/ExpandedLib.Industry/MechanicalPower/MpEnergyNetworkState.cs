using System;

namespace ExpandedLib.Industry.MechanicalPower;

/// <summary>Live state of one mechanical-energy run, modelled as a single spinning shaft:
/// <c>I*dw/dt = T_drive - T_load - T_fric</c>, with stored energy <c>E = 1/2*I*w^2</c>. Quantities
/// are SI (w rad/s, I kg*m^2, T N*m, E J, P W).</summary>
public class MpEnergyNetworkState {
  /// <summary>Shaft speed <c>w</c> in rad/s, integrated from the net torque.</summary>
  public float Speed { get; set; }

  /// <summary>Lumped rotational inertia <c>I</c> in kg*m^2: the sum over every flywheel and
  /// transmission buffer.</summary>
  public float Inertia { get; set; }

  /// <summary>Stored mechanical energy <c>E = 1/2*I*w^2</c> in joules, recomputed each step.</summary>
  public float StoredEnergy { get; set; }

  /// <summary>Power fed into the run this tick, <c>P = T_drive*w</c> in watts. Display only.</summary>
  public float SupplyPower { get; set; }

  /// <summary>Power drawn from the run this tick, <c>P = T_load*w</c> in watts. Display only.</summary>
  public float DemandPower { get; set; }

  /// <summary>Direction of rotation. <see cref="Speed"/> is unsigned; machines whose geometry
  /// depends on direction read this instead.</summary>
  public bool Reversed { get; set; }

  /// <summary>Reservoir capacity <c>E_cap = 1/2*I*w_max^2</c> in joules for the given inertia and
  /// burst speed.</summary>
  public static float CapacityFor(float inertia, float maxSpeed) =>
    0.5f * inertia * maxSpeed * maxSpeed;

  /// <summary>Shaft speed for a reservoir holding <paramref name="storedEnergy"/>: <c>w = sqrt(2E/I)</c>. Zero
  /// when there is no inertia.</summary>
  public static float DeriveSpeed(float storedEnergy, float inertia) =>
    inertia > 0f ? MathF.Sqrt(2f * MathF.Max(0f, storedEnergy) / inertia) : 0f;

  /// <summary>Energy held at a given speed: <c>E = 1/2*I*w^2</c>, the inverse of
  /// <see cref="DeriveSpeed"/>.</summary>
  public static float EnergyAtSpeed(float inertia, float speed) =>
    0.5f * inertia * speed * speed;

  /// <summary>One integration step of the shaft dynamics: <c>w += (T_drive - T_load - T_fric)/I *
  /// dt</c>, clamped to <c>[0, maxSpeed]</c>, with <c>T_fric = b*w + T_idle</c>.</summary>
  public static void Step(
    MpEnergyNetworkState s,
    float dt,
    float driveTorque,
    float loadTorque,
    float frictionCoeff,
    float idleTorque,
    float maxSpeed
  ) {
    // The idle floor is a resistance and only ever opposes rotation.
    float frictionTorque = frictionCoeff * s.Speed + MathF.Max(0f, idleTorque);
    float netTorque = driveTorque - loadTorque - frictionTorque;
    float dOmega = s.Inertia > 0f ? netTorque / s.Inertia * dt : 0f;

    s.Speed = Math.Clamp(s.Speed + dOmega, 0f, maxSpeed);
    s.StoredEnergy = EnergyAtSpeed(s.Inertia, s.Speed);
    s.SupplyPower = driveTorque * s.Speed;
    s.DemandPower = loadTorque * s.Speed;
  }

  /// <summary>Couples two separate runs across a rigid gear of reduction
  /// <paramref name="ratio"/> (at least 1). A no-op when either side has no inertia.</summary>
  public static void CoupleRatio(
    MpEnergyNetworkState south,
    MpEnergyNetworkState north,
    float ratio,
    float maxSpeed,
    float retention = 1f
  ) {
    if (ratio <= 0f || south.Inertia <= 0f || north.Inertia <= 0f)
      return;

    float total =
      (
        EnergyAtSpeed(south.Inertia, south.Speed)
        + EnergyAtSpeed(north.Inertia, north.Speed)
      ) * Math.Clamp(retention, 0f, 1f);
    float combined = south.Inertia + north.Inertia / (ratio * ratio);
    float omegaSouth = MathF.Min(MathF.Sqrt(2f * total / combined), maxSpeed);
    float omegaNorth = omegaSouth / ratio;

    south.Speed = omegaSouth;
    north.Speed = omegaNorth;
    south.StoredEnergy = EnergyAtSpeed(south.Inertia, omegaSouth);
    north.StoredEnergy = EnergyAtSpeed(north.Inertia, omegaNorth);
  }
}
