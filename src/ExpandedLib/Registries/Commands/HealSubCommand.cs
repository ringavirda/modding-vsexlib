using System.ComponentModel;
using ExpandedLib.Migrations;
using ExpandedLib.Registries;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace ExpandedLib.Registries;

/// <summary>
/// Adds <c>/exmod heal</c>: sweeps every loaded chunk and recreates any orphaned block entity.
/// Server-side; the <c>/exmod</c> root requires <c>controlserver</c>.
/// </summary>
[SubCommandRegister(Side = EnumAppSide.Server)]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class HealSubCommand : IExSubCommand {
  public string ParentName => "exmod";

  public void Register(ICoreAPI api, Mod mod, IChatCommand parent) {
    var healer = api.ModLoader.GetModSystem<BlockEntityHealModSystem>();

    parent
      .BeginSubCommand("heal")
      .WithDescription(Lang.Get("exlib:command-heal-desc"))
      .HandleWith(_ => Dispatch(healer))
      .EndSubCommand();
  }

  /// <summary>The command's logic with fluent arg parsing stripped, callable directly by a test.</summary>
  internal static TextCommandResult Dispatch(BlockEntityHealModSystem healer) =>
    TextCommandResult.Success(
      Lang.Get("exlib:command-heal-result", healer.HealLoadedChunks())
    );
}
