#if GAME_GE_1_22
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace ExpandedLib.Helpers;

/// <summary>The handbook page set and the page a stack opens, including the page of the nearest
/// variant for a stack whose own variant has none.</summary>
internal static class HandbookPages
{
  private const string StacksKey = "exlib-handbookstacks";
  private const string CodesKey = "exlib-handbookpages";

  /// <summary>The page code of every handbook stack the loaded collectibles list, read once per
  /// client.</summary>
  internal static HashSet<string> Codes(ICoreClientAPI capi) =>
    ObjectCacheUtil.GetOrCreate(
      capi,
      CodesKey,
      () =>
        Stacks(capi)
          .Select(GuiHandbookItemStackPage.PageCodeForStack)
          .ToHashSet()
    );

  /// <summary>Whether <paramref name="pageCode"/> is the page of a handbook stack.</summary>
  internal static bool Has(ICoreClientAPI capi, string pageCode) =>
    Codes(capi).Contains(pageCode);

  /// <summary>The page <paramref name="stack"/> opens: its collectible's own provider when it has
  /// one, else the stack's own page code.</summary>
  internal static string PageCode(ICoreClientAPI capi, ItemStack stack) =>
    stack
      .Collectible.GetCollectibleInterface<IHandBookPageCodeProvider>()
      ?.HandbookPageCodeForStack(capi.World, stack)
    ?? GuiHandbookItemStackPage.PageCodeForStack(stack);

  /// <summary>The page of <paramref name="stack"/> when it has one; else, for a block, the page of
  /// the handbook stack that differs from it in the fewest variant states, the lower block id
  /// winning a tie. The candidates are the blocks its <c>handbook.groupBy</c> selects, or without
  /// groupBy the blocks of its class whose code is the same once every variant value is
  /// <c>*</c>. The stack's own page code when no candidate exists.</summary>
  internal static string Nearest(ICoreClientAPI capi, ItemStack stack)
  {
    string own = GuiHandbookItemStackPage.PageCodeForStack(stack);
    if (Has(capi, own) || stack.Collectible is not Block block)
      return own;

    Block? nearest = Stacks(capi)
      .Select(s => s.Collectible)
      .OfType<Block>()
      .Where(b => b.BlockId != block.BlockId && Groups(block, b))
      .OrderBy(b => Distance(block, b))
      .ThenBy(b => b.BlockId)
      .FirstOrDefault();
    return nearest == null
      ? own
      : GuiHandbookItemStackPage.PageCodeForStack(new ItemStack(nearest));
  }

  private static ItemStack[] Stacks(ICoreClientAPI capi) =>
    ObjectCacheUtil.GetOrCreate(
      capi,
      StacksKey,
      () =>
        capi
          .World.Collectibles.SelectMany(c => c.GetHandBookStacks(capi) ?? [])
          .ToArray()
    );

  private static bool Groups(Block block, Block other)
  {
    string[]? groupBy = block.Attributes?["handbook"]?[
      "groupBy"
    ]?.AsArray<string>(null);
    if (groupBy != null)
      return groupBy.Any(g =>
        WildcardUtil.Match(
          g.Contains(':')
            ? new AssetLocation(g)
            : new AssetLocation(block.Code.Domain, g),
          other.Code
        )
      );
    return block.GetType() == other.GetType() && Shape(block) == Shape(other);
  }

  private static string Shape(Block block)
  {
    string suffix = string.Concat(block.Variant.Values.Select(v => "-" + v));
    string path = block.Code.Path;
    string stem = path.EndsWith(suffix) ? path[..^suffix.Length] : path;
    return $"{block.Code.Domain}:{stem}:{block.Variant.Count}";
  }

  private static int Distance(Block block, Block other) =>
    block.Variant.Count(v => other.Variant[v.Key] != v.Value)
    + other.Variant.Count(v => !block.Variant.ContainsKey(v.Key));
}
#endif
