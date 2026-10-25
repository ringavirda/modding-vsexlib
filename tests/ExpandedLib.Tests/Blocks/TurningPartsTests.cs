using System.Linq;
using ExpandedLib.Industry.MechanicalPower;
using ExpandedLib.Networks;
using ExpandedLib.Testing;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// <see cref="BEBehaviorTurningParts"/> and its renderer: which elements turn, the matrix each instance gets,
/// the draw groups and what goes to the chunk mesh.
/// </summary>
public class TurningPartsTests {
  private static readonly BlockPos Here = new(0, 64, 0);
  private static readonly BlockPos Anchor = new(9, 9, 9);

  private static float[] Run(
    TestWorld world,
    ClientTwin twin,
    double angle,
    float nodeRatio = 1f
  ) {
    TurningFixtures.NameRun(twin, Anchor, nodeRatio);
    TurningFixtures
      .Angles(world)
      .Receive(TurningFixtures.Packet(Anchor, angle, 0f), 0);
    TurningInstance instance = TurningFixtures.Behavior(twin).Instances[0];
    var block = twin.Twin.Block;
    return TurningPartsRenderer.Compose(
      new Vec3f(0f, 0f, 0f),
      new Vec3f(block.Shape.rotateX, block.Shape.rotateY, block.Shape.rotateZ),
      instance.Part,
      instance.Theta
    );
  }

  private static Vec3f Map(float[] m, float x, float y, float z) =>
    new(
      m[0] * x + m[4] * y + m[8] * z + m[12],
      m[1] * x + m[5] * y + m[9] * z + m[13],
      m[2] * x + m[6] * y + m[10] * z + m[14]
    );

  private static string Part(string extra) =>
    "{ \"parts\": [ { \"elements\": [\"Shaft\"]" + extra + " } ] }";

  // Fails when the part's ratio is dropped from the angle, or its sign.
  [Theory]
  [InlineData(1f, 1)]
  [InlineData(2f, 1)]
  [InlineData(0.5f, 1)]
  [InlineData(1f, -1)]
  [InlineData(2f, -1)]
  [InlineData(0.5f, -1)]
  public void A_part_turns_by_the_angle_times_its_ratio_and_sign(
    float ratio,
    int sign
  ) {
    using var world = TurningFixtures.NewWorld();
    Block block = TurningFixtures.Define(
      world,
      "shaft",
      Part($", \"ratio\": {ratio}, \"sign\": {sign}")
    );
    var twin = TurningFixtures.Stand(world, block, Here);

    float[] m = Run(world, twin, 0.7);

    double turn = 0.7 * ratio * sign;
    Vec3f rim = Map(m, 12f / 16, 8f / 16, 8f / 16);
    Assert.Equal(0.5 + 0.25 * System.Math.Cos(turn), rim.X, 4);
    Assert.Equal(0.5 + 0.25 * System.Math.Sin(turn), rim.Y, 4);
    Assert.Equal(0.5, rim.Z, 4);
  }

  // Fails when the node's ratio does not scale the run's angle for its part.
  [Fact]
  public void A_part_on_a_geared_node_turns_by_its_nodes_ratio() {
    using var world = TurningFixtures.NewWorld();
    Block block = TurningFixtures.Define(world, "shaft", Part(""));
    var twin = TurningFixtures.Stand(world, block, Here);

    float[] m = Run(world, twin, 0.4, nodeRatio: -2f);

    Vec3f rim = Map(m, 12f / 16, 8f / 16, 8f / 16);
    Assert.Equal(0.5 + 0.25 * System.Math.Cos(-0.8), rim.X, 4);
    Assert.Equal(0.5 + 0.25 * System.Math.Sin(-0.8), rim.Y, 4);
  }

