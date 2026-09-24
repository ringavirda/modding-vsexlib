using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ExpandedLib.Checks;

namespace ExpandedLib.Testing;

/// <summary>Checks that every grid recipe's block output names a block the mod registers, scoped to
/// block outputs in the mod's own domain.</summary>
public static class RecipeCodes {
  /// <summary>One output that names no registered block: the recipe file it sits in and the code.</summary>
  public sealed record Unresolvable(string RecipePath, string Code);

  /// <summary>Every concrete block code <paramref name="domain"/>'s grid recipes can output, with
  /// placeholders expanded.</summary>
  [CheckHelper("lists the block codes grid recipes output")]
  public static IEnumerable<string> OutputBlockCodes(
    string domain,
    Assembly asm
  ) =>
    RecipeCodesCheck
      .Outputs(new AssemblyCheckSource((domain, asm)), domain)
      .Select(o => o.Code);

  /// <summary>Every block output in <paramref name="domain"/>'s recipes that no definition in the same
  /// assembly produces.</summary>
  public static IReadOnlyList<Unresolvable> UnresolvableOutputs(
    string domain,
    Assembly asm
  ) =>
    [
      .. RecipeCodesCheck
        .Unresolvable(
          new AssemblyCheckSource((domain, asm))
          {
            PropertyGroupsAsWildcard = true,
          },
          domain
        )
        .Select(o => new Unresolvable(o.File.ToShortString(), o.Code)),
    ];
}
