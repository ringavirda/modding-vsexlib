using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary><see cref="HarnessUse"/> over exlib's tests, samples and harness, and against fixtures
/// that each break one of its rules.</summary>
[GuardOf(typeof(HarnessUse), nameof(HarnessUse.CompletionWrites))]
[GuardOf(typeof(HarnessUse), nameof(HarnessUse.HalfBehaviours))]
[GuardOf(typeof(HarnessUse), nameof(HarnessUse.GameConstrainedGenerics))]
[GuardOf(typeof(HarnessUse), nameof(HarnessUse.UncalledGuards))]
[GuardOf(typeof(HarnessUse), nameof(HarnessUse.Unpremised))]
[GuardOf(typeof(HarnessUse), nameof(HarnessUse.SubstituteLoggers))]
[GuardOf(typeof(HarnessUse), nameof(HarnessUse.UncalledPlants))]
public class HarnessUseGuards {
  /// <summary>Guard file, and why it calls no <see cref="Premise"/>.</summary>
  private static readonly Dictionary<string, string> PremiseAllowed = new(
    StringComparer.Ordinal
  ) {
    ["HostProcessParityTests.cs"] =
      "compares two named files; a missing file or class fails the test",
    ["UnprovenGuardTests.cs"] =
      "PlantedDefects.Unproven throws on a folder with no guard",
    ["VersionPinTests.cs"] =
      "fails on its own when no template PackageReference or sample dependency is found",
  };

  /// <summary>Test file, and why it may hand code a substitute logger.</summary>
  private static readonly Dictionary<string, string> LoggerAllowed = new(
    StringComparer.Ordinal
  );

  private const string ThroughExlibChecks =
    "reached through ExlibChecks.All, which runs every registered check";

  /// <summary>Planted test, as <c>file: Type.Member</c>, and how it reaches the member it never
  /// names.</summary>
  private static readonly Dictionary<string, string> PlantAllowed = new(
    StringComparer.Ordinal
  ) {
    ["ChecksProveThemselvesTests.cs: PlantedSurveyFixture.Gone"] =
      "a fixture mark naming no member, for Survey's own test",
    ["ExlibChecksTests.cs: MultiblockCodesCheck.Run"] = ThroughExlibChecks,
    ["ExlibChecksTests.cs: RecipeCodesCheck.Run"] = ThroughExlibChecks,
    ["ExlibChecksTests.cs: LangCoverageCheck.Run"] = ThroughExlibChecks,
    ["ExlibChecksTests.cs: PinnedNetworkNodesCheck.Run"] = ThroughExlibChecks,
    ["ExlibChecksTests.cs: CodePrefixCollisionCheck.Run"] = ThroughExlibChecks,
    ["ExlibChecksTests.cs: DefinitionCatalogueCheck.Run"] = ThroughExlibChecks,
    ["ExlibChecksTests.cs: NetworkNodeContractCheck.Run"] = ThroughExlibChecks,
    ["ExlibChecksTests.cs: LateDefinitionCheck.Run"] = ThroughExlibChecks,
  };

  #region exlib

  // Fails when an exlib test sets StructureComplete through ReflectionHelpers.SetProperty instead
  // of standing or breaking the structure.
  [Fact]
  public void Exlibs_tests_never_force_a_structure_complete() {
    IReadOnlyList<string> files = Premise.NotEmpty(Sources(), "test sources");

    Assert.Contains(
      files,
      f => File.ReadAllText(f).Contains(".StructureComplete")
    );
    IReadOnlyList<string> offenders = HarnessUse.CompletionWrites(files);
    Assert.True(offenders.Count == 0, string.Join("\n", offenders));
  }

  // Fails when a block double such as ExOrientableRig's placer gets BlockBehaviors without
  // CollectibleBehaviors.
  [Fact]
  public void Exlibs_block_doubles_fill_both_behaviour_arrays() {
    IReadOnlyList<string> files = Premise.NotEmpty(Sources(), "test sources");

    Assert.Contains(
      files,
      f => File.ReadAllText(f).Contains(".BlockBehaviors = ")
    );
    IReadOnlyList<string> offenders = HarnessUse.HalfBehaviours(files);
    Assert.True(offenders.Count == 0, string.Join("\n", offenders));
  }

