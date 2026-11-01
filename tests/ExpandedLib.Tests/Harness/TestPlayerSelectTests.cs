using System;
using System.Collections.Generic;
using ExpandedLib.Testing;
using Newtonsoft.Json.Linq;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary><see cref="TestPlayer.Select"/>: the active hotbar slot changed as the number keys do.</summary>
public class TestPlayerSelectTests {
  private static (
    TestWorld World,
    TestPlayer Player,
    List<(int From, int To)> Events
  ) Stand() {
    var world = new TestWorld();
    TestPlayer player = world.Player();
    var events = new List<(int, int)>();
    world.Api.Event.AfterActiveSlotChanged += (_, args) =>
      events.Add((args.FromSlot, args.ToSlot));
    return (world, player, events);
  }

  // Fails when the slot is set and no AfterActiveSlotChanged is raised.
  [Fact]
  public void Select_RaisesActiveSlotChange() {
    var (_, player, events) = Stand();

    player.Select(3);

    Assert.Equal([(0, 3)], events);
  }

  // Fails when Select moves a stack between hotbar slots, or the number is not answered.
  [Fact]
  public void Select_LeavesEachStackInItsOwnHotbarSlot() {
    var (world, player, _) = Stand();
    var held = new ItemStack(world.RegisterItem("game:stick"));
    var other = new ItemStack(world.RegisterItem("game:flint"));
    player.Hold(held);
    player.Hotbar[3].Itemstack = other;

    player.Select(3);

    Assert.Same(held, player.Hotbar[0].Itemstack);
    Assert.Same(other, player.Hotbar[3].Itemstack);
    Assert.Same(other, player.ActiveSlot.Itemstack);
    Assert.Equal(3, player.Player.InventoryManager.ActiveHotbarSlotNumber);
  }

  // Fails when the stack in hand is missing from the hotbar inventory at the active number.
  [Fact]
  public void AStackInHand_IsInTheHotbarInventoryAtTheActiveNumber() {
    var (world, player, _) = Stand();
    var held = new ItemStack(world.RegisterItem("game:stick"));
    player.Select(4);

    player.Hold(held);

    IPlayerInventoryManager manager = player.Player.InventoryManager;
    Assert.Same(held, manager.GetHotbarInventory()[4].Itemstack);
    Assert.Same(manager.GetHotbarInventory()[4], manager.ActiveHotbarSlot);
    Assert.Same(manager.ActiveHotbarSlot, player.Entity.RightHandItemSlot);
    Assert.Same(held, player.StackInSlot(4));
  }

  // Fails when selecting the active slot again raises an event.
  [Fact]
  public void Select_OfTheActiveSlot_RaisesNothing() {
    var (_, player, events) = Stand();

    player.Select(0);

    Assert.Empty(events);
  }

  // Fails when a slot past the hotbar is taken.
  [Fact]
  public void Select_OfAnOffHotbarSlot_Throws() {
    var (_, player, events) = Stand();

    Assert.Throws<ArgumentOutOfRangeException>(() => player.Select(10));
    Assert.Throws<ArgumentOutOfRangeException>(() => player.Select(-1));
    Assert.Empty(events);
  }

  private static ItemStack Heated(TestWorld world, string code, float degrees) {
    var stack = new ItemStack(world.RegisterItem(code));
    stack.Collectible.SetTemperature(world.World, stack, degrees);
    return stack;
  }

  // Fails when a BeforeActiveSlotChanged veto does not stop the change.
  [Fact]
  public void Select_VetoedByABeforeHandler_ChangesNothing() {
    var (world, player, events) = Stand();
    world.Api.Event.BeforeActiveSlotChanged += (_, _) =>
      EnumHandling.PreventDefault;

    player.Select(3);

    Assert.Equal(0, player.ActiveNumber);
    Assert.Empty(events);
  }

