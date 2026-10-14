#if GAME_GE_1_22
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Blocks;
using ExpandedLib.Networks;
using ExpandedLib.Structures;
using ExpandedLib.Testing;
using Newtonsoft.Json.Linq;
using NSubstitute;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>The outline of a hit control covers every box with its id across the principal and its linked
/// fillers, the body covers every id-less box, and a structure without a table is outlined as vanilla does.</summary>
public class ControlOutlineTests
{
  private static readonly BlockPos PrincipalPos = new(10, 5, 10);

  private const string Controls =
    """[{ "id": "valve", "kind": "valve" }, { "id": "hatch", "kind": "door" }]""";

  private const string Valve =
    """{ "x1": 0.1, "y1": 0.5, "z1": 0.1, "x2": 0.4, "y2": 0.9, "z2": 0.4 }""";
  private const string Body =
    """{ "x1": 0, "y1": 0, "z1": 0, "x2": 1, "y2": 0.5, "z2": 1 }""";

  private static readonly string Cell = $$"""
    "collisionBoxes": [{{Body}}],
    "selectionBoxes": [{{Body}}, {{Valve}}],
    "controls": [{ "id": "valve", "box": {{Valve}} }]
    """;

  private static readonly string Offsets = $$"""
    [
      { "x": 1, "y": 0, "z": 0, {{Cell}} },
      { "x": 2, "y": 0, "z": 0, {{Cell}} },
      { "x": 3, "y": 0, "z": 0, {{Cell}} },
      {
        "x": 0, "y": 1, "z": 0,
        "collisionBoxes": [{{Body}}],
        "doors": [{
          "control": "hatch",
          "shut": [{ "x1": 0.75, "y1": 0.5, "z1": 0, "x2": 1, "y2": 1, "z2": 1 }],
          "open": [{ "x1": 0, "y1": 0.5, "z1": 0.75, "x2": 1, "y2": 1, "z2": 1 }],
          "opening": [],
          "openCollides": false
        }]
      }
    ]
    """;

  #region Boxes

  // Fails when only the hit cell's boxes are collected.
  [Fact]
  public void A_control_hit_draws_its_boxes_in_every_cell()
  {
    var scene = new Scene();

    Cuboidf[] boxes = scene.Boxes(new BlockPos(11, 5, 10), "valve")!;

    Assert.Equal(
      [0.1f - 1f, 0.1f, 0.1f + 1f, 0.1f + 2f],
      boxes.Select(b => b.X1).OrderBy(x => x)
    );
    Assert.All(boxes, b => Assert.Equal(0.5f, b.Y1, 4));
  }

  // Fails when only the hit cell's boxes are collected.
  [Fact]
  public void A_body_hit_draws_every_body_box()
  {
    var scene = new Scene();

    Cuboidf[] boxes = scene.Boxes(PrincipalPos, null)!;

    Assert.Equal([0f, 0f, 1f, 2f, 3f], boxes.Select(b => b.X1).OrderBy(x => x));
    Assert.Contains(boxes, b => b.Y2 > 1f);
  }

  // Fails when the filler's principal is not compared with the structure's.
  [Fact]
  public void A_cell_not_linked_is_skipped()
  {
    var scene = new Scene();
    scene.Relink(new BlockPos(13, 5, 10), new BlockPos(20, 5, 20));

    Cuboidf[] boxes = scene.Boxes(PrincipalPos, null)!;

    Assert.Equal(4, boxes.Length);
    Assert.DoesNotContain(boxes, b => b.X1 > 2.5f);
  }

  // Fails when the principal's states are not passed to the box composition.
  [Fact]
  public void An_open_door_draws_its_open_leaf()
  {
    var scene = new Scene();
    scene.Principal.States["hatch"] = ControlState.Open;

    Cuboidf box = Assert.Single(scene.Boxes(PrincipalPos, "hatch")!);

    Assert.Equal(0.75f, box.Z1, 4);
    Assert.Equal(1f, box.Z2, 4);
    Assert.Equal(0f, box.X1, 4);
    Assert.Equal(1f, box.X2, 4);
  }

  // Fails when a block without a table gets the whole-structure boxes.
  [Fact]
  public void No_table_draws_like_vanilla()
  {
    var scene = new Scene(withTable: false);
    var drawn = new List<Cuboidf>();

    Assert.Null(scene.Boxes(new BlockPos(11, 5, 10), null));
    ControlOutline.Render(
      scene.Filler,
      scene.Api,
      new BlockSelection { Position = new BlockPos(11, 5, 10) },
      (box, width, color) => drawn.Add(box)
    );

    Cuboidf[] own = scene.Filler.GetSelectionBoxes(
      scene.Accessor,
      new BlockPos(11, 5, 10)
    );
    Assert.Equal(own.Length, drawn.Count);
    Assert.Equal(own[0].X2, drawn[0].X2, 4);
  }

