using System.Collections.Generic;
using ExpandedLib.Definitions;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace ExpandedLib.Catalogues;

/// <summary>A machine tool as its <c>machinetool</c> attribute declares it.</summary>
/// <param name="Tier">Hardness tier, 0 or more.</param>
/// <param name="Kind">The tool kind (<c>cutter</c>, <c>bit</c>, any string a machine kind's step names), or null for a
/// tool that fits no machining slot.</param>
/// <param name="SpentCode">Code of the item a worn-out tool leaves, or null when it leaves nothing.</param>
/// <param name="SpentCount">How many of <paramref name="SpentCode"/> it leaves. At least 1 when a code is named.</param>
public sealed record MachineToolSpec(
  int Tier,
  string? Kind,
  string? SpentCode,
  int SpentCount
);

/// <summary>
/// The cutting tooling a machine is fitted with: a consumable carrying a hardness tier and, optionally, the
/// kind of tool it is and what it leaves when worn out. Recognised by carrying a tier that parses, never by
/// its code.
/// </summary>
public static class MachineTool {
  /// <summary>The attribute key a tool carries its tier under.</summary>
  public const string AttributeKey = "machinetool";

  /// <summary>The schema a tool without a kind or a spent drop writes and every parser reads.</summary>
  public const int CurrentSchema = SpecSchema.First;

  /// <summary>The schema a tool with a kind or a spent drop writes; this parser reads up to it.</summary>
  public const int KindedSchema = 2;

  /// <summary>Parses and validates a tool's <c>machinetool</c> attribute. Returns false with a
  /// human-readable <paramref name="error"/> on any malformed field.</summary>
  public static bool TryParse(JsonObject? node, out int tier, out string? error) {
    bool ok = TryParse(node, out MachineToolSpec spec, out error);
    tier = spec.Tier;
    return ok;
  }

  /// <summary>Parses and validates a tool's <c>machinetool</c> attribute with its kind and spent drop. A
  /// schema 1 attribute reads kind and spent as null. Returns false with a human-readable
  /// <paramref name="error"/> on any malformed field.</summary>
  public static bool TryParse(
    JsonObject? node,
    out MachineToolSpec spec,
    out string? error
  ) {
    spec = new MachineToolSpec(0, null, null, 0);
    error = null;

    if (node is not { Exists: true }) {
      error = $"missing '{AttributeKey}' attribute";
      return false;
    }
    if (!SpecSchema.TryRead(node, KindedSchema, out int schema, out error))
      return false;

    int tier = node["tier"].AsInt(-1);
    if (tier < 0) {
      error =
        "missing or negative 'tier' (a tool with no hardness cannot be checked against a job's floor)";
      return false;
    }
    if (schema < KindedSchema) {
      spec = new MachineToolSpec(tier, null, null, 0);
      return true;
    }

    string? kind = node["kind"].AsString();
    if (node["kind"].Exists && string.IsNullOrWhiteSpace(kind)) {
      error = "'kind' is blank";
      return false;
    }
    string? spentCode = null;
    int spentCount = 0;
    if (node["spent"].Exists) {
      spentCode = node["spent"]["code"].AsString();
      spentCount = node["spent"]["count"].AsInt(1);
      if (string.IsNullOrWhiteSpace(spentCode) || spentCount < 1) {
        error = "'spent' needs a 'code' and a 'count' of at least 1";
        return false;
      }
    }
    spec = new MachineToolSpec(tier, kind, spentCode, spentCount);
    return true;
  }

  /// <summary>Whether <paramref name="stack"/> is machine tooling, i.e. carries a tier that parses.</summary>
  public static bool IsTool(ItemStack? stack) =>
    TryParse(stack?.Collectible?.Attributes?[AttributeKey], out int _, out _);

  /// <summary>The hardness tier of the tool fitted as <paramref name="stack"/>, or <c>-1</c> when
  /// nothing is fitted or what is fitted is not tooling.</summary>
  public static int TierOf(ItemStack? stack) =>
    TryParse(stack?.Collectible?.Attributes?[AttributeKey], out int tier, out _)
      ? tier
      : -1;

  /// <summary>The tool kind of <paramref name="stack"/>, or null when nothing is fitted, what is fitted is not
  /// tooling, or the tool declares no kind.</summary>
  public static string? KindOf(ItemStack? stack) =>
    TryParse(
      stack?.Collectible?.Attributes?[AttributeKey],
      out MachineToolSpec spec,
      out _
    )
      ? spec.Kind
      : null;

  /// <summary>One tool's tier, as the nested attribute a variant carries.</summary>
  public static object Tier(int tier) =>
    new { machinetool = new { schema = CurrentSchema, tier } };

  /// <summary>One tool's tier, kind and optional spent drop, as the nested attribute a variant carries.</summary>
  /// <param name="tier">Hardness tier, 0 or more.</param>
  /// <param name="kind">The tool kind a machine kind's step names.</param>
  /// <param name="spentCode">Code of the item the worn-out tool leaves, or null for none.</param>
  /// <param name="spentCount">How many of it. At least 1.</param>
  public static object Tier(
    int tier,
    string kind,
    string? spentCode = null,
    int spentCount = 1
  ) =>
    spentCode == null
      ? new {
        machinetool = new {
          schema = KindedSchema,
          tier,
          kind,
        },
      }
      : new {
        machinetool = new {
          schema = KindedSchema,
          tier,
          kind,
          spent = new { code = spentCode, count = spentCount },
        },
      };

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
  ) =>
    Build(
      domain,
      code,
      tiers,
      shape,
      variantGroup,
      assetName,
      tier => Tier(tier)
    );

  /// <summary>Builds a mod's whole tooling itemtype of one tool kind from its own tier table.</summary>
  /// <param name="domain">The mod's domain; its tools land there.</param>
  /// <param name="code">The itemtype code, e.g. <c>cutter</c>.</param>
  /// <param name="kind">The tool kind every variant carries.</param>
  /// <param name="tiers">Variant name to its tier.</param>
  /// <param name="shape">Shape every variant renders as.</param>
  /// <param name="variantGroup">The group the variants belong to, e.g. <c>type</c>.</param>
  /// <param name="assetName">The itemtype's asset path under <c>itemtypes/</c>, sub-folders allowed;
  /// the code itself when null.</param>
  /// <param name="spentCode">Code of the item a worn-out tool leaves, or null for none.</param>
  /// <param name="spentCount">How many of it. At least 1.</param>
  public static ExItemDef Itemtype(
    string domain,
    string code,
    string kind,
    IReadOnlyDictionary<string, int> tiers,
    string shape,
    string variantGroup,
    string? assetName,
    string? spentCode,
    int spentCount
  ) =>
    Build(
      domain,
      code,
      tiers,
      shape,
      variantGroup,
      assetName,
      tier => Tier(tier, kind, spentCode, spentCount)
    );

  private static ExItemDef Build(
    string domain,
    string code,
    IReadOnlyDictionary<string, int> tiers,
    string shape,
    string variantGroup,
    string? assetName,
    System.Func<int, object> attribute
  ) {
    var byType = new Dictionary<string, object>();
    foreach ((string type, int tier) in tiers)
      byType["*-" + type] = attribute(tier);

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
