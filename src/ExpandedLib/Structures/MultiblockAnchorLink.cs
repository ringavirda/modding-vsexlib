using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Structures;

/// <summary>A throttled resolver a functional component keeps to find and keep reading its multiblock anchor.</summary>
public sealed class MultiblockAnchorLink<T>
  where T : BlockEntityMultiblockStructure {
  private readonly BlockEntity _component;
  private readonly int _horizontal;
  private readonly int _below;
  private readonly int _above;

  private BlockPos? _anchorPos;
  private long _lastScanMs = -1;

  private const long RescanIntervalMs = 1000;

  /// <param name="component">The functional block whose anchor this resolves. Its position is the scan
  /// origin, its <c>Api.World</c> the scan surface.</param>
  /// <param name="horizontal">Cells to scan out on each horizontal axis.</param>
  /// <param name="below">Cells to scan downward (components sit above their anchor).</param>
  /// <param name="above">Cells to scan upward (a small margin).</param>
  public MultiblockAnchorLink(
    BlockEntity component,
    int horizontal,
    int below,
    int above
  ) {
    _component = component;
    _horizontal = horizontal;
    _below = below;
    _above = above;
  }

  /// <summary>The owning anchor, or null when none is in range; re-scans at most once per <see cref="RescanIntervalMs"/>.</summary>
  public T? Resolve() {
    IWorldAccessor? world = _component.Api?.World;
    if (world == null)
      return null;

    // Still ours only if a T still sits at that cell and still owns us.
    if (_anchorPos != null) {
      if (
        world.BlockAccessor.GetBlockEntity(_anchorPos) is T cached
        && cached.OwnsCell(_component.Pos)
      )
        return cached;
      _anchorPos = null;
    }

    long now = world.ElapsedMilliseconds;
    if (_lastScanMs >= 0 && now - _lastScanMs < RescanIntervalMs)
      return null;
    _lastScanMs = now;

    T? found = BlockEntityMultiblockStructure.FindAnchorOwning<T>(
      world,
      _component.Pos,
      _horizontal,
      _below,
      _above
    );
    _anchorPos = found?.Pos;
    return found;
  }
}
