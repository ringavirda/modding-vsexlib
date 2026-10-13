using System;
using System.Collections;
using System.IO;
using System.Linq;
using ExpandedLib.Blocks;
using ExpandedLib.Definitions;
using ExpandedLib.Registries;
using ExpandedLib.Testing;
using NSubstitute;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>The clip-actions behaviour on a client twin: it builds the animator itself and moves
/// each framed clip toward the target its synced state sets.</summary>
public class ClipActionsBehaviorTests {
  private const float Dt = 1f / 60f;

  private static readonly string Fixtures = Path.Combine(
    RepoPaths.Root,
    "tests",
    "ExpandedLib.Tests",
    "Laws",
    "Fixtures",
    "cliptrace"
  );

  private static readonly BlockPos Here = new(0, 64, 0);

  private static TestWorld NewWorld() =>
    new TestWorld()
      .ClientAssets("cliptrace", Fixtures)
      .RegisterClass("test-door", typeof(DoorEntity))
      .RegisterClass("test-claim", typeof(ClaimDoor))
      .RegisterClass("ClipActions", typeof(BEBehaviorClipActions));

  private static ExBlockDef DoorDef(string code, string shape) =>
    ExBlockDef
      .Create("cliptrace", code)
      .Shape("cliptrace:" + shape)
      .EntityClass("test-door")
      .EntityBehavior("ClipActions");

  private static ExBlockDef Driven(string code = "drivendoor") =>
    DoorDef(code, "drivendoor")
      .ClipAction(new ClipAction("open") { State = "doorOpen", Seconds = 1f });

  private static (DoorEntity Server, ClientTwin Twin) Stand(
    TestWorld world,
    ExBlockDef def,
    Action<DoorEntity>? setup = null,
    Action<Block>? tweak = null
  ) {
    Block block = world.DefineBlock(def, DefinitionCodes.Expand(def).First());
    tweak?.Invoke(block);
    world.Accessor.SetBlock(block.Id, Here);
    var door = (DoorEntity)world.GetBlockEntity(Here)!;
    setup?.Invoke(door);
    return (door, ClientTwin.Stand(world, door)[0]);
  }

  private static void Open(DoorEntity door, ClientTwin twin, bool open) {
    door.Open = open;
    door.MarkDirty();
    twin.Sync();
  }

  private static float Frame(ClientTwin twin) =>
    twin.Animator!.GetAnimationState("open")!.CurrentFrame;

  private static BEBehaviorClipActions Behavior(ClientTwin twin) =>
    twin.Twin.GetBehavior<BEBehaviorClipActions>()!;

  private static int Active(ClientTwin twin) =>
    Behavior(twin).animUtil.activeAnimationsByAnimCode.Count;

  [Fact]
  public void A_block_without_clip_actions_builds_and_plays_nothing() {
    using var world = NewWorld();
    var (_, twin) = Stand(world, DoorDef("bare", "door"));

    twin.Frames(Dt, 3);

    Assert.IsType<ClientAnimator>(twin.Animator);
    Assert.Equal(0, Active(twin));
    Assert.Equal(0, twin.Animator!.ActiveAnimationCount);
  }

  [Fact]
  public void The_frame_moves_at_its_rate() {
    using var world = NewWorld();
    var (door, twin) = Stand(world, Driven());
    twin.Frames(Dt, 1);

    Open(door, twin, true);
    twin.Frames(Dt, 30);

    Assert.InRange(Frame(twin), 14f, 16f);
    twin.Frames(Dt, 40);
    Assert.Equal(30f, Frame(twin), 0.01f);

    Open(door, twin, false);
    twin.Frames(Dt, 70);
    Assert.Equal(0, Active(twin));
    Assert.Equal(0f, Frame(twin));
  }

  [Fact]
  public void A_load_shows_the_target_at_once() {
    using var world = NewWorld();
    var (_, twin) = Stand(world, Driven(), door => door.Open = true);

    twin.Frames(Dt, 1);

    Assert.Equal(30f, Frame(twin), 0.01f);
  }

