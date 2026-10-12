#if GAME_GE_1_22
using System;
using System.IO;
using System.Linq;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests.Setups;

/// <summary>What the <c>expect</c>, <c>wait</c> and <c>repeat</c> steps check and run, and the steps,
/// keys and values the reader refuses or fails by name (setups.md, What the harness runs).</summary>
[Collection("Setups")]
public class SetupExpectTests : IDisposable
{
  public void Dispose() => SetupPlacers.Clear();

  private static string Fixture(params string[] parts) =>
    Path.Combine([RepoPaths.Root, .. parts]);

  private static readonly string BoxPath = Fixture(
    "tests",
    "ExpandedLib.Tests",
    "Setups",
    "Fixtures",
    "SetupBox"
  );

  private static string ModPath(string id) =>
    id switch
    {
      "setupbox" => BoxPath,
      "setuphold" => Fixture(
        "tests",
        "ExpandedLib.Tests",
        "Setups",
        "Fixtures",
        "SetupHold"
      ),
      "platedpipes" => Fixture("samples", "PlatedPipes"),
      "twintubblower" => Fixture("samples", "TwinTubBlower"),
      _ => throw new ArgumentOutOfRangeException(nameof(id), id, "no such mod"),
    };

  private static SetupFile Inline(
    string mod,
    string steps,
    string? layout = null
  ) =>
    SetupFile.Parse(
      "{\"setup\": \"t\", \"description\": \"d\", \"mods\": [\""
        + mod
        + "\"], "
        + (layout == null ? "" : "\"layout\": " + layout + ", ")
        + "\"steps\": "
        + steps
        + ", \"seconds\": 10, \"record\": {}}",
      "t.json"
    );

  private static SetupScene Build(SetupFile file) =>
    SetupScene.Build(
      file,
      [.. file.Mods.Select(ModPath)],
      null,
      w =>
        SetupTickOrderTests
          .RegisterTick(w, [])
          .RegisterClass("SetupBox", typeof(BlockEntity))
          .RegisterBlockEntityFactory(
            "SetupBox",
            () =>
            {
              var box = new SetupBox();
              box.Behaviors.Add(new BoxBehaviour(box));
              return box;
            }
          )
          .RegisterClass("SetupHoldBlock", typeof(SetupHoldBlock))
          .RegisterClass("SetupHoldBox", typeof(SetupHoldBox))
          .RegisterBlockEntityFactory("SetupHoldBox", () => new SetupHoldBox())
          .RegisterClass("SetupHoldEntity", typeof(SetupHoldEntity))
          .RegisterBlockEntityFactory(
            "SetupHoldEntity",
            () => new SetupHoldEntity()
          )
          .RegisterClass("SetupHoldEmber", typeof(SetupHoldEmber))
          .RegisterBlockEntityFactory(
            "SetupHoldEmber",
            () => new SetupHoldEmber()
          )
          .RegisterBlockEntityBehaviorFactory(
            "SetupSpin",
            be => new SetupSpin(be)
          )
    );

  private static SetupScene Box(string steps) =>
    Build(Inline("setupbox", "[" + PlaceBox + ", " + steps + "]"));

  private static string BoxRefused(string steps) =>
    Assert
      .Throws<InvalidOperationException>(() =>
        Build(Inline("setupbox", "[" + PlaceBox + ", " + steps + "]"))
      )
      .Message;

  private static string Refused(string steps) =>
    Assert
      .Throws<InvalidOperationException>(() => Build(Inline("setupbox", steps)))
      .Message;

  private static SetupScene Dials(string steps) =>
    Build(Inline("setuphold", "[" + PlaceDial + ", " + steps + "]"));

  private static string DialsRefused(string steps) =>
    Assert.Throws<InvalidOperationException>(() => Dials(steps)).Message;

  private const string PlaceDial =
    "{\"place\": {\"id\": \"dial\", \"code\": \"setuphold:dial\", \"at\": [0, 0, 0]}}";

  private const string FiresMin2 =
    "{\"at\": [0, 0, 0], \"member\": \"Fires\", \"min\": 2}";

