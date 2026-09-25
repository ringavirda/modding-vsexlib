using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using ExpandedLib.Testing;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>Every public type of <c>exlib.dll</c> outside <c>ExpandedLib.Industry</c> is named on
/// the Supported API page or carries <c>[EditorBrowsable(Never)]</c>.</summary>
public class PublicSurfaceTests {
  // Words the page's tables use in code spans that are not type names.
  private static readonly string[] NotATypeName = [];

  private static string PagePath =>
    Path.Combine(RepoPaths.Wiki, "Supported-API.md");

  // Only the Type column of a table row: `| \`Name\` | ... |`.
  private static readonly Regex TableRow = new(
    @"^\|\s*`([A-Za-z][A-Za-z0-9_.<>,\s]*)`\s*\|",
    RegexOptions.Multiline
  );

  private static bool IsPubliclyVisible(Type t) {
    if (t.IsPublic)
      return true;
    if (!t.IsNestedPublic)
      return false;
    for (Type? d = t.DeclaringType; d != null; d = d.DeclaringType) {
      if (!(d.IsPublic || d.IsNestedPublic))
        return false;
    }
    return true;
  }

  // A C# 14 extension block's synthetic container and method-holder types carry '<' in their
  // name; no real identifier does.
  private static bool IsCompilerSynthesized(Type t) =>
    t.Name.Contains('<')
    || t.IsDefined(typeof(CompilerGeneratedAttribute), false);

  // Publicly visible, outside ExpandedLib.Industry, written by hand and not a ref struct.
  private static IEnumerable<Type> ContractTypes(IEnumerable<Type> types) =>
    types
      .Where(IsPubliclyVisible)
      .Where(t =>
        t.Namespace != null && !t.Namespace.StartsWith("ExpandedLib.Industry")
      )
      .Where(t => !IsCompilerSynthesized(t))
      .Where(t =>
        t.GetCustomAttributesData()
          .All(a => a.AttributeType.Name != "IsByRefLikeAttribute")
      );

  private static IEnumerable<Type> ContractTypes() =>
    ContractTypes(typeof(ExpandedLibModSystem).Assembly.GetTypes());

  private static bool IsHidden(Type t) =>
    t.GetCustomAttributesData()
      .Any(a => a.AttributeType == typeof(EditorBrowsableAttribute));

  // Strips generic arity: List`1 -> List.
  private static string SimpleName(Type t) {
    int tick = t.Name.IndexOf('`');
    return tick < 0 ? t.Name : t.Name[..tick];
  }

  // A nested type is named `Outer.Inner` on the page.
  private static string QualifiedName(Type t) =>
    t.IsNested
      ? $"{SimpleName(t.DeclaringType!)}.{SimpleName(t)}"
      : SimpleName(t);

  private static int Arity(Type t) => t.GetGenericArguments().Length;

  // A row's first cell, split into the bare name and its declared arity: `Foo` is arity 0,
  // `Foo<T>` is arity 1, `Foo<T, U>` is arity 2.
  private static (string Name, int Arity) ParseRow(string raw) {
    int lt = raw.IndexOf('<');
    if (lt < 0)
      return (raw, 0);
    int gt = raw.LastIndexOf('>');
    int arity = raw[(lt + 1)..gt].Count(c => c == ',') + 1;
    return (raw[..lt], arity);
  }

  private static IEnumerable<string> ListedNames(string page) =>
    TableRow.Matches(page).Select(m => m.Groups[1].Value);

  /// <summary>Each contract type of <paramref name="types"/> (publicly visible, outside
  /// <c>ExpandedLib.Industry</c>, not compiler-made) that carries no
  /// <see cref="EditorBrowsableAttribute"/> and no row of <paramref name="page"/> names.</summary>
  /// <param name="page">Markdown whose table rows open with the type in a code span: a nested
  /// type as <c>Outer.Inner</c>, a generic one with its parameters.</param>
  /// <returns><c>Namespace.Name</c> per type, in input order.</returns>
  public static IReadOnlyList<string> Unlisted(
    IEnumerable<Type> types,
    string page
  ) {
    // Bare name plus the arity its row was written at.
    var listed = ListedNames(page).Select(ParseRow).ToHashSet();
    var missing = new List<string>();
    foreach (Type t in ContractTypes(types)) {
      if (IsHidden(t))
        continue;
      string name = QualifiedName(t);
      if (!listed.Contains((name, Arity(t))))
        missing.Add($"{t.Namespace}.{name}");
    }
    return missing;
  }

