using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Assembling;
using ExpandedLib.Blocks;
using ExpandedLib.Networks;
using ExpandedLib.Testing;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>The placements a held part offers at an aim: which are offered, in what order, and how many joints each makes.</summary>
public class HandAssemblyCandidatesTests {
  private static readonly BlockPos Cell = new(64, 16, 64, 0);
  private static readonly BlockPos Ground = new(64, 15, 64, 0);

  private static string[] Tokens(IEnumerable<AssemblyCandidate> candidates) =>
    [.. candidates.Select(c => c.Token)];

  #region Order

  // Fails when the order's first rule, the straight on the anchor's axis, is dropped.
  [Fact]
  public void The_straight_on_the_anchors_axis_comes_first_and_every_arm_set_has_an_arm_on_it() {
    var scene = Scene.Standing(["ne", "nes", "ns", "we", "ud"]);
    scene.Pipe(Cell.AddCopy(BlockFacing.NORTH), "ns");

    var offered = scene.Offer(
      Cell.AddCopy(BlockFacing.NORTH),
      BlockFacing.SOUTH
    );

    Assert.Equal(["ns", "ne", "nes"], Tokens(offered));
  }

  // Fails when the straight along the look is no longer ranked ahead of the declared order.
  [Fact]
  public void A_support_face_offers_the_straight_along_the_look_then_across_then_vertical() {
    var scene = Scene.Standing(["ud", "ns", "we"]);
    scene.Player.Looking(BlockFacing.EAST);

    var offered = scene.Offer(Ground, BlockFacing.UP);

    Assert.Equal(["we", "ns", "ud"], Tokens(offered));
  }

  // Fails when across and vertical are ranked alike, leaving the declared order to decide.
  [Fact]
  public void Across_the_look_comes_before_vertical() {
    var scene = Scene.Standing(["ud", "ns"]);
    scene.Player.Looking(BlockFacing.EAST);

    var offered = scene.Offer(Ground, BlockFacing.UP);

    Assert.Equal(["ns", "ud"], Tokens(offered));
  }

  // Fails when joints no longer rank ahead of the declared order.
  [Fact]
  public void More_joints_come_before_fewer() {
    var scene = Scene.Standing(["nw", "nes"]);
    scene.Pipe(Cell.AddCopy(BlockFacing.NORTH), "ns");
    scene.Pipe(Cell.AddCopy(BlockFacing.EAST), "we");

    var offered = scene.Offer(Ground, BlockFacing.UP);

    Assert.Equal(["nes", "nw"], Tokens(offered));
  }

  [Theory]
  [InlineData("east", new[] { "u", "e", "w", "d" })]
  [InlineData("west", new[] { "u", "w", "e", "d" })]
  public void Turns_about_a_horizontal_axis_run_up_toward_the_player_away_down(
    string side,
    string[] expected
  ) {
    var scene = Scene.Standing(["ns"], turns: true);
    scene.Player.StandAt(Cell, BlockFacing.FromCode(side)!);

    var offered = scene.Offer(Ground, BlockFacing.UP);

    Assert.Equal(expected, offered.Select(c => c.Turn));
  }

  // Fails when the vertical-axis turns do not run clockwise from above, beginning toward the player.
  [Fact]
  public void Turns_about_a_vertical_axis_start_toward_the_player_and_run_clockwise() {
    var scene = Scene.Standing(["ud"], turns: true);
    scene.Player.StandAt(Cell, BlockFacing.EAST);

    var offered = scene.Offer(Ground, BlockFacing.UP);

    Assert.Equal(["e", "s", "w", "n"], offered.Select(c => c.Turn));
  }

