#if GAME_GE_1_22
using Vintagestory.API.Common;

namespace ExpandedLib.WorldGen;

/// <summary>The multiplier a world configuration value gives a deposit's tries per chunk.</summary>
internal static class WorldScale
{
  /// <summary>
  /// Reads the configuration value <paramref name="scale"/> names and returns its multiplier.
  /// Returns 1 when <paramref name="scale"/> is null or the key is not set; returns 1 and logs a
  /// warning for a value the table does not list.
  /// </summary>
  /// <param name="scale">The file's <c>worldScale</c>, or null.</param>
  /// <param name="world">The world whose configuration is read.</param>
  /// <param name="logger">Receives the warning.</param>
  /// <param name="fromFile">The deposit file, named in the warning.</param>
  public static float Resolve(
    SeamWorldScale? scale,
    IWorldAccessor world,
    ILogger logger,
    string fromFile
  )
  {
    if (scale == null)
    {
      return 1;
    }
    string? value = world.Config.GetAsString(scale.Code);
    if (value == null)
    {
      return 1;
    }
    if (scale.Values.TryGetValue(value, out float factor))
    {
      return factor;
    }
    logger.Warning(
      "Deposit {0}: world setting {1} has value '{2}' which worldScale does not list; counted as 1.",
      fromFile,
      scale.Code,
      value
    );
    return 1;
  }
}
#endif
