using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using ExpandedLib.Helpers;
using Vintagestory.API.Common;

namespace ExpandedLib.Industry.Casting;

/// <summary>How much sand is rammed into a sand station.</summary>
public enum SandStationLevel {
  /// <summary>No sand.</summary>
  Empty,

  /// <summary>Struck half height: a closed pattern's drag at the parting. One ram clears it to <see cref="Full"/>.</summary>
  Half,

  /// <summary>Fully rammed; ready to take a pattern impression.</summary>
  Full,
}

/// <summary>What a right-click on a sand station resolves to, given what the player holds and the station's state.</summary>
public enum SandStationAction {
  /// <summary>Nothing to do; fall through to the default block interaction.</summary>
  None,

  /// <summary>Ramming a drag that holds a closed piece; refused.</summary>
  Bedded,

  /// <summary>Ram the held sand into the station (Empty or Half to Full).</summary>
  RamSand,

  /// <summary>Ram the held pattern into full sand, forming the impression.</summary>
  Imprint,

  /// <summary>Press the held pattern again into an impression it already made, bedding one more piece.</summary>
  Press,

  /// <summary>Collect the hardened casting (shake-out).</summary>
  Harvest,

  /// <summary>The metal is still liquid; the shake-out is refused.</summary>
  TooHot,
}

/// <summary>What pressing a pattern into a station that already holds an impression comes to.</summary>
public enum SandStationPressOutcome {
  /// <summary>One more piece is bedded.</summary>
  Bed,

  /// <summary>The station's count of pieces is already pressed.</summary>
  Full,

  /// <summary>The held pattern is not the type the impression was made with.</summary>
  OtherPattern,
}

/// <summary>What a shake-out gives: whole parts and units of scrap.</summary>
/// <param name="Parts">Pieces filled to their full units.</param>
/// <param name="ScrapUnits">Metal of the pieces left part filled, or of the whole cast on a misrun.</param>
public readonly record struct SandStationYield(int Parts, int ScrapUnits);

/// <summary>
/// A sand station's interaction and cast-outcome rules as pure functions, callable without a world.
/// </summary>
public static class SandStationLogic {
  /// <summary>Resolves a right-click into the one action it performs.</summary>
  /// <param name="holdingSand">The player holds the station's moulding sand (<see cref="CastingStationKind.Matches"/>).</param>
  /// <param name="holdingPattern">The player holds a mold pattern.</param>
  /// <param name="emptyHand">The player's active hand is empty.</param>
  /// <param name="sand">Current sand level.</param>
  /// <param name="hasImpression">A pattern has been rammed in.</param>
  /// <param name="hasMetal">The station currently holds metal, liquid or solidified.</param>
  /// <param name="isHardened">That metal has cooled past the hardened threshold.</param>
  /// <param name="closedImpression">The impression is a closed pattern's, struck to <see cref="SandStationLevel.Half"/>; sand is refused and a pattern presses again at Half.</param>
  public static SandStationAction Decide(
    bool holdingSand,
    bool holdingPattern,
    bool emptyHand,
    SandStationLevel sand,
    bool hasImpression,
    bool hasMetal,
    bool isHardened,
    bool closedImpression = false
  ) {
    if (hasMetal) {
      if (emptyHand)
        return isHardened
          ? SandStationAction.Harvest
          : SandStationAction.TooHot;
      return SandStationAction.None;
    }

    if (holdingSand && closedImpression)
      return SandStationAction.Bedded;

    if (holdingSand && sand != SandStationLevel.Full)
      return SandStationAction.RamSand;

    if (holdingPattern && sand == SandStationLevel.Full && !hasImpression)
      return SandStationAction.Imprint;

    if (
      holdingPattern
      && hasImpression
      && (
        sand == SandStationLevel.Full
        || (closedImpression && sand == SandStationLevel.Half)
      )
    )
      return SandStationAction.Press;

    return SandStationAction.None;
  }

  /// <summary>The sand level a station is at once its first pattern is pressed: a closed pattern strikes the drag to <see cref="SandStationLevel.Half"/>, the parting; an open one leaves it <see cref="SandStationLevel.Full"/>.</summary>
  /// <param name="sand">The sand level before the press.</param>
  /// <param name="closedPattern">The pressed pattern is closed.</param>
  public static SandStationLevel SandAfterImprint(
    SandStationLevel sand,
    bool closedPattern
  ) =>
    closedPattern && sand == SandStationLevel.Full
      ? SandStationLevel.Half
      : sand;

