using System;

namespace ExpandedLib.Testing;

/// <summary>
/// Marks a guard class in a suite's <c>Invariants</c> folder as a caller of the check
/// <see cref="Check"/>'s <see cref="Member"/>, proven by that check's planted-defect tests.
/// </summary>
/// <remarks><see cref="PlantedDefects.Unproven"/> counts the guard proven;
/// <see cref="HarnessUse.UncalledGuards"/> fails a file that never calls the member.</remarks>
/// <param name="check">The type that declares the check.</param>
/// <param name="member">The check's name on <paramref name="check"/>; write it with
/// <c>nameof</c>.</param>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class GuardOfAttribute(Type check, string member) : Attribute {
  /// <summary>The type that declares the check.</summary>
  public Type Check { get; } = check;

  /// <summary>The check's member name on <see cref="Check"/>.</summary>
  public string Member { get; } = member;
}
