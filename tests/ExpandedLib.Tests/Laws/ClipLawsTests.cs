#if GAME_GE_1_22
using System;
using System.IO;
using System.Linq;
using ExpandedLib.Blocks;
using ExpandedLib.Definitions;
using ExpandedLib.Structures;
using ExpandedLib.Testing;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;
using Xunit;
using Xunit.Abstractions;

namespace ExpandedLib.Tests;

/// <summary>The clip laws against doors that each break one: a leaf that snaps shut, one four times
/// too fast, one that ignores the sync, a change the server never marks, a load that plays from
/// frame zero, a door that stops half way, and two shapes keyed alike.</summary>
public class ClipLawsTests(ITestOutputHelper output)
{
  private static readonly string Fixtures = Path.Combine(
    RepoPaths.Root,
    "tests",
    "ExpandedLib.Tests",
    "Laws",
    "Fixtures",
    "cliptrace"
  );

  private static readonly System.Reflection.Assembly[] Exlib =
  [
    typeof(BlockStructureFiller).Assembly,
  ];

  private static void Prepare(TestWorld world)
  {
    world.RegisterClass("ClipActions", typeof(BEBehaviorClipActions));
    world.RegisterClass("test-toggleblock", typeof(ToggleBlock));
    world.RegisterClass("test-stepblock", typeof(StepBlock));
    world.RegisterClass("test-door", typeof(DoorEntity));
    world.RegisterClass("test-hatch", typeof(HatchEntity));
    world.RegisterClass("test-late", typeof(LateHatch));
    world.RegisterClass("test-gate", typeof(GateEntity));
    world.RegisterClass("test-stuck", typeof(StuckDoor));
    world.RegisterClass("test-snap", typeof(SnapDoor));
    world.RegisterClass("test-fast", typeof(FastDoor));
    world.RegisterClass("test-deaf", typeof(DeafDoor));
    world.RegisterClass("test-quiet", typeof(QuietDoor));
    world.RegisterClass("test-jump", typeof(JumpLoadDoor));
    world.RegisterClass("test-half", typeof(HalfDoor));
    world.RegisterClass("test-short", typeof(ShortSlider));
    world.RegisterClass("test-mid", typeof(MidDoor));
    world.RegisterClass("test-midfast", typeof(MidFastDoor));
    world.RegisterClass("test-key", typeof(KeyDoor));
    world.RegisterClass("test-throws", typeof(ThrowingDoor));
    world.RegisterClass("test-unbuilt", typeof(UnbuiltDoor));
    world.RegisterClass("test-renderthrow", typeof(RenderThrowDoor));
  }

  private static BlockLaws.Result Run(
    string? exceptions,
    params ExBlockDef[] defs
  ) =>
    ClipLaws.Run("cliptrace", defs, Exlib, exceptions, Prepare, _ => Fixtures);

  private static BlockLaws.Result Run(params ExBlockDef[] defs) =>
    Run(null, defs);

  private static ClipAction Toggle(string line = "cliptrace:toggle") =>
    new("open")
    {
      State = "doorOpen",
      By = [line],
      Seconds = 1f,
    };

  private static ExBlockDef Leaf(
    string code,
    string entity,
    string shape = "door",
    ClipAction? action = null
  ) =>
    ExBlockDef
      .Create("cliptrace", code)
      .Class("test-toggleblock")
      .Shape("cliptrace:" + shape)
      .EntityClass(entity)
      .EntityBehavior("ClipActions")
      .ClipAction(action ?? Toggle());

  private static ExBlockDef Driven(
    string code,
    string entity,
    string shape,
    ClipAction action
  ) =>
    ExBlockDef
      .Create("cliptrace", code)
      .Class("test-toggleblock")
      .Shape("cliptrace:" + shape)
      .EntityClass(entity)
      .EntityBehavior("ClipActions")
      .ClipAction(action);

  private static ExBlockDef Hatch() =>
    ExBlockDef
      .Create("cliptrace", "drivenhatch")
      .Shape("cliptrace:hatch")
      .EntityClass("test-hatch")
      .EntityBehavior("ClipActions")
      .ClipAction(new ClipAction("open") { State = "raised", Seconds = 0.5f });

  private static ExBlockDef Gate() =>
    ExBlockDef
      .Create("cliptrace", "drivengate")
      .Class("test-stepblock")
      .Shape("cliptrace:door")
      .EntityClass("test-gate")
      .EntityBehavior("ClipActions")
      .ClipAction(
        new ClipAction("open")
        {
          State = "level",
          Range = (0f, 3f),
          By = ["cliptrace:raise", "cliptrace:lower"],
          Seconds = 1f,
        }
      );

