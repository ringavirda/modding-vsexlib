using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using ExpandedLib.Definitions;
using Newtonsoft.Json.Linq;

namespace ExpandedLib.Testing;

/// <summary>
/// Finds domain-qualified block-code string literals in a mod's source that name a
/// variant-grouped block by its bare base code, which can never resolve to a placed block.
/// </summary>
public static class CodeLiterals {
  private static readonly Regex Qualified = new(
    @"""(?<domain>[a-z]+):(?<path>[a-zA-Z0-9_\-/\.\*]+)""",
    RegexOptions.Compiled
  );

  /// <summary>Matches a line whose literal addresses an asset (shape, texture, animation), not a
  /// block.</summary>
  private static readonly Regex AssetReference = new(
    @"\b(Shape|Texture|Animation|Sound)\w*\s*\(|\b(shapes|textures)/",
    RegexOptions.Compiled
  );

  /// <summary>Base codes in <paramref name="domain"/> that declare at least one variant group.</summary>
  private static HashSet<string> VariantGroupedBaseCodes(
    string domain,
    Assembly asm
  ) =>
    DefinitionGoldens
      .Collect(domain, asm)
      .OfType<ExBlockDef>()
      .Where(d => d.ToJson()["variantgroups"] is JArray g && g.Count > 0)
      .Select(d => d.Code)
      .ToHashSet(StringComparer.Ordinal);

  /// <summary>Every <c>file:line</c> holding a literal that names a variant-grouped block by its
  /// bare code.</summary>
  /// <param name="srcDir">The mod's own source root.</param>
  /// <returns>Empty when none.</returns>
  public static IReadOnlyList<string> UnresolvableBareCodes(
    string domain,
    Assembly asm,
    string srcDir
  ) {
    HashSet<string> bare = VariantGroupedBaseCodes(domain, asm);
    if (bare.Count == 0)
      return [];

    string root = srcDir;
    var findings = new List<string>();

    foreach (
      string file in Directory.EnumerateFiles(
        root,
        "*.cs",
        SearchOption.AllDirectories
      )
    ) {
      string norm = file.Replace('\\', '/');
      // Build output and generated tables are not authored source.
      if (
        norm.Contains("/bin/")
        || norm.Contains("/obj/")
        || norm.Contains("/Generated/")
      )
        continue;

      string[] lines = File.ReadAllLines(file);
      for (int i = 0; i < lines.Length; i++) {
        // An asset path can equal a base code exactly; filtering is by the calling method, not
        // the literal.
        if (AssetReference.IsMatch(lines[i]))
          continue;

        foreach (Match m in Qualified.Matches(lines[i])) {
          if (m.Groups["domain"].Value != domain)
            continue;

          string path = m.Groups["path"].Value;
          if (!bare.Contains(path))
            continue;

          findings.Add(
            $"{norm[(norm.IndexOf("/src/", StringComparison.Ordinal) + 1)..]}:{i + 1}"
              + $"  \"{domain}:{path}\" - {path} declares variant groups, so this bare code resolves "
              + "to null. Name a variant, or use a wildcard if any will do."
          );
        }
      }
    }

    return findings;
  }
}
