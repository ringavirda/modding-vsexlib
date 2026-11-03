using System.Threading;
using ExpandedLib.Catalogues;
using ExpandedLib.Industry.Machining;
using ExpandedLib.Industry.MechanicalPower;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using WasherPunch.BlockEntities;
using WasherPunch.Blocks;
using Xunit;

namespace WasherPunch.Tests;

/// <summary>The drilling machine on a loaded game, driven through its drive reading: what it cuts, what
/// the bit and the plate decide, and what the lever and the block's node do.</summary>
[Collection(WorldCollection.Name)]
public class WasherDrillTests(LoadedWorld loaded) {
  // 360 W at the kind's 10.7 rad/s; 1.5 kg of wrought iron at 193,500 J/kg is 290,250 J, 806.25 s.
  private const double CutSeconds = 806.25;

  private static int _column = 200;

  private sealed class Driven : BlockEntityWasherDrill {
    public MpRunReading Drive = new(10.7f, 1000f);

    protected override MpRunReading ReadDrive() => Drive;
  }

  private sealed record Rig(
    TestWorld World,
    TestPlayer Player,
    BlockWasherDrill Block,
    Driven Drill
  );

  private Rig NewRig() {
    TestWorld world = loaded.World;
    var pos = new BlockPos(Interlocked.Increment(ref _column), 110, 64);
    var block = (BlockWasherDrill)
      world.World.GetBlock(new AssetLocation("washerpunch:machine-drill-ns"))!;
    var drill = new Driven { Pos = pos, Block = block };
    world.Place(pos, block, drill);
    world.Initialize(drill);
    drill.DisablePickRangeCheck();
    return new Rig(world, world.Player(), block, drill);
  }

  private static ItemStack Stack(Rig rig, string code) {
    var location = new AssetLocation(code);
    return new ItemStack(
      (CollectibleObject?)rig.World.World.GetItem(location)
        ?? rig.World.World.GetBlock(location)
    );
  }

  private static void Load(Rig rig, string plate, string? bit) {
    rig.Drill.Inventory[BlockEntityMachineTool.WorkSlot].Itemstack = Stack(
      rig,
      plate
    );
    if (bit != null)
      rig.Drill.Inventory[BlockEntityMachineTool.ToolSlot].Itemstack = Stack(
        rig,
        bit
      );
  }

  private static string? Piece(Rig rig) =>
    rig
      .Drill.Inventory[BlockEntityMachineTool.WorkSlot]
      .Itemstack?.Collectible.Code.ToString();

  private static void Seconds(Rig rig, double seconds) {
    rig.World.AdvanceHours(seconds / 3600d);
    rig.World.FireBlockEntityTicks(1f);
  }

  private static void Click(Rig rig, int box) =>
    rig.Block.OnBlockInteractStart(
      rig.World.World,
      rig.Player.Player,
      new BlockSelection { Position = rig.Drill.Pos, SelectionBoxIndex = box }
    );

  [Fact]
  public void The_block_names_the_drill_kind() {
    Rig rig = NewRig();

    MachineKind? kind = MachineKind.Of(rig.Block);

    Assert.Equal("washerpunch:drill", kind?.Code);
  }

  [Fact]
  public void An_iron_plate_with_an_iron_bit_is_drilled_in_the_energy_over_power_time_and_wears_the_bit() {
    Rig rig = NewRig();
    Load(rig, "game:metalplate-iron", "washerpunch:drillbit-iron");
    ItemSlot tool = rig.Drill.Inventory[BlockEntityMachineTool.ToolSlot];
    int fresh = tool.Itemstack.Collectible.GetRemainingDurability(
      tool.Itemstack
    );
    Assert.True(rig.Drill.TryStart());

    Seconds(rig, CutSeconds - 1.25);
    Assert.Equal("game:metalplate-iron", Piece(rig));

    Seconds(rig, 1.5);
    Assert.Equal("washerpunch:drilledplate-iron", Piece(rig));
    Assert.Equal(
      fresh - 1,
      tool.Itemstack.Collectible.GetRemainingDurability(tool.Itemstack)
    );
  }

  [Fact]
  public void A_meteoric_plate_is_short_of_the_iron_bit_and_drilled_by_the_steel_one() {
    Rig rig = NewRig();
    Load(rig, "game:metalplate-meteoriciron", "washerpunch:drillbit-iron");
    Assert.True(rig.Drill.TryStart());

    Seconds(rig, CutSeconds + 1);
    Assert.Equal("game:metalplate-meteoriciron", Piece(rig));
    Assert.Equal(MachineCutVerdict.TierShort, rig.Drill.Verdict);

    rig.Drill.Inventory[BlockEntityMachineTool.ToolSlot].Itemstack = Stack(
      rig,
      "washerpunch:drillbit-steel"
    );
    Seconds(rig, 1);
    Seconds(rig, CutSeconds + 1);
    Assert.Equal("washerpunch:drilledplate-meteoriciron", Piece(rig));
  }

  [Fact]
  public void A_plain_ingot_runs_idle_and_names_that_no_machine_works_it() {
    Rig rig = NewRig();
    Load(rig, "game:ingot-iron", "washerpunch:drillbit-iron");
    Assert.True(rig.Drill.TryStart());

    Seconds(rig, CutSeconds + 1);

    Assert.Equal("game:ingot-iron", Piece(rig));
    Assert.Equal(MachineCutVerdict.NoJob, rig.Drill.Verdict);
    Assert.Equal(0f, rig.Drill.LoadTorque(10.7f));
    Assert.Equal("exlib:machine-tool-info-nojob", rig.Drill.InfoLine());
  }

  [Fact]
  public void A_shaft_at_rest_cuts_nothing_and_the_info_says_no_drive() {
    Rig rig = NewRig();
    Load(rig, "game:metalplate-iron", "washerpunch:drillbit-iron");
    rig.Drill.Drive = new MpRunReading(0f, 1000f);
    Assert.True(rig.Drill.TryStart());

    Seconds(rig, CutSeconds + 1);

    Assert.Equal("game:metalplate-iron", Piece(rig));
    Assert.Equal(MachineCutVerdict.NoDrive, rig.Drill.Verdict);
    Assert.Equal("exlib:machine-tool-info-nodrive", rig.Drill.InfoLine());
  }

  [Fact]
  public void A_node_on_no_run_credits_nothing_until_the_run_turns() {
    Rig rig = NewRig();
    Load(rig, "game:metalplate-iron", "washerpunch:drillbit-iron");
    rig.Drill.Drive = MpRunReading.None;
    Assert.True(rig.Drill.TryStart());

    Seconds(rig, CutSeconds + 1);
    Assert.Equal("game:metalplate-iron", Piece(rig));

    rig.Drill.Drive = new MpRunReading(10.7f, 1000f);
    Seconds(rig, CutSeconds + 1);
    Assert.Equal("washerpunch:drilledplate-iron", Piece(rig));
  }

  [Fact]
  public void The_lever_engages_and_disengages_and_the_body_does_not() {
    Rig rig = NewRig();

    Click(rig, BlockWasherDrill.BodyBox);
    Assert.False(rig.Drill.IsRunning);

    Click(rig, BlockWasherDrill.LeverBox);
    Assert.True(rig.Drill.IsRunning);

    Click(rig, BlockWasherDrill.LeverBox);
    Assert.False(rig.Drill.IsRunning);
  }

  [Fact]
  public void The_block_declares_the_mpshaft_network_type() {
    Rig rig = NewRig();

    Assert.Equal("mpshaft", rig.Block.NetworkType);
  }
}
