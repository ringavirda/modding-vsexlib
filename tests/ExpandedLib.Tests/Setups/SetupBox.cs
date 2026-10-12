#if GAME_GE_1_22
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace ExpandedLib.Tests.Setups;

internal enum BoxState
{
  Idle,
  Running,
}

internal sealed class SetupBox : BlockEntityContainer
{
  private readonly InventoryGeneric _inventory = new(2, null, null, null);
  private int _count;
  private BoxState _state;

  public int Level;
  public int Dirtied;
  public int CountSeen => _count;
  public BoxState StateSeen => _state;

  public override InventoryBase Inventory => _inventory;

  public override string InventoryClassName => "setupbox";

  public override void MarkDirty(
    bool redrawOnClient = false,
    IPlayer? skipPlayer = null
  )
  {
    Dirtied++;
  }

  public override void ToTreeAttributes(ITreeAttribute tree)
  {
    base.ToTreeAttributes(tree);
    tree.SetInt("level", Level);
  }

  public override void FromTreeAttributes(
    ITreeAttribute tree,
    IWorldAccessor worldForResolving
  )
  {
    base.FromTreeAttributes(tree, worldForResolving);
    Level = tree.GetInt("level");
  }

  public string Poke(int n, BoxState state, ItemStack stack, IPlayer player) =>
    $"{n}:{state}:{stack.StackSize}:{player.PlayerUID}";
}
#endif
