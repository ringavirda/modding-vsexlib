#if GAME_GE_1_22
using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Blocks;
using ExpandedLib.Helpers;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ExpandedLib.Testing;

/// <summary>One finding of a clip law before its variants are gathered: the law, the key a known
/// row names, and what is wrong.</summary>
internal sealed record ClipProblem(string Law, string Key, string Message);

/// <summary>The twin's state after one frame step: the clip's frame and blend weight, whether it is
/// active, and each traced part's pose.</summary>
internal sealed record ClipSample(
  int Tick,
  float Frame,
  float Weight,
  bool Active,
  IReadOnlyDictionary<string, float[]> Poses
);

/// <summary>The samples of one action worked one way: the pose before the first step, then one per
/// step until the parts had settled for five steps or the wait ran out.</summary>
/// <param name="Played">Whether the clip was active at any step.</param>
/// <param name="Arrived">The step at which the moving parts reached their end pose and stayed;
/// null when they did not.</param>
internal sealed record ClipWay(
  IReadOnlyList<ClipSample> Samples,
  bool Played,
  int? Arrived
);

/// <summary>What tracing one action found, and the ways it was worked.</summary>
/// <param name="Timed">Ways judged for time.</param>
internal sealed record ClipTraced(
  IReadOnlyList<ClipProblem> Problems,
  ClipWay? Forward,
  ClipWay? Reverse,
  int Timed
);

/// <summary>Works a block's declared frame actions on a client twin and reads the poses its
/// animator gives; see <see cref="ClipLaws"/>.</summary>
internal sealed class ClipTrace
{
  /// <summary>Seconds one frame step advances the client clock.</summary>
  internal const float Dt = 1f / 60f;

  private const float Degrees = 0.5f;
  // Poses carry translations in blocks (offset / 16), so 0.05 voxel is 0.05 / 16.
  private const float Voxels = 0.05f / 16f;
  private const int SettledSteps = 5;
  private const int MaxClicks = 64;
  private const string CacheName = "animUtil-animCache";
  private const string NoAnimator = "has no animator after its first frame";

  private readonly TestWorld _world;
  private readonly string _domain;
  private readonly BlockLaws.Sites _sites = new();
  private readonly Block _solid;
  private readonly TestPlayer _player;

  internal ClipTrace(TestWorld world, string domain)
  {
    _world = world;
    _domain = domain;
    _solid = BlockLaws.Solid(world);
    _player = InteractionLaw.User(world);
  }

  /// <summary>The metadata that holds <paramref name="clip"/> at <paramref name="frame"/>: it starts
  /// once at that frame, advancing a thousandth of real time, with instant eases.</summary>
  internal static AnimationMetaData Hold(string clip, float frame) =>
    new AnimationMetaData
    {
      Code = clip,
      Animation = clip,
      AnimationSpeed = 0.001f,
      EaseInSpeed = 6000f,
      EaseOutSpeed = 6000f,
      StartFrameOnce = frame,
    }.Init();

  internal static float[] PoseOf(AnimatorBase animator, string part)
  {
    ElementPose pose = animator.GetPosebyName(part);
    return
    [
      pose.degX,
      pose.degY,
      pose.degZ,
      pose.translateX,
      pose.translateY,
      pose.translateZ,
    ];
  }

  /// <summary>Traces the declared frame, play and run actions of every variant in the domain.</summary>
  /// <returns>Blocktypes traced, actions traced, and ways judged for time.</returns>
  internal (int Types, int Actions, int Timed) Actions(
    List<(string Code, ClipProblem Problem)> found
  )
  {
    int types = 0,
      actions = 0,
      timed = 0;
    foreach (
      IGrouping<string, Block> type in BlockLaws.Blocktypes(_world, _domain)
    )
    {
      bool traced = false;
      foreach (Block block in type)
      {
        ClipActions declared = ClipActions.Of(block);
        if (declared.Declared.Count == 0)
          continue;
        ClipReference reference = ClipReference.Load(_world, block);
        if (!declared.Declared.All(a => reference.HasClip(a.Clip)))
          continue;
        IReadOnlyList<ClipAction> resolved = declared.Resolve(reference.Shape);
        foreach (ClipAction action in resolved)
        {
          ClipAction? from = null;
          if (action.Pose == ClipPose.Play)
            from = resolved.FirstOrDefault(a =>
              a != action
              && a.Pose == ClipPose.Play
              && a.State != null
              && a.State == action.State
            );
          else if (action.Pose != ClipPose.Frame && action.Pose != ClipPose.Run)
            continue;
          traced = true;
          actions++;
          ClipTraced result = Trace(block, action, reference, from);
          timed += result.Timed;
          found.AddRange(
            result.Problems.Select(p => (block.Code.ToString(), p))
          );
        }
      }
      if (traced)
        types++;
    }
    return (types, actions, timed);
  }

