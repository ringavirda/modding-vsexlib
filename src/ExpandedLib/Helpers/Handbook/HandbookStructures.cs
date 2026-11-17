#if GAME_GE_1_22
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Util;

namespace ExpandedLib.Helpers;

/// <summary>The Part of section: the structures whose <c>multiblockStructure</c> selectors name a
/// block.</summary>
internal static class HandbookStructures
{
  private static readonly AssetLocation Air = new("game:air");

  private static readonly ConditionalWeakTable<
    ICoreClientAPI,
    List<(Block Owner, MultiblockStructure Structure)>
  > Structures = new();

  /// <summary>One line per structure page that counts <paramref name="block"/> among its cells,
  /// ordered by the structure block's id: the block's name and the number of cells that carry it,
  /// or that it stands in when a selector that matches it also matches air. Structure blocks
  /// without a handbook page and the block's own structure are left out; null when none
  /// remain.</summary>
  internal static HandbookSection? PartOf(ICoreClientAPI capi, Block block)
  {
    var lines = new List<HandbookPart[]>();
    var pages = new HashSet<string>();
    foreach ((Block owner, MultiblockStructure structure) in Of(capi))
    {
      if (owner.Code.Equals(block.Code))
        continue;
      (bool matches, bool standsIn, int cells) = Matching(
        structure,
        block.Code
      );
      if (!matches)
        continue;
      foreach (ItemStack stack in owner.GetHandBookStacks(capi) ?? [])
      {
        if (!pages.Add(HandbookMadeAtPatch.PageCode(capi, stack)))
          continue;
        string text = Lang.Get(
          standsIn
            ? ExlibLang.HandbookPartofStandsin
            : ExlibLang.HandbookPartofLine,
          stack.GetName(),
          cells
        );
        lines.Add([new StackPart([stack], text)]);
      }
    }
    return lines.Count == 0
      ? null
      : new HandbookSection(ExlibLang.HandbookPartof, [.. lines]);
  }

  private static (bool Matches, bool StandsIn, int Cells) Matching(
    MultiblockStructure structure,
    AssetLocation code
  )
  {
    bool matches = false;
    bool standsIn = false;
    int cells = 0;
    foreach ((AssetLocation selector, int number) in structure.BlockNumbers)
    {
      if (!WildcardUtil.Match(selector, code))
        continue;
      matches = true;
      standsIn |= WildcardUtil.Match(selector, Air);
      cells += structure.Offsets.Count(o => o.W == number);
    }
    return (matches, standsIn, cells);
  }

  private static List<(Block Owner, MultiblockStructure Structure)> Of(
    ICoreClientAPI capi
  ) =>
    Structures.GetValue(
      capi,
      c =>
        [
          .. c
            .World.Blocks.Where(b =>
              b?.Code != null
              && b.Attributes?["multiblockStructure"].Exists == true
            )
            .OrderBy(b => b.BlockId)
            .Select(b =>
              (
                Owner: b,
                Structure: b.Attributes["multiblockStructure"]
                  .AsObject<MultiblockStructure>()
              )
            )
            .Where(s => s.Structure != null),
        ]
    );
}
#endif
