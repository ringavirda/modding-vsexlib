using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ExpandedLib.Checks;
using ExpandedLib.Definitions;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Util;

namespace ExpandedLib.Testing;

/// <summary>Collects every code a mod's recipes, construction stages and definition bodies point at,
/// as opposed to the codes they register, and reports the ones that name nothing.</summary>
public static class ReferencedCodes {
  /// <summary>Where a reference was authored, which is what a failure message has to name for the
  /// reference to be findable.</summary>
  public enum Origin {
    /// <summary>A recipe's <c>output</c>.</summary>
    RecipeOutput,

    /// <summary>A recipe ingredient, from any of the three shapes (grid slot map, smithing singular,
    /// barrel array).</summary>
    RecipeIngredient,

    /// <summary>One <c>requireStacks</c> entry of an <c>ExRightClickConstructable</c> stage.</summary>
    ConstructionRequire,

    /// <summary>A stack a blocktype or itemtype names in its own body: a drop, a smelted, ground or
    /// shattered stack, a mold's output.</summary>
    DefinitionStack,
  }

  /// <summary>One authored reference, with its placeholders already filled.</summary>
  /// <param name="Source">The def that authored it - a recipe's asset path, or a block code.</param>
  /// <param name="Code">The concrete (or wildcard) code, after placeholder expansion.</param>
  /// <param name="IsBlock">Which registry it lands in; <c>type</c> decides, defaulting to item as the
  /// game's own deserializer does.</param>
  public sealed record Reference(
    string Source,
    Origin Origin,
    string Code,
    bool IsBlock
  ) {
    /// <summary>The domain segment of <see cref="Code"/>, or <c>game</c> when it carries none.</summary>
    public string Domain =>
      Code.Contains(':', StringComparison.Ordinal)
        ? Code[..Code.IndexOf(':', StringComparison.Ordinal)]
        : GlobalConstants.DefaultDomain;

    /// <inheritdoc/>
    public override string ToString() =>
      $"{Source}: {Code} ({(IsBlock ? "block" : "item")}, {Origin})";
  }

  #region Collecting

  /// <summary>Every code <paramref name="domain"/>'s recipe files reference - outputs and ingredients of
  /// all three recipe shapes.</summary>
  [CheckHelper("collects the codes a domain's recipes reference")]
  public static IEnumerable<Reference> InRecipes(string domain, Assembly asm) =>
    GameReferencesCheck
      .InRecipes(new AssemblyCheckSource((domain, asm)), domain)
      .Select(Of);

  /// <summary>Every code <paramref name="domain"/>'s blocktypes and itemtypes name in their own
  /// bodies: construction requires, drops, smelted, ground and shattered stacks, mold outputs.</summary>
  [CheckHelper("collects the codes a domain's definitions reference")]
  public static IEnumerable<Reference> InDefinitions(
    string domain,
    Assembly asm
  ) =>
    GameReferencesCheck
      .InDefinitions(new AssemblyCheckSource((domain, asm)), domain)
      .Select(Of);

  private static Reference Of(GameReferencesCheck.Reference r) =>
    new(r.Source, (Origin)(int)r.Origin, r.Code, r.IsBlock);

  #endregion

  #region Resolving

  /// <summary>The subset of <paramref name="references"/> that name nothing any of
  /// <paramref name="domains"/> registers.</summary>
  public static IReadOnlyList<Reference> Unresolvable(
    IEnumerable<Reference> references,
    IReadOnlyDictionary<string, Assembly> domains
  ) {
    var catalogue = new Catalogue(domains);
    return [.. references.Where(r => !catalogue.Resolves(r)).Distinct()];
  }

  /// <summary>How many of <paramref name="references"/> this harness can actually judge: the ones
  /// whose domain is in <paramref name="domains"/>.</summary>
  public static int Checkable(
    IEnumerable<Reference> references,
    IReadOnlyDictionary<string, Assembly> domains
  ) => references.Count(r => domains.ContainsKey(r.Domain));

  /// <summary>References written with no domain whose exact path names something a mod registers.
  /// Wildcards are excluded.</summary>
  public static IReadOnlyList<Reference> BareButOurs(
    IEnumerable<Reference> references,
    IReadOnlyDictionary<string, Assembly> domains
  ) {
    var catalogue = new Catalogue(domains);
    return
    [
      .. references
        .Where(r =>
          !r.Code.Contains(':', StringComparison.Ordinal)
          && !r.Code.Contains('*', StringComparison.Ordinal)
          && !r.Code.Contains("@(", StringComparison.Ordinal)
        )
        .Where(r =>
          domains.Keys.Any(d =>
            catalogue.Resolves(r with { Code = $"{d}:{r.Code}" })
          )
        )
        .Distinct(),
    ];
  }

  // Registered code patterns per domain, expanded once and cached.
  private sealed class Catalogue(IReadOnlyDictionary<string, Assembly> domains) {
    private readonly Dictionary<
      (string Domain, bool IsBlock),
      AssetLocation[]
    > _cache = [];

    internal bool Resolves(Reference reference) {
      if (!domains.TryGetValue(reference.Domain, out Assembly? asm))
        return true;

      var target = new AssetLocation(reference.Code);
      // Both directions: either side may carry a wildcard, and WildcardUtil only reads the pattern side.
      return Patterns(reference.Domain, reference.IsBlock, asm)
        .Any(p =>
          WildcardUtil.Match(p, target) || WildcardUtil.Match(target, p)
        );
    }

    private AssetLocation[] Patterns(string domain, bool isBlock, Assembly asm) {
      if (_cache.TryGetValue((domain, isBlock), out AssetLocation[]? cached))
        return cached;

      AssetLocation[] patterns =
      [
        .. (
          isBlock
            ? DefinitionCatalogue.BlockPatterns(domain, asm)
            : DefinitionCatalogue.ItemPatterns(domain, asm)
        ).Select(c => new AssetLocation(c)),
      ];
      _cache[(domain, isBlock)] = patterns;
      return patterns;
    }
  }

  #endregion
}