  /// <summary>Compares the clips each variant's shape carries with the clips its actions name.</summary>
  /// <param name="found">Receives a finding for each clip no action drives and is not a static row, and for
  /// each action naming a clip the shape lacks.</param>
  /// <param name="exceptions">The file whose static rows excuse a clip from having a driver.</param>
  /// <returns>Blocktypes whose shape carries clips, clips judged across their variants, and the keys of
  /// every clip no action drives, static or not.</returns>
  internal (int Types, int Clips, HashSet<string> Undriven) Drivers(
    List<(string Code, ClipProblem Problem)> found,
    ClipExceptions exceptions
  )
  {
    var clipsByShape = new Dictionary<string, string[]>();
    var undriven = new HashSet<string>(StringComparer.Ordinal);
    int types = 0,
      judged = 0;
    foreach (
      IGrouping<string, Block> type in BlockLaws.Blocktypes(_world, _domain)
    )
    {
      bool carries = false;
      foreach (Block block in type)
      {
        if (block.Shape?.Base == null)
          continue;
        string shape = block.Shape.Base.ToString();
        if (!clipsByShape.TryGetValue(shape, out string[]? clips))
          clipsByShape[shape] = clips = [
            .. ExMeshCache
              .LoadShape(_world.ClientApi, ExMeshCache.ShapePathOf(block))
              ?.Animations?.Select(a => a.Code)
              ?? [],
          ];
        if (clips.Length == 0)
          continue;
        carries = true;
        judged += clips.Length;
        string[] named =
        [
          .. ClipActions.Of(block).Declared.Select(a => a.Clip),
        ];
        foreach (
          string ghost in named.Where(n =>
            !clips.Contains(n, StringComparer.OrdinalIgnoreCase)
          )
        )
          found.Add(
            (
              block.Code.ToString(),
              new ClipProblem(
                ClipLaws.DriverName,
                $"{shape} {ghost}",
                "names a clip the shape does not have"
              )
            )
          );
        foreach (
          string clip in clips.Where(c =>
            !named.Contains(c, StringComparer.OrdinalIgnoreCase)
          )
        )
        {
          string key = $"{shape} {clip}";
          undriven.Add(key);
          if (!exceptions.Static.ContainsKey(key))
            found.Add(
              (
                block.Code.ToString(),
                new ClipProblem(
                  ClipLaws.DriverName,
                  key,
                  "no action drives the clip"
                )
              )
            );
        }
      }
      if (carries)
        types++;
    }
    return (types, judged, undriven);
  }

