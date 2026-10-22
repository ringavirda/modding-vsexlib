#if GAME_GE_1_22
using System;
using System.Collections.Generic;
using System.IO;
using ExpandedLib.Structures;
using ExpandedLib.Testing;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>A cell is drawn as the game's chunk tesselator draws it: the entity's pooled meshes when its
/// <c>OnTesselation</c> returns true, the block's own mesh otherwise, through the game's own shape
/// tesselator.</summary>
public class DrawnCellTests
{
  private static readonly string DrawFixtures = Path.Combine(
    RepoPaths.Root,
    "tests",
    "ExpandedLib.Tests",
    "Harness",
    "Fixtures",
    "draw"
  );

  private static readonly string DoorFixtures = Path.Combine(
    RepoPaths.Root,
    "tests",
    "ExpandedLib.Tests",
    "Laws",
    "Fixtures",
    "cliptrace"
  );

  private static readonly BlockPos Here = new(0, 64, 0);

  private static TestWorld DrawWorld() =>
    new TestWorld()
      .ClientAssets("drawfix", DrawFixtures)
      .ClientAssets("cliptrace", DoorFixtures);

  private static Block ShapeBlock(
    string shape,
    int id = 9101,
    EnumDrawType drawType = EnumDrawType.JSON
  )
  {
    var block = TestBlocks.Configure(new Block(), $"drawfix:{shape}", id);
    block.DrawType = drawType;
    block.Shape = new CompositeShape
    {
      Base = new AssetLocation($"drawfix:{shape}"),
    };
    return block;
  }

  private static ProbeEntity Place(
    TestWorld world,
    string mode,
    string shape = "oneface",
    EnumDrawType drawType = EnumDrawType.JSON
  )
  {
    var entity = new ProbeEntity { Mode = mode };
    world.Place(Here, ShapeBlock(shape, drawType: drawType), entity);
    world.Initialize(entity);
    return entity;
  }

  private static DrawnCell Draw(TestWorld world)
  {
    world.ClientTesselates = true;
    return DrawnCell.Of(world, Here);
  }

  [Fact]
  public void An_entity_returning_true_over_nothing_draws_nothing()
  {
    using var world = DrawWorld();
    Place(world, "true");

    DrawnCell cell = Draw(world);

    Assert.True(cell.SkipsBlockMesh);
    Assert.Empty(cell.Meshes);
    Assert.Equal(0, cell.Indices);
    Assert.True(cell.DrawsNothing);
  }

  [Fact]
  public void An_entity_returning_false_draws_the_blocks_one_face_shape()
  {
    using var world = DrawWorld();
    Place(world, "false");

    DrawnCell cell = Draw(world);

    Assert.False(cell.SkipsBlockMesh);
    Assert.Equal(4, cell.Vertices);
    Assert.Equal(6, cell.Indices);
    Assert.True(Assert.Single(cell.Meshes).FromBlock);
    Assert.Contains("all", cell.TextureKeys);
  }

  [Fact]
  public void Faces_set_and_not_resolved_draw_nothing()
  {
    using var world = DrawWorld();
    Place(world, "unresolved");

    DrawnCell cell = Draw(world);

    Assert.Null(cell.Threw);
    Assert.Equal(0, cell.Indices);
  }

  [Fact]
  public void A_throw_after_pooling_keeps_the_pooled_mesh_and_adds_the_blocks()
  {
    using var world = DrawWorld();
    Place(world, "pool-throw");

    DrawnCell cell = Draw(world);

    Assert.IsType<InvalidOperationException>(cell.Threw);
    Assert.False(cell.SkipsBlockMesh);
    Assert.Equal(2, cell.Meshes.Count);
    Assert.False(cell.Meshes[0].FromBlock);
    Assert.True(cell.Meshes[1].FromBlock);
    Assert.Equal(8, cell.Vertices);
  }

  [Fact]
  public void A_cube_drawn_block_never_calls_its_entity()
  {
    using var world = DrawWorld();
    ProbeEntity entity = Place(world, "true", drawType: EnumDrawType.Cube);

    world.ClientTesselates = true;
    ClientTwin twin = ClientTwin.Stand(world, entity)[0];
    DrawnCell cell = DrawnCell.Of(twin);

    Assert.Equal(0, ((ProbeEntity)twin.Twin).Calls);
    Assert.False(cell.SkipsBlockMesh);
    Assert.Equal(4, cell.Vertices);
  }

  [Fact]
  public void A_mesh_turned_about_the_origin_lies_outside_the_footprint()
  {
    using var world = DrawWorld();
    Place(world, "turned");

    DrawnCell cell = Draw(world);

    Assert.Equal(cell.Vertices, cell.VerticesOutside);
    Assert.InRange(cell.Overshoot, 0.99, 1.01);
  }

  [Fact]
  public void A_one_voxel_lip_is_inside_the_footprint()
  {
    using var world = DrawWorld();
    world.Place(Here, ShapeBlock("lip"));

    DrawnCell cell = Draw(world);

    Assert.Equal(17.0 / 16, cell.Bounds!.X2, 6);
    Assert.Equal(0, cell.VerticesOutside);
  }

  [Fact]
  public void A_mesh_over_a_filler_cell_is_inside_the_footprint()
  {
    using var world = DrawWorld();
    world.Place(Here, ShapeBlock("wide"));
    world.Place(
      Here.AddCopy(1, 0, 0),
      world.Filler,
      new BlockEntityStructureFiller { Principal = Here.Copy() }
    );

    DrawnCell cell = Draw(world);

    Assert.Equal(2.0, cell.Bounds!.X2, 6);
    Assert.Equal(0, cell.VerticesOutside);
    Assert.Equal(2, cell.Footprint.Count);
  }