  [Fact]
  public void A_driven_door_passes_every_clip_law()
  {
    BlockLaws.Result result = Run(
      Driven("drivendoor", "test-door", "door", Toggle()),
      Hatch(),
      ExBlockDef.Create("cliptrace", "plain")
    );

    Assert.All(result.Laws, law => Assert.Empty(law.Findings));
    Assert.Equal(2, result[ClipLaws.ActionName].Cases);
    Assert.Equal(4, result[ClipLaws.TimeName].Cases);
    Assert.Equal(2, result[ClipLaws.KeyName].Cases);
  }

  [Fact]
  public void A_raise_and_lower_line_is_worked_until_the_tree_stops()
  {
    BlockLaws.Result result = Run(Gate());

    Assert.All(result.Laws, law => Assert.Empty(law.Findings));
    Assert.Equal(1, result[ClipLaws.ActionName].Cases);
  }

  [Fact]
  public void The_trace_records_each_tick_of_the_driven_door()
  {
    TestWorld world = BlockLaws.Stand(
      [Driven("drivendoor", "test-door", "door", Toggle())],
      Exlib,
      Prepare
    );
    world.ClientAssets("cliptrace", Fixtures);
    Block block = world.World.Blocks.First(b => b?.Code?.Path == "drivendoor");
    ClipReference reference = ClipReference.Load(world, block);
    ClipAction action = ClipActions.Of(block).Resolve(reference.Shape).Single();

    ClipTraced traced = new ClipTrace(world, "cliptrace").Trace(
      block,
      action,
      reference
    );

    foreach (
      (string name, ClipWay way) in new[]
      {
        ("forward", traced.Forward!),
        ("reverse", traced.Reverse!),
      }
    )
    foreach (ClipSample s in way.Samples)
      output.WriteLine(
        $"{name} tick {s.Tick} frame {s.Frame:0.##} weight {s.Weight:0.##} leaf degY {s.Poses["leaf"][1]:0.##}"
      );
    Assert.Empty(traced.Problems);
    Assert.Equal(60, traced.Forward!.Arrived);
    Assert.Equal(15f, traced.Forward.Samples[30].Frame, 0.01f);
    Assert.Equal(45f, traced.Forward.Samples[30].Poses["leaf"][1], 0.1f);
    Assert.Equal(1f, traced.Forward.Samples[30].Weight, 0.01f);
    Assert.Equal(60, traced.Reverse!.Arrived);
    Assert.Equal(0f, traced.Reverse.Samples[^1].Poses["leaf"][1], 0.1f);
  }

  [Fact]
  [PlantedDefect(typeof(ClipLaws), nameof(ClipLaws.Run))]
  public void Shutting_to_frame_zero_in_one_tick_is_a_jump()
  {
    BlockLaws.Result result = Run(Leaf("snapdoor", "test-snap"));

    Assert.Contains(
      "cliptrace:door open | reverse moves a part more than three even steps in one tick | cliptrace:snapdoor",
      result[ClipLaws.ActionName].Findings
    );
  }

  [Fact]
  [PlantedDefect(typeof(ClipLaws), nameof(ClipLaws.Run))]
  public void An_open_four_times_too_fast_misses_its_time()
  {
    BlockLaws.Result result = Run(Leaf("fastdoor", "test-fast"));

    Assert.Equal(
      [
        "cliptrace:door open | forward settles in 0.25 s against 1 s | cliptrace:fastdoor",
      ],
      result[ClipLaws.TimeName].Findings.Where(f => f.Contains("forward"))
    );
  }

  [Fact]
  [PlantedDefect(typeof(ClipLaws), nameof(ClipLaws.Run))]
  public void A_door_that_ignores_the_sync_plays_no_clip()
  {
    BlockLaws.Result result = Run(Leaf("deafdoor", "test-deaf"));

    Assert.Equal(
      ["cliptrace:door open | forward plays no clip | cliptrace:deafdoor"],
      result[ClipLaws.ActionName].Findings
    );
    Assert.Empty(result[ClipLaws.TimeName].Findings);
  }

  [Fact]
  [PlantedDefect(typeof(ClipLaws), nameof(ClipLaws.Run))]
  public void A_change_the_server_never_marked_dirty_is_found()
  {
    BlockLaws.Result result = Run(Leaf("quietdoor", "test-quiet"));

    Assert.Equal(
      [
        "cliptrace:door open | changes its tree and never marks the entity dirty, so the client is not told | cliptrace:quietdoor",
      ],
      result[ClipLaws.ActionName].Findings
    );
  }

