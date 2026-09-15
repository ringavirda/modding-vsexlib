using Vintagestory.API.Common;

namespace ExpandedLib.Registries;

/// <summary>
/// A chat sub-command that attaches itself to an existing top-level command, letting one mod hang
/// options off another's command without that command declaring them up front.
/// </summary>
public interface IExSubCommand {
  /// <summary>Name of the existing top-level command to attach to (e.g. <c>"exmod"</c>).</summary>
  string ParentName { get; }

  /// <summary>Builds this sub-command onto <paramref name="parent"/>. Called once per applicable side by <see cref="CommandRegistry.RegisterAll"/>.</summary>
  void Register(ICoreAPI api, Mod mod, IChatCommand parent);
}
