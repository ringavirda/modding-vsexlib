using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using static ExpandedLib.Checks.StageWildcardsCheck;

namespace ExpandedLib.Checks;

/// <summary>
/// <see cref="StageWildcardsCheck"/>'s rules (b) and (c) for a construction stage wildcard in a
/// domain the source does not cover, such as <c>game:plank-*</c>, decided against the variant
/// groups of the blocks and items the game loaded.
/// </summary>
/// <remarks>(b): the <c>*</c> spans only the key's variant group; (c): the key is a variant group
/// of every loaded match, and the pattern matches something. An item pattern is decided as a block
/// one is, since a loaded item carries its variant groups.</remarks>
public static class LoadedStageWildcardsCheck {
  /// <summary>Every stored wildcard of <paramref name="domain"/>'s stage tables, outside the
  /// covered domains, that breaks rule (b) or (c).</summary>
  /// <param name="game">The domain's definitions and the loaded collectibles.</param>
  /// <param name="domain">The domain whose stage tables are checked.</param>
  /// <returns>The check's <see cref="CheckResult"/>, named <c>LoadedStageWildcards</c>, one error
  /// per violation prefixed with its rule; no errors when none.</returns>
  /// <exception cref="ArgumentException">A stage table reads a JSON object or array where a
  /// string is read, or an array where an object is read.</exception>
  /// <exception cref="InvalidOperationException">A stage table reads a JSON value where an object
  /// is read.</exception>
  public static CheckResult Run(ILoadedGame game, string domain) {
    HashSet<string> covered = [.. game.Domains];
    Collectible[]? loaded = null;
    var errors = new List<string>();
    foreach (Construction c in Constructions(game, domain))
      for (int i = 0; i < c.Stages.Count; i++)
        foreach (JToken ing in Ingredients(c.Stages, i)) {
          string code = (string?)ing["code"] ?? "";
          if (
            !code.Contains('*')
            || (string?)ing["storeWildCard"] is not { } key
            || covered.Contains(new AssetLocation(code).Domain)
          )
            continue;
          loaded ??= [.. game.Collectibles.Select(Of)];
          var pattern = new AssetLocation(code);
          bool block =
            ((string?)ing["type"])?.ToLowerInvariant() is null or "block";
          int star = pattern.Path.IndexOf('*');
          IEnumerable<Collectible> among = loaded.Where(k =>
            k.Block == block
            && k.Code.StartsWith(
              pattern.Domain + ":" + pattern.Path[..star],
              StringComparison.Ordinal
            )
          );
          foreach (
            string line in Decide(pattern, ing, key, among.Select(k => k.Of))
          )
            errors.Add($"{c.Code} stage {i} {code}: {line}");
        }
    return new CheckResult("LoadedStageWildcards", domain, errors);
  }

  private sealed record Collectible(
    bool Block,
    string Code,
    StageWildcardsCheck.Collectible Of
  );

  private static Collectible Of(CollectibleObject c) =>
    new(
      c.ItemClass == EnumItemClass.Block,
      c.Code.ToString(),
      new StageWildcardsCheck.Collectible(
        c.Code.ToString(),
        ObtainabilityCheck.TypeOf(c),
        c.Variant == null ? [] : [.. c.Variant.Select(v => (v.Key, v.Value))],
        []
      )
    );
}
