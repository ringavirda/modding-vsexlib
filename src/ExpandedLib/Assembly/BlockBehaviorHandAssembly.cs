using ExpandedLib.Registries;
using Vintagestory.API.Common;

namespace ExpandedLib.Assembling;

/// <summary>Marks a block as placed only by hand assembly; the def's <c>handAssembly</c> attribute carries the tool and fasteners. Declared first among a def's behaviours by <see cref="Definitions.ExBlockDef.HandAssembly"/>.</summary>
[BlockBehaviorRegister("HandAssembly", PrefixModId = false)]
public class BlockBehaviorHandAssembly : BlockBehavior {
  /// <summary>Creates the behaviour for <paramref name="block"/>.</summary>
  /// <param name="block">The block the behaviour is declared on.</param>
  public BlockBehaviorHandAssembly(Block block)
    : base(block) { }

  /// <summary>The tool and fasteners the block declares, or null when its attribute is missing.</summary>
  public HandAssemblySpec? Spec => HandAssemblySpec.Of(block);

  /// <summary>Refuses a survival placement outside the assembly handler and, in creative, places the aim's first candidate fixed.</summary>
  public override bool TryPlaceBlock(
    IWorldAccessor world,
    IPlayer byPlayer,
    ItemStack itemstack,
    BlockSelection blockSel,
    ref EnumHandling handling,
    ref string failureCode
  ) {
    if (HandAssemblyGate.Refuses(world, byPlayer, itemstack, ref failureCode)) {
      handling = EnumHandling.PreventSubsequent;
      return false;
    }
    if (
      !HandAssemblyGate.PlacesFirst(
        world,
        byPlayer,
        itemstack,
        blockSel,
        ref failureCode,
        out bool placed
      )
    )
      return true;
    handling = EnumHandling.PreventSubsequent;
    return placed;
  }
}