  private static int Fires(SetupScene scene) =>
    Assert
      .IsType<SetupHoldEntity>(
        scene.World.GetBlockEntity(scene.PositionOf("dial"))
      )
      .Fires;

  private const string PlaceBox =
    "{\"place\": {\"id\": \"box\", \"code\": \"setupbox:box\", \"at\": [0, 0, 0]}}";

  private const string SetLevel =
    "{\"set\": {\"at\": [0, 0, 0], \"member\": {\"Level\": 5}}}";

  private static SetupBox BoxOf(SetupScene scene) =>
    Assert.IsType<SetupBox>(
      scene.World.GetBlockEntity(scene.PositionOf("box"))
    );

  #region Expect

  // Fails when a block expectation is not evaluated, or its wildcard is ignored.
  [Fact]
  public void An_expect_block_passes_on_the_cell_s_block_and_fails_on_another()
  {
    using SetupScene scene = Box(
      "{\"expect\": {\"at\": [0, 0, 0], \"block\": \"setupbox:b*\"}}"
    );

    string message = BoxRefused(
      "{\"expect\": {\"at\": [0, 0, 0], \"block\": \"setupbox:slab\"}}"
    );

    Assert.NotNull(scene);
    Assert.Contains("t.json", message);
    Assert.Contains("steps[1]", message);
    Assert.Contains("setupbox:slab", message);
  }

  // Fails when a member test reads another member, or a failing test passes.
  [Fact]
  public void An_expect_member_compares_the_entity_member()
  {
    using SetupScene scene = Box(
      SetLevel
        + ", {\"expect\": {\"at\": [0, 0, 0], \"member\": \"Level\", \"equals\": 5}}"
    );

    string message = BoxRefused(
      SetLevel
        + ", {\"expect\": {\"at\": [0, 0, 0], \"member\": \"Level\", \"equals\": 6}}"
    );

    Assert.Equal(5, BoxOf(scene).Level);
    Assert.Contains("steps[2]", message);
    Assert.Contains("Level", message);
    Assert.Contains("5", message);
  }

  // Fails when a member whose entity is missing is read as a pass instead of failing the step.
  [Fact]
  public void An_expect_at_a_cell_with_no_entity_fails_naming_the_cell()
  {
    string message = BoxRefused(
      "{\"expect\": {\"at\": [3, 0, 0], \"member\": \"Level\", \"equals\": 0}}"
    );

    Assert.Contains("steps[1]", message);
    Assert.Contains("No block entity", message);
  }

  // Fails when a tree test reads the member instead of the saved tree, or never reads the tree.
  [Fact]
  public void An_expect_tree_compares_the_saved_key()
  {
    using SetupScene scene = Box(
      SetLevel
        + ", {\"expect\": {\"at\": [0, 0, 0], \"tree\": \"level\", \"min\": 5, \"max\": 5}}"
    );

    string message = BoxRefused(
      SetLevel
        + ", {\"expect\": {\"at\": [0, 0, 0], \"tree\": \"level\", \"min\": 6}}"
    );

    Assert.NotNull(scene);
    Assert.Contains("tree level", message);
  }

  // Fails when info reads no line of the entity, leaves out a behaviour's line, or never fails.
  [Fact]
  public void An_expect_info_matches_a_line_of_the_entity_and_its_behaviours()
  {
    using SetupScene scene = Box(
      SetLevel
        + ", {\"expect\": {\"at\": [0, 0, 0], \"info\": \"^Level: 5$\"}}"
        + ", {\"expect\": {\"at\": [0, 0, 0], \"info\": \"^Nudged: 0$\"}}"
    );

    string message = BoxRefused(
      SetLevel + ", {\"expect\": {\"at\": [0, 0, 0], \"info\": \"^Level: 9$\"}}"
    );

    Assert.NotNull(scene);
    Assert.Contains("steps[2]", message);
    Assert.Contains("Level: 5", message);
  }

