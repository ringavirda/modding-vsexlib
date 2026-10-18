using System;
using System.Collections.Generic;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary><see cref="TestPlayer.Select"/>: the active hotbar slot changed as the number keys do.</summary>
public class TestPlayerSelectTests {
  private static (TestWorld World, TestPlayer Player, List<(int From, int To)> Events) Stand() {
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
    var (_, player, _) = Stand();
    var held = new ItemStack();
    var other = new ItemStack();
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
}
