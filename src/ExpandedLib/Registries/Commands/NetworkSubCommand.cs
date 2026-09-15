using System.ComponentModel;
using ExpandedLib.Networks;
using ExpandedLib.Registries;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace ExpandedLib.Registries;

/// <summary>
/// Adds <c>.exmod network hi</c> and <c>.exmod network unhi</c>: toggles the coloured block-network
/// highlight (see <see cref="NetworkHighlightModSystem"/>). Client-side.
/// </summary>
[SubCommandRegister(Side = EnumAppSide.Client)]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class NetworkSubCommand : IExSubCommand {
  public string ParentName => "exmod";

  public void Register(ICoreAPI api, Mod mod, IChatCommand parent) {
    var highlight = api.ModLoader.GetModSystem<NetworkHighlightModSystem>();

    parent
      .BeginSubCommand("network")
      .WithDescription(Lang.Get("exlib:command-network-desc"))
      .BeginSubCommand("hi")
      .WithDescription(Lang.Get("exlib:command-network-hi-desc"))
      .HandleWith(_ => DispatchHi(highlight))
      .EndSubCommand()
      .BeginSubCommand("unhi")
      .WithDescription(Lang.Get("exlib:command-network-unhi-desc"))
      .HandleWith(_ => DispatchUnhi(highlight))
      .EndSubCommand()
      .EndSubCommand();
  }

  /// <summary>The two handlers with fluent arg parsing stripped, callable directly by a test.</summary>
  internal static TextCommandResult DispatchHi(
    NetworkHighlightModSystem highlight
  ) {
    highlight.SetEnabled(true);
    return TextCommandResult.Success(Lang.Get(ExlibLang.NetworkHiOn));
  }

  internal static TextCommandResult DispatchUnhi(
    NetworkHighlightModSystem highlight
  ) {
    highlight.SetEnabled(false);
    return TextCommandResult.Success(Lang.Get(ExlibLang.NetworkHiOff));
  }
}
