#if GAME_GE_1_22
using System.Collections.Generic;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ExpandedLib.Tests.Setups;

internal sealed class SetupHoldBlock : Block, IIgnitable
{
  public readonly List<string> Calls = [];
  public readonly List<float> Overs = [];
  public int Tries;
  public bool SneakAtStart;
  public bool CtrlAtStart;
  public bool SprintAtStart;
  public ItemStack? HeldAtStart;
  public float HeldTemperature;

  public override bool OnBlockInteractStart(
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection blockSel
  )
  {
    Calls.Add("start");
    SneakAtStart = byPlayer.Entity.Controls.ShiftKey;
    CtrlAtStart = byPlayer.Entity.Controls.CtrlKey;
    SprintAtStart = byPlayer.Entity.Controls.Sprint;
    HeldAtStart = byPlayer.InventoryManager.ActiveHotbarSlot.Itemstack;
    HeldTemperature =
      HeldAtStart?.Collectible.GetTemperature(world, HeldAtStart) ?? 0f;
    return Attributes?["takes"].AsBool(true) ?? true;
  }

  public override bool OnBlockInteractStep(
    float secondsUsed,
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection blockSel
  )
  {
    Calls.Add($"step {secondsUsed:0.0}");
    return secondsUsed
      < (Attributes?["declinesAt"].AsFloat(float.MaxValue) ?? float.MaxValue);
  }

  public override void OnBlockInteractStop(
    float secondsUsed,
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection blockSel
  ) => Calls.Add($"stop {secondsUsed:0.0}");

  public EnumIgniteState OnTryIgniteBlock(
    EntityAgent byEntity,
    BlockPos pos,
    float secondsIgniting
  )
  {
    Tries++;
    float at = Attributes?["ignitesAt"].AsFloat(-1f) ?? -1f;
    return at < 0f ? EnumIgniteState.NotIgnitable
      : secondsIgniting >= at ? EnumIgniteState.IgniteNow
      : EnumIgniteState.Ignitable;
  }

  public EnumIgniteState OnTryIgniteStack(
    EntityAgent byEntity,
    BlockPos pos,
    ItemSlot slot,
    float secondsIgniting
  ) => EnumIgniteState.NotIgnitable;

  public void OnTryIgniteBlockOver(
    EntityAgent byEntity,
    BlockPos pos,
    float secondsIgniting,
    ref EnumHandling handling
  ) => Overs.Add(secondsIgniting);
}

internal sealed class SetupHoldEntity : BlockEntity
{
  public int Fires;

  public override void Initialize(ICoreAPI api)
  {
    base.Initialize(api);
    RegisterGameTickListener(_ => Fires++, 1000);
  }
}

internal sealed class SetupHoldBox : BlockEntityContainer
{
  private readonly InventoryGeneric _inventory = new(2, null, null, null);
  public readonly List<(int Packet, byte[]? Data, int Slot0)> Packets = [];

  public override InventoryBase Inventory => _inventory;

  public override string InventoryClassName => "setuphold-box";

  public override void OnReceivedClientPacket(
    IPlayer player,
    int packetid,
    byte[] data
  )
  {
    if (packetid == TestWindow.ClosePacketId)
      player.InventoryManager?.CloseInventory(Inventory);
    else if (packetid < TestWindow.OpenPacketId)
      Inventory.InvNetworkUtil.HandleClientPacket(player, packetid, data);
    else if (packetid == TestWindow.OpenPacketId)
      player.InventoryManager?.OpenInventory(Inventory);
    Packets.Add((packetid, data, _inventory[0].Itemstack?.StackSize ?? 0));
  }
}
#endif
