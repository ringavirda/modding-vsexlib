using System.Linq;
using System.Text.RegularExpressions;
using ExpandedLib.Testing.Live;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests.Scenarios.Live;

/// <summary>Plot allocation, the climate a plot's region keeps and the log watch (scenarios.md,
/// Plots and coordinates, Climate, Errors and exceptions).</summary>
public class PlotTests {
  /// <summary>Fails when the 8-block margin is dropped (V7).</summary>
  [Fact]
  public void A_plot_takes_the_columns_that_hold_its_size_and_a_margin_either_side() {
    Assert.Equal(2, PlotAllocator.Columns(16));
    Assert.Equal(3, PlotAllocator.Columns(32));
  }

  /// <summary>Fails when the first plot is not 32 blocks east of the spawn (V8).</summary>
  [Fact]
  public void The_first_plot_starts_in_the_column_east_of_the_spawn() {
    PlotBox box = new PlotAllocator(512_000, 512_000).Next(16, 16);
    Assert.Equal(16_001, box.MinColumnX);
    Assert.Equal(16_001 + 1, box.MaxColumnX);
  }

  /// <summary>Fails when the next plot starts less than a whole plot east of the last (V9).</summary>
  [Fact]
  public void Plots_run_in_a_row_without_sharing_a_column() {
    var plots = new PlotAllocator(0, 0);
    PlotBox first = plots.Next(16, 16);
    PlotBox second = plots.Next(40, 16);
    Assert.Equal(first.MaxColumnX + 1, second.MinColumnX);
    Assert.Equal(
      PlotAllocator.Columns(40),
      second.MaxColumnX - second.MinColumnX + 1
    );
  }

  /// <summary>Fails when the origin column is taken at the plot's west edge (V10).</summary>
  [Fact]
  public void The_centre_is_the_middle_of_the_plots_columns() {
    PlotBox box = new PlotAllocator(0, 0).Next(16, 16);
    Assert.Equal(box.MinColumnX * 32 + 32, box.CenterX);
    Assert.Equal(box.MinColumnZ * 32 + 32, box.CenterZ);
  }

  /// <summary>Fails when a negative block coordinate rounds toward zero (V11).</summary>
  [Fact]
  public void A_region_index_rounds_down() {
    Assert.Equal(-1, ClimateHold.Region(-1, 512));
    Assert.Equal(1, ClimateHold.Region(512, 512));
    Assert.Equal(0, ClimateHold.Region(511, 512));
  }

  private static ClimateCondition Cold() =>
    new() {
      Temperature = -10,
      WorldGenTemperature = -10,
      Rainfall = 1,
      WorldgenRainfall = 1,
    };

  /// <summary>Fails when any of the four climate figures is left as the game computed it
  /// (V12).</summary>
  [Fact]
  public void A_held_region_reads_twenty_degrees_and_no_rain_in_all_four_figures() {
    var hold = new ClimateHold(512);
    hold.Hold(new PlotBox(31, 31, 31, 31, 1008, 1008));
    ClimateCondition climate = Cold();
    hold.Apply(
      ref climate,
      new BlockPos(1010, 70, 1010),
      EnumGetClimateMode.NowValues,
      0
    );
    Assert.Equal(20f, climate.Temperature);
    Assert.Equal(20f, climate.WorldGenTemperature);
    Assert.Equal(0f, climate.Rainfall);
    Assert.Equal(0f, climate.WorldgenRainfall);
  }

  /// <summary>Fails when a position outside the held region, in x or in z, is overwritten
  /// (V13, V14).</summary>
  [Theory]
  [InlineData(1000 + 512, 1000)]
  [InlineData(1000, 1000 + 512)]
  public void A_region_that_holds_no_plot_keeps_the_games_climate(int x, int z) {
    var hold = new ClimateHold(512);
    hold.Hold(new PlotBox(31, 31, 31, 31, 1008, 1008));
    ClimateCondition climate = Cold();
    hold.Apply(
      ref climate,
      new BlockPos(x, 70, z),
      EnumGetClimateMode.NowValues,
      0
    );
    Assert.Equal(-10f, climate.Temperature);
  }

