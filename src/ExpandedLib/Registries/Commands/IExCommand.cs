using Vintagestory.API.Common;

namespace ExpandedLib.Registries;

/// <summary>
/// A self-contained chat command, carrying a <see cref="CommandRegisterAttribute"/> and building
/// itself in <see cref="Register"/>. Instantiated through a parameterless constructor.
/// </summary>
public interface IExCommand {
  /// <summary>Builds and registers this command. Called once per applicable side by <see cref="CommandRegistry.RegisterAll"/>.</summary>
  void Register(ICoreAPI api, Mod mod);
}
