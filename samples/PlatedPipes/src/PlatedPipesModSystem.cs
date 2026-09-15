using ExpandedLib.Industry.Pipes;
using ExpandedLib.Registries;
using Vintagestory.API.Common;

namespace PlatedPipes;

/// <summary>
/// The whole registration walk: <see cref="ExModSystem"/> loads this assembly's config, registers
/// every attribute-marked class and code-first definition. The one thing left to write here is the
/// plated tier's own rating, registered into <see cref="BlockPipe"/>'s per-tier tables - exlib's own
/// <see cref="BlockPipe"/> carries no tier of its own.
/// </summary>
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
