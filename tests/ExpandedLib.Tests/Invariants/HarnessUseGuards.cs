using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary><see cref="HarnessUse"/> over exlib's tests, samples and harness, and against fixtures
/// that each break one of its rules.</summary>
public class HarnessUseGuards {
  #region exlib

  // Fails when an exlib test sets StructureComplete through ReflectionHelpers.SetProperty instead
  // of standing or breaking the structure.
  [Fact]
  public void Exlibs_tests_never_force_a_structure_complete() {
    string[] files = Sources();

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
    string[] files = Sources();

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

  #endregion

  #region Rules

  // Fails when CompletionWrites stops matching SetProperty.
  [Fact]
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
  public void A_completion_set_through_its_backing_field_is_named() {
    Assert.Single(
      Scan(
        HarnessUse.CompletionWrites,
        "Poke(be, \"<StructureComplete>k__BackingField\", true);"
      )
    );
  }

  // Fails when CompletionWrites reads comments, or judges the whole file instead of one statement.
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