  [Fact]
  public void Every_public_type_outside_Industry_is_listed_or_hidden() {
    string page = File.ReadAllText(PagePath);
    List<string> missing =
    [
      .. Unlisted(
        Premise.NotEmpty(ContractTypes(), "public contract types"),
        page
      ),
    ];

    Assert.True(
      missing.Count == 0,
      $"{missing.Count} public type(s) outside ExpandedLib.Industry are neither listed on "
        + "Supported-API.md nor marked [EditorBrowsable(Never)]:\n  "
        + string.Join("\n  ", missing)
    );
  }

  /// <summary>Each type name a table row of <paramref name="page"/> opens with that resolves to no
  /// publicly visible type of <paramref name="types"/> at the row's arity, nor to one of
  /// <paramref name="bareNames"/>.</summary>
  /// <param name="bareNames">Names that resolve at arity 0 with no type behind them.</param>
  /// <returns>Each unresolved code span as written, once, in page order.</returns>
  /// <remarks>A dotted name also resolves by its last segment.</remarks>
  public static IReadOnlyList<string> Unresolved(
    string page,
    IEnumerable<Type> types,
    IEnumerable<string> bareNames
  ) {
    var resolvable = new Dictionary<string, HashSet<int>>();
    void Add(string name, int arity) {
      if (!resolvable.TryGetValue(name, out var arities))
        resolvable[name] = arities = new HashSet<int>();
      arities.Add(arity);
    }

    foreach (
      Type t in types
        .Where(IsPubliclyVisible)
        .Where(t => !IsCompilerSynthesized(t))
    ) {
      int arity = Arity(t);
      Add(SimpleName(t), arity);
      Add(QualifiedName(t), arity);
    }
    foreach (string name in bareNames)
      Add(name, 0);

    var missing = new List<string>();
    foreach (Match m in TableRow.Matches(page)) {
      string raw = m.Groups[1].Value;
      (string name, int arity) = ParseRow(raw);
      if (NotATypeName.Contains(name))
        continue;
      if (
        resolvable.TryGetValue(name, out var arities) && arities.Contains(arity)
      )
        continue;
      // Fall back to the last dotted segment.
      string lastSegment = name.Contains('.')
        ? name[(name.LastIndexOf('.') + 1)..]
        : name;
      if (
        resolvable.TryGetValue(lastSegment, out var lastArities)
        && lastArities.Contains(arity)
      )
        continue;
      missing.Add(raw);
    }
    return [.. missing.Distinct()];
  }

  [Fact]
  public void Every_listed_type_exists() {
    string page = File.ReadAllText(PagePath);

    // A name resolves when it is a public or nested-public type of exlib.dll or
    // exlib.industry.dll, a generator type, or a legacy type gated by `#if !GAME_GE_*`.
    IReadOnlyList<string> missing = Unresolved(
      page,
      typeof(ExpandedLibModSystem)
        .Assembly.GetTypes()
        .Concat(typeof(Industry.IndustryModule).Assembly.GetTypes()),
      GeneratorTypeNames().Concat(LegacyOnlyTypeNames())
    );

    Assert.True(
      missing.Count == 0,
      $"{missing.Count} name(s) on Supported-API.md do not resolve to a public exlib type:\n  "
        + string.Join("\n  ", missing)
    );
  }

  /// <summary>The simple names of the contract types of <paramref name="types"/> that appear in
  /// none of <paramref name="testTexts"/>, ordinal-sorted.</summary>
  public static IReadOnlyList<string> Untested(
    IEnumerable<Type> types,
    IEnumerable<string> testTexts
  ) {
    string[] texts = [.. testTexts];
    return
    [
      .. ContractTypes(types)
        .Select(SimpleName)
        .Distinct()
        .Where(name =>
          !texts.Any(t => t.Contains(name, StringComparison.Ordinal))
        )
        .OrderBy(n => n, StringComparer.Ordinal),
    ];
  }

  /// <summary>Reports every public contract type whose simple name appears in no test file outside
  /// this guard directory, as a soft ceiling.</summary>
  [Fact]
  public void Untested_public_types_are_reported() {
    string testsDir = Path.Combine(
      RepoPaths.Root,
      "tests",
      "ExpandedLib.Tests"
    );
    string invariantsDir =
      Path.Combine(testsDir, "Invariants") + Path.DirectorySeparatorChar;

    List<string> testText =
    [
      .. Directory
        .EnumerateFiles(testsDir, "*.cs", SearchOption.AllDirectories)
        .Where(f =>
          !f.Contains(
            $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"
          )
        )
        .Where(f =>
          !f.Contains(
            $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"
          )
        )
        .Where(f => !f.StartsWith(invariantsDir, StringComparison.Ordinal))
        .Select(File.ReadAllText),
    ];

    IReadOnlyList<string> untested = Untested(
      typeof(ExpandedLibModSystem).Assembly.GetTypes(),
      testText
    );

    Assert.True(
      untested.Count < 60,
      $"{untested.Count} untested public type(s):\n  "
        + string.Join("\n  ", untested)
    );
  }

