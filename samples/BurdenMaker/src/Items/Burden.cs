using System;
using System.Diagnostics.CodeAnalysis;
using Vintagestory.API.Common;

namespace BurdenMaker.Items;

/// <summary>The composition of a <see cref="ItemBurden"/> stack: relative parts of iron ore and
/// flux, stored raw and read back as fractions.</summary>
public readonly record struct BurdenMix(float Iron, float Flux) {
  public float Sum => Iron + Flux;
  public bool HasContent => Sum > 0.0001f;

  public float IronFrac => HasContent ? Iron / Sum : 0f;
  public float FluxFrac => HasContent ? Flux / Sum : 0f;
}

/// <summary>Read/write helpers and the grade classifier for burden mixes.</summary>
public static class Burden {
  private const string IronKey = "iron";
  private const string FluxKey = "flux";

  /// <summary>True when <paramref name="stack"/> is the burden item.</summary>
  public static bool Is([NotNullWhen(true)] ItemStack? stack) =>
    stack?.Collectible?.Code is { Domain: "burdenmaker", Path: "burden" };

  /// <summary>Stamps the mix parts onto a burden stack (any non-negative parts; read back as fractions).</summary>
  public static void Write(ItemStack stack, BurdenMix mix) {
    var a = stack.Attributes;
    a.SetFloat(IronKey, Math.Max(0f, mix.Iron));
    a.SetFloat(FluxKey, Math.Max(0f, mix.Flux));
  }

  /// <summary>Reads the mix parts off a burden stack; an unstamped stack reads as empty.</summary>
  public static BurdenMix Read(ItemStack? stack) {
    var a = stack?.Attributes;
    if (a == null)
      return default;
    return new BurdenMix(a.GetFloat(IronKey), a.GetFloat(FluxKey));
  }

  /// <summary>Lang key of the named grade for a mix, graded on flux alone.</summary>
  public static string ProfileLangKey(BurdenMix mix) {
    if (!mix.HasContent)
      return "burdenmaker:burden-profile-empty";

    foreach (BurdenProfile p in BurdenMakerValues.BurdenProfiles)
      if (InBand(mix.FluxFrac, p.MinFlux, p.MaxFlux))
        return "burdenmaker:burden-profile-" + p.Key;

    // Unreachable with the shipped bands (they tile 0..1), but a player's edited config can leave a gap.
    return "burdenmaker:burden-profile-offspec";
  }

  private static bool InBand(float value, float min, float max) =>
    value >= min && value <= max;
}
