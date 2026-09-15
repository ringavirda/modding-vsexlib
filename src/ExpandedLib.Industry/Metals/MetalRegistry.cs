using System.Collections.Generic;
using ExpandedLib.Catalogues;
using ExpandedLib.Registries;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace ExpandedLib.Industry.Metals;

/// <summary>
/// Process-wide catalogue of <see cref="MetalDef"/>s, populated at <c>AssetsFinalize</c> from each
/// domain's <c>config/metals/*.json</c> plus an auto-derived baseline. Reader helpers fall back to
/// a code convention for an unregistered metal.
/// </summary>
public static class MetalRegistry {
  /// <summary>Recovery item code used by <see cref="FallbackOf"/> when a metal declares no
  /// <see cref="MetalDef.RecoveryFallback"/>; null means no fallback.</summary>
  public static AssetLocation? DefaultRecoveryFallback { get; set; }

  /// <summary>Code contributions to this registry, invoked by <see cref="MetalCatalogueLoader"/> after
  /// each load's overlay.</summary>
  public static CatalogueContributors Contributors { get; } = new();

  // Indexed by molten-item AssetLocation and by short Code.
  private static readonly ExKeyedRegistry<MetalDef> _byMoltenItem = new(d =>
    Normalize(d.MoltenItem)
  );
  private static readonly ExKeyedRegistry<MetalDef> _byCode = new(d => d.Code);

  private static readonly IReadOnlyList<string> DefaultMedia = new[]
  {
    "molten",
  };

  /// <summary>Registers (or replaces) a metal by both its molten-item code and its short code.</summary>
  public static void Register(MetalDef def) {
    _byMoltenItem.Register(def);
    _byCode.Register(def);
  }

  /// <summary>Drops every registered metal.</summary>
  public static void Clear() {
    _byMoltenItem.Clear();
    _byCode.Clear();
  }

  /// <summary>Every registered metal descriptor.</summary>
  public static IReadOnlyCollection<MetalDef> All => _byMoltenItem.Values;

  #region Lookup
  /// <summary>Looks up a metal by its molten-item <c>AssetLocation</c> (domain-normalised).</summary>
  public static bool TryGet(AssetLocation moltenItem, out MetalDef def) =>
    _byMoltenItem.TryGet(Normalize(moltenItem), out def);

  /// <summary>Looks up a metal by its molten-item code string (domain-normalised).</summary>
  public static bool TryGet(string moltenItemCode, out MetalDef def) =>
    _byMoltenItem.TryGet(Normalize(moltenItemCode), out def);

  /// <summary>Looks up a metal by a stack's collectible code.</summary>
  public static bool TryGet(ItemStack stack, out MetalDef def) =>
    TryGet(stack.Collectible.Code, out def);

  /// <summary>Resolves a short metal token ("iron", "steel", "slag") to its descriptor, falling back to
  /// a convention descriptor for an unregistered token.</summary>
  public static MetalDef ResolveByCode(string shortCode) =>
    _byCode.TryGet(shortCode, out var def)
      ? def
      : new MetalDef {
        Code = shortCode,
        MoltenItem = new AssetLocation("game", "ingot-" + shortCode).ToString(),
      };

  /// <summary>The molten-item <c>AssetLocation</c> for a short metal token (registered or convention).</summary>
  public static AssetLocation MoltenItemOf(string shortCode) =>
    new(ResolveByCode(shortCode).MoltenItem);
  #endregion

  #region Reader helpers (registered override else convention)
  /// <summary>Solid item chipped/broken out of molten metal. Convention: <c>ingot-X -> metalbit-X</c>
  /// (same domain); a non-ingot carrier drops as itself.</summary>
  public static AssetLocation SolidDropOf(AssetLocation moltenItem) =>
    TryGet(moltenItem, out var def) && def.SolidDrop != null
      ? new AssetLocation(def.SolidDrop)
      : ConventionSolidDrop(moltenItem);

  /// <summary>Molten units recovered per solid drop item (convention: 5).</summary>
  public static int UnitsPerBitOf(AssetLocation moltenItem) =>
    (TryGet(moltenItem, out var def) ? def.UnitsPerBit : null) ?? 5;

