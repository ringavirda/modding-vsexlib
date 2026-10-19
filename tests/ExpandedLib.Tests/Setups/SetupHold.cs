#if GAME_GE_1_22
using System.Collections.Generic;
using ExpandedLib.Industry.Molten;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Vintagestory.GameContent.Mechanics;

namespace ExpandedLib.Tests.Setups;

internal sealed class SetupHoldCanalBlock : Block
{
  public override bool OnBlockInteractStart(
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection blockSel
  ) =>
    MoltenChisel.TryChisel(
      world,
      byPlayer,
      blockSel.Position,
      (IChiselableMolten)world.BlockAccessor.GetBlockEntity(blockSel.Position)!,
      new AssetLocation("game:sounds/block/chisel"),
      damageChisel: false
    ) == ChiselOutcome.Chiseled;
}

internal sealed class SetupHoldCanal : BlockEntity, IChiselableMolten
{
  public BEBehaviorMoltenCell Cell { get; private set; } = null!;

  public int Ticks { get; private set; }

  public bool HasChiselableContent => Cell.Solidified && Cell.CellAmount > 0;

  public bool CanChiselOut => Cell.Solidified;

  public string? ChiselBlockedError => null;

  public override void Initialize(ICoreAPI api)
  {
    base.Initialize(api);
    Cell = new BEBehaviorMoltenCell(this);
    Behaviors.Add(Cell);
    Cell.PushMetalRaw(
      40,
      "game:ingot-iron",
      Block.Attributes["temperature"].AsFloat(),
      api.World
    );
    Cell.UpdateThermal(api.World);
    RegisterGameTickListener(_ => Ticks++, 1000);
  }

  public ItemStack? ChiselOut()
  {
    ItemStack? recovered = Cell.GetRecoveryDrop(Api.World);
    Cell.ClearContents();
    return recovered;
  }
}

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

  public SetupHoldBlock() => PlacedPriorityInteract = true;

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

internal sealed class SetupTickEntity(List<string> log) : BlockEntity
{
  public int Count;
  public int Lag;

  public override void Initialize(ICoreAPI api)
  {
    base.Initialize(api);
    RegisterGameTickListener(OnTick, 1000);
  }

  public void ListenLate() => RegisterGameTickListener(OnLate, 1000);

  public void CallLater() =>
    Api.Event.RegisterCallback(_ => log.Add("callback"), 1);

  private void OnLate(float dt) => log.Add("late");

  private void OnTick(float dt)
  {
    Count++;
    log.Add(Pos.X.ToString());
    foreach (int dx in new[] { 1, -1 })
      if (
        Api.World.BlockAccessor.GetBlockEntity(Pos.AddCopy(dx, 0, 0))
        is SetupTickEntity peer
      )
        Lag = peer.Count - Count;
  }
}

internal sealed class SetupTickBehavior(BlockEntity be, List<string> log)
  : BlockEntityBehavior(be)
{
  public override void Initialize(ICoreAPI api, JsonObject properties)
  {
    base.Initialize(api, properties);
    string x = Blockentity.Pos.X.ToString();
    List<string> sink = log;
    Blockentity.RegisterGameTickListener(_ => sink.Add(x), 1000);
  }
}

internal sealed class SetupHoldEmber : BlockEntity, IIgnitable
{
  public readonly List<float> Overs = [];

  public EnumIgniteState OnTryIgniteBlock(
    EntityAgent byEntity,
    BlockPos pos,
    float secondsIgniting
  ) =>
    secondsIgniting >= 1f
      ? EnumIgniteState.IgniteNow
      : EnumIgniteState.Ignitable;

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

internal sealed class SetupSpin(BlockEntity be) : BlockEntityBehavior(be)
{
  public void Drive(float speed, float torque)
  {
    MechanicalNetwork line = Blockentity
      .GetBehavior<BEBehaviorMPBase>()
      .Network;
    line.Speed = speed;
    line.TotalAvailableTorque = torque;
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

internal sealed class SetupMoltenEntity : BlockEntity, IMoltenCell
{
  public int CellAmount { get; set; }

  public string CellMetalType { get; set; } = "";

  public float CellTemperature { get; set; }

  public int MaxUnitCapacity => 50;

  public bool Sealed => false;

  public bool Solidified => false;

  public bool IsFlowSource => false;

  public bool AcceptsSubMinimumFlow => false;

  public void EnsureMetalStack(IWorldAccessor world) { }

  public int PushMetalRaw(
    int amount,
    string metalType,
    float temperature,
    IWorldAccessor world
  ) => 0;

  public int DrainMetal(int amount) => 0;

  public void UpdateThermal(IWorldAccessor world) { }
}
#endif
