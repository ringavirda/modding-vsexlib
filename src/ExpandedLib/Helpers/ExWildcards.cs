using System.Text;

namespace ExpandedLib.Helpers;

/// <summary>Picks one concrete choice out of a wildcard code path, the way a rig or a scenario
/// fills a layout cell.</summary>
internal static class ExWildcards {
  /// <summary>Collapses every alternation group of <paramref name="path"/>, <c>@(a|b)</c> or a
  /// bare <c>(a|b)</c>, to its first branch, a branch's own nested alternation included. An
  /// unbalanced <c>(</c> is kept as written; <c>*</c> and every other character are kept.</summary>
  internal static string FirstAlternative(string path) {
    var sb = new StringBuilder();
    int i = 0;
    while (i < path.Length) {
      bool tagged = path[i] == '@' && i + 1 < path.Length && path[i + 1] == '(';
      if (!tagged && path[i] != '(') {
        sb.Append(path[i++]);
        continue;
      }

      int open = tagged ? i + 1 : i;
      int close = MatchingParen(path, open);
      if (close < 0) {
        sb.Append(path[i++]);
        continue;
      }

      sb.Append(
        FirstAlternative(
          FirstBranch(path.Substring(open + 1, close - open - 1))
        )
      );
      i = close + 1;
    }
    return sb.ToString();
  }

  // Index of the ) closing the ( at open, or -1 when unbalanced.
  private static int MatchingParen(string s, int open) {
    int depth = 0;
    for (int i = open; i < s.Length; i++) {
      if (s[i] == '(')
        depth++;
      else if (s[i] == ')' && --depth == 0)
        return i;
    }
    return -1;
  }

  // The part of an alternation body before its first top-level |.
  private static string FirstBranch(string inner) {
    int depth = 0;
    for (int i = 0; i < inner.Length; i++) {
      if (inner[i] == '(')
        depth++;
      else if (inner[i] == ')')
        depth--;
      else if (inner[i] == '|' && depth == 0)
        return inner[..i];
    }
    return inner;
  }
}
