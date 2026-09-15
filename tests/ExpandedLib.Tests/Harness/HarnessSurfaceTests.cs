using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ExpandedLib.Testing;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>Every public type <c>ExpandedLib.Testing</c> declares is named on
/// <c>Testing-API-Reference.md</c>, and every type its "Where things are" table claims actually
/// exists.</summary>
public class HarnessSurfaceTests {
  private static readonly string ReferencePath = Path.Combine(
    RepoPaths.Root,
    "wiki",
    "Testing-API-Reference.md"
  );

  private static string ReferenceText => File.ReadAllText(ReferencePath);

  /// <summary>Every public top-level type the harness assembly declares, by its bare name with any
  /// generic arity suffix stripped.</summary>
  private static IEnumerable<string> PublicTypeNames() =>
    typeof(TestWorld)
      .Assembly.GetExportedTypes()
      .Where(t => t.DeclaringType == null)
      .Select(t => t.Name)
      .Select(name => name.Contains('`') ? name[..name.IndexOf('`')] : name)
      .Distinct();

  public static IEnumerable<object[]> Types() =>
    PublicTypeNames()
      .OrderBy(n => n, StringComparer.Ordinal)
      .Select(n => new object[] { n });

  [Theory]
  [MemberData(nameof(Types))]
  public void Every_public_type_is_named_on_the_reference_page(string typeName) {
    Assert.True(
      Regex.IsMatch(ReferenceText, $@"\b{Regex.Escape(typeName)}\b"),
      $"{typeName} is public in ExpandedLib.Testing but is not mentioned anywhere in {ReferencePath}"
    );
  }

  [Fact]
  public void Every_type_named_in_the_where_things_are_table_exists() {
    var known = PublicTypeNames().ToHashSet(StringComparer.Ordinal);

    // The "Where things are" table's rows, up to the next blank line.
    string[] lines = ReferenceText
      .Split('\n')
      .SkipWhile(l => !l.StartsWith("## Where things are"))
      .Skip(1)
      .SkipWhile(string.IsNullOrWhiteSpace)
      .TakeWhile(l => !string.IsNullOrWhiteSpace(l))
      .Where(l => l.StartsWith('|'))
      .ToArray();

    // A fully-backtick-enclosed token, optionally generic.
    var claimed = new List<string>();
    foreach (string line in lines)
      foreach (
        Match m in Regex.Matches(line, @"`([A-Za-z][A-Za-z0-9]*)(?:<[^>`]*>)?`")
      )
        claimed.Add(m.Groups[1].Value);

    Assert.NotEmpty(claimed);

    var unknown = claimed.Where(n => !known.Contains(n)).Distinct().ToList();
    Assert.True(
      unknown.Count == 0,
      "Testing-API-Reference.md's \"Where things are\" table names a type that does not exist: "
        + string.Join(", ", unknown)
    );
  }
}
