using System.Linq;
using Vintagestory.API.Common;

namespace ExpandedLib.Blocks;

/// <summary>The key a block's animator is cached under in the client's animator cache: the cache
/// holds a shape's clips, elements and poses, so the key names the shape and nothing else.</summary>
public static class AnimatorKeys {
  /// <summary>The shape's asset path with each overlay's asset path after it, joined by
  /// <c>+</c>. Selective elements and rotation are not part of it.</summary>
  /// <param name="block">A block with a shape; it is read as resolved for its variant.</param>
  /// <returns>For example <c>iiex:pipe/valve</c>; never empty.</returns>
  public static string Of(Block block) =>
    string.Join(
      "+",
      new[] { block.Shape.Base.ToString() }.Concat(
        block.Shape.Overlays?.Select(overlay => overlay.Base.ToString()) ?? []
      )
    );
}