  [Fact]
  public void A_mesh_over_a_cell_no_filler_names_is_outside_the_footprint()
  {
    using var world = DrawWorld();
    world.Place(Here, ShapeBlock("wide"));

    DrawnCell cell = Draw(world);

    Assert.True(cell.VerticesOutside > 0);
    Assert.InRange(cell.Overshoot, 0.99, 1.01);
  }

  [Fact]
  public void A_running_animation_credits_the_cell()
  {
    using var world = DrawWorld();
    var door = new AnimatedDoor();
    var block = TestBlocks.Configure(new Block(), "cliptrace:door", 9102);
    block.DrawType = EnumDrawType.JSON;
    block.Shape = new CompositeShape
    {
      Base = new AssetLocation("cliptrace:door"),
    };
    world.Place(Here, block, door);
    world.Initialize(door);

    DrawnCell cell = Draw(world);

    Assert.True(cell.SkipsBlockMesh);
    Assert.Equal(0, cell.Indices);
    Assert.True(cell.AnimatorDraws);
    Assert.False(cell.DrawsNothing);
  }

  [Fact]
  public void The_matrix_pool_overload_moves_the_bounds()
  {
    using var world = DrawWorld();
    Place(world, "moved");

    DrawnCell cell = Draw(world);

    Assert.Equal(2.0, cell.Bounds!.X1, 6);
    Assert.Equal(3.0, cell.Bounds!.X2, 6);
  }

  [Fact]
  public void A_redraw_mark_reads_once()
  {
    using var world = DrawWorld();
    Place(world, "false");
    world.Accessor.MarkBlockDirty(Here);

    Assert.True(Draw(world).RedrawMarked);
    Assert.False(DrawnCell.Of(world, Here).RedrawMarked);
  }

  [Fact]
  public void An_obj_shape_is_refused()
  {
    using var world = DrawWorld();
    var block = ShapeBlock("oneface");
    block.Shape.Format = EnumShapeFormat.Obj;
    world.Place(Here, block);

    DrawnCell cell = Draw(world);

    Assert.IsType<NotSupportedException>(cell.BlockMeshThrew);
    Assert.Equal(0, cell.Indices);
  }

  [Fact]
  public void Drawing_with_the_switch_off_is_refused()
  {
    using var world = DrawWorld();
    Place(world, "false");

    Assert.Throws<InvalidOperationException>(() => DrawnCell.Of(world, Here));
  }

  private sealed class ProbeEntity : BlockEntity
  {
    public string Mode { get; set; } = "";

    public int Calls { get; private set; }

    public override bool OnTesselation(
      ITerrainMeshPool mesher,
      ITesselatorAPI tessThreadTesselator
    )
    {
      Calls++;
      switch (Mode)
      {
        case "true":
          return true;
        case "false":
          return false;
        case "unresolved":
          mesher.AddMeshData(
            Tesselate(tessThreadTesselator, Slab(16, resolve: false))
          );
          return true;
        case "pool-throw":
          mesher.AddMeshData(Tesselate(tessThreadTesselator, Slab(16)));
          throw new InvalidOperationException("after pooling");
        case "turned":
          mesher.AddMeshData(
            Tesselate(tessThreadTesselator, Slab(16, from: 8))
              .Rotate(new Vec3f(0, 0, 0), 0, GameMath.PIHALF, 0)
          );
          return true;
        case "moved":
          float[] matrix = Mat4f.Create();
          Mat4f.Translate(matrix, matrix, 2, 0, 0);
          mesher.AddMeshData(Tesselate(tessThreadTesselator, Slab(16)), matrix);
          return true;
        default:
          return base.OnTesselation(mesher, tessThreadTesselator);
      }
    }

    private static MeshData Tesselate(ITesselatorAPI tess, Shape shape)
    {
      tess.TesselateShape(
        "probe",
        shape,
        out MeshData mesh,
        tess.GetTextureSource(new Block())
      );
      return mesh;
    }

    private static Shape Slab(double to, double from = 0, bool resolve = true)
    {
      var element = new ShapeElement
      {
        Name = "slab",
        From = [from, 0, from],
        To = [to, 4, to],
        Faces = new Dictionary<string, ShapeElementFace>
        {
          ["up"] = new ShapeElementFace
          {
            Texture = "#all",
            Uv = [0, 0, 16, 16],
            Enabled = true,
          },
        },
      };
      if (resolve)
        element.TrimTextureNamesAndResolveFaces();
      return new Shape { Elements = [element], Textures = [] };
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
      base.ToTreeAttributes(tree);
      tree.SetString("mode", Mode);
    }

    public override void FromTreeAttributes(
      ITreeAttribute tree,
      IWorldAccessor worldAccessForResolve
    )
    {
      base.FromTreeAttributes(tree, worldAccessForResolve);
      Mode = tree.GetString("mode", "");
    }
  }

  private sealed class AnimatedDoor : BlockEntity
  {
    public AnimatedDoor() => Behaviors.Add(new BEBehaviorAnimatable(this));

    public override void Initialize(ICoreAPI api)
    {
      base.Initialize(api);
      if (api.Side != EnumAppSide.Client)
        return;
      var util = GetBehavior<BEBehaviorAnimatable>()!.animUtil;
      util.InitializeAnimator("cliptrace-door");
      util.StartAnimation(
        new AnimationMetaData
        {
          Code = "open",
          Animation = "open",
          AnimationSpeed = 1f,
          EaseInSpeed = 6000f,
          EaseOutSpeed = 6000f,
        }
      );
    }
  }
}
#endif
