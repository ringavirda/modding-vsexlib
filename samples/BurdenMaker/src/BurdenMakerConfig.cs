using System.Collections.Generic;
using ExpandedLib.Config;

namespace BurdenMaker;

/// <summary>The burdenmaker's tunables, generated into a typed <c>BurdenMakerValues</c> accessor.</summary>
[ExConfigRegister("burdenmaker.json", "burdenmaker", Manageable = true)]
public class BurdenMakerConfig : IExVersionedConfig {
  /// <summary>Mod version that last wrote this file. Managed by the config store - do not set by hand.</summary>
  public string? ConfigVersion { get; set; }

  /// <summary>Maximum crushed iron ore (units) the wide hopper holds; 1 to 100000.</summary>
  [ExConfigRange(1, 100_000)]
  public int BurdenmakerOreCapacity { get; set; } = 512;

  /// <summary>Maximum flux (units) the narrow hopper holds; 1 to 100000.</summary>
  [ExConfigRange(1, 100_000)]
  public int BurdenmakerFluxCapacity { get; set; } = 205;

  /// <summary>Maximum burden (units) the shared basin holds before it must be emptied; 1 to 100000.</summary>
  [ExConfigRange(1, 100_000)]
  public int BurdenmakerBunkerCapacity { get; set; } = 1152;

  /// <summary>Seconds a full gate-open batch takes to drain into the basin; 1 to 60.</summary>
  [ExConfigRange(1, 60)]
  public int BurdenmakerDrainSeconds { get; set; } = 8;

  /// <summary>Named burden grades, matched by flux band (fractions 0..1 of the total mix).</summary>
  public List<BurdenProfile> BurdenProfiles { get; set; } =
  [
    // Bands are inclusive on both ends; ordering is the tie-break, do not sort.
    new() { Key = "underfluxed", MaxFlux = 0.03f },
    new()
    {
      Key = "standard",
      MinFlux = 0.03f,
      MaxFlux = 0.08f,
    },
    new() { Key = "overfluxed", MinFlux = 0.08f },
  ];
}

/// <summary>One named burden grade: a lang-keyed label plus an inclusive flux band (0..1 of the
/// total mix). Pure data; the classifier lives in <see cref="Items.Burden"/>.</summary>
public class BurdenProfile {
  /// <summary>Grade key; the burdenmaker readout and the tooltip show <c>burdenmaker:burden-profile-{Key}</c>.</summary>
  public string Key { get; set; } = "";

  public float MinFlux { get; set; }
  public float MaxFlux { get; set; } = 1f;
}
