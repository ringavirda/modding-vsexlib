using System;
using System.Linq;
using ExpandedLib.Industry.Casting;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests.Casting;

/// <summary>The sand station block entity on a hand-built block: ram, impress, press, pour, set and shake out.
/// Shares the process-wide <see cref="CastingStationRegistry.Shared"/>.</summary>
[Collection("WorldState")]
public class SandStationTests : IDisposable {
  public void Dispose() => CastingStationRegistry.ResetForWorld();

  private sealed class RecordingStation : BlockEntitySandStation {
    public bool Intake = true;
    public bool Scrap;
    public int Moved;
    public int Pulled;
    public int Pulls;
    public int Shaken;
    public int Ticks;

    protected internal override bool IntakeAllowed() =>
      Intake && base.IntakeAllowed();

    protected internal override bool PourIsScrap => Scrap;

    protected internal override void OnMetalMoved(
      int units,
      string metal,
      float temperatureC
    ) => Moved += units;

    protected internal override void OnPulled(int accepted) {
      Pulled += accepted;
      Pulls++;
    }

    protected internal override void OnShakenOut() => Shaken++;

    protected internal override void OnStationTick(float dt) => Ticks++;
  }

  // Fails when the hotbar take is removed from ramming.
  [Fact]
  public void Ramming_takes_one_sand_and_fills_the_station() {
    var scene = SandStationScene.Build(sand: SandStationLevel.Empty);
    ItemStack sand = scene.SandStack();
    sand.StackSize = 3;

    Assert.True(scene.Interact(sand));

    Assert.Equal(SandStationLevel.Full, scene.Entity.Sand);
    Assert.Equal(2, scene.Held!.StackSize);
  }

  // Fails when holdingSand is true for any held item.
  [Fact]
  public void An_item_that_is_not_the_stations_sand_does_not_ram() {
    var scene = SandStationScene.Build(sand: SandStationLevel.Empty);

    Assert.False(scene.Interact(scene.SandStack("game:gravel-granite")));

    Assert.Equal(SandStationLevel.Empty, scene.Entity.Sand);
  }

  // Fails when the spec pick ignores the station code.
  [Fact]
  public void A_pattern_for_two_stations_imprints_this_stations_spec() {
    var scene = SandStationScene.Build();
    ItemStack pattern = scene.PatternStack(
      "twin",
      units: 25,
      share: 7,
      stations: [SandStationScene.OtherStation, SandStationScene.Station]
    );

    Assert.True(scene.Interact(pattern));

    Assert.Equal(SandStationScene.Station, scene.Entity.Spec!.Station);
    Assert.Equal(1 * 25 + 7, scene.Molten.MaxUnitCapacity);
  }

  // Fails when a pattern for another station is imprinted anyway.
  [Fact]
  public void A_pattern_for_another_station_is_refused_naming_this_station() {
    var scene = SandStationScene.Build();
    ItemStack pattern = scene.PatternStack(
      "elsewhere",
      stations: [SandStationScene.OtherStation]
    );

    Assert.True(scene.Interact(pattern));

    Assert.Equal("exlib-sandstation-wrongstation", scene.LastError);
    Assert.False(scene.Entity.HasImpression);
  }

  // Fails when Press admits one piece past the count.
  [Fact]
  public void Pressing_beds_pieces_up_to_the_count_then_refuses() {
    var scene = SandStationScene.Build();
    ItemStack pattern = scene.PatternStack("row", pieces: 3);

    scene.Interact(pattern);
    scene.Interact(pattern);
    scene.Interact(pattern);
    Assert.Equal(3, scene.Entity.Pressed);
    Assert.Null(scene.LastError);

    scene.Interact(pattern);
    Assert.Equal("exlib-sandstation-pressfull", scene.LastError);
    Assert.Equal(3, scene.Entity.Pressed);
  }