  /// <summary>Stands every animated variant's twin on one shared animator cache, in code order,
  /// and adds a finding for each cache key serving more than one shape.</summary>
  /// <returns>Blocktypes with an animated variant, and twins stood.</returns>
  internal (int Types, int Twins) Keys(
    List<(string Code, ClipProblem Problem)> found
  )
  {
    ClearCache();
    var built = new List<(string Code, string Shape, string Key)>();
    int types = 0;
    foreach (
      IGrouping<string, Block> type in BlockLaws.Blocktypes(_world, _domain)
    )
    {
      bool animated = false;
      foreach (Block block in type)
      {
        BlockLaws.EntityCase? step = InteractionLaw.StandUp(
          _world,
          block,
          _sites.Next(),
          _solid,
          null,
          null
        );
        if (
          step?.Entity is not { } entity
          || (
            entity.GetBehavior<BEBehaviorAnimatable>() == null
            && ClipActions.Of(block).Declared.Count == 0
          )
        )
          continue;
        animated = true;
        ClientTwin? twin = null;
        try
        {
          twin = ClientTwin.Stand(_world, entity)[0];
          twin.Frames(Dt, 1);
          if (twin.Animator is not { } animator)
            found.Add(
              (
                block.Code.ToString(),
                new ClipProblem(
                  ClipLaws.KeyName,
                  $"{block.Shape.Base} twin",
                  NoAnimator
                )
              )
            );
          else
            built.Add(
              (block.Code.ToString(), ShapeOf(block), CacheKeyOf(animator))
            );
        }
        catch (Exception e)
        {
          found.Add(
            (
              block.Code.ToString(),
              new ClipProblem(
                ClipLaws.KeyName,
                $"{block.Shape.Base} twin",
                $"threw {BlockLaws.Describe(e)}"
              )
            )
          );
        }
        finally
        {
          if (twin != null)
            _world.ReleaseClientTwin(twin.Twin);
        }
      }
      if (animated)
        types++;
    }

    foreach (
      var shared in built
        .GroupBy(b => b.Key)
        .Where(g => g.Select(b => b.Shape).Distinct().Count() > 1)
        .GroupBy(g =>
          string.Join(
            " + ",
            g.Select(b => b.Shape).Distinct().Order(StringComparer.Ordinal)
          )
        )
    )
    {
      string keys = string.Join(", ", shared.Select(g => g.Key).Order());
      foreach (
        string code in shared.SelectMany(g => g).Select(b => b.Code).Distinct()
      )
        found.Add(
          (
            code,
            new ClipProblem(
              ClipLaws.KeyName,
              shared.Key,
              $"share the animator key {keys}"
            )
          )
        );
    }
    return (types, built.Count);
  }

  /// <summary>Works <paramref name="action"/> on a fresh twin of <paramref name="block"/>, then loads a
  /// twin with the state already set, and judges each. A frame action is worked both ways; a play action
  /// is entered from <paramref name="from"/>; a run action is run and stopped.</summary>
  /// <param name="from">For a play action, the action of its group the block rests in before; null enters
  /// from the clip's own first frame.</param>
  internal ClipTraced Trace(
    Block block,
    ClipAction action,
    ClipReference reference,
    ClipAction? from = null
  )
  {
    if (action.Pose == ClipPose.Run)
      return TraceRun(block, action, reference);
    bool play = action.Pose == ClipPose.Play;
    string shape = block.Shape.Base.ToString();
    string key = $"{shape} {action.Clip}";
    var problems = new List<ClipProblem>();
    void Add(string law, string message) =>
      problems.Add(new ClipProblem(law, key, message));
    ClipWay? forward = null,
      reverse = null;
    int timed = 0;
    try
    {
      string[] parts = [.. action.Parts!.Where(reference.HasPart)];
      foreach (string ghost in action.Parts!.Except(parts))
        Add(
          ClipLaws.ActionName,
          $"names the part '{ghost}', which the shape does not have"
        );
      float seconds = action.Seconds!.Value;
      float last = reference.Last(action.Clip);
      var start =
        play && from != null
          ? reference.Pose(from.Clip, reference.Last(from.Clip), parts)
          : reference.Pose(action.Clip, 0f, parts);
      var end = reference.Pose(action.Clip, last, parts);
      List<(string Part, int Channel)> moving = Moving(start, end);
      if (moving.Count == 0)
      {
        Add(
          ClipLaws.ActionName,
          "moves no part between its first and last frame"
        );
        return new ClipTraced(problems, null, null, 0);
      }

      BlockLaws.EntityCase step = Place(block);
      ClientTwin twin = StandTwin(step, 2);
      try
      {
        if (twin.Animator == null)
        {
          Add(ClipLaws.ActionName, NoAnimator);
          return new ClipTraced(problems, null, null, 0);
        }
        twin.Sync();
        if (play && from != null)
        {
          Work(step, from, true);
          twin.Sync();
          twin.Frames(Dt, Settle(from));
        }
        if (!Worked(step, twin, action, true, Add))
          return new ClipTraced(problems, null, null, 0);
        forward = Walk(twin, action, parts, end, moving, seconds);
        if (!forward.Played)
        {
          Add(ClipLaws.ActionName, "forward plays no clip");
          return new ClipTraced(problems, forward, null, 0);
        }
        timed += Judge("forward", forward, seconds, last, moving, start, end, Add);
        if (!play)
        {
          if (!Worked(step, twin, action, false, Add))
            return new ClipTraced(problems, forward, null, timed);
          reverse = Walk(twin, action, parts, start, moving, seconds);
          timed += Judge("reverse", reverse, seconds, last, moving, end, start, Add);
        }
      }
      finally
      {
        _world.ReleaseClientTwin(twin.Twin);
      }

      BlockLaws.EntityCase loadStep = Place(block);
      Work(loadStep, action, true);
      ClientTwin loaded = StandTwin(loadStep, 1);
      try
      {
        if (!Near(Poses(loaded, parts), end, moving))
          Add(
            ClipLaws.ActionName,
            "loaded at its far end, it does not show the end pose on its first frame"
          );
      }
      finally
      {
        _world.ReleaseClientTwin(loaded.Twin);
      }
    }
    catch (Exception e)
    {
      problems.Add(
        new ClipProblem(
          ClipLaws.ActionName,
          $"{shape} twin",
          $"threw {BlockLaws.Describe(e)}"
        )
      );
    }
    return new ClipTraced(problems, forward, reverse, timed);
  }