  /// <summary>Human-readable metal name. Convention: strip <c>ingot-</c> and capitalise
  /// ("game:ingot-iron" -> "Iron"); an empty code reads as the unknown-metal label.</summary>
  public static string DisplayName(string moltenItemCode) {
    if (moltenItemCode.Length == 0)
      return Lang.Get("exlib:metal-unknown");
    if (TryGet(moltenItemCode, out var def) && def.DisplayLangKey != null)
      return Lang.Get(def.DisplayLangKey);
    return ConventionDisplayName(moltenItemCode);
  }

  /// <summary>Recovery item code when the solid drop cannot resolve: the metal's own
  /// <see cref="MetalDef.RecoveryFallback"/>, else <see cref="DefaultRecoveryFallback"/>, else
  /// null.</summary>
  public static AssetLocation? FallbackOf(AssetLocation moltenItem) {
    string? code = TryGet(moltenItem, out var def)
      ? def.RecoveryFallback
      : null;
    return code != null ? new AssetLocation(code) : DefaultRecoveryFallback;
  }

  /// <summary>Network media this metal flows in (convention: <c>["molten"]</c>).</summary>
  public static IReadOnlyList<string> MediaOf(AssetLocation moltenItem) =>
    (TryGet(moltenItem, out var def) ? def.Media : null) ?? DefaultMedia;

  /// <summary>Fraction of the melting point above which this metal flows (convention: the global default).</summary>
  public static float LiquidThresholdOf(AssetLocation moltenItem) =>
    (TryGet(moltenItem, out var def) ? def.LiquidThreshold : null)
    ?? ExlibValues.MetalLiquidThreshold;

  /// <summary>Fraction of the melting point below which this metal is chisellable (convention: the global default).</summary>
  public static float HardenedThresholdOf(AssetLocation moltenItem) =>
    (TryGet(moltenItem, out var def) ? def.HardenedThreshold : null)
    ?? ExlibValues.MetalHardenedThreshold;

  /// <summary>Temperature ( deg C) below which this metal emits no glow (convention: the global default).</summary>
  public static float GlowMinTempOf(AssetLocation moltenItem) =>
    (TryGet(moltenItem, out var def) ? def.GlowMinTemp : null)
    ?? ExlibValues.MetalGlowMinTemp;

  /// <summary>Domain owning this metal's cast products (convention: none - the template's own domain).</summary>
  public static string? CastDomainOf(AssetLocation moltenItem) =>
    TryGet(moltenItem, out var def) ? def.CastDomain : null;

  /// <summary>Resolves a tool-mold drop for <paramref name="template"/> and <paramref name="moltenItem"/>,
  /// substituting <c>{metal}</c> and rehoming into <see cref="MetalDef.CastDomain"/> when set.</summary>
  public static AssetLocation CastProductOf(
    AssetLocation template,
    AssetLocation moltenItem
  ) {
    AssetLocation loc = template.Clone();
    loc.Path = loc.Path.Replace("{metal}", ShortMetalOf(moltenItem));
    string? domain = CastDomainOf(moltenItem);
    if (domain != null)
      loc.Domain = domain;
    return loc;
  }
  #endregion

  #region Conventions (the exact pre-registry behaviour)
  // Mirrors vanilla: BlockEntityToolMold.stackFromCode uses Collectible.LastCodePart().
  private static string ShortMetalOf(AssetLocation moltenItem) {
    string path = moltenItem.Path;
    int dash = path.LastIndexOf('-');
    return dash >= 0 ? path[(dash + 1)..] : path;
  }

  private static AssetLocation ConventionSolidDrop(AssetLocation moltenItem) =>
    moltenItem.Path.StartsWith("ingot-")
      ? new AssetLocation(moltenItem.Domain, "metalbit-" + moltenItem.Path[6..])
      : moltenItem;

  private static string ConventionDisplayName(string moltenItemCode) {
    string path = new AssetLocation(moltenItemCode).Path;
    string name = path.StartsWith("ingot-") ? path[6..] : path;
    return name.Length > 0 ? char.ToUpper(name[0]) + name[1..] : name;
  }

  // Domain-normalized key: "ingot-iron" and "game:ingot-iron" resolve alike.
  private static string Normalize(string code) =>
    new AssetLocation(code).ToString();

  private static string Normalize(AssetLocation loc) => loc.ToString();
  #endregion
}
