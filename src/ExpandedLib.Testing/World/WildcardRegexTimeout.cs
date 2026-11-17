using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;
using Vintagestory.API.Util;

namespace ExpandedLib.Testing;

/// <summary>Matches the game's <c>@</c>-prefixed wildcard patterns with a match timeout of
/// <see cref="Timeout"/> in place of the game's 1 s, through a prefix on
/// <c>WildcardUtil.RegexCache.IsMatch</c>. A test host short of memory can stall a match past
/// 1 s, which the game's loaders log as an Error. A match still running at <see cref="Timeout"/>
/// throws <see cref="RegexMatchTimeoutException"/> as the game's does.</summary>
internal static class WildcardRegexTimeout {
  /// <summary>The match timeout the patched cache builds its expressions with.</summary>
  internal static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

  private static readonly ConcurrentDictionary<string, Regex> Cache = new();
  private static readonly object Gate = new();
  private static readonly Harmony Patcher = new(
    "expandedlib.testing.wildcardregex"
  );
  private static bool _installed;

  /// <summary>Patches the game's regex cache for the rest of the process. Later calls do nothing;
  /// a game whose <c>WildcardUtil</c> has no such cache is left unpatched.</summary>
  internal static void Install() {
    lock (Gate) {
      if (_installed)
        return;
      _installed = true;
      MethodInfo? isMatch = typeof(WildcardUtil)
        .GetNestedType("RegexCache", BindingFlags.NonPublic)
        ?.GetMethod(
          "IsMatch",
          BindingFlags.Static | BindingFlags.Public,
          [typeof(string), typeof(string)]
        );
      if (isMatch == null)
        return;
      Patcher.Patch(
        isMatch,
        prefix: new HarmonyMethod(
          typeof(WildcardRegexTimeout).GetMethod(
            nameof(IsMatch),
            BindingFlags.Static | BindingFlags.NonPublic
          )
        )
      );
    }
  }

  /// <summary>The cached expression for <paramref name="pattern"/>, an <c>@</c>-prefixed wildcard,
  /// anchored at both ends as the game builds it; built on first use.</summary>
  internal static Regex For(string pattern) =>
    Cache.GetOrAdd(
      pattern,
      p => new Regex(
        string.Concat("^", p.AsSpan(1), "$"),
        RegexOptions.CultureInvariant,
        Timeout
      )
    );

  private static bool IsMatch(string pattern, string input, ref bool __result) {
    __result = For(pattern).IsMatch(input);
    return false;
  }
}