  // Fails when the Before event is not raised with the old and the new number.
  [Fact]
  public void Select_AsksBeforeHandlersWithTheNumbers() {
    var (world, player, _) = Stand();
    var asked = new List<(int, int)>();
    world.Api.Event.BeforeActiveSlotChanged += (_, args) => {
      asked.Add((args.FromSlot, args.ToSlot));
      return EnumHandling.PassThrough;
    };

    player.Select(3);

    Assert.Equal([(0, 3)], asked);
    Assert.Equal(3, player.ActiveNumber);
  }

  // Fails when a stack above the touch temperature stays in the hand of a player without tongs.
  [Fact]
  public void Select_OfATooHotStack_DropsIt() {
    var (world, player, _) = Stand();
    player.Hotbar[3].Itemstack = Heated(world, "game:ingot-iron", 900f);

    player.Select(3);

    Assert.Null(player.ActiveSlot.Itemstack);
    player.Player.InventoryManager.Received().DropItem(player.ActiveSlot, true);
  }

  // Fails when a cold stack is dropped.
  [Fact]
  public void Select_OfAColdStack_KeepsIt() {
    var (world, player, _) = Stand();
    ItemStack cold = Heated(world, "game:ingot-iron", 20f);
    player.Hotbar[3].Itemstack = cold;

    player.Select(3);

    Assert.Same(cold, player.ActiveSlot.Itemstack);
  }

  // Fails when a heat resistant off-hand item does not spare the stack.
  [Fact]
  public void Select_OfATooHotStack_KeepsItWithHeatResistantOffHand() {
    var (world, player, _) = Stand();
    Item tongs = world.RegisterItem("game:tongs");
    tongs.Attributes = new JsonObject(JToken.Parse("{ heatResistant: true }"));
    player.HoldOffHand(new ItemStack(tongs));
    ItemStack hot = Heated(world, "game:ingot-iron", 900f);
    player.Hotbar[3].Itemstack = hot;

    player.Select(3);

    Assert.Same(hot, player.ActiveSlot.Itemstack);
  }

  // Fails when the creative game mode is not exempt from the drop.
  [Fact]
  public void Select_OfATooHotStack_KeepsItInCreative() {
    var (world, player, _) = Stand();
    player.GameMode = EnumGameMode.Creative;
    ItemStack hot = Heated(world, "game:ingot-iron", 900f);
    player.Hotbar[3].Itemstack = hot;

    player.Select(3);

    Assert.Same(hot, player.ActiveSlot.Itemstack);
  }

#if GAME_GE_1_21
  // Fails when the inventory manager does not answer the off-hand slot and its tool.
  [Fact]
  public void TheInventoryManager_AnswersTheOffHandSlotAndItsTool()
  {
    var (world, player, _) = Stand();
    Item hammer = world.RegisterItem("game:hammer-copper");
    hammer.Tool = EnumTool.Hammer;

    player.HoldOffHand(new ItemStack(hammer));

    IPlayerInventoryManager manager = player.Player.InventoryManager;
    Assert.Same(player.OffHandSlot, manager.OffhandHotbarSlot);
    Assert.Equal(EnumTool.Hammer, manager.OffhandTool);
  }
#endif

  // Fails when TryGiveItemstack returns false or leaves the stack out of the hotbar.
  [Fact]
  public void TryGiveItemstack_PutsTheStackInTheFirstEmptyHotbarSlot() {
    var (world, player, _) = Stand();
    var stack = new ItemStack(world.RegisterItem("game:ingot-iron"));

    bool given = player.Player.InventoryManager.TryGiveItemstack(stack);

    Assert.True(given);
    Assert.Equal(0, stack.StackSize);
    Assert.Equal(
      "game:ingot-iron",
      player.ActiveSlot.Itemstack?.Collectible.Code.ToString()
    );
    Assert.Same(player.ActiveSlot.Itemstack, player.StackInSlot(0));
  }

  // Fails when a null stack throws instead of returning false.
  [Fact]
  public void TryGiveItemstack_OfNull_ReturnsFalse() {
    var (_, player, _) = Stand();

    Assert.False(player.Player.InventoryManager.TryGiveItemstack(null!));
  }

