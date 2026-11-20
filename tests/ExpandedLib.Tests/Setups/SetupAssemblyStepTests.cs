using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ExpandedLib.Assembling;
using ExpandedLib.Helpers;
using ExpandedLib.Scenarios;
using ExpandedLib.Testing;
using ExpandedLib.Testing.Live;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Client;
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

  // Fails when the step fits with the cap anywhere but the main hand: the server takes the fitted cap from the active slot the packet names.
  [Fact]
  public void CapStep_FitsWithTheCapInTheMainHand() {
    using SetupScene scene = BuildInline(
      "[" + WePipe + ", { \"cap\": { \"at\": [0, 0, 0], \"face\": \"e\" } }]"
    );

    Assert.Equal(
      "setupasm:cap",
      RecordAt(scene, 0, 0, 0)
        .CapAt(BlockFacing.EAST)
        ?.Collectible.Code.ToString()
    );
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

  // Fails when the cap step's wear departs from the hands': a fit wears the wrench once, a refused fit not at all, a take-off once more.
  [Theory]
  [InlineData("fit", 1)]
  [InlineData("refusal", 0)]
  [InlineData("take-off", 2)]
  public void CapStep_WearsTheWrenchOnEachFitAndTakeOff(string click, int wear) {
    string face = click == "refusal" ? "e" : "w";
    string fails =
      click == "refusal" ? ", \"fails\": \"exlib-handassembly-capjoined\"" : "";
    string beside =
      click != "refusal"
        ? ""
        : "{ \"assemble\": { \"code\": \"setupasm:pipe-straight-we\", \"at\": [1, 0, 0], \"arms\": \"ew\" } },";
    string cap =
      "{ \"cap\": { \"at\": [0, 0, 0], \"face\": \""
      + face
      + "\""
      + fails
      + " } }";
    if (click == "take-off")
      cap += "," + cap;

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
              StorageFlags = plain.StorageFlags,
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

  private static readonly string FitFixturePath = Path.Combine(
    RepoPaths.Root,
    "tests",
    "ExpandedLib.Tests",
    "Setups",
    "Fixtures",
    "SetupFitPost"
  );

  private static SetupScene BuildFit(string steps) =>
    SetupScene.Build(
      SetupFile.Parse(
        "{\"setup\": \"t\", \"description\": \"d\", \"mods\": [\"setupfitpost\"], \"steps\": "
          + steps
          + ", \"seconds\": 10, \"record\": {}}",
        "t.json"
      ),
      [FitFixturePath],
      world => {
        foreach (int x in new[] { 0, 2 })
          world.Place(
            new BlockPos(x, 0, 0),
            world.World.GetBlock(new AssetLocation("setupfitpost:fitpost"))
          );
      },
      world => {
        world.RegisterClass("SetupFitPost", typeof(SetupFitPost));
        world.RegisterClass("SetupFitCell", typeof(SetupFitCell));
      }
    );

  // Fails when the fit step ignores its at and fits the first post it finds, so the post at 0 changes and the one at 2 does not.
  [Fact]
  public void FitStep_FitsTheNamedCell() {
    using SetupScene scene = BuildFit(
      "[{ \"fit\": { \"at\": [2, 0, 0], \"stack\": \"setupfitpost:shaft\" } }]"
    );

    Assert.Equal(
      "setupfitpost:fitpost",
      scene.World.GetBlock(new BlockPos(0, 0, 0)).Code.ToString()
    );
    Assert.Equal(
      "setupfitpost:fitdone",
      scene.World.GetBlock(new BlockPos(2, 0, 0)).Code.ToString()
    );
  }

  // Fails when stack is dropped from the required fields: a fit step with no stack holds nothing.
  [Fact]
  public void FitStep_FieldsRequired() {
    Exception e = Assert.ThrowsAny<Exception>(() =>
      BuildFit("[{ \"fit\": { \"at\": [0, 0, 0] } }]")
    );

    Assert.Contains("takes \"stack\"", e.Message);
  }

  private static SetupScene BuildCell(string steps) =>
    SetupScene.Build(
      SetupFile.Parse(
        "{\"setup\": \"t\", \"description\": \"d\", \"mods\": [\"setupfitpost\"], \"steps\": "
          + steps
          + ", \"seconds\": 10, \"record\": {}}",
        "t.json"
      ),
      [FitFixturePath],
      world => {
        world.Place(
          new BlockPos(0, 0, 0),
          world.World.GetBlock(new AssetLocation("setupfitpost:fitcell"))
        );
        world.PlaceFiller(
          new BlockPos(1, 0, 0),
          null,
          new BlockPos(0, 0, 0)
        );
      },
      world => {
        world.RegisterClass("SetupFitPost", typeof(SetupFitPost));
        world.RegisterClass("SetupFitCell", typeof(SetupFitCell));
      }
    );

  // Fails when the fit step resolves its at to the principal before the target is asked: the target sees the principal's cell, not the filler clicked, and the hit is lost.
  [Fact]
  public void FitStep_OnFillerCell() {
    using SetupScene scene = BuildCell(
      "[{ \"fit\": { \"at\": [1, 0, 0], \"hit\": [4, 8, 12], \"stack\": \"setupfitpost:shaft\" } }]"
    );

    var post = (SetupFitCell)scene.World.GetBlock(new BlockPos(0, 0, 0));
    Assert.NotNull(post.LastFit);
    Assert.Equal(new BlockPos(1, 0, 0), post.LastFit.Value.Cell);
    Assert.Equal(new Vec3d(0.25, 0.5, 0.75), post.LastFit.Value.Hit);
    Assert.Equal(2, post.Companions);
  }

  // Fails when the takeoff step aims the principal instead of the filler clicked, loses its hit, or draws the fit's tool where the target names another: the hammer is the removal's tool and the fixture's fit tool is the wrench.
  [Fact]
  public void TakeOffStep_OnFillerCell() {
    using SetupScene scene = BuildCell(
      "[{ \"fit\": { \"at\": [1, 0, 0], \"stack\": \"setupfitpost:shaft\" } }, { \"takeoff\": { \"at\": [1, 0, 0], \"hit\": [12, 8, 4] } }]"
    );

    var post = (SetupFitCell)scene.World.GetBlock(new BlockPos(0, 0, 0));
    Assert.NotNull(post.TakenOffAt);
    Assert.Equal(new BlockPos(1, 0, 0), post.TakenOffAt.Value.Cell);
    Assert.Equal(new Vec3d(0.75, 0.5, 0.25), post.TakenOffAt.Value.Hit);
  }

  private sealed class SetupFitCell : Block, IHandFitCellTarget {
    public HandFitPoint? LastFit { get; private set; }
    public HandFitPoint? TakenOffAt { get; private set; }
    public int Companions { get; private set; }

    public HandFitPlan? Plan(
      IBlockAccessor accessor,
      BlockPos principal,
      HandFitPoint at,
      ItemStack held,
      out string? failureCode
    ) {
      failureCode = null;
      return new HandFitPlan(
        [new HandFitCompanion("setupfitpost:washer", 2)],
        2
      );
    }

    public IReadOnlyList<HandFitMesh>? Ghost(
      ICoreClientAPI capi,
      BlockPos principal,
      HandFitPoint at,
      ItemStack held,
      HandFitPlan plan
    ) => null;

    public bool Fit(
      IWorldAccessor world,
      BlockPos principal,
      HandFitPoint at,
      IPlayer player,
      ItemSlot held,
      ref string failureCode
    ) {
      LastFit = at;
      Companions = ExCarried.Count(player, "setupfitpost:washer");
      held.TakeOut(1);
      held.MarkDirty();
      return true;
    }

    HandFitRemoval? IHandFitCellTarget.FittedAt(
      IBlockAccessor accessor,
      BlockPos principal,
      HandFitPoint at
    ) =>
      LastFit != null && TakenOffAt == null
        ? new HandFitRemoval([], 0, "setupfitpost:hammer")
        : null;

    public bool TakeOff(
      IWorldAccessor world,
      BlockPos principal,
      HandFitPoint at,
      IPlayer player
    ) {
      TakenOffAt = at;
      return true;
    }
  }

  private sealed class SetupFitPost : Block, IHandFitTarget {
    public Block? FitResult(
      IBlockAccessor accessor,
      BlockPos pos,
      ItemStack held,
      out string? failureCode
    ) {
      failureCode = null;
      return api.World.GetBlock(new AssetLocation("setupfitpost:fitdone"));
    }

    public bool Fit(
      IWorldAccessor world,
      BlockPos pos,
      IPlayer player,
      ItemSlot held,
      ref string failureCode
    ) {
      world.BlockAccessor.SetBlock(
        FitResult(world.BlockAccessor, pos, held.Itemstack, out _)!.Id,
        pos
      );
      held.TakeOut(1);
      held.MarkDirty();
      return true;
    }
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

  // Fails when a hand assembly gesture is made from the origin, not beside its cell: the cells of the run, 20 blocks out, are refused as out of range.
  [Fact]
  public void A_run_far_from_the_origin_is_fixed_from_beside_its_cells() {
    using SetupScene scene = BuildInline(
      """
      [{ "run": { "id": "r", "code": "setupasm:pipe-straight-we",
         "from": [20, 0, 0], "to": [22, 0, 0],
         "family": ["setupasm:pipe-straight-*"],
         "open": [{ "at": [20, 0, 0], "face": "w" }, { "at": [22, 0, 0], "face": "e" }] } }]
      """
    );

    for (int x = 20; x < 23; x++)
      Assert.True(RecordAt(scene, x, 0, 0).Fixed);
  }

  // Fails when an assemble ignores the spot a stand step left and stands beside its cell: the step 20 blocks from that spot is then fixed, not refused.
  [Fact]
  public void An_assemble_far_from_the_stand_spot_is_refused_out_of_range() {
    using SetupScene scene = BuildInline(
      "["
        + WePipe
        + ", { \"stand\": { \"at\": [20, 0, 0] } },"
        + " { \"assemble\": { \"code\": \"setupasm:pipe-straight-we\", \"at\": [1, 0, 0], \"arms\": \"ew\", \"fails\": \"exlib-handassembly-outofrange\" } }]"
    );

    Assert.Equal(0, scene.World.GetBlock(new BlockPos(1, 0, 0)).Id);
  }
}
