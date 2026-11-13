using System;
using System.IO;
using System.Linq;
using ExpandedLib.Assembling;
using ExpandedLib.Scenarios;
using ExpandedLib.Testing;
using ExpandedLib.Testing.Live;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests.Setups;

/// <summary>The steps and the reading that drive hand assembly (setups.md, Steps the setups add): which runner takes them, and what the <c>asm</c> reading says of a cell.</summary>
public class SetupAssemblyStepTests {
  private static string FixtureText() =>
    File.ReadAllText(
      Path.Combine(
        RepoPaths.Root,
        "tests",
        "ExpandedLib.Tests",
        "Setups",
        "Files",
        "hand-assembly.json"
      )
    );

  private static object Read(string json, Scenarios.SetupTestWorld world) =>
    SetupReadings
      .Parse(
        JToken.Parse(json),
        new SetupJson.Where("test.json", "steps[0] (expect)"),
        new ReadingScope(null, true, InCheck: false, new SetupReferences())
      )
      .Read(new ReadingContext(world));

  private static Scenarios.SetupTestWorld Assembled(out BEBehaviorAssembled record) {
    var world = new Scenarios.SetupTestWorld();
    Item bolt = world.Test.RegisterItem("test:bolt");
    var be = new BlockEntityAssembled();
    record = new BEBehaviorAssembled(be);
    be.Behaviors.Add(record);
    world.Test.Place(
      new BlockPos(0, 0, 0),
      TestBlocks.Configure(new Block(), "test:piece", 1),
      be
    );
    record.Fixed = true;
    record.SetFastener(BlockFacing.NORTH, new ItemStack(bolt, 2));
    return world;
  }

  // Fails when the live runner does not run assemble, takeoff, the place keys or the asm reading of the fixture setup.
  [Fact]
  public void The_fixture_setup_is_a_file_the_live_runner_runs() {
    ScenarioCase scenario = ScenarioCase.Load(
      "exlib/case",
      FixtureText(),
      "hand-assembly.json",
      ["platedpipes"],
      "1.22"
    );

    Assert.Equal(
      ["place", "assemble", "assemble", "expect", "takeoff"],
      scenario.Model.Steps.Select(s => s.Kind)
    );
    Assert.All(scenario.Model.Steps, s => Assert.Null(ScenarioSteps.Refusal(s)));
  }

  // Fails when the harness reader runs a step that needs a player, so a file with assemble passes there unseen.
  [Theory]
  [InlineData("assemble", "the fake world has no player to fix a part with")]
  [InlineData("takeoff", "the fake world has no player to take a piece off with")]
  public void The_harness_reader_refuses_the_steps_that_need_a_player(
    string kind,
    string reason
  ) {
    string body =
      kind == "assemble"
        ? "{\"code\": \"platedpipes:pipe-plated-straight-we\", \"at\": [0, 0, 0]}"
        : "{\"at\": [0, 0, 0]}";
    SetupFile file = SetupFile.Parse(
      "{\"setup\": \"t\", \"description\": \"d\", \"mods\": [\"platedpipes\"], \"steps\": [{\""
        + kind
        + "\": "
        + body
        + "}], \"seconds\": 10, \"record\": {}}",
      "t.json"
    );

    Exception e = Assert.ThrowsAny<Exception>(() =>
      SetupScene.Build(
        file,
        [Path.Combine(RepoPaths.Root, "samples", "PlatedPipes")]
      )
    );

    Assert.Contains(reason, e.Message);
  }

  // Fails when the fixed flag is read as anything but the record's.
  [Fact]
  public void Asm_reads_whether_the_piece_is_fixed() {
    Scenarios.SetupTestWorld world = Assembled(out BEBehaviorAssembled record);

    Assert.Equal(true, Read("""{ "at": [0, 0, 0], "asm": "fixed" }""", world));
    record.Fixed = false;
    Assert.Equal(false, Read("""{ "at": [0, 0, 0], "asm": "fixed" }""", world));
  }

  // Fails when a face letter reads another face's fasteners, or a face with none reads anything but 0.
  [Fact]
  public void Asm_reads_the_fasteners_each_face_holds() {
    Scenarios.SetupTestWorld world = Assembled(out _);

    Assert.Equal(2f, Read("""{ "at": [0, 0, 0], "asm": "n" }""", world));
    Assert.Equal(0f, Read("""{ "at": [0, 0, 0], "asm": "e" }""", world));
  }

  // Fails when a cell with no record reads as unfixed instead of failing.
  [Fact]
  public void Asm_of_a_cell_with_no_record_fails() {
    var world = new Scenarios.SetupTestWorld();
    world.Test.Place(
      new BlockPos(0, 0, 0),
      TestBlocks.Configure(new Block(), "test:plain", 1)
    );

    Assert.Throws<SetupReadingException>(() =>
      Read("""{ "at": [0, 0, 0], "asm": "fixed" }""", world)
    );
  }

  // Fails when a word that is not a face, fixed or joint parses.
  [Fact]
  public void Asm_refuses_a_word_that_names_nothing() {
    Assert.Throws<SetupFileException>(() =>
      Read("""{ "at": [0, 0, 0], "asm": "up" }""", new Scenarios.SetupTestWorld())
    );
  }
}