  // Fails when the cup is not turned to the block's facing: e, s and w keep the north cup.
  [Theory]
  [InlineData("n", 0.5, 0.9, 0.5, 0.1)]
  [InlineData("e", 0.1, 0.5, 0.9, 0.5)]
  [InlineData("s", 0.5, 0.1, 0.5, 0.9)]
  [InlineData("w", 0.9, 0.5, 0.1, 0.5)]
  public void A_pour_outside_the_cup_takes_nothing_and_inside_fills(
    string side,
    double inX,
    double inZ,
    double outX,
    double outZ
  ) {
    var scene = SandStationScene.Build(side, cup: "[4, 0, 10, 12, 16, 16]");
    scene.Interact(scene.PatternStack("row"));
    ItemStack metal = scene.SandStack(SandStationScene.Metal);
    int amount = 40;

    scene.Entity.BeginFill(new Vec3d(outX, 0.5, outZ));
    scene.Entity.ReceiveLiquidMetal(metal, ref amount, 1200f);
    Assert.Equal(40, amount);
    Assert.Equal(0, scene.Molten.CellAmount);

    scene.Entity.OnPourOver();
    scene.Entity.BeginFill(new Vec3d(inX, 0.5, inZ));
    scene.Entity.ReceiveLiquidMetal(metal, ref amount, 1200f);
    Assert.Equal(0, amount);
    Assert.Equal(40, scene.Molten.CellAmount);
  }

  // Fails when the launder face is not the facing turned around, or the pull is 50 a tick.
  [Fact]
  public void A_launder_neighbour_gives_one_hundred_units_a_tick() {
    var scene = SandStationScene.Build();
    scene.Interact(scene.PatternStack("row"));
    var launder = scene.Launder(220, 1300f);

    scene.Tick();

    Assert.Equal(100, scene.Molten.CellAmount);
    Assert.Equal(120, launder.CellAmount);
  }

  // Fails when the second face is never drained.
  [Fact]
  public void A_second_launder_face_is_drained_too() {
    var scene = SandStationScene.Build(launderFaces: """[{ "turns": 1 }]""");
    scene.Interact(scene.PatternStack("row"));
    var south = scene.Launder(30, 1300f);
    var west = scene.Launder(40, 1300f, BlockFacing.WEST);

    Assert.Equal(
      [BlockFacing.SOUTH, BlockFacing.WEST],
      scene.Entity.LaunderFaces.Select(f => f.Facing)
    );
    scene.Tick();

    Assert.Equal(70, scene.Molten.CellAmount);
    Assert.Equal(0, south.CellAmount);
    Assert.Equal(0, west.CellAmount);
  }

  // Fails when the face's offset is ignored and the principal's neighbour is read instead.
  [Fact]
  public void A_face_four_cells_along_the_body_is_drained_from_its_own_cell() {
    var scene = SandStationScene.Build(
      launderFaces: """[{ "z": -4, "turns": 2 }]"""
    );
    scene.Interact(scene.PatternStack("row"));
    var far = scene.Launder(60, 1300f, BlockFacing.NORTH, new Vec3i(0, 0, -4));
    var near = scene.Launder(50, 1300f, BlockFacing.NORTH);

    scene.Tick();

    Assert.Equal(60, scene.Molten.CellAmount);
    Assert.Equal(0, far.CellAmount);
    Assert.Equal(50, near.CellAmount);
  }

  // Fails when the offset is not rotated with the side.
  [Fact]
  public void The_same_block_turned_a_quarter_drains_the_turned_cell() {
    var scene = SandStationScene.Build(
      side: "e",
      launderFaces: """[{ "z": -4, "turns": 2 }]"""
    );
    scene.Interact(scene.PatternStack("row"));
    var far = scene.Launder(60, 1300f, BlockFacing.EAST, new Vec3i(4, 0, 0));

    Assert.Equal(
      [4],
      scene.Entity.LaunderFaces.Select(f => f.Offset.X).Skip(1).ToArray()
    );
    scene.Tick();

    Assert.Equal(60, scene.Molten.CellAmount);
    Assert.Equal(0, far.CellAmount);
  }

