using System.Threading;
using ExpandedLib.Catalogues;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using WasherPunch.BlockEntities;
using WasherPunch.Blocks;
using Xunit;

namespace WasherPunch.Tests;

/// <summary>The press on a loaded game: a fly click is one stroke against the plate in the input
/// slot, a body click is not.</summary>
[Collection(WorldCollection.Name)]
public class WasherPunchStrokeTests(LoadedWorld loaded) {
  private const int Washers = WasherPunchModSystem.WashersPerPlate;

  private static int _column = 64;

  private sealed record Rig(
    TestWorld World,
    TestPlayer Player,
    BlockWasherPunch Block,
    BlockEntityWasherPunch Press
  );

  private Rig NewRig() {
    TestWorld world = loaded.World;
    var pos = new BlockPos(Interlocked.Increment(ref _column), 110, 64);
    var block = TestBlocks.Configure(
      new BlockWasherPunch(),
      "washerpunch:press",
      140
    );
    var press = new BlockEntityWasherPunch { Pos = pos, Block = block };
    world.Place(pos, block, press);
    world.Initialize(press);
    press.DisablePickRangeCheck();
    return new Rig(world, world.Player(), block, press);
  }

  private static ItemStack Stack(Rig rig, string code, int size = 1) =>
    new(rig.World.World.GetItem(new AssetLocation(code)), size);

  private static void Click(Rig rig, int box) =>
    rig.Block.OnBlockInteractStart(
      rig.World.World,
      rig.Player.Player,
      new BlockSelection { Position = rig.Press.Pos, SelectionBoxIndex = box }
    );

  private static void Strike(Rig rig, int times) {
    for (int i = 0; i < times; i++)
      Click(rig, BlockWasherPunch.FlyBox);
  }

  private static ItemSlot Input(Rig rig) =>
    rig.Press.Inventory[BlockEntityWasherPunch.InputSlot];

  private static ItemSlot Output(Rig rig) =>
    rig.Press.Inventory[BlockEntityWasherPunch.OutputSlot];

  [Theory]
  [InlineData("iron")]
  [InlineData("copper")]
  public void A_plate_keeps_its_place_until_the_last_stroke(string metal) {
    Rig rig = NewRig();
    Input(rig).Itemstack = Stack(rig, $"game:metalplate-{metal}");
    ProcessJob job = rig.Press.CurrentJob!;

    Strike(rig, job.Strokes - 1);

    Assert.Equal(job.Strokes - 1, rig.Press.Strokes);
    Assert.False(Input(rig).Empty);
    Assert.True(Output(rig).Empty);
    Assert.Equal("washerpunch:press-strokes", rig.Press.InfoLine());
  }

  [Theory]
  [InlineData("iron")]
  [InlineData("copper")]
  public void The_last_stroke_takes_the_plate_and_puts_the_washers_out(
    string metal
  ) {
    Rig rig = NewRig();
    Input(rig).Itemstack = Stack(rig, $"game:metalplate-{metal}");
    ProcessJob job = rig.Press.CurrentJob!;

    Strike(rig, job.Strokes);

    Assert.True(Input(rig).Empty);
    Assert.Equal(0, rig.Press.Strokes);
    Assert.Equal(
      $"washerpunch:washer-{metal}",
      Output(rig).Itemstack.Collectible.Code.ToString()
    );
    Assert.Equal(job.Count, Output(rig).StackSize);
  }

  [Fact]
  public void An_item_with_no_job_takes_strokes_and_changes_nothing() {
    Rig rig = NewRig();
    Input(rig).Itemstack = Stack(rig, "game:ingot-iron", 2);

    Strike(rig, Washers);

    Assert.Equal(0, rig.Press.Strokes);
    Assert.Equal(2, Input(rig).StackSize);
    Assert.True(Output(rig).Empty);
    Assert.Equal("washerpunch:press-nojob", rig.Press.InfoLine());
  }

  [Fact]
  public void A_full_output_counts_no_stroke() {
    Rig rig = NewRig();
    Input(rig).Itemstack = Stack(rig, "game:metalplate-iron");
    ItemStack washers = Stack(rig, "washerpunch:washer-iron");
    int held = washers.Collectible.MaxStackSize - Washers + 1;
    washers.StackSize = held;
    Output(rig).Itemstack = washers;

    Strike(rig, Washers);

    Assert.Equal(0, rig.Press.Strokes);
    Assert.False(Input(rig).Empty);
    Assert.Equal(held, Output(rig).StackSize);
  }

  [Fact]
  public void A_second_plate_adds_to_the_washers_already_out() {
    Rig rig = NewRig();
    Input(rig).Itemstack = Stack(rig, "game:metalplate-iron", 2);

    Strike(rig, Washers * 2);

    Assert.True(Input(rig).Empty);
    Assert.Equal(Washers * 2, Output(rig).StackSize);
  }

  [Fact]
  public void A_click_on_the_body_is_not_a_stroke() {
    Rig rig = NewRig();
    Input(rig).Itemstack = Stack(rig, "game:metalplate-iron");

    Click(rig, BlockWasherPunch.BodyBox);

    Assert.Equal(0, rig.Press.Strokes);
  }

  [Fact]
  public void The_stroke_count_survives_a_save_and_load() {
    Rig rig = NewRig();
    Rig other = NewRig();
    Input(rig).Itemstack = Stack(rig, "game:metalplate-iron");
    Strike(rig, 7);

    var tree = new TreeAttribute();
    rig.Press.ToTreeAttributes(tree);
    other.Press.FromTreeAttributes(tree, other.World.World);

    Assert.Equal(7, other.Press.Strokes);
  }
}