  // Fails when the footprint of a placed megablock is not found filled, or an air check passes.
  [Fact]
  public void An_expect_footprint_holds_the_filled_cells_and_fails_air()
  {
    const string layout =
      "{\"legend\": {\"B\": {\"id\": \"blower\", \"code\": \"twintubblower:blower-twintubblower-n\"}, \"b\": {\"cells\": \"blower\"}}, "
      + "\"layers\": {\"2\": [\"b\", \"b\", \"b\"], \"1\": [\"b\", \"b\", \"B\"]}, \"origin\": [0, -2]}";

    using SetupScene scene = Build(
      Inline(
        "twintubblower",
        "[{\"expect\": {\"of\": \"blower\", \"footprint\": \"filled\"}}]",
        layout
      )
    );
    string message = Assert
      .Throws<InvalidOperationException>(() =>
        Build(
          Inline(
            "twintubblower",
            "[{\"expect\": {\"of\": \"blower\", \"footprint\": \"air\"}}]",
            layout
          )
        )
      )
      .Message;

    Assert.NotNull(scene);
    Assert.Contains("steps[0]", message);
    Assert.Contains("footprint cell", message);
    Assert.Contains("expected air", message);
  }

  private const string PipeLayout =
    "{\"legend\": {\"P\": {\"run\": \"main\", \"code\": \"platedpipes:pipe-plated-straight-we\", "
    + "\"family\": [\"platedpipes:pipe-plated-straight-*\", \"platedpipes:pipe-plated-bend-*\"]}, \"o\": {\"open\": true}}, "
    + "\"layers\": {\"0\": [\"oPPPo\"]}}";

  private const string Charge =
    "{\"call\": {\"at\": [1, 0, 0], \"method\": \"TryProduce\", \"args\": [10, 100, \"Steam\", 1, true], \"returns\": true}}";

  // Fails when a pipe network member is read from another network type, or not read at all.
  [Fact]
  public void An_expect_network_reads_the_pipe_network_at_the_cell()
  {
    using SetupScene scene = Build(
      Inline(
        "platedpipes",
        "["
          + Charge
          + ", {\"expect\": {\"at\": [1, 0, 0], \"network\": \"pipe\"}}, "
          + "{\"expect\": {\"at\": [1, 0, 0], \"network\": \"pipe\", \"member\": \"MediumType\", \"equals\": \"Steam\"}}, "
          + "{\"expect\": {\"at\": [1, 0, 0], \"network\": \"pipe\", \"member\": \"Volume\", \"min\": 0.1}}]",
        PipeLayout
      )
    );
    string message = Assert
      .Throws<InvalidOperationException>(() =>
        Build(
          Inline(
            "platedpipes",
            "["
              + Charge
              + ", {\"expect\": {\"at\": [1, 0, 0], \"network\": \"pipe\", \"member\": \"MediumType\", \"equals\": \"Water\"}}]",
            PipeLayout
          )
        )
      )
      .Message;

    string other = Assert
      .Throws<InvalidOperationException>(() =>
        Build(
          Inline(
            "platedpipes",
            "[{\"expect\": {\"at\": [1, 0, 0], \"network\": \"molten\"}}]",
            PipeLayout
          )
        )
      )
      .Message;

    Assert.NotNull(scene);
    Assert.Contains("steps[1]", message);
    Assert.Contains("pipe MediumType", message);
    Assert.Contains("steps[0]", other);
  }

  // Fails when two cells of one network are read as two, or a cell outside any network as in one.
  [Fact]
  public void An_expect_same_holds_for_cells_of_one_network_and_fails_for_others()
  {
    using SetupScene scene = Build(
      Inline(
        "platedpipes",
        "[{\"expect\": {\"at\": [1, 0, 0], \"network\": \"pipe\", \"same\": [3, 0, 0]}}]",
        PipeLayout
      )
    );
    string message = Assert
      .Throws<InvalidOperationException>(() =>
        Build(
          Inline(
            "platedpipes",
            "[{\"expect\": {\"at\": [1, 0, 0], \"network\": \"pipe\", \"same\": [9, 0, 0]}}]",
            PipeLayout
          )
        )
      )
      .Message;

    Assert.NotNull(scene);
    Assert.Contains(
      "(1, 0, 0) and (9, 0, 0) are not in one pipe network",
      message
    );
  }

