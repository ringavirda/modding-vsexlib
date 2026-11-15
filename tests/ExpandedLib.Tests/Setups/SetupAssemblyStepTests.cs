using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ExpandedLib.Assembling;
using ExpandedLib.Scenarios;
using ExpandedLib.Testing;
using ExpandedLib.Testing.Live;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
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

  private static Scenarios.SetupTestWorld Assembled(
    out BEBehaviorAssembled record
  ) {
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
      ["setupasm"],
      "1.22"
    );

    Assert.Equal(
      ["place", "assemble", "assemble", "expect", "expect", "takeoff"],
      scenario.Model.Steps.Select(s => s.Kind)
    );
    Assert.All(
      scenario.Model.Steps,
      s => Assert.Null(ScenarioSteps.Refusal(s))
    );
  }

  private static readonly string FixturePath = Path.Combine(
    RepoPaths.Root,
    "tests",
    "ExpandedLib.Tests",
    "Setups",
    "Fixtures",
    "SetupAsm"
  );

  private static SetupScene BuildInline(string steps, string layout = "") =>
    SetupScene.Build(
      SetupFile.Parse(
        "{\"setup\": \"t\", \"description\": \"d\", \"mods\": [\"setupasm\"], "
          + layout
          + "\"steps\": "
          + steps
          + ", \"seconds\": 10, \"record\": {}}",
        "t.json"
      ),
      [FixturePath]
    );

  private static BEBehaviorAssembled RecordAt(
    SetupScene scene,
    int x,
    int y,
    int z
  ) =>
    scene
      .World.GetBlockEntity(new BlockPos(x, y, z))!
      .GetBehavior<BEBehaviorAssembled>();

  // Fails when place, assemble or takeoff, run by the harness reader, skip the server's handler: a part is
  // then set by the world and holds no record, no fastener is spent, or the piece stays after its take-off.
  [Fact]
  public void The_fixture_setup_runs_as_a_harness_row() {
    using SetupScene scene = SetupScene.Build(
      SetupFile.Load(
        Path.Combine(
          RepoPaths.Root,
          "tests",
          "ExpandedLib.Tests",
          "Setups",
          "Files",
          "hand-assembly.json"
        )
      ),
      [FixturePath]
    );

    Assert.True(RecordAt(scene, 0, 0, 0).Fixed);
    Assert.Equal(0, scene.World.GetBlock(new BlockPos(1, 0, 0)).Id);
    Assert.Equal(0, scene.World.GetBlock(new BlockPos(2, 0, 0)).Id);
    Assert.Equal(["granted 2 setupasm:bolt for hand assembly"], scene.StandIns);
  }

  // Fails when a run of an opted-in block is laid by the world, so its cells are not fixed.
  [Fact]
  public void The_cells_of_a_run_of_an_opted_in_block_are_fixed_by_hand() {
    using SetupScene scene = BuildInline(
      """
      [{ "run": { "id": "r", "code": "setupasm:pipe-straight-we",
         "from": [0, 0, 0], "to": [2, 0, 0],
         "family": ["setupasm:pipe-straight-*"],
         "open": [{ "at": [0, 0, 0], "face": "w" }, { "at": [2, 0, 0], "face": "e" }] } }]
      """
    );

    for (int x = 0; x < 3; x++)
      Assert.True(RecordAt(scene, x, 0, 0).Fixed);
  }

  // Fails when a part whose candidates the arms leave empty is fixed anyway, or its refusal is not read.
  [Fact]
  public void An_assemble_that_no_candidate_answers_fails_the_step() {
    Exception e = Assert.ThrowsAny<Exception>(() =>
      BuildInline(
        """
        [{ "assemble": { "code": "setupasm:pipe-straight-we", "at": [0, 0, 0], "arms": "es", "fails": "exlib-handassembly-needstool" } }]
        """
      )
    );

    Assert.Contains("harness-nocandidate", e.Message);
  }

  private const string WePipe =
    """{ "place": { "id": "first", "code": "setupasm:pipe-straight-we", "at": [0, 0, 0], "face": "u", "arms": "ew" } }""";

  // Fails when the cap step ignores its face and clicks the centre, so the first arm is capped whichever is named; a second click on the capped face must take the cap off.
  [Fact]
  public void CapStep_FitsTheNamedFace() {
    using SetupScene scene = BuildInline(
      "[" + WePipe + ", { \"cap\": { \"at\": [0, 0, 0], \"face\": \"w\" } }]"
    );

    BEBehaviorAssembled record = RecordAt(scene, 0, 0, 0);
    Assert.NotNull(record.CapAt(BlockFacing.WEST));
    Assert.Null(record.CapAt(BlockFacing.EAST));
    Assert.Equal(2, record.CapFastenersAt(BlockFacing.WEST)!.StackSize);

    using SetupScene off = BuildInline(
      "["
        + WePipe
        + ", { \"cap\": { \"at\": [0, 0, 0], \"face\": \"w\" } }, { \"cap\": { \"at\": [0, 0, 0], \"face\": \"w\" } }]"
    );
    Assert.Null(RecordAt(off, 0, 0, 0).CapAt(BlockFacing.WEST));
  }

  // Fails when a take-off step stages a fresh cap and fasteners before the server removes the fitted ones: the pack, holding one cap and one joint's bolts, is overdrawn.
  [Fact]
  public void CapStep_TakeOffDrawsNothingFromThePack() {
    using SetupScene scene = BuildInline(
      "["
        + WePipe
        + ", { \"cap\": { \"at\": [0, 0, 0], \"face\": \"w\" } }, { \"cap\": { \"at\": [0, 0, 0], \"face\": \"w\" } }]",
      "\"pack\": { \"scope\": \"all\", \"gathered\": { \"setupasm:wrench\": 1, \"setupasm:pipe-straight-we\": 1, \"setupasm:cap\": 1, \"setupasm:bolt\": 2 } }, "
    );

    Assert.Null(RecordAt(scene, 0, 0, 0).CapAt(BlockFacing.WEST));
  }

  // Fails when the cap step wears the wrench only on a click the server took, or in creative: a refusal wears it as a fit does.
  [Theory]
  [InlineData("fit", 1)]
  [InlineData("refusal", 1)]
  public void CapStep_WearsTheWrenchOnEveryClick(string click, int wear) {
    string face = click == "fit" ? "w" : "e";
    string fails =
      click == "fit" ? "" : ", \"fails\": \"exlib-handassembly-capjoined\"";
    string beside =
      click == "fit"
        ? ""
        : "{ \"assemble\": { \"code\": \"setupasm:pipe-straight-we\", \"at\": [1, 0, 0], \"arms\": \"ew\" } },";
    string cap =
      "{ \"cap\": { \"at\": [0, 0, 0], \"face\": \""
      + face
      + "\""
      + fails
      + " } }";

    int Uses(string last) {
      int uses = 0;
      using SetupScene scene = SetupScene.Build(
        SetupFile.Parse(
          "{\"setup\": \"t\", \"description\": \"d\", \"mods\": [\"setupasm\"], \"steps\": ["
            + WePipe
            + ","
            + beside
            + last
            + "], \"seconds\": 10, \"record\": {}}",
          "t.json"
        ),
        [FixturePath],
        w => {
          Item plain = w.World.GetItem(new AssetLocation("setupasm:wrench"));
          w.Register(
            new WearCounter(() => uses++) {
              Code = plain.Code,
              ItemId = plain.ItemId,
              Durability = 100,
            }
          );
        }
      );
      return uses;
    }

    Assert.Equal(wear, Uses(cap) - Uses("{ \"wait\": 1 }"));
  }

  private sealed class WearCounter(System.Action worn) : Item {
    public override void DamageItem(
      IWorldAccessor world,
      Entity byEntity,
      ItemSlot itemslot,
      int amount = 1,
      bool destroyOnZeroDurability = true
    ) => worn();
  }

  // Fails when the step names the refusal and a joined face is capped, or the refusal code is not compared.
  [Fact]
  public void CapStep_FailsNamesTheRefusal() {
    using SetupScene scene = BuildInline(
      "["
        + WePipe
        + ", { \"assemble\": { \"code\": \"setupasm:pipe-straight-we\", \"at\": [1, 0, 0], \"arms\": \"ew\" } },"
        + " { \"cap\": { \"at\": [0, 0, 0], \"face\": \"e\", \"fails\": \"exlib-handassembly-capjoined\" } }]"
    );

    Assert.Null(RecordAt(scene, 0, 0, 0).CapAt(BlockFacing.EAST));
  }

  // Fails when a step naming another refusal than the server's passes.
  [Fact]
  public void CapStep_FailsOnAnotherRefusalThanNamed() {
    Exception e = Assert.ThrowsAny<Exception>(() =>
      BuildInline(
        "["
          + WePipe
          + ", { \"assemble\": { \"code\": \"setupasm:pipe-straight-we\", \"at\": [1, 0, 0], \"arms\": \"ew\" } },"
          + " { \"cap\": { \"at\": [0, 0, 0], \"face\": \"e\", \"fails\": \"exlib-handassembly-capclosed\" } }]"
      )
    );

    Assert.Contains("was refused with exlib-handassembly-capjoined", e.Message);
  }

  // Fails when a step expecting a refusal passes after the cap was fitted.
  [Fact]
  public void CapStep_FailsWhenTheFitSucceeds() {
    Exception e = Assert.ThrowsAny<Exception>(() =>
      BuildInline(
        "["
          + WePipe
          + ", { \"cap\": { \"at\": [0, 0, 0], \"face\": \"e\", \"fails\": \"exlib-handassembly-capclosed\" } }]"
      )
    );

    Assert.Contains("expected the refusal", e.Message);
  }

  // Fails when a step with no refusal named passes a refused click.
  [Fact]
  public void CapStep_RefusalWithoutFailsFailsTheStep() {
    Exception e = Assert.ThrowsAny<Exception>(() =>
      BuildInline(
        "["
          + WePipe
          + ", { \"assemble\": { \"code\": \"setupasm:pipe-straight-we\", \"at\": [1, 0, 0], \"arms\": \"ew\" } },"
          + " { \"cap\": { \"at\": [0, 0, 0], \"face\": \"e\" } }]"
      )
    );

    Assert.Contains("could not cap", e.Message);
  }

  // Fails when face is dropped from the required fields: a cap step with no face clicks the up face.
  [Fact]
  public void CapStep_FieldsRequired() {
    Exception e = Assert.ThrowsAny<Exception>(() =>
      BuildInline("[" + WePipe + ", { \"cap\": { \"at\": [0, 0, 0] } }]")
    );

    Assert.Contains("takes \"face\"", e.Message);
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
      Read(
        """{ "at": [0, 0, 0], "asm": "up" }""",
        new Scenarios.SetupTestWorld()
      )
    );
  }
}
