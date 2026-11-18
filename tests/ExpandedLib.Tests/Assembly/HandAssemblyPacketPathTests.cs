using System.Linq;
using ExpandedLib.Assembling;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Xunit;
using Scene = ExpandedLib.Tests.HandAssemblyFixTests.Scene;

namespace ExpandedLib.Tests;

/// <summary>Fixes and take-offs driven from the client's hold through the packet bytes to the server, and where the bolts a joint spent end up.</summary>
public class HandAssemblyPacketPathTests {
  private const double HoldSeconds = 10;

  private static readonly BlockPos Cell = new(64, 16, 64, 0);
  private static readonly BlockPos Ground = new(64, 15, 64, 0);
  private static readonly BlockPos North = Cell.AddCopy(BlockFacing.NORTH);
  private static readonly BlockPos FarNorth = North.AddCopy(BlockFacing.NORTH);

  private sealed class Rig {
    private Rig(Scene scene) => Scene = scene;

    public Scene Scene { get; }

    public static Rig Standing(int bolts) {
      var scene = Scene.Standing(["ns"]);
      scene.Bolts(bolts);
      return new Rig(scene);
    }

    public int Pieces =>
      Scene.Player.Hotbar.Sum(slot =>
        slot.Itemstack is { Block: { } block } stack
        && block.Code.Path.StartsWith("pipe-")
          ? stack.StackSize
          : 0
      );

    public int Spawned(Item item) =>
      Scene
        .World.Spawns.Where(s => s.Stack.Item == item)
        .Sum(s => s.Stack.StackSize);

    public void Fix(BlockPos aimed, BlockFacing face, EnumGameMode mode) {
      Scene.Player.GameMode = mode;
      Scene.Player.Hold(new ItemStack(Scene.Part("ns")));
      Scene.Player.Sneaking = true;
      var driver = new HandAssemblyControlTests.Driver(
        Scene,
        Scene.Select(aimed, face)
      ).Start();
      driver.Hold(HoldSeconds, sneak: true);

      AssemblyFix sent = Assert.IsType<AssemblyFix>(
        Assert.Single(driver.Channel.Sent)
      );
      AssemblyFix arrived = SerializerUtil.Deserialize<AssemblyFix>(
        SerializerUtil.Serialize(sent)
      );
      Assert.Null(
        HandAssemblyServer.Fix(Scene.World.World, Scene.Player.Player, arrived)
      );
    }

    public void TakeOff(BlockPos pos, EnumGameMode mode) {
      Scene.Player.GameMode = mode;
      Scene.Player.Hold(new ItemStack(Scene.Wrench));
      Scene.Player.Sneaking = true;
      var driver = new HandAssemblyControlTests.Driver(
        Scene,
        Scene.Select(pos, BlockFacing.UP)
      ).Start();
      driver.Hold(HoldSeconds, sneak: true);

      AssemblyTakeOff sent = Assert.IsType<AssemblyTakeOff>(
        Assert.Single(driver.Channel.Sent)
      );
      AssemblyTakeOff arrived = SerializerUtil.Deserialize<AssemblyTakeOff>(
        SerializerUtil.Serialize(sent)
      );
      Assert.Null(
        HandAssemblyServer.TakeOff(
          Scene.World.World,
          Scene.Player.Player,
          arrived
        )
      );
    }

    public Rig Pair(EnumGameMode mode = EnumGameMode.Survival) {
      Fix(Ground, BlockFacing.UP, mode);
      Fix(Cell, BlockFacing.NORTH, mode);
      return this;
    }
  }

  // Fails when a survival take-off of the later piece through the packet does not give its joint's bolts back.
  [Fact]
  public void SurvivalFixThenTakeOff_LaterPieceBoltsLand() {
    Rig rig = Rig.Standing(10).Pair();
    Assert.Equal(8, rig.Scene.Carried);

    rig.TakeOff(North, EnumGameMode.Survival);

    Assert.Equal(10, rig.Scene.Carried);
    Assert.Equal(1, rig.Pieces);
    Assert.Equal(0, rig.Scene.World.GetBlock(North).Id);
  }

  // Fails when a survival take-off of the earlier piece does not give back the bolts the later piece holds toward it.
  [Fact]
  public void SurvivalFixThenTakeOff_EarlierPieceBoltsLand() {
    Rig rig = Rig.Standing(10).Pair();

    rig.TakeOff(Cell, EnumGameMode.Survival);

    Assert.Equal(10, rig.Scene.Carried);
    Assert.Equal(1, rig.Pieces);
    Assert.Null(rig.Scene.RecordAt(North).FastenerAt(BlockFacing.SOUTH));
  }

