using System.Collections.Generic;
using ExpandedLib.Config;

namespace BurdenMaker;

/// <summary>
/// The burdenmaker's tunables, generated into a typed <c>BurdenMakerValues</c> accessor by
/// <c>ExConfigGenerator</c>. Loaded from and written to the <c>burdenmaker</c> section of
/// <c>ModConfig/burdenmaker.json</c>.
/// </summary>
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

  /// <summary>
  /// Named burden grades, matched by flux band; fractions are 0..1 of the total mix. The classifier
  /// returns the first profile whose band contains the mix's flux fraction, so the list is scanned in
  /// order and a boundary belongs to the earlier band. The shipped bands tile 0..1, so "off-spec" is
  /// unreachable unless a player edits a hole into them.
  /// </summary>
  public List<BurdenProfile> BurdenProfiles { get; set; } =
  [
    // Bands are inclusive on both ends and scanned in order, so a boundary belongs to the earlier
    // band: 0.03 reads underfluxed, 0.08 reads standard. Ordering is the tie-break - do not sort.
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

/// <summary>
/// One named burden grade: a lang-keyed label (<c>burdenmaker:burden-profile-{Key}</c>) plus an
/// inclusive flux band (fraction 0..1 of the total mix). Pure data; the classifier lives in
/// <see cref="Items.Burden"/>. There are no iron or fuel bounds: burden is ore and flux only, so
/// <c>IronFrac = 1 - FluxFrac</c>.
/// </summary>
public class BurdenProfile {
  /// <summary>Grade key; the burdenmaker readout and the tooltip show <c>burdenmaker:burden-profile-{Key}</c>.</summary>
  public string Key { get; set; } = "";

  public float MinFlux { get; set; }
  public float MaxFlux { get; set; } = 1f;
}
