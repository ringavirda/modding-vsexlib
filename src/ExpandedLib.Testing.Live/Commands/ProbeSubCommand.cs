using System.ComponentModel;
using ExpandedLib.Registries;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace ExpandedLib.Testing.Live;

/// <summary>
/// Adds <c>/exmod probe run &lt;x|spawn&gt; &lt;z|spawn&gt; &lt;columnsX&gt; &lt;columnsZ&gt; &lt;tileColumns&gt;
/// &lt;code...&gt;</c>: generates the chunk columns tile by tile, counts the blocks matching the codes
/// per tile and writes <c>&lt;dataPath&gt;/probe/probe-&lt;seed&gt;-&lt;time&gt;.json</c>; the
/// <c>[probe] done:</c> log line names it.
/// </summary>
[SubCommandRegister(Side = EnumAppSide.Server)]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class ProbeSubCommand : IExSubCommand {
  private WorldProbe? _probe;

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
      .EndSubCommand();
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
