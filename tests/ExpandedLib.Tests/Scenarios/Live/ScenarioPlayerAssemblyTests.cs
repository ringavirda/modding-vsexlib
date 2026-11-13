using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Assembling;
using ExpandedLib.Scenarios;
using ExpandedLib.Testing;
using ExpandedLib.Testing.Live;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests.Scenarios.Live;

/// <summary>The scenario player's hand assembly: the candidate a placement takes, the route an
/// opted-in block takes, the tool and fasteners it is paid with, and the take-off's returns.</summary>
public class ScenarioPlayerAssemblyTests {
  private static readonly BlockPos Cell = new(64, 16, 64, 0);
  private static readonly BlockPos Ground = new(64, 15, 64, 0);
  private static readonly BlockPos North = Cell.AddCopy(BlockFacing.NORTH);
  private static readonly BlockPos East = Cell.AddCopy(BlockFacing.EAST);

  private const string Pack =
    """{ pack: { scope: "all", gathered: { "test:wrench-iron": 1, "test:bolt": 4 } } }""";

  private static ScenarioContext Context(
    HandAssemblyFixTests.Scene scene,
    List<string> lines,
    string extra = "{}"
  ) {
    TestWorld test = scene.World;
    var ctx = new ScenarioContext(
      test.World,
      new ScenarioWorld(test.World, new BlockPos(0, 0, 0), test.Networks),
      () => 0,
      new ScenarioErrors([]),
      lines.Add,
      "case.json"
    );
    if (extra != "{}")
      ctx.Pack = SetupPack.Start(
        LiveFixture.Case("[]", extra).Model,
        _ => null
      );
    return ctx;
  }

  private static AssemblyCandidate Candidate(
    string code,
    int id,
    string? turn = null,
    params BlockFacing[] arms
  ) => new(TestBlocks.Configure(new Block(), code, id), "", turn, arms, [], 0);

  private static HandAssemblyFixTests.Scene Scene() {
    var scene = HandAssemblyFixTests.Scene.Standing(["nes", "nw", "ns"]);
    scene.Pipe(North, "ns");
    scene.Pipe(East, "we");
    return scene;
  }

  private static AssemblyResult? Place(
    HandAssemblyFixTests.Scene scene,
    ScenarioContext ctx,
    string token,
    int steps = 0
  ) =>
    HandAssemblyPlay.Place(
      ctx,
      scene.World.World,
      scene.Player.Player,
      scene.Part("nes"),
      Cell,
      BlockFacing.UP,
      ScenarioPlayer.Hit(BlockFacing.UP),
      new AssemblyPick(
        Block: token == "" ? null : $"test:pipe-{token}",
        Steps: steps
      )
    );

  // Fails when the arms filter is dropped, so the first candidate wins whatever faces it couples on.
  [Fact]
  public void Choose_takes_the_candidate_whose_arms_are_named() {
    var ns = Candidate("t:ns", 1, null, BlockFacing.NORTH, BlockFacing.SOUTH);
    var we = Candidate("t:we", 2, null, BlockFacing.EAST, BlockFacing.WEST);

    Assert.Same(we, new AssemblyPick(Arms: "ew").Choose([ns, we]));
    Assert.Null(new AssemblyPick(Arms: "ud").Choose([ns, we]));
  }

  // Fails when the turn filter is dropped, so a fitting's first turn is taken whatever the pick names.
  [Fact]
  public void Choose_takes_the_candidate_whose_turn_is_named() {
    var first = Candidate("t:a", 1, "n");
    var second = Candidate("t:b", 2, "e");

    Assert.Same(second, new AssemblyPick(Turn: "e").Choose([first, second]));
  }

  // Fails when the right-clicks do not step on, or do not wrap to the first after the last.
  [Fact]
  public void Choose_steps_through_the_candidates_and_wraps_after_the_last() {
    AssemblyCandidate[] list =
    [
      Candidate("t:a", 1),
      Candidate("t:b", 2),
      Candidate("t:c", 3),
    ];

    Assert.Same(list[1], new AssemblyPick(Steps: 1).Choose(list));
    Assert.Same(list[0], new AssemblyPick(Steps: 3).Choose(list));
  }

  // Fails when assemble ignores its steps: the first candidate goes down, not the one stepped to.
  [Fact]
  public void Assemble_steps_then_fixes() {
    var scene = Scene();
    var lines = new List<string>();
    IReadOnlyList<AssemblyCandidate> offered = scene.Offer(
      Ground,
      BlockFacing.UP
    );
    Assert.True(offered.Count > 1);

    Assert.Null(Place(scene, Context(scene, lines), "", steps: 1));

    Assert.Equal(offered[1].Block.Code, scene.World.GetBlock(Cell).Code);
    Assert.True(scene.RecordAt(Cell).Fixed);
  }