  private ClipTraced TraceRun(
    Block block,
    ClipAction action,
    ClipReference reference
  )
  {
    string shape = block.Shape.Base.ToString();
    string key = $"{shape} {action.Clip}";
    var problems = new List<ClipProblem>();
    void Add(string law, string message) =>
      problems.Add(new ClipProblem(law, key, message));
    ClipWay? forward = null,
      reverse = null;
    int timed = 0;
    try
    {
      string[] parts = [.. action.Parts!.Where(reference.HasPart)];
      foreach (string ghost in action.Parts!.Except(parts))
        Add(
          ClipLaws.ActionName,
          $"names the part '{ghost}', which the shape does not have"
        );
      float seconds = action.Seconds!.Value;
      var start = reference.Pose(action.Clip, 0f, parts);
      var end = reference.Pose(action.Clip, reference.Last(action.Clip), parts);
      List<(string Part, int Channel)> moving = Moving(start, end);
      if (moving.Count == 0)
      {
        Add(
          ClipLaws.ActionName,
          "moves no part between its first and last frame"
        );
        return new ClipTraced(problems, null, null, 0);
      }

      BlockLaws.EntityCase step = Place(block);
      ClientTwin twin = StandTwin(step, 2);
      try
      {
        if (twin.Animator == null)
        {
          Add(ClipLaws.ActionName, NoAnimator);
          return new ClipTraced(problems, null, null, 0);
        }
        twin.Sync();
        if (!Worked(step, twin, action, true, Add))
          return new ClipTraced(problems, null, null, 0);
        forward = WalkFor(
          twin,
          action,
          parts,
          Math.Max(6, (int)Math.Ceiling(0.4f * seconds / Dt))
        );
        if (!forward.Played)
        {
          Add(ClipLaws.ActionName, "forward plays no clip");
          return new ClipTraced(problems, forward, null, 0);
        }
        timed += JudgeRate(
          forward,
          reference.Last(action.Clip) + 1,
          seconds,
          Add
        );
        if (action.When == null)
        {
          if (!Worked(step, twin, action, false, Add))
            return new ClipTraced(problems, forward, null, timed);
          reverse = WalkFor(twin, action, parts, 2 * SettledSteps);
          if (Jumps(reverse, moving, start, end, seconds))
            Add(
              ClipLaws.ActionName,
              "stopping moves a part more than three even steps in one tick"
            );
        }
      }
      finally
      {
        _world.ReleaseClientTwin(twin.Twin);
      }

      BlockLaws.EntityCase loadStep = Place(block);
      Work(loadStep, action, true);
      ClientTwin loaded = StandTwin(loadStep, 2);
      try
      {
        if (
          loaded.Animator?.GetAnimationState(action.Clip)
          is not { Active: true }
        )
          Add(
            ClipLaws.ActionName,
            "loaded running, it does not run on its first frame"
          );
      }
      finally
      {
        _world.ReleaseClientTwin(loaded.Twin);
      }
    }
    catch (Exception e)
    {
      problems.Add(
        new ClipProblem(
          ClipLaws.ActionName,
          $"{shape} twin",
          $"threw {BlockLaws.Describe(e)}"
        )
      );
    }
    return new ClipTraced(problems, forward, reverse, timed);
  }

