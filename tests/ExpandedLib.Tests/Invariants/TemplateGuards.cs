using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ExpandedLib.Testing;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ExpandedLib.Tests.Invariants;

/// <summary>Every template under templates/content parses, names itself by convention and is
/// cut clean of the sample it came from.</summary>
public class TemplateGuards {
  private static readonly string Root = Path.Combine(
    RepoPaths.Root,
    "templates",
    "content"
  );
  private static readonly string[] Kinds =
  [
    "block",
    "item",
    "recipe",
    "megablock",
    "multiblock",
    "node",
    "blockbehavior",
    "entitybehavior",
    "config",
    "migration",
    "command",
    "tests",
  ];

  private static readonly string[] SampleLiterals =
  [
    "TwinTubBlower",
    "twintubblower",
    "BurdenMaker",
    "burdenmaker",
  ];

  /// <summary>How the template folders under <paramref name="root"/> fall short of one
  /// <c>exlib-&lt;kind&gt;</c> folder per kind, each with a
  /// <c>.template.config/template.json</c> whose <c>shortName</c> is the folder's name and whose
  /// <c>identity</c> starts with <c>ExpandedLib.Templates.</c>.</summary>
  /// <param name="root">The folder holding one folder per template, each named
  /// <c>exlib-&lt;kind&gt;</c>.</param>
  /// <returns>One message per shortfall; empty when every kind is in order.</returns>
  /// <exception cref="Newtonsoft.Json.JsonReaderException">A manifest is not JSON.</exception>
  public static IReadOnlyList<string> ManifestFindings(
    string root,
    IEnumerable<string> kinds
  ) {
    var findings = new List<string>();
    string[] expected = [.. kinds.Order()];
    string[] found =
    [
      .. Directory
        .GetDirectories(root)
        .Select(d => Path.GetFileName(d)!["exlib-".Length..])
        .Order(),
    ];
    if (!expected.SequenceEqual(found))
      findings.Add(
        $"template kinds are {string.Join(", ", found)}; expected {string.Join(", ", expected)}"
      );
    foreach (string kind in expected) {
      string path = Path.Combine(
        root,
        $"exlib-{kind}",
        ".template.config",
        "template.json"
      );
      if (!File.Exists(path)) {
        findings.Add($"{path} is missing");
        continue;
      }
      JObject manifest = JObject.Parse(File.ReadAllText(path));
      if ((string?)manifest["shortName"] != $"exlib-{kind}")
        findings.Add($"{path}: shortName is not exlib-{kind}");
      if (
        (string?)manifest["identity"] is not string identity
        || !identity.StartsWith(
          "ExpandedLib.Templates.",
          StringComparison.Ordinal
        )
      )
        findings.Add(
          $"{path}: identity does not start with ExpandedLib.Templates."
        );
    }
    return findings;
  }

  /// <summary>Each file of <paramref name="files"/> naming a sample a template was cut from
  /// (<c>TwinTubBlower</c>, <c>BurdenMaker</c>, either in lower case).</summary>
  /// <param name="files">Template files as their path and text.</param>
  /// <returns><c>path: literal</c> per literal found, in input order.</returns>
  public static IReadOnlyList<string> SampleNames(
    IEnumerable<(string Path, string Text)> files
  ) =>
    [
      .. files.SelectMany(f =>
        SampleLiterals
          .Where(literal => f.Text.Contains(literal))
          .Select(literal => $"{f.Path}: {literal}")
      ),
    ];

  [Fact]
  public void Every_kind_has_a_manifest_named_by_convention() {
    IReadOnlyList<string> findings = ManifestFindings(Root, Kinds);

    Assert.True(findings.Count == 0, string.Join("\n", findings));
  }

  [Fact]
  public void No_template_file_names_the_sample_it_was_cut_from() {
    string[] files = Directory
      .GetFiles(Root, "*", SearchOption.AllDirectories)
      .Where(f =>
        !f.Contains(
          $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"
        )
        && !f.Contains(
          $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"
        )
      )
      .ToArray();
    Premise.NotEmpty(files, "template files");
    IReadOnlyList<string> named = SampleNames(
      files.Select(f => (f, File.ReadAllText(f)))
    );

    Assert.True(named.Count == 0, string.Join("\n", named));
  }

  private static string Manifest(string shortName, string identity) =>
    $"{{ \"shortName\": \"{shortName}\", \"identity\": \"{identity}\" }}";

  // Fails when ManifestFindings passes a missing kind, a stray folder, a missing manifest, or a
  // wrong shortName or identity, or names a manifest in order.
  [Fact]
  [PlantedDefect(typeof(TemplateGuards), nameof(ManifestFindings))]
  public void A_template_out_of_convention_is_named() {
    using var planted = new PlantedFiles();
    const string config = ".template.config/template.json";
    planted.Write(
      $"exlib-block/{config}",
      Manifest("exlib-block", "ExpandedLib.Templates.Block")
    );
    planted.Write(
      $"exlib-item/{config}",
      Manifest("exlib-block", "Other.Item")
    );
    planted.Write("exlib-stray/readme.md", "");

    IReadOnlyList<string> findings = ManifestFindings(
      planted.Root,
      ["block", "item", "recipe"]
    );

    string item = Path.Combine(
      planted.Root,
      "exlib-item",
      ".template.config",
      "template.json"
    );
    Assert.Equal(
      [
        "template kinds are block, item, stray; expected block, item, recipe",
        $"{item}: shortName is not exlib-item",
        $"{item}: identity does not start with ExpandedLib.Templates.",
        $"{Path.Combine(planted.Root, "exlib-recipe", ".template.config", "template.json")} "
          + "is missing",
      ],
      findings
    );
  }

  // Fails when SampleNames misses a sample name in either case or names a clean file.
  [Fact]
  [PlantedDefect(typeof(TemplateGuards), nameof(SampleNames))]
  public void A_template_naming_its_sample_is_named() {
    Assert.Equal(
      ["a.cs: TwinTubBlower", "b.json: burdenmaker"],
      SampleNames([
        ("a.cs", "class TwinTubBlowerBlock { }"),
        ("b.json", "{ \"code\": \"burdenmaker:hopper\" }"),
        ("c.cs", "class Blower { }"),
      ])
    );
  }
}
