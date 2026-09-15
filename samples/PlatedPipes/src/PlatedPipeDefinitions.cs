using System.Collections.Generic;
using ExpandedLib.Definitions;
using ExpandedLib.Industry.Pipes;

namespace PlatedPipes;

/// <summary>The plated pipe tier: straight/bend/T/X segments plus wall passthroughs, under the
/// <c>platedpipes</c> domain. The thin per-mod <see cref="IExBlockDefProvider"/> binding to the
/// registered <c>exlib.BlockPipe</c>/<c>BlockEntityPipe</c> classes.</summary>
public class PlatedPipeDefinitions : IExBlockDefProvider {
  public static IEnumerable<ExBlockDef> Definitions(string domain) =>
    [
      .. BlockPipe.Segments(domain, BlockPipe.PlatedTier),
      .. BlockPipePassthrough.Passthroughs(domain, BlockPipe.PlatedTier),
    ];
}