  private static int Settle(ClipAction action) =>
    (int)Math.Ceiling((2 * action.Seconds!.Value + 1) / Dt) + SettledSteps;

  private static int JudgeRate(
    ClipWay walked,
    float quantity,
    float seconds,
    Action<string, string> add
  )
  {
    ClipSample first = walked.Samples[1];
    ClipSample last = walked.Samples[^1];
    float rate = (last.Frame - first.Frame) / ((last.Tick - first.Tick) * Dt);
    float time = rate > 0f ? quantity / rate : float.PositiveInfinity;
    if (Math.Abs(time - seconds) > Math.Max(0.1f * seconds, 2 * Dt))
      add(
        ClipLaws.TimeName,
        $"run cycles in {time:0.##} s against {seconds:0.##} s"
      );
    return 1;
  }

  private ClipWay WalkFor(
    ClientTwin twin,
    ClipAction action,
    string[] parts,
    int ticks
  )
  {
    var samples = new List<ClipSample> { Sample(twin, action, parts, 0) };
    for (int tick = 1; tick <= ticks; tick++)
    {
      twin.Frames(Dt, 1);
      samples.Add(Sample(twin, action, parts, tick));
    }
    return new ClipWay(samples, samples.Any(s => s.Active), null);
  }

  private bool Worked(
    BlockLaws.EntityCase step,
    ClientTwin twin,
    ClipAction action,
    bool forward,
    Action<string, string> add
  )
  {
    string way = forward ? "forward" : "reverse";
    Effect work = Work(step, action, forward);
    if (work == Effect.NoLine)
    {
      add(
        ClipLaws.ActionName,
        $"has no right-click help line '{Line(action, forward)}' for the {way} action"
      );
      return false;
    }
    if (work == Effect.Unchanged)
    {
      add(
        ClipLaws.ActionName,
        $"the {way} line '{Line(action, forward)}' changes nothing in the server's tree"
      );
      return false;
    }
    if (!twin.Sync())
    {
      add(
        ClipLaws.ActionName,
        "changes its tree and never marks the entity dirty, so the client is not told"
      );
      return false;
    }
    return true;
  }

  private int Judge(
    string way,
    ClipWay walked,
    float seconds,
    float last,
    List<(string Part, int Channel)> moving,
    IReadOnlyDictionary<string, float[]> from,
    IReadOnlyDictionary<string, float[]> to,
    Action<string, string> add
  )
  {
    if (Jumps(walked, moving, from, to, seconds))
      add(
        ClipLaws.ActionName,
        $"{way} moves a part more than three even steps in one tick"
      );
    if (walked.Arrived is not { } arrived)
    {
      add(ClipLaws.ActionName, $"{way} ends away from the clip's end pose");
      return 0;
    }
    bool forward = way == "forward";
    ClipSample begin = walked.Samples[0];
    ClipSample finish = walked.Samples[^1];
    float f0 = begin.Active ? begin.Frame : forward ? 0f : last;
    float f1 = finish.Active ? finish.Frame : forward ? last : 0f;
    float expected = seconds * Math.Abs(f1 - f0) / last;
    float time = arrived * Dt;
    if (Math.Abs(time - expected) > Math.Max(0.1f * expected, 2 * Dt))
      add(
        ClipLaws.TimeName,
        $"{way} settles in {time:0.##} s against {expected:0.##} s"
      );
    return 1;
  }