  [Fact]
  public void An_exchange_rebuilds_the_animator() {
    using var world = NewWorld();
    var (door, twin) = Stand(world, Driven());
    twin.Frames(Dt, 1);
    AnimatorBase first = twin.Animator!;
    ExBlockDef otherDef = DoorDef("otherdoor", "pacedoor");
    Block other = world.DefineBlock(
      otherDef,
      DefinitionCodes.Expand(otherDef).First()
    );

    world.Place(Here, other, door);
    twin.Twin.Block = other;
    twin.Frames(Dt, 1);

    Assert.NotSame(first, twin.Animator);
    var cache = (IDictionary)world.ClientApi.ObjectCache["animUtil-animCache"];
    Assert.True(cache.Contains("animutil-cliptrace:pacedoor"));
  }

  [Fact]
  public void The_animator_draws_with_the_blocks_full_rotation() {
    using var world = NewWorld();
    var (_, twin) = Stand(
      world,
      Driven(),
      null,
      block => block.Shape.rotateX = 90f
    );

    twin.Frames(Dt, 1);

    float[] transform = Behavior(twin).animUtil.renderer.CustomTransform!;
    float[] moved = Mat4f.MulWithVec4(transform, 0.5f, 0.5f, 1f, 1f);
    Assert.Equal(0.5f, moved[0], 0.001f);
    Assert.Equal(0f, moved[1], 0.001f);
    Assert.Equal(0.5f, moved[2], 0.001f);
  }

  [Fact]
  public void A_numeric_state_maps_through_its_range() {
    using var world = NewWorld();
    var (door, twin) = Stand(
      world,
      DoorDef("leandoor", "drivendoor")
        .ClipAction(
          new ClipAction("open") {
            State = "lean",
            Range = (10f, 20f),
            Seconds = 1f,
          }
        )
    );
    twin.Frames(Dt, 1);

    door.Lean = 15f;
    door.MarkDirty();
    twin.Sync();
    twin.Frames(Dt, 60);

    Assert.Equal(15f, Frame(twin), 0.01f);
  }

  [Fact]
  public void A_refused_declaration_logs_and_still_builds_the_animator() {
    using var world = NewWorld();
    world.Log.Expect(EnumLogType.Error, "neither a state nor a by line");
    var (_, twin) = Stand(
      world,
      DoorDef("idledoor", "drivendoor").ClipAction(new ClipAction("open"))
    );

    twin.Frames(Dt, 2);

    Assert.IsType<ClientAnimator>(twin.Animator);
    Assert.Contains(world.Log.Errors, m => m.Contains("neither a state nor"));
  }

  [Fact]
  public void A_clip_the_shape_lacks_logs_and_moves_nothing() {
    using var world = NewWorld();
    world.Log.Expect(EnumLogType.Error, "'missing'");
    var (door, twin) = Stand(
      world,
      DoorDef("ghostdoor", "drivendoor")
        .ClipAction(new ClipAction("missing") { State = "doorOpen" })
    );

    Open(door, twin, true);
    twin.Frames(Dt, 5);

    Assert.Contains(world.Log.Errors, m => m.Contains("'missing'"));
    Assert.Equal(0, Active(twin));
  }

  [Fact]
  public void A_clip_the_shape_lacks_is_ignored_by_SetTarget() {
    using var world = NewWorld();
    var (_, twin) = Stand(world, DoorDef("bare", "door"));

    Behavior(twin).SetTarget("missing", 1f);
    twin.Frames(Dt, 3);

    Assert.Equal(0, Active(twin));
    Assert.Equal(0f, Frame(twin));
  }

  [Fact]
  public void SetTarget_works_a_clip_no_action_declares() {
    using var world = NewWorld();
    var (_, twin) = Stand(world, DoorDef("bare", "door"));
    twin.Frames(Dt, 1);

    Behavior(twin).SetTarget("open", 1f);
    twin.Frames(Dt, 30);

    Assert.InRange(Frame(twin), 14f, 16f);
  }

  [Fact]
  public void SetTarget_clamps_its_fraction() {
    using var world = NewWorld();
    var (_, twin) = Stand(world, DoorDef("bare", "door"));
    twin.Frames(Dt, 1);

    Behavior(twin).SetTarget("open", -1f);
    twin.Frames(Dt, 5);

    Assert.Equal(0, Active(twin));
  }

