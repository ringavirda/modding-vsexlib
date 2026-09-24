using System;

namespace ExpandedLib.Testing;

/// <summary>
/// Marks a public static member of a check type that returns data rather than findings (a reader,
/// a path, a collector), so no planted defect can make it fail. <see cref="PlantedDefects"/>
/// counts it exempt instead of unproven.
/// </summary>
/// <param name="reason">What the member returns and which rule reads it; a blank reason is
/// reported by <see cref="PlantedDefects.Survey"/>.</param>
[AttributeUsage(
  AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Field
)]
public sealed class CheckHelperAttribute(string reason) : Attribute {
  /// <summary>Why the member is not a rule.</summary>
  public string Reason { get; } = reason;
}
