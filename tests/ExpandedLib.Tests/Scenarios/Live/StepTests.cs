using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Scenarios;
using ExpandedLib.Testing;
using ExpandedLib.Testing.Live;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Xunit;

namespace ExpandedLib.Tests.Scenarios.Live;

/// <summary>The sequence runner and the steps that need no block entity, over a
/// <see cref="ExpandedLib.Testing.TestWorld"/> with the plot at (100, 50, 100).</summary>
public class StepTests {
  private const string Place =
    """{ "place": { "id": "a", "code": "game:stone", "at": [1, 2, 3] } }""";

  private static IReadOnlyList<SetupStep> Steps(string steps) =>
    LiveFixture.Case(steps).Model.Steps;

  /// <summary>Fails when a plot-local cell is not moved by the plot's origin (V35).</summary>
  [Fact]
  public void A_placed_block_lands_at_the_plot_origin_plus_its_cell() {
    var fixture = new LiveFixture();
    fixture.Run(Steps($"[{Place}]"));
    Assert.Equal(
      fixture.Stone.Id,
      fixture.Test.GetBlock(new BlockPos(101, 52, 103)).Id
    );
    Assert.Contains(
      "[scenario]   1 place game:stone at [1,2,3] ok: game:stone at plot [1, 2, 3]",
      fixture.Lines
    );
  }

  /// <summary>Fails when a place onto an occupied cell goes ahead (V36).</summary>
  [Fact]
  public void A_place_onto_a_block_fails_the_step_and_blames_the_scenario() {
    var fixture = new LiveFixture();
    fixture.Run(Steps($"[{Place}]"));
    StepFailure failure = Assert.Throws<StepFailure>(() =>
      fixture.Run(Steps($"[{Place}]"))
    );
    Assert.Equal(Suspect.Scenario, failure.Suspect);
    Assert.Contains("not air", failure.Message);
  }

  /// <summary>Fails when an expect that holds is reported as failed (V37).</summary>
  [Fact]
  public void An_expect_that_holds_prints_the_value_it_read() {
    var fixture = new LiveFixture();
    fixture.Run(
      Steps(
        $$"""[{{Place}}, { "expect": { "at": { "of": "a" }, "block": "game:stone" } }]"""
      )
    );
    Assert.Contains(
      fixture.Lines,
      l => l.StartsWith("[scenario]   2 expect") && l.Contains(" ok: ")
    );
  }

  /// <summary>Fails when an expect that does not hold passes (V37).</summary>
  [Fact]
  public void An_expect_that_does_not_hold_prints_its_fail_line_and_detail_once() {
    var fixture = new LiveFixture();
    StepFailure failure = Assert.Throws<StepFailure>(() =>
      fixture.Run(
        Steps(
          $$"""[{{Place}}, { "expect": { "at": { "of": "a" }, "block": "game:air" } }]"""
        )
      )
    );
    Assert.True(failure.Reported);
    Assert.Single(
      fixture.Lines,
      l => l.StartsWith("[scenario]   2 expect") && l.Contains("FAIL: ")
    );
    Assert.Equal("2", fixture.Context.Current);
    Assert.Contains(
      fixture.Lines,
      l => l.StartsWith("[scenario]   | step {\"expect\"")
    );
  }

  /// <summary>Fails when a fill leaves off its last cell or lays a different block (V38).</summary>
  [Fact]
  public void A_fill_lays_every_cell_of_the_inclusive_box() {
    var fixture = new LiveFixture();
    fixture.Run(
      Steps(
        """[{ "fill": { "code": "game:stone", "from": [0, 0, 0], "to": [2, 0, 1] } }]"""
      )
    );
    for (int x = 0; x <= 2; x++)
      for (int z = 0; z <= 1; z++)
        Assert.Equal(
          fixture.Stone.Id,
          fixture.Test.GetBlock(new BlockPos(100 + x, 50, 100 + z)).Id
        );
    Assert.Equal(0, fixture.Test.GetBlock(new BlockPos(103, 50, 100)).Id);
    Assert.Contains(
      fixture.Lines,
      l => l.EndsWith("ok: 6 cell(s) of game:stone")
    );
  }