  // Fails when a hot stack given into the empty hand is kept.
  [Fact]
  public void TryGiveItemstack_OfAHotStack_DropsItFromTheHand() {
    var (world, player, _) = Stand();
    ItemStack hot = Heated(world, "game:ingot-iron", 1000);

    player.Player.InventoryManager.TryGiveItemstack(hot);

    player.Player.InventoryManager.Received().DropItem(player.ActiveSlot, true);
    Assert.Null(player.ActiveSlot.Itemstack);
  }

  // Fails when the stack held in the active slot is passed over for an empty slot instead of merged into.
  [Fact]
  public void TryGiveItemstack_MergesIntoTheHeldStack() {
    var (world, player, _) = Stand();
    Item ingot = world.RegisterItem("game:ingot-iron");
    ingot.MaxStackSize = 64;
    var held = new ItemStack(ingot, 3);
    player.Hold(held);

    player.Player.InventoryManager.TryGiveItemstack(new ItemStack(ingot, 2));

    Assert.Same(held, player.ActiveSlot.Itemstack);
    Assert.Equal(5, held.StackSize);
    Assert.Null(player.Hotbar[1].Itemstack);
  }

  // Fails when a stack given for an item already in another hotbar slot throws or lands in a new slot instead of merging.
  [Fact]
  public void TryGiveItemstack_OfOfAHeldElsewhereItem_MergesIntoItsSlot() {
    var (world, player, _) = Stand();
    Item bits = world.RegisterItem("game:metalbit-copper");
    bits.MaxStackSize = 64;

    player.Hold(new ItemStack(world.RegisterItem("game:stick")));
    player.Hotbar[2].Itemstack = new ItemStack(bits, 3);

    bool given = player.Player.InventoryManager.TryGiveItemstack(
      new ItemStack(bits, 2)
    );

    Assert.True(given);
    Assert.Equal(5, player.Hotbar[2].Itemstack?.StackSize);
    Assert.Null(player.Hotbar[1].Itemstack);
  }

  // Fails when a collectible registered through TestWorld.Register has no api and its stacks throw on comparison.
  [Fact]
  public void TryGiveItemstack_OfARegisteredBlockAndItem_MergesWithoutThrowing() {
    var (world, player, _) = Stand();
    Item bits = new() {
      Code = new AssetLocation("game:metalbit-tin"),
      ItemId = 900,
      MaxStackSize = 64,
    };
    Block mould = TestBlocks.Configure(new Block(), "game:mould", 901);
    mould.MaxStackSize = 64;
    world.Register(bits).Register(mould);
    player.Hold(new ItemStack(world.RegisterItem("game:stick")));
    player.Hotbar[2].Itemstack = new ItemStack(bits, 3);
    player.Hotbar[3].Itemstack = new ItemStack(mould, 1);

    bool bitsGiven = player.Player.InventoryManager.TryGiveItemstack(
      new ItemStack(bits, 2)
    );
    bool mouldGiven = player.Player.InventoryManager.TryGiveItemstack(
      new ItemStack(mould, 1)
    );

    Assert.True(bitsGiven);
    Assert.True(mouldGiven);
    Assert.Equal(5, player.Hotbar[2].Itemstack?.StackSize);
    Assert.Equal(2, player.Hotbar[3].Itemstack?.StackSize);
  }

  // Fails when the skill or off hand slot takes a general stack, or a stack with no slot is reported as given or loses its size.
  [Fact]
  public void TryGiveItemstack_OfAFullHotbar_ReturnsFalseAndKeepsTheStack() {
    var (world, player, _) = Stand();
    Item ingot = world.RegisterItem("game:ingot-iron");
    for (int i = 0; i < 10; i++)
      player.Hotbar[i].Itemstack = new ItemStack(
        world.RegisterItem("game:stick")
      );
    var stack = new ItemStack(ingot);

    bool given = player.Player.InventoryManager.TryGiveItemstack(stack);

    Assert.False(given);
    Assert.Null(player.Hotbar[10].Itemstack);
    Assert.Null(player.Hotbar[11].Itemstack);
    Assert.Equal(1, stack.StackSize);
  }
}
