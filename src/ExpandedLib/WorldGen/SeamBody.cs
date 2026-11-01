#if GAME_GE_1_22
using System;
using Vintagestory.API.MathTools;

namespace ExpandedLib.WorldGen;

/// <summary>The per-body values a seam draws once, before it knows which chunk it generates.</summary>
internal sealed class SeamBody
{
  /// <summary>The footprint with this body's run, strike and dip direction.</summary>
  public required DepositFootprint Footprint { get; init; }

  /// <summary>Whole length along the dip in blocks, at most <see cref="DepositFootprint.MaxLength"/>.</summary>
  public required float Run { get; init; }

  /// <summary>Whole length along the strike in blocks, at most <see cref="DepositFootprint.MaxLength"/>.</summary>
  public required float Strike { get; init; }

  /// <summary>Dip direction in radians, 0 up to 2 pi.</summary>
  public required float DipDirection { get; init; }

  /// <summary>Drop in blocks per block down the dip.</summary>
  public required float Dip { get; init; }

  /// <summary>The fraction of sea level height for <see cref="SeamAnchor.Sealevel"/>, the depth
  /// in blocks for <see cref="SeamAnchor.Surface"/>.</summary>
  public required float Anchor { get; init; }

  /// <summary>Blocks across the bed per layer, 0 or more.</summary>
  public required float[] Thicknesses { get; init; }

  /// <summary>Outcrop depth in blocks down the layer per layer, before the climate scale; null
  /// for a layer with no outcrop.</summary>
  public required float?[] OutcropDepths { get; init; }

  /// <summary>Seed of the position hash every column roll uses.</summary>
  public required int Seed { get; init; }

  /// <summary>
  /// Draws a body from <paramref name="rand"/> in a fixed order: run, strike, dip direction, dip,
  /// anchor (yCropRel or depth by the spec's anchor), each layer's thickness, each outcrop layer's
  /// depth, then the seed. A later chunk redrawing from the same seed gets the same body.
  /// </summary>
  /// <param name="spec">The deposit's attributes; its layers are all drawn.</param>
  /// <param name="rand">The deposit random, freshly seeded for the body.</param>
  public static SeamBody Draw(SeamSpec spec, IRandom rand)
  {
    var fp = spec.Footprint;
    float run = Math.Min(fp.Run.nextFloat(1, rand), DepositFootprint.MaxLength);
    float strike = Math.Min(
      fp.Strike.nextFloat(1, rand),
      DepositFootprint.MaxLength
    );
    float direction = rand.NextFloat() * MathF.Tau;
    float dip = spec.Dip.nextFloat(1, rand);
    float anchor =
      spec.Anchor == SeamAnchor.Sealevel
        ? spec.YCropRel.nextFloat(1, rand)
        : spec.Depth.nextFloat(1, rand);
    var thicknesses = new float[spec.Layers.Count];
    for (int i = 0; i < thicknesses.Length; i++)
    {
      thicknesses[i] = Math.Max(
        0f,
        spec.Layers[i].Thickness.nextFloat(1, rand)
      );
    }
    var outcrops = new float?[thicknesses.Length];
    for (int i = 0; i < outcrops.Length; i++)
    {
      if (spec.Layers[i].Outcrop is { } outcrop)
      {
        outcrops[i] = Math.Max(0f, outcrop.Depth.nextFloat(1, rand));
      }
    }
    return new SeamBody
    {
      Footprint = new DepositFootprint(
        run,
        strike,
        fp.Exponent,
        fp.TaperFull,
        fp.TaperZero,
        direction
      ),
      Run = run,
      Strike = strike,
      DipDirection = direction,
      Dip = dip,
      Anchor = anchor,
      Thicknesses = thicknesses,
      OutcropDepths = outcrops,
      Seed = rand.NextInt(),
    };
  }
}
#endif
