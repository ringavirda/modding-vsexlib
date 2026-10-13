using System;
using System.Collections.Generic;
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

  private static T Place<T>(TestWorld world, BlockPos pos, T entity)
    where T : BlockEntity {
    world.Place(pos, DoorBlock(), entity);
    world.Initialize(entity);
    return entity;
  }

  private static HandDoor PlaceDoor(TestWorld world, BlockPos pos) =>
    Place(world, pos, new HandDoor());

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
    var first = Place(world, Here, new NeighbourDoor());
    var second = Place(world, beside, new NeighbourDoor());
    var twins = ClientTwin.Stand(world, first, second);
    IBlockAccessor clientAccessor = world.ClientApi.World.BlockAccessor;

    Assert.Same(twins[1].Twin, ((NeighbourDoor)twins[0].Twin).Seen);
    Assert.Same(twins[1].Twin, clientAccessor.GetBlockEntity(beside));
    Assert.Null(clientAccessor.GetBlockEntity(new BlockPos(5, 64, 5)));
  }

  [Fact]
  public void A_client_tick_gets_the_seconds_since_it_last_fired() {
    using var world = DoorWorld();
    var twin = ClientTwin.Stand(world, Place(world, Here, new TimedDoor()))[0];

    twin.Frames(0.02f, 6);

    var seconds = ((TimedDoor)twin.Twin).Seconds;
    Assert.Equal(2, seconds.Count);
    Assert.Equal(0.06f, seconds[0], 0.001f);
    Assert.Equal(0.06f, seconds[1], 0.001f);
  }

  [Fact]
  public void A_client_tick_on_the_world_overload_fires_with_the_seconds() {
    using var world = DoorWorld();
    var twin = ClientTwin.Stand(world, Place(world, Here, new WorldTickDoor()))[
      0
    ];

    twin.Frames(0.02f, 6);

    var seconds = ((WorldTickDoor)twin.Twin).Seconds;
    Assert.Equal(2, seconds.Count);
    Assert.Equal(0.06f, seconds[0], 0.001f);
  }

  [Fact]
  public void A_client_tick_error_goes_to_the_entitys_handler() {
    using var world = DoorWorld();
    var twin = ClientTwin.Stand(world, Place(world, Here, new ErringDoor()))[0];

    twin.Frames(0.05f, 1);

    var errors = ((ErringDoor)twin.Twin).Errors;
    Assert.Single(errors);
    Assert.IsType<InvalidOperationException>(errors[0]);
  }

  [Fact]
  public void Releasing_a_twin_removes_its_renderers_and_ticks() {
    using var world = DoorWorld();
    int renderers = world.ClientRendererCount;
    int ticks = world.ClientTickCount;
    var twin = ClientTwin.Stand(world, Place(world, Here, new RenderingDoor()))[
      0
    ];
    Assert.True(world.ClientTickCount > ticks);

    world.ReleaseClientTwin(twin.Twin);

    Assert.Equal(renderers, world.ClientRendererCount);
    Assert.Equal(ticks, world.ClientTickCount);
    Assert.Null(world.ClientApi.World.BlockAccessor.GetBlockEntity(Here));
  }

  [Fact]
  public void A_stand_that_fails_releases_the_twins_it_made() {
    using var world = DoorWorld();
    var good = Place(world, Here, new RenderingDoor());
    var bad = Place(world, new BlockPos(1, 64, 0), new StandFailingDoor());
    int renderers = world.ClientRendererCount;
    int ticks = world.ClientTickCount;

    Assert.Throws<InvalidOperationException>(() =>
      ClientTwin.Stand(world, good, bad)
    );

    Assert.Equal(renderers, world.ClientRendererCount);
    Assert.Equal(ticks, world.ClientTickCount);
    Assert.Null(world.ClientApi.World.BlockAccessor.GetBlockEntity(Here));
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

  private sealed class NeighbourDoor : BlockEntity {
    public BlockEntity? Seen { get; private set; }

    public override void Initialize(ICoreAPI api) {
      base.Initialize(api);
      if (api.Side == EnumAppSide.Client)
        Seen = api.World.BlockAccessor.GetBlockEntity(
          new BlockPos(Pos.X + 1, Pos.Y, Pos.Z)
        );
    }
  }

  private sealed class TimedDoor : BlockEntity {
    public List<float> Seconds { get; } = new();

    public override void Initialize(ICoreAPI api) {
      base.Initialize(api);
      if (api.Side == EnumAppSide.Client)
        RegisterGameTickListener(dt => Seconds.Add(dt), 50);
    }
  }

  private sealed class WorldTickDoor : BlockEntity {
    public List<float> Seconds { get; } = new();

    public override void Initialize(ICoreAPI api) {
      base.Initialize(api);
      if (api.Side == EnumAppSide.Client)
        RegisterGameTickListener((_, _, dt) => Seconds.Add(dt), 50);
    }
  }

  private sealed class ErringDoor : BlockEntity {
    public List<Exception> Errors { get; } = new();

    public override void Initialize(ICoreAPI api) {
      base.Initialize(api);
      if (api.Side == EnumAppSide.Client)
        RegisterGameTickListener(
          _ => throw new InvalidOperationException("the tick threw"),
          50
        );
    }

    public override void TickingExceptionHandler(Exception e) => Errors.Add(e);
  }

  private sealed class RenderingDoor : BlockEntity {
    public override void Initialize(ICoreAPI api) {
      base.Initialize(api);
      if (api.Side != EnumAppSide.Client)
        return;
      ((ICoreClientAPI)api).Event.RegisterRenderer(
        new CountingRenderer(),
        EnumRenderStage.Opaque,
        "rendering"
      );
      RegisterGameTickListener(_ => { }, 50);
    }
  }

  private sealed class StandFailingDoor : BlockEntity {
    public override void Initialize(ICoreAPI api) {
      base.Initialize(api);
      if (api.Side == EnumAppSide.Client)
        throw new InvalidOperationException("the twin cannot stand");
    }
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
