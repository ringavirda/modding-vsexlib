using ExpandedLib.Registries;
using Vintagestory.API.Common;

namespace ExpandedLib.Assembling;

/// <summary>Holds what hand assembly records on a placed part: whether it is fixed and the fasteners each joint spent.</summary>
[BlockEntityBehaviorRegister]
public class BEBehaviorAssembled(BlockEntity blockentity)
  : BlockEntityBehavior(blockentity) { }