  // Fails when the facings keep their declared order instead of the placement's facing first.
  [Fact]
  public void An_orientable_block_offers_the_facing_its_placement_would_wear_first() {
    var scene = Scene.Standing(["n", "e", "s", "w"], orientable: true);
    scene.Player.StandAt(Cell, BlockFacing.EAST);

    var offered = scene.Offer(Ground, BlockFacing.UP);

    Assert.Equal("w", offered[0].Token);
    Assert.Equal(["w", "n", "e", "s"], Tokens(offered));
  }

  #endregion

  #region What is offered

  // Fails when a refused crossing rejects the box's one candidate instead of leaving that arm open.
  [Fact]
  public void Box_RefusedCrossingLeavesFaceOpen() {
    var scene = Scene.Standing(["nsweud"], keepsOpen: true);
    scene.Pipe(Cell.AddCopy(BlockFacing.NORTH), "ns", family: "welded");

    var offered = scene.Offer(Ground, BlockFacing.UP);

    AssemblyCandidate box = Assert.Single(offered);
    Assert.Equal(6, box.Arms.Count);
    Assert.Contains(BlockFacing.NORTH, box.OpenArms);
  }

  // Fails when an arm meeting a neighbour with no connector back is still offered.
  [Fact]
  public void No_candidate_has_an_arm_toward_a_neighbour_that_offers_no_connector_back() {
    var scene = Scene.Standing(["ns", "we", "ne", "ud"]);
    scene.Pipe(Cell.AddCopy(BlockFacing.EAST), "ns");

    var offered = scene.Offer(Ground, BlockFacing.UP);

    Assert.Equal(["ns", "ud"], Tokens(offered).OrderBy(t => t));
    Assert.DoesNotContain(offered, c => c.Arms.Contains(BlockFacing.EAST));
  }

  // Fails when the joint family of a crossing is not compared.
  [Fact]
  public void No_candidate_has_an_arm_across_another_joint_family() {
    var scene = Scene.Standing(["ns", "we"]);
    scene.Pipe(Cell.AddCopy(BlockFacing.NORTH), "ns", family: "welded");

    var offered = scene.Offer(Ground, BlockFacing.UP);

    Assert.Equal(["we"], Tokens(offered));
  }

  // Fails when the part's own refinement is not asked.
  [Fact]
  public void A_part_that_does_not_allow_a_candidate_is_not_offered() {
    var scene = Scene.Standing(["ns", "we", "ud"], refusing: "we");

    var offered = scene.Offer(Ground, BlockFacing.UP);

    Assert.DoesNotContain("we", Tokens(offered));
    Assert.Equal(2, offered.Count);
  }

  [Fact]
  public void A_replaceable_aimed_cell_is_the_target_cell_and_a_solid_one_offsets_by_the_face() {
    var scene = Scene.Standing(["ns"]);
    var grass = TestBlocks.Configure(
      new Block { Replaceable = 7000 },
      "game:grass",
      80
    );
    scene.World.Place(Ground, grass);

    HandAssemblyTarget onGrass = scene.Target(Ground, BlockFacing.UP);
    scene.World.Place(
      Ground,
      TestBlocks.Configure(new Block(), "game:rock", 81)
    );
    HandAssemblyTarget onRock = scene.Target(Ground, BlockFacing.UP);

    Assert.Equal(Ground, onGrass.Cell);
    Assert.Equal(Cell, onRock.Cell);
    Assert.Equal(BlockFacing.DOWN, onRock.Anchor);
    Assert.False(onRock.Joinable);
  }

  [Fact]
  public void An_open_crossing_of_a_compatible_pipe_makes_the_anchor_joinable() {
    var scene = Scene.Standing(["ns"]);
    scene.Pipe(Cell.AddCopy(BlockFacing.NORTH), "ns");

    Assert.True(
      scene.Target(Cell.AddCopy(BlockFacing.NORTH), BlockFacing.SOUTH).Joinable
    );
  }

  #endregion

  #region Joints

