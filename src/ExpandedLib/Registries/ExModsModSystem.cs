using System.ComponentModel;
using Vintagestory.API.Common;

namespace ExpandedLib.Registries;

/// <summary>Sets <see cref="ExMods.FlagKey"/> to <c>true</c> in <c>api.World.Config</c> for every
/// enabled mod, ahead of the JSON patch loader.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public class ExModsModSystem : ModSystem {
  public override double ExecuteOrder() => 0.0;

  public override void StartPre(ICoreAPI api) => SetFlags(api);

  public override void Start(ICoreAPI api) => SetFlags(api);

  // Runs from both StartPre and Start: World.Config may still be null in StartPre.
  private static void SetFlags(ICoreAPI api) {
    var config = api.World?.Config;
    if (config == null)
      return;
    foreach (Mod mod in api.ModLoader.Mods)
      config.SetBool(ExMods.FlagKey(mod.Info.ModID), true);
  }
}
