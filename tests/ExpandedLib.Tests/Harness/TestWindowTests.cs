using System;
using System.Collections.Generic;
using ExpandedLib.Machines;
using ExpandedLib.Testing;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary><see cref="TestWindow"/> over <see cref="BlockEntityMachineStation"/> and a container
/// running the reinforced hopper's handshake: each verb reaches the entity as the dialog's packet
/// and moves stacks only through the engine's slot protocol.</summary>
public class TestWindowTests {
  private sealed class Station : BlockEntityMachineStation {
    public readonly List<(int Id, byte[]? Data)> Pressed = new();

    protected override MachineSlotSpec[] SlotSpecs =>
      [MachineSlotSpec.AnyInput(), MachineSlotSpec.Output()];

    public override string InventoryClassName => "test-window-station";

    protected override bool OnStationPacket(
      IPlayer player,
      int packetid,
      byte[] data
    ) {
      Pressed.Add((packetid, data));
      return true;
    }
  }

  // The reinforced hopper's handshake over a plain container: close, claim check, slot packets,
  // open.
  private sealed class Hopper : BlockEntityContainer {
    private readonly InventoryGeneric _inventory = new(4, null, null);

    public override InventoryBase Inventory => _inventory;

    public override string InventoryClassName => "test-hopper";

    public override void OnReceivedClientPacket(
      IPlayer player,
      int packetid,
      byte[] data
    ) {
      if (packetid == 1001) {
        player.InventoryManager?.CloseInventory(Inventory);
        return;
      }
      if (!Api.World.Claims.TryAccess(player, Pos, EnumBlockAccessFlags.Use))
        return;
      if (packetid < 1000) {
        Inventory.InvNetworkUtil.HandleClientPacket(player, packetid, data);
        return;
      }
      if (packetid == 1000)
        player.InventoryManager?.OpenInventory(Inventory);
    }
  }

  private sealed class Bare : BlockEntity { }

  private static readonly BlockPos At = new(0, 1, 0);

  private static (TestWorld, T, TestPlayer, Item) Stand<T>(T entity)
    where T : BlockEntity {
    var world = new TestWorld();
    Block block = TestBlocks.Configure(new Block(), "test:window", 1);
    entity.Pos = At.Copy();
    world.Place(At, block, entity);
    world.Initialize(entity);
    if (entity is BlockEntityMachineStation station)
      station.DisablePickRangeCheck();
    Item ingot = world.RegisterItem("game:ingot-iron");
    ingot.MaxStackSize = 64;
    return (world, entity, world.Player(), ingot);
  }

  private static void RefuseClaims(TestWorld world) =>
    world
      .World.Claims.TryAccess(
        Arg.Any<IPlayer>(),
        Arg.Any<BlockPos>(),
        Arg.Any<EnumBlockAccessFlags>()
      )
      .Returns(false);

  #region Station

  // Fails when the put does not reach the station's slot packet path.
  [Fact]
  public void A_put_lands_in_the_slot_and_empties_the_hand() {
    var (world, station, player, ingot) = Stand(new Station());
    TestWindow window = player.Window(station);
    player.Hold(new ItemStack(ingot, 5));

    window.Open();
    int moved = window.Put(0);

    Assert.Equal(5, moved);
    Assert.Equal(5, station.Inventory[0].StackSize);
    Assert.True(player.ActiveSlot.Empty);
    Assert.True(player.Mouse[0].Empty);
    Assert.True(station.Inventory.HasOpened(player.Player));
  }

  // Fails when the verb writes the slot itself: the output slot's own rule refuses a put only on the
  // engine's slot path.
  [Fact]
  public void A_put_into_an_output_slot_is_refused_by_the_slot() {
    var (world, station, player, ingot) = Stand(new Station());
    TestWindow window = player.Window(station);
    player.Hold(new ItemStack(ingot, 5));

    window.Open();
    int moved = window.Put(1);

    Assert.Equal(0, moved);
    Assert.True(station.Inventory[1].Empty);
    Assert.Equal(5, player.ActiveSlot.StackSize);
  }

  // Fails when the verb or the harness refuses a put the game's slot-click path lands without an
  // open.
  [Fact]
  public void A_put_without_open_lands_as_the_games_slot_click_does() {
    var (world, station, player, ingot) = Stand(new Station());
    player.Hold(new ItemStack(ingot, 3));

    int moved = player.Window(station).Put(0);

    Assert.Equal(3, moved);
    Assert.Equal(3, station.Inventory[0].StackSize);
    Assert.False(station.Inventory.HasOpened(player.Player));
  }

