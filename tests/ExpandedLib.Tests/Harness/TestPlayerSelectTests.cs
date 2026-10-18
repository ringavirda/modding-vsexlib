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

  // Fails when the active stack stays in the active slot, or the number is not answered.
  [Fact]
  public void Select_ExchangesTheStacksAndAnswersTheNumber() {
    var (world, player, _) = Stand();
    var held = new ItemStack(world.RegisterItem("game:stick"));
    var other = new ItemStack(world.RegisterItem("game:flint"));
    player.ActiveSlot.Itemstack = held;
    player.Hotbar[3].Itemstack = other;

    player.Select(3);

    Assert.Same(held, player.Hotbar[0].Itemstack);
    Assert.Same(other, player.ActiveSlot.Itemstack);
    Assert.Null(player.Hotbar[3].Itemstack);
    Assert.Equal(3, player.Player.InventoryManager.ActiveHotbarSlotNumber);
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

  // Fails when Select overwrites the stack of the slot it leaves with the empty active slot.
  [Fact]
  public void Select_KeepsAStackStowedInTheActiveHotbarSlot() {
    var (_, player, _) = Stand();
    var stowed = new ItemStack();
    player.Hotbar[0].Itemstack = stowed;

    player.Select(3);

    Assert.Same(stowed, player.Hotbar[0].Itemstack);
  }

  // Fails when both the active slot and its hotbar slot are full and one stack is silently lost.
  [Fact]
  public void Select_WithTwoStacksInTheSlotLeft_Throws() {
    var (_, player, events) = Stand();
    player.Hotbar[0].Itemstack = new ItemStack();
    player.ActiveSlot.Itemstack = new ItemStack();

    Assert.Throws<InvalidOperationException>(() => player.Select(3));
    Assert.Empty(events);
    Assert.Equal(0, player.ActiveNumber);
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

  // Fails when the inventory manager does not answer the off-hand slot and its tool.
  [Fact]
  public void TheInventoryManager_AnswersTheOffHandSlotAndItsTool() {
    var (world, player, _) = Stand();
    Item hammer = world.RegisterItem("game:hammer-copper");
    hammer.Tool = EnumTool.Hammer;

    player.HoldOffHand(new ItemStack(hammer));

    IPlayerInventoryManager manager = player.Player.InventoryManager;
    Assert.Same(player.OffHandSlot, manager.OffhandHotbarSlot);
    Assert.Equal(EnumTool.Hammer, manager.OffhandTool);
  }
}
