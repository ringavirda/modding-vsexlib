#if GAME_GE_1_22
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using ExpandedLib.Helpers;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace ExpandedLib.Industry.MechanicalPower;

/// <summary>The Drives and Driven by sections of a block's handbook page: a block whose entity or
/// entity behaviours are <see cref="IMpEnergyProducer"/> lists the consumers that are not producers
/// themselves, a consumer that is not a producer lists the producers.</summary>
internal static class MpHandbookRoles
{
  private static readonly ConditionalWeakTable<
    ICoreClientAPI,
    List<(Block Block, bool Producer, bool Consumer)>
  > Roles = new();

#pragma warning disable CA2255
  [ModuleInitializer]
#pragma warning restore CA2255
  internal static void Init() => HandbookSections.MpLinks = Section;

  /// <summary>The section for <paramref name="block"/>; null for a block that is neither a producer
  /// nor a consumer, or whose partners have no handbook page.</summary>
  internal static HandbookSection? Section(ICoreClientAPI capi, Block block)
  {
    var all = Of(capi);
    var self = all.FirstOrDefault(r => r.Block.BlockId == block.BlockId);
    if (self.Block == null || (!self.Producer && !self.Consumer))
      return null;
    bool drives = self.Producer;

    var lines = new List<HandbookPart[]>();
    var pages = new HashSet<string>();
    foreach (var other in all)
    {
      if (other.Block.BlockId == block.BlockId)
        continue;
      bool listed = drives ? other.Consumer && !other.Producer : other.Producer;
      if (!listed)
        continue;
      foreach (ItemStack stack in other.Block.GetHandBookStacks(capi) ?? [])
      {
        if (pages.Add(HandbookMadeAtPatch.PageCode(capi, stack)))
          lines.Add([new StackPart([stack], stack.GetName())]);
      }
    }
    return lines.Count == 0
      ? null
      : new HandbookSection(
        drives ? ExlibLang.HandbookDrives : ExlibLang.HandbookDrivenby,
        [.. lines]
      );
  }

  private static List<(Block Block, bool Producer, bool Consumer)> Of(
    ICoreClientAPI capi
  ) =>
    Roles.GetValue(
      capi,
      c =>
      {
        var found = new List<(Block, bool, bool)>();
        foreach (Block b in c.World.Blocks.OrderBy(b => b?.BlockId ?? 0))
        {
          if (b?.Code == null)
            continue;
          Type[] types = TypesOf(c, b);
          found.Add(
            (
              b,
              types.Any(typeof(IMpEnergyProducer).IsAssignableFrom),
              types.Any(typeof(IMpEnergyConsumer).IsAssignableFrom)
            )
          );
        }
        return found;
      }
    );

  private static Type[] TypesOf(ICoreClientAPI capi, Block block)
  {
    IClassRegistryAPI registry = capi.World.ClassRegistry;
    var types = new List<Type?>();
    if (!string.IsNullOrEmpty(block.EntityClass))
      types.Add(registry.GetBlockEntity(block.EntityClass));
    foreach (
      BlockEntityBehaviorType behavior in block.BlockEntityBehaviors ?? []
    )
      types.Add(registry.GetBlockEntityBehaviorClass(behavior.Name));
    return [.. types.OfType<Type>()];
  }
}
#endif