  /// <summary>Decides a press of the held pattern into an existing impression.</summary>
  /// <param name="impressedCode">Item code of the pattern that made the impression.</param>
  /// <param name="heldCode">Item code of the held pattern.</param>
  /// <param name="pressed">Pieces already bedded.</param>
  /// <param name="pieces">The station's count of pieces for the part.</param>
  public static SandStationPressOutcome Press(
    string impressedCode,
    string heldCode,
    int pressed,
    int pieces
  ) {
    if (impressedCode != heldCode)
      return SandStationPressOutcome.OtherPattern;
    return pressed >= pieces
      ? SandStationPressOutcome.Full
      : SandStationPressOutcome.Bed;
  }

  /// <summary>
  /// The batches a pour fills the pressed pieces in. Pieces within a batch share the metal evenly; a batch
  /// takes metal only once the batches before it are full.
  /// </summary>
  /// <param name="order">The filling's layout.</param>
  /// <param name="pieces">The station's count of pieces; a <see cref="FillOrder.Rows"/> filling has half of them in the front row.</param>
  /// <param name="pressed">Pieces bedded, 0 to <paramref name="pieces"/>; the first <paramref name="pressed"/> pieces in pressing order.</param>
  /// <returns>Zero-based piece indices per batch in fill order, empty batches left out. A chain is one piece a batch, a
  /// row pairs of pieces, and two rows the front pair then the pair behind it, outward from the drop.</returns>
  public static IReadOnlyList<int[]> FillBatches(
    FillOrder order,
    int pieces,
    int pressed
  ) {
    var batches = new List<int[]>();
    void Add(params int[] indices) {
      int[] kept = indices.Where(i => i < pressed).ToArray();
      if (kept.Length > 0)
        batches.Add(kept);
    }

    switch (order) {
      case FillOrder.Chain:
        for (int i = 0; i < pressed; i++)
          Add(i);
        break;
      case FillOrder.Pairs:
        for (int i = 0; i < pieces; i += 2)
          Add(i, i + 1);
        break;
      default:
        int front = pieces / 2;
        for (int i = 0; i < front; i += 2) {
          Add(i, i + 1);
          Add(front + i, front + i + 1);
        }
        break;
    }
    return batches;
  }

  /// <summary>Units of metal each pressed piece holds after a pour of <paramref name="amount"/>.</summary>
  /// <param name="amount">Units poured into the cavity.</param>
  /// <param name="unitsPerPiece">Units one piece holds.</param>
  /// <param name="batches">The fill order from <see cref="FillBatches"/>.</param>
  /// <returns>One entry per piece index in the batches; a short batch shares what is left evenly, the units
  /// that do not divide going to its first pieces.</returns>
  public static int[] PieceFills(
    int amount,
    int unitsPerPiece,
    IReadOnlyList<int[]> batches
  ) {
    var fills = new int[batches.Sum(b => b.Length)];
    int left = amount;
    foreach (int[] batch in batches) {
      int need = batch.Length * unitsPerPiece;
      if (left >= need) {
        foreach (int piece in batch)
          fills[piece] = unitsPerPiece;
        left -= need;
        continue;
      }
      for (int k = 0; k < batch.Length; k++)
        fills[batch[k]] =
          left / batch.Length + (k < left % batch.Length ? 1 : 0);
      left = 0;
    }
    return fills;
  }

  /// <summary>The yield of a pour: each full piece is a part and the rest of the metal comes back as scrap.</summary>
  /// <param name="misrun">The pour was below the minimum pour temperature; the whole cast is scrap.</param>
  /// <param name="amount">Units in the cavity.</param>
  /// <param name="unitsPerPiece">Units one piece holds.</param>
  /// <param name="batches">The fill order from <see cref="FillBatches"/>.</param>
  public static SandStationYield YieldOf(
    bool misrun,
    int amount,
    int unitsPerPiece,
    IReadOnlyList<int[]> batches
  ) {
    if (misrun)
      return new SandStationYield(0, amount);
    int parts = 0;
    int left = amount;
    foreach (int[] batch in batches) {
      int need = batch.Length * unitsPerPiece;
      if (left < need)
        break;
      parts += batch.Length;
      left -= need;
    }
    return new SandStationYield(parts, amount - parts * unitsPerPiece);
  }