  // Fails when a stack the full pack cannot hold is not spawned at the player's feet.
  [Fact]
  public void SurvivalFixThenTakeOff_FullPackDropsAtFeet() {
    Rig rig = Rig.Standing(2).Pair();
    Assert.Equal(0, rig.Scene.Carried);
    for (int slot = 1; slot < 10; slot++)
      rig.Scene.Player.Hotbar[slot].Itemstack = new ItemStack(rig.Scene.Hammer);

    rig.TakeOff(North, EnumGameMode.Survival);

    Assert.Equal(0, rig.Scene.Carried);
    Assert.Equal(2, rig.Spawned(rig.Scene.Bolt));
    Assert.Single(
      rig.Scene.World.Spawns,
      s => s.Stack.Block?.Code.Path.StartsWith("pipe-") == true
    );
  }

  // Fails when a creative take-off of the earlier piece clears the later piece's record, so ClearRecordsToward runs under it.
  [Fact]
  public void CreativeTakeOffOfEarlier_LaterKeepsItsRecord() {
    Rig rig = Rig.Standing(10).Pair();

    rig.TakeOff(Cell, EnumGameMode.Creative);

    Assert.Equal(8, rig.Scene.Carried);
    Assert.Equal(
      2,
      rig.Scene.RecordAt(North).FastenerAt(BlockFacing.SOUTH)!.StackSize
    );
    rig.TakeOff(North, EnumGameMode.Survival);
    Assert.Equal(10, rig.Scene.Carried);
    Assert.Equal(1, rig.Pieces);
  }

  // Fails when a creative take-off of the later piece does not move its own record onto the earlier piece.
  [Fact]
  public void CreativeTakeOffOfLater_EarlierGetsTheRecord() {
    Rig rig = Rig.Standing(10).Pair();

    rig.TakeOff(North, EnumGameMode.Creative);

    Assert.Equal(8, rig.Scene.Carried);
    Assert.Equal(
      2,
      rig.Scene.RecordAt(Cell).FastenerAt(BlockFacing.NORTH)!.StackSize
    );
    rig.TakeOff(Cell, EnumGameMode.Survival);
    Assert.Equal(10, rig.Scene.Carried);
    Assert.Equal(1, rig.Pieces);
  }

  // Fails when Place does not mark a creative fix's joint free, so the record is a plain absence the take-off cannot tell from a lost one.
  [Fact]
  public void CreativeFix_RecordsOwedNothing() {
    Rig rig = Rig.Standing(10).Pair(EnumGameMode.Creative);
    Assert.Equal(10, rig.Scene.Carried);
    Assert.True(rig.Scene.RecordAt(North).FreeAt(BlockFacing.SOUTH));
    Assert.True(rig.Scene.RecordAt(North).HasFree);
    rig.Scene.World.Reload(North);
    Assert.True(rig.Scene.RecordAt(North).FreeAt(BlockFacing.SOUTH));

    rig.TakeOff(North, EnumGameMode.Survival);

    Assert.Equal(10, rig.Scene.Carried);
    Assert.Equal(1, rig.Pieces);
  }

  // Fails when a survival joint is marked free too, or the free joint of a creative piece is counted as owed.
  [Fact]
  public void MixedJoints_ReturnOnlyTheSpent() {
    Rig rig = Rig.Standing(10).Pair();
    rig.Fix(North, BlockFacing.NORTH, EnumGameMode.Creative);
    Assert.Equal(8, rig.Scene.Carried);
    Assert.False(rig.Scene.RecordAt(North).FreeAt(BlockFacing.SOUTH));
    Assert.True(rig.Scene.RecordAt(FarNorth).FreeAt(BlockFacing.SOUTH));

    (var stacks, bool anyFree) = HandAssemblyServer.TakeOffReturns(
      rig.Scene.World.World.BlockAccessor,
      North
    );
    Assert.Equal(2, stacks.Sum(s => s.StackSize));
    Assert.True(anyFree);

    rig.TakeOff(North, EnumGameMode.Survival);

    Assert.Equal(10, rig.Scene.Carried);
    Assert.Equal(1, rig.Pieces);
  }

  // Fails when a fix against a face that holds a kept record overwrites that record, so a take-off returns fewer than were spent.
  [Fact]
  public void KeptRecordThenRefix_ReturnsBoth() {
    Rig rig = Rig.Standing(10).Pair();
    rig.TakeOff(Cell, EnumGameMode.Creative);

    rig.Fix(Ground, BlockFacing.UP, EnumGameMode.Survival);

    Assert.Equal(6, rig.Scene.Carried);
    Assert.Equal(
      2,
      rig.Scene.RecordAt(North).FastenerAt(BlockFacing.SOUTH)!.StackSize
    );
    Assert.Equal(
      2,
      rig.Scene.RecordAt(Cell).FastenerAt(BlockFacing.NORTH)!.StackSize
    );
    rig.TakeOff(Cell, EnumGameMode.Survival);
    Assert.Equal(10, rig.Scene.Carried);
    Assert.Equal(1, rig.Pieces);
  }
}
