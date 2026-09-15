using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ExpandedLib.Blocks;

/// <summary>Owns the animator and <see cref="ExRightClickConstructable"/> lifecycle shared by
/// constructed, animator-rendered mega-blocks.</summary>
public sealed class ConstructedAnimator {
  private readonly BlockEntity _be;
  private readonly Func<string> _cacheKey;
  private readonly Action<BlockEntityAnimationUtil, MeshData>? _onAnimatorBuilt;
  private readonly Func<ITexPositionSource?>? _texSource;

  // System.Func spelled out: Vintagestory.API.Common declares its own Func<,>, ambiguous otherwise.
  private readonly System.Func<string[]?, string[]?>? _composeElements;

  private BEBehaviorAnimatable? _animatable;
  private ExRightClickConstructable? _rcc;
  private bool _ready;
  private Action? _repose;

  /// <param name="be">The owning block entity.</param>
  /// <param name="cacheKey">Per-block animator/shape cache key, evaluated lazily.</param>
  /// <param name="onAnimatorBuilt">Optional hook run after each successful (re)build, before the
  /// pose.</param>
  /// <param name="texSource">Optional texture source for the build, evaluated per build.</param>
  /// <param name="composeElements">Optional refinement of the element set, applied to every
  /// build.</param>
  public ConstructedAnimator(
    BlockEntity be,
    Func<string> cacheKey,
    Action<BlockEntityAnimationUtil, MeshData>? onAnimatorBuilt = null,
    Func<ITexPositionSource?>? texSource = null,
    System.Func<string[]?, string[]?>? composeElements = null
  ) {
    _be = be;
    _cacheKey = cacheKey;
    _onAnimatorBuilt = onAnimatorBuilt;
    _texSource = texSource;
    _composeElements = composeElements;
  }

  /// <summary>True once the player has finished the construction stages. Valid on the server
  /// too.</summary>
  public bool IsConstructed => _rcc?.IsComplete ?? false;

  /// <summary>Whether a real animator currently exists. The single gate on <see cref="Pose"/>.</summary>
  public bool Ready => _ready;

  /// <summary>The wrapped animation utility, or null off-client / before <see cref="Initialize"/>.</summary>
  public BlockEntityAnimationUtil? AnimUtil => _animatable?.animUtil;

  /// <summary>The construction behavior, or null when the block has none (a purely-animated machine).</summary>
  public ExRightClickConstructable? Rcc => _rcc;

  /// <summary>Resolves the animatable and construction behaviors, wires construction-stage
  /// re-tessellation, builds the initial mesh and applies the first pose. Safe to call on any
  /// side.</summary>
  public void Initialize(Action repose) {
    _repose = repose;
    _animatable = _be.GetBehavior<BEBehaviorAnimatable>();
    _rcc = _be.GetBehavior<ExRightClickConstructable>();

    if (_be.Api is not ICoreClientAPI || _animatable == null)
      return;

    // Re-render whenever a construction stage adds/removes elements.
    if (_rcc != null)
      _rcc.OnShapeChanged += OnShapeChanged;

    Rebuild(_rcc?.shape?.SelectiveElements);
    _repose?.Invoke();
  }

  private void OnShapeChanged(CompositeShape cs) {
    Rebuild(cs?.SelectiveElements);
    _repose?.Invoke();
  }

  /// <summary>Rebuilds the mesh at the current construction stage and re-applies the pose.</summary>
  public void Refresh() {
    Rebuild(_rcc?.shape?.SelectiveElements);
    _repose?.Invoke();
  }

  /// <summary>(Re)builds the animator to render exactly the currently-built elements, narrowed by
  /// the constructor's element composer where one was supplied.</summary>
  public void Rebuild(string[]? selectiveElements) {
    if (_be.Api is not ICoreClientAPI || _animatable == null)
      return;

    if (_composeElements != null)
      selectiveElements = _composeElements(selectiveElements);

    BlockEntityAnimationUtil util = _animatable.animUtil;

    // CreateMesh resolves a fresh shape each call; rotation is applied by the renderer, not the mesh.
    MeshData meshData = util.CreateMesh(
      _cacheKey(),
      null,
      out Shape resolvedShape,
      _texSource?.Invoke(),
      new TesselationMetaData { SelectiveElements = selectiveElements }
    );

    util.InitializeAnimator(
      _cacheKey(),
      meshData,
      resolvedShape,
      new Vec3f(0, _be.Block.Shape.rotateY, 0)
    );

    // A failed shape resolve leaves animUtil.animator null.
    _ready = util.animator != null;
    if (_ready)
      _onAnimatorBuilt?.Invoke(util, meshData);
  }

  /// <summary>Runs <paramref name="pose"/> against the animation utility only on a client with a
  /// live animator. A no-op otherwise.</summary>
  public void Pose(Action<BlockEntityAnimationUtil> pose) {
    if (_be.Api is not ICoreClientAPI || _animatable == null || !_ready)
      return;
    pose(_animatable.animUtil);
  }

  /// <summary>Unsubscribes from construction-stage events. Call from both <c>OnBlockRemoved</c> and
  /// <c>OnBlockUnloaded</c>.</summary>
  public void Dispose() {
    if (_rcc != null)
      _rcc.OnShapeChanged -= OnShapeChanged;
  }
}
