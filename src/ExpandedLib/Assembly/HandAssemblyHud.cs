using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Assembling;

/// <summary>The line shown while a part is held: which placement is shown, what it joins, what stays open and what fixing costs.</summary>
internal static class HandAssemblyHud {
  /// <summary>Builds the line, such as <c>3/4 Tee: joins Pipe (N), Pipe (S); open E | 2 Bolt bundle, 37 carried</c>.</summary>
  /// <param name="get">Translates a lang key with its arguments; <c>Lang.Get</c> in the game.</param>
  /// <param name="shown">The shown placement's number, from 1.</param>
  /// <param name="count">How many placements are offered.</param>
  /// <param name="part">The placed block's name.</param>
  /// <param name="joins">The neighbours the placement joins, each with its name and the face of the placement it joins on.</param>
  /// <param name="open">The arms with nothing to join; none leaves the open clause out.</param>
  /// <param name="cost">The fasteners fixing spends; 0 leaves the cost clause out.</param>
  /// <param name="fastener">The fastener's name.</param>
  /// <param name="carried">How many of the fastener the player carries.</param>
  public static string Line(
    Func<string, object[], string> get,
    int shown,
    int count,
    string part,
    IReadOnlyList<(string Name, BlockFacing Face)> joins,
    IReadOnlyList<BlockFacing> open,
    int cost,
    string fastener,
    int carried
  ) {
    var line = new StringBuilder(
      get(ExlibLang.HandassemblyHudHead, [shown, count, part])
    );
    line.Append(' ');
    line.Append(
      joins.Count == 0
        ? get(ExlibLang.HandassemblyHudNojoins, [])
        : get(
          ExlibLang.HandassemblyHudJoins,
          [string.Join(", ", joins.Select(j => $"{j.Name} ({Letter(j.Face)})"))]
        )
    );
    if (open.Count > 0)
      line.Append("; ")
        .Append(
          get(
            ExlibLang.HandassemblyHudOpen,
            [string.Join(", ", open.Select(Letter))]
          )
        );
    if (cost > 0)
      line.Append(" | ")
        .Append(get(ExlibLang.HandassemblyHudCost, [cost, fastener, carried]));
    return line.ToString();
  }

  private static char Letter(BlockFacing face) =>
    char.ToUpperInvariant(face.Code[0]);
}
