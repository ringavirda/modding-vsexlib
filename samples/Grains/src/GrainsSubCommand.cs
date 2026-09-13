using System.Linq;
using ExpandedLib.Registries;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace Grains;

/// <summary>The whole command walk: <c>/exmod grains</c> prints one line per catalogue entry.</summary>
[SubCommandRegister(Side = EnumAppSide.Server)]
public sealed class GrainsSubCommand : IExSubCommand {
  public string ParentName => "exmod";

  public void Register(ICoreAPI api, Mod mod, IChatCommand parent) {
    parent
      .BeginSubCommand("grains")
      .WithDescription(Lang.Get("grains:command-grains-desc"))
      .HandleWith(args =>
        TextCommandResult.Success(
          string.Join(
            "\n",
            GrainCatalogue.All.Select(g => $"{g.Code}: {g.Grain} -> {g.Flour}, {g.Seconds}")
          )
        )
      )
      .EndSubCommand();
  }
}