  /// <summary>Fails when a wait ends a tick late (V39).</summary>
  [Fact]
  public void A_wait_of_seconds_yields_until_the_clock_has_run_that_long() {
    var fixture = new LiveFixture();
    int ticks = fixture.Run(Steps("""[{ "wait": 1 }]"""));
    Assert.Equal(2, ticks);
    Assert.Contains(fixture.Lines, l => l.EndsWith("ok: 1.0 s"));
  }

  /// <summary>Fails when a wait for a condition that never holds passes (V40).</summary>
  [Fact]
  public void A_wait_until_that_never_holds_fails_after_max() {
    var fixture = new LiveFixture();
    StepFailure failure = Assert.Throws<StepFailure>(() =>
      fixture.Run(
        Steps(
          $$"""[{{Place}}, { "wait": { "max": 2, "until": { "at": { "of": "a" }, "block": "game:air" } } }]"""
        )
      )
    );
    Assert.Contains("not met in 2 s", failure.Message);
  }

  /// <summary>Fails when a wait for a condition that holds keeps waiting (V41).</summary>
  [Fact]
  public void A_wait_until_that_holds_ends_at_once() {
    var fixture = new LiveFixture();
    int ticks = fixture.Run(
      Steps(
        $$"""[{{Place}}, { "wait": { "max": 5, "until": { "at": { "of": "a" }, "block": "game:stone" } } }]"""
      )
    );
    Assert.Equal(0, ticks);
    Assert.Contains(fixture.Lines, l => l.Contains("ok: held after 0.0 s"));
  }

  /// <summary>Fails when a repeat runs one round too many or too few (V42).</summary>
  [Fact]
  public void A_repeat_runs_its_steps_times_times_under_nested_labels() {
    var fixture = new LiveFixture();
    fixture.Run(
      Steps(
        $$"""[{{Place}}, { "repeat": { "times": 3, "steps": [{ "expect": { "at": { "of": "a" }, "block": "game:stone" } }] } }]"""
      )
    );
    string[] nested =
    [
      .. fixture
        .Lines.Where(l => l.Contains(" expect "))
        .Select(l =>
          l.Split(' ', System.StringSplitOptions.RemoveEmptyEntries)[1]
        ),
    ];
    Assert.Equal(["2.1.1", "2.2.1", "2.3.1"], nested);
    Assert.Contains(fixture.Lines, l => l.EndsWith("ok: 3 round(s)"));
  }

  /// <summary>Fails when an error logged during a step does not fail it (V43).</summary>
  [Fact]
  public void An_error_in_the_server_log_fails_the_step_running() {
    var fixture = new LiveFixture();
    fixture.Errors.Add(EnumLogType.Error, "tick threw at 1 2 3");
    StepFailure failure = Assert.Throws<StepFailure>(() =>
      fixture.Run(Steps($"[{Place}]"))
    );
    Assert.Contains("server log error: tick threw", failure.Message);
  }

  /// <summary>Fails when the failure detail leaves the position unmoved from plot-local to
  /// absolute (V44), or drops the case's logged errors (V45).</summary>
  [Fact]
  public void The_detail_lists_each_position_plot_local_and_absolute_and_the_logged_errors() {
    var fixture = new LiveFixture();
    fixture.Errors.Add(EnumLogType.Error, "tick threw");
    fixture.Errors.Add(EnumLogType.Warning, "worn");
    fixture.Run(Steps($"[{Place}]").Take(0).ToList());
    SetupStep step = Steps($"[{Place}]")[0];
    fixture.Context.Position(step, step.Body["at"], "at");
    IReadOnlyList<string> lines = FailureDetail.Lines(
      fixture.Context,
      step,
      new StepFailure("why", Suspect.Mod)
    );
    Assert.Contains(
      "position at: plot [1, 2, 3] absolute [101, 52, 103]",
      lines
    );
    Assert.Contains("error tick threw", lines);
    Assert.Contains("warning worn", lines);
    Assert.Contains("suspect mod", lines);
    Assert.StartsWith("step {\"place\"", lines[0]);
  }

  /// <summary>Fails when a stack frame loses an "at " after its leading one (S54), or when a
  /// frame without the prefix loses its first characters (S55).</summary>
  [Fact]
  public void A_stack_frame_keeps_every_at_after_its_leading_one() {
    Assert.Equal(
      "at Probe.Run() in /home/a sat at/x.cs:line 4",
      FailureDetail.FrameLine("   at Probe.Run() in /home/a sat at/x.cs:line 4")
    );
    Assert.Equal("at bare text", FailureDetail.FrameLine("bare text"));
  }

