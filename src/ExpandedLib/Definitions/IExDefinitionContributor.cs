using Vintagestory.API.Common;

namespace ExpandedLib.Definitions;

/// <summary>
/// Implemented by an entry point that emits code-first definitions depending on assets that are
/// only readable once every mod's <c>Start</c> has run. Server only.
/// </summary>
public interface IExDefinitionContributor {
  /// <summary>Registers this contributor's definitions through <see cref="ExDefinitions"/>'s
  /// <c>Register*</c> methods.</summary>
  void Contribute(ICoreAPI api);
}
