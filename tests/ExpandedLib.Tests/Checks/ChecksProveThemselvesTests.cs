using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ExpandedLib.Checks;
using ExpandedLib.Testing;
using Xunit;
using Xunit.Abstractions;

namespace ExpandedLib.Tests;

/// <summary>Every public static member of a check type, in <c>ExpandedLib.Testing/Checks</c> and
/// every <c>*Check.Run</c> in <c>ExpandedLib/Checks</c>, is proven by a planted-defect test, marked
/// a helper, or pending.</summary>
public class ChecksProveThemselvesTests(ITestOutputHelper output) {
  /// <summary>Members with no planted-defect test yet, in file order.</summary>
  private static readonly string[] Pending =
  [
    "CodeLiterals.UnresolvableBareCodes",
    "CodePrefixCollision.Collisions",
    "CostSelectorOverlap.Overlaps",
    "DefinitionAssets.MissingShapes",
    "DefinitionCatalogue.Resolves",
    "DefinitionGoldens.CheckGolden",
    "DefinitionGoldens.CheckCompleteness",
    "HandbookSync.Problems",
    "HandbookSync.Check",
    "LangCallSites.Unresolvable",
    "LangCoverage.OrphanedDescriptions",
    "LangKeys.Check",
    "LangParity.Check",
    "LoopingAnimations.Check",
    "MegablockFrames.Misfit",
    "MultiblockCodes.Unresolvable",
    "NetworkNodeContract.Violations",
    "NetworkNodeContract.SchemeViolations",
    "NetworkNodeContract.TypeGroupViolations",
    "PinnedNetworkNodes.Violations",
    "PressureVesselGate.NailedIngredients",
    "RecipeCodes.UnresolvableOutputs",
    "ReferencedCodes.Unresolvable",
    "ReferencedCodes.Checkable",
    "ReferencedCodes.BareButOurs",
    "SelectorCoverage.Check",
    "ShippedJson.Check",
#if GAME_GE_1_22
    "StructureBreaks.InScope",
#endif
    "TreeKeys.AssertGolden",
  ];

  private static PlantedDefects.Census Testing() =>
    PlantedDefects.Survey(
      Path.Combine(RepoPaths.Root, "src", "ExpandedLib.Testing", "Checks"),
      typeof(PlantedDefects).Assembly,
      typeof(ChecksProveThemselvesTests).Assembly
    );

  private static PlantedDefects.Census Runs() =>
    PlantedDefects.Survey(
      Path.Combine(RepoPaths.Root, "src", "ExpandedLib", "Checks"),
      typeof(ExlibChecks).Assembly,
      typeof(ChecksProveThemselvesTests).Assembly,
      "*Check.cs",
      "Run"
    );

  // Fails when a check member gains no planted test, helper mark or pending entry, or a pending
  // member gains a planted test and stays listed.
  [Fact]
  public void Every_check_member_is_proven_a_helper_or_pending() {
    PlantedDefects.Census testing = Testing(),
      runs = Runs();
    output.WriteLine(
      $"covered {testing.Proven.Count + runs.Proven.Count}, "
        + $"exempt {testing.Helpers.Count + runs.Helpers.Count}, "
        + $"pending {Pending.Length}"
    );

    FindingLists.Assert(
      [.. testing.Unplanted, .. runs.Unplanted],
      new Dictionary<string, string>(),
      Pending.ToDictionary(p => p, _ => "no planted-defect test yet")
    );
  }

  // Fails when Survey stops reading a surveyed file as a check type.
  [Fact]
  public void Both_surveys_reach_their_checks() {
    Assert.Contains("HarnessUse.CompletionWrites", Testing().Proven);
    Assert.Contains("LayoutTable.From", Testing().Helpers);
    Assert.Contains("RecipeCodesCheck.Run", Runs().Proven);
  }

  #region Survey