  // Fails when every arm counts as a joint, met or not.
  [Fact]
  public void Only_arms_that_meet_a_crossing_count_as_joints() {
    var scene = Scene.Standing(["ns"]);
    scene.Pipe(Cell.AddCopy(BlockFacing.NORTH), "ns");

    int joints = HandAssemblyCandidates.JointsMade(
      scene.World.Accessor,
      Cell,
      scene.Part("ns")
    );

    Assert.Equal(1, joints);
  }

  // Fails when a block that is not a node makes no joint.
  [Fact]
  public void A_block_that_is_not_a_network_node_makes_one_joint() {
    var scene = Scene.Standing(["ns"]);
    var bracket = TestBlocks.Configure(new Block(), "test:bracket", 70);

    Assert.Equal(
      1,
      HandAssemblyCandidates.JointsMade(scene.World.Accessor, Cell, bracket)
    );
  }

  #endregion

  /// <summary>The spec a block declares, read back.</summary>
  [Fact]
  public void The_spec_reads_tool_fastener_and_count_off_the_attribute() {
    Block block = TestBlocks.Configure(new Block(), "test:spec", 71);
    block.Attributes = new JsonObject(
      JToken.Parse(
        """{"handAssembly":{"tool":"game:wrench-*","fastener":"iiex:bolt","perJoint":2}}"""
      )
    );

    Assert.Equal(
      new HandAssemblySpec("game:wrench-*", "iiex:bolt", 2),
      HandAssemblySpec.Of(block)
    );
    Assert.Null(
      HandAssemblySpec.Of(TestBlocks.Configure(new Block(), "test:none", 72))
    );
  }

  private sealed class Scene {
    private readonly Pipes _pipes;

    private Scene(TestWorld world, TestPlayer player, Pipes pipes) {
      World = world;
      Player = player;
      _pipes = pipes;
    }

    public TestWorld World { get; }
    public TestPlayer Player { get; }

    public static Scene Standing(
      string[] tokens,
      bool turns = false,
      bool orientable = false,
      string? refusing = null,
      bool keepsOpen = false
    ) {
      var world = new TestWorld();
      world.Place(Ground, TestBlocks.Configure(new Block(), "game:rock", 5));
      TestPlayer player = world.Player();
      player.StandAt(Cell, BlockFacing.SOUTH);
      return new Scene(
        world,
        player,
        new Pipes(world, tokens, turns, orientable, refusing, keepsOpen)
      );
    }

    public Block Part(string token) => _pipes.Variant(token, null);

    public void Pipe(BlockPos pos, string token, string family = "flanged") =>
      World.Place(pos, _pipes.Neighbour(token, family));

    public HandAssemblyTarget Target(BlockPos aimed, BlockFacing face) =>
      HandAssemblyTarget.Resolve(
        World.Accessor,
        Select(aimed, face),
        _pipes.Held
      );

    public IReadOnlyList<AssemblyCandidate> Offer(
      BlockPos aimed,
      BlockFacing face
    ) =>
      HandAssemblyCandidates.Of(
        World.World,
        Player.Player,
        _pipes.Held,
        Select(aimed, face)
      );

    private static BlockSelection Select(BlockPos aimed, BlockFacing face) =>
      new() {
        Position = aimed.Copy(),
        Face = face,
        HitPosition = new Vec3d(0.5, 0.5, 0.5),
      };
  }

  /// <summary>The variants of one held part, registered in the world, and neighbours of other joint families.</summary>
  private sealed class Pipes {
    private static readonly string[] TurnLetters =
    [
      "u",
      "d",
      "n",
      "e",
      "s",
      "w",
    ];
    private readonly TestWorld _world;
    private readonly string[] _tokens;
    private readonly string? _refusing;
    private readonly bool _keepsOpen;
    private readonly Dictionary<(string, string?), Block> _variants = [];
    private int _nextId = 100;

