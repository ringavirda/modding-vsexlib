using System;

namespace ExpandedLib.Testing;

/// <summary>
/// Marks a <c>[Fact]</c> or <c>[Theory]</c> that feeds <see cref="Check"/>'s
/// <see cref="Member"/> a planted defect and asserts the finding it reports.
/// <see cref="PlantedDefects"/> counts a member as proven only through such a test.
/// </summary>
/// <param name="check">The type that declares the rule.</param>
/// <param name="member">The rule's name on <paramref name="check"/>; write it with
/// <c>nameof</c>. Every overload of that name counts as proven.</param>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class PlantedDefectAttribute(Type check, string member)
  : Attribute {
  /// <summary>The type that declares the rule.</summary>
  public Type Check { get; } = check;

  /// <summary>The rule's member name on <see cref="Check"/>.</summary>
  public string Member { get; } = member;
}
