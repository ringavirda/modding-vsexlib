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
using Vintagestory.GameContent.Mechanics;
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

  private static void Place(TestWorld world, BlockPos pos, BlockEntity entity) {
    world.Place(pos, DoorBlock(), entity);
    world.Initialize(entity);
  }

  private static HandDoor PlaceDoor(TestWorld world, BlockPos pos) {
    var door = new HandDoor();
    Place(world, pos, door);
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
#if GAME_GE_1_22
    Assert.Equal(1, world.QueuedMainThreadTasks);
#else
    // Below 1.22 AnimationUtil.InitializeAnimator queues a task beside the renderer's.
    Assert.Equal(2, world.QueuedMainThreadTasks);
#endif
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
    var first = new NeighbourDoor();
    Place(world, Here, first);
    var second = new NeighbourDoor();
    Place(world, beside, second);
    var twins = ClientTwin.Stand(world, first, second);
    IBlockAccessor clientAccessor = world.ClientApi.World.BlockAccessor;

    Assert.Same(twins[1].Twin, ((NeighbourDoor)twins[0].Twin).Seen);
    Assert.Same(twins[1].Twin, clientAccessor.GetBlockEntity(beside));
    Assert.Null(clientAccessor.GetBlockEntity(new BlockPos(5, 64, 5)));
  }

  [Fact]
  public void A_client_tick_gets_the_seconds_since_it_last_fired() {
    using var world = DoorWorld();
    var door = new TimedDoor();
    Place(world, Here, door);
    var twin = ClientTwin.Stand(world, door)[0];

    twin.Frames(0.02f, 6);

    var seconds = ((TimedDoor)twin.Twin).Seconds;
    Assert.Equal(2, seconds.Count);
    Assert.Equal(0.06f, seconds[0], 0.001f);
    Assert.Equal(0.06f, seconds[1], 0.001f);
  }

#if GAME_GE_1_22
  [Fact]
  public void A_client_tick_on_the_world_overload_fires_with_the_seconds()
  {
    using var world = DoorWorld();
    var door = new WorldTickDoor();
    Place(world, Here, door);
    var twin = ClientTwin.Stand(world, door)[0];

    twin.Frames(0.02f, 6);

    var seconds = ((WorldTickDoor)twin.Twin).Seconds;
    Assert.Equal(2, seconds.Count);
    Assert.Equal(0.06f, seconds[0], 0.001f);
  }
