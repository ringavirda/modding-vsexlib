using ExpandedLib.Registries;
using Vintagestory.API.Common;

namespace ExpandedLib.Assembling;

/// <summary>The block entity of a hand-assembled block that is not a network node; it exists to carry <see cref="BEBehaviorAssembled"/>.</summary>
[BlockEntityRegister("Assembled", PrefixModId = false)]
public class BlockEntityAssembled : BlockEntity { }
