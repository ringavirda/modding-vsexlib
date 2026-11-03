using System.ComponentModel;
using ExpandedLib.Registries;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace ExpandedLib.Testing.Live;

/// <summary>
/// Adds <c>/exmod probe run &lt;x|spawn&gt; &lt;z|spawn&gt; &lt;columnsX&gt; &lt;columnsZ&gt; &lt;tileColumns&gt;
/// &lt;code...&gt;</c>: generates the chunk columns tile by tile, counts the blocks matching the codes
/// per tile and writes <c>&lt;dataPath&gt;/probe/probe-&lt;seed&gt;-&lt;time&gt;.json</c>; the
/// <c>[probe] done:</c> log line names it. With <c>+code</c> words it also writes the positions of those
/// blocks and with <c>@terrain</c> the terrain heights and provinces (see <see cref="WorldProbe"/>).
/// <c>/exmod probe reading &lt;x&gt; &lt;z&gt;...</c> loads the column at each block position and writes the
/// prospecting pick's density readings to <c>&lt;dataPath&gt;/probe/readings-&lt;seed&gt;-&lt;time&gt;.json</c>.
/// </summary>
[SubCommandRegister(Side = EnumAppSide.Server)]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class ProbeSubCommand : IExSubCommand {
  private WorldProbe? _probe;
  private ProbeReadings? _readings;

  public string ParentName => "exmod";

  public void Register(ICoreAPI api, Mod mod, IChatCommand parent) {
    var server = (ICoreServerAPI)api;
    parent
      .BeginSubCommand("probe")
      .RequiresPrivilege(Privilege.controlserver)
      .WithDescription("Counts blocks over generated chunk columns.")
      .BeginSubCommand("run")
      .WithDescription(ProbeRequest.Usage)
      .WithArgs(api.ChatCommands.Parsers.OptionalAll("arguments"))
      .HandleWith(args => Run(server, args))
      .EndSubCommand()
      .BeginSubCommand("reading")
      .WithDescription("reading <x> <z> [<x> <z>...]")
      .WithArgs(api.ChatCommands.Parsers.OptionalAll("positions"))
      .HandleWith(args => Reading(server, args))
      .EndSubCommand()
      .EndSubCommand();
  }

  private TextCommandResult Reading(
    ICoreServerAPI api,
    TextCommandCallingArgs args
  ) {
    if (_readings is { Active: true })
      return TextCommandResult.Error("A readings run is already in progress.");
    var positions = ProbeReadings.ParsePositions(
      (string?)args[0] ?? "",
      out string error
    );
    if (positions == null)
      return TextCommandResult.Error(error);
    try {
      _readings = new ProbeReadings(api, positions);
    } catch (System.InvalidOperationException e) {
      return TextCommandResult.Error(e.Message);
    }
    return TextCommandResult.Success("Readings started.");
  }

  private TextCommandResult Run(ICoreServerAPI api, TextCommandCallingArgs args) {
    if (_probe is { Active: true })
      return TextCommandResult.Error("A probe run is already in progress.");
    ProbeRequest? request = ProbeRequest.Parse(
      (string?)args[0] ?? "",
      out string error
    );
    if (request == null)
      return TextCommandResult.Error(error);
    _probe = new WorldProbe(api, request);
    return TextCommandResult.Success("Probe started.");
  }
}
