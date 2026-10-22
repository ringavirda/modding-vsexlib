using Vintagestory.API.Common;

namespace ExpandedLib.Helpers;

/// <summary>A block entity that tessellates a shape of its own choosing in place of its block's
/// <c>shape</c>. The setup scene dump writes <see cref="DrawnShape"/> for the cell so the scene
/// renderer draws what the game draws.</summary>
public interface IDrawsShape {
  /// <summary>Location of the shape as a blocktype's <c>shape.base</c> names it: the domain and the
  /// path under <c>shapes/</c>, without the extension, such as <c>iiex:molten/canal/slope-5</c>.
  /// Read from the entity's current state on every call.</summary>
  AssetLocation DrawnShape { get; }
}