  // Fails when an exlib test class holds a helper like Make<T>() where T : BlockEntity, new().
  [Fact]
  public void Exlibs_test_classes_hold_no_game_constrained_generics() {
    IReadOnlyList<string> offenders = HarnessUse.GameConstrainedGenerics(
      Sources(),
      typeof(HarnessUseGuards).Assembly
    );

    Assert.True(offenders.Count == 0, string.Join("\n", offenders));
  }

  // Fails when a guard in exlib's Invariants names a check in [GuardOf] and never calls it.
  [Fact]
  public void Exlibs_guards_call_the_checks_they_name() {
    string[] files =
    [
      .. Directory
        .GetFiles(
          Path.Combine(
            RepoPaths.Root,
            "tests",
            "ExpandedLib.Tests",
            "Invariants"
          ),
          "*.cs"
        )
        // This file carries the rule's fixtures as text.
        .Where(f =>
          !f.EndsWith("/HarnessUseGuards.cs", StringComparison.Ordinal)
        ),
    ];

    Assert.Contains(files, f => File.ReadAllText(f).Contains("[GuardOf("));
    IReadOnlyList<string> offenders = HarnessUse.UncalledGuards(files);
    Assert.True(offenders.Count == 0, string.Join("\n", offenders));
  }

  // Fails when a guard in exlib's Invariants reads a corpus with no Premise and is not allowed.
  [Fact]
  public void Exlibs_guards_assert_their_corpus() {
    FindingLists.Assert(
      HarnessUse.Unpremised(
        Directory.GetFiles(
          Path.Combine(
            RepoPaths.Root,
            "tests",
            "ExpandedLib.Tests",
            "Invariants"
          ),
          "*.cs"
        )
      ),
      PremiseAllowed,
      new Dictionary<string, string>(),
      f => f.Split(':')[0]
    );
  }

  // Fails when an exlib test hands code a substitute ILogger instead of a RecordingLogger.
  [Fact]
  public void Exlibs_tests_log_where_the_log_rule_reads() {
    FindingLists.Assert(
      HarnessUse.SubstituteLoggers(Premise.NotEmpty(Sources(), "test sources")),
      LoggerAllowed,
      new Dictionary<string, string>(),
      f => f.Split(':')[0]
    );
  }

  // Fails when an exlib test marked [PlantedDefect] never reaches the member it claims to prove.
  [Fact]
  public void Exlibs_planted_tests_name_their_member() {
    FindingLists.Assert(
      HarnessUse.UncalledPlants(
        Premise.NotEmpty(
          [
            .. Sources(),
            Path.Combine(
              RepoPaths.Root,
              "tests",
              "ExpandedLib.Tests",
              "Invariants",
              "HarnessUseGuards.cs"
            ),
          ],
          "test sources"
        )
      ),
      PlantAllowed,
      new Dictionary<string, string>(),
      f =>
        f.Split(':')[0]
        + ": "
        + System
          .Text.RegularExpressions.Regex.Match(f, @" names (\S+), ")
          .Groups[1]
          .Value
    );
  }

  #endregion

  #region Rules

  // Fails when CompletionWrites stops matching SetProperty.
  [Fact]
  [PlantedDefect(typeof(HarnessUse), nameof(HarnessUse.CompletionWrites))]
  public void A_completion_set_through_SetProperty_is_named() {
    IReadOnlyList<string> offenders = Scan(
      HarnessUse.CompletionWrites,
      "var be = Make();\n"
        + "ReflectionHelpers.SetProperty(be, nameof(be.StructureComplete), false);"
    );

    Assert.EndsWith(
      ":2: StructureComplete written by reflection; build or break the structure instead",
      Assert.Single(offenders)
    );
  }

  // Fails when CompletionWrites stops matching a PropertyInfo.SetValue.
  [Fact]
  [PlantedDefect(typeof(HarnessUse), nameof(HarnessUse.CompletionWrites))]
  public void A_completion_set_through_SetValue_is_named() {
    Assert.Single(
      Scan(
        HarnessUse.CompletionWrites,
        "typeof(T).GetProperty(\"StructureComplete\")!\n  .SetValue(be, true);"
      )
    );
  }

