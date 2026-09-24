using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Blocks;
using ExpandedLib.Networks;
using Vintagestory.API.Common;
using Vintagestory.API.Util;
using static ExpandedLib.Checks.GameReferencesCheck;

namespace ExpandedLib.Checks;

/// <summary>Checks that a survival player can make what a domain asks of him, one step deep: every
/// recipe ingredient, construction stage ingredient and block a creative tab lists.</summary>
/// <remarks>Made means the output of a loaded recipe or of exlib's process catalogues, a smelted,
/// crushed or ground stack, a beehive kiln's firing, the drop of a block of another type, a world
/// source vanilla places, or a code a mod declares its machines make
/// (<see cref="ExlibChecks.Produces"/>). A wildcard is made when one code it matches is, among the
/// states its stack's <c>allowedVariants</c> allow and its <c>skipVariants</c> leave. A block a
/// creative tab lists is made when a block differing from it only in groups its placement writes
/// is: the orientation groups <see cref="GridOutputVariantCheck"/> reads, and a network node's
/// <c>orientation</c>, the faces it connects on. One crafted orientation covers the rest; another
/// machine or pipe shape of the same blocktype does not. A creative tab is no source: it lists what
/// a creative player can take. A block's drop of its own type is no source either, since breaking
/// it needs it first. Nothing is followed further back than the one step.</remarks>
public static class ObtainabilityCheck {
  /// <summary>Every ingredient and creative-listed block of <paramref name="domain"/> that nothing
  /// makes.</summary>
  /// <param name="game">The domain's recipes and definitions, and what the loaded game
  /// makes.</param>
  /// <param name="domain">The domain whose ingredients and blocks are checked.</param>
  /// <returns>The check's <see cref="CheckResult"/>, named <c>Obtainability</c>, one error per
  /// distinct code nothing makes, naming where it is asked for, and one per declaration of
  /// <paramref name="domain"/> that matches no loaded block or item; no errors when none.</returns>
  public static CheckResult Run(ILoadedGame game, string domain) {
    CollectibleObject[] loaded = [.. game.Collectibles];
    var made = new Made(loaded, game.RecipeOutputs);
    var errors = new List<string>();
    foreach (ExlibChecks.Declaration declared in ExlibChecks.Produced())
      if (!made.Declare(loaded, declared.Code) && declared.Domain == domain)
        errors.Add(
          $"{declared.Code} (made by {declared.Source}): matches no loaded block or item"
        );
    foreach (
      Reference r in InRecipes(game, domain)
        .Where(r => r.Origin == Origin.RecipeIngredient)
        .Concat(
          InDefinitions(game, domain)
            .Where(r => r.Origin == Origin.ConstructionRequire)
        )
        .Select(r => r with { Code = r.Qualified })
        .Distinct()
    )
      if (!made.Makes(r.IsBlock, r.Code, r.Allowed, r.Skipped))
        errors.Add($"{r}: nothing makes it");
    Block[] own =
    [
      .. loaded.OfType<Block>().Where(b => b.Code.Domain == domain),
    ];
    ILookup<string, Block> byKind = own.ToLookup(KindOf);
    var kindMade = new Dictionary<string, bool>(StringComparer.Ordinal);
    foreach (
      Block block in own.Where(b => b.CreativeInventoryTabs is { Length: > 0 })
    ) {
      string kind = KindOf(block);
      if (!kindMade.TryGetValue(kind, out bool any))
        kindMade[kind] = any = byKind[kind]
          .Any(b => made.Makes(true, b.Code.ToString(), null, null));
      if (!any)
        errors.Add($"{block.Code} (block, creative tab): nothing makes it");
    }
    return new CheckResult("Obtainability", domain, errors);
  }

  /// <summary>The codes vanilla's world gives a survival player with no recipe: item class, code
  /// pattern, and where it comes from.</summary>
  internal static IEnumerable<(
    EnumItemClass Type,
    string Pattern,
    string Source
  )> WorldSources() =>
    [(EnumItemClass.Block, "game:gravel-*", "worldgen, dug where it lies")];

  // A block's type and its states in every group its placement does not write: the orientation
  // groups, and the connector faces a network node's placement and its neighbours rewrite.
  private static string KindOf(Block block) {
    var placed = new HashSet<string>(
      GridOutputVariantCheck.OrientationGroups(block),
      StringComparer.Ordinal
    );
    if (block is BlockNetworkNode)
      placed.Add(BlockBehaviorExOrientable.OrientationVariant);
    return TypeOf(block)
      + string.Concat(
        block
          .Variant?.Where(v => !placed.Contains(v.Key))
          .Select(v => $"|{v.Key}={v.Value}")
          ?? []
      );
  }

