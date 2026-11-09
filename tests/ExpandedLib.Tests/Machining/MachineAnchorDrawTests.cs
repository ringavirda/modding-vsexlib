#if GAME_GE_1_22
using System;
using System.IO;
using System.Linq;
using System.Numerics;
using ExpandedLib.Industry.Machining;
using ExpandedLib.Industry.MechanicalPower;
using ExpandedLib.Networks;
using ExpandedLib.Testing;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests.Machining;

/// <summary>The mounted piece a machine tool pools through its <c>OnTesselation</c>, drawn through the game's own shape tesselator.</summary>
public sealed class MachineAnchorDrawTests : IDisposable
{
  private static readonly string DrawFixtures = Path.Combine(
    RepoPaths.Root,
    "tests",
    "ExpandedLib.Tests",
    "Harness",
    "Fixtures",
    "draw"
  );

  private static readonly BlockPos Here = new(0, 64, 0);

  private static readonly MachineAnchor Table = new(
    "table",
    new Vector3(6f, 10f, 12f),
    Vector3.Zero,
    1f
  );

  private static MachineKind Kind = null!;
  private static MachiningJobRegistry Registry = null!;

  private sealed class DrawHost : BlockEntityMachineTool
  {
    protected override MachineKind? Kind => MachineAnchorDrawTests.Kind;

    protected override MachiningJobRegistry Jobs => Registry;
  }

  private readonly TestWorld _w;
  private readonly Item _blank;
  private readonly Item _turned;
  private readonly Item _other;

  public MachineAnchorDrawTests()
  {
    TestLang.Init();
    _w = new TestWorld().ClientAssets("drawfix", DrawFixtures);
    _w.ClientTesselates = true;
    _w.RegisterNetwork("mpshaft", sys => new MpShaftNetwork(sys));
    _blank = Item("drawfix:blank");
    _turned = Item("drawfix:turned");
    _other = Item("drawfix:other");
    Kind = KindWith("table");
    Registry = new MachiningJobRegistry();
    Registry.Contribute(
      MachiningTestData.Set(
        MachiningTestData.Job(
          "turn",
          "drawfix:blank",
          "drawfix:turned",
          material: "castiron"
        )
      )
    );
  }

  public void Dispose() => _w.Dispose();

  private Item Item(string code)
  {
    Item item = _w.RegisterItem(code);
    item.Shape = new CompositeShape
    {
      Base = new AssetLocation("drawfix:oneface"),
    };
    return item;
  }

  private static MachineKind KindWith(string? anchor) =>
    new(
      "drawfix:lathe",
      10f,
      0f,
      false,
      [Table],
      [
        new MachineKindStep(
          "turn",
          CutMotion.Rotary,
          822f,
          "cutter",
          anchor,
          null
        ),
      ]
    );

  private DrawHost Stand(Item? piece, float rotateY = 0f)
  {
    var block = TestBlocks.Configure(new Block(), "drawfix:lathe", 9201);
    block.DrawType = EnumDrawType.JSON;
    block.Shape = new CompositeShape
    {
      Base = new AssetLocation("drawfix:oneface"),
      rotateY = rotateY,
    };
    var host = new DrawHost();
    _w.Place(Here, block, host);
    _w.Initialize(host);
    if (piece is not null)
      host.Inventory[BlockEntityMachineTool.WorkSlot].Itemstack = new ItemStack(
        piece
      );
    return host;
  }

  private static PooledMesh[] Pooled(DrawnCell cell) =>
    [.. cell.Meshes.Where(m => !m.FromBlock)];

  private static (float[] Min, float[] Max) Box(MeshData mesh)
  {
    float[] min = [float.MaxValue, float.MaxValue, float.MaxValue];
    float[] max = [float.MinValue, float.MinValue, float.MinValue];
    for (int i = 0; i < mesh.VerticesCount; i++)
    for (int axis = 0; axis < 3; axis++)
    {
      float v = mesh.xyz[i * 3 + axis];
      min[axis] = Math.Min(min[axis], v);
      max[axis] = Math.Max(max[axis], v);
    }
    return (min, max);
  }

