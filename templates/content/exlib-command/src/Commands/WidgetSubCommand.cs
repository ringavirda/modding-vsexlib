using ExpandedLib.Registries;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace WidgetNamespace.Commands;

/// <summary>The whole command walk: <c>/exmod widget</c> answers one line.</summary>
[SubCommandRegister(Side = EnumAppSide.Server)]
public sealed class WidgetSubCommand : IExSubCommand {
  public string ParentName => "exmod";

  public void Register(ICoreAPI api, Mod mod, IChatCommand parent) {
    parent
      .BeginSubCommand("widget")
      .WithDescription(Lang.Get("widgetdomain:command-widget-desc"))
      .HandleWith(_ =>
        TextCommandResult.Success(Lang.Get("widgetdomain:command-widget-result"))
      )
      .EndSubCommand();
  }
}