  // Fails when CompletionWrites stops matching the backing field written by name.
  [Fact]
  [PlantedDefect(typeof(HarnessUse), nameof(HarnessUse.CompletionWrites))]
  public void A_completion_set_through_its_backing_field_is_named() {
    Assert.Single(
      Scan(
        HarnessUse.CompletionWrites,
        "Poke(be, \"<StructureComplete>k__BackingField\", true);"
      )
    );
  }

  // Fails when CompletionWrites stops following a PropertyInfo local bound to the name.
  [Fact]
  [PlantedDefect(typeof(HarnessUse), nameof(HarnessUse.CompletionWrites))]
  public void A_completion_set_through_a_reflection_local_is_named() {
    Assert.EndsWith(
      ":2: StructureComplete written by reflection through a local; build or break the structure "
        + "instead",
      Assert.Single(
        Scan(
          HarnessUse.CompletionWrites,
          "var p = typeof(T).GetProperty(\"StructureComplete\");\np!.SetValue(be, true);"
        )
      )
    );
  }

  // Fails when CompletionWrites stops matching a subclass writing through the protected setter.
  [Fact]
  [PlantedDefect(typeof(HarnessUse), nameof(HarnessUse.CompletionWrites))]
  public void A_completion_set_by_a_subclass_through_its_setter_is_named() {
    Assert.EndsWith(
      ":2: StructureComplete written through its setter by a subclass; build or break the "
        + "structure instead",
      Assert.Single(
        Scan(
          HarnessUse.CompletionWrites,
          "class Spy : BlockEntityMultiblockStructure {\n"
            + "  public void Force() { StructureComplete = true; }\n}"
        )
      )
    );
  }

  // Fails when CompletionWrites names a comparison, an expression body, or another member's local.
  [Fact]
  public void A_comparison_and_another_members_local_are_not_named() {
    Assert.Empty(
      Scan(
        HarnessUse.CompletionWrites,
        "var q = typeof(T).GetProperty(\"Other\");\n"
          + "q.SetValue(be, 1);\n"
          + "bool c = be.StructureComplete == true;\n"
          + "public bool Done => StructureComplete;"
      )
    );
  }

  // Fails when CompletionWrites reads comments, or matches across statements.
  [Fact]
  public void A_read_a_comment_and_another_members_write_are_not_named() {
    Assert.Empty(
      Scan(
        HarnessUse.CompletionWrites,
        "// ReflectionHelpers.SetProperty(be, nameof(be.StructureComplete), true);\n"
          + "Assert.True(be.StructureComplete);\n"
          + "ReflectionHelpers.SetProperty(be, \"Other\", 1);"
      )
    );
  }

  // Fails when HalfBehaviours stops matching a BlockBehaviors assignment.
  [Fact]
  [PlantedDefect(typeof(HarnessUse), nameof(HarnessUse.HalfBehaviours))]
  public void BlockBehaviors_alone_is_named() {
    Assert.EndsWith(
      ":2: torch sets BlockBehaviors but not CollectibleBehaviors, which GetBehavior reads",
      Assert.Single(
        Scan(
          HarnessUse.HalfBehaviours,
          "var torch = new Block();\ntorch.BlockBehaviors = [new BlockBehaviorCanIgnite(torch)];"
        )
      )
    );
  }

  // Fails when HalfBehaviours pairs CollectibleBehaviors on another receiver with this one.
  [Fact]
  [PlantedDefect(typeof(HarnessUse), nameof(HarnessUse.HalfBehaviours))]
  public void CollectibleBehaviors_on_another_block_does_not_pair() {
    Assert.Single(
      Scan(
        HarnessUse.HalfBehaviours,
        "a.BlockBehaviors = [x];\nb.CollectibleBehaviors = [x];"
      )
    );
  }

