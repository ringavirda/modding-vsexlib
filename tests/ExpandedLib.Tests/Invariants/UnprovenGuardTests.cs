using System.Collections.Generic;
using System.IO;
using System.Linq;
using ExpandedLib.Testing;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>Every guard class in exlib's <c>Invariants</c> calls a proven check through
/// <see cref="GuardOfAttribute"/>, proves its own rule with a planted defect, or is pending.
/// </summary>
[GuardOf(typeof(PlantedDefects), nameof(PlantedDefects.Unproven))]
[GuardOf(typeof(FindingLists), nameof(FindingLists.Assert))]
public class UnprovenGuardTests {
  /// <summary>Guards with no planted-defect test yet, in file order.</summary>
  private static readonly string[] Pending =
  [
    "AwayCatchupStampTests",
    "CatalogueNamingTests",
    "CollectibleMappingGuardTests",
    "CommentStyleGuards",
    "FillerCleanupHookTests",
    "HostProcessParityTests",
    "IndustryBoundaryTests",
    "ModSystemOrderTests",
    "ProcessExtensionGuards",
    "PublicSurfaceTests",
    "ShapeLoadingGuards",
    "TeardownSymmetryTests",
    "TemplateGuards",
    "VersionPinTests",
  ];

  // Fails when a guard gains neither a [GuardOf] nor a planted test of its own rule and is not
  // pending, or a pending guard is proven and stays listed.
  [Fact]
  public void No_guard_is_Unproven_outside_the_pending_list() {
    FindingLists.Assert(
      PlantedDefects.Unproven(
        typeof(UnprovenGuardTests).Assembly,
        Path.Combine(RepoPaths.Root, "tests", "ExpandedLib.Tests", "Invariants")
      ),
      new Dictionary<string, string>(),
      Pending.ToDictionary(p => p, _ => "no planted-defect test yet")
    );
  }
}
