using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace ExpandedLib.Helpers;

/// <summary>Rendering helper for a block entity that animates through <see cref="BEBehaviorAnimatable"/> and <see cref="BlockEntityAnimationUtil"/> but is not raised via RightClickConstructable.</summary>
public sealed class ToggleAnimator {
  private readonly BlockEntity _be;
  private readonly Action<BEBehaviorAnimatable> _build;

  private BEBehaviorAnimatable? _animatable;
  private bool _ready;
  private Action? _repose;

  /// <param name="be">The owning block entity.</param>
  /// <param name="build">(Re)builds the animator on the resolved animatable behavior. Runs client-side only.</param>
  public ToggleAnimator(BlockEntity be, Action<BEBehaviorAnimatable> build) {
    _be = be;
    _build = build;
  }

  /// <summary>Whether a real animator currently exists; the gate on <see cref="Pose"/>.</summary>
  public bool Ready => _ready;

  /// <summary>The wrapped animation utility, or null off-client or before <see cref="Initialize"/>.</summary>
  public BlockEntityAnimationUtil? AnimUtil => _animatable?.animUtil;

  /// <summary>Resolves the animatable behavior, runs the initial build and applies the first pose, on the client only.</summary>
  public void Initialize(Action repose) {
    _repose = repose;
    _animatable = _be.GetBehavior<BEBehaviorAnimatable>();
    if (_be.Api is not ICoreClientAPI || _animatable == null)
      return;

    Rebuild();
    _repose?.Invoke();
  }

  /// <summary>(Re)builds the animator through the build delegate and re-evaluates the ready-guard.</summary>
  public void Rebuild() {
    if (_be.Api is not ICoreClientAPI || _animatable == null)
      return;

    _build(_animatable);
    // A failed shape resolve leaves animUtil.animator null; posing against it NREs inside vanilla.
    _ready = _animatable.animUtil.animator != null;
  }

  /// <summary>Runs <paramref name="pose"/> against the animation utility only on a client with a live animator.</summary>
  public void Pose(Action<BlockEntityAnimationUtil> pose) {
    if (_be.Api is not ICoreClientAPI || _animatable == null || !_ready)
      return;
    pose(_animatable.animUtil);
  }
}
