using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using ExpandedLib.Industry;
using ExpandedLib.Testing;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>Boundary guard: the family-specific half of exlib lives in
/// <c>exlib.industry.dll</c> under <c>ExpandedLib.Industry.*</c>; the domain layer may depend on
/// the framework, never the reverse.</summary>
public class IndustryBoundaryTests {
  #region Corpus

  private static readonly string[] IndustryTypeNames =
  [
    "PipeNetwork",
    "MoltenNetwork",
    "MpEnergyNetwork",
    "MetalRegistry",
    "HeatBalance",
    "ExSounds",
    "ExParticles",
    "ExMoldGate",
    "ExMoldDrops",
    "BEBehaviorMoltenCell",
    "BEBehaviorMPFillerPort",
    "MPAnim",
    "Roles",
  ];

  #endregion

  /// <summary>Each of <paramref name="names"/> that no type of <paramref name="industryTypes"/>
  /// carries, or whose type lies outside <c>ExpandedLib.Industry</c>.</summary>
  /// <returns>One message per name, in input order.</returns>
  /// <exception cref="InvalidOperationException">Two of the types carry one name.</exception>
  public static IReadOnlyList<string> MisplacedIndustryTypes(
    IEnumerable<Type> industryTypes,
    IEnumerable<string> names
  ) {
    Type[] types = [.. industryTypes];
    var misplaced = new List<string>();
    foreach (string name in names) {
      Type? type = types.SingleOrDefault(t => t.Name == name);
      if (type == null)
        misplaced.Add(
          $"exlib.industry.dll declares no type named '{name}' - was it renamed, or did it move back "
            + "into the framework assembly?"
        );
      else if (
        type.Namespace?.StartsWith(
          "ExpandedLib.Industry",
          StringComparison.Ordinal
        ) != true
      )
        misplaced.Add(
          $"{name} lives in {type.Namespace}, outside ExpandedLib.Industry"
        );
    }
    return misplaced;
  }

  [Fact]
  public void Every_industry_type_lives_in_the_Industry_assembly() {
    IReadOnlyList<string> misplaced = MisplacedIndustryTypes(
      typeof(IndustryModule).Assembly.GetTypes(),
      IndustryTypeNames
    );

    Assert.True(misplaced.Count == 0, string.Join("\n", misplaced));
  }

  // Namespaces the top-level folders may declare (exdocs/exlib/design/conventions.md "How exlib is laid out").
  private static readonly string[] ContractNamespaces =
  [
    "ExpandedLib",
    "ExpandedLib.Registries",
    "ExpandedLib.Config",
    "ExpandedLib.Definitions",
    "ExpandedLib.Blocks",
    "ExpandedLib.Migrations",
    "ExpandedLib.Structures",
    "ExpandedLib.Machines",
    "ExpandedLib.Networks",
    "ExpandedLib.Catalogues",
    "ExpandedLib.Checks",
    "ExpandedLib.Helpers",
    "ExpandedLib.Legacy",
  ];

  /// <summary>The distinct <c>ExpandedLib*</c> namespaces of <paramref name="types"/> missing
  /// from <paramref name="contract"/>, sorted.</summary>
  public static IReadOnlyList<string> NamespacesOutside(
    IEnumerable<Type> types,
    IEnumerable<string> contract
  ) =>
    [
      .. types
        .Where(t =>
          t.Namespace != null && t.Namespace.StartsWith("ExpandedLib")
        )
        .Select(t => t.Namespace!)
        .Distinct()
        .Where(ns => !contract.Contains(ns))
        .OrderBy(ns => ns),
    ];

  /// <summary>The distinct <c>ExpandedLib*</c> namespaces of <paramref name="types"/> that are
  /// neither <c>ExpandedLib.Industry</c> nor below it, in ordinal order.</summary>
  public static IReadOnlyList<string> NonIndustryNamespaces(
    IEnumerable<Type> types
  ) =>
    [
      .. types
        .Where(t =>
          t.Namespace != null && t.Namespace.StartsWith("ExpandedLib")
        )
        .Where(t => !IsIndustryNamespace(t.Namespace))
        .Select(t => t.Namespace!)
        .Distinct()
        .OrderBy(ns => ns, StringComparer.Ordinal),
    ];

  [Fact]
  public void Every_contract_namespace_is_a_top_level_folder() {
    IReadOnlyList<string> stray = NamespacesOutside(
      typeof(ExpandedLibModSystem).Assembly.GetTypes(),
      ContractNamespaces
    );

    Assert.True(
      stray.Count == 0,
      "namespace(s) outside the folder-mapped contract set: "
        + string.Join(", ", stray)
    );
  }

  [Fact]
  public void The_Industry_assembly_declares_only_Industry_namespaces() {
    IReadOnlyList<string> stray = NonIndustryNamespaces(
      typeof(IndustryModule).Assembly.GetTypes()
    );

    Assert.True(
      stray.Count == 0,
      "exlib.industry.dll declares namespace(s) outside ExpandedLib.Industry: "
        + string.Join(", ", stray)
    );
  }

  #region Reference boundary

  private const string IndustryNamespacePrefix = "ExpandedLib.Industry";

  private static bool IsIndustryNamespace(string? ns) =>
    ns != null
    && (
      ns == "ExpandedLib.Industry"
      || ns.StartsWith(IndustryNamespacePrefix + ".")
    );