  [Fact]
  [PlantedDefect(typeof(ClipLaws), nameof(ClipLaws.Run))]
  public void A_door_loaded_open_shows_open_on_its_first_frame()
  {
    BlockLaws.Result result = Run(Leaf("jumpdoor", "test-jump"));

    Assert.Equal(
      [
        "cliptrace:door open | loaded at its far end, it does not show the end pose on its first frame | cliptrace:jumpdoor",
      ],
      result[ClipLaws.ActionName].Findings
    );
    Assert.Empty(result[ClipLaws.TimeName].Findings);
  }

  [Fact]
  [PlantedDefect(typeof(ClipLaws), nameof(ClipLaws.Run))]
  public void A_door_that_stops_half_way_ends_away_from_its_pose()
  {
    BlockLaws.Result result = Run(Leaf("halfdoor", "test-half"));

    Assert.Contains(
      "cliptrace:door open | forward ends away from the clip's end pose | cliptrace:halfdoor",
      result[ClipLaws.ActionName].Findings
    );
  }

  // Fails when the arrival tolerance on a translation is 0.05 block instead of 0.05 voxel.
  [Fact]
  [PlantedDefect(typeof(ClipLaws), nameof(ClipLaws.Run))]
  public void A_part_half_a_voxel_short_of_its_end_pose_is_not_arrived()
  {
    BlockLaws.Result result = Run(Leaf("shortslider", "test-short", "slider"));

    Assert.Contains(
      "cliptrace:slider open | forward ends away from the clip's end pose | cliptrace:shortslider",
      result[ClipLaws.ActionName].Findings
    );
  }

  // Fails when the reference pose is read after a full second of the clip's advance.
  [Fact]
  public void A_reference_pose_is_read_at_the_frame_asked()
  {
    TestWorld world = BlockLaws.Stand(
      [Leaf("spindledoor", "test-door", "spindle")],
      Exlib,
      Prepare
    );
    world.ClientAssets("cliptrace", Fixtures);
    Block block = world.World.Blocks.First(b => b?.Code?.Path == "spindledoor");
    ClipReference reference = ClipReference.Load(world, block);

    float[] first = reference.Pose("open", 0f, ["leaf"])["leaf"];
    float[] quarter = reference.Pose("open", 15f, ["leaf"])["leaf"];

    Assert.Equal(0f, first[1], 1e-3f);
    Assert.Equal(720f, quarter[1], 1e-3f);
  }

  // Fails when a walk from half the clip is judged against the whole clip's seconds.
  [Fact]
  public void A_walk_from_half_the_clip_is_timed_by_the_part_it_travels()
  {
    BlockLaws.Result result = Run(Leaf("middoor", "test-mid"));

    Assert.All(result.Laws, law => Assert.Empty(law.Findings));
    Assert.Equal(2, result[ClipLaws.TimeName].Cases);
  }

  [Fact]
  [PlantedDefect(typeof(ClipLaws), nameof(ClipLaws.Run))]
  public void A_walk_from_half_the_clip_at_twice_the_rate_misses_its_time()
  {
    BlockLaws.Result result = Run(Leaf("midfast", "test-midfast"));

    Assert.Equal(
      ["cliptrace:door open | forward settles in 0.25 s against 0.5 s | cliptrace:midfast"],
      result[ClipLaws.TimeName].Findings.Where(f => f.Contains("forward"))
    );
  }

  [Fact]
  [PlantedDefect(typeof(ClipLaws), nameof(ClipLaws.Run))]
  public void Two_shapes_on_one_key_are_found()
  {
    BlockLaws.Result result = Run(
      ExBlockDef
        .Create("cliptrace", "twinkey")
        .Shape("cliptrace:door")
        .EntityClass("test-key"),
      ExBlockDef
        .Create("cliptrace", "twinkey2")
        .Shape("cliptrace:pacedoor")
        .EntityClass("test-key")
    );

    Assert.Equal(
      [
        "cliptrace:door + cliptrace:pacedoor | share the animator key animutil-cliptrace-door | cliptrace:twinkey, cliptrace:twinkey2",
      ],
      result[ClipLaws.KeyName].Findings
    );
  }

  [Fact]
  [PlantedDefect(typeof(ClipLaws), nameof(ClipLaws.Run))]
  public void An_unused_known_row_is_a_finding()
  {
    string file = Exceptions(
      """{"known": {"clip action": {"cliptrace:door open": "AT9"}}}"""
    );
    try
    {
      BlockLaws.Result result = Run(
        file,
        Driven("drivendoor", "test-door", "door", Toggle())
      );

      Assert.Equal(
        ["cliptrace:door open | known row matches no finding; remove it"],
        result[ClipLaws.ActionName].Findings
      );
    }
    finally
    {
      File.Delete(file);
    }
  }