  [Fact]
  public void A_pose_that_is_not_a_frame_is_left_alone() {
    using var world = NewWorld();
    var (_, twin) = Stand(
      world,
      DoorDef("rundoor", "drivendoor")
        .ClipAction(
          new ClipAction("open") { Pose = ClipPose.Run, State = "doorOpen" }
        )
    );

    Behavior(twin).SetTarget("open", 1f);
    twin.Frames(Dt, 5);

    Assert.Equal(0, Active(twin));
  }

  private static ExBlockDef Swing() =>
    DoorDef("gatedoor", "gatedoor")
      .ClipAction(
        new ClipAction("open") {
          Pose = ClipPose.Play,
          State = "doorOpen",
          When = 1f,
          Seconds = 1f,
        }
      )
      .ClipAction(
        new ClipAction("closed") {
          Pose = ClipPose.Play,
          State = "doorOpen",
          When = 0f,
          Seconds = 1f,
        }
      );

  private static ExBlockDef Fan(float seconds = 2f) =>
    DoorDef("fanblock", "fan")
      .ClipAction(
        new ClipAction("spin") {
          Pose = ClipPose.Run,
          State = "lean",
          Seconds = seconds,
        }
      );

  private static float FrameOf(ClientTwin twin, string clip) =>
    twin.Animator!.GetAnimationState(clip)!.CurrentFrame;

  private static bool Playing(ClientTwin twin, string clip) =>
    Behavior(twin).animUtil.activeAnimationsByAnimCode.ContainsKey(clip);

  [Fact]
  public void A_play_state_loaded_shows_its_clip_at_its_last_frame() {
    using var world = NewWorld();
    var (_, twin) = Stand(world, Swing(), door => door.Open = true);

    twin.Frames(Dt, 1);

    Assert.Equal(30f, FrameOf(twin, "open"), 0.01f);
    Assert.False(Playing(twin, "closed"));
  }

  [Fact]
  public void A_play_clip_starts_at_its_first_frame_and_stops_its_group() {
    using var world = NewWorld();
    var (door, twin) = Stand(world, Swing());
    twin.Frames(Dt, 1);
    Assert.True(Playing(twin, "closed"));

    Open(door, twin, true);
    twin.Frames(Dt, 29);

    Assert.False(Playing(twin, "closed"));
    Assert.InRange(FrameOf(twin, "open"), 13f, 17f);
    twin.Frames(Dt, 40);
    Assert.Equal(30f, FrameOf(twin, "open"), 0.01f);
  }

  [Fact]
  public void Play_chooses_a_clip_of_a_group() {
    using var world = NewWorld();
    var (_, twin) = Stand(world, Swing());
    twin.Frames(Dt, 1);

    Behavior(twin).Play("open");
    twin.Frames(Dt, 5);

    Assert.True(Playing(twin, "open"));
    Assert.False(Playing(twin, "closed"));
  }

  [Fact]
  public void A_clip_declared_with_another_pose_is_not_played_or_run() {
    using var world = NewWorld();
    var (_, twin) = Stand(world, Driven(), door => door.Open = true);
    twin.Frames(Dt, 1);

    Behavior(twin).Play("open");
    Behavior(twin).Run("open", 1f);
    twin.Frames(Dt, 5);

    Assert.True(
      Behavior(twin).animUtil.activeAnimationsByAnimCode["open"].AnimationSpeed
        < 0.01f
    );
    Assert.Equal(30f, Frame(twin), 0.01f);
  }

  [Fact]
  public void A_run_rate_follows_its_state() {
    using var world = NewWorld();
    var (_, twin) = Stand(world, Fan(), door => door.Lean = 2f);

    twin.Frames(Dt, 30);

    Assert.InRange(FrameOf(twin, "spin"), 29f, 32f);
  }

  [Fact]
  public void Run_holds_a_clip_at_rate_zero_where_it_is() {
    using var world = NewWorld();
    var (_, twin) = Stand(world, DoorDef("plainfan", "fan"));
    twin.Frames(Dt, 1);

    Behavior(twin).Run("spin", 1f);
    twin.Frames(Dt, 30);
    Behavior(twin).Run("spin", 0f);
    twin.Frames(Dt, 2);
    float held = FrameOf(twin, "spin");
    twin.Frames(Dt, 20);

    Assert.InRange(held, 10f, 40f);
    Assert.Equal(held, FrameOf(twin, "spin"), 0.1f);
    Assert.True(Playing(twin, "spin"));
  }

