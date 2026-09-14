using ExpandedLib.Registries;

namespace TwinTubBlower;

/// <summary>
/// The whole registration walk: <see cref="ExModSystem"/> loads this assembly's config, registers
/// every attribute-marked class and code-first definition - with nothing to write here.
/// </summary>
public class TwinTubBlowerModSystem : ExModSystem { }
