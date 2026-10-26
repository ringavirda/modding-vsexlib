using System.ComponentModel;
using ExpandedLib.Migrations;
using ExpandedLib.Registries;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace ExpandedLib.Registries;

/// <summary>
/// Adds <c>/exmod heal</c>: sweeps every loaded chunk and recreates any orphaned block entity.
/// Server-side; requires <c>controlserver</c>.
/// </summary>
[SubCommandRegister(Side = EnumAppSide.Server)]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class HealSubCommand : IExSubCommand {
  public string ParentName => "exmod";

  public void Register(ICoreAPI api, Mod mod, IChatCommand parent) {
    var healer = api.ModLoader.GetModSystem<BlockEntityHealModSystem>();

    parent
      .BeginSubCommand("heal")
      .RequiresPrivilege(Privilege.controlserver)
      .WithDescription(Lang.Get("exlib:command-heal-desc"))
      .HandleWith(args => Dispatch(healer, args.LanguageCode))
      .EndSubCommand();
  }

  /// <summary>The command's logic with fluent arg parsing stripped, callable directly by a test;
  /// the reply is worded in <paramref name="languageCode"/>.</summary>
  internal static TextCommandResult Dispatch(
    BlockEntityHealModSystem healer,
    string languageCode
  ) =>
    TextCommandResult.Success(
      Lang.GetL(
        languageCode,
        "exlib:command-heal-result",
        healer.HealLoadedChunks()
      )
    );
}
