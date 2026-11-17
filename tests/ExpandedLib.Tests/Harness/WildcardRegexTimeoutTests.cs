// The game's wildcard regex cache this patches is read from the current game version only.
#if GAME_GE_1_22
using System.Collections.Concurrent;
using System.Reflection;
using System.Text.RegularExpressions;
using ExpandedLib.Testing;
using Vintagestory.API.Util;
using Xunit;

namespace ExpandedLib.Tests;

public class WildcardRegexTimeoutTests
{
  private static ConcurrentDictionary<string, Regex> GameCache() =>
    (ConcurrentDictionary<string, Regex>)
      typeof(WildcardUtil)
        .GetNestedType("RegexCache", BindingFlags.NonPublic)!
        .GetField("cache", BindingFlags.Static | BindingFlags.NonPublic)!
        .GetValue(null)!;

  // Fails when the game's loaders run without WildcardRegexTimeout.Install, or its prefix lets the
  // game's IsMatch run: the game's cache then builds the expression with its 1 s timeout.
  [Fact]
  public void A_vanilla_load_matches_wildcard_regexes_outside_the_games_cache()
  {
    using var world = new TestWorld();
    world.RegisterVanillaClasses();
    world.LoadVanilla(["axle"], []);
    const string pattern = "@wildcardregextimeout-(a|b)+";

    Assert.True(WildcardUtil.Match(pattern, "wildcardregextimeout-abba"));
    Assert.False(WildcardUtil.Match(pattern, "wildcardregextimeout-abc"));

    Assert.False(GameCache().ContainsKey(pattern));
  }
}
#endif