  [Fact]
  [PlantedDefect(typeof(ClipLaws), nameof(ClipLaws.Run))]
  public void A_known_row_accepts_the_findings_under_its_key()
  {
    string file = Exceptions(
      """
      {"known": {
        "clip action": {"cliptrace:door open": "AT9"},
        "clip time": {"cliptrace:door open": "AT9"}}}
      """
    );
    try
    {
      BlockLaws.Result result = Run(file, Leaf("snapdoor", "test-snap"));

      Assert.All(result.Laws, law => Assert.Empty(law.Findings));
    }
    finally
    {
      File.Delete(file);
    }
  }

  [Fact]
  [PlantedDefect(typeof(ClipLaws), nameof(ClipLaws.Run))]
  public void A_part_the_shape_lacks_is_found()
  {
    BlockLaws.Result result = Run(
      Leaf(
        "ghostdoor",
        "test-snap",
        action: Toggle() with
        {
          Parts = ["ghost"],
        }
      )
    );

    Assert.Equal(
      [
        "cliptrace:door open | names the part 'ghost', which the shape does not have | cliptrace:ghostdoor",
        "cliptrace:door open | moves no part between its first and last frame | cliptrace:ghostdoor",
      ],
      result[ClipLaws.ActionName].Findings
    );
  }

  [Fact]
  [PlantedDefect(typeof(ClipLaws), nameof(ClipLaws.Run))]
  public void A_help_line_the_block_lacks_is_found()
  {
    BlockLaws.Result result = Run(
      Leaf("mutedoor", "test-door", action: Toggle("cliptrace:nothing"))
    );

    Assert.Equal(
      [
        "cliptrace:door open | has no right-click help line 'cliptrace:nothing' for the forward action | cliptrace:mutedoor",
      ],
      result[ClipLaws.ActionName].Findings
    );
  }

  [Fact]
  [PlantedDefect(typeof(ClipLaws), nameof(ClipLaws.Run))]
  public void A_click_that_changes_nothing_is_found()
  {
    BlockLaws.Result result = Run(Leaf("stuckdoor", "test-stuck"));

    Assert.Equal(
      [
        "cliptrace:door open | the forward line 'cliptrace:toggle' changes nothing in the server's tree | cliptrace:stuckdoor",
      ],
      result[ClipLaws.ActionName].Findings
    );
  }

  [Fact]
  [PlantedDefect(typeof(ClipLaws), nameof(ClipLaws.Run))]
  public void A_twin_that_throws_standing_is_found()
  {
    BlockLaws.Result result = Run(Leaf("throwdoor", "test-throws"));

    Assert.Single(
      result[ClipLaws.ActionName].Findings,
      f => f.StartsWith("cliptrace:door twin | threw InvalidOperationException")
    );
    Assert.Single(
      result[ClipLaws.KeyName].Findings,
      f => f.StartsWith("cliptrace:door twin | threw InvalidOperationException")
    );
  }

  [Fact]
  [PlantedDefect(typeof(ClipLaws), nameof(ClipLaws.Run))]
  public void A_twin_that_throws_while_framed_does_not_poison_the_next_block()
  {
    BlockLaws.Result result = Run(
      Leaf("athrow", "test-renderthrow"),
      Driven("drivendoor", "test-door", "door", Toggle())
    );

    Assert.Single(
      result[ClipLaws.KeyName].Findings,
      f => f.StartsWith("cliptrace:door twin | threw InvalidOperationException")
    );
    Assert.All(
      result.Laws.SelectMany(l => l.Findings),
      f => Assert.Contains("| cliptrace:athrow", f)
    );
    Assert.DoesNotContain(
      result.Laws.SelectMany(l => l.Findings),
      f => f.Contains("cliptrace:drivendoor")
    );
  }

  [Fact]
  [PlantedDefect(typeof(ClipLaws), nameof(ClipLaws.Run))]
  public void An_animatable_entity_with_no_animator_is_its_own_finding()
  {
    BlockLaws.Result result = Run(Leaf("unbuiltdoor", "test-unbuilt"));

    Assert.Equal(
      [
        "cliptrace:door twin | has no animator after its first frame | cliptrace:unbuiltdoor",
      ],
      result[ClipLaws.KeyName].Findings
    );
    Assert.Equal(
      [
        "cliptrace:door open | has no animator after its first frame | cliptrace:unbuiltdoor",
      ],
      result[ClipLaws.ActionName].Findings
    );
  }

