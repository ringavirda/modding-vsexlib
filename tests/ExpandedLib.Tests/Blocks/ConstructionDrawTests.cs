#if GAME_GE_1_22
using System.IO;
using ExpandedLib.Blocks;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>A construction block draws the elements of the stages built so far, once.</summary>
public class ConstructionDrawTests
{
  private static readonly BlockPos Here = new(0, 64, 0);

  private static readonly string Fixture = Path.Combine(
    RepoPaths.Root,
    "tests",
    "ExpandedLib.Tests",
    "Harness",
    "Fixtures",
    "ConstructFixture"
  );

  private static TestWorld Stand(int stage)
  {
    var world = new TestWorld();
    world.LoadAssets(Fixture);
    world.ClientAssets(
      "constructfixture",
      Path.Combine(Fixture, "assets", "constructfixture")
    );
    world.ClientTesselates = true;
    Block block = world.World.GetBlock(
      new AssetLocation("constructfixture:stand")
    );
    world.Accessor.SetBlock(block.BlockId, Here, new ItemStack(block));
    ExRightClickConstructable behavior = world
      .GetBlockEntity(Here)!
      .GetBehavior<ExRightClickConstructable>();
    StructureBreaks.Pay(
      world,
      Here,
      block,
      behavior,
      stage,
      StructureBreaks.Payment.CreativeWithCtrl,
      []
    );
    return world;
  }

  private static ExRightClickConstructable Twin(TestWorld world) =>
    world
      .ClientApi.World.BlockAccessor.GetBlockEntity(Here)!
      .GetBehavior<ExRightClickConstructable>();

  // Fails when OnTesselation returns true without pooling the built elements' mesh.
  [Theory]
  [InlineData(0, 4)]
  [InlineData(1, 8)]
  [InlineData(2, 12)]
  public void Each_stage_draws_the_elements_built_so_far(
    int stage,
    int vertices
  )
  {
    using TestWorld world = Stand(stage);

    DrawnCell cell = DrawnCell.Of(world, Here);

    Assert.True(cell.SkipsBlockMesh);
    Assert.Equal(vertices, cell.Vertices);
    Assert.Equal(0, cell.VerticesOutside);
  }

  // Fails when the guard on OnShapeChanged subscribers is dropped: an animator-drawn shape is added a second time.
  [Fact]
  public void A_shape_an_animator_subscribes_to_is_not_drawn_again()
  {
    using TestWorld world = Stand(2);
    DrawnCell.Of(world, Here);
    Twin(world).OnShapeChanged += _ => { };

    DrawnCell cell = DrawnCell.Of(world, Here);

    Assert.True(cell.SkipsBlockMesh);
    Assert.Equal(0, cell.Vertices);
  }

  // Fails when the guard on EntityDrawsShape is dropped: a shape the entity draws is added a second time.
  [Fact]
  public void A_shape_the_entity_draws_is_not_drawn_again()
  {
    using TestWorld world = Stand(2);
    DrawnCell.Of(world, Here);
    Twin(world).EntityDrawsShape = true;

    DrawnCell cell = DrawnCell.Of(world, Here);

    Assert.True(cell.SkipsBlockMesh);
    Assert.Equal(0, cell.Vertices);
  }
}
#endif
