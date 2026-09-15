using System.Collections.Generic;
using Vintagestory.API.Datastructures;

namespace ExpandedLib.Structures;

/// <summary>
/// Reads the <c>multiblockRoles</c> attribute: <see cref="CellRole"/> to the authored (north-frame)
/// offsets of cells carrying that role. A sibling of <c>multiblockStructure</c>, kept out of vanilla's
/// own schema. A layout with no roles gets <see cref="None"/>.
/// </summary>
public sealed class MultiblockCellRoles {
  /// <summary>A layout that declares no roles. Every lookup answers empty.</summary>
  public static readonly MultiblockCellRoles None = new(
    new Dictionary<CellRole, HashSet<(int X, int Y, int Z)>>()
  );

  private static readonly HashSet<(int X, int Y, int Z)> NoCells = new();

  private readonly Dictionary<
    CellRole,
    HashSet<(int X, int Y, int Z)>
  > _cellsOf;

  private MultiblockCellRoles(
    Dictionary<CellRole, HashSet<(int X, int Y, int Z)>> cellsOf
  ) => _cellsOf = cellsOf;

  /// <summary>True when the layout marks no cell with any role.</summary>
  public bool IsEmpty => _cellsOf.Count == 0;

  /// <summary>The authored offsets carrying <paramref name="role"/>, empty when the layout declares none.</summary>
  public IReadOnlySet<(int X, int Y, int Z)> CellsOf(CellRole role) =>
    _cellsOf.TryGetValue(role, out var cells) ? cells : NoCells;

  /// <summary>Reads the <c>multiblockRoles</c> attribute, or <see cref="None"/> when absent. Never throws.</summary>
  public static MultiblockCellRoles FromAttributes(JsonObject? attributes) {
    var map = new Dictionary<CellRole, HashSet<(int X, int Y, int Z)>>();
    foreach (
      var (cell, key) in LayoutAttribute.CellsByKey(
        attributes,
        "multiblockRoles"
      )
    ) {
      // Blank keys skipped: Of() throws on an empty role.
      if (string.IsNullOrWhiteSpace(key))
        continue;
      // Primary constructor, not Of(): preserves the role's declared arity.
      var role = new CellRole(key);
      if (!map.TryGetValue(role, out HashSet<(int X, int Y, int Z)>? offsets))
        map[role] = offsets = [];
      offsets.Add(cell);
    }
    return map.Count == 0 ? None : new MultiblockCellRoles(map);
  }
}
