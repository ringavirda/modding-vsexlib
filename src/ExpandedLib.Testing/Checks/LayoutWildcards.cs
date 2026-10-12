using System;
using System.Collections.Generic;
using ExpandedLib.Definitions;
using Newtonsoft.Json.Linq;

namespace ExpandedLib.Testing;

/// <summary>
/// Guards the wildcard form inside a layout's alternations: the game reads the body of <c>@(...)</c>
/// as a regex, so <c>@(air|hearthmetal-*)</c> refuses <c>hearthmetal-pigiron</c>; the form is <c>.*</c>.
/// </summary>
public static class LayoutWildcards {
  /// <summary>What one run read and found.</summary>
  /// <param name="Layouts">The definitions carrying a <c>multiblockStructure</c> layout.</param>
  /// <param name="Alternations">The <c>@(...)</c> groups across those layouts.</param>
  /// <param name="Findings">One line per bare <c>*</c>, as
  /// <c>{golden path}: {code} has * not preceded by . inside @(...)</c>.</param>
  public sealed record Result(
    int Layouts,
    int Alternations,
    IReadOnlyList<string> Findings
  );

  /// <summary>Runs the rule over the block codes of every layout among <paramref name="defs"/>; a
  /// definition without a layout is skipped.</summary>
  /// <param name="defs">The definitions to read.</param>
  /// <returns>The layouts read, the alternations among them, and one finding per layout code with
  /// a bare <c>*</c> inside an alternation; no findings when clean. A group with no closing
  /// parenthesis reads to the end of the code.</returns>
  /// <exception cref="InvalidCastException">A layout's <c>blockNumbers</c> is not an
  /// object.</exception>
  public static Result Check(IEnumerable<ExBlockDef> defs) {
    int layouts = 0,
      alternations = 0;
    var findings = new List<string>();
    foreach (ExBlockDef def in defs) {
      JToken? structure = def.ToJson()["attributes"]?["multiblockStructure"];
      if (structure is not JObject)
        continue;
      layouts++;
      foreach (
        JProperty code in ((JObject)structure["blockNumbers"]!).Properties()
      ) {
        bool bare = false;
        for (
          int at = code.Name.IndexOf("@(", StringComparison.Ordinal);
          at >= 0;
          at = code.Name.IndexOf("@(", at + 2, StringComparison.Ordinal)
        ) {
          alternations++;
          int depth = 0,
            i = at + 2;
          for (; i < code.Name.Length; i++) {
            char c = code.Name[i];
            if (c == '(')
              depth++;
            else if (c == ')' && depth-- == 0)
              break;
            else if (c == '*' && code.Name[i - 1] != '.')
              bare = true;
          }
        }
        if (bare)
          findings.Add(
            $"{DefinitionGoldens.RelativePath(def)}: {code.Name} has * not preceded by . inside @(...)"
          );
      }
    }
    return new Result(layouts, alternations, findings);
  }
}
