using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Blocks;
using Newtonsoft.Json.Linq;

namespace ExpandedLib.Definitions;

/// <summary>Typed builder for the <c>ExRightClickConstructable</c> behavior's <c>stages</c> table.</summary>
public sealed class ConstructionStages {
  private readonly JArray _stages = new();
  private readonly HashSet<string> _stored = new(StringComparer.Ordinal);
  private float? _brokenDropsRatio;
  private bool? _gatesProduction;

  /// <summary>Appends one build stage configured through <paramref name="configure"/>. Stage order
  /// is the build order.</summary>
  /// <remarks>The stage's <see cref="ConstructionStage.RequireMetalPlate"/>,
  /// <see cref="ConstructionStage.RequireMetalRod"/> and
  /// <see cref="ConstructionStage.RequireMetalNails"/> take the metal an earlier paid stage stored,
  /// so the first paid stage asking for metal picks the construction's one metal.</remarks>
  public ConstructionStages Stage(Action<ConstructionStage> configure) {
    var stage = new ConstructionStage(_stages.Count == 0 ? [] : [.. _stored]);
    configure(stage);
    JObject built = stage.Build();
    if (_stages.Count > 0 && built["requireStacks"] is JArray required)
      foreach (JToken ingredient in required)
        if ((string?)ingredient["storeWildCard"] is { } key)
          _stored.Add(key);
    _stages.Add(built);
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

/// <summary>One construction stage: the shape elements it adds/removes, the materials it requires and
/// optionally the tool it is worked with.</summary>
public sealed class ConstructionStage {
  private readonly JObject _stage = new();
  private readonly IReadOnlyCollection<string> _stored;
  private JArray? _requireStacks;

  /// <summary>A stage that follows no paid stage storing a key.</summary>
  public ConstructionStage()
    : this([]) { }

  // stored: the storeWildCard keys earlier paid stages of the construction store.
  internal ConstructionStage(IReadOnlyCollection<string> stored) =>
    _stored = stored;

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

  /// <summary>Makes this stage a tool stage: the player holds <paramref name="tool"/> in the main hand
  /// and right-click, and the stage is built at the last of <paramref name="hits"/> swings, which take
  /// the materials; each step wears its tool at its last swing. The first call sets <c>tool</c>,
  /// <c>hits</c>, <c>animation</c> and <c>sound</c>; each later call appends a step to <c>steps</c>,
  /// paid after the earlier ones, so the stage is built at the last swing of the last call.</summary>
  /// <param name="tool">Item code the main-hand stack must match, wildcards allowed.</param>
  /// <param name="hits">Finished swings the stage takes, 1 or more.</param>
  /// <param name="animation">Entity animation code played at each swing; null for the tool's default.</param>
  /// <param name="sound">Asset path of the sound played at each strike; null for the tool's default.</param>
  /// <exception cref="ArgumentOutOfRangeException"><paramref name="hits"/> is below 1.</exception>
  public ConstructionStage Tool(
    string tool,
    int hits = ExConstructionStage.DefaultHits,
    string? animation = null,
    string? sound = null
  ) {
    if (hits < 1)
      throw new ArgumentOutOfRangeException(
        nameof(hits),
        hits,
        "A tool stage takes at least one hit."
      );
    var step = new JObject { ["tool"] = tool, ["hits"] = hits };
    if (animation != null)
      step["animation"] = animation;
    if (sound != null)
      step["sound"] = sound;
    if (_stage["tool"] == null) {
      foreach (JProperty property in step.Properties())
        _stage[property.Name] = property.Value;
    } else {
      if (_stage["steps"] is not JArray steps)
        _stage["steps"] = steps = new JArray();
      steps.Add(step);
    }
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

  /// <summary>Requires iron or steel metal plate: of the metal an earlier paid stage stored, or
  /// either, storing the one paid as <c>metal</c>.</summary>
  public ConstructionStage RequireMetalPlate(string domain, int quantity) =>
    RequireMetal(domain, "metalplate-*", "metalplate", quantity);

  /// <summary>Requires iron or steel nails and strips: of the metal an earlier paid stage stored,
  /// or either, storing the one paid as <c>metal</c>.</summary>
  public ConstructionStage RequireMetalNails(string domain, int quantity) =>
    RequireMetal(domain, "metalnailsandstrips-*", "nailsandstrips", quantity);

  /// <summary>Requires rivets.</summary>
  public ConstructionStage RequireRivets(
    string domain,
    string rivetCode,
    int quantity
  ) => Require(rivetCode, quantity, $"{domain}:rcc-ingredient-rivet");

  /// <summary>Requires an iron or steel metal rod: of the metal an earlier paid stage stored, or
  /// either, storing the one paid as <c>metal</c>.</summary>
  public ConstructionStage RequireMetalRod(string domain, int quantity) =>
    RequireMetal(domain, "rod-*", "rod", quantity);

  // Vanilla keeps one value per key and refunds every storing ingredient at the last one stored.
  private ConstructionStage RequireMetal(
    string domain,
    string code,
    string kind,
    int quantity
  ) =>
    _stored.Contains(Metal)
      ? Require(
        code.Replace("*", "{" + Metal + "}"),
        quantity,
        $"{domain}:rcc-ingredient-{kind}"
      )
      : Require(
        code,
        quantity,
        $"{domain}:rcc-ingredient-{kind}",
        storeWildCard: Metal,
        allowedVariants: ["iron", "steel"]
      );

  private const string Metal = "metal";

  internal JObject Build() => _stage;
}
