using System.Collections.Generic;
using ExpandedLib.Definitions;
using ExpandedLib.Industry.Pipes;

namespace PlatedPipes;

/// <summary>
/// The plated pipe tier: straight/bend/T/X segments plus wall passthroughs, authored by the shared
/// <see cref="BlockPipe.Segments"/> and <see cref="BlockPipePassthrough.Passthroughs"/> factories under
/// the <c>platedpipes</c> domain - the pipe base classes live in exlib; this is the thin per-mod
/// <see cref="IExBlockDefProvider"/> every tier needs. Discovered when this assembly is scanned, it
/// injects <c>platedpipes:pipe-plated-*</c> segments and passthroughs bound to the registered
/// <c>exlib.BlockPipe</c> and <c>exlib.BlockEntityPipe</c> classes.
/// </summary>
public class PlatedPipeDefinitions : IExBlockDefProvider {
  public static IEnumerable<ExBlockDef> Definitions(string domain) =>
    [
      .. BlockPipe.Segments(domain, BlockPipe.PlatedTier),
      .. BlockPipePassthrough.Passthroughs(domain, BlockPipe.PlatedTier),
    ];
}