  // Fails when the part turns about the cell centre instead of its declared axis and pivot.
  [Fact]
  public void A_part_turns_about_its_declared_axis_and_pivot() {
    using var world = TurningFixtures.NewWorld();
    Block block = TurningFixtures.Define(
      world,
      "shaft",
      Part(", \"axis\": \"x\", \"pivot\": [8, 12, 8]")
    );
    var twin = TurningFixtures.Stand(world, block, Here);

    float[] m = Run(world, twin, System.Math.PI / 2);

    Vec3f below = Map(m, 8f / 16, 8f / 16, 8f / 16);
    Assert.Equal(0.5, below.X, 4);
    Assert.Equal(0.75, below.Y, 4);
    Assert.Equal(0.25, below.Z, 4);
  }

  // Fails when the block's rotation is left out of the instance matrix.
  [Fact]
  public void The_blocks_rotation_carries_the_part() {
    using var world = TurningFixtures.NewWorld();
    Block block = TurningFixtures.Define(world, "shaft", Part(""), rotateY: 90);
    var twin = TurningFixtures.Stand(world, block, Here);

    float[] m = Run(world, twin, 0.0);

    Vec3f rim = Map(m, 12f / 16, 8f / 16, 8f / 16);
    Assert.Equal(0.5, rim.X, 4);
    Assert.Equal(0.25, rim.Z, 4);
  }

  // Fails when a camera offset is left out of the matrix.
  [Fact]
  public void The_cell_offset_from_the_camera_moves_the_part() {
    float[] m = TurningPartsRenderer.Compose(
      new Vec3f(3f, -2f, 5f),
      new Vec3f(0f, 0f, 0f),
      new TurningPart(["Shaft"]),
      0d
    );

    Vec3f corner = Map(m, 0f, 0f, 0f);
    Assert.Equal(3f, corner.X, 4);
    Assert.Equal(-2f, corner.Y, 4);
    Assert.Equal(5f, corner.Z, 4);
  }

  // Fails when the group key holds the block code, so blocks that share a shape draw apart.
  [Fact]
  public void Blocks_sharing_a_shape_draw_as_one_group() {
    using var world = TurningFixtures.NewWorld();
    Block ns = TurningFixtures.Define(
      world,
      "shaftns",
      TurningFixtures.ShaftPart
    );
    Block we = TurningFixtures.Define(
      world,
      "shaftwe",
      TurningFixtures.ShaftPart,
      rotateY: 90
    );

    TurningFixtures.Stand(world, ns, Here);
    TurningFixtures.Stand(world, we, Here.AddCopy(1, 0, 0));
    TurningFixtures.Stand(world, ns, Here.AddCopy(2, 0, 0));

    var group = Assert.Single(TurningPartsRenderer.For(world.ClientApi).Groups);
    Assert.Equal(3, group.Instances.Count);
  }

  // Fails when parts of one block that turn different elements share a group.
  [Fact]
  public void Parts_naming_other_elements_draw_apart() {
    using var world = TurningFixtures.NewWorld();
    Block a = TurningFixtures.Define(
      world,
      "shafta",
      TurningFixtures.ShaftPart
    );
    Block b = TurningFixtures.Define(
      world,
      "shaftb",
      "{ \"parts\": [ { \"elements\": [\"Bearing\"] } ] }"
    );

    TurningFixtures.Stand(world, a, Here);
    TurningFixtures.Stand(world, b, Here.AddCopy(1, 0, 0));

    Assert.Equal(2, TurningPartsRenderer.For(world.ClientApi).Groups.Count);
  }

  // Fails when removing the block leaves its instances with the renderer.
  [Fact]
  public void A_removed_block_leaves_the_renderer() {
    using var world = TurningFixtures.NewWorld();
    Block block = TurningFixtures.Define(
      world,
      "shaft",
      TurningFixtures.ShaftPart
    );
    var twin = TurningFixtures.Stand(world, block, Here);
    Assert.Single(TurningPartsRenderer.For(world.ClientApi).Groups);

    twin.Twin.OnBlockRemoved();

    Assert.Empty(TurningPartsRenderer.For(world.ClientApi).Groups);
    Assert.Empty(TurningFixtures.Behavior(twin).Instances);
  }

