using System.Collections.Generic;
using ExpandedLib.Machines;
using ExpandedLib.Testing;
using NSubstitute;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// The window handshake in <see cref="BlockEntityMachineStation"/>: closing must dispose the dialog
/// exactly once, and a refused open (a duplicate <see cref="GuiDialogBlockEntity"/>) must leave the
/// station able to open again rather than wedged behind a dialog that never opened.
/// </summary>
public class MachineStationWindowTests {
  private sealed class WindowedStation : BlockEntityMachineStation {
    protected override MachineSlotSpec[] SlotSpecs => [];

    public override string InventoryClassName => "test-window-station";

    public SpyDialog? LastDialog;

    protected override GuiDialogBlockEntity? CreateDialog(ICoreClientAPI capi) =>
      LastDialog = new SpyDialog("test", Pos, capi);
  }

  // Counts disposals; TryOpen's own duplicate refusal is exercised for real (by pre-populating
  // ClientApi.OpenedGuis with a dialog at the same position) rather than by overriding TryOpen here.
  private sealed class SpyDialog : GuiDialogBlockEntity {
    public int DisposeCalls;

    public SpyDialog(string title, BlockPos pos, ICoreClientAPI capi)
      : base(title, pos, capi) { }

    public override void Dispose() {
      DisposeCalls++;
      base.Dispose();
    }
  }

  private static (
    TestWorld world,
    WindowedStation station,
    TestPlayer player
  ) Setup() {
    var world = new TestWorld();

    // GuiDialog.TryOpen reads Gui.LoadedGuis (a concrete List<T>, so NSubstitute cannot auto-mock it)
    // to decide whether it must register itself.
    var gui = Substitute.For<IGuiAPI>();
    gui.LoadedGuis.Returns(new List<GuiDialog>());
    world.ClientApi.Gui.Returns(gui);
    world.ClientApi.OpenedGuis.Returns(new List<object>());

    var station = new WindowedStation {
      Pos = new BlockPos(0, 1, 0),
      Block = TestBlocks.Configure(new Block(), "test:window-station", 1),
    };
    world.Place(station.Pos, station.Block, station);
    station.Initialize(world.ClientApi);

    var player = world.Player();
    return (world, station, player);
  }

  [Fact]
  public void Closing_disposes_the_dialog_exactly_once() {
    var (world, station, player) = Setup();

    station.ToggleWindow(player.Player);
    SpyDialog dialog = station.LastDialog!;
    Assert.True(dialog.IsOpened());
    world.ClientApi.Network.Received(1).SendBlockEntityPacket(station.Pos, 1000, null);

    station.ToggleWindow(player.Player); // toggles the same window closed

    Assert.Equal(1, dialog.DisposeCalls);
    Assert.Null(ReflectionHelpers.GetField(station, "_dialog"));
    world.ClientApi.Network.Received(1).SendBlockEntityPacket(station.Pos, 1001, null);
  }

  [Fact]
  public void A_refused_open_leaves_no_dialog_and_sends_nothing() {
    var (world, station, player) = Setup();

    // A dialog already registered as opened against this station's position - TryOpen refuses a new
    // one as a duplicate, the same as a second click landing before the first dialog registers.
    var openedGuis = (List<object>)world.ClientApi.OpenedGuis;
    openedGuis.Add(new SpyDialog("stale", station.Pos, world.ClientApi));

    station.ToggleWindow(player.Player);

    Assert.Null(ReflectionHelpers.GetField(station, "_dialog"));
    Assert.Equal(1, station.LastDialog!.DisposeCalls);
    Assert.DoesNotContain(
      player.Player.PlayerUID,
      station.Inventory.openedByPlayerGUIds
    );
    world.ClientApi.Network.DidNotReceive().SendPacketClient(Arg.Any<object>());
    world.ClientApi.Network
      .DidNotReceive()
      .SendBlockEntityPacket(
        Arg.Any<BlockPos>(),
        Arg.Any<int>(),
        Arg.Any<byte[]>()
      );
  }

  [Fact]
  public void A_later_toggle_opens_again_after_a_refusal() {
    var (world, station, player) = Setup();

    var openedGuis = (List<object>)world.ClientApi.OpenedGuis;
    var stale = new SpyDialog("stale", station.Pos, world.ClientApi);
    openedGuis.Add(stale);
    station.ToggleWindow(player.Player);
    Assert.Null(ReflectionHelpers.GetField(station, "_dialog"));

    openedGuis.Remove(stale);
    station.ToggleWindow(player.Player);

    Assert.NotNull(ReflectionHelpers.GetField(station, "_dialog"));
  }
}
