using System;
using System.Collections.Generic;

namespace ExpandedLib.FastForwarding;

/// <summary>Partitions members into connected groups: two members share a group when they share a link key, directly or through other members.</summary>
internal static class FastForwardGroups {
  /// <summary>Splits <paramref name="members"/> by the keys <paramref name="linksOf"/> names for each; a member with no shared key is a group of one. Keys compare by value equality.</summary>
  internal static List<List<T>> Partition<T>(
    IReadOnlyList<T> members,
    Func<T, IEnumerable<object>> linksOf
  ) {
    int[] parent = new int[members.Count];
    for (int i = 0; i < parent.Length; i++)
      parent[i] = i;

    int Find(int i) {
      while (parent[i] != i)
        i = parent[i] = parent[parent[i]];
      return i;
    }

    var owner = new Dictionary<object, int>();
    for (int i = 0; i < members.Count; i++)
      foreach (object key in linksOf(members[i]))
        if (owner.TryGetValue(key, out int other))
          parent[Find(i)] = Find(other);
        else
          owner[key] = i;

    var byRoot = new Dictionary<int, List<T>>();
    for (int i = 0; i < members.Count; i++) {
      int root = Find(i);
      if (!byRoot.TryGetValue(root, out List<T>? group))
        byRoot[root] = group = [];
      group.Add(members[i]);
    }
    return [.. byRoot.Values];
  }
}