  // Fails when the buffer does not double from 64 until it holds the instances.
  [Fact]
  public void The_instance_buffer_starts_at_64_and_doubles() {
    Assert.Equal(64, TurningPartsRenderer.CapacityFor(0, 64));
    Assert.Equal(64, TurningPartsRenderer.CapacityFor(64, 64));
    Assert.Equal(128, TurningPartsRenderer.CapacityFor(65, 64));
    Assert.Equal(256, TurningPartsRenderer.CapacityFor(129, 64));
  }

  // Fails when a block with no parts declared does not turn its whole shape about z, or a declared part's fields are not read.
  [Fact]
  public void Parts_are_read_from_the_behaviors_properties() {
    using var world = TurningFixtures.NewWorld();
    Block bare = TurningFixtures.Define(world, "bare");
    Block full = TurningFixtures.Define(
      world,
      "full",
      "{ \"parts\": [ { \"elements\": [\"Shaft\"], \"slot\": 1, \"ratio\": 0.5, \"sign\": -1, \"axis\": \"x\", \"pivot\": [1, 2, 3] } ] }"
    );

    var whole = TurningFixtures.Behavior(
      TurningFixtures.Stand(world, bare, Here)
    );
    var named = TurningFixtures.Behavior(
      TurningFixtures.Stand(world, full, Here.AddCopy(1, 0, 0))
    );

    TurningPart all = Assert.Single(whole.Parts);
    Assert.Equal(["*"], all.Elements);
    Assert.Equal(EnumAxis.Z, all.Axis);
    Assert.Null(all.Pivot);
    TurningPart part = Assert.Single(named.Parts);
    Assert.Equal(["Shaft"], part.Elements);
    Assert.Equal((byte)1, part.Slot);
    Assert.Equal(0.5f, part.Ratio);
    Assert.Equal(-1, part.Sign);
    Assert.Equal(EnumAxis.X, part.Axis);
    Assert.Equal(new Vec3f(1f, 2f, 3f), part.Pivot);
  }

  // Fails when UseParts keeps the declared instances.
  [Fact]
  public void UseParts_replaces_the_parts_and_their_instances() {
    using var world = TurningFixtures.NewWorld();
    Block block = TurningFixtures.Define(
      world,
      "shaft",
      TurningFixtures.ShaftPart
    );
    var twin = TurningFixtures.Stand(world, block, Here);

    TurningFixtures
      .Behavior(twin)
      .UseParts([
        new TurningPart(["Bearing"]),
        new TurningPart(["Shaft"], Ratio: 2f),
      ]);

    Assert.Equal(2, TurningFixtures.Behavior(twin).Instances.Count);
    Assert.Equal(2, TurningPartsRenderer.For(world.ClientApi).Groups.Count);
  }

  // Fails when a slot with no run reads a turn, or the server reads one.
  [Fact]
  public void A_slot_with_no_run_reads_zero() {
    using var world = TurningFixtures.NewWorld();
    Block block = TurningFixtures.Define(
      world,
      "shaft",
      TurningFixtures.ShaftPart
    );
    var twin = TurningFixtures.Stand(world, block, Here);
    TurningFixtures.NameRun(twin, Anchor);
    TurningFixtures
      .Angles(world)
      .Receive(TurningFixtures.Packet(Anchor, 1.0, 0f), 0);

    Assert.Equal(1.0f, TurningFixtures.Behavior(twin).AngleRad(0), 4);
    Assert.Equal(0f, TurningFixtures.Behavior(twin).AngleRad(3));
    Assert.Equal(
      0f,
      twin.Server.GetBehavior<BEBehaviorTurningParts>()!.AngleRad(0)
    );
  }

