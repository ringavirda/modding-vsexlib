#if GAME_GE_1_22
using System.Collections.Generic;
using System.Linq;

namespace ExpandedLib.Testing;

/// <summary>The tick orders a setup's <c>tickOrders</c> names (setups.md, Tick orders).</summary>
internal static class SetupTickOrders
{
  /// <summary>The name of the network tick in an order.</summary>
  public const string Network = "network";

  /// <summary>
  /// Every order of <paramref name="ids"/> with the network tick after them, then the network tick
  /// in each earlier place of <paramref name="ids"/> as given and of its reverse: n! + 2n orders.
  /// </summary>
  /// <param name="ids">The placed machines <c>tickOrders</c> names, two to four.</param>
  /// <returns>Each order as the ids and <see cref="Network"/> in firing order.</returns>
  public static IReadOnlyList<string[]> Of(IReadOnlyList<string> ids)
  {
    var orders = new List<string[]>();
    foreach (IEnumerable<string> order in Permutations(ids))
      orders.Add([.. order, Network]);
    foreach (
      string[] named in (string[][])
        [
          [.. ids],
          [.. ids.Reverse()],
        ]
    )
      for (int at = 0; at < named.Length; at++)
        orders.Add([.. named.Take(at), Network, .. named.Skip(at)]);
    return orders;
  }

  /// <summary>The name a case takes for <paramref name="order"/>: its entries joined by commas.</summary>
  public static string Name(IEnumerable<string> order) =>
    string.Join(",", order);

  private static IEnumerable<IEnumerable<string>> Permutations(
    IReadOnlyList<string> ids
  ) =>
    ids.Count <= 1
      ? [ids]
      : ids.SelectMany(
        (id, i) =>
          Permutations([.. ids.Take(i), .. ids.Skip(i + 1)])
            .Select(rest => rest.Prepend(id))
      );
}
#endif