  private static ScenarioErrors Watch(params string[] allowed) =>
    new([.. allowed.Select(p => new Regex(p))]);

  /// <summary>Fails when a fatal entry does not fail the case (V53), or a warning is not listed
  /// (V54).</summary>
  [Fact]
  public void An_error_or_fatal_entry_fails_and_a_warning_is_only_listed() {
    ScenarioErrors errors = Watch();
    errors.Add(EnumLogType.Error, "boom at 1 2 3");
    errors.Add(EnumLogType.Fatal, "dead");
    errors.Add(EnumLogType.Warning, "careful");
    errors.Add(EnumLogType.Notification, "fine");
    Assert.Equal(["boom at 1 2 3", "dead"], errors.Errors);
    Assert.Equal(["careful"], errors.Warnings);
  }

  /// <summary>Fails when an entry the case allows still fails it (V15).</summary>
  [Fact]
  public void An_error_matching_allow_errors_does_not_fail_the_case() {
    ScenarioErrors errors = Watch("expected .* thing");
    errors.Add(EnumLogType.Error, "expected loud thing");
    errors.Add(EnumLogType.Error, "other");
    Assert.Equal(["other"], errors.Errors);
  }

  /// <summary>Fails when the runner's own lines are read back as errors (V16).</summary>
  [Fact]
  public void The_runners_own_lines_are_not_errors() {
    ScenarioErrors errors = Watch();
    errors.Add(EnumLogType.Error, "[scenario] error x: y");
    Assert.Empty(errors.Errors);
  }

  /// <summary>Fails when the recreated-block-entity notice no longer fails a case (V17).</summary>
  [Fact]
  public void A_recreated_block_entity_notice_fails_and_an_unrelated_notice_does_not() {
    ScenarioErrors errors = Watch();
    errors.Add(EnumLogType.Notification, "Recreated 2 orphaned block entities");
    errors.Add(EnumLogType.Notification, "orphaned block entities seen");
    Assert.Single(errors.Errors);
  }

  /// <summary>Fails when the same entry is handed out twice (V18).</summary>
  [Fact]
  public void Each_failing_entry_is_taken_once() {
    ScenarioErrors errors = Watch();
    errors.Add(EnumLogType.Error, "one");
    Assert.Equal("one", errors.TakeNew());
    Assert.Null(errors.TakeNew());
  }

  /// <summary>Fails when a plot spanning two regions holds only the first one along X (S53) or along Z
  /// (S67).</summary>
  [Fact]
  public void A_plot_holds_every_region_its_columns_span() {
    var climate = new ClimateHold(512);
    climate.Hold(new PlotBox(15, 0, 16, 0, 512, 16));
    Assert.Equal(ClimateHold.Temperature, TemperatureAt(climate, 100, 5));
    Assert.Equal(ClimateHold.Temperature, TemperatureAt(climate, 520, 5));
    Assert.Equal(5f, TemperatureAt(climate, 1100, 5));
    var south = new ClimateHold(512);
    south.Hold(new PlotBox(0, 15, 0, 16, 16, 512));
    Assert.Equal(ClimateHold.Temperature, TemperatureAt(south, 5, 100));
    Assert.Equal(ClimateHold.Temperature, TemperatureAt(south, 5, 520));
    Assert.Equal(5f, TemperatureAt(south, 5, 1100));
  }

  private static float TemperatureAt(ClimateHold climate, int x, int z) {
    var condition = new ClimateCondition { Temperature = 5f };
    climate.Apply(
      ref condition,
      new BlockPos(x, 5, z),
      EnumGetClimateMode.NowValues,
      0
    );
    return condition.Temperature;
  }