  private static bool Jumps(
    ClipWay walked,
    List<(string Part, int Channel)> moving,
    IReadOnlyDictionary<string, float[]> from,
    IReadOnlyDictionary<string, float[]> to,
    float seconds
  )
  {
    for (int i = 1; i < walked.Samples.Count; i++)
      foreach ((string part, int channel) in moving)
      {
        float even =
          Math.Abs(to[part][channel] - from[part][channel]) * Dt / seconds;
        float moved = Math.Abs(
          walked.Samples[i].Poses[part][channel]
            - walked.Samples[i - 1].Poses[part][channel]
        );
        if (moved > 3 * even + 1e-3f)
          return true;
      }
    return false;
  }

  private ClipWay Walk(
    ClientTwin twin,
    ClipAction action,
    string[] parts,
    IReadOnlyDictionary<string, float[]> to,
    List<(string Part, int Channel)> moving,
    float seconds
  )
  {
    var samples = new List<ClipSample> { Sample(twin, action, parts, 0) };
    int wait = (int)Math.Ceiling((2 * seconds + 1) / Dt) + SettledSteps;
    int near = 0;
    int? arrived = null;
    for (int tick = 1; tick <= wait && arrived == null; tick++)
    {
      twin.Frames(Dt, 1);
      samples.Add(Sample(twin, action, parts, tick));
      near = Near(samples[^1].Poses, to, moving) ? near + 1 : 0;
      if (near == SettledSteps)
        arrived = tick - SettledSteps + 1;
    }
    return new ClipWay(samples, samples.Any(s => s.Active), arrived);
  }

  private static ClipSample Sample(
    ClientTwin twin,
    ClipAction action,
    string[] parts,
    int tick
  )
  {
    RunningAnimation state = twin.Animator!.GetAnimationState(action.Clip);
    return new ClipSample(
      tick,
      state.CurrentFrame,
      state.BlendedWeight,
      state.Active,
      Poses(twin, parts)
    );
  }

  private static Dictionary<string, float[]> Poses(
    ClientTwin twin,
    IEnumerable<string> parts
  ) => parts.ToDictionary(p => p, p => PoseOf(twin.Animator!, p));

  private static bool Near(
    IReadOnlyDictionary<string, float[]> poses,
    IReadOnlyDictionary<string, float[]> target,
    List<(string Part, int Channel)> moving
  ) =>
    moving.All(m =>
      Math.Abs(poses[m.Part][m.Channel] - target[m.Part][m.Channel])
      <= Tolerance(m.Channel)
    );

  private static List<(string Part, int Channel)> Moving(
    IReadOnlyDictionary<string, float[]> start,
    IReadOnlyDictionary<string, float[]> end
  ) =>
    [
      .. start.Keys.SelectMany(part =>
        Enumerable
          .Range(0, 6)
          .Where(c => Math.Abs(end[part][c] - start[part][c]) > Tolerance(c))
          .Select(c => (part, c))
      ),
    ];

  private static float Tolerance(int channel) => channel < 3 ? Degrees : Voxels;

  private BlockLaws.EntityCase Place(Block block) =>
    InteractionLaw.StandUp(_world, block, _sites.Next(), _solid, null, null)
    ?? throw new InvalidOperationException(
      $"{block.Code} could not be placed."
    );

  private ClientTwin StandTwin(BlockLaws.EntityCase step, int frames)
  {
    ClearCache();
    ClientTwin twin = ClientTwin.Stand(
      _world,
      step.Entity
        ?? throw new InvalidOperationException(
          $"{step.Block.Code} has no block entity."
        )
    )[0];
    try
    {
      twin.Frames(Dt, frames);
    }
    catch
    {
      _world.ReleaseClientTwin(twin.Twin);
      throw;
    }
    return twin;
  }

  private void ClearCache() => _world.ClientApi.ObjectCache.Remove(CacheName);

