#if GAME_GE_1_22
using System.Text;
using ExpandedLib.Helpers;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace ExpandedLib.Tests.Setups;

internal enum BoxState
{
  Idle,
  Running,
}

internal sealed class SetupBox : BlockEntityContainer, IDrawsShape
{
  private readonly InventoryGeneric _inventory = new(2, null, null, null);
  private int _count;
  private BoxState _state;

  public int Level;
  public long Big;
  public float Ratio;
  public double Exact;
  public bool On;
  public string Label = "";
  public (int N, BoxState State, ItemStack Stack, IPlayer Player)? Poked;
  public int Dirtied;
  public int CountSeen => _count;
  public BoxState StateSeen => _state;

  public AssetLocation DrawnShape => new("setupbox", $"box-{Level}");

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
    tree.SetLong("big", Big);
    tree.SetFloat("ratio", Ratio);
    tree.SetDouble("exact", Exact);
    tree.SetBool("on", On);
    tree.SetString("label", Label);
    tree.SetBytes("raw", []);
  }

  public override void FromTreeAttributes(
    ITreeAttribute tree,
    IWorldAccessor worldForResolving
  )
  {
    base.FromTreeAttributes(tree, worldForResolving);
    Level = tree.GetInt("level");
    Big = tree.GetLong("big");
    Ratio = tree.GetFloat("ratio");
    Exact = tree.GetDouble("exact");
    On = tree.GetBool("on");
    Label = tree.GetString("label");
  }

  public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
  {
    base.GetBlockInfo(forPlayer, dsc);
    dsc.AppendLine($"Level: {Level}");
  }

  public bool Spilling;

  public void Arm() => Spilling = true;

  public void Spill(string code, int count, int dx, int dy, int dz)
  {
    var at = new AssetLocation(code);
    CollectibleObject item =
      (CollectibleObject?)Api.World.GetItem(at) ?? Api.World.GetBlock(at);
    Api.World.SpawnItemEntity(
      new ItemStack(item, count),
      Pos.ToVec3d().Add(dx + 0.5, dy + 0.5, dz + 0.5)
    );
  }

  public override void Initialize(ICoreAPI api)
  {
    base.Initialize(api);
    RegisterGameTickListener(
      _ =>
      {
        if (Spilling)
          Spill("setupbox:box", 1, 0, 0, 0);
      },
      1000
    );
  }

  public string Poke(int n, BoxState state, ItemStack stack, IPlayer player)
  {
    Poked = (n, state, stack, player);
    return $"{n}:{state}:{stack.StackSize}:{player.PlayerUID}";
  }
}

internal sealed class BoxBehaviour(BlockEntity be) : BlockEntityBehavior(be)
{
  public int Nudged;

  public int Nudge(int by) => Nudged += by;

  public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc) =>
    dsc.AppendLine($"Nudged: {Nudged}");
}
#endif
