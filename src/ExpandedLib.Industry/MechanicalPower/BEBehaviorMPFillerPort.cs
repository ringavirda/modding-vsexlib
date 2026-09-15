using ExpandedLib.Registries;
using ExpandedLib.Structures;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent.Mechanics;

namespace ExpandedLib.Industry.MechanicalPower;

/// <summary>A minimal mechanical-power node a mega-block hosts on one of its invisible footprint
/// cells. The port renders nothing and only loads the network with a configurable resistance.</summary>
[BlockEntityBehaviorRegister]
public class BEBehaviorMPFillerPort(BlockEntity blockentity)
  : BEBehaviorMPBase(blockentity),
    IFillerHostedBehavior {
  /// <summary>Default network load when the declaration sets no <c>resistance</c> property.</summary>
  public const float DefaultResistance = 0.5f;

  private BlockFacing _face = BlockFacing.NORTH;
  private float _resistance = DefaultResistance;
  private bool _through = true;

  /// <summary>The face this port couples an axle on (already in the placed orientation).</summary>
  public BlockFacing PortFacing => _face;

  /// <summary>The network's current rotation angle (radians); 0 when the port has no network.</summary>
  public float CurrentAngleRad => Network != null ? AngleRad : 0f;

  /// <summary>The coupled axle's angle (radians): <see cref="CurrentAngleRad"/> negated on a west
  /// or north port, straight on east or south; 0 when the port has no network.</summary>
  public float DrivenAngleRad {
    get {
      Vec3i n = _face.Normali;
      return -(AxisSign[0] * n.X + AxisSign[1] * n.Y + AxisSign[2] * n.Z)
        * CurrentAngleRad;
    }
  }

  /// <summary>True while the axle is turning, that is, while the port delivers power.</summary>
  public bool IsTurning => Network is { Speed: > 0.001f or < -0.001f };

  /// <summary>This port's own rotation speed, absolute and geared through
  /// <see cref="BEBehaviorMPBase.GearedRatio"/>; 0 when the port has no network.</summary>
  public float Speed =>
    Network != null ? System.Math.Abs(Network.Speed * GearedRatio) : 0f;

  /// <summary>Which way the axle turns: true when the vanilla network runs negative.</summary>
  public bool IsReversed => Network is { Speed: < -0.001f };

  public void ConfigureFromFiller(
    BlockPos? principal,
    BlockFacing? connectorFace,
    JsonObject? properties
  ) {
    if (connectorFace != null)
      _face = connectorFace;
    if (properties != null) {
      _resistance = properties["resistance"].AsFloat(DefaultResistance);
      _through = properties["through"].AsBool(true);
    }
  }

  /// <summary>Whether the port also couples the face opposite its own (the default). A machine
  /// that takes power on one side only declares <c>through: false</c>.</summary>
  public bool Through => _through;

  public override void Initialize(ICoreAPI api, JsonObject properties) {
    base.Initialize(api, properties);

    // A through port also couples the opposite end of the axis.
    if (api.Side == EnumAppSide.Server && OutFacingForNetworkDiscovery != null) {
      tryConnect(OutFacingForNetworkDiscovery);
      if (_through)
        tryConnect(OutFacingForNetworkDiscovery.Opposite);
    }
  }

  public override float GetResistance() => _resistance;

  /// <summary>The faces power leaves this port by: both ends of the axis for a through port,
  /// none for a one-sided one.</summary>
#if GAME_GE_1_22
  public override MechPowerPath[] GetMechPowerExits(MechPowerPath entryDir) =>
#else
  protected override MechPowerPath[] GetMechPowerExits(
    MechPowerPath entryDir
  ) =>
#endif
    _through ? base.GetMechPowerExits(entryDir) : [];

  public override void SetOrientations() {
    OutFacingForNetworkDiscovery = _face;
    // Vanilla's sign for the axis the coupled axle runs on.
    AxisSign = _face.Axis switch {
      EnumAxis.X => [-1, 0, 0],
      EnumAxis.Y => [0, 1, 0],
      _ => [0, 0, -1],
    };
  }

  /// <summary>The filler is invisible; the principal renders the rotor, so the port adds no mesh.</summary>
  public override bool OnTesselation(
    ITerrainMeshPool mesher,
    ITesselatorAPI tesselator
  ) => false;
}
