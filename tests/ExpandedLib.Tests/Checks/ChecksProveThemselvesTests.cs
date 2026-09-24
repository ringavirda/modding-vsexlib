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

  /// <summary>Census line, and why it stands.</summary>
  private static readonly Dictionary<string, string> Allowed = new(
    StringComparer.Ordinal
  ) {
#if !GAME_GE_1_22
    ["StructureBreaks: the file names no type in ExpandedLib.Testing"] =
      "the file compiles on 1.22 and later only",
#endif
  };

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
      Allowed,
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

  // Fails when Survey passes a file named for no type, as a check file renamed away from its type
  // is, or surveys only the type a file is named after.
  [Fact]
  [PlantedDefect(typeof(PlantedDefects), nameof(PlantedDefects.Survey))]
  public void Survey_names_a_renamed_file_and_reads_every_type_a_file_declares() {
    string checks = Path.Combine(
      RepoPaths.Root,
      "src",
      "ExpandedLib.Testing",
      "Checks"
    );
    using var dir = new FixtureDirectory();
    File.Copy(
      Path.Combine(checks, "LangParity.cs"),
      Path.Combine(dir.Path, "LangParityRules.cs")
    );
    File.Copy(
      Path.Combine(checks, "DefinitionJson.cs"),
      Path.Combine(dir.Path, "DefinitionJson.cs")
    );

    PlantedDefects.Census census = PlantedDefects.Survey(
      dir.Path,
      typeof(PlantedDefects).Assembly,
      typeof(ChecksProveThemselvesTests).Assembly
    );

    Assert.Contains("LangParity.Check", census.Proven);
    Assert.Contains("VanillaToolTiers.Iron", census.Helpers);
    Assert.Contains(
      "LangParityRules: the file names no type in ExpandedLib.Testing",
      census.Unplanted
    );
  }

  // Fails when Survey counts a skipped test's mark as proof.
  [Fact]
  [PlantedDefect(typeof(PlantedDefects), nameof(PlantedDefects.Survey))]
  public void Survey_counts_no_skipped_test() {
    using var dir = new FixtureDirectory(nameof(SkippedSurveyFixture) + ".cs");

    PlantedDefects.Census census = PlantedDefects.Survey(
      dir.Path,
      typeof(SkippedSurveyFixture).Assembly,
      typeof(SkippedSurveyFixture).Assembly
    );

    Assert.Empty(census.Proven);
    Assert.Equal(
      [
        "SkippedSurveyFixture.Rule",
        "SkippedSurveyFixture.Rule: [PlantedDefect] on SkippedPlants.Skipped, which is skipped",
      ],
      census.Unplanted
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
  private static void NotATest() => PlantedSurveyFixture.Bare();

  /// <summary>A skipped test, hidden from discovery, marking a fixture rule.</summary>
#pragma warning disable xUnit1000
  private sealed class SkippedPlants {
    [Fact(Skip = "a fixture for Survey_counts_no_skipped_test")]
    [PlantedDefect(
      typeof(SkippedSurveyFixture),
      nameof(SkippedSurveyFixture.Rule)
    )]
    public void Skipped() => Assert.Empty(SkippedSurveyFixture.Rule());
  }
#pragma warning restore xUnit1000

  /// <summary>A temporary directory holding empty files, deleted on dispose.</summary>
  private sealed class FixtureDirectory : IDisposable {
    public FixtureDirectory(params string[] files) {
      Path = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(),
        "exlib_survey_" + Guid.NewGuid().ToString("N")
      );
      Directory.CreateDirectory(Path);
      foreach (string file in files)
        File.WriteAllText(System.IO.Path.Combine(Path, file), "");
    }

    public string Path { get; }

    public void Dispose() => Directory.Delete(Path, recursive: true);
  }

  #endregion

  #region Unproven

  // Fails when Unproven counts a bare guard proven, passes a [GuardOf] naming no member, a helper or
  // a file naming no type, or stops reading [GuardOf] or a guard's own planted rule.
  [Fact]
  [PlantedDefect(typeof(PlantedDefects), nameof(PlantedDefects.Unproven))]
  public void Unproven_names_the_bare_guard_the_stray_mark_and_the_typeless_file() {
    using var dir = new FixtureDirectory(
      nameof(UnprovenFixtureGuarded) + ".cs",
      nameof(UnprovenFixtureOwnRule) + ".cs",
      nameof(UnprovenFixtureBare) + ".cs",
      nameof(UnprovenFixtureStray) + ".cs",
      nameof(UnprovenFixtureHelper) + ".cs",
      "NoSuchGuard.cs"
    );

    IReadOnlyList<string> unproven = PlantedDefects.Unproven(
      typeof(ChecksProveThemselvesTests).Assembly,
      dir.Path
    );

    Assert.Equal(
      [
        "NoSuchGuard: the file names no type in ExpandedLib.Tests",
        nameof(UnprovenFixtureBare),
        "UnprovenFixtureHelper: [GuardOf] names DefinitionJson.Parse, a [CheckHelper], which "
          + "proves nothing",
        "UnprovenFixtureStray: [GuardOf] names HarnessUse.Gone, no public static member",
      ],
      unproven
    );
  }

  // Fails when Unproven passes a folder with no guard in it.
  [Fact]
  [PlantedDefect(typeof(PlantedDefects), nameof(PlantedDefects.Unproven))]
  public void Unproven_over_an_empty_folder_throws() {
    using var dir = new FixtureDirectory();

    Assert.Throws<InvalidOperationException>(() =>
      PlantedDefects.Unproven(
        typeof(ChecksProveThemselvesTests).Assembly,
        dir.Path
      )
    );
  }

  // Fails when the fixture guard's own rule stops naming an empty code.
  [Fact]
  [PlantedDefect(
    typeof(UnprovenFixtureOwnRule),
    nameof(UnprovenFixtureOwnRule.Blank)
  )]
  public void The_fixture_guards_rule_names_an_empty_code() {
    Assert.Single(UnprovenFixtureOwnRule.Blank(["a", ""]));
  }

  #endregion
}

/// <summary>A guard calling a check.</summary>
[GuardOf(typeof(HarnessUse), nameof(HarnessUse.HalfBehaviours))]
internal sealed class UnprovenFixtureGuarded { }

/// <summary>A guard exposing its own rule.</summary>
internal static class UnprovenFixtureOwnRule {
  public static IReadOnlyList<string> Blank(IEnumerable<string> codes) =>
    [.. codes.Where(c => c.Length == 0).Select(_ => "an empty code")];
}

/// <summary>A guard naming a helper, which has no planted test to inherit.</summary>
[GuardOf(typeof(DefinitionJson), nameof(DefinitionJson.Parse))]
internal sealed class UnprovenFixtureHelper { }

/// <summary>A guard that proves nothing.</summary>
internal sealed class UnprovenFixtureBare { }

/// <summary>A guard naming a member its check does not have.</summary>
[GuardOf(typeof(HarnessUse), "Gone")]
internal sealed class UnprovenFixtureStray { }

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

/// <summary>A check type whose one rule only a skipped test marks.</summary>
public static class SkippedSurveyFixture {
  /// <summary>Names nothing.</summary>
  public static IReadOnlyList<string> Rule() => [];
}