  [Fact]
  [PlantedDefect(typeof(ClipLaws), nameof(ClipLaws.Run))]
  public void A_known_row_under_a_law_that_did_not_run_is_a_finding()
  {
    string file = Exceptions(
      """{"known": {"clip-action": {"cliptrace:door open": "AT9"}}}"""
    );
    try
    {
      BlockLaws.Result result = Run(
        file,
        Driven("drivendoor", "test-door", "door", Toggle())
      );

      Assert.Equal(
        ["clip-action | known law names no law that ran; remove it"],
        result[ClipLaws.ExceptionsName].Findings
      );
      Assert.Empty(result[ClipLaws.ActionName].Findings);
    }
    finally
    {
      File.Delete(file);
    }
  }

  [Fact]
  [PlantedDefect(typeof(ClipLaws), nameof(ClipLaws.Run))]
  public void An_undeclared_clip_has_no_driver()
  {
    BlockLaws.Result result = Run(Rattle("rattledoor"));

    Assert.Equal(
      [
        "cliptrace:rattledoor rattle | no action drives the clip | cliptrace:rattledoor",
      ],
      result[ClipLaws.DriverName].Findings
    );
    Assert.Equal(3, result[ClipLaws.DriverName].Cases);
  }

  [Fact]
  [PlantedDefect(typeof(ClipLaws), nameof(ClipLaws.Run))]
  public void A_static_clip_with_its_reason_passes_and_an_unused_one_fails()
  {
    string file = Exceptions(
      """
      {"static": {
        "cliptrace:rattledoor rattle": "swung by the sample's own code",
        "cliptrace:door open": "an action drives it"}}
      """
    );
    try
    {
      BlockLaws.Result result = Run(
        file,
        Rattle("rattledoor"),
        Driven("drivendoor", "test-door", "door", Toggle())
      );

      Assert.Equal(
        [
          "cliptrace:door open | static row matches no undriven clip; remove it",
        ],
        result[ClipLaws.DriverName].Findings
      );
    }
    finally
    {
      File.Delete(file);
    }
  }

  [Fact]
  [PlantedDefect(typeof(ClipLaws), nameof(ClipLaws.Run))]
  public void An_action_naming_a_clip_the_shape_lacks_is_a_finding()
  {
    BlockLaws.Result result = ClipLaws.Run(
      "cliptrace",
      [
        Driven(
          "ghostclip",
          "test-door",
          "door",
          new ClipAction("slam") { State = "doorOpen", Seconds = 1f }
        ),
      ],
      Exlib,
      null,
      world =>
      {
        Prepare(world);
        world.Log.Expect(
          EnumLogType.Error,
          "names a clip the shape does not have"
        );
      },
      _ => Fixtures
    );

    Assert.Equal(
      [
        "cliptrace:door slam | names a clip the shape does not have | cliptrace:ghostclip",
        "cliptrace:door open | no action drives the clip | cliptrace:ghostclip",
      ],
      result[ClipLaws.DriverName].Findings
    );
  }

  private static ExBlockDef Swing(string code = "gatedoor") =>
    Driven(
        code,
        "test-door",
        "gatedoor",
        new ClipAction("open")
        {
          Pose = ClipPose.Play,
          State = "doorOpen",
          When = 1f,
          By = ["cliptrace:toggle"],
          Seconds = 1f,
        }
      )
      .ClipAction(
        new ClipAction("closed")
        {
          Pose = ClipPose.Play,
          State = "doorOpen",
          When = 0f,
          By = ["cliptrace:toggle"],
          Seconds = 1f,
        }
      );

  private static ExBlockDef Spinner(
    string code = "spinner",
    string entity = "test-hatch",
    string state = "raised",
    float? when = null
  ) =>
    ExBlockDef
      .Create("cliptrace", code)
      .Shape("cliptrace:fan")
      .EntityClass(entity)
      .EntityBehavior("ClipActions")
      .ClipAction(
        new ClipAction("spin")
        {
          Pose = ClipPose.Run,
          State = state,
          When = when,
          Seconds = 1f,
        }
      );

  [Fact]
  public void A_play_pair_passes_every_clip_law()
  {
    BlockLaws.Result result = Run(Swing(), Spinner());

    Assert.All(result.Laws, law => Assert.Empty(law.Findings));
    Assert.Equal(3, result[ClipLaws.ActionName].Cases);
    Assert.Equal(3, result[ClipLaws.TimeName].Cases);
  }

