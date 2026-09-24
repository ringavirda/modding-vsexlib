using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Definitions;
using ExpandedLib.Structures;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Datastructures;

namespace ExpandedLib.Testing;

/// <summary>
/// Guards where weather snow meets a layout open to the sky: in a column topped by an open cell,
/// the first solid cell below the open run and the open cell above it carry
/// <see cref="CellRoles.NoSnow"/>, since snow lies on the one and settles in the other.
/// </summary>
/// <remarks>An open cell is one the layout completes with air. A column topped by a solid cell or
/// holding no solid cell requires nothing, and a mark the rule does not require is never a
/// finding.</remarks>
public static class OpenLayoutCells {
  /// <summary>What one run read and found.</summary>
  /// <param name="Layouts">The definitions carrying a <c>multiblockStructure</c> layout.</param>
  /// <param name="Cells">The cells the rule requires across those layouts.</param>
  /// <param name="Findings">One line per required cell without the role, as
  /// <c>{golden path}: ({x},{y},{z}) {what} carries no nosnow</c>.</param>
  public sealed record Result(
    int Layouts,
    int Cells,
    IReadOnlyList<string> Findings
  );

  /// <summary>Runs the rule over every layout among <paramref name="defs"/>; a definition without
  /// a layout is skipped.</summary>
  /// <param name="defs">The definitions to read.</param>
  /// <returns>The layouts read, the cells the rule requires, and one finding per required cell
  /// without the role; no findings when clean.</returns>
  /// <exception cref="NullReferenceException">A layout has no <c>blockNumbers</c> or no
  /// <c>offsets</c>.</exception>
  /// <exception cref="InvalidCastException">A layout's <c>blockNumbers</c> is not an object, or
  /// its <c>offsets</c> not an array.</exception>
  /// <exception cref="ArgumentException">A block number, or an offset's <c>x</c>, <c>y</c>,
  /// <c>z</c> or <c>w</c>, is missing or not an integer, or an offset is an array.</exception>
  /// <exception cref="InvalidOperationException">An offset is a JSON value, not an
  /// object.</exception>
  /// <exception cref="KeyNotFoundException">An offset's <c>w</c> names no block
  /// number.</exception>
  public static Result Check(IEnumerable<ExBlockDef> defs) {
    int layouts = 0,
      cells = 0;
    var findings = new List<string>();
    foreach (ExBlockDef def in defs) {
      if (def.ToJson()["attributes"]?["multiblockStructure"] is not JObject)
        continue;
      layouts++;
      var open = LayoutTable
        .From(def)
        .ToDictionary(
          c => (c.Key.X, c.Key.Y, c.Key.Z),
          c => AirSatisfied(c.Value)
        );
      var marked = MultiblockCellRoles
        .FromAttributes(new JsonObject((JObject)def.ToJson()["attributes"]!))
        .CellsOf(CellRoles.NoSnow);

      foreach (
        var column in open
          .Keys.GroupBy(c => (c.X, c.Z))
          .OrderBy(g => g.Key.X)
          .ThenBy(g => g.Key.Z)
      ) {
        var down = column.OrderByDescending(c => c.Y).ToList();
        if (!open[down[0]])
          continue;
        int solid = down.FindIndex(c => !open[c]);
        if (solid < 0)
          continue;
        foreach (
          var (cell, what) in new[]
          {
            (down[solid - 1], "snow settles in"),
            (down[solid], "snow lies on"),
          }
        ) {
          cells++;
          if (!marked.Contains(cell))
            findings.Add(
              $"{DefinitionGoldens.RelativePath(def)}: ({cell.X},{cell.Y},{cell.Z}) {what} "
                + "carries no nosnow"
            );
        }
      }
    }
    return new Result(layouts, cells, findings);
  }

  // Satisfied by leaving the cell empty: the first alternative of the wanted path is air, the way
  // StructureRig fills a layout.
  private static bool AirSatisfied(string wanted) {
    string path = wanted[(wanted.IndexOf(':') + 1)..];
    if (path.StartsWith("@(", StringComparison.Ordinal)) {
      int depth = 0,
        end = 2;
      for (; end < path.Length; end++) {
        char c = path[end];
        if (c == '(')
          depth++;
        else if (c == ')' && depth-- == 0)
          break;
        else if (c == '|' && depth == 0)
          break;
      }
      path = path[2..end];
    }
    return path.Replace("*", "x") == "air";
  }
}
