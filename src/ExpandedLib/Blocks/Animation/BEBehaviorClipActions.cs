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
/// animator and moves each <see cref="ClipPose.Frame"/> clip toward the target its synced state sets.
/// A <see cref="BEBehaviorAnimatable"/>, so code that looks that behaviour up finds it.</summary>
/// <remarks>Registered as <c>ClipActions</c>. The animator is built on the first client frame and
/// again whenever the entity's block changes. The synced tree is read on every client
/// <c>FromTreeAttributes</c>, so a state key written on the server shows on the client with no entity
/// code. A block with the behaviour and no declaration gets its animator and plays nothing until its
/// entity calls <see cref="SetTarget"/>.</remarks>
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
  private Dictionary<string, ClipAction> _resolved = new(
    StringComparer.OrdinalIgnoreCase
  );
  private ClipActions _declared = ClipActions.None;
  private Block? _declaredFor;
  private string? _declaredError;
  private Block? _builtFor;
  private bool _ownBuild;
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

  private void Step(ICoreClientAPI capi, float dt) {
    if (capi.IsGamePaused)
      return;
    Block block = Blockentity.Block;
    if (_builtFor != block && (animUtil.animator == null || _ownBuild))
      Build(capi, block);
    if (animUtil.animator is not { } animator)
      return;

    bool fresh = !ReferenceEquals(animator, _posed);
    _posed = animator;
    if (fresh)
      _frames.Clear();
    foreach ((string clip, float fraction) in _targets.ToArray())
      Move(animator, clip, fraction, dt, fresh);
  }

  private void Build(ICoreClientAPI capi, Block block) {
    _builtFor = block;
    if (_declaredError != null)
      capi.Logger.Error("{0}", _declaredError);
    AssetLocation path = ExMeshCache.ShapePathOf(block);
    if (ExMeshCache.LoadShape(capi, path) is not { } shape) {
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
    _ownBuild = true;

    try {
      _resolved = Declared()
        .Resolve(resolved)
        .ToDictionary(a => a.Clip, StringComparer.OrdinalIgnoreCase);
    } catch (InvalidOperationException e) {
      capi.Logger.Error("{0}", e.Message);
      _resolved = new(StringComparer.OrdinalIgnoreCase);
    }
  }

  private void Move(
    AnimatorBase animator,
    string clip,
    float fraction,
    float dt,
    bool snap
  ) {
    _resolved.TryGetValue(clip, out ClipAction? action);
    if (
      action is { Pose: not ClipPose.Frame }
      || animator.GetAnimationState(clip)
        is not { Animation: { } animation } state
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
