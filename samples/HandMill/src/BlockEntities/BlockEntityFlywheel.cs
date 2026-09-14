using System.Text;
using ExpandedLib.Blocks;
using ExpandedLib.Helpers;
using ExpandedLib.Industry.MechanicalPower;
using ExpandedLib.Machines;
using ExpandedLib.Networks;
using ExpandedLib.Registries;
using HandMill.Blocks;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace HandMill.BlockEntities;

[BlockEntityRegister]
public class BlockEntityFlywheel : ExBlockEntity, IMpEnergyStorage {
  private readonly HostMembership _membership;

  public BlockEntityFlywheel() {
    // Added in the constructor: BlockEntity fans Initialize and FromTreeAttributes out over
    // Behaviors, so a membership added later misses whichever has already run.
    _membership = new HostMembership(this);
    Behaviors.Add(_membership);
  }

  public float Inertia => HandMillValues.FlywheelInertia;

  public override void Initialize(ICoreAPI api) {
    // The axle passes through the hub: the wheel couples on both faces normal to its plane, in
    // the placed orientation.
    int angle = (Block as BlockFlywheel)?.StructureAngle ?? 0;
    _membership.Connectors = [
      ExOrientation.RotateFacing(BlockFacing.NORTH, angle),
      ExOrientation.RotateFacing(BlockFacing.SOUTH, angle),
    ];
    base.Initialize(api);
  }

  /// <summary>Stops the run this wheel is on: its stored energy is dropped to zero.</summary>
  public void Brake() {
    if (this.NetworkAt<MpEnergyNetwork>(Pos)?.State is { } state) {
      state.StoredEnergy = 0f;
      state.Speed = 0f;
    }
  }

  public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc) {
    base.GetBlockInfo(forPlayer, dsc);
    MpEnergyNetworkState? state = this.NetworkAt<MpEnergyNetwork>(Pos)?.State;
    dsc.Lang("handmill:speed", state?.Speed ?? 0f);
    dsc.Lang("handmill:energy", state?.StoredEnergy ?? 0f);
  }

  private sealed class HostMembership(BlockEntityFlywheel owner)
    : BEBehaviorNetworkMember(owner) {
    public override string NetworkType {
      get => "mpenergy";
      protected set { }
    }
  }
}