  // Fails when HalfBehaviours stops matching an initializer's bare BlockBehaviors.
  [Fact]
  [PlantedDefect(typeof(HarnessUse), nameof(HarnessUse.HalfBehaviours))]
  public void An_initializer_with_BlockBehaviors_alone_is_named() {
    Assert.Contains(
      "an initializer sets BlockBehaviors",
      Assert.Single(
        Scan(
          HarnessUse.HalfBehaviours,
          "var b = new Block { BlockBehaviors = [x] };"
        )
      )
    );
  }

  // Fails when HalfBehaviours names a paired double (the null-forgiving ! kept in the receiver),
  // a comparison, or a comment.
  [Fact]
  public void Paired_arrays_a_comparison_and_a_comment_are_not_named() {
    Assert.Empty(
      Scan(
        HarnessUse.HalfBehaviours,
        "placer!.BlockBehaviors = [b];\n"
          + "placer.CollectibleBehaviors = [b];\n"
          + "var b = new Block { BlockBehaviors = [x], CollectibleBehaviors = [x] };\n"
          + "if (c.BlockBehaviors == null) return;\n"
          + "// d.BlockBehaviors = [x];"
      )
    );
  }

  // Fails when GameConstrainedGenerics stops resolving a constraint on a game class.
  [Fact]
  [PlantedDefect(
    typeof(HarnessUse),
    nameof(HarnessUse.GameConstrainedGenerics)
  )]
  public void A_helper_constrained_on_a_game_class_is_named() {
    Assert.EndsWith(
      ":3: T is constrained on the game type BlockEntity; write one helper per type",
      Assert.Single(
        Scan(
          Generics,
          "[Fact]\npublic void A() { }\n"
            + "private static T Make<T>() where T : BlockEntity, new() => new T();"
        )
      )
    );
  }

  // Fails when GameConstrainedGenerics stops walking a mod type's base classes.
  [Fact]
  [PlantedDefect(
    typeof(HarnessUse),
    nameof(HarnessUse.GameConstrainedGenerics)
  )]
  public void A_helper_constrained_on_a_type_deriving_from_a_game_class_is_named() {
    Assert.Single(
      Scan(
        Generics,
        "[Theory]\npublic void A() { }\n"
          + "private static T Make<T>()\n  where T : EntityPosFixture\n{\n  return null;\n}"
      )
    );
  }

  // Fails when GameConstrainedGenerics stops reading a type's interfaces.
  [Fact]
  [PlantedDefect(
    typeof(HarnessUse),
    nameof(HarnessUse.GameConstrainedGenerics)
  )]
  public void A_helper_constrained_on_a_type_implementing_a_game_interface_is_named() {
    Assert.Single(
      Scan(
        Generics,
        "[Fact]\npublic void A() { }\n"
          + "private static void Use<T>(T t) where T : ByteSerializableFixture { }"
      )
    );
  }

  // Fails when GameConstrainedGenerics scans a file with no test in it, names a constraint on a
  // non-game type or a keyword, or reads a comment.
  [Fact]
  public void Helpers_outside_tests_or_on_other_types_are_not_named() {
    Assert.Empty(
      Scan(
        Generics,
        "public static T Make<T>() where T : BlockEntity, new() => new T();"
      )
    );
    Assert.Empty(
      Scan(
        Generics,
        "[Fact]\npublic void A() { }\n"
          + "private static T Max<T>(T a) where T : IComparable<T>, new() => a;\n"
          + "private static T Min<T, U>(T a) where T : class, U where U : notnull => a;\n"
          + "// where T : BlockEntity"
      )
    );
  }

  // Fails when UncalledGuards passes a guard that never calls the check it names.
  [Fact]
  [PlantedDefect(typeof(HarnessUse), nameof(HarnessUse.UncalledGuards))]
  public void A_guard_naming_a_check_it_never_calls_is_named() {
    Assert.EndsWith(
      ":2: [GuardOf] names SoundUse.ShortRepeats, which the file never calls",
      Assert.Single(
        Scan(
          HarnessUse.UncalledGuards,
          "[GuardOf(typeof(SoundUse), nameof(SoundUse.DirectSounds))]\n"
            + "[GuardOf(typeof(SoundUse), nameof(SoundUse.ShortRepeats))]\n"
            + "public class G { void A() => SoundUse.DirectSounds(files); }\n"
            + "// SoundUse.ShortRepeats(files);"
        )
      )
    );
  }

