using ExpandedLib.Registries;
using ExpandedLib.Structures;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent.Mechanics;

namespace ExpandedLib.Industry.MechanicalPower;

/// <summary>
/// A minimal mechanical-power node a mega-block hosts on one of its invisible footprint cells (see
/// <see cref="StructureFillers"/> / <see cref="IFillerHostedBehavior"/>), giving the MP network a
/// participant at the cell where an axle couples - the principal block, two cells away, cannot accept
/// power at that face. The port renders nothing and only loads the network with a configurable
/// resistance; the principal reads back the resulting <see cref="BEBehaviorMPBase.Network"/> speed and
/// angle to drive its parts in sync. Orientation comes from the principal:
/// <see cref="ConfigureFromFiller"/> passes the connector face already rotated into the placed
/// orientation, not the shared filler block's own always-north variant.
/// </summary>
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

  /// <summary>
  /// The network's current rotation angle (radians), for phase-locking a driven part to the axle via
  /// <see cref="ExpandedLib.Industry.MechanicalPower.MPAnim.AdvanceFrame"/>; 0 when the port has no network.
  /// </summary>
  public float CurrentAngleRad => Network != null ? AngleRad : 0f;

  /// <summary>
  /// The coupled axle's angle (radians) as a turn about the axis running from the port face into
  /// the machine, in the sense vanilla draws the axle: what a shaft keyframed as a positive turn
  /// about that axis locks to. Vanilla signs a horizontal axle's rotation negative along its axis,
  /// so this reads <see cref="CurrentAngleRad"/> negated on a west or north port and straight on an
  /// east or south one; 0 when the port has no network.
  /// </summary>
  public float DrivenAngleRad {
    get {
      Vec3i n = _face.Normali;
      return -(AxisSign[0] * n.X + AxisSign[1] * n.Y + AxisSign[2] * n.Z)
        * CurrentAngleRad;
    }
  }

  /// <summary>True while the axle is turning, that is, while the port delivers power.</summary>
  public bool IsTurning => Network is { Speed: > 0.001f or < -0.001f };

  /// <summary>
  /// This port's own rotation speed, absolute; 0 when the port has no network. A principal scales the
  /// work it does by it.
  /// <para>
  /// Geared through <see cref="BEBehaviorMPBase.GearedRatio"/>, as vanilla's
  /// <c>BEBehaviorMPConsumer.TrueSpeed</c> is. A network holds ONE speed, in the frame of whichever
  /// node seeded it, so reading it raw makes a machine behind a gear train report the drive's speed
  /// or its own depending on chunk load order.
  /// </para>
  /// </summary>
  public float Speed =>
    Network != null ? System.Math.Abs(Network.Speed * GearedRatio) : 0f;

  /// <summary>
  /// Which way the axle turns: true when the vanilla network runs negative. <see cref="Speed"/> is
  /// absolute, so a machine whose geometry depends on rotation direction reads the sign here - the
  /// rolling mill's feed side follows the rolls, so reversing the drive swaps input and output decks.
  /// </summary>
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

  /// <summary>
  /// Whether the port also couples the face opposite its own, so a row of ports along one axis
  /// joins into one line an axle drives from either end (the default). A machine that takes power
  /// on one side only declares <c>through: false</c> in the port's filler properties, and the cell
  /// then accepts an axle on the port face alone.
  /// </summary>
  public bool Through => _through;

  public override void Initialize(ICoreAPI api, JsonObject properties) {
    base.Initialize(api, properties);

    // The base seeds OutFacingForNetworkDiscovery but joins nothing on its own: an axle placed later
    // spreads its network into the port, and a port raised beside an axle that already turns has to
    // reach out itself. A through port couples the opposite end of the axis too, so a row of ports
    // merges into one line an axle drives from either side.
    if (api.Side == EnumAppSide.Server && OutFacingForNetworkDiscovery != null) {
      tryConnect(OutFacingForNetworkDiscovery);
      if (_through)
        tryConnect(OutFacingForNetworkDiscovery.Opposite);
    }
  }

  public override float GetResistance() => _resistance;

  /// <summary>
  /// The faces power leaves this port by: both ends of the axis for a through port, none for a
  /// one-sided one. The path's out-facing is its direction of travel, so a one-sided port ends the
  /// network the way a vanilla consumer does rather than passing it out of the machine's far side.
  /// </summary>
  public override MechPowerPath[] GetMechPowerExits(MechPowerPath entryDir) =>
    _through ? base.GetMechPowerExits(entryDir) : [];

  public override void SetOrientations() {
    OutFacingForNetworkDiscovery = _face;
    // Vanilla's sign for the axis the coupled axle runs on, so the port turns as that axle is drawn.
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
