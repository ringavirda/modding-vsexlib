using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Assembling;
using ExpandedLib.Definitions;
using ExpandedLib.Helpers;
using ExpandedLib.Industry.Pipes;
using ExpandedLib.Networks;
using ExpandedLib.Testing;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>The corner segment: eight tokens of three perpendicular arms, one drawing turned per token, and the six-form set that carries it.</summary>
public class PipeCornerTests {
  #region Fixtures

  private static ExBlockDef Corner => BlockPipe.Corner("test", "cast");

  private static string[] Tokens =>
    [.. Corner.VariantGroups.Single(g => g.Name == "orientation").States];

  private static BlockPipe Piece(string type, string token, int id) {
    var block = TestBlocks.Configure(
      new BlockPipe(),
      $"test:pipe-cast-{type}-{token}",
      id,
      ("tier", "cast"),
      ("type", type),
      ("orientation", token)
    );
    block.SetNetworkTypeForTest(type);
    block.ApplyOrientationForTest(token);
    return block;
  }

  // The drawing at unw has its arms north, west and up.
  private static readonly string[] DrawnArms = ["n", "w", "u"];

  private static string TurnY(string arm) =>
    arm switch {
      "n" => "w",
      "w" => "s",
      "s" => "e",
      "e" => "n",
      _ => arm,
    };

  private static string TurnZ(string arm) =>
    arm switch {
      "w" => "d",
      "d" => "e",
      "e" => "u",
      "u" => "w",
      _ => arm,
    };

  // Z turns apply before Y; each step is a quarter turn.
  private static string[] ArmsAfter(JObject shape) {
    int y = (int?)shape["rotateY"] ?? 0;
    int z = (int?)shape["rotateZ"] ?? 0;
    Assert.Null(shape["rotateX"]);
    var arms = new List<string>();
    foreach (string drawn in DrawnArms) {
      string arm = drawn;
      for (int i = 0; i < z / 90; i++)
        arm = TurnZ(arm);
      for (int i = 0; i < y / 90; i++)
        arm = TurnY(arm);
      arms.Add(arm);
    }
    return [.. arms.OrderBy(a => a)];
  }

  private static bool Reaches(Cuboidf box, BlockFacing face) =>
    face.Axis switch {
      EnumAxis.X => face.Normali.X > 0 ? box.X2 >= 0.9999f : box.X1 <= 0.0001f,
      EnumAxis.Y => face.Normali.Y > 0 ? box.Y2 >= 0.9999f : box.Y1 <= 0.0001f,
      _ => face.Normali.Z > 0 ? box.Z2 >= 0.9999f : box.Z1 <= 0.0001f,
    };

  // Turns the declared boxes the way the game does at load, without a loaded world.
  private sealed class BoxProbe : BlockPipe {
    private readonly string[] _tokens;

    public BoxProbe(string[] tokens) => _tokens = tokens;

    public override Dictionary<string, string[]> AllowedOrientations =>
      new() { ["corner"] = _tokens };

    public Cuboidf[] Rotated(Cuboidf[] declared, string token) {
      CollisionBoxes = declared;
      SelectionBoxes = declared;
      PrecomputeRotatedBoxes();
      return collisionBoxesCache[$"corner-{token}"];
    }
  }

  #endregion

  // Fails when a token repeats a letter or leaves a connector out, when dws is registered as dw, or when a face is answered against its token.
  [Fact]
  public void Corner_EightTokensThreeConnectors() {
    Assert.Equal(8, Tokens.Length);
    Assert.Equal(8, Tokens.Distinct().Count());
    foreach (string token in Tokens) {
      BlockPipe block = Piece("corner", token, 410);

      Assert.Equal(3, token.Distinct().Count());
      Assert.Equal(3, block.GetConnectorFaces()!.Length);
      foreach (BlockFacing face in BlockFacing.ALLFACES)
        Assert.Equal(token.Contains(face.Code[0]), block.HasConnectorAt(face));
      Assert.Equal(1, token.Count(c => c is 'u' or 'd'));
      Assert.Equal(1, token.Count(c => c is 'n' or 's'));
      Assert.Equal(1, token.Count(c => c is 'e' or 'w'));
    }
    Assert.Equal(Tokens, ExOrientations.PipeCorner.Tokens.ToArray());
  }

  // Fails when a token turns the drawing to arms other than its letters (dse given use's rotation).
  [Fact]
  public void Corner_ShapeArmsMatchToken() {
    JObject byType = (JObject)Corner.ToJson()["shapebytype"]!;

    foreach (string token in Tokens) {
      JObject shape = (JObject)byType[$"*-corner-{token}"]!;

      Assert.Equal("test:" + "pipe/cast/corner", (string?)shape["base"]);
      Assert.Equal(
        [.. token.Select(c => c.ToString()).OrderBy(a => a)],
        ArmsAfter(shape)
      );
    }
    Assert.Equal(Tokens.Length, byType.Count);
  }

