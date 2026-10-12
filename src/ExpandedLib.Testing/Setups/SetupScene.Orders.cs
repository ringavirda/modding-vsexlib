#if GAME_GE_1_22
using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Testing;

public sealed partial class SetupScene
{
  private bool _ordered;
  private long? _networkBefore;

  /// <summary>The network tick a second runs once, after the listeners or in the place a tick
  /// order gives it.</summary>
  internal Action NetworkTick { get; set; }

  /// <summary>Fires the seconds that follow in <paramref name="order"/>: the listeners of the named
  /// machines after every other, in the order given, and the network tick in the place of
  /// <see cref="SetupTickOrders.Network"/> among them.</summary>
  /// <param name="order">Placed ids and the network entry, each once, as <see cref="SetupTickOrders.Of"/> gives it.</param>
  /// <exception cref="System.InvalidOperationException">A named machine registered no tick listener.</exception>
  internal void Order(IReadOnlyList<string> order)
  {
    string[] machines = [.. order.Where(o => o != SetupTickOrders.Network)];
    List<HashSet<long>> groups = [.. machines.Select(ListenersOf)];
    World.MoveListenersLast(groups);
    int network = order.ToList().IndexOf(SetupTickOrders.Network);
    _networkBefore =
      network == order.Count - 1
        ? null
        : World
          .TickCallbacks()
          .Select(l => l.Id)
          .First(groups[network].Contains);
    _ordered = true;
  }

  private HashSet<long> ListenersOf(string id)
  {
    IReadOnlyList<BlockPos> cells = CellsOf(id);
    HashSet<long> own =
    [
      .. cells.SelectMany(c => _listeners.GetValueOrDefault(c) ?? []),
      .. World
        .TickCallbacks()
        .Where(l =>
          OwnerPos(l.Callback) is { } pos
          && cells.Any(c => c.X == pos.X && c.Y == pos.Y && c.Z == pos.Z)
        )
        .Select(l => l.Id),
    ];
    if (own.Count == 0)
      throw Fail(
        "tickOrders",
        $"\"{id}\" registered no tick listener, so it has no place in a tick order."
      );
    return own;
  }

  private static BlockPos? OwnerPos(System.Action<float> callback) =>
    callback.Target switch
    {
      BlockEntity be => be.Pos,
      BlockEntityBehavior behavior => behavior.Blockentity.Pos,
      _ => null,
    };
}
#endif
