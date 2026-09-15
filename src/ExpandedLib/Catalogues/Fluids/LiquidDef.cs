using System.Collections.Generic;

namespace ExpandedLib.Catalogues;

/// <summary>
/// One pipe or canal medium descriptor: the data the network's compatibility, priority and
/// phase-change logic reads in place of hardcoded medium strings.
/// </summary>
public class LiquidDef {
  /// <summary>The medium code, equal to the network <c>MediumType</c> ("Air", "Steam", "Water"...).</summary>
  public string Code { get; set; } = "";

  /// <summary>Gas (mixable family) or Liquid (mixes only with the same code).</summary>
  public LiquidPhase Phase { get; set; } = LiquidPhase.Gas;

  /// <summary>Merge-dominance rank when two gas runs join.</summary>
  public int Priority { get; set; }

  /// <summary>Phase-change target read by a condenser/boiler; null = no condensation.</summary>
  public string? CondensesTo { get; set; }

  /// <summary>Condense only below this temperature (C); null = temperature-independent.</summary>
  public float? CondenseBelowC { get; set; }

  /// <summary>Volume multiplier applied on condensation; null = the consuming mod's own default.</summary>
  public float? CondenseVolumeFactor { get; set; }

  /// <summary>Gas-phase target a still or boiler boils this liquid into; null = does not boil to a
  /// carried medium.</summary>
  public string? VaporisesTo { get; set; }

  /// <summary>Boil only at or above this temperature (C); null = temperature-independent.</summary>
  public float? BoilPointC { get; set; }

  /// <summary>Volume multiplier applied on vaporisation; null = the consuming mod's own default.</summary>
  public float? VaporiseVolumeFactor { get; set; }
}

/// <summary>The <c>config/liquids.json</c> file shape: one wrapper object carrying the medium
/// entries.</summary>
public class LiquidCatalogue {
  /// <summary>The medium descriptors this file contributes.</summary>
  public List<LiquidDef>? Liquids { get; set; }
}