  /// <summary>The type code a loaded collectible's variant states were appended to, one
  /// <c>-state</c> per variant group in group order.</summary>
  internal static string TypeOf(CollectibleObject c) {
    string type = c.Code.ToString();
    if (c.Variant == null)
      return type;
    foreach (string state in c.Variant.Select(v => v.Value).Reverse())
      if (type.EndsWith("-" + state, StringComparison.Ordinal))
        type = type[..^(state.Length + 1)];
    return type;
  }

  // Every code something makes, per item class; a made pattern is kept apart and matched.
  private sealed class Made {
    private readonly Dictionary<bool, HashSet<string>> _codes = new() {
      [true] = new(StringComparer.Ordinal),
      [false] = new(StringComparer.Ordinal),
    };
    private readonly Dictionary<bool, List<AssetLocation>> _patterns = new() {
      [true] = [],
      [false] = [],
    };

    internal Made(
      IEnumerable<CollectibleObject> loaded,
      IEnumerable<LoadedOutput> outputs
    ) {
      foreach (LoadedOutput output in outputs)
        Add(output.Type, output.Code);
      foreach (CollectibleObject c in loaded) {
        Add(c.CombustibleProps?.SmeltedStack);
        Add(c.CrushingProps?.CrushedStack);
        Add(c.GrindingProps?.GroundStack);
        if (c.Attributes?["beehivekiln"] is { Exists: true } kiln)
          foreach (
            JsonItemStack? fired in kiln.AsObject<
              Dictionary<string, JsonItemStack?>
            >(null)?.Values
              ?? Enumerable.Empty<JsonItemStack?>()
          )
            Add(fired);
        if (c is not Block block || block.Drops == null)
          continue;
        string type = TypeOf(block);
        foreach (BlockDropItemStack drop in block.Drops)
          if (
            drop?.Code != null
            && drop.Code.ToString() != type
            && !drop
              .Code.ToString()
              .StartsWith(type + "-", StringComparison.Ordinal)
          )
            Add(drop.Type, drop.Code);
      }
      foreach ((EnumItemClass type, string pattern, _) in WorldSources())
        Add(type, new AssetLocation(pattern));
    }

    // Adds every loaded collectible code matches, of either class; false when it matches none.
    internal bool Declare(IEnumerable<CollectibleObject> loaded, string code) {
      var pattern = new AssetLocation(code);
      int wild = pattern.Path.IndexOfAny(Wild);
      string prefix =
        pattern.Domain + ":" + (wild < 0 ? pattern.Path : pattern.Path[..wild]);
      bool any = false;
      foreach (
        CollectibleObject c in loaded.Where(c =>
          c.Code.ToString().StartsWith(prefix, StringComparison.Ordinal)
        )
      )
        if (
          wild < 0
            ? c.Code.ToString() == pattern.ToString()
            : WildcardUtil.Match(pattern, c.Code)
        ) {
          Add(c.ItemClass, c.Code);
          any = true;
        }
      return any;
    }

    private void Add(JsonItemStack? stack) {
      if (stack?.Code != null)
        Add(stack.Type, stack.Code);
    }

    private void Add(EnumItemClass type, AssetLocation code) {
      bool block = type == EnumItemClass.Block;
      if (code.Path.IndexOfAny(Wild) >= 0)
        _patterns[block].Add(code);
      else
        _codes[block].Add(code.ToString());
    }

    // A single * narrowed by allowed states matches exactly those states' codes; the allowed
    // states of a code with several are not read.
    internal bool Makes(
      bool isBlock,
      string code,
      string[]? allowed,
      string[]? skipped
    ) {
      if (allowed != null && code.Count(c => c == '*') == 1)
        return allowed
          .Where(s => skipped?.Contains(s) != true)
          .Any(s => Makes(isBlock, code.Replace("*", s), null, null));
      if (_codes[isBlock].Contains(code))
        return true;
      var target = new AssetLocation(Unbound(code));
      if (_patterns[isBlock].Any(p => WildcardUtil.Match(p, target)))
        return true;
      int wild = target.Path.IndexOfAny(Wild);
      if (wild < 0)
        return false;
      string prefix = target.Domain + ":" + target.Path[..wild];
      return _codes[isBlock]
        .Where(c => c.StartsWith(prefix, StringComparison.Ordinal))
        .Select(c => new AssetLocation(c))
        .Any(c =>
          WildcardUtil.Match(target, c)
          && (skipped == null || !WildcardUtil.Match(target, c, skipped))
        );
    }

    private static readonly char[] Wild = ['*', '@', '{'];
  }
}
