using ExpandedLib.Registries;

namespace ExpandedLib.Industry.Pipes;

/// <summary>
/// Block entity for the pipe passthrough: a plain pipe node, used as a gas consumer by adjacent
/// machines. Every tier's passthrough binds to the one key <c>exlib.BlockEntityPipePassthrough</c>.
/// </summary>
[BlockEntityRegister]
public class BlockEntityPipePassthrough : BlockEntityPipe { }
