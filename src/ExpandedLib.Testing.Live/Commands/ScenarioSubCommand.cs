using System.ComponentModel;
using System.Linq;
using ExpandedLib.Registries;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace ExpandedLib.Testing.Live;

/// <summary>
/// Adds <c>/exmod scenario run &lt;names...&gt; [keep]</c>: runs the named scenarios from
/// <c>&lt;dataPath&gt;/Scenarios</c> and logs the <c>[scenario]</c> lines.
/// </summary>
[SubCommandRegister(Side = EnumAppSide.Server)]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class ScenarioSubCommand : IExSubCommand {
  private ScenarioRunner? _runner;
  private ClimateHold? _climate;

  public string ParentName => "exmod";

  public void Register(ICoreAPI api, Mod mod, IChatCommand parent) {
    var server = (ICoreServerAPI)api;
    parent
      .BeginSubCommand("scenario")
      .RequiresPrivilege(Privilege.controlserver)
      .WithDescription("Runs live scenarios.")
      .BeginSubCommand("run")
      .WithDescription("run <names...> [keep]")
      .WithArgs(api.ChatCommands.Parsers.OptionalAll("names"))
      .HandleWith(args => Run(server, args))
      .EndSubCommand()
      .EndSubCommand();
  }

  private TextCommandResult Run(ICoreServerAPI api, TextCommandCallingArgs args) {
    if (_runner is { Active: true })
      return TextCommandResult.Error("A scenario run is already in progress.");
    string[] words = ((string?)args[0] ?? "").Split(
      ' ',
      System.StringSplitOptions.RemoveEmptyEntries
    );
    bool keep = words.Contains("keep");
    string[] names = [.. words.Where(w => w != "keep")];
    _climate ??= new ClimateHold(api.WorldManager.RegionSize);
    _runner = new ScenarioRunner(
      api,
      names,
      keep,
      args.Caller.Player as IServerPlayer,
      _runner?.Plots,
      _climate
    );
    return TextCommandResult.Success(
      $"Scenario run started: {names.Length} name(s)."
    );
  }
}
