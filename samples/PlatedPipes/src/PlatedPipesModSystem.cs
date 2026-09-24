using ExpandedLib.Checks;
using ExpandedLib.Industry.Pipes;
using ExpandedLib.Registries;
using Vintagestory.API.Common;

namespace PlatedPipes;

/// <summary>Registers this assembly's config and code-first definitions, and the plated tier's
/// burst, throughput and joint ratings into <see cref="BlockPipe"/>, and exempts the wall
/// passthroughs from <see cref="ObtainabilityCheck"/>: no recipe makes them.</summary>
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
    string domain = Mod.Info.ModID;
    string[] bricks =
    [
      "black",
      "brown",
      "cream",
      "fire",
      "gray",
      "orange",
      "red",
      "tan",
    ];
    foreach (string brick in bricks)
      foreach (
        string shape in new[]
        {
        $"passthrough-{brick}-ns",
        $"passthroughbend-{brick}-nw",
        }
      )
        ExlibChecks.Exempt(
          domain,
          "Obtainability",
          $"{domain}:pipe-plated-{shape}",
          "this sample builds the tier's segments; its wall fittings carry no recipe"
        );
  }
}