  // Fails when the facings are read for the principal's cell whatever cell is asked.
  [Fact]
  public void The_facings_a_cell_takes_are_the_faces_declared_on_it() {
    var scene = SandStationScene.Build(
      launderFaces: """[{ "z": -4, "turns": 2 }]"""
    );
    Block block = scene.Entity.Block;
    BlockPos principal = scene.Entity.Pos;

    Assert.Equal(
      [BlockFacing.NORTH],
      BlockEntitySandStation.LaunderFacingsAt(
        block,
        principal,
        principal.AddCopy(0, 0, -4)
      )
    );
    Assert.Equal(
      [BlockFacing.SOUTH],
      BlockEntitySandStation.LaunderFacingsAt(block, principal, principal)
    );
    Assert.Empty(
      BlockEntitySandStation.LaunderFacingsAt(
        block,
        principal,
        principal.AddCopy(0, 0, -2)
      )
    );
  }

  // Fails when both feeds are counted on one face instead of once per face.
  [Fact]
  public void Each_launder_face_is_a_feed_of_its_own() {
    var station = new RecordingStation();
    var scene = SandStationScene.Build(
      make: () => station,
      launderFaces: """[{ "turns": 1 }]"""
    );
    scene.Interact(scene.PatternStack("row"));
    scene.Launder(30, 1300f);
    scene.Launder(40, 1300f, BlockFacing.WEST);

    scene.Tick();

    Assert.Equal(2, station.Pulls);
    Assert.Equal(70, station.Pulled);
    Assert.Equal(70, station.Moved);
  }

  // Fails when a block without a side variant is given a default launder face.
  [Fact]
  public void A_block_with_no_side_variant_takes_no_launder_feed() {
    var scene = SandStationScene.Build(side: "");

    Assert.Empty(scene.Entity.LaunderFaces);
    scene.Interact(scene.PatternStack("row"));
    scene.Tick();

    Assert.Equal(0, scene.Molten.CellAmount);
  }

  // Fails when the TooHot branch of the interaction switch is removed.
  [Fact]
  public void Harvesting_while_liquid_is_refused_as_too_hot() {
    var scene = SandStationScene.Build();
    scene.Interact(scene.PatternStack("row"));
    scene.Pour(40, 1300f);

    Assert.True(scene.Interact(null));

    Assert.Equal("exlib-sandstation-toohot", scene.LastError);
    Assert.Empty(scene.Harvested);
  }

  // Fails when AfterShakeOut returns Empty, or the pieces yield is not the batch count.
  [Fact]
  public void After_cooling_an_empty_hand_gives_the_parts_and_the_station_stays_rammed() {
    var scene = SandStationScene.Build();
    ItemStack pattern = scene.PatternStack("row", units: 25, pieces: 4);
    for (int i = 0; i < 4; i++)
      scene.Interact(pattern);
    scene.Pour(100, 1300f).CoolToHardened();

    Assert.True(scene.Interact(null));

    ItemStack part = Assert.Single(
      scene.Harvested,
      s => s.Collectible.Code.ToString() == SandStationScene.PartCode
    );
    Assert.Equal(4, part.StackSize);
    Assert.Equal(SandStationLevel.Full, scene.Entity.Sand);
    Assert.False(scene.Entity.HasImpression);
  }

  // Fails when the misrun check is ignored.
  [Fact]
  public void A_fill_below_the_minimum_pour_temperature_gives_scrap_only() {
    var scene = SandStationScene.Build();
    ItemStack pattern = scene.PatternStack(
      "row",
      units: 25,
      pieces: 4,
      minPourTemp: 1300f
    );
    for (int i = 0; i < 4; i++)
      scene.Interact(pattern);
    scene.Launder(100, 1000f);
    scene.Tick().Tick();
    scene.CoolToHardened();

    scene.Interact(null);

    Assert.DoesNotContain(
      scene.Harvested,
      s => s.Collectible.Code.ToString() == SandStationScene.PartCode
    );
    Assert.Equal(
      100 / 5,
      scene
        .Harvested.Where(s => s.Collectible.Code.ToString() == scene.ScrapCode)
        .Sum(s => s.StackSize)
    );
  }