  // Fails when UncalledGuards stops reading a qualified type, a string member or a method group.
  [Fact]
  [PlantedDefect(typeof(HarnessUse), nameof(HarnessUse.UncalledGuards))]
  public void A_guard_calling_its_checks_in_any_form_is_not_named() {
    Assert.Empty(
      Scan(
        HarnessUse.UncalledGuards,
        "[GuardOf(typeof(ExpandedLib.Testing.SoundUse), \"ShortRepeats\")]\n"
          + "[GuardOf(typeof(HarnessUse), nameof(HarnessUse.HalfBehaviours))]\n"
          + "public class G {\n"
          + "  void A() => Run(SoundUse.ShortRepeats, HarnessUse\n    .HalfBehaviours);\n}"
      )
    );
    Assert.Single(
      Scan(
        HarnessUse.UncalledGuards,
        "[GuardOf(typeof(SoundUse), \"ShortRepeats\")]\npublic class G { }"
      )
    );
  }

  // Fails when Unpremised passes a file with no Premise call, or names one that makes it.
  [Fact]
  [PlantedDefect(typeof(HarnessUse), nameof(HarnessUse.Unpremised))]
  public void A_guard_file_calling_no_Premise_is_named() {
    Assert.Equal(
      ": calls no Premise, so an empty corpus passes",
      Assert.Single(
        Scan(
          HarnessUse.Unpremised,
          "public class G {\n  // Premise.NotEmpty(files, \"x\");\n}"
        )
      )[^45..]
    );
    Assert.Empty(
      Scan(
        HarnessUse.Unpremised,
        "var f = Premise\n  .NotEmpty(Files(), \"x\");"
      )
    );
    Assert.Empty(
      Scan(HarnessUse.Unpremised, "Premise.Covers(read, \"iiex\");")
    );
  }

  // Fails when UncalledPlants passes a planted test that never reaches its member, directly or
  // through a helper of the file, or reads a comment or a string literal as naming it.
  [Fact]
  [PlantedDefect(typeof(HarnessUse), nameof(HarnessUse.UncalledPlants))]
  public void A_planted_test_that_never_reaches_its_member_is_named() {
    IReadOnlyList<string> offenders = Scan(
      HarnessUse.UncalledPlants,
      "[Fact]\n"
        + "[PlantedDefect(typeof(LangKeys), nameof(LangKeys.Check))]\n"
        + "public void Empty() { Assert.True(true); }\n"
        + "[Fact]\n"
        + "[PlantedDefect(\n  typeof(ExpandedLib.Testing.SoundUse),\n  \"ShortRepeats\"\n)]\n"
        + "public void Quoted() =>\n"
        + "  Assert.Empty(Other(\"ShortRepeats\")); // SoundUse.ShortRepeats\n"
        + "[Fact]\n[PlantedDefect(typeof(G), nameof(G.Rule))]\n"
        + "public void Helped() => Assert.Empty(Helper());\n"
        + "private static string[] Helper() => G.Other();\n"
    );

    Assert.Equal(
      [
        ":2: [PlantedDefect] on Empty names LangKeys.Check, which it never reaches",
        ":5: [PlantedDefect] on Quoted names SoundUse.ShortRepeats, which it never reaches",
        ":12: [PlantedDefect] on Helped names G.Rule, which it never reaches",
      ],
      offenders.Select(o => o[o.IndexOf(':')..])
    );
  }

