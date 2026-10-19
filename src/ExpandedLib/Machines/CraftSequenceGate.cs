#if GAME_GE_1_22
using System;
using System.Runtime.CompilerServices;
using ExpandedLib.Catalogues;
using Vintagestory.API.Common;

namespace ExpandedLib.Machines;

/// <summary>
/// Turns a grid recipe that has a <see cref="CraftSequence"/> away from every grid but its
/// machine's: the hand grid, and a station of another machine. A station marks the match it makes
/// with <see cref="At"/>.
/// </summary>
public static class CraftSequenceGate
{
  [ThreadStatic]
  private static string? _machine;

  private static readonly ConditionalWeakTable<IEventAPI, object> Registered =
    new();
  private static readonly object Marker = new();

  /// <summary>Marks the matches the calling thread makes until the scope is disposed as made at
  /// <paramref name="machine"/>. Scopes nest; disposing restores the enclosing mark.</summary>
  /// <param name="machine">The machine key the station's sequences are filed under.</param>
  /// <returns>The scope to dispose when the station's match is done.</returns>
  public static IDisposable At(string machine)
  {
    string? before = _machine;
    _machine = machine;
    return new Scope(before);
  }

  /// <summary>Whether <paramref name="recipe"/> may match on the grid the current mark names.</summary>
  /// <param name="registry">The catalogue whose sequences turn recipes away.</param>
  /// <param name="recipe">The grid recipe being matched.</param>
  /// <returns>True for a recipe with no sequence in <paramref name="registry"/>, or whose sequence
  /// is the marked machine's; false otherwise, including with no mark.</returns>
  public static bool Allows(CraftSequenceRegistry registry, GridRecipe recipe)
  {
    string? name = recipe.Name?.ToString();
    return registry.Sequence(name) == null
      || registry.Sequence(_machine, name) != null;
  }

  /// <summary>Adds the gate to <paramref name="events"/>' grid-recipe matching, over
  /// <see cref="CraftSequenceRegistry.Shared"/>. Registering twice on one api is harmless and keeps the
  /// gate's place among the handlers.</summary>
  /// <param name="events">The event api whose <c>MatchesGridRecipe</c> the gate joins.</param>
  public static void Register(IEventAPI events)
  {
    if (Registered.TryGetValue(events, out _))
      return;
    Registered.Add(events, Marker);
    events.MatchesGridRecipe += Matches;
  }

  private static bool Matches(
    IPlayer player,
    GridRecipe recipe,
    ItemSlot[] ingredients,
    int gridWidth
  ) => Allows(CraftSequenceRegistry.Shared, recipe);

  private sealed class Scope(string? before) : IDisposable
  {
    public void Dispose() => _machine = before;
  }
}
#endif