  // Fails when cc_pattern is not persisted.
  [Fact]
  public void The_tree_round_trip_keeps_sand_pattern_and_pressed() {
    var scene = SandStationScene.Build();
    ItemStack pattern = scene.PatternStack("row", pieces: 4);
    scene.Interact(pattern);
    scene.Interact(pattern);

    var fresh = (BlockEntitySandStation)scene.World.Reload(scene.Entity.Pos)!;

    Assert.Equal(SandStationLevel.Full, fresh.Sand);
    Assert.True(fresh.HasImpression);
    Assert.Equal(2, fresh.Pressed);
  }

  // Fails when the break spawns nothing.
  [Fact]
  public void Breaking_a_set_station_spawns_the_same_stacks_as_a_shake_out() {
    var scene = SandStationScene.Build();
    ItemStack pattern = scene.PatternStack("row", units: 25, pieces: 4);
    for (int i = 0; i < 4; i++)
      scene.Interact(pattern);
    scene.Pour(100, 1300f).CoolToHardened();

    scene.Entity.ShakeOutOnBreak();

    ItemStack part = Assert.Single(
      scene.Harvested,
      s => s.Collectible.Code.ToString() == SandStationScene.PartCode
    );
    Assert.Equal(4, part.StackSize);
    Assert.Equal(0, scene.Molten.CellAmount);
  }

  // Fails when BlockSandStation.OnBlockBroken does not shake the casting out.
  [Fact]
  public void Breaking_the_block_spawns_the_set_casting() {
    var scene = SandStationScene.Build();
    ItemStack pattern = scene.PatternStack("row", units: 25, pieces: 4);
    for (int i = 0; i < 4; i++)
      scene.Interact(pattern);
    scene.Pour(100, 1300f).CoolToHardened();

    scene.Entity.Block.OnBlockBroken(
      scene.World.World,
      scene.Entity.Pos,
      scene.Player
    );

    ItemStack part = Assert.Single(
      scene.Harvested,
      s => s.Collectible.Code.ToString() == SandStationScene.PartCode
    );
    Assert.Equal(4, part.StackSize);
  }

  // Fails when IntakeAllowed is not consulted before the launder pull.
  [Fact]
  public void A_station_that_narrows_intake_pulls_nothing() {
    var station = new RecordingStation { Intake = false };
    var scene = SandStationScene.Build(make: () => station);
    scene.Interact(scene.PatternStack("row"));
    scene.Launder(100, 1300f);

    scene.Tick();

    Assert.Equal(0, scene.Molten.CellAmount);
    Assert.Equal(1, station.Ticks);
  }

  // Fails when OnMetalMoved or OnPulled is not called after a pull or a carried pour.
  [Fact]
  public void Pulls_and_carried_pours_report_the_moved_units() {
    var station = new RecordingStation();
    var scene = SandStationScene.Build(make: () => station);
    scene.Interact(scene.PatternStack("row"));
    scene.Launder(30, 1300f);
    scene.Tick();
    Assert.Equal(30, station.Moved);
    Assert.Equal(30, station.Pulled);

    int amount = 20;
    station.BeginFill(new Vec3d(0.5, 0.5, 0.5));
    station.ReceiveLiquidMetal(
      scene.SandStack(SandStationScene.Metal),
      ref amount,
      1300f
    );
    Assert.Equal(50, station.Moved);
    Assert.Equal(30, station.Pulled);
  }

  // Fails when PourIsScrap is not folded into the yield, or OnShakenOut is not called.
  [Fact]
  public void A_scrap_pour_gives_no_parts_and_a_shake_out_is_reported() {
    var station = new RecordingStation { Scrap = true };
    var scene = SandStationScene.Build(make: () => station);
    ItemStack pattern = scene.PatternStack("row", units: 25, pieces: 4);
    for (int i = 0; i < 4; i++)
      scene.Interact(pattern);
    scene.Pour(100, 1300f).CoolToHardened();

    scene.Interact(null);

    Assert.DoesNotContain(
      scene.Harvested,
      s => s.Collectible.Code.ToString() == SandStationScene.PartCode
    );
    Assert.Equal(1, station.Shaken);
  }
}
