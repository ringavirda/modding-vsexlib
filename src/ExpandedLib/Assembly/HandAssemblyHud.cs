using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Assembling;

/// <summary>The line shown while a part or a cap is held: which placement is shown, what it joins, what stays open and what fixing costs; or which cap goes on and what it costs.</summary>
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

  /// <summary>Builds the line of an aimed cap, such as <c>Cap Cast iron pipe cap | 4 Bolt, 37 carried</c>.</summary>
  /// <param name="get">Translates a lang key with its arguments; <c>Lang.Get</c> in the game.</param>
  /// <param name="cap">The held cap's name.</param>
  /// <param name="refusal">The line of the reason the aimed end takes no cap, shown in place of the cost; null when it takes one.</param>
  /// <param name="cost">The fasteners fitting spends; 0 leaves the cost clause out.</param>
  /// <param name="fastener">The fastener's name.</param>
  /// <param name="carried">How many of the fastener the player carries.</param>
  public static string CapLine(
    Func<string, object[], string> get,
    string cap,
    string? refusal,
    int cost,
    string fastener,
    int carried
  ) {
    var line = new StringBuilder(get(ExlibLang.HandassemblyHudCap, [cap]));
    if (refusal != null)
      line.Append(" | ").Append(refusal);
    else if (cost > 0)
      line.Append(" | ")
        .Append(get(ExlibLang.HandassemblyHudCost, [cost, fastener, carried]));
    return line.ToString();
  }

  /// <summary>Builds the line of an aimed fit, such as <c>Fit: makes Shaft with key | 1 Key, 4 carried</c>.</summary>
  /// <param name="get">Translates a lang key with its arguments; <c>Lang.Get</c> in the game.</param>
  /// <param name="made">The name of the block the fit makes.</param>
  /// <param name="cost">The fasteners the fit spends; 0 leaves the cost clause out.</param>
  /// <param name="fastener">The fastener's name.</param>
  /// <param name="carried">How many of the fastener the player carries.</param>
  /// <param name="companions">The further items the fit takes, each name with its count as in <see cref="Stacks"/>; null or empty leaves the clause out.</param>
  public static string FitLine(
    Func<string, object[], string> get,
    string made,
    int cost,
    string fastener,
    int carried,
    IReadOnlyList<(string Name, int Count)>? companions = null
  ) {
    var line = new StringBuilder(get(ExlibLang.HandassemblyHudFit, [made]));
    if (companions is { Count: > 0 })
      line.Append(" | ")
        .Append(get(ExlibLang.HandassemblyHudCompanions, [Stacks(get, companions)]));
    if (cost > 0)
      line.Append(" | ")
        .Append(get(ExlibLang.HandassemblyHudCost, [cost, fastener, carried]));
    return line.ToString();
  }

  /// <summary>Builds the line of an aimed fixed piece the wrench would take off, such as <c>Take off Pipe and 2 Bolt, 1 Cap</c>.</summary>
  /// <param name="get">Translates a lang key with its arguments; <c>Lang.Get</c> in the game.</param>
  /// <param name="piece">The aimed piece's name.</param>
  /// <param name="returns">What a survival take-off gives besides the piece, grouped by item as in <see cref="Stacks"/>; empty for none.</param>
  /// <param name="anyFree">Whether a joint of the piece was made in creative and is owed nothing.</param>
  /// <param name="creative">Whether the player is in creative, where a take-off returns nothing; this wins over the other arguments.</param>
  public static string TakeOffLine(
    Func<string, object[], string> get,
    string piece,
    IReadOnlyList<(string Name, int Count)> returns,
    bool anyFree,
    bool creative
  ) =>
    creative ? get(ExlibLang.HandassemblyHudTakeoffCreative, [piece])
    : returns.Count > 0
      ? get(ExlibLang.HandassemblyHudTakeoff, [piece, Stacks(get, returns)])
    : anyFree ? get(ExlibLang.HandassemblyHudTakeoffFree, [piece])
    : get(ExlibLang.HandassemblyHudTakeoffBare, [piece]);

  /// <summary>Joins the stacks into one phrase, such as <c>2 Bolt, 1 Cap</c>.</summary>
  /// <param name="get">Translates a lang key with its arguments; <c>Lang.Get</c> in the game.</param>
  /// <param name="stacks">Each item's name and how many of it, in the order shown.</param>
  public static string Stacks(
    Func<string, object[], string> get,
    IEnumerable<(string Name, int Count)> stacks
  ) =>
    string.Join(
      ", ",
      stacks.Select(s => get(ExlibLang.HandassemblyHudStack, [s.Count, s.Name]))
    );

  /// <summary>Height of one line of the HUD's text, in unscaled GUI px.</summary>
  internal const double LineHeight = 24;

  /// <summary>The HUD's text broken into whole lines, with the widest line's width and the height of all of them.</summary>
  /// <param name="Lines">The lines, in order; one empty line for empty text.</param>
  /// <param name="Width">The widest line's measured width, in the unit <c>measure</c> returns.</param>
  /// <param name="Height">The lines' total height, in unscaled GUI px.</param>
  internal readonly record struct HudLayout(
    IReadOnlyList<string> Lines,
    double Width,
    double Height
  );

  /// <summary>Breaks <paramref name="text"/> between words into lines no wider than <paramref name="maxWidth"/>; text that fits stays one line. A word wider than <paramref name="maxWidth"/> stands alone on its line, uncut.</summary>
  /// <param name="text">The line to show; words are separated by single spaces.</param>
  /// <param name="maxWidth">The widest a line may be, in the unit <c>measure</c> returns.</param>
  /// <param name="measure">The width of a string in the font the text is drawn in.</param>
  public static HudLayout Layout(
    string text,
    double maxWidth,
    Func<string, double> measure
  ) {
    var lines = new List<string>();
    if (measure(text) <= maxWidth)
      lines.Add(text);
    else {
      var current = new StringBuilder();
      foreach (string word in text.Split(' ')) {
        if (current.Length > 0 && measure(current + " " + word) > maxWidth) {
          lines.Add(current.ToString());
          current.Clear();
        }
        if (current.Length > 0)
          current.Append(' ');
        current.Append(word);
      }
      lines.Add(current.ToString());
    }
    return new HudLayout(lines, lines.Max(measure), lines.Count * LineHeight);
  }

  private static char Letter(BlockFacing face) =>
    char.ToUpperInvariant(face.Code[0]);
}