  /// <summary>Whether <paramref name="referenced"/> names <paramref name="assembly"/>, ignoring
  /// case.</summary>
  public static bool References(
    IEnumerable<AssemblyName> referenced,
    string assembly
  ) =>
    referenced
      .Select(a => a.Name)
      .Contains(assembly, StringComparer.OrdinalIgnoreCase);

  [Fact]
  public void The_framework_assembly_does_not_reference_the_Industry_one() {
    string industry = typeof(IndustryModule).Assembly.GetName().Name!;

    Assert.False(
      References(
        typeof(ExpandedLibModSystem).Assembly.GetReferencedAssemblies(),
        industry
      ),
      $"{typeof(ExpandedLibModSystem).Assembly.GetName().Name} references {industry}. The framework "
        + "must not depend on the family's domain layer; the dependency runs the other way."
    );
  }

  // Catches a stray using/qualified name before a circular project reference would even compile.
  private static readonly Regex IndustryMention = new(
    @"\bExpandedLib\.Industry\b",
    RegexOptions.Compiled
  );

  /// <summary>Each line of <paramref name="files"/> naming the <c>ExpandedLib.Industry</c>
  /// namespace or one below it.</summary>
  /// <param name="files">Sources as their relative path and lines.</param>
  /// <returns><c>path:line</c> per line, in input order.</returns>
  public static IReadOnlyList<string> IndustryMentions(
    IEnumerable<(string Relative, string[] Lines)> files
  ) =>
    [
      .. files.SelectMany(f =>
        Enumerable
          .Range(0, f.Lines.Length)
          .Where(i => IndustryMention.IsMatch(f.Lines[i]))
          .Select(i => $"{f.Relative}:{i + 1}")
      ),
    ];

  [Fact]
  public void No_framework_source_mentions_the_Industry_namespace() {
    string srcRoot = RepoPaths.Src("exlib");
    IReadOnlyList<string> hits = IndustryMentions(
      Premise
        .NotEmpty(
          Directory.EnumerateFiles(
            srcRoot,
            "*.cs",
            SearchOption.AllDirectories
          ),
          "framework sources"
        )
        .Select(path =>
          (Path.GetRelativePath(srcRoot, path), File.ReadAllLines(path))
        )
    );

    Assert.True(
      hits.Count == 0,
      "framework source under src names ExpandedLib.Industry:\n"
        + string.Join("\n", hits)
    );
  }

  // Fails when MisplacedIndustryTypes passes a missing name or a framework type, or names an
  // Industry one.
  [Fact]
  [PlantedDefect(typeof(IndustryBoundaryTests), nameof(MisplacedIndustryTypes))]
  public void A_missing_or_framework_type_is_named() {
    IReadOnlyList<string> misplaced = MisplacedIndustryTypes(
      [typeof(IndustryModule), typeof(ExpandedLibModSystem)],
      ["IndustryModule", "ExpandedLibModSystem", "Gone"]
    );

    Assert.Equal(
      [
        "ExpandedLibModSystem lives in ExpandedLib, outside ExpandedLib.Industry",
        "exlib.industry.dll declares no type named 'Gone' - was it renamed, or did it move back "
          + "into the framework assembly?",
      ],
      misplaced
    );
  }

  // Fails when NamespacesOutside passes a namespace missing from the contract or names one in it
  // or outside ExpandedLib.
  [Fact]
  [PlantedDefect(typeof(IndustryBoundaryTests), nameof(NamespacesOutside))]
  public void A_namespace_outside_the_contract_is_named() {
    Assert.Equal(
      ["ExpandedLib.Tests"],
      NamespacesOutside(
        [
          typeof(ExpandedLibModSystem),
          typeof(IndustryBoundaryTests),
          typeof(CommentStyleGuards),
          typeof(string),
        ],
        ContractNamespaces
      )
    );
  }

  // Fails when NonIndustryNamespaces passes a framework namespace or names an Industry one.
  [Fact]
  [PlantedDefect(typeof(IndustryBoundaryTests), nameof(NonIndustryNamespaces))]
  public void A_framework_namespace_in_the_Industry_assembly_is_named() {
    Assert.Equal(
      ["ExpandedLib"],
      NonIndustryNamespaces([
        typeof(IndustryModule),
        typeof(ExpandedLibModSystem),
        typeof(string),
      ])
    );
  }

  // Fails when References misses a reference or its case-blind match, or names one absent.
  [Fact]
  [PlantedDefect(typeof(IndustryBoundaryTests), nameof(References))]
  public void A_reference_to_the_named_assembly_is_found() {
    AssemblyName[] industryReferences =
      typeof(IndustryModule).Assembly.GetReferencedAssemblies();
    string framework = typeof(ExpandedLibModSystem).Assembly.GetName().Name!;

    Assert.True(References(industryReferences, framework.ToUpperInvariant()));
    Assert.False(References(industryReferences, "exlib.nowhere"));
  }

  // Fails when IndustryMentions misses a using or a qualified name, or names a longer identifier.
  [Fact]
  [PlantedDefect(typeof(IndustryBoundaryTests), nameof(IndustryMentions))]
  public void A_mention_of_the_Industry_namespace_is_named() {
    Assert.Equal(
      ["a.cs:1", "a.cs:3"],
      IndustryMentions([
        (
          "a.cs",
          [
            "using ExpandedLib.Industry.Metals;",
            "var m = ExpandedLib.IndustryModule.X;",
            "var n = global::ExpandedLib.Industry.Roles.Y;",
          ]
        ),
      ])
    );
  }

  #endregion
}