  // Fails when a play clip starts at its last frame, which jumps the leaf to the open pose.
  [Fact]
  public void Play_clips_cross_without_a_jump()
  {
    TestWorld world = BlockLaws.Stand([Swing()], Exlib, Prepare);
    world.ClientAssets("cliptrace", Fixtures);
    Block block = world.World.Blocks.First(b => b?.Code?.Path == "gatedoor");
    ClipReference reference = ClipReference.Load(world, block);
    ClipAction[] actions = [.. ClipActions.Of(block).Resolve(reference.Shape)];

    foreach (ClipAction action in actions)
    {
      ClipTraced traced = new ClipTrace(world, "cliptrace").Trace(
        block,
        action,
        reference,
        actions.Single(a => a != action)
      );

      Assert.Empty(traced.Problems);
      float[] leaf =
      [
        .. traced.Forward!.Samples.Select(s => s.Poses["leaf"][1]),
      ];
      Assert.Equal(action.Clip == "open" ? 0f : 90f, leaf[0], 0.1f);
      Assert.InRange(
        leaf.Zip(leaf.Skip(1), (a, b) => Math.Abs(b - a)).Max(),
        0f,
        3 * 90f / 60f
      );
      Assert.Equal(60, traced.Forward.Arrived);
    }
  }

  // Fails when a clip is stopped with Stop: the rotor snaps from the middle of its turn to frame 0.
  [Fact]
  public void A_run_clip_stops_without_a_jump()
  {
    TestWorld world = BlockLaws.Stand([Spinner()], Exlib, Prepare);
    world.ClientAssets("cliptrace", Fixtures);
    Block block = world.World.Blocks.First(b => b?.Code?.Path == "spinner");
    ClipReference reference = ClipReference.Load(world, block);
    ClipAction action = ClipActions.Of(block).Resolve(reference.Shape).Single();

    ClipTraced traced = new ClipTrace(world, "cliptrace").Trace(
      block,
      action,
      reference
    );

    Assert.Empty(traced.Problems);
    float turned = traced.Forward!.Samples[^1].Poses["rotor"][1];
    Assert.InRange(turned, 100f, 200f);
    Assert.All(
      traced.Reverse!.Samples,
      s => Assert.InRange(s.Poses["rotor"][1], turned - 0.5f, turned + 8f)
    );
  }

  // Fails when the run rate is not the clip's frames over its seconds.
  [Fact]
  public void A_run_clip_that_cycles_in_another_time_misses_its_time()
  {
    TestWorld world = BlockLaws.Stand([Spinner()], Exlib, Prepare);
    world.ClientAssets("cliptrace", Fixtures);
    Block block = world.World.Blocks.First(b => b?.Code?.Path == "spinner");
    ClipReference reference = ClipReference.Load(world, block);
    ClipAction action = ClipActions.Of(block).Resolve(reference.Shape).Single();

    ClipTraced traced = new ClipTrace(world, "cliptrace").Trace(
      block,
      action with
      {
        Seconds = 2f,
      },
      reference
    );

    Assert.Single(traced.Problems);
    Assert.Equal(ClipLaws.TimeName, traced.Problems[0].Law);
    Assert.StartsWith(
      "run cycles in 1 s against 2 s",
      traced.Problems[0].Message
    );
  }

  // Fails when a run clip chosen by its when value is not run at its pace.
  [Fact]
  public void A_run_clip_picked_by_its_state_runs_and_holds_nothing_back()
  {
    BlockLaws.Result result = Run(Spinner("pickedspinner", when: 1f));

    Assert.All(result.Laws, law => Assert.Empty(law.Findings));
    Assert.Equal(1, result[ClipLaws.TimeName].Cases);
  }

  [Fact]
  public void A_run_clip_that_never_hears_its_state_plays_no_clip()
  {
    BlockLaws.Result result = Run(Spinner("deafspinner", state: "nokey"));

    Assert.Equal(
      ["cliptrace:fan spin | forward plays no clip | cliptrace:deafspinner"],
      result[ClipLaws.ActionName].Findings
    );
  }

  [Fact]
  public void A_run_clip_a_load_does_not_start_is_found()
  {
    BlockLaws.Result result = Run(Spinner("latespinner", "test-late"));

    Assert.Equal(
      [
        "cliptrace:fan spin | loaded running, it does not run on its first frame | cliptrace:latespinner",
      ],
      result[ClipLaws.ActionName].Findings
    );
  }

  [Fact]
  public void A_play_action_entered_in_its_own_state_changes_nothing()
  {
    BlockLaws.Result result = Run(
      Driven(
        "lonedoor",
        "test-door",
        "gatedoor",
        new ClipAction("closed")
        {
          Pose = ClipPose.Play,
          State = "doorOpen",
          When = 0f,
          Seconds = 1f,
        }
      )
    );

    Assert.Equal(
      [
        "cliptrace:gatedoor closed | the forward line '' changes nothing in the server's tree | cliptrace:lonedoor",
      ],
      result[ClipLaws.ActionName].Findings
    );
  }

