using System;

namespace ExpandedLib.Industry.Machining;

/// <summary>The motion a cut makes: the work or the tool turning, or the tool or the work moving to and fro.</summary>
public enum CutMotion {
  /// <summary>Turning, boring and drilling.</summary>
  Rotary,

  /// <summary>Planing, shaping, slotting and filing.</summary>
  Reciprocating,
}

/// <summary>A metal a machine can cut: its density and the energy a kilogram of it costs to cut away.</summary>
/// <param name="Code">Bare lowercase name a job or an item's <c>cuttingMaterial</c> attribute refers to.</param>
/// <param name="Density">Mass of one cubic metre, kg/m3. Above 0.</param>
/// <param name="RotaryEnergy">Energy to cut away one kilogram by a rotary cut, J/kg. Above 0.</param>
/// <param name="ReciprocatingEnergy">Energy to cut away one kilogram by a reciprocating cut, J/kg. Above 0.</param>
public sealed record CuttingMaterial(
  string Code,
  float Density,
  float RotaryEnergy,
  float ReciprocatingEnergy
) {
  /// <summary>Energy to cut away one kilogram with <paramref name="motion"/>, J/kg.</summary>
  /// <exception cref="ArgumentOutOfRangeException"><paramref name="motion"/> is not a declared motion.</exception>
  public float EnergyFor(CutMotion motion) =>
    motion switch {
      CutMotion.Rotary => RotaryEnergy,
      CutMotion.Reciprocating => ReciprocatingEnergy,
      _ => throw new ArgumentOutOfRangeException(nameof(motion)),
    };
}

/// <summary>A man's sustained working power at a tool, by motion.</summary>
/// <param name="Rotary">Power at a crank, brace or hand drill, W. Above 0.</param>
/// <param name="Reciprocating">Power at a file or saw, W. Above 0.</param>
public sealed record HandPower(float Rotary, float Reciprocating) {
  /// <summary>Power for <paramref name="motion"/>, W.</summary>
  /// <exception cref="ArgumentOutOfRangeException"><paramref name="motion"/> is not a declared motion.</exception>
  public float For(CutMotion motion) =>
    motion switch {
      CutMotion.Rotary => Rotary,
      CutMotion.Reciprocating => Reciprocating,
      _ => throw new ArgumentOutOfRangeException(nameof(motion)),
    };
}