  // The generators ship no runtime assembly; their names come from source, not reflection.
  private static IEnumerable<string> GeneratorTypeNames() {
    string dir = Path.Combine(RepoPaths.Root, "src/ExpandedLib.Generators");
    var declared = new Regex(
      @"\bclass\s+(?<name>\w+)\s*:\s*[^{\r\n]*\bIIncrementalGenerator\b"
    );
    foreach (string file in Directory.EnumerateFiles(dir, "*.cs"))
      foreach (Match m in declared.Matches(File.ReadAllText(file)))
        yield return m.Groups["name"].Value;
  }

  // A file compiled only for a game version below the current one (`#if !GAME_GE_<ver>`).
  private static readonly Regex LegacyGuard = new(@"#if\s+!GAME_GE_\d+_\d+");
  private static readonly Regex LegacyTypeDecl = new(
    @"^\s*public\s+(?:static\s+|sealed\s+|abstract\s+|partial\s+|readonly\s+)*"
      + @"(?:class|struct|record|interface|enum)\s+(\w+)",
    RegexOptions.Multiline
  );

  private static IEnumerable<string> LegacyOnlyTypeNames() {
    string srcDir = RepoPaths.Src("exlib");
    foreach (
      string file in Directory.EnumerateFiles(
        srcDir,
        "*.cs",
        SearchOption.AllDirectories
      )
    ) {
      string text = File.ReadAllText(file);
      if (!LegacyGuard.IsMatch(text))
        continue;
      foreach (Match m in LegacyTypeDecl.Matches(text))
        yield return m.Groups[1].Value;
    }
  }

  // Fails when Unlisted passes an unlisted type or one listed at another arity, or names a hidden
  // type, an Industry one or a listed one.
  [Fact]
  [PlantedDefect(typeof(PublicSurfaceTests), nameof(Unlisted))]
  public void An_unlisted_public_type_is_named() {
    Assert.Equal(
      [
        "ExpandedLib.Registries.ExKeyedRegistry",
        "ExpandedLib.Registries.RegistrySubCommand",
      ],
      Unlisted(
        [
          typeof(ExpandedLib.Registries.ExKeyedRegistry<>),
          typeof(ExpandedLib.Registries.RegistrySubCommand<>),
          typeof(ExlibConfig),
          typeof(Industry.IndustryModule),
          typeof(ExpandedLib.Registries.ExHarmony),
        ],
        "| `ExKeyedRegistry` | no arity |\n"
          + "| `ExHarmony` | listed |\n"
          + "| x | `RegistrySubCommand<T>` in another column |\n"
      )
    );
  }

  // Fails when Unresolved passes a name no type carries or one at the wrong arity, drops the
  // last-segment or bare-name fallback, or names a row twice.
  [Fact]
  [PlantedDefect(typeof(PublicSurfaceTests), nameof(Unresolved))]
  public void A_listed_name_no_type_carries_is_named() {
    Assert.Equal(
      ["Gone", "ExlibConfig<T>"],
      Unresolved(
        "| `ExlibConfig` | a type |\n"
          + "| `Gone` | none |\n"
          + "| `ExlibConfig<T>` | wrong arity |\n"
          + "| `Some.Outer.ExlibConfig` | dotted |\n"
          + "| `ExlibLang` | generated |\n"
          + "| `Gone` | twice |\n",
        [typeof(ExlibConfig)],
        ["ExlibLang"]
      )
    );
  }

  // Fails when Untested passes a type no test text names or names one a text names.
  [Fact]
  [PlantedDefect(typeof(PublicSurfaceTests), nameof(Untested))]
  public void A_public_type_no_test_names_is_reported() {
    Assert.Equal(
      ["ExlibConfig"],
      Untested(
        [typeof(ExlibConfig), typeof(ExpandedLib.Registries.ExKeyedRegistry<>)],
        ["var r = new ExKeyedRegistry<int>(x => x);"]
      )
    );
  }
}
