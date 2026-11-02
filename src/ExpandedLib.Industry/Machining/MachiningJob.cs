using System;
using System.Collections.Generic;

namespace ExpandedLib.Industry.Machining;

/// <summary>An item a job leaves behind besides its output.</summary>
/// <param name="Code">Item code.</param>
/// <param name="Count">How many. At least one.</param>
public sealed record MachiningByproduct(string Code, int Count);

/// <summary>The bench route that does the same job by hand.</summary>
/// <param name="Machine">The bench machine key the recipe's craft sequence is filed under.</param>
/// <param name="Recipe">Asset name of the grid recipe whose craft sequence is the labour.</param>
public sealed record MachiningHand(string Machine, string Recipe);

/// <summary>
/// One cut: a piece goes in, a worked piece comes out, and <see cref="Count"/> of them do. Keyed by
/// <see cref="Step"/> and <see cref="Input"/>; offered at every machine kind that declares the step.
/// </summary>
/// <param name="Step">The cutting step, an open string; iiex uses <c>turn</c>, <c>bore</c>, <c>plane</c>, <c>slot</c> and <c>drill</c>.</param>
/// <param name="Input">Exact item code the machine takes.</param>
/// <param name="Output">Exact item code it yields.</param>
/// <param name="Count">How many of the output the input is worth. At least one.</param>
/// <param name="Material">Code of the <see cref="CuttingMaterial"/> cut, or null when the input item's <c>cuttingMaterial</c> attribute names it.</param>
/// <param name="Removed">Mass cut away, kg, or null to derive it from the items' <c>materialUnits</c>.</param>
/// <param name="Energy">Energy the cut takes, J, or null to derive it from the removed mass and the material.</param>
/// <param name="MinTier">Temper floor of the fitted tool. 0 when any will do.</param>
/// <param name="Wear">Points of wear the job costs its fitted tool. At least 0.</param>
/// <param name="Fixture">Exact code of an item the job needs present and never consumes, or null.</param>
/// <param name="Machines">Machine kind codes the job is limited to; empty offers it at every kind with the step.</param>
/// <param name="Byproducts">What the job leaves besides its output.</param>
/// <param name="Hand">The bench route that does the same job, or null.</param>
public sealed record MachiningJob(
  string Step,
  string Input,
  string Output,
  int Count,
  string? Material,
  float? Removed,
  float? Energy,
  int MinTier,
  int Wear,
  string? Fixture,
  IReadOnlyList<string> Machines,
  IReadOnlyList<MachiningByproduct> Byproducts,
  MachiningHand? Hand
) {
  /// <summary>The item attribute holding an item's metal content in units; read for the removed mass.</summary>
  public const string UnitsAttribute = "materialUnits";

  /// <summary>The item attribute naming the <see cref="CuttingMaterial"/> of an input that its job does not name.</summary>
  public const string MaterialAttribute = "cuttingMaterial";

  /// <summary>What a job costs its tool when it declares no wear of its own.</summary>
  public const int DefaultWear = 1;

  /// <summary>Mass cut away, kg. Set by <see cref="MachiningJobs.Resolve(IEnumerable{Vintagestory.API.Common.CollectibleObject?}, MachiningJobRegistry?, CuttingMaterialRegistry?)"/>;
  /// 0 until the job is resolved.</summary>
  public float RemovedKg { get; internal set; }

  /// <summary>Code of the cutting material the job resolved to, or null until resolved.</summary>
  public string? ResolvedMaterial { get; internal set; }

  /// <summary>Whether this job is the one for <paramref name="input"/> at <paramref name="step"/>.</summary>
  public bool Matches(string step, string input) =>
    string.Equals(Step, step, StringComparison.OrdinalIgnoreCase)
    && string.Equals(Input, input, StringComparison.OrdinalIgnoreCase);
}
