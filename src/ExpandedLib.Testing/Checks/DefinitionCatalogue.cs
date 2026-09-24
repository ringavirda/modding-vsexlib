using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ExpandedLib.Checks;
using ExpandedLib.Definitions;
using Vintagestory.API.Common;
using Vintagestory.API.Util;

namespace ExpandedLib.Testing;

/// <summary>
/// Answers whether a code names something a mod registers, over blocks and items together. A
/// code that names nothing neither throws nor logs. Membership is tested with
/// <see cref="WildcardUtil"/>, never equality.
/// </summary>
public static class DefinitionCatalogue {
  /// <summary>Every block code pattern <paramref name="domain"/> registers, worldproperty groups as
  /// <c>*</c>.</summary>
  [CheckHelper(
    "lists the block code patterns a domain registers, which Resolves matches"
  )]
  public static IEnumerable<string> BlockPatterns(
    string domain,
    Assembly asm
  ) => DefinitionCodes.PatternsForDomain(domain, asm);

  /// <summary>Every item code pattern <paramref name="domain"/> registers.</summary>
  [CheckHelper(
    "lists the item code patterns a domain registers, which Resolves matches"
  )]
  public static IEnumerable<string> ItemPatterns(string domain, Assembly asm) =>
    DefinitionGoldens
      .Collect(domain, asm)
      .OfType<ExItemDef>()
      .SelectMany(d =>
        DefinitionCatalogueCheck.Patterns(d.Domain, d.Code, d.ToJson())
      )
      .Distinct();

  /// <summary>Whether <paramref name="stack"/>'s code names something <paramref name="domains"/>
  /// register; a domain not among <paramref name="domains"/> is reported as resolvable.</summary>
  public static bool Resolves(
    JsonItemStack? stack,
    IReadOnlyDictionary<string, Assembly> domains
  ) {
    if (stack?.Code is not { } code)
      return false;

    if (!domains.TryGetValue(code.Domain, out Assembly? asm))
      return true;

    IEnumerable<string> patterns =
      stack.Type == EnumItemClass.Block
        ? BlockPatterns(code.Domain, asm)
        : ItemPatterns(code.Domain, asm);

    return patterns.Any(p => WildcardUtil.Match(new AssetLocation(p), code));
  }

  /// <summary>Single-domain convenience for the common case.</summary>
  public static bool Resolves(
    JsonItemStack? stack,
    string domain,
    Assembly asm
  ) => Resolves(stack, new Dictionary<string, Assembly> { [domain] = asm });
}
