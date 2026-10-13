using System;
using System.IO;
using ExpandedLib.Testing;
using NSubstitute;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>A server entity's client twin: its animator is built by the engine's own client code, on the
/// fake client's cache, and its frames and syncs follow the server only where the server marked them.</summary>
public class ClientTwinTests {
  private static readonly string Fixtures = Path.Combine(
    RepoPaths.Root,
    "tests",
    "ExpandedLib.Tests",
    "Laws",
    "Fixtures",
    "cliptrace"
  );

  private static readonly BlockPos Here = new(0, 64, 0);

  private static TestWorld DoorWorld() =>
    new TestWorld().ClientAssets("cliptrace", Fixtures);

  private static Block DoorBlock() {
    var block = TestBlocks.Configure(new Block(), "cliptrace:door", 9001);
    block.Shape = new CompositeShape {
      Base = new AssetLocation("cliptrace:door"),
    };
    return block;
  }

  private static HandDoor PlaceDoor(TestWorld world, BlockPos pos) {
    var door = new HandDoor();
    world.Place(pos, DoorBlock(), door);
    world.Initialize(door);
    return door;
  }

  private static (HandDoor Server, ClientTwin Twin) Standing(
    TestWorld world,
    BlockPos pos
  ) {
    HandDoor door = PlaceDoor(world, pos);
    return (door, ClientTwin.Stand(world, door)[0]);
  }

  [Fact]
  public void A_twin_builds_a_client_animator_in_the_client_cache() {
    using var world = DoorWorld();
    var (_, twin) = Standing(world, Here);

    Assert.IsType<ClientAnimator>(twin.Animator);
    Assert.True(world.ClientApi.ObjectCache.ContainsKey("animUtil-animCache"));
    Assert.False(world.Api.ObjectCache.ContainsKey("animUtil-animCache"));
    Assert.Equal(1, world.QueuedMainThreadTasks);
  }

  [Fact]
  public void Frames_move_a_held_clip_thirty_frames_a_second() {
    using var world = DoorWorld();
    var (_, twin) = Standing(world, Here);

    twin.Frames(1f / 30f, 15);

    Assert.InRange(
      twin.Animator!.GetAnimationState("open")!.CurrentFrame,
      14.5f,
      15.5f
    );
  }

  [Fact]
  public void A_twin_syncs_only_a_tree_the_server_marked_dirty() {
    using var world = DoorWorld();
    var (door, twin) = Standing(world, Here);
    var clientDoor = (HandDoor)twin.Twin;

    door.Opened = 1;
    Assert.False(twin.Sync());
    Assert.Equal(0, clientDoor.Opened);

    door.MarkDirty();
    Assert.True(twin.Sync());
    Assert.Equal(1, clientDoor.Opened);
    Assert.False(twin.Sync());
  }

  [Fact]
  public void A_twins_own_mark_dirty_stays_on_the_client() {
    using var world = DoorWorld();
    var (_, twin) = Standing(world, Here);

    twin.Twin.MarkDirty();

    Assert.True(world.ClientMarkedDirty(Here));
    Assert.False(twin.Sync());
  }

  [Fact]
  public void A_twin_reads_its_neighbours_twin() {
    using var world = DoorWorld();
    var beside = new BlockPos(1, 64, 0);
    HandDoor first = PlaceDoor(world, Here);
    HandDoor second = PlaceDoor(world, beside);
    var twins = ClientTwin.Stand(world, first, second);
    IBlockAccessor clientAccessor = world.ClientApi.World.BlockAccessor;

    Assert.Same(twins[1].Twin, clientAccessor.GetBlockEntity(beside));
    Assert.Null(clientAccessor.GetBlockEntity(new BlockPos(5, 64, 5)));
  }

  [Fact]
  public void A_twins_frames_fire_exlib_and_animation_renderers_only() {
    using var world = DoorWorld();
    var (_, twin) = Standing(world, Here);
    var exlib = new CountingRenderer();
    var foreign = Substitute.For<IRenderer>();
    world.ClientApi.Event.RegisterRenderer(
      exlib,
      EnumRenderStage.Opaque,
      "exlib"
    );
    world.ClientApi.Event.RegisterRenderer(
      foreign,
      EnumRenderStage.Opaque,
      "foreign"
    );

    twin.Frames(1f / 30f, 3);

    Assert.Equal(3, exlib.Fired);
    foreign
      .DidNotReceive()
      .OnRenderFrame(Arg.Any<float>(), Arg.Any<EnumRenderStage>());
  }

  [Fact]
  public void A_twin_client_tick_fires_once_per_interval() {
    using var world = DoorWorld();
    var (_, twin) = Standing(world, Here);

    twin.Frames(0.5f, 4);

    Assert.Equal(2, ((HandDoor)twin.Twin).Ticks);
  }

  [Fact]
  public void A_removed_twin_stops_rendering_and_ticking() {
    using var world = DoorWorld();
    var (_, twin) = Standing(world, Here);
    var clientDoor = (HandDoor)twin.Twin;

    clientDoor.OnBlockRemoved();
    twin.Frames(0.5f, 4);

    Assert.Equal(0, clientDoor.Ticks);
    Assert.Equal(0f, twin.Animator!.GetAnimationState("open")!.CurrentFrame);
  }

  [Fact]
  public void ClientAssets_refuses_a_domain_whose_shapes_were_already_read() {
    using var world = new TestWorld();
    Assert.Null(
      world.ClientApi.Assets.TryGet(
        new AssetLocation("cliptrace:shapes/door.json")
      )
    );

    Assert.Throws<InvalidOperationException>(() =>
      world.ClientAssets("cliptrace", Fixtures)
    );
  }

  [Fact]
  public void A_domain_without_a_shapes_folder_reads_null() {
    using var world = new TestWorld();

    Assert.Null(
      world.ClientApi.Assets.TryGet(
        new AssetLocation("nodomain:shapes/door.json")
      )
    );
  }

  [Fact]
  public void A_game_shape_reads_from_the_install_survival_tree() {
    using var world = new TestWorld();

    Assert.NotNull(
      world.ClientApi.Assets.TryGet(
        new AssetLocation("game:shapes/block/blastpowdersack.json")
      )
    );
  }

  private sealed class CountingRenderer : IRenderer {
    public int Fired { get; private set; }
    public double RenderOrder => 0;
    public int RenderRange => 0;

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage) =>
      Fired++;

    public void Dispose() { }
  }

  private sealed class HandDoor : BlockEntity {
    public HandDoor() => Behaviors.Add(new BEBehaviorAnimatable(this));

    public int Opened { get; set; }

    public int Ticks { get; private set; }

    public override void Initialize(ICoreAPI api) {
      base.Initialize(api);
      if (api.Side != EnumAppSide.Client)
        return;
      var util = GetBehavior<BEBehaviorAnimatable>()!.animUtil;
      util.InitializeAnimator("cliptrace-door");
      util.StartAnimation(
        new AnimationMetaData {
          Code = "open",
          Animation = "open",
          AnimationSpeed = 1f,
          EaseInSpeed = 6000f,
          EaseOutSpeed = 6000f,
        }
      );
      RegisterGameTickListener(_ => Ticks++, 1000);
    }

    public override void ToTreeAttributes(ITreeAttribute tree) {
      base.ToTreeAttributes(tree);
      tree.SetInt("opened", Opened);
    }

    public override void FromTreeAttributes(
      ITreeAttribute tree,
      IWorldAccessor worldAccessForResolve
    ) {
      base.FromTreeAttributes(tree, worldAccessForResolve);
      Opened = tree.GetInt("opened");
    }
  }
}