    public Pipes(
      TestWorld world,
      string[] tokens,
      bool turns,
      bool orientable,
      string? refusing,
      bool keepsOpen
    ) {
      _world = world;
      _tokens = tokens;
      _refusing = refusing;
      _keepsOpen = keepsOpen;

      foreach (string token in tokens)
        foreach (string? turn in turns ? TurnLetters : [null]) {
          Block block = orientable ? Chair(token) : Node(token, turn, "flanged");
          world.Register(block);
          _variants[(token, turn)] = block;
        }

      Held = _variants[(tokens[0], turns ? "u" : null)];
      if (orientable) {
        var behaviour = new BlockBehaviorExOrientable(Held);
        behaviour.Initialize(
          new JsonObject(JToken.Parse("""{"mode":"horizontal"}"""))
        );
        Held.BlockBehaviors = [behaviour];
        Held.CollectibleBehaviors = [behaviour];
        foreach (Block other in _variants.Values.Where(b => b != Held)) {
          other.BlockBehaviors = Held.BlockBehaviors;
          other.CollectibleBehaviors = Held.CollectibleBehaviors;
        }
      }
    }

    public Block Held { get; }

    public Block Variant(string token, string? turn) =>
      _variants[(token, turn)];

    public Block Neighbour(string token, string family) =>
      Node(token, null, family, $"test:neighbour-{family}-{token}");

    private Block Chair(string token) =>
      TestBlocks.Configure(
        new Block(),
        $"test:chair-{token}",
        _nextId++,
        ("side", token)
      );

    private Block Node(
      string token,
      string? turn,
      string family,
      string? code = null
    ) {
      (string, string)[] variants =
        turn == null
          ? [("orientation", token)]
          : [("orientation", token), ("turn", turn)];
      return TestBlocks.Configure(
        new PipeNode(token, family, _tokens, _refusing, _keepsOpen),
        code ?? $"test:pipe-{token}" + (turn == null ? "" : $"-{turn}"),
        _nextId++,
        variants
      );
    }
  }

  private sealed class PipeNode : BlockNetworkNode, IHandAssemblyPart {
    private readonly string _family;
    private readonly string? _refusing;
    private readonly bool _keepsOpen;

    public PipeNode(
      string token,
      string family,
      string[] tokens,
      string? refusing,
      bool keepsOpen
    ) {
      Type = "mixed";
      Orientation = token;
      _family = family;
      _refusing = refusing;
      _keepsOpen = keepsOpen;
      AllowedOrientations = new() { ["mixed"] = tokens };
    }

    public override string NetworkType => "test";

    internal override bool KeepsRefusedArmsOpen => _keepsOpen;

    public override Dictionary<string, string[]> AllowedOrientations { get; }

    public override bool AcceptsNeighbour(Block neighbour) =>
      neighbour is not PipeNode other || other._family == _family;

    public bool Allows(
      IWorldAccessor world,
      BlockPos pos,
      Block candidate,
      out string? failureCode
    ) {
      failureCode = null;
      if (candidate is PipeNode node && node.Orientation == _refusing) {
        failureCode = "refused";
        return false;
      }
      return true;
    }
  }
}

internal static class AssemblyPlayerExtensions {
  /// <summary>Stands the player three cells from <paramref name="cell"/> on its <paramref name="side"/>.</summary>
  public static void StandAt(
    this TestPlayer player,
    BlockPos cell,
    BlockFacing side
  ) =>
    player.Entity.Pos.SetPos(
      cell.X + 0.5 + (side.Normali.X * 3),
      cell.Y + 0.5,
      cell.Z + 0.5 + (side.Normali.Z * 3)
    );

  /// <summary>Turns the player to look along <paramref name="facing"/>.</summary>
  public static void Looking(this TestPlayer player, BlockFacing facing) =>
    player.Entity.Pos.Yaw = new[]
    {
      0f,
      GameMath.PIHALF,
      GameMath.PI,
      GameMath.PI + GameMath.PIHALF,
    }.First(yaw => BlockFacing.HorizontalFromYaw(yaw) == facing);
}
