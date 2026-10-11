using ExpandedLib.Registries;
using Vintagestory.API.Common;

namespace ExpandedLib.Testing.Live;

/// <summary>The live scenario mod: registers <c>/exmod scenario</c> on the server.</summary>
public sealed class ScenarioModSystem : ModSystem {
  public override bool ShouldLoad(EnumAppSide forSide) =>
    forSide == EnumAppSide.Server;

  public override void StartServerSide(
    Vintagestory.API.Server.ICoreServerAPI api
  ) =>
    CommandRegistry.RegisterAll(api, Mod, typeof(ScenarioModSystem).Assembly);
}
