#if GAME_GE_1_22
using System;
using System.Runtime.CompilerServices;
using ExpandedLib.Catalogues;
using Vintagestory.API.Common;

namespace ExpandedLib.Machines;

/// <summary>
/// Turns a grid recipe that has a <see cref="CraftSequence"/> away from every grid but its
/// machine's, and one keyed to a diagram (<see cref="RecipeDiagram"/>) away from every match not
/// marked with that diagram. A station marks its match with <see cref="At(string, string?)"/>.
/// </summary>
public static class CraftSequenceGate
{
  [ThreadStatic]
  private static string? _machine;

  [ThreadStatic]
  private static string? _diagram;

  private static readonly ConditionalWeakTable<IEventAPI, object> Registered =
    new();
  private static readonly object Marker = new();

  /// <summary>Marks the matches the calling thread makes until the scope is disposed as made at
  /// <paramref name="machine"/> with no diagram in its slot. Same as <c>At(machine, null)</c>.</summary>
  /// <param name="machine">The machine key the station's sequences are filed under.</param>
  /// <returns>The scope to dispose when the station's match is done.</returns>
  public static IDisposable At(string machine) => At(machine, null);

  /// <summary>Marks the matches the calling thread makes until the scope is disposed as made at
  /// <paramref name="machine"/> with <paramref name="diagram"/> in its diagram slot. Scopes nest;
  /// disposing restores the enclosing mark.</summary>
  /// <param name="machine">The machine key the station's sequences are filed under.</param>
  /// <param name="diagram">The full code of the diagram in the station's slot; null for an empty
  /// slot or a station without one.</param>
  /// <returns>The scope to dispose when the station's match is done.</returns>
  public static IDisposable At(string machine, string? diagram)
  {
    var before = new Scope(_machine, _diagram);
    _machine = machine;
    _diagram = diagram;
    return before;
  }

  /// <summary>Whether <paramref name="recipe"/> may match on the grid the current mark names.</summary>
  /// <param name="registry">The catalogue whose sequences turn recipes away.</param>
  /// <param name="recipe">The grid recipe being matched.</param>
  /// <returns>False for a recipe keyed to a diagram unless the mark names that diagram, compared
  /// ordinally; otherwise true for a recipe with no sequence in <paramref name="registry"/>, or whose
  /// sequence is the marked machine's, and false for any other, including with no mark.</returns>
  public static bool Allows(CraftSequenceRegistry registry, GridRecipe recipe)
  {
    if (
      RecipeDiagram.Of(recipe) is { } diagram
      && !string.Equals(diagram, _diagram, StringComparison.Ordinal)
    )
      return false;
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

  private sealed class Scope(string? machine, string? diagram) : IDisposable
  {
    public void Dispose()
    {
      _machine = machine;
      _diagram = diagram;
    }
  }
}
#endif