  private const string PairLayout =
    "{\"legend\": {\"A\": {\"id\": \"first\", \"code\": \"twintubblower:blower-twintubblower-n\"}, \"a\": {\"cells\": \"first\"}, "
    + "\"B\": {\"id\": \"second\", \"code\": \"twintubblower:blower-twintubblower-s\"}, \"b\": {\"cells\": \"second\"}}, "
    + "\"layers\": {\"2\": [\" a\", \" a\", \"ba\", \"b\", \"b\"], \"1\": [\" a\", \" a\", \"BA\", \"b\", \"b\"]}, \"origin\": [0, -2]}";

  private const string LoneLayout =
    "{\"legend\": {\"A\": {\"id\": \"first\", \"code\": \"twintubblower:blower-twintubblower-n\"}, \"a\": {\"cells\": \"first\"}}, "
    + "\"layers\": {\"2\": [\" a\", \" a\", \" a\"], \"1\": [\" a\", \" a\", \" A\"]}, \"origin\": [0, -2]}";

  private static string MechanicalExpect(string rest) =>
    "{\"expect\": {\"at\": [1, 2, 0], \"network\": \"mechanical\", "
    + rest
    + "}}";

  // Fails when the type reads no network, the unjoined line, an unstepped line or another member.
  [Fact]
  public void An_expect_network_mechanical_reads_the_stepped_line_s_resistance()
  {
    using SetupScene scene = Build(
      Inline(
        "twintubblower",
        "[{\"wait\": 1}, "
          + MechanicalExpect(
            "\"member\": \"NetworkResistance\", \"min\": 0.0001"
          )
          + ", {\"expect\": {\"at\": [1, 2, 0], \"network\": \"mechanical\", \"same\": [0, 2, 0]}}]",
        PairLayout
      )
    );
    string slow = Assert
      .Throws<InvalidOperationException>(() =>
        Build(
          Inline(
            "twintubblower",
            "[" + MechanicalExpect("\"member\": \"Speed\", \"min\": 1") + "]",
            PairLayout
          )
        )
      )
      .Message;
    string none = Assert
      .Throws<InvalidOperationException>(() =>
        Build(
          Inline(
            "twintubblower",
            "[{\"expect\": {\"at\": [5, 5, 5], \"network\": \"mechanical\", \"member\": \"Speed\", \"equals\": 0}}]",
            PairLayout
          )
        )
      )
      .Message;

    Assert.NotNull(scene);
    Assert.Contains("mechanical Speed", slow);
    Assert.Contains("No mechanical network", none);
  }

  // Fails when a step reads the line the game's discovery made, which gives a lone blower a line, instead
  // of the scene's joined lines, which give it none.
  [Fact]
  public void An_expect_network_mechanical_in_the_steps_finds_no_line_for_a_lone_blower()
  {
    using SetupScene scene = Build(
      Inline(
        "twintubblower",
        "[" + MechanicalExpect("\"equals\": false") + "]",
        LoneLayout
      )
    );

    Assert.NotNull(scene);
  }

  // Fails when the mechanical Speed is read with its sign, or the torque is read from another member.
  [Fact]
  public void An_expect_network_mechanical_reads_a_reversed_line_s_speed_without_its_sign()
  {
    const string axles =
      "{\"place\": {\"id\": \"first\", \"code\": \"setuphold:spinaxle-ns\", \"at\": [0, 0, 0]}}, "
      + "{\"place\": {\"code\": \"setuphold:spinaxle-ns\", \"at\": [0, 0, 1]}}, "
      + "{\"call\": {\"at\": [0, 0, 0], \"method\": \"SetupSpin.Drive\", \"args\": [-2, 3]}}";
    const string line =
      "{\"expect\": {\"at\": [0, 0, 1], \"network\": \"mechanical\", ";

    using SetupScene scene = Build(
      Inline(
        "setuphold",
        "["
          + axles
          + ", "
          + line
          + "\"member\": \"Speed\", \"equals\": 2}}, "
          + line
          + "\"member\": \"TotalAvailableTorque\", \"equals\": 3}}]"
      )
    );

    Assert.NotNull(scene);
  }