  [Fact]
  public void A_play_action_whose_help_line_is_missing_is_found()
  {
    BlockLaws.Result result = Run(
      Driven(
        "linedoor",
        "test-door",
        "gatedoor",
        new ClipAction("open")
        {
          Pose = ClipPose.Play,
          State = "doorOpen",
          When = 1f,
          By = ["cliptrace:nothere"],
          Seconds = 1f,
        }
      )
    );

    Assert.Equal(
      [
        "cliptrace:gatedoor open | has no right-click help line 'cliptrace:nothere' for the forward action | cliptrace:linedoor",
      ],
      result[ClipLaws.ActionName].Findings
    );
  }

  private static ExBlockDef Rattle(string code) =>
    Leaf(code, "test-door", "rattledoor")
      .ClipAction(
        new ClipAction("shake") { Pose = ClipPose.Run, State = "shaking" }
      );

  private static string Exceptions(string json)
  {
    string file = Path.GetTempFileName();
    File.WriteAllText(file, json);
    return file;
  }

  private class DoorEntity : BlockEntity
  {
    public bool Open { get; set; }

    public virtual void Toggle()
    {
      Open = !Open;
      MarkDirty();
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
      base.ToTreeAttributes(tree);
      tree.SetBool("doorOpen", Open);
    }

    public override void FromTreeAttributes(
      ITreeAttribute tree,
      IWorldAccessor worldAccessForResolve
    )
    {
      base.FromTreeAttributes(tree, worldAccessForResolve);
      Open = tree.GetBool("doorOpen");
    }
  }

  private sealed class StuckDoor : DoorEntity
  {
    public override void Toggle() { }
  }

  private sealed class HatchEntity : BlockEntity
  {
    public float Raised { get; set; }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
      base.ToTreeAttributes(tree);
      tree.SetFloat("raised", Raised);
    }