  private string CacheKeyOf(AnimatorBase animator) =>
    (
      (Dictionary<string, AnimCacheEntry>)
        _world.ClientApi.ObjectCache[CacheName]
    )
      .Single(e => ReferenceEquals(e.Value.RootPoses, animator.RootPoses))
      .Key;

  private static string ShapeOf(Block block) =>
    string.Join(
      "+",
      new[] { block.Shape.Base.ToString() }.Concat(
        block.Shape.Overlays?.Select(o => o.Base.ToString()) ?? []
      )
    );

  private static string Line(ClipAction action, bool forward) =>
    action.By is { Length: > 0 } by
      ? by[by.Length == 1 || forward ? 0 : 1]
      : "";

  private enum Effect
  {
    Changed,
    Unchanged,
    NoLine,
  }

  private Effect Work(
    BlockLaws.EntityCase step,
    ClipAction action,
    bool forward
  )
  {
    BlockEntity entity = step.Entity!;
    if (action.Pose == ClipPose.Play || action.When != null)
      return Enter(step, action);
    if (action.By is not { Length: > 0 } by)
    {
      (float low, float high) = action.Range ?? (0f, 1f);
      WriteState(entity, action.State!, forward ? high : low);
      return Effect.Changed;
    }
    InteractionLaw.Click? click = ClickFor(step, Line(action, forward));
    if (click == null)
      return Effect.NoLine;
    bool changed = false;
    for (int i = 0; i < (by.Length > 1 ? MaxClicks : 1); i++)
    {
      byte[] before = TreeOf(step.Entity!);
      InteractionLaw.Press(_world, _player, step, click, judged: false);
      if (before.AsSpan().SequenceEqual(TreeOf(step.Entity!)))
        break;
      changed = true;
    }
    return changed ? Effect.Changed : Effect.Unchanged;
  }

  private Effect Enter(BlockLaws.EntityCase step, ClipAction action)
  {
    BlockEntity entity = step.Entity!;
    float when = action.When ?? 1f;
    if (
      action.State is { } state
      && Math.Abs(StateOf(entity, state) - when) < 0.001f
    )
      return Effect.Unchanged;
    if (action.By is not { Length: > 0 })
    {
      if (action.State == null)
        return Effect.NoLine;
      WriteState(entity, action.State, when);
      return Effect.Changed;
    }
    InteractionLaw.Click? click = ClickFor(step, Line(action, true));
    if (click == null)
      return Effect.NoLine;
    byte[] before = TreeOf(entity);
    InteractionLaw.Press(_world, _player, step, click, judged: false);
    return before.AsSpan().SequenceEqual(TreeOf(entity))
      ? Effect.Unchanged
      : Effect.Changed;
  }

  private static float StateOf(BlockEntity entity, string key)
  {
    var tree = new TreeAttribute();
    entity.ToTreeAttributes(tree);
    return tree[key] is BoolAttribute
      ? (tree.GetAsBool(key) ? 1f : 0f)
      : (float)tree.GetDecimal(key);
  }

  private void WriteState(BlockEntity entity, string key, float value)
  {
    var tree = new TreeAttribute();
    entity.ToTreeAttributes(tree);
    switch (tree[key])
    {
      case BoolAttribute:
        tree.SetBool(key, value > 0.5f);
        break;
      case IntAttribute:
        tree.SetInt(key, (int)Math.Round(value));
        break;
      default:
        tree.SetFloat(key, value);
        break;
    }
    entity.FromTreeAttributes(tree, _world.World);
    entity.MarkDirty();
  }

  private static byte[] TreeOf(BlockEntity entity)
  {
    var tree = new TreeAttribute();
    entity.ToTreeAttributes(tree);
    return tree.ToBytes();
  }

  private InteractionLaw.Click? ClickFor(BlockLaws.EntityCase step, string line)
  {
    BlockSelection selection = InteractionLaw.Selection(step.At);
    WorldInteraction? help = step.Help(step.At, selection, _player.Player)
      .FirstOrDefault(h =>
        h.MouseButton == EnumMouseButton.Right && h.ActionLangCode == line
      );
    return help == null ? null : InteractionLaw.ClickOf(help, selection);
  }
}
#endif