  [Fact]
  public void A_run_clip_picked_by_its_state_replaces_the_one_it_leaves() {
    using var world = NewWorld();
    var (door, twin) = Stand(
      world,
      DoorDef("twofans", "rattledoor")
        .ClipAction(
          new ClipAction("shake") {
            Pose = ClipPose.Run,
            State = "lean",
            When = 0f,
          }
        )
        .ClipAction(
          new ClipAction("rattle") {
            Pose = ClipPose.Run,
            State = "lean",
            When = 1f,
          }
        )
    );
    twin.Frames(Dt, 3);
    Assert.True(Playing(twin, "shake"));
    Assert.False(Playing(twin, "rattle"));

    door.Lean = 1f;
    door.MarkDirty();
    twin.Sync();
    twin.Frames(Dt, 3);

    Assert.True(Playing(twin, "rattle"));
    Assert.False(Playing(twin, "shake"));
  }

  [Fact]
  public void A_claimed_build_is_posed_by_the_behaviour() {
    using var world = NewWorld();
    var (door, twin) = Stand(
      world,
      DoorDef("claimdoor", "drivendoor")
        .EntityClass("test-claim")
        .ClipAction(new ClipAction("open") { State = "doorOpen", Seconds = 1f })
    );
    twin.Frames(Dt, 3);

    var cache = (IDictionary)world.ClientApi.ObjectCache["animUtil-animCache"];
    Assert.True(cache.Contains("animutil-claimed-key"));
    Assert.False(cache.Contains("animutil-cliptrace:drivendoor"));
    Open(door, twin, true);
    twin.Frames(Dt, 70);
    Assert.Equal(30f, Frame(twin), 0.01f);
  }

  [Fact]
  public void A_paused_game_does_not_move_the_clip() {
    using var world = NewWorld();
    var (door, twin) = Stand(world, Driven());
    twin.Frames(Dt, 1);
    world.ClientApi.IsGamePaused.Returns(true);

    Open(door, twin, true);
    twin.Frames(Dt, 10);

    Assert.Equal(0, Active(twin));
  }

  [Fact]
  public void A_block_whose_shape_is_missing_builds_no_animator() {
    using var world = NewWorld();
    world.Log.Expect(EnumLogType.Error, "nosuchshape");
    var (_, twin) = Stand(world, DoorDef("lostdoor", "nosuchshape"));

    twin.Frames(Dt, 3);

    Assert.Null(twin.Animator);
  }

  [Theory]
  [InlineData("removed")]
  [InlineData("unloaded")]
  [InlineData("broken")]
  public void A_released_behaviour_stops_moving_the_clip(string how) {
    using var world = NewWorld();
    var (door, twin) = Stand(world, Driven());
    twin.Frames(Dt, 1);
    BEBehaviorClipActions behavior = Behavior(twin);

    switch (how) {
      case "removed":
        behavior.OnBlockRemoved();
        break;
      case "unloaded":
        behavior.OnBlockUnloaded();
        break;
      default:
        behavior.OnBlockBroken();
        break;
    }
    Open(door, twin, true);
    twin.Frames(Dt, 10);

    Assert.Equal(0, Active(twin));
  }

  private sealed class ClaimDoor : DoorEntity {
    public override void Initialize(ICoreAPI api) {
      base.Initialize(api);
      if (api is ICoreClientAPI)
        new ConstructedAnimator(this, () => "claimed-key").Initialize();
    }
  }

  private class DoorEntity : BlockEntity {
    public bool Open { get; set; }

    public float Lean { get; set; }

    public override void ToTreeAttributes(ITreeAttribute tree) {
      base.ToTreeAttributes(tree);
      tree.SetBool("doorOpen", Open);
      tree.SetFloat("lean", Lean);
    }

    public override void FromTreeAttributes(
      ITreeAttribute tree,
      IWorldAccessor worldAccessForResolve
    ) {
      base.FromTreeAttributes(tree, worldAccessForResolve);
      Open = tree.GetBool("doorOpen");
      Lean = tree.GetFloat("lean");
    }
  }
}