  // Fails when UncalledPlants names a test that names its member as a method group or a bare call,
  // after other attributes, braces in a string or a nested block, or through a chain of the file's
  // helpers, or reads a mark in a string.
  [Fact]
  public void A_planted_test_reaching_its_member_is_not_named() {
    Assert.Empty(
      Scan(
        HarnessUse.UncalledPlants,
        "[Fact]\n[PlantedDefect(typeof(HarnessUse), nameof(HarnessUse.Unpremised))]\n"
          + "[Trait(\"a\", \"b\")]\n"
          + "public void A() {\n  var s = $\"}{x[\"k\"]}\";\n  if (x) { Y('}'); }\n"
          + "  Scan(HarnessUse\n    .Unpremised, s);\n}\n"
          + "[Fact]\n[PlantedDefect(typeof(G), nameof(G.Rule))]\n"
          + "public void B() => Assert.Single(Rule(-1));\n"
          + "[Fact]\n[PlantedDefect(typeof(G), nameof(G.Deep))]\n"
          + "public void D() => Assert.Empty(Outer());\n"
          + "private static IReadOnlyList<string> Outer() => Inner(1);\n"
          + "static List<string> Inner(int x) {\n  return G.Deep(x);\n}\n"
          + "string t = \"[PlantedDefect(typeof(G), nameof(G.Gone))] void C() { }\";\n"
      )
    );
  }

  // Fails when SubstituteLoggers stops matching a substitute logger, bare or qualified.
  [Fact]
  [PlantedDefect(typeof(HarnessUse), nameof(HarnessUse.SubstituteLoggers))]
  public void A_substitute_logger_is_named() {
    IReadOnlyList<string> offenders = Scan(
      HarnessUse.SubstituteLoggers,
      "var api = Substitute.For<ICoreAPI>();\n"
        + "api.Logger.Returns(Substitute.For< ILogger >());\n"
        + "var log = Substitute\n  .For<Vintagestory.API.Common.ILogger, IDisposable>();"
    );

    Assert.Equal(2, offenders.Count);
    Assert.EndsWith(
      ":2: a substitute ILogger hides its entries from the log rule; log into a RecordingLogger",
      offenders[0]
    );
    Assert.Contains(":3: ", offenders[1]);
  }

  // Fails when SubstituteLoggers names a recording logger, another substitute, an argument matcher
  // or a comment.
  [Fact]
  public void A_recording_logger_a_matcher_and_a_comment_are_not_named() {
    Assert.Empty(
      Scan(
        HarnessUse.SubstituteLoggers,
        "api.Logger.Returns(new RecordingLogger());\n"
          + "var l = Substitute.For<ILoggerFactory>();\n"
          + "assets.GetMany<JToken>(Arg.Any<ILogger>(), \"recipes\");\n"
          + "// api.Logger.Returns(Substitute.For< ILogger >());"
      )
    );
  }

  #endregion

  private static IReadOnlyList<string> Generics(IEnumerable<string> files) =>
    HarnessUse.GameConstrainedGenerics(
      files,
      typeof(HarnessUseGuards).Assembly
    );

  private static string[] Sources() =>
    new[]
    {
      Path.Combine(RepoPaths.Root, "tests"),
      Path.Combine(RepoPaths.Root, "samples"),
      Path.Combine(RepoPaths.Root, "src", "ExpandedLib.Testing"),
    }
      .SelectMany(d =>
        Directory.EnumerateFiles(d, "*.cs", SearchOption.AllDirectories)
      )
      .Where(f =>
        !f.Contains("/bin/", StringComparison.Ordinal)
        && !f.Contains("/obj/", StringComparison.Ordinal)
        // The law and this file carry each rule's pattern as text.
        && !f.EndsWith("/HarnessUse.cs", StringComparison.Ordinal)
        && !f.EndsWith("/HarnessUseGuards.cs", StringComparison.Ordinal)
        && (
          f.Contains("/tests/", StringComparison.Ordinal)
          || f.Contains("/ExpandedLib.Testing/", StringComparison.Ordinal)
        )
      )
      .ToArray();

  private static IReadOnlyList<string> Scan(
    System.Func<IEnumerable<string>, IReadOnlyList<string>> rule,
    string source
  ) {
    string file = Path.Combine(
      Path.GetTempPath(),
      $"harnessuse-{Guid.NewGuid():N}.cs"
    );
    File.WriteAllText(file, source);
    try {
      return rule([file]);
    } finally {
      File.Delete(file);
    }
  }
}

internal sealed class ByteSerializableFixture : IByteSerializable {
  public void ToBytes(BinaryWriter writer) { }

  public void FromBytes(BinaryReader reader, IWorldAccessor resolver) { }
}

internal sealed class EntityPosFixture : EntityPos { }