  #endregion

  #region Wait and repeat

  // Fails when a bare wait ticks nothing, or ticks more or fewer seconds than it names.
  [Fact]
  public void A_wait_of_seconds_ticks_the_world_that_long()
  {
    using SetupScene scene = Dials(
      "{\"wait\": 3}, {\"wait\": {\"seconds\": 2}}"
    );

    Assert.Equal(5, Fires(scene));
  }

  // Fails when an until is not polled before each tick, or runs on after it holds.
  [Fact]
  public void A_wait_until_a_reading_holds_stops_when_it_does()
  {
    using SetupScene scene = Dials(
      "{\"wait\": {\"until\": " + FiresMin2 + ", \"max\": 10}}"
    );

    Assert.Equal(2, Fires(scene));
  }

  // Fails when an until that never holds passes, or the failure leaves out the step or the reading.
  [Fact]
  public void A_wait_until_a_reading_that_never_holds_fails_at_max_with_the_reading()
  {
    string message = DialsRefused(
      "{\"wait\": {\"until\": {\"at\": [0, 0, 0], \"member\": \"Fires\", \"min\": 99}, \"max\": 3}}"
    );

    Assert.Contains("steps[1]", message);
    Assert.Contains("not met in 3.0 s", message);
    Assert.Contains("Fires", message);
  }

  // Fails when hold is not checked each second, or a hold that breaks does not fail the step.
  [Fact]
  public void A_wait_hold_fails_the_moment_its_reading_stops_holding()
  {
    string broken = DialsRefused(
      "{\"wait\": {\"seconds\": 4, \"hold\": {\"at\": [0, 0, 0], \"member\": \"Fires\", \"max\": 1}}}"
    );
    using SetupScene kept = Dials(
      "{\"wait\": {\"seconds\": 4, \"hold\": {\"at\": [0, 0, 0], \"member\": \"Fires\", \"max\": 4}}}"
    );

    Assert.Contains("hold broke after 2.0 s", broken);
    Assert.Equal(4, Fires(kept));
  }

  // Fails when a wait object names neither seconds nor until, or an until has no max.
  [Theory]
  [InlineData(
    "{\"wait\": {\"hold\": {\"at\": [0, 0, 0], \"block\": \"*\"}}}",
    "wait takes seconds"
  )]
  [InlineData(
    "{\"wait\": {\"until\": {\"at\": [0, 0, 0], \"block\": \"*\"}}}",
    "\"until\" takes \"max\""
  )]
  public void A_wait_without_its_keys_fails_the_step(string wait, string said)
  {
    Assert.Contains(said, DialsRefused(wait));
  }

  // Fails when repeat runs its steps fewer or more times than it names.
  [Fact]
  public void A_repeat_runs_its_steps_times_times()
  {
    using SetupScene scene = Dials(
      "{\"repeat\": {\"times\": 3, \"steps\": [{\"wait\": 1}]}}"
    );

    Assert.Equal(3, Fires(scene));
  }

  // Fails when an until is not checked after a round, so the repeat runs to times or max.
  [Fact]
  public void A_repeat_until_stops_at_the_first_round_that_holds()
  {
    using SetupScene scene = Dials(
      "{\"repeat\": {\"until\": "
        + FiresMin2
        + ", \"max\": 9, \"steps\": [{\"wait\": 1}]}}"
    );

    Assert.Equal(2, Fires(scene));
  }

  // Fails when an until never met passes, or the failure names neither the rounds nor the reading.
  [Fact]
  public void A_repeat_until_never_met_fails_after_max_rounds()
  {
    string message = DialsRefused(
      "{\"repeat\": {\"until\": {\"at\": [0, 0, 0], \"member\": \"Fires\", \"min\": 99}, \"max\": 3, \"steps\": [{\"wait\": 1}]}}"
    );

    Assert.Contains("steps[1]", message);
    Assert.Contains("until not met after 3 round(s)", message);
    Assert.Contains("Fires", message);
  }