  // Fails when a placement calls the block's own TryPlaceBlock: the gate refuses it for want of the
  // tool, nothing is fixed and no fastener is spent.
  [Fact]
  public void Place_fixes_through_the_handler_and_spends_the_fasteners() {
    var scene = Scene();
    var lines = new List<string>();

    AssemblyResult? refused = Place(scene, Context(scene, lines), "nes");

    Assert.Null(refused);
    BEBehaviorAssembled record = scene.RecordAt(Cell);
    Assert.True(record.Fixed);
    Assert.Equal(2, record.FastenerAt(BlockFacing.NORTH)!.StackSize);
    Assert.Equal(0, scene.Carried);
    Assert.Null(scene.Player.ActiveSlot.Itemstack);
    Assert.Null(scene.Player.OffHandSlot.Itemstack);
  }

  // Fails when a refusal is turned into a placement or dropped: the server's code comes back.
  [Fact]
  public void A_refused_fix_returns_the_servers_code() {
    var scene = Scene();
    scene.Player.GameMode = EnumGameMode.Survival;
    scene.World.UnloadChunkAt(Cell.AddCopy(BlockFacing.WEST));

    AssemblyResult? refused = Place(scene, Context(scene, []), "nes");

    Assert.Equal(HandAssemblyServer.Unloaded, refused!.Code);
    Assert.Equal(0, scene.World.GetBlock(Cell).Id);
  }

  // Fails when the grant line is not printed outside scope all, or is printed again for a second
  // grant of the same fastener.
  [Fact]
  public void Grant_printed_outside_scope_all() {
    var scene = Scene();
    var lines = new List<string>();
    ScenarioContext ctx = Context(scene, lines);

    Assert.Null(Place(scene, ctx, "nes"));
    scene.World.World.BlockAccessor.SetBlock(0, Cell);
    Assert.Null(Place(scene, ctx, "nes"));

    Assert.Equal(
      ["granted 4 test:bolt for hand assembly"],
      lines.Where(l => l.StartsWith("granted"))
    );
  }

  // Fails when the fasteners are granted under scope all instead of drawn from the pack, which then
  // keeps them: the line prints and the pack still holds all four.
  [Fact]
  public void Under_scope_all_the_fasteners_are_drawn_from_the_pack() {
    var scene = Scene();
    var lines = new List<string>();
    ScenarioContext ctx = Context(scene, lines, Pack);

    Assert.Null(Place(scene, ctx, "nes"));

    Assert.Empty(lines.Where(l => l.StartsWith("granted")));
    Assert.Equal(0, ctx.Pack!.Held("test:bolt"));
    Assert.Equal(1, ctx.Pack.Held("test:wrench-iron"));
  }

  // Fails when the take-off leaves what it returned with the player: the pack holds neither the
  // piece nor its fasteners afterwards.
  [Fact]
  public void TakeOff_returns_to_pack() {
    var scene = Scene();
    ScenarioContext ctx = Context(scene, [], Pack);
    Assert.Null(Place(scene, ctx, "nes"));

    AssemblyResult? refused = HandAssemblyPlay.TakeOff(
      ctx,
      scene.World.World,
      scene.Player.Player,
      Cell
    );

    Assert.Null(refused);
    Assert.Equal(0, scene.World.GetBlock(Cell).Id);
    Assert.Equal(4, ctx.Pack!.Held("test:bolt"));
    Assert.Equal(1, ctx.Pack.Held("test:pipe-nes"));
    Assert.Equal(1, ctx.Pack.Held("test:wrench-iron"));
    Assert.Equal(0, scene.Carried);
  }

  // Fails when the route sends an opted-in block to the plain click: it is placed outside the handler.
  [Fact]
  public void Run_opted_in_goes_through_handler() {
    var scene = Scene();
    var routed = new List<string>();
    RunStep.PlayerPlace plain = (
      Block b,
      BlockPos at,
      BlockFacing f,
      out string? failure
    ) => {
      routed.Add("plain " + b.Code);
      failure = null;
      return true;
    };
    RunStep.PlayerPlace assembled = (
      Block b,
      BlockPos at,
      BlockFacing f,
      out string? failure
    ) => {
      routed.Add("assembled " + b.Code);
      failure = null;
      return true;
    };
    RunStep.PlayerPlace place = RunStep.Routed(plain, assembled);

    place(scene.Part("nes"), Cell, BlockFacing.UP, out _);
    place(
      TestBlocks.Configure(new Block(), "test:ordinary", 900),
      Cell,
      BlockFacing.UP,
      out _
    );

    Assert.Equal(["assembled test:pipe-nes", "plain test:ordinary"], routed);
  }

  // Fails when the placement law places an opted-in part with the plain click: the handler is not
  // reached and the cell holds no fixed piece.
  [Fact]
  public void PlacementLaw_places_an_opted_in_part_by_hand() {
    var scene = Scene();
    Block part = scene.Part("nes");
    HandAssemblySpec spec = HandAssemblySpec.Of(part)!;

    bool went = PlacementLaw.PlaceByHand(
      scene.World,
      scene.Player,
      part,
      spec,
      new BlockSelection {
        Position = Ground.Copy(),
        Face = BlockFacing.UP,
        HitPosition = new Vec3d(0.5, 0.5, 0.5),
      },
      out string failure
    );

    Assert.True(went, failure);
    Assert.NotNull(scene.World.World.BlockAccessor.GetBlockEntity(Cell));
    Assert.NotEqual(0, scene.World.GetBlock(Cell).Id);
  }
}
