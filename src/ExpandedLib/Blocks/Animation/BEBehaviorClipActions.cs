using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Helpers;
using ExpandedLib.Registries;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ExpandedLib.Blocks;

/// <summary>Animates a block from the clip actions it declares: on the client it builds the block's
/// animator, moves each <see cref="ClipPose.Frame"/> clip toward the target its synced state sets,
/// plays the <see cref="ClipPose.Play"/> clip of the state the block is in and runs the
/// <see cref="ClipPose.Run"/> clip at the rate its state gives.
/// A <see cref="BEBehaviorAnimatable"/>, so code that looks that behaviour up finds it.</summary>
/// <remarks>Registered as <c>ClipActions</c>. The animator is built on the first client frame and
/// again whenever the entity's block changes, unless a <see cref="ConstructedAnimator"/> has
/// <see cref="Claim"/>ed the build. The synced tree is read on every client
/// <c>FromTreeAttributes</c>, so a state key written on the server shows on the client with no entity
/// code. A block with the behaviour and no declaration gets its animator and plays nothing until its
/// entity calls <see cref="SetTarget"/>, <see cref="Play"/> or <see cref="Run"/>.
/// A play clip starts at its first frame and moves to its last in the action's seconds while the
/// group's other clips stop, so a pair of play clips must meet: the end pose of one is the start pose
/// of the other. A block loaded in a state shows that state's clip at its last frame.</remarks>
[BlockEntityBehaviorRegister("ClipActions", PrefixModId = false)]
public class BEBehaviorClipActions(BlockEntity blockentity)
  : BEBehaviorAnimatable(blockentity) {
  // The animation speed and ease speeds of a clip whose frames the mover sets.
  private const float HoldSpeed = 0.001f;
  private const float HoldEase = 6000f;

  private readonly Dictionary<string, float> _targets = new(
    StringComparer.OrdinalIgnoreCase
  );
  private readonly Dictionary<string, float> _frames = new(
    StringComparer.OrdinalIgnoreCase
  );
  private readonly Dictionary<string, float> _pending = new(
    StringComparer.OrdinalIgnoreCase
  );
  private readonly Dictionary<string, float> _rates = new(
    StringComparer.OrdinalIgnoreCase
  );
  private readonly Dictionary<string, (string Clip, ClipPose Pose)> _active =
  [];
  private readonly Dictionary<string, string> _shown = [];
  private Dictionary<string, ClipAction> _resolved = new(
    StringComparer.OrdinalIgnoreCase
  );
  private ClipActions _declared = ClipActions.None;
  private Block? _declaredFor;
  private string? _declaredError;
  private Block? _builtFor;
  private bool _claimed;
  private AnimatorBase? _posed;
  private MoverRenderer? _mover;

  /// <summary>Sets how far a clip is shown, which the clip then moves to at its action's
  /// <see cref="ClipAction.Seconds"/>, or at the clip's frames over 30 a second when no action
  /// declares it.</summary>
  /// <remarks>Meaningful on the client; on the server the target is kept and never drawn. A clip the
  /// shape lacks is ignored, as is one whose declared pose is not <see cref="ClipPose.Frame"/>.</remarks>
  /// <param name="clip">The animation's code.</param>
  /// <param name="fraction">0 for the clip's first frame to 1 for its last; clamped.</param>
  public void SetTarget(string clip, float fraction) =>
    _targets[clip] = GameMath.Clamp(fraction, 0f, 1f);

  /// <summary>Starts the clip of a play group, which stops the group's other clips: those whose
  /// actions share its <see cref="ClipAction.State"/>, or the clip alone when it declares none.</summary>
  /// <remarks>Meaningful on the client; on the server the choice is kept and never drawn. A clip the
  /// shape lacks is ignored, as is one whose declared pose is not <see cref="ClipPose.Play"/>. A synced
  /// state that sets the clip's group replaces the choice.</remarks>
  /// <param name="clip">The animation's code.</param>
  public void Play(string clip) => Choose(clip, ClipPose.Play);

  /// <summary>Starts the clip repeating at <paramref name="rate"/> times its action's pace, which stops
  /// the other clips of its group.</summary>
  /// <remarks>Meaningful on the client; on the server the choice is kept and never drawn. A clip the
  /// shape lacks is ignored, as is one whose declared pose is not <see cref="ClipPose.Run"/>. A rate
  /// of 0 holds the clip where it is, without a jump. The pace is one cycle in the action's
  /// <see cref="ClipAction.Seconds"/>, or the clip's frames over 30 a second when no action declares
  /// it.</remarks>
  /// <param name="clip">The animation's code.</param>
  /// <param name="rate">Multiple of the pace, 0 or more; below 0 is 0.</param>
  public void Run(string clip, float rate) {
    _rates[clip] = Math.Max(rate, 0f);
    Choose(clip, ClipPose.Run);
  }

  /// <summary>Leaves the animator build to the caller, which builds and rebuilds it on this block
  /// entity's animation utility; the behaviour poses what the utility holds and never builds.</summary>
  /// <remarks>Call from the block entity's <c>Initialize</c>, before the first client frame.</remarks>
  public void Claim() => _claimed = true;

  public override void Initialize(ICoreAPI api, JsonObject properties) {
    base.Initialize(api, properties);
    if (api is not ICoreClientAPI capi)
      return;
    _mover = new MoverRenderer(this);
    capi.Event.RegisterRenderer(_mover, EnumRenderStage.Before, "clipactions");
  }

  public override void FromTreeAttributes(
    ITreeAttribute tree,
    IWorldAccessor worldAccessForResolve
  ) {
    base.FromTreeAttributes(tree, worldAccessForResolve);
    foreach (ClipAction action in Declared().Declared) {
      if (action.State is not { } key || !tree.HasAttribute(key))
        continue;
      float value =
        tree[key] is BoolAttribute
          ? (tree.GetAsBool(key) ? 1f : 0f)
          : (float)tree.GetDecimal(key);
      (float low, float high) = action.Range ?? (0f, 1f);
      SetTarget(action.Clip, (value - low) / (high - low));
      _pending[action.Clip] = value;
    }
  }

  public override void OnBlockUnloaded() {
    base.OnBlockUnloaded();
    Release();
  }

  public override void OnBlockBroken(IPlayer? byPlayer = null) {
    base.OnBlockBroken(byPlayer);
    Release();
  }

  public override void OnBlockRemoved() {
    base.OnBlockRemoved();
    Release();
  }

  private void Release() {
    if (_mover != null && Api is ICoreClientAPI capi)
      capi.Event.UnregisterRenderer(_mover, EnumRenderStage.Before);
    _mover = null;
  }

  private ClipActions Declared() {
    Block block = Blockentity.Block;
    if (_declaredFor == block)
      return _declared;
    _declaredFor = block;
    try {
      _declared = ClipActions.Of(block);
      _declaredError = null;
    } catch (InvalidOperationException e) {
      _declared = ClipActions.None;
      _declaredError = e.Message;
    }
    return _declared;
  }

  private void Choose(string clip, ClipPose pose) {
    ClipAction? action = Declared()
      .Declared.FirstOrDefault(a =>
        string.Equals(a.Clip, clip, StringComparison.OrdinalIgnoreCase)
      );
    _active[GroupOf(action, clip)] = (clip, pose);
  }

  private static string GroupOf(ClipAction? action, string clip) =>
    action?.State is { } state ? "state:" + state : "clip:" + clip;

  private void Step(ICoreClientAPI capi, float dt) {
    if (capi.IsGamePaused)
      return;
    Block block = Blockentity.Block;
    if (!_claimed && _builtFor != block)
      Build(capi, block);
    if (animUtil.animator is not { } animator)
      return;

    bool fresh = !ReferenceEquals(animator, _posed);
    _posed = animator;
    if (fresh) {
      _frames.Clear();
      _shown.Clear();
    }
    if (_claimed && (fresh || _builtFor != block)) {
      _builtFor = block;
      Resolve(
        capi,
        new Shape {
          Animations = [.. animator.Animations.Select(a => a.Animation)],
        }
      );
    }

    foreach ((string clip, float value) in _pending) {
      if (_resolved.TryGetValue(clip, out ClipAction? action))
        Select(action, value);
    }
    _pending.Clear();
    foreach ((string group, (string clip, ClipPose pose)) in _active.ToArray())
      Drive(animator, group, clip, pose, dt, fresh);
    foreach ((string clip, float fraction) in _targets.ToArray()) {
      _resolved.TryGetValue(clip, out ClipAction? action);
      if (action == null || action.Pose == ClipPose.Frame)
        Move(animator, clip, action, fraction, dt, fresh);
    }
  }

  private void Select(ClipAction action, float value) {
    if (action.Pose is not (ClipPose.Play or ClipPose.Run))
      return;
    string group = GroupOf(action, action.Clip);
    bool run = action.Pose == ClipPose.Run;
    if (run && action.When == null) {
      _rates[action.Clip] = Math.Max(value, 0f);
      _active[group] = (action.Clip, ClipPose.Run);
    } else if (Math.Abs(value - (action.When ?? 1f)) < 0.001f) {
      if (run)
        _rates[action.Clip] = 1f;
      _active[group] = (action.Clip, action.Pose.Value);
    } else if (run && _active.GetValueOrDefault(group).Clip == action.Clip) {
      _rates[action.Clip] = 0f;
    }
  }

  private void Drive(
    AnimatorBase animator,
    string group,
    string clip,
    ClipPose pose,
    float dt,
    bool fresh
  ) {
    _resolved.TryGetValue(clip, out ClipAction? action);
    if (action != null && action.Pose != pose)
      return;
    if (_shown.GetValueOrDefault(group) != clip) {
      foreach (
        string other in _resolved
          .Values.Where(a =>
            a.Pose is ClipPose.Play or ClipPose.Run
            && GroupOf(a, a.Clip) == group
          )
          .Select(a => a.Clip)
          .Append(_shown.GetValueOrDefault(group) ?? clip)
          .Where(c =>
            !string.Equals(c, clip, StringComparison.OrdinalIgnoreCase)
          )
      ) {
        animUtil.StopAnimation(other);
        _frames.Remove(other);
      }
      _shown[group] = clip;
      if (pose == ClipPose.Play) {
        animUtil.StopAnimation(clip);
        _frames[clip] = 0f;
      }
    }
    if (pose == ClipPose.Play)
      Move(animator, clip, action, 1f, dt, fresh);
    else
      Spin(animator, clip, action);
  }

  private void Spin(AnimatorBase animator, string clip, ClipAction? action) {
    if (
      animator.GetAnimationState(clip) is not { Animation: { } animation } state
    )
      return;
    float rate = _rates.GetValueOrDefault(clip);
    float seconds = action?.Seconds ?? (animation.QuantityFrames - 1) / 30f;
    float speed =
      rate > 0f ? rate * animation.QuantityFrames / (30f * seconds) : HoldSpeed;
    if (animUtil.activeAnimationsByAnimCode.TryGetValue(clip, out var meta))
      meta.AnimationSpeed = speed;
    else if (rate > 0f)
      animUtil.StartAnimation(
        new AnimationMetaData {
          Code = clip,
          Animation = clip,
          AnimationSpeed = speed,
          EaseInSpeed = HoldEase,
          EaseOutSpeed = HoldEase,
        }.Init()
      );
    else
      return;
    state.EasingFactor = 1f;
  }

  private void Build(ICoreClientAPI capi, Block block) {
    _builtFor = block;
    AssetLocation path = ExMeshCache.ShapePathOf(block);
    if (ExMeshCache.LoadShape(capi, path) is not { } shape) {
      Declared();
      if (_declaredError != null)
        capi.Logger.Error("{0}", _declaredError);
      capi.Logger.Error(
        "{0}: shape {1} not found, so no animator is built.",
        block.Code,
        path
      );
      return;
    }

    foreach (string code in animUtil.activeAnimationsByAnimCode.Keys.ToList())
      animUtil.StopAnimation(code);
    string key = AnimatorKeys.Of(block);
    MeshData mesh = animUtil.CreateMesh(key, shape, out Shape resolved, null);
    var rotation = new Vec3f(
      block.Shape.rotateX,
      block.Shape.rotateY,
      block.Shape.rotateZ
    );
    animUtil.InitializeAnimator(key, mesh, resolved, rotation);
    if (animUtil.renderer is { } renderer)
      renderer.CustomTransform = RotationTransform(rotation);
    Resolve(capi, resolved);
  }

  private void Resolve(ICoreClientAPI capi, Shape shape) {
    Declared();
    if (_declaredError != null)
      capi.Logger.Error("{0}", _declaredError);
    try {
      _resolved = Declared()
        .Resolve(shape)
        .ToDictionary(a => a.Clip, StringComparer.OrdinalIgnoreCase);
    } catch (InvalidOperationException e) {
      capi.Logger.Error("{0}", e.Message);
      _resolved = new(StringComparer.OrdinalIgnoreCase);
    }
  }

  private void Move(
    AnimatorBase animator,
    string clip,
    ClipAction? action,
    float fraction,
    float dt,
    bool snap
  ) {
    if (
      animator.GetAnimationState(clip) is not { Animation: { } animation } state
    )
      return;

    float last = animation.QuantityFrames - 1;
    float target = fraction * last;
    float seconds = action?.Seconds ?? last / 30f;
    float frame = snap
      ? target
      : MoveToward(
        _frames.GetValueOrDefault(clip),
        target,
        last / seconds * dt
      );
    _frames[clip] = frame;

    if (frame == 0f && target == 0f) {
      if (animUtil.activeAnimationsByAnimCode.ContainsKey(clip)) {
        state.CurrentFrame = 0f;
        animUtil.StopAnimation(clip);
      }
      return;
    }
    if (!animUtil.activeAnimationsByAnimCode.ContainsKey(clip))
      animUtil.StartAnimation(
        new AnimationMetaData {
          Code = clip,
          Animation = clip,
          AnimationSpeed = HoldSpeed,
          EaseInSpeed = HoldEase,
          EaseOutSpeed = HoldEase,
          StartFrameOnce = frame,
        }.Init()
      );
    state.CurrentFrame = frame;
    state.EasingFactor = 1f;
  }

  private static float MoveToward(float from, float to, float step) =>
    from < to ? Math.Min(from + step, to) : Math.Max(from - step, to);

  // The model-to-world matrix of a CompositeShape's full X, Y and Z rotation.
  private static float[] RotationTransform(Vec3f degrees) {
    float[] rotation = Mat4f.Create();
    Mat4f.RotateXYZ(
      rotation,
      degrees.X * GameMath.DEG2RAD,
      degrees.Y * GameMath.DEG2RAD,
      degrees.Z * GameMath.DEG2RAD
    );
    float[] transform = Mat4f.Create();
    Mat4f.Identity(transform);
    Mat4f.Translate(transform, transform, 0.5f, 0.5f, 0.5f);
    Mat4f.Mul(transform, transform, rotation);
    Mat4f.Translate(transform, transform, -0.5f, -0.5f, -0.5f);
    return transform;
  }

  private sealed class MoverRenderer(BEBehaviorClipActions owner) : IRenderer {
    public double RenderOrder => 0;
    public int RenderRange => 99;

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage) =>
      owner.Step((ICoreClientAPI)owner.Api, deltaTime);

    public void Dispose() { }
  }
}
