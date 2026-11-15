using System.Linq;
using ExpandedLib.Assembling;
using ExpandedLib.Definitions;
using ExpandedLib.Industry.Pipes;
using ExpandedLib.Testing;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>The junction box: one variant with a connector on all six faces, one candidate and an arm box on every face.</summary>
public class PipeJunctionBoxTests {
  #region Fixtures

  private static ExBlockDef Box => BlockPipe.Junction("test", "cast");

  private static string[] Tokens =>
    [.. Box.VariantGroups.Single(g => g.Name == "orientation").States];

  private static BlockPipe Piece(string type, string token, int id) {
    var block = TestBlocks.Configure(
      new BlockPipe(),
      $"test:pipe-cast-{type}-{token}",
      id,
      ("type", type),
      ("tier", "cast"),
      ("orientation", token)
    );
    block.SetNetworkTypeForTest(type);
    block.ApplyOrientationForTest(token);
    return block;
  }

  #endregion

  // Fails when the box's token leaves a face out, so it joins on fewer than six.
  [Fact]
  public void Box_SixConnectors() {
    BlockPipe block = Piece("junction", Assert.Single(Tokens), 400);

    Assert.Equal(6, block.GetConnectorFaces()!.Length);
    foreach (BlockFacing face in BlockFacing.ALLFACES)
      Assert.True(block.HasConnectorAt(face), face.Code);
  }

  // Fails when the box offers several tokens, as the cross does, or does not keep a refused arm open.
  [Fact]
  public void Box_OneCandidateAllArms() {
    string token = Assert.Single(Tokens);

    Assert.Equal(6, token.Distinct().Count());
    Assert.True(Piece("junction", token, 401).KeepsRefusedArmsOpen);
    Assert.False(Piece("xjunction", "nswe", 402).KeepsRefusedArmsOpen);
  }

  // Fails when hand assembly takes the box's tokens from the first type's list, so a configured box is never offered.
  [Fact]
  public void Box_IsOfferedByHandAssembly() {
    var w = new TestWorld();
    var cell = new BlockPos(64, 16, 64, 0);
    var ground = new BlockPos(64, 15, 64, 0);
    w.Place(ground, TestBlocks.Configure(new Block(), "game:rock", 5));
    string token = Assert.Single(Tokens);
    var box = TestBlocks.Configure(
      new BlockPipe(),
      $"test:pipe-cast-junction-{token}",
      403,
      ("tier", "cast"),
      ("type", "junction"),
      ("orientation", token)
    );
    box.SetNetworkTypeForTest("junction");
    box.ApplyOrientationForTest(token);
    w.Register(box);
    TestPlayer player = w.Player();
    player.StandAt(cell, BlockFacing.SOUTH);

    var offered = HandAssemblyCandidates.Of(
      w.World,
      player.Player,
      box,
      new BlockSelection {
        Position = ground.Copy(),
        Face = BlockFacing.UP,
        HitPosition = new Vec3d(0.5, 0.5, 0.5),
      }
    );

    Assert.Equal(Tokens, offered.Select(c => c.Token));
  }

  // Fails when arm boxes are declared only for some faces: each face of the cell needs a box that reaches it.
  [Fact]
  public void Box_ArmBoxesOnAllSixFaces() {
    JArray collision = (JArray)Box.ToJson()["collisionboxes"]!;
    JArray selection = (JArray)Box.ToJson()["selectionboxes"]!;

    foreach (JArray boxes in new[] { collision, selection }) {
      Assert.Equal(7, boxes.Count);
      foreach (BlockFacing face in BlockFacing.ALLFACES)
        Assert.Contains(boxes, b => Reaches((JObject)b, face));
    }
  }

  private static bool Reaches(JObject box, BlockFacing face) {
    string axis = face.Axis switch {
      EnumAxis.X => "x",
      EnumAxis.Y => "y",
      _ => "z",
    };
    int sign = face.Normali.X + face.Normali.Y + face.Normali.Z;
    return sign > 0
      ? (double)box[axis + "2"]! >= 1
      : (double)box[axis + "1"]! <= 0;
  }
}