  // The slab's one face is its top, 0.25 above the cell floor; the anchor puts the cell centre at (6,10,12)/16.
  private static void AssertSlabAt(MeshData mesh, float rotateY)
  {
    float[] corners = [-0.5f, 0.5f];
    float cx = 6f / 16 - 0.5f;
    float cz = 12f / 16 - 0.5f;
    double turn = rotateY * Math.PI / 180d;
    double sin = Math.Sin(turn);
    double cos = Math.Cos(turn);
    double[] xs =
    [
      .. corners.SelectMany(a =>
        corners.Select(b => 0.5 + (cx + a) * cos + (cz + b) * sin)
      ),
    ];
    double[] zs =
    [
      .. corners.SelectMany(a =>
        corners.Select(b => 0.5 - (cx + a) * sin + (cz + b) * cos)
      ),
    ];
    var (min, max) = Box(mesh);
    Assert.Equal(xs.Min(), min[0], 3);
    Assert.Equal(xs.Max(), max[0], 3);
    Assert.Equal(10f / 16 - 0.25f, min[1], 3);
    Assert.Equal(10f / 16 - 0.25f, max[1], 3);
    Assert.Equal(zs.Min(), min[2], 3);
    Assert.Equal(zs.Max(), max[2], 3);
  }

  // Fails when the mesh is added at the block origin instead of the anchor, or the block's facing is not applied.
  [Theory]
  [InlineData(0f)]
  [InlineData(90f)]
  [InlineData(180f)]
  [InlineData(270f)]
  public void A_mounted_piece_is_drawn_at_the_anchor_in_every_facing(
    float rotateY
  )
  {
    Stand(_blank, rotateY);

    PooledMesh mounted = Assert.Single(Pooled(DrawnCell.Of(_w, Here)));

    AssertSlabAt(mounted.Mesh, rotateY);
  }

  // Fails when the result is drawn only while a job applies to it, so the cut piece vanishes.
  [Theory]
  [InlineData(0f)]
  [InlineData(90f)]
  [InlineData(180f)]
  [InlineData(270f)]
  public void The_cut_result_is_drawn_at_the_anchor_of_the_step_that_made_it(
    float rotateY
  )
  {
    Stand(_turned, rotateY);

    PooledMesh result = Assert.Single(Pooled(DrawnCell.Of(_w, Here)));

    AssertSlabAt(result.Mesh, rotateY);
  }

  // Fails when a piece no job takes or yields is drawn mounted.
  [Fact]
  public void A_piece_no_job_names_is_not_drawn_mounted()
  {
    Stand(_other);

    Assert.Empty(Pooled(DrawnCell.Of(_w, Here)));
  }

  // Fails when an empty work slot is read as a piece, or pools a mesh.
  [Fact]
  public void An_empty_work_slot_draws_no_piece()
  {
    Stand(null);

    DrawnCell cell = DrawnCell.Of(_w, Here);

    Assert.Null(cell.Threw);
    Assert.Empty(Pooled(cell));
    Assert.False(cell.SkipsBlockMesh);
  }

  // Fails when a step with no anchor still draws the piece.
  [Fact]
  public void A_step_that_names_no_anchor_draws_no_piece()
  {
    Kind = KindWith(null);
    Stand(_blank);

    Assert.Empty(Pooled(DrawnCell.Of(_w, Here)));
  }

  // Fails when the piece is tesselated again at every chunk redraw instead of read from the cache by item code.
  [Fact]
  public void The_pieces_mesh_is_cached_by_item_code()
  {
    Stand(_blank);

    int first = DrawnCell.Of(_w, Here).TextureKeys.Count;
    int second = DrawnCell.Of(_w, Here).TextureKeys.Count;

    Assert.Equal(first - 1, second);
  }
}
#endif
