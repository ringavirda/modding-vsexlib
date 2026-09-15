using System.Collections.Generic;
using Vintagestory.API.Common;

namespace ExpandedLib.Registries;

/// <summary>Process-wide registry of <see cref="RecipeProfile"/>s keyed by mod code, for mods that
/// expose recipe-cost levels. Owns the shared apply pipeline.</summary>
public static class ExRecipeProfiles {
  private static readonly ExKeyedRegistry<RecipeProfile> _profiles = new(p =>
    p.Code
  );

  /// <summary>Registers (or replaces) a mod's profile.</summary>
  public static void Register(RecipeProfile profile) =>
    _profiles.Register(profile);

  /// <summary>Looks up a registered profile by mod code (case-insensitive).</summary>
  public static bool TryGet(string code, out RecipeProfile profile) =>
    _profiles.TryGet(code, out profile);

  /// <summary>Removes a mod's profile, if registered.</summary>
  internal static void Unregister(string code) => _profiles.Remove(code);

  /// <summary>The registered mod codes, for listing in the command.</summary>
  public static IReadOnlyCollection<string> Codes => _profiles.Codes;

  /// <summary>Runs the apply pipeline for every registered profile.</summary>
  public static void ApplyAll(ICoreAPI api) {
    foreach (var profile in _profiles.Values)
      Apply(api, profile);
  }

  /// <summary>Runs the pipeline for one profile: repair the catalogue against the mod's defaults,
  /// fill the derived levels, persist if changed, then apply the selected level.</summary>
  public static void Apply(ICoreAPI api, RecipeProfile profile) {
    var live = profile.Catalogue();

    bool changed = ExRecipeCosts.Reconcile(live, profile.Defaults());
    changed |= ExRecipeCosts.EnsureNormalExtracted(api, live);
    foreach (var (level, factor) in profile.DerivedLevels)
      changed |= ExRecipeCosts.EnsureScaledLevel(live, level, factor);

    // The catalogue is server-authoritative; the client never writes it.
    if (changed && api.Side == EnumAppSide.Server)
      profile.SaveCatalogue();

    ExRecipeCosts.Apply(api, live, profile.GetLevel());
  }
}
