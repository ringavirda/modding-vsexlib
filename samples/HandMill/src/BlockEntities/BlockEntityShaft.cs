using ExpandedLib.Industry.MechanicalPower;
using ExpandedLib.Networks;
using ExpandedLib.Registries;

namespace HandMill.BlockEntities;

/// <summary>
/// Block entity for a <see cref="Blocks.BlockShaft"/>: a pass-through node of the mpenergy run that
/// also acts as a small <see cref="IMpEnergyStorage"/> buffer. Graph membership (add, remove,
/// connectivity) is handled by the <see cref="BlockEntityNetworkNode"/> base.
/// </summary>
[BlockEntityRegister]
public class BlockEntityShaft : BlockEntityNetworkNode, IMpEnergyStorage {
  public override string NetworkType {
    get => "mpenergy";
    set { }
  }

  public float Inertia => HandMillValues.ShaftInertia;
}