  // Fails when a repeat with neither times nor until runs once and passes.
  [Fact]
  public void A_repeat_without_times_or_until_fails_the_step()
  {
    Assert.Contains(
      "repeat takes \"times\" or \"until\"",
      DialsRefused("{\"repeat\": {\"steps\": []}}")
    );
  }

  #endregion

  #region Refusals

  // Fails when expect drops is run, or refused without naming itself.
  [Fact]
  public void An_expect_drops_is_refused_naming_the_kind()
  {
    string message = BoxRefused("{\"expect\": {\"drops\": \"declared\"}}");

    Assert.Contains("steps[1]", message);
    Assert.Contains("\"expect drops\" is refused", message);
  }

  // Fails when a climate key is accepted, or the refusal leaves out what the world lacks.
  [Fact]
  public void A_climate_is_refused_naming_what_the_world_lacks()
  {
    SetupFile file = SetupFile.Parse(
      "{\"setup\": \"t\", \"description\": \"d\", \"mods\": [\"setupbox\"], \"steps\": [], "
        + "\"climate\": {\"temperature\": 5}, \"seconds\": 10, \"record\": {}}",
      "t.json"
    );

    string message = Assert
      .Throws<InvalidOperationException>(() => Build(file))
      .Message;

    Assert.Contains("\"climate\" is refused", message);
    Assert.Contains("no weather", message);
  }

  // Fails when a numbered stage is rejected by the parser as a position instead of reaching the
  // reader's refusal, or accepted as a build to completion.
  [Fact]
  public void A_complete_to_a_stage_number_is_refused_by_the_reader()
  {
    string message = BoxRefused("{\"complete\": {\"of\": \"box\", \"to\": 2}}");

    Assert.Contains("steps[1]", message);
    Assert.Contains("\"complete\" to a stage is refused", message);
  }

  // Fails when a complete to a word, a fraction or a bool loads, or escapes the parse as a raw cast
  // exception instead of naming the key.
  [Theory]
  [InlineData("\"some\"")]
  [InlineData("2.5")]
  [InlineData("2.0")]
  [InlineData("true")]
  public void A_complete_to_something_other_than_all_or_a_whole_number_fails_the_parse(
    string to
  )
  {
    InvalidOperationException e = Assert.Throws<InvalidOperationException>(() =>
      Inline(
        "setupbox",
        "["
          + PlaceBox
          + ", {\"complete\": {\"of\": \"box\", \"to\": "
          + to
          + "}}]"
      )
    );

    Assert.Contains("takes \"all\" or a whole number", e.Message);
  }

  #endregion

  #region Failures by file and step

  // Fails when a negative slot indexes the inventory and escapes as a raw exception.
  [Fact]
  public void An_insert_into_a_negative_slot_fails_naming_the_file_and_step()
  {
    string message = BoxRefused(
      "{\"insert\": {\"at\": [0, 0, 0], \"stack\": \"setupbox:slab\", \"slot\": -1}}"
    );

    Assert.Contains("t.json: steps[1]", message);
    Assert.Contains("slot -1 is negative", message);
  }

  // Fails when a tree value that does not fit the key's type escapes as a raw cast exception.
  [Fact]
  public void A_set_tree_value_of_the_wrong_type_fails_naming_the_file_step_and_key()
  {
    string message = BoxRefused(
      "{\"set\": {\"at\": [0, 0, 0], \"tree\": {\"level\": \"high\"}}}"
    );

    Assert.Contains("t.json: steps[1]", message);
    Assert.Contains("does not fit tree key \"level\"", message);
  }

  // Fails when an enum member given as something other than a name or a number escapes as a raw
  // cast exception.
  [Fact]
  public void A_set_enum_member_given_a_list_fails_naming_the_file_and_step()
  {
    string message = BoxRefused(
      "{\"set\": {\"at\": [0, 0, 0], \"member\": {\"_state\": [1]}}}"
    );

    Assert.Contains("t.json: steps[1]", message);
    Assert.Contains("is not a member of BoxState", message);
  }

  #endregion
}
#endif