    public override void FromTreeAttributes(
      ITreeAttribute tree,
      IWorldAccessor worldAccessForResolve
    )
    {
      base.FromTreeAttributes(tree, worldAccessForResolve);
      Raised = tree.GetFloat("raised");
    }
  }

  private sealed class LateHatch : BlockEntity
  {
    public float Raised { get; set; }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
      base.ToTreeAttributes(tree);
      tree.SetFloat("raised", Raised);
    }

    public override void FromTreeAttributes(
      ITreeAttribute tree,
      IWorldAccessor worldAccessForResolve
    )
    {
      Raised = tree.GetFloat("raised");
      if (Api != null)
        base.FromTreeAttributes(tree, worldAccessForResolve);
    }
  }

  private sealed class GateEntity : BlockEntity
  {
    public int Level { get; private set; }

    public void Step(int by)
    {
      Level = Math.Clamp(Level + by, 0, 3);
      MarkDirty();
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
      base.ToTreeAttributes(tree);
      tree.SetInt("level", Level);
    }

    public override void FromTreeAttributes(
      ITreeAttribute tree,
      IWorldAccessor worldAccessForResolve
    )
    {
      base.FromTreeAttributes(tree, worldAccessForResolve);
      Level = tree.GetInt("level");
    }
  }

  private sealed class ToggleBlock : Block
  {
    public override bool OnBlockInteractStart(
      IWorldAccessor world,
      IPlayer byPlayer,
      BlockSelection blockSel
    )
    {
      if (
        world.BlockAccessor.GetBlockEntity(blockSel.Position)
        is not DoorEntity door
      )
        return false;
      door.Toggle();
      return true;
    }

    public override WorldInteraction[] GetPlacedBlockInteractionHelp(
      IWorldAccessor world,
      BlockSelection selection,
      IPlayer forPlayer
    ) =>
      [
        new WorldInteraction
        {
          ActionLangCode = "cliptrace:toggle",
          MouseButton = EnumMouseButton.Right,
        },
      ];
  }

  private sealed class StepBlock : Block
  {
    public override bool OnBlockInteractStart(
      IWorldAccessor world,
      IPlayer byPlayer,
      BlockSelection blockSel
    )
    {
      if (
        world.BlockAccessor.GetBlockEntity(blockSel.Position)
        is not GateEntity gate
      )
        return false;
      gate.Step(byPlayer.Entity.Controls.Sneak ? -1 : 1);
      return true;
    }

    public override WorldInteraction[] GetPlacedBlockInteractionHelp(
      IWorldAccessor world,
      BlockSelection selection,
      IPlayer forPlayer
    ) =>
      [
        new WorldInteraction
        {
          ActionLangCode = "cliptrace:raise",
          MouseButton = EnumMouseButton.Right,
        },
        new WorldInteraction
        {
          ActionLangCode = "cliptrace:lower",
          MouseButton = EnumMouseButton.Right,
          HotKeyCode = "sneak",
        },
      ];
  }

  private abstract class HandLeaf : DoorEntity
  {
    private const float Last = 30f;

    private float _target;
    private float _frame;
    private bool _opened;

    protected HandLeaf() => Behaviors.Add(new BEBehaviorAnimatable(this));

    protected virtual float Speed => 1f;

    protected virtual float Reach => 1f;

    protected virtual float Rest => 0f;

    protected virtual bool SnapsShut => false;

    protected virtual bool FollowsTheSync => true;

    protected virtual bool ShowsTheLoadAtOnce => true;

    protected virtual string Key => "cliptrace-door";

    private BlockEntityAnimationUtil Util =>
      GetBehavior<BEBehaviorAnimatable>()!.animUtil;

    public override void Initialize(ICoreAPI api)
    {
      base.Initialize(api);
      if (api.Side != EnumAppSide.Client)
        return;
      Util.InitializeAnimator(Key);
      Aim();
      _frame = ShowsTheLoadAtOnce ? _target * Last : 0f;
      RegisterGameTickListener(Pose, 1);
    }

    public override void FromTreeAttributes(
      ITreeAttribute tree,
      IWorldAccessor worldAccessForResolve
    )
    {
      base.FromTreeAttributes(tree, worldAccessForResolve);
      if (Api == null || FollowsTheSync)
        Aim();
    }

    private void Aim()
    {
      _opened |= Open;
      _target = Open ? Reach : _opened ? 0f : Rest;
    }

    private void Pose(float dt)
    {
      BlockEntityAnimationUtil util = Util;
      if (util.animator?.GetAnimationState("open") is not { } state)
        return;
      float target = _target * Last;
      float step = Last * Speed * dt;
      _frame =
        _frame < target
          ? Math.Min(_frame + step, target)
          : Math.Max(_frame - step, target);
      if (SnapsShut && target == 0f)
        _frame = 0f;
      if (_frame == 0f && target == 0f)
      {
        if (util.activeAnimationsByAnimCode.ContainsKey("open"))
        {
          state.CurrentFrame = 0f;
          util.StopAnimation("open");
        }
        return;
      }
      if (!util.activeAnimationsByAnimCode.ContainsKey("open"))
        util.StartAnimation(ClipTrace.Hold("open", _frame));
      state.CurrentFrame = _frame;
      state.EasingFactor = 1f;
    }
  }

  private sealed class SnapDoor : HandLeaf
  {
    protected override bool SnapsShut => true;
  }

  private sealed class FastDoor : HandLeaf
  {
    protected override float Speed => 4f;
  }

  private sealed class DeafDoor : HandLeaf
  {
    protected override bool FollowsTheSync => false;
  }

  private sealed class QuietDoor : HandLeaf
  {
    public override void Toggle() => Open = !Open;
  }

  private sealed class JumpLoadDoor : HandLeaf
  {
    protected override bool ShowsTheLoadAtOnce => false;
  }

  private sealed class HalfDoor : HandLeaf
  {
    protected override float Reach => 0.5f;
  }

  private sealed class ShortSlider : HandLeaf
  {
    protected override float Reach => 1f - 0.5f / 16f;
  }

  private class MidDoor : HandLeaf
  {
    protected override float Rest => 0.5f;
  }

  private sealed class MidFastDoor : MidDoor
  {
    protected override float Speed => 2f;
  }

  private sealed class KeyDoor : HandLeaf { }

  private sealed class ThrowingDoor : HandLeaf
  {
    public override void Initialize(ICoreAPI api)
    {
      base.Initialize(api);
      if (api.Side == EnumAppSide.Client)
        throw new InvalidOperationException("the twin cannot stand");
    }
  }

  private sealed class UnbuiltDoor : BlockEntity
  {
    public UnbuiltDoor() => Behaviors.Add(new BEBehaviorAnimatable(this));
  }

  private sealed class RenderThrowDoor : BlockEntity
  {
    public override void Initialize(ICoreAPI api)
    {
      base.Initialize(api);
      if (api is ICoreClientAPI capi)
        capi.Event.RegisterRenderer(
          new ThrowingRenderer(),
          EnumRenderStage.Opaque,
          "throws"
        );
    }
  }

  private sealed class ThrowingRenderer : IRenderer
  {
    public double RenderOrder => 0;

    public int RenderRange => 0;

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage) =>
      throw new InvalidOperationException("the renderer threw");

    public void Dispose() { }
  }
}
#endif
