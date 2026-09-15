using System.Collections.Generic;
using ExpandedLib.Definitions;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace ExpandedLib.Catalogues;

/// <summary>
/// The cutting tooling a machine is fitted with: a consumable carrying a hardness tier and nothing
/// else. Recognised by carrying a tier that parses, never by its code.
/// </summary>
public static class MachineTool {
  /// <summary>The attribute key a tool carries its tier under.</summary>
  public const string AttributeKey = "machinetool";

  /// <summary>The schema this parser writes and reads up to.</summary>
  public const int CurrentSchema = SpecSchema.First;

  /// <summary>Parses and validates a tool's <c>machinetool</c> attribute. Returns false with a
  /// human-readable <paramref name="error"/> on any malformed field.</summary>
  public static bool TryParse(JsonObject? node, out int tier, out string? error) {
    tier = 0;
    error = null;

    if (node is not { Exists: true }) {
      error = $"missing '{AttributeKey}' attribute";
      return false;
    }
    if (!SpecSchema.TryRead(node, CurrentSchema, out _, out error))
      return false;

    tier = node["tier"].AsInt(-1);
    if (tier < 0) {
      error =
        "missing or negative 'tier' (a tool with no hardness cannot be checked against a job's floor)";
      return false;
    }
    return true;
  }

  /// <summary>Whether <paramref name="stack"/> is machine tooling, i.e. carries a tier that parses.</summary>
  public static bool IsTool(ItemStack? stack) =>
    TryParse(stack?.Collectible?.Attributes?[AttributeKey], out _, out _);

  /// <summary>The hardness tier of the tool fitted as <paramref name="stack"/>, or <c>-1</c> when
  /// nothing is fitted or what is fitted is not tooling.</summary>
  public static int TierOf(ItemStack? stack) =>
    TryParse(stack?.Collectible?.Attributes?[AttributeKey], out int tier, out _)
      ? tier
      : -1;

  /// <summary>One tool's tier, as the nested attribute a variant carries.</summary>
  public static object Tier(int tier) =>
    new { machinetool = new { schema = CurrentSchema, tier } };

  /// <summary>Builds a mod's whole tooling itemtype from its own tier table.</summary>
  /// <param name="domain">The mod's domain; its tools land there.</param>
  /// <param name="code">The itemtype code, e.g. <c>shearblade</c>.</param>
  /// <param name="tiers">Variant name to its tier.</param>
  /// <param name="shape">Shape every variant renders as.</param>
  /// <param name="variantGroup">The group the variants belong to. Defaults to <c>type</c>.</param>
  /// <param name="assetName">The itemtype's asset path under <c>itemtypes/</c>, sub-folders allowed;
  /// the code itself when null.</param>
  public static ExItemDef Itemtype(
    string domain,
    string code,
    IReadOnlyDictionary<string, int> tiers,
    string shape = "game:item/ingot",
    string variantGroup = "type",
    string? assetName = null
  ) {
    var byType = new Dictionary<string, object>();
    foreach ((string type, int tier) in tiers)
      byType["*-" + type] = Tier(tier);

    return ExItemDef
      .Create(domain, code, assetName ?? code)
      .Shape(shape)
      .VariantGroup(variantGroup, [.. tiers.Keys])
      // Each fitted tool wears independently.
      .MaxStackSize(1)
      .RootKey("attributesByType", byType)
      .CreativeCommon("*");
  }
}
