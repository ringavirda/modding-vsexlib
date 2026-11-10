using System;
using ExpandedLib.Blocks;
using ExpandedLib.Industry.MechanicalPower;
using ExpandedLib.Networks;
using ExpandedLib.Registries;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Industry.Creative;

/// <summary>
/// The drive of <see cref="BlockCreativeMpEnergy"/>: torque falls straight from <see cref="Torque"/> at
/// rest to nothing at <see cref="Speed"/>. Both are saved under the tree keys <c>torque</c> and <c>speed</c>.
/// </summary>
[BlockEntityRegister]
public class BlockEntityCreativeMpEnergy
  : BlockEntityNetworkNode,
    IMpEnergyProducer {
  /// <summary>Torque in N*m the source delivers at rest; 0 or more.</summary>
  [Persist("torque")]
  public float Torque { get; set; }

  /// <summary>Speed in rad/s at which the source delivers nothing; above 0.</summary>
  [Persist("speed")]
  public float Speed {
    get => _speed;
    set => _speed = value > 0f ? value : 1f;
  }

  private float _speed = 1f;

  /// <summary>Always "mpshaft".</summary>
  public override string NetworkType {
    get => "mpshaft";
    set { }
  }

  /// <summary>True on every face.</summary>
  public override bool HasConnectorAt(BlockFacing face) => true;

  /// <summary>Torque in N*m at node speed <paramref name="speed"/> (rad/s, signed): <see cref="Torque"/>
  /// scaled down linearly to 0 at <see cref="Speed"/>, and 0 beyond it.</summary>
  public float DriveTorque(float speed) =>
    Torque * MathF.Max(0f, 1f - MathF.Abs(speed) / Speed);

  /// <summary>Change of <see cref="DriveTorque"/> per rad/s of speed in N*m per rad/s: negative below
  /// <see cref="Speed"/>, 0 at and beyond it.</summary>
  public float DriveSlope(float speed) =>
    MathF.Abs(speed) < Speed ? -Torque / Speed : 0f;
}