  // Fails when Render draws the hit cell instead of the boxes it was given.
  [Fact]
  public void Render_draws_every_cell_of_the_control()
  {
    var scene = new Scene();
    var drawn = new List<Cuboidf>();

    ControlOutline.Render(
      scene.Filler,
      scene.Api,
      new BlockSelection
      {
        Position = new BlockPos(11, 5, 10),
        SelectionBoxId = "valve",
      },
      (box, width, color) => drawn.Add(box)
    );

    Assert.Equal(4, drawn.Count);
  }

  #endregion

  #region Interface

  // Fails when the filler drops ICustomSelectionBoxRender.
  [Fact]
  public void The_filler_always_answers_the_renderer()
  {
    foreach (bool withTable in new[] { true, false })
    {
      var scene = new Scene(withTable);

      Assert.NotNull(
        scene.Filler.GetInterface<ICustomSelectionBoxRender>(
          Substitute.For<IWorldAccessor>(),
          null!
        )
      );
    }
  }

  // Fails when a block without a table answers the renderer.
  [Fact]
  public void A_node_and_a_megastructure_answer_with_a_table_and_null_without()
  {
    var world = Substitute.For<IWorldAccessor>();
    foreach (bool withTable in new[] { true, false })
    {
      Block mega = TestBlocks.Configure(new MegaBlock(), "test:mega", 50);
      Block node = TestBlocks.Configure(new PlainNode(), "test:node", 61);
      foreach (Block block in new[] { mega, node })
      {
        block.Attributes = new JsonObject(
          withTable
            ? new JObject { ["controls"] = JArray.Parse(Controls) }
            : new JObject()
        );
        Assert.Equal(
          withTable,
          block.GetInterface<ICustomSelectionBoxRender>(world, PrincipalPos)
            != null
        );
      }
    }
  }

  #endregion

  #region Fixture

  private sealed class Scene
  {
    public readonly MegaBlock Host;
    public readonly BlockStructureFiller Filler;
    public readonly StatesEntity Principal = new() { Pos = PrincipalPos };
    public readonly IBlockAccessor Accessor = Substitute.For<IBlockAccessor>();
    public readonly ICoreClientAPI Api = Substitute.For<ICoreClientAPI>();

    public Scene(bool withTable = true)
    {
      Host = TestBlocks.Configure(
        new MegaBlock(),
        "test:mega",
        50,
        ("side", "north")
      );
      var attributes = new JObject
      {
        ["fillerOffsets"] = JArray.Parse(Offsets),
        ["principalCell"] = JObject.Parse("{" + Cell + "}"),
      };
      if (withTable)
        attributes["controls"] = JArray.Parse(Controls);
      Host.Attributes = new JsonObject(attributes);
      Host.CollisionBoxes = [new Cuboidf(0f, 0f, 0f, 1f, 1f, 1f)];
      Host.SelectionBoxes = [new Cuboidf(0f, 0f, 0f, 1f, 1f, 1f)];
      Filler = TestBlocks.Configure(
        new BlockStructureFiller(),
        "exlib:structurefiller",
        70
      );
      Filler.CollisionBoxes = [new Cuboidf(0f, 0f, 0f, 1f, 1f, 1f)];
      Filler.SelectionBoxes = [new Cuboidf(0f, 0f, 0f, 1f, 1f, 1f)];
      Principal.Block = Host;
      Accessor.GetBlock(PrincipalPos).Returns(Host);
      Accessor.GetBlockEntity(PrincipalPos).Returns(Principal);
      for (int x = 1; x <= 3; x++)
        Relink(PrincipalPos.AddCopy(x, 0, 0), PrincipalPos);
      Relink(PrincipalPos.AddCopy(0, 1, 0), PrincipalPos);
      Api.Side.Returns(EnumAppSide.Client);
      var world = Substitute.For<IClientWorldAccessor>();
      world.BlockAccessor.Returns(Accessor);
      Api.World.Returns(world);
      ((ICoreAPI)Api).World.Returns(world);
    }

    public void Relink(BlockPos cell, BlockPos principal)
    {
      Accessor.GetBlock(cell).Returns(Filler);
      Accessor
        .GetBlockEntity(cell)
        .Returns(
          new BlockEntityStructureFiller
          {
            Pos = cell,
            Block = Filler,
            Principal = principal.Copy(),
            CollisionBoxes = [new Cuboidf(0f, 0f, 0f, 1f, 1f, 1f)],
          }
        );
    }

    public Cuboidf[]? Boxes(BlockPos hit, string? id) =>
      ControlOutline.Boxes(Accessor, hit, id);
  }

  private sealed class MegaBlock : BlockFilledMegastructure { }

  private sealed class PlainNode : BlockNetworkNode
  {
    public override string NetworkType => "test";

    public override Dictionary<string, string[]> AllowedOrientations { get; } =
      new() { { "straight", ["ns"] } };

    protected override string GetFallbackOrientation(string? type) => "ns";
  }

  private sealed class StatesEntity : BlockEntity, IControlStates
  {
    public readonly Dictionary<string, ControlState> States = [];

    public ControlState? StateOf(string controlId) =>
      States.TryGetValue(controlId, out ControlState state) ? state : null;
  }

  #endregion
}
#endif
