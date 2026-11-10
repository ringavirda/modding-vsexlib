#if GAME_GE_1_22
using System;
using ExpandedLib.Industry.MechanicalPower;
using Vintagestory.API.Common;

namespace ExpandedLib.Testing;

/// <summary>
/// The drive a setup's <c>power</c> step stands on a cell of an <c>mpshaft</c> run (setups.md, Placement
/// kinds): a source whose torque falls straight from <see cref="StallNm"/> at rest to nothing at
/// <see cref="FreeRadS"/>, applied at every node of the cell's block entity.
/// </summary>
public sealed class MpTestSource(BlockEntity blockentity)
  : BlockEntityBehavior(blockentity),
    IMpEnergyProducer
{
  /// <summary>Torque in N*m the source delivers at rest; 0 or more.</summary>
  public float StallNm { get; set; }

  /// <summary>Speed in rad/s at which the source delivers nothing; above 0.</summary>
  public float FreeRadS { get; set; } = 1f;

  /// <summary>Torque in N*m at node speed <paramref name="speed"/> (rad/s, signed): <see cref="StallNm"/>
  /// scaled down linearly to 0 at <see cref="FreeRadS"/>, and 0 beyond it.</summary>
  public float DriveTorque(float speed) =>
    StallNm * MathF.Max(0f, 1f - MathF.Abs(speed) / FreeRadS);

  /// <summary>Change of <see cref="DriveTorque"/> per rad/s of speed in N*m per rad/s: negative below
  /// <see cref="FreeRadS"/>, 0 at and beyond it.</summary>
  public float DriveSlope(float speed) =>
    MathF.Abs(speed) < FreeRadS ? -StallNm / FreeRadS : 0f;
}
#endif
