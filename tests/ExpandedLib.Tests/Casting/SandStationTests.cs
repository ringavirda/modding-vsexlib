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

    protected internal override void OnPulled(int accepted) =>
      Pulled += accepted;

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

  // Fails when the cup test always passes.
  [Fact]
  public void A_pour_outside_the_cup_takes_nothing_and_inside_fills() {
    var scene = SandStationScene.Build(cup: "[4, 0, 10, 12, 16, 16]");
    scene.Interact(scene.PatternStack("row"));
    ItemStack metal = scene.SandStack(SandStationScene.Metal);
    int amount = 40;

    scene.Entity.BeginFill(new Vec3d(0.9, 0.5, 0.1));
    scene.Entity.ReceiveLiquidMetal(metal, ref amount, 1200f);
    Assert.Equal(40, amount);
    Assert.Equal(0, scene.Molten.CellAmount);

    scene.Entity.OnPourOver();
    scene.Entity.BeginFill(new Vec3d(0.5, 0.5, 0.9));
    scene.Entity.ReceiveLiquidMetal(metal, ref amount, 1200f);
    Assert.Equal(0, amount);
    Assert.Equal(40, scene.Molten.CellAmount);
  }

  // Fails when the launder face is not the facing turned around.
  [Fact]
  public void A_launder_neighbour_gives_fifty_units_a_tick() {
    var scene = SandStationScene.Build();
    scene.Interact(scene.PatternStack("row"));
    var launder = scene.Launder(120, 1300f);

    scene.Tick();

    Assert.Equal(50, scene.Molten.CellAmount);
    Assert.Equal(70, launder.CellAmount);
  }

  // Fails when a block without a side variant is given a default launder face.
  [Fact]
  public void A_block_with_no_side_variant_takes_no_launder_feed() {
    var scene = SandStationScene.Build(side: "");

    Assert.Null(scene.Entity.LaunderFace);
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
