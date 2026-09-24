using System.Collections.Generic;
using System.IO;
using ExpandedLib.Testing;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>Every guard class in exlib's <c>Invariants</c> calls a proven check through
/// <see cref="GuardOfAttribute"/> or proves its own rule with a planted defect.</summary>
[GuardOf(typeof(PlantedDefects), nameof(PlantedDefects.Unproven))]
[GuardOf(typeof(FindingLists), nameof(FindingLists.Assert))]
public class UnprovenGuardTests {
  // Fails when a guard gains neither a [GuardOf] nor a planted test of its own rule.
  [Fact]
  public void No_guard_is_Unproven() {
    FindingLists.Assert(
      PlantedDefects.Unproven(
        typeof(UnprovenGuardTests).Assembly,
        Path.Combine(RepoPaths.Root, "tests", "ExpandedLib.Tests", "Invariants")
      ),
      new Dictionary<string, string>(),
      new Dictionary<string, string>()
    );
  }
}
