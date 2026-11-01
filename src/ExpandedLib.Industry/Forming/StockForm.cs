using System;
using System.Linq;

namespace ExpandedLib.Industry.Forming;

/// <summary>Dimensions of a piece of rolling stock as it enters the mill, and how it spreads as the rolls
/// flatten it.</summary>
/// <param name="Name">The form's code, as a roll set's <c>accepts</c> lists it; bare lowercase.</param>
/// <param name="BaseWidth">Width of the authored base shape, in voxels.</param>
/// <param name="BaseThickness">Thickness of the authored base shape, in voxels.</param>
/// <param name="MaxWidth">Width the piece will not exceed however far it is rolled, in voxels.</param>
/// <param name="BaseLength">Length of the authored base shape, in voxels.</param>
/// <param name="SpreadExponent">Share of the reduction that goes sideways: <c>w = w0 * (t0/t)^e</c>.</param>
/// <param name="Shape">Shape file holding this form's drawn states, or null when it draws none.</param>
/// <param name="BaseElement">The element that is the piece as it arrives, scaled for every undrawn gauge.</param>
/// <param name="FormerNames">Alternate names this form resolves by, for a piece already in a world.</param>
public sealed record StockForm(
  string Name,
  float BaseWidth,
  float BaseThickness,
  float MaxWidth,
  float BaseLength,
  float SpreadExponent,
  string? Shape = null,
  string? BaseElement = null,
  string[]? FormerNames = null
) {
  /// <summary>Alternate names this form resolves by. Empty, never null.</summary>
  public string[] FormerNames { get; init; } = FormerNames ?? [];

  /// <summary>Width this form reaches once rolled to <paramref name="thickness"/>, capped at
  /// <see cref="MaxWidth"/>.</summary>
  public float WidthAt(float thickness) =>
    SpreadWidth(BaseWidth, BaseThickness, thickness, SpreadExponent, MaxWidth);

  /// <summary>How wide a piece becomes once rolled from <paramref name="startThickness"/> down to
  /// <paramref name="thickness"/>: <c>w = w0 * (t0/t)^e</c>.</summary>
  /// <param name="startWidth">Width before rolling; zero or less yields 0.</param>
  /// <param name="startThickness">Thickness before rolling; zero or less leaves the width alone.</param>
  /// <param name="thickness">Thickness after rolling; zero or less leaves the width alone.</param>
  /// <param name="exponent">Share of the reduction that goes sideways.</param>
  /// <param name="maxWidth">Cap on the result when above 0; 0 means uncapped.</param>
  public static float SpreadWidth(
    float startWidth,
    float startThickness,
    float thickness,
    float exponent = 0.5f,
    float maxWidth = 0f
  ) {
    if (startWidth <= 0f)
      return 0f;
    if (startThickness <= 0f || thickness <= 0f)
      return startWidth;
    float width = startWidth * MathF.Pow(startThickness / thickness, exponent);
    return maxWidth > 0f ? MathF.Min(width, maxWidth) : width;
  }

  /// <summary>Whether <paramref name="other"/> declares the same form, former names compared by
  /// content.</summary>
  public bool SameAs(StockForm other) =>
    Name == other.Name
    && BaseWidth == other.BaseWidth
    && BaseThickness == other.BaseThickness
    && MaxWidth == other.MaxWidth
    && BaseLength == other.BaseLength
    && SpreadExponent == other.SpreadExponent
    && Shape == other.Shape
    && BaseElement == other.BaseElement
    && FormerNames.SequenceEqual(other.FormerNames);
}
