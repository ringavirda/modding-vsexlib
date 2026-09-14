using ExpandedLib.Registries;

namespace HandMill;

/// <summary>
/// The whole registration walk: <see cref="ExModSystem"/> loads this assembly's config, registers
/// every attribute-marked class and code-first definition, and wires the command on each side - all
/// with nothing to write here.
/// </summary>
public class HandMillModSystem : ExModSystem { }
