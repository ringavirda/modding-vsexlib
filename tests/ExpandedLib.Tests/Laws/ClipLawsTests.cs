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
    world.RegisterClass("test-gate", typeof(GateEntity));
    world.RegisterClass("test-stuck", typeof(StuckDoor));
    world.RegisterClass("test-snap", typeof(SnapDoor));
    world.RegisterClass("test-fast", typeof(FastDoor));
    world.RegisterClass("test-deaf", typeof(DeafDoor));
    world.RegisterClass("test-quiet", typeof(QuietDoor));
    world.RegisterClass("test-jump", typeof(JumpLoadDoor));
    world.RegisterClass("test-half", typeof(HalfDoor));
    world.RegisterClass("test-key", typeof(KeyDoor));
    world.RegisterClass("test-throws", typeof(ThrowingDoor));
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

    protected HandLeaf() => Behaviors.Add(new BEBehaviorAnimatable(this));

    protected virtual float Speed => 1f;

    protected virtual float Reach => 1f;

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
      _target = Open ? Reach : 0f;
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
        _target = Open ? Reach : 0f;
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
}
#endif