  // Fails when the verb writes the slot itself, bypassing the station's claim check, or the station
  // stops rolling the client back.
  [Fact]
  public void A_put_without_claim_access_changes_nothing_and_rolls_back() {
    var (world, station, player, ingot) = Stand(new Station());
    TestWindow window = player.Window(station);
    player.Hold(new ItemStack(ingot, 5));
    window.Open();
    world.Api.Network.ClearReceivedCalls();
    RefuseClaims(world);

    int moved = window.Put(0);

    Assert.Equal(0, moved);
    Assert.True(station.Inventory[0].Empty);
    Assert.Equal(5, player.ActiveSlot.StackSize);
#if GAME_GE_1_22
    if (player.ServerPlayer != null)
      world
        .Api.Network.Received(1)
        .SendArbitraryPacket(Arg.Any<object>(), Arg.Any<IServerPlayer[]>());
#endif
  }

  // Fails when the take does not move the slot's stack to the hand through the cursor.
  [Fact]
  public void A_take_moves_the_slots_stack_into_the_hand() {
    var (world, station, player, ingot) = Stand(new Station());
    TestWindow window = player.Window(station);
    station.Inventory[0].Itemstack = new ItemStack(ingot, 4);

    window.Open();
    ItemStack? taken = window.Take(0);

    Assert.Equal(4, taken?.StackSize);
    Assert.Same(taken, player.ActiveSlot.Itemstack);
    Assert.True(station.Inventory[0].Empty);
    Assert.True(player.Mouse[0].Empty);
  }

  // Fails when a button's packet does not reach the station's own handler with its id and data.
  [Fact]
  public void A_button_reaches_the_station_with_its_id_and_data() {
    var (world, station, player, ingot) = Stand(new Station());
    TestWindow window = player.Window(station);
    byte[] data = [7, 9];

    window.Open();
    window.Press(BlockEntityMachineStation.FirstMachinePacketId, data);

    var (id, sent) = Assert.Single(station.Pressed);
    Assert.Equal(BlockEntityMachineStation.FirstMachinePacketId, id);
    Assert.Same(data, sent);
  }

  // Fails when Close does not send the close packet the station closes the inventory on.
  [Fact]
  public void Closing_closes_the_inventory_for_the_player() {
    var (world, station, player, ingot) = Stand(new Station());
    TestWindow window = player.Window(station);

    window.Open();
    window.Close();

    Assert.False(station.Inventory.HasOpened(player.Player));
  }

  [Fact]
  public void A_button_id_at_or_below_close_is_refused() {
    var (world, station, player, ingot) = Stand(new Station());

    Assert.Throws<ArgumentOutOfRangeException>(() =>
      player.Window(station).Press(TestWindow.ClosePacketId)
    );
  }

  [Fact]
  public void A_put_with_an_empty_hand_or_a_full_cursor_throws() {
    var (world, station, player, ingot) = Stand(new Station());
    TestWindow window = player.Window(station);

    Assert.Throws<InvalidOperationException>(() => window.Put(0));

    player.Hold(new ItemStack(ingot, 1));
    player.Mouse[0].Itemstack = new ItemStack(ingot, 1);
    Assert.Throws<InvalidOperationException>(() => window.Put(0));
  }

  [Fact]
  public void A_take_with_a_full_hand_throws() {
    var (world, station, player, ingot) = Stand(new Station());
    player.Hold(new ItemStack(ingot, 1));

    Assert.Throws<InvalidOperationException>(() =>
      player.Window(station).Take(0)
    );
  }

  [Fact]
  public void A_slot_verb_on_an_entity_without_an_inventory_throws() {
    var (world, _, player, ingot) = Stand(new Station());
    player.Hold(new ItemStack(ingot, 1));

    Assert.Throws<InvalidOperationException>(() =>
      player.Window(new Bare()).Put(0)
    );
  }

  #endregion

  #region Container

  // Fails when the hopper's own handshake does not run under the verbs: a put, a take, and a put
  // its claim check refuses.
  [Fact]
  public void The_hoppers_handshake_takes_a_top_off_and_refuses_without_claims() {
    var (world, hopper, player, ingot) = Stand(new Hopper());
    TestWindow window = player.Window(hopper);
    player.Hold(new ItemStack(ingot, 8));

    window.Open();
    Assert.Equal(8, window.Put(2));
    Assert.Equal(8, window.Take(2)?.StackSize);
    Assert.True(hopper.Inventory[2].Empty);

    RefuseClaims(world);
    Assert.Equal(0, window.Put(2));
    Assert.True(hopper.Inventory[2].Empty);
    Assert.Equal(8, player.ActiveSlot.StackSize);
  }

  #endregion
}