  private const string DeepFill =
    """[{ "fill": { "code": "game:water-still-7", "from": [6, -3, 8], "to": [8, -5, 10] } }]""";

  /// <summary>Fails when the lowest cell scan skips a fill's <c>from</c> or <c>to</c>.</summary>
  [Fact]
  public void The_lowest_y_of_a_file_is_its_deepest_fill_cell() {
    Assert.Equal(-5, LiveFixture.Case(DeepFill).LowestY);
    Assert.Equal(0, LiveFixture.Case("[]").LowestY);
  }

  /// <summary>Fails when the origin is not raised for the deepest cell, or the ground is not
  /// laid under the raised plot.</summary>
  [Fact]
  public void A_plot_with_a_cell_five_below_zero_gets_an_origin_that_holds_it() {
    var blocks = Substitute.For<IBlockAccessor>();
    var world = Substitute.For<IWorldAccessor>();
    world.BlockAccessor.Returns(blocks);
    blocks.GetTerrainMapheightAt(Arg.Any<BlockPos>()).Returns(2);
    blocks.GetBlock(Arg.Any<BlockPos>()).Returns(new Block { BlockId = 7 });
    var set = new System.Collections.Generic.List<(int X, int Y, int Z)>();
    blocks
      .When(b => b.SetBlock(7, Arg.Any<BlockPos>()))
      .Do(c => {
        var p = c.ArgAt<BlockPos>(1);
        set.Add((p.X, p.Y, p.Z));
      });
    var plot = new ScenarioPlot(new PlotBox(0, 0, 1, 1, 32, 32), [2, 2, 2]);
    int lowest = LiveFixture.Case(DeepFill).LowestY;

    plot.Locate(world, lowest);

    Assert.True(plot.Origin.Y + lowest >= 1);
    Assert.Equal(6, plot.Origin.Y);
    Assert.Equal(5 * 5 * 3, set.Count);
    Assert.Contains((30, 3, 34), set);
    Assert.Contains((34, 5, 30), set);
    Assert.All(set, p => Assert.InRange(p.Y, 3, 5));
  }

  /// <summary>Fails when a plot whose cells all sit at or above 0 is lifted or has ground laid
  /// under it.</summary>
  [Fact]
  public void A_plot_with_no_cell_below_zero_sits_one_above_the_terrain() {
    var blocks = Substitute.For<IBlockAccessor>();
    var world = Substitute.For<IWorldAccessor>();
    world.BlockAccessor.Returns(blocks);
    blocks.GetTerrainMapheightAt(Arg.Any<BlockPos>()).Returns(2);
    var plot = new ScenarioPlot(new PlotBox(0, 0, 1, 1, 32, 32), [2, 2, 2]);

    plot.Locate(world, 0);

    Assert.Equal(3, plot.Origin.Y);
    blocks.DidNotReceiveWithAnyArgs().SetBlock(default, default!);
  }

  /// <summary>Fails when the world bound check on one side is dropped: below the floor, at the
  /// height, before x 0 or past z.</summary>
  [Theory]
  [InlineData(0, -51, 0)]
  [InlineData(0, 206, 0)]
  [InlineData(-101, 0, 0)]
  [InlineData(0, 0, 924)]
  public void A_cell_outside_the_world_fails_its_step_naming_the_cell(
    int x,
    int y,
    int z
  ) {
    var fixture = new LiveFixture();
    StepFailure failure = Assert.Throws<StepFailure>(() =>
      fixture.Run(
        LiveFixture
          .Case(
            $$"""[{ "fill": { "code": "game:stone", "from": [{{x}}, {{y}}, {{z}}], "to": [{{x}}, {{y}}, {{z}}] } }]"""
          )
          .Model.Steps
      )
    );
    Assert.Equal(Suspect.Scenario, failure.Suspect);
    Assert.Contains($"[{x}, {y}, {z}]", failure.Message);
    Assert.Contains("outside the world", failure.Message);
  }
}
