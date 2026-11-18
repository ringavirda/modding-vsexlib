using Vintagestory.API.MathTools;

namespace ExpandedLib.Helpers;

/// <summary>The colours of a projection, as ints in <see cref="ColorUtil.ColorFromRgba"/> order with red in the low byte, the order <c>Block.GetColor</c> returns and <c>IClientWorldAccessor.HighlightBlocks</c> reads.</summary>
internal static class ProjectionColors {
  /// <summary>The alpha of every projection colour.</summary>
  internal const int Alpha = 0x60;

  /// <summary>A cell occupied by the wrong block, or a placement short of its tool or fasteners.</summary>
  internal static readonly int Refused = ColorUtil.ColorFromRgba(
    215,
    94,
    94,
    Alpha
  );

  /// <summary>A wanted block code that resolves to no block.</summary>
  internal static readonly int Unknown = ColorUtil.ColorFromRgba(
    94,
    94,
    215,
    Alpha
  );

  /// <summary>A joint made, or a cell a click would change.</summary>
  internal static readonly int Joined = ColorUtil.ColorFromRgba(
    94,
    215,
    94,
    Alpha
  );

  /// <summary>An arm left open.</summary>
  internal static readonly int Open = ColorUtil.ColorFromRgba(
    240,
    180,
    60,
    Alpha
  );

  /// <summary>Keeps the RGB of <paramref name="blockColor"/> and sets the alpha to <see cref="Alpha"/>.</summary>
  internal static int Of(int blockColor) =>
    (blockColor & 0xFFFFFF) | (Alpha << 24);

  /// <summary>Gives each channel of <paramref name="color"/> as 0..1 for a shader tint, reading red from the low byte, unlike <c>ColorUtil.ToRGBAVec4f</c>.</summary>
  internal static Vec4f Tint(int color) =>
    new(
      (color & 0xFF) / 255f,
      ((color >> 8) & 0xFF) / 255f,
      ((color >> 16) & 0xFF) / 255f,
      ((uint)color >> 24) / 255f
    );
}