#endif

  [Fact]
  public void A_client_tick_error_goes_to_the_entitys_handler() {
    using var world = DoorWorld();
    var door = new ErringDoor();
    Place(world, Here, door);
    var twin = ClientTwin.Stand(world, door)[0];

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
    var door = new RenderingDoor();
    Place(world, Here, door);
    var twin = ClientTwin.Stand(world, door)[0];
    Assert.True(world.ClientTickCount > ticks);

    world.ReleaseClientTwin(twin.Twin);

    Assert.Equal(renderers, world.ClientRendererCount);
    Assert.Equal(ticks, world.ClientTickCount);
    Assert.Null(world.ClientApi.World.BlockAccessor.GetBlockEntity(Here));
  }

  [Fact]
  public void A_stand_that_fails_releases_the_twins_it_made() {
    using var world = DoorWorld();
    var good = new RenderingDoor();
    Place(world, Here, good);
    var bad = new StandFailingDoor();
    Place(world, new BlockPos(1, 64, 0), bad);
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
  public void A_twin_finds_its_own_power_mod_and_not_the_servers() {
    using var world = DoorWorld();
    var server = new MechanicalPowerMod();
    world.Mods.Register(server);
    var door = new ModReadingDoor();
    Place(world, Here, door);
    int renderers = world.ClientRendererCount;

    var twin = ClientTwin.Stand(world, door)[0];

    MechanicalPowerMod? found = ((ModReadingDoor)twin.Twin).Found;
    Assert.NotNull(found);
    Assert.NotSame(server, found);
    Assert.Equal(renderers, world.ClientRendererCount);
  }

  [Fact]
  public void A_twin_reads_the_recipes_the_world_registered() {
    using var world = DoorWorld();
    var recipes = new RecipeRegistrySystem();
    world.Mods.Register(recipes);
    var door = new ModReadingDoor();
    Place(world, Here, door);

    var twin = ClientTwin.Stand(world, door)[0];

    Assert.Same(recipes, ((ModReadingDoor)twin.Twin).Recipes);
  }

  [Fact]
  public void A_twin_sees_the_mods_the_world_lists() {
    using var world = DoorWorld();
    world.Mods.Add("listedmod", "2.3.4");
    var door = new ModReadingDoor();
    Place(world, Here, door);

    var twin = ClientTwin.Stand(world, door)[0];

    Assert.Equal("2.3.4", ((ModReadingDoor)twin.Twin).ListedVersion);
  }

  [Fact]
  public void Stepping_one_twin_leaves_the_others_ticks_alone() {
    using var world = DoorWorld();
    HandDoor first = PlaceDoor(world, Here);
    HandDoor second = PlaceDoor(world, new BlockPos(1, 64, 0));
    var a = ClientTwin.Stand(world, first)[0];
    var b = ClientTwin.Stand(world, second)[0];

    world.StepClientFrames(1.5f, 1, b.Twin);

    Assert.Equal(0, ((HandDoor)a.Twin).Ticks);
    Assert.Equal(1, ((HandDoor)b.Twin).Ticks);
  }

#if GAME_GE_1_22
  [Fact]
  public void A_drawing_twin_reads_a_shape_by_location()
  {
    using var world = DoorWorld();
    world.ClientTesselates = true;
    var door = new ModReadingDoor();
    Place(world, Here, door);

    var twin = ClientTwin.Stand(world, door)[0];

    Assert.NotNull(((ModReadingDoor)twin.Twin).Shape);
  }

  [Fact]
  public void A_drawing_twin_resolves_any_texture_in_the_block_atlas()
  {
    using var world = DoorWorld();
    world.ClientTesselates = true;
    var door = new ModReadingDoor();
    Place(world, Here, door);

    var twin = ClientTwin.Stand(world, door)[0];

    Assert.NotNull(((ModReadingDoor)twin.Twin).Atlas);
  }
#endif

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

#if GAME_GE_1_22
  private sealed class WorldTickDoor : BlockEntity
  {
    public List<float> Seconds { get; } = new();

    public override void Initialize(ICoreAPI api)
    {
      base.Initialize(api);
      if (api.Side == EnumAppSide.Client)
        RegisterGameTickListener((_, _, dt) => Seconds.Add(dt), 50);
    }
  }
#endif

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

    public override void OnBlockRemoved() {
      base.OnBlockRemoved();
      if (Api.Side == EnumAppSide.Client)
        throw new NullReferenceException("a half-initialised twin");
    }
  }

  private sealed class ModReadingDoor : BlockEntity {
    public MechanicalPowerMod? Found { get; private set; }
    public string? ListedVersion { get; private set; }
    public RecipeRegistrySystem? Recipes { get; private set; }
    public Shape? Shape { get; private set; }
    public TextureAtlasPosition? Atlas { get; private set; }

    public override void Initialize(ICoreAPI api) {
      base.Initialize(api);
      if (api is not ICoreClientAPI capi)
        return;
      Found = api.ModLoader.GetModSystem<MechanicalPowerMod>();
      ListedVersion = api.ModLoader.GetMod("listedmod")?.Info.Version;
      Recipes = api.ModLoader.GetModSystem<RecipeRegistrySystem>();
      Shape = api.Assets.Get<Shape>(
        new AssetLocation("game:shapes/block/basic/cube.json")
      );
      Atlas = capi.BlockTextureAtlas[
        new AssetLocation("game:block/stone/rock/granite1")
      ];
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
