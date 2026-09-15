using ExpandedLib.Industry.Pipes;
using ExpandedLib.Registries;
using Vintagestory.API.Common;

namespace PlatedPipes;

/// <summary>Registers this assembly's config and code-first definitions, and the plated tier's
/// burst, throughput and joint ratings into <see cref="BlockPipe"/>.</summary>
public class PlatedPipesModSystem : ExModSystem {
  protected override void OnStart(ICoreAPI api) {
    BlockPipe.RegisterBurst(
      BlockPipe.PlatedTier,
      () => PlatedPipesValues.PlatedPipeBurstPressure
    );
    BlockPipe.RegisterThroughput(
      BlockPipe.PlatedTier,
      () => PlatedPipesValues.PlatedPipeThroughput
    );
    BlockPipe.RegisterJoint(BlockPipe.PlatedTier, BlockPipe.FlangedJoint);
  }
}
