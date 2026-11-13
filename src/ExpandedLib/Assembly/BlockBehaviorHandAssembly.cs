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
}
