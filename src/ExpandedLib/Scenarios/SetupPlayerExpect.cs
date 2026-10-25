using Newtonsoft.Json.Linq;

namespace ExpandedLib.Scenarios;

/// <summary>The <c>expect</c> form that reads the scenario player: <c>{ player: { health: { min?, max? } } }</c>,
/// the player's current health in hit points. Only the live runner has a player with health.</summary>
internal static class SetupPlayerExpect {
  /// <summary>Whether the <c>expect</c> body is a player reading.</summary>
  public static bool Is(JObject body) => body["player"] != null;

  /// <summary>Checks the keys of a player reading.</summary>
  /// <exception cref="SetupFileException">The body has a key besides <c>player</c>, the player holds
  /// a key besides <c>health</c>, the health holds neither <c>min</c> nor <c>max</c> or a key besides
  /// them, a bound is no number, or <c>min</c> is over <c>max</c>.</exception>
  public static void Validate(JObject body, SetupJson.Where at) {
    SetupJson.RefuseUnknown(body, at, "player");
    SetupJson.Where player = at.In("player");
    JObject reading = SetupJson.Object(body["player"], player);
    SetupJson.RefuseUnknown(reading, player, "health");
    SetupJson.Where health = player.In("health");
    JObject bounds = SetupJson.Object(reading["health"], health);
    SetupJson.RefuseUnknown(bounds, health, "min", "max");
    if (bounds["min"] == null && bounds["max"] == null)
      throw health.Fail("takes \"min\" or \"max\".");
    double? min = Bound(bounds, "min", health);
    double? max = Bound(bounds, "max", health);
    if (min > max)
      throw health.Fail("\"min\" is over \"max\".");
  }

  /// <summary>Tests a health against the reading.</summary>
  /// <param name="body">The <c>expect</c> body.</param>
  /// <param name="health">The player's current health, in hit points.</param>
  /// <returns>What differs, or null when the health is within the bounds.</returns>
  public static string? Mismatch(JObject body, float health) {
    JObject bounds = (JObject)body["player"]!["health"]!;
    if (bounds["min"] != null && health < (double)bounds["min"]!)
      return $"health is {health}, expected at least {(double)bounds["min"]!}";
    if (bounds["max"] != null && health > (double)bounds["max"]!)
      return $"health is {health}, expected at most {(double)bounds["max"]!}";
    return null;
  }

  private static double? Bound(
    JObject bounds,
    string key,
    SetupJson.Where at
  ) => bounds[key] == null ? null : SetupJson.Number(bounds[key], at.In(key));
}