  // Fails when the boxes are not turned with the token: every token given unw's boxes.
  [Fact]
  public void Corner_ArmBoxesFollowToken() {
    JArray json = (JArray)Corner.ToJson()["collisionboxes"]!;
    Cuboidf[] declared =
    [
      .. json.Select(b => new Cuboidf(
        (float)b["x1"]!,
        (float)b["y1"]!,
        (float)b["z1"]!,
        (float)b["x2"]!,
        (float)b["y2"]!,
        (float)b["z2"]!
      )),
    ];
    Assert.Equal(4, declared.Length);

    foreach (string token in Tokens) {
      Cuboidf[] boxes = new BoxProbe(Tokens).Rotated(declared, token);
      foreach (BlockFacing face in BlockFacing.ALLFACES)
        Assert.Equal(
          token.Contains(face.Code[0]),
          boxes.Any(b => Reaches(b, face))
        );
    }
  }

  // Fails when the scheme is registered without its d half, leaving four candidates.
  [Fact]
  public void Corner_CandidatesEight() {
    var w = new TestWorld();
    var cell = new BlockPos(64, 16, 64, 0);
    var ground = new BlockPos(64, 15, 64, 0);
    w.Place(ground, TestBlocks.Configure(new Block(), "game:rock", 5));
    BlockPipe held = Piece("corner", "unw", 411);
    foreach ((string token, int i) in Tokens.Select((t, i) => (t, i)))
      w.Register(Piece("corner", token, 420 + i));
    TestPlayer player = w.Player();
    player.StandAt(cell, BlockFacing.SOUTH);

    var offered = HandAssemblyCandidates.Of(
      w.World,
      player.Player,
      held,
      new BlockSelection {
        Position = ground.Copy(),
        Face = BlockFacing.UP,
        HitPosition = new Vec3d(0.5, 0.5, 0.5),
      }
    );

    Assert.Equal(8, offered.Count);
    Assert.Equal(Tokens.Order(), offered.Select(c => c.Token).Order());
  }

  // Fails when the flow node drops an arm: a corner at dse joins its neighbours on east, south and down.
  [Fact]
  public void Corner_FlowCarriesThreeArms() {
    var w = new TestWorld();
    w.RegisterNetwork("pipeflow", sys => new PipeFlowNetwork(sys));
    var at = new BlockPos(10, 10, 10, 0);
    BlockPipe corner = Piece("corner", "dse", 412);
    corner.SetNetworkTypeForTest("pipeflow");
    TestMemberBlockEntity be = TestMemberBlockEntity.Carrying("pipeflow");
    w.Place(at, corner, be);
    w.Initialize(be);
    w.PlaceNode(at.AddCopy(BlockFacing.EAST), "pipeflow", "we");
    w.PlaceNode(at.AddCopy(BlockFacing.SOUTH), "pipeflow", "ns");
    w.PlaceNode(at.AddCopy(BlockFacing.DOWN), "pipeflow", "ud");
    w.PlaceNode(at.AddCopy(BlockFacing.NORTH), "pipeflow", "ns");
    w.PlaceNode(at.AddCopy(BlockFacing.UP), "pipeflow", "ud");

    var joined = w
      .Networks.GetConnectedNeighbors(w.Accessor, at, "pipeflow")
      .ToList();

    Assert.Equal(3, joined.Count);
    foreach (
      BlockFacing face in new[]
      {
        BlockFacing.EAST,
        BlockFacing.SOUTH,
        BlockFacing.DOWN,
      }
    )
      Assert.Contains(at.AddCopy(face), joined);
    var run = w.NetworkAt(at)!;
    Assert.Same(run, w.NetworkAt(at.AddCopy(BlockFacing.DOWN)));
    Assert.NotSame(run, w.NetworkAt(at.AddCopy(BlockFacing.NORTH)));
  }

  // Fails when the six-form set leaves the corner out or reorders the forms.
  [Fact]
  public void SetSegments_SixFormsInOrder() {
    var forms = BlockPipe.SetSegments("test", "cast").ToList();

    Assert.Equal(
      ["straight", "bend", "tjunction", "corner", "xjunction", "junction"],
      forms.Select(f =>
        f.VariantGroups.Single(g => g.Name == "type").States.Single()
      )
    );
  }

  // Fails when every segment blocktype lists all six segment selectors instead of its own.
  [Theory]
  [InlineData("cast")]
  [InlineData(null)]
  public void SetSegments_HandbookGroupsOnlyOwnSegment(string? tier) {
    foreach (ExBlockDef def in BlockPipe.SetSegments("test", tier)) {
      string type = def
        .VariantGroups.Single(g => g.Name == "type")
        .States.Single();
      string[] groups =
      [
        .. def.ToJson()["attributes"]!["handbook"]!["groupBy"]!.Select(t =>
          (string)t!
        ),
      ];

      Assert.Equal(
        [tier == null ? $"pipe-{type}-*" : $"pipe-{tier}-{type}-*"],
        groups
      );
    }
  }

  // Fails when Definitions stays at the four segments and the box: the corner's tokens are then never allowed.
  [Fact]
  public void Definitions_AllowsCornerAndBox() {
    BlockPipe block = Piece("corner", "unw", 413);

    Dictionary<string, string[]> allowed = block.AllowedOrientations;

    Assert.Equal(Tokens, allowed["corner"]);
    Assert.Equal(["nsweud"], allowed["junction"]);
  }
}