  // Fails when Survey counts an unmarked member proven or a helper, skips a helper's blank reason,
  // counts a mark on a method that is not a test, or passes a mark naming no member.
  [Fact]
  [PlantedDefect(typeof(PlantedDefects), nameof(PlantedDefects.Survey))]
  public void Survey_sorts_a_check_types_members_and_names_the_stray_marks() {
    using var dir = new FixtureDirectory(nameof(PlantedSurveyFixture) + ".cs");

    PlantedDefects.Census census = PlantedDefects.Survey(
      dir.Path,
      typeof(PlantedSurveyFixture).Assembly,
      typeof(PlantedSurveyFixture).Assembly
    );

    Assert.Equal(["PlantedSurveyFixture.Rule"], census.Proven);
    Assert.Equal(
      ["PlantedSurveyFixture.Helper", "PlantedSurveyFixture.Blank"],
      census.Helpers
    );
    Assert.Equal(
      [
        "PlantedSurveyFixture.Bare",
        "PlantedSurveyFixture.Blank: marked [CheckHelper] without a reason",
      ],
      census.Unplanted.Take(2)
    );
    Assert.Contains(
      "PlantedSurveyFixture.Bare: [PlantedDefect] on ChecksProveThemselvesTests.NotATest, "
        + "which is not a [Fact] or [Theory]",
      census.Unplanted
    );
    Assert.Contains(
      "PlantedSurveyFixture.Gone: [PlantedDefect] on "
        + "ChecksProveThemselvesTests.The_fixture_rule_names_a_negative names no public static member",
      census.Unplanted
    );
    Assert.Equal(4, census.Unplanted.Count);
  }

  // Fails when Survey passes a directory that names no check type.
  [Fact]
  [PlantedDefect(typeof(PlantedDefects), nameof(PlantedDefects.Survey))]
  public void Survey_of_a_directory_naming_no_type_throws() {
    using var dir = new FixtureDirectory("NoSuchType.cs");

    Assert.Throws<InvalidOperationException>(() =>
      PlantedDefects.Survey(
        dir.Path,
        typeof(PlantedSurveyFixture).Assembly,
        typeof(PlantedSurveyFixture).Assembly
      )
    );
  }

  // Fails when the fixture rule stops naming a negative.
  [Fact]
  [PlantedDefect(
    typeof(PlantedSurveyFixture),
    nameof(PlantedSurveyFixture.Rule)
  )]
  [PlantedDefect(typeof(PlantedSurveyFixture), "Gone")]
  public void The_fixture_rule_names_a_negative() {
    Assert.Single(PlantedSurveyFixture.Rule(-1));
  }

  [PlantedDefect(
    typeof(PlantedSurveyFixture),
    nameof(PlantedSurveyFixture.Bare)
  )]
  private static void NotATest() { }

  /// <summary>A temporary directory holding one empty file, deleted on dispose.</summary>
  private sealed class FixtureDirectory : IDisposable {
    public FixtureDirectory(string file) {
      Path = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(),
        "exlib_survey_" + Guid.NewGuid().ToString("N")
      );
      Directory.CreateDirectory(Path);
      File.WriteAllText(System.IO.Path.Combine(Path, file), "");
    }

    public string Path { get; }

    public void Dispose() => Directory.Delete(Path, recursive: true);
  }

  #endregion
}

/// <summary>A check type for <see cref="PlantedDefects.Survey"/>'s own tests: one proven rule, one
/// helper, one bare rule and one helper marked without a reason.</summary>
public static class PlantedSurveyFixture {
  /// <summary>Names every negative in <paramref name="values"/>.</summary>
  public static IReadOnlyList<string> Rule(params int[] values) =>
    [.. values.Where(v => v < 0).Select(v => $"{v} is negative")];

  /// <summary>A value for the rules.</summary>
  [CheckHelper("returns a constant the rules read")]
  public static int Helper() => 0;

  /// <summary>A rule no test proves.</summary>
  public static IReadOnlyList<string> Bare() => [];

  /// <summary>A helper marked without a reason.</summary>
  [CheckHelper(" ")]
  public static int Blank => 0;
}