  // Fails when the angle is not folded into [0, 2 pi).
  [Fact]
  public void AngleRad_folds_to_one_turn() {
    using var world = TurningFixtures.NewWorld();
    Block block = TurningFixtures.Define(
      world,
      "shaft",
      TurningFixtures.ShaftPart
    );
    var twin = TurningFixtures.Stand(world, block, Here);
    TurningFixtures.NameRun(twin, Anchor);
    TurningFixtures
      .Angles(world)
      .Receive(
        TurningFixtures.Packet(Anchor, -1.0 - 4 * System.Math.PI, 0f),
        0
      );

    Assert.Equal(
      2 * System.Math.PI - 1.0,
      TurningFixtures.Behavior(twin).AngleRad(0),
      4
    );
  }

  // Fails when a run named by the server is not written to the tree, or is read back with the wrong anchor.
  [Fact]
  public void A_run_survives_the_tree() {
    using var world = TurningFixtures.NewWorld();
    Block block = TurningFixtures.Define(
      world,
      "shaft",
      TurningFixtures.ShaftPart
    );
    var twin = TurningFixtures.Stand(world, block, Here);

    TurningFixtures.NameRun(twin, new BlockPos(4, 5, 6), 2f);
    TurningFixtures
      .Angles(world)
      .Receive(TurningFixtures.Packet(new BlockPos(4, 5, 6), 1.0, 0f), 0);

    Assert.Equal(2.0f, TurningFixtures.Behavior(twin).AngleRad(0), 4);
  }

#if GAME_GE_1_22
  private static DrawnCell Draw(TestWorld world, Block block)
  {
    world.Accessor.SetBlock(block.Id, Here);
    world.ClientTesselates = true;
    return DrawnCell.Of(world, Here);
  }

  // Fails when OnTesselation tesselates the whole shape, turning elements included.
  [Fact]
  public void Still_elements_go_to_the_chunk_mesh_and_turning_ones_to_the_renderer()
  {
    using var world = TurningFixtures.NewWorld();
    Block block = TurningFixtures.Define(
      world,
      "shaft",
      TurningFixtures.ShaftPart
    );

    DrawnCell cell = Draw(world, block);

    Assert.True(cell.SkipsBlockMesh);
    Assert.Equal(4, cell.Vertices);
    var group = Assert.Single(TurningPartsRenderer.For(world.ClientApi).Groups);
    Assert.Equal(["Shaft"], group.Instances[0].Part.Elements);
  }

  // Fails when a block that turns nothing loses its own mesh.
  [Fact]
  public void A_block_with_no_parts_keeps_its_own_mesh()
  {
    using var world = TurningFixtures.NewWorld();
    Block block = TurningFixtures.Define(world, "clip", "{ \"parts\": [] }");

    DrawnCell cell = Draw(world, block);

    Assert.False(cell.SkipsBlockMesh);
    Assert.Equal(12, cell.Vertices);
    Assert.Empty(TurningPartsRenderer.For(world.ClientApi).Groups);
  }

  // Fails when a cell whose whole shape turns still adds a mesh to the chunk.
  [Fact]
  public void A_block_whose_whole_shape_turns_adds_nothing_to_the_chunk()
  {
    using var world = TurningFixtures.NewWorld();
    Block block = TurningFixtures.Define(world, "whole");

    DrawnCell cell = Draw(world, block);

    Assert.True(cell.SkipsBlockMesh);
    Assert.Equal(0, cell.Vertices);
  }

  // Fails when a part naming no element is removed from the chunk mesh, or is not logged once.
  [Fact]
  public void A_part_naming_no_element_stays_in_the_chunk_and_is_logged_once()
  {
    using var world = TurningFixtures.NewWorld();
    Block block = TurningFixtures.Define(
      world,
      "lost",
      "{ \"parts\": [ { \"elements\": [\"Nothing\"] } ] }"
    );
    DrawnCell cell = Draw(world, block);

    world.StepClientFrames(1f / 30f, 3);

    Assert.Equal(12, cell.Vertices);
    world.Log.Expect(EnumLogType.Error, "matches no element");
  }
#endif
}