  private sealed class VesselBox : BlockEntity, IBlockEntityContainer {
    public InventoryGeneric Slots { get; set; } = null!;

    public IInventory Inventory => Slots;

    public string InventoryClassName => "vessel";

    public void DropContents(Vec3d atPos) { }

    public void CheckInventoryClearedMidTick() { }
  }

  private static LiveFixture VesselFixture() {
    var fixture = new LiveFixture();
    fixture.Test.LoadAssets(
      [],
      null,
      ["game:woodbucket", "game:waterportion", "game:charcoal"]
    );
    StructureBreaks.Ready(
      fixture.Test,
      fixture.Test.World.GetBlock(new AssetLocation("game:woodbucket"))
    );
    Block box = TestBlocks.Configure(
      new Block { EntityClass = "vessel" },
      "exlib:vessel",
      900
    );
    fixture
      .Test.Register(box)
      .RegisterBlockEntityFactory(
        "vessel",
        () =>
          new VesselBox {
            Slots = new InventoryGeneric(
              1,
              "vessel",
              null,
              fixture.Test.Api
            ),
          }
      );
    fixture.Run(
      Steps(
        """[{ "place": { "id": "v", "code": "exlib:vessel", "at": [0, 0, 0] } }]"""
      )
    );
    return fixture;
  }

  private static ItemStack Held(LiveFixture fixture) =>
    ((VesselBox)fixture.Context.Setup.BlockEntityAt(new BlockPos(0, 0, 0))!)
      .Slots[0]
      .Itemstack;

  /// <summary>Fails when an insert drops the stack's content and litres, leaving the vessel empty.</summary>
  [Fact]
  public void An_insert_of_a_bucket_with_a_content_leaves_those_litres_in_it() {
    LiveFixture fixture = VesselFixture();
    fixture.Run(
      Steps(
        """[{ "insert": { "at": [0, 0, 0], "stack": "game:woodbucket", "content": "game:waterportion", "litres": 7 } }]"""
      )
    );
    ItemStack held = Held(fixture);
    var bucket = (BlockLiquidContainerBase)held.Collectible;
    Assert.Equal(7f, bucket.GetCurrentLitres(held), 2);
    Assert.Equal(
      "game:waterportion",
      bucket.GetContent(held).Collectible.Code.ToString()
    );
  }

  /// <summary>Fails when a hand's content and litres are dropped on the way to its stack (the interact,
  /// break and window steps read their stack there).</summary>
  [Fact]
  public void A_held_object_with_a_content_makes_a_filled_stack() {
    LiveFixture fixture = VesselFixture();
    ItemStack stack = StepValues.Held(
      fixture.Context,
      Newtonsoft.Json.Linq.JObject.Parse(
        """{ "stack": "game:woodbucket", "content": "game:waterportion", "litres": 7 }"""
      )
    );
    Assert.Equal(
      7f,
      ((BlockLiquidContainerBase)stack.Collectible).GetCurrentLitres(stack),
      2
    );
  }

  /// <summary>Fails when a content on a stack that is no liquid container is let through, or blames the
  /// mod instead of the scenario.</summary>
  [Fact]
  public void A_content_on_charcoal_fails_the_step_and_blames_the_scenario() {
    LiveFixture fixture = VesselFixture();
    StepFailure failure = Assert.Throws<StepFailure>(() =>
      fixture.Run(
        Steps(
          """[{ "insert": { "at": [0, 0, 0], "stack": "game:charcoal", "content": "game:waterportion", "litres": 1 } }]"""
        )
      )
    );
    Assert.Equal(Suspect.Scenario, failure.Suspect);
    Assert.Contains("is not a liquid container", failure.Message);
  }

  /// <summary>Fails when a content without its litres is let through, or blames the mod.</summary>
  [Fact]
  public void A_content_without_litres_fails_the_step_and_blames_the_scenario() {
    LiveFixture fixture = VesselFixture();
    StepFailure failure = Assert.Throws<StepFailure>(() =>
      fixture.Run(
        Steps(
          """[{ "insert": { "at": [0, 0, 0], "stack": "game:woodbucket", "content": "game:waterportion" } }]"""
        )
      )
    );
    Assert.Equal(Suspect.Scenario, failure.Suspect);
    Assert.Contains("needs its \"litres\"", failure.Message);
  }
}