  /// <summary>The sand level a station is at once its casting is shaken out: full again.</summary>
  public const SandStationLevel AfterShakeOut = SandStationLevel.Full;

  /// <summary>Whether a pour is a misrun: the metal came in below the minimum pour temperature.
  /// Temperatures in degrees Celsius.</summary>
  /// <param name="pourTemp">The temperature of the metal last drawn into the cavity; null counts as no misrun.</param>
  /// <param name="minPourTemp">The pattern's minimum pour temperature; 0 disables the check.</param>
  public static bool IsMisrun(float? pourTemp, float minPourTemp) =>
    minPourTemp > 0f && pourTemp is { } poured && poured < minPourTemp;

  /// <summary>Whether the station can draw metal from its feed.</summary>
  public static bool CanIntake(
    bool hasImpression,
    bool cavityFull,
    bool solidified
  ) => hasImpression && !cavityFull && !solidified;

  /// <summary>Whether metal stands in the runner box: the station holds more than the pressed pieces take, so the pour's head is filled.</summary>
  /// <param name="spec">The impressed pattern's spec; null when none.</param>
  /// <param name="pressed">Pieces bedded.</param>
  /// <param name="amount">Units of metal in the cavity.</param>
  public static bool HeadStands(MoldSpec? spec, int pressed, float amount) =>
    spec is { Share: > 0 } && amount > pressed * spec.UnitsPerPiece;

  /// <summary>The rammed-sand filling shape for the station's current state, or <c>null</c> for no sand.</summary>
  /// <param name="kind">The station kind whose sand meshes are drawn.</param>
  /// <param name="sand">The station's sand level.</param>
  /// <param name="hasImpression">Whether a pattern has been rammed in.</param>
  /// <param name="impressionShape">The pattern's own filling shape, or <c>null</c> when unresolved.</param>
  /// <param name="closedImpression">The impression is a closed pattern's; its struck drag shows the pattern's own filling at <see cref="SandStationLevel.Half"/>.</param>
  public static string? FillingShape(
    CastingStationKind kind,
    SandStationLevel sand,
    bool hasImpression,
    string? impressionShape,
    bool closedImpression = false
  ) =>
    sand switch {
      SandStationLevel.Empty => null,
      SandStationLevel.Half => closedImpression && hasImpression
        ? impressionShape ?? kind.SandHalf
        : kind.SandHalf,
      SandStationLevel.Full => hasImpression
        ? impressionShape ?? kind.SandBase
        : kind.SandBase,
      _ => null,
    };

  /// <summary>The mesh-cache key of a filling drawn with <paramref name="pressed"/> pieces bedded.</summary>
  public static string FillingMeshKey(string shapeRef, int pressed) =>
    $"{shapeRef}|{pressed}";

  private static readonly Regex PieceGroup = new(@"^Piece(\d+)$");

  /// <summary>The filling shape showing only the pressed pieces: of each top-level group <c>Piece&lt;k&gt;</c>, the <c>Bedded</c> child when <c>k</c> is at most <paramref name="pressed"/> and every other child (<c>Level</c> with its split parts) when not. Elements outside the groups stay; a shape without groups is returned as it is.</summary>
  /// <param name="shape">The loaded filling shape; not modified.</param>
  /// <param name="pressed">Pieces bedded into the impression so far.</param>
  public static Shape PrunedToPressed(Shape shape, int pressed) {
    var keep = new List<string>();
    bool grouped = false;
    foreach (ShapeElement el in shape.Elements ?? []) {
      string name = el.Name ?? "";
      Match m = PieceGroup.Match(name);
      if (!m.Success || el.Children is not { Length: > 0 } children) {
        keep.Add(name);
        continue;
      }
      grouped = true;
      bool bedded = int.Parse(m.Groups[1].Value) <= pressed;
      foreach (ShapeElement child in children)
        if ((child.Name == "Bedded") == bedded)
          keep.Add(name + "/" + child.Name);
    }
    return grouped ? ExShapeElements.Pruned(shape, keep) : shape;
  }
}
