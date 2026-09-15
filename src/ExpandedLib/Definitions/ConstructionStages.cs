using System;
using Newtonsoft.Json.Linq;

namespace ExpandedLib.Definitions;

/// <summary>Typed builder for the <c>ExRightClickConstructable</c> behavior's <c>stages</c> table.</summary>
public sealed class ConstructionStages {
  private readonly JArray _stages = new();
  private float? _brokenDropsRatio;
  private bool? _gatesProduction;

  /// <summary>Appends one build stage configured through <paramref name="configure"/>. Stage order
  /// is the build order.</summary>
  public ConstructionStages Stage(Action<ConstructionStage> configure) {
    var stage = new ConstructionStage();
    configure(stage);
    _stages.Add(stage.Build());
    return this;
  }

  /// <summary>Sets the top-level <c>brokenDropsRatio</c>.</summary>
  public ConstructionStages BrokenDropsRatio(float ratio) {
    _brokenDropsRatio = ratio;
    return this;
  }

  /// <summary>Sets the top-level <c>gatesProduction</c>. Default <c>true</c>.</summary>
  public ConstructionStages GatesProduction(bool gates) {
    _gatesProduction = gates;
    return this;
  }

  internal JObject Build() {
    var properties = new JObject { ["stages"] = _stages };
    if (_brokenDropsRatio.HasValue)
      properties["brokenDropsRatio"] = _brokenDropsRatio.Value;
    if (_gatesProduction.HasValue)
      properties["gatesProduction"] = _gatesProduction.Value;
    return properties;
  }
}

/// <summary>One construction stage: the shape elements it adds/removes and the materials it requires.</summary>
public sealed class ConstructionStage {
  private readonly JObject _stage = new();
  private JArray? _requireStacks;

  /// <summary>The shape elements this stage reveals. Sets <c>addElements</c>.</summary>
  public ConstructionStage AddElements(params string[] elements) {
    _stage["addElements"] = new JArray(elements);
    return this;
  }

  /// <summary>The shape elements this stage hides again. Sets <c>removeElements</c>.</summary>
  public ConstructionStage RemoveElements(params string[] elements) {
    _stage["removeElements"] = new JArray(elements);
    return this;
  }

  /// <summary>Requires <paramref name="quantity"/> of an ingredient to advance this stage.
  /// Accumulates across calls.</summary>
  public ConstructionStage Require(
    string code,
    int quantity,
    string? name = null,
    string type = "item",
    string? storeWildCard = null,
    string[]? allowedVariants = null
  ) {
    var ingredient = new JObject {
      ["type"] = type,
      ["code"] = code,
      ["quantity"] = quantity,
    };
    if (name != null)
      ingredient["name"] = name;
    if (allowedVariants != null)
      ingredient["allowedVariants"] = new JArray(allowedVariants);
    if (storeWildCard != null)
      ingredient["storeWildCard"] = storeWildCard;

    _requireStacks ??= new JArray();
    _requireStacks.Add(ingredient);
    _stage["requireStacks"] = _requireStacks;
    return this;
  }

  /// <summary>Requires iron or steel metal plate.</summary>
  public ConstructionStage RequireMetalPlate(string domain, int quantity) =>
    RequireMetal(domain, "metalplate-*", "metalplate", quantity);

  /// <summary>Requires iron or steel nails and strips.</summary>
  public ConstructionStage RequireMetalNails(string domain, int quantity) =>
    RequireMetal(domain, "metalnailsandstrips-*", "nailsandstrips", quantity);

  /// <summary>Requires rivets.</summary>
  public ConstructionStage RequireRivets(
    string domain,
    string rivetCode,
    int quantity
  ) => Require(rivetCode, quantity, $"{domain}:rcc-ingredient-rivet");

  /// <summary>Requires an iron or steel metal rod.</summary>
  public ConstructionStage RequireMetalRod(string domain, int quantity) =>
    RequireMetal(domain, "rod-*", "rod", quantity);

  // Shared iron/steel ingredient shape.
  private ConstructionStage RequireMetal(
    string domain,
    string code,
    string kind,
    int quantity
  ) =>
    Require(
      code,
      quantity,
      $"{domain}:rcc-ingredient-{kind}",
      storeWildCard: "metal",
      allowedVariants: ["iron", "steel"]
    );

  internal JObject Build() => _stage;
}
