using System;
using System.Collections.Generic;

namespace ExpandedLib.Industry.Pipes;

/// <summary>What a bore class of the physical pipe network is made of and how it fails.</summary>
/// <param name="BoreMetres">Real bore in m, above zero.</param>
/// <param name="WorkingAtm">Working pressure W in absolute atm, above 1. A piece loaded past it weeps.</param>
/// <param name="BurstAtm">Burst pressure B in absolute atm, at least <paramref name="WorkingAtm"/>. A piece held above it bursts.</param>
/// <param name="Span">Cells a centred piece may stand from a supported cell, 1 or more.</param>
/// <param name="Friction">Darcy friction factor, above zero.</param>
/// <param name="Joint">Joint family the class couples with, as <see cref="BlockPipe.FlangedJoint"/>.</param>
/// <param name="WallKgPerMetre">Mass of the wall per metre of run in kg/m, zero or more; 0 builds a cell without a wall.</param>
/// <param name="WallMetres">Wall thickness in m, zero or more; widens the wall's outer area.</param>
public sealed record FlowBore(
  double BoreMetres,
  double WorkingAtm,
  double BurstAtm,
  int Span,
  double Friction,
  string Joint,
  double WallKgPerMetre = 0,
  double WallMetres = 0
) {
  /// <summary>The burst pressure of a bore with working pressure <paramref name="workingAtm"/>, in absolute atm: the gauge working pressure times 1.25, over the atmosphere.</summary>
  /// <param name="workingAtm">Working pressure W in absolute atm, 1 or more.</param>
  public static double BurstFor(double workingAtm) =>
    1 + 1.25 * (workingAtm - 1);
}

/// <summary>The registry of bore classes the physical pipe network reads, keyed by a pipe tier and a class within it.</summary>
public static class FlowBores {
  private static readonly Dictionary<
    (string Tier, string Class),
    FlowBore
  > _bores = [];

  /// <summary>Registers the bore class <paramref name="boreClass"/> of <paramref name="tier"/>; a later registration for the same pair replaces the earlier one.</summary>
  /// <param name="tier">The pipe tier, as <see cref="BlockPipe.Tier"/>.</param>
  /// <param name="boreClass">The class within the tier, such as "main" or "small".</param>
  /// <param name="boreMetres">Real bore in m, above zero.</param>
  /// <param name="workingAtm">Working pressure W in absolute atm, above 1.</param>
  /// <param name="burstAtm">Burst pressure B in absolute atm, at least <paramref name="workingAtm"/>; <see cref="FlowBore.BurstFor"/> gives the 1.25 rule.</param>
  /// <param name="span">Cells a centred piece may stand from a supported cell, 1 or more.</param>
  /// <param name="friction">Darcy friction factor, above zero.</param>
  /// <param name="joint">Joint family, as <see cref="BlockPipe.FlangedJoint"/>.</param>
  /// <param name="wallKgPerMetre">Mass of the wall per metre of run in kg/m, zero or more; 0 builds a cell without a wall.</param>
  /// <param name="wallMetres">Wall thickness in m, zero or more.</param>
  /// <exception cref="ArgumentException">A name is empty.</exception>
  /// <exception cref="ArgumentOutOfRangeException">A number is outside the range its parameter names.</exception>
  public static void Register(
    string tier,
    string boreClass,
    double boreMetres,
    double workingAtm,
    double burstAtm,
    int span,
    double friction,
    string joint,
    double wallKgPerMetre = 0,
    double wallMetres = 0
  ) {
    ArgumentException.ThrowIfNullOrEmpty(tier);
    ArgumentException.ThrowIfNullOrEmpty(boreClass);
    ArgumentException.ThrowIfNullOrEmpty(joint);
    if (boreMetres <= 0)
      throw new ArgumentOutOfRangeException(nameof(boreMetres));
    if (workingAtm <= 1)
      throw new ArgumentOutOfRangeException(nameof(workingAtm));
    if (burstAtm < workingAtm)
      throw new ArgumentOutOfRangeException(nameof(burstAtm));
    if (span < 1)
      throw new ArgumentOutOfRangeException(nameof(span));
    if (friction <= 0)
      throw new ArgumentOutOfRangeException(nameof(friction));
    if (wallKgPerMetre < 0)
      throw new ArgumentOutOfRangeException(nameof(wallKgPerMetre));
    if (wallMetres < 0)
      throw new ArgumentOutOfRangeException(nameof(wallMetres));
    _bores[(tier, boreClass)] = new FlowBore(
      boreMetres,
      workingAtm,
      burstAtm,
      span,
      friction,
      joint,
      wallKgPerMetre,
      wallMetres
    );
  }

  /// <summary>The bore class registered for <paramref name="tier"/> and <paramref name="boreClass"/>.</summary>
  /// <returns>False when none is registered.</returns>
  public static bool TryGet(string tier, string boreClass, out FlowBore bore) =>
    _bores.TryGetValue((tier, boreClass), out bore!);

  internal static void Clear() => _bores.Clear();
}
