using System.ComponentModel;
using ExpandedLib.Registries;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace ExpandedLib.Registries;

/// <summary>
/// The shared <c>exmod</c> root command for every Expanded mod; exlib always registers it. On its
/// own it prints help; dependent mods hang their options off it as <see cref="IExSubCommand"/>s.
/// </summary>
[CommandRegister(Side = EnumAppSide.Universal)]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class ExmodCommand : IExCommand {
  public void Register(ICoreAPI api, Mod mod) {
    bool isClient = api.Side == EnumAppSide.Client;

    // ChatCommands is per-side: client hosts ".exmod", server hosts "/exmod".
    string privilege = isClient ? Privilege.chat : Privilege.controlserver;

    api.ChatCommands.GetOrCreate("exmod")
      .RequiresPrivilege(privilege)
      .WithDescription(
        Lang.Get(
          isClient
            ? "exlib:command-exmod-desc-client"
            : "exlib:command-exmod-desc-server"
        )
      )
      .HandleWith(args =>
        TextCommandResult.Success(
          Lang.GetL(
            args.LanguageCode,
            isClient
              ? "exlib:command-exmod-help-client"
              : "exlib:command-exmod-help-server"
          )
        )
      );
  }
}
