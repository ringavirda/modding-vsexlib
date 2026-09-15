using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Registries;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;

namespace ExpandedLib.Definitions;

/// <summary>Code-first item definition, the item-side sibling of <see cref="ExBlockDef"/>. Builds
/// the <see cref="JObject"/> an <c>itemtypes/</c> asset consumes.</summary>
public sealed class ExItemDef : IExDef {
  private readonly string _domain;
  private readonly string _code;
  private readonly string _assetName;
  private readonly JObject _root = new();

  private ExItemDef(string domain, string code, string assetName) {
    _domain = domain;
    _code = code;
    _assetName = assetName;
    _root["code"] = code;
  }

  /// <summary>Starts an item definition for <paramref name="code"/> in <paramref name="domain"/> (the mod
  /// id). The synthetic asset is placed at <c>itemtypes/{code}.json</c>.</summary>
  public static ExItemDef Create(string domain, string code) =>
    new(domain, code, code);

  /// <summary>Starts an item definition whose asset path differs from its <paramref name="code"/>.
  /// <paramref name="assetName"/> may include sub-folders.</summary>
  public static ExItemDef Create(
    string domain,
    string code,
    string assetName
  ) => new(domain, code, assetName);

  /// <summary>The mod id / asset domain this item belongs to.</summary>
  public string Domain => _domain;

  /// <summary>The item's <c>code</c> field.</summary>
  public string Code => _code;

  /// <summary>The synthetic asset location the loader keys on:
  /// <c>{domain}:itemtypes/{assetName}.json</c>.</summary>
  public AssetLocation Location =>
    new(_domain, "itemtypes/" + _assetName + ".json");

  #region Class binding (type-safe)

  /// <summary>Sets <c>class</c> to <typeparamref name="T"/>'s registered key, taken from the same
  /// <c>{modid}.{ClassName}</c> source the registry uses.</summary>
  public ExItemDef Class<T>()
    where T : Item => Set("class", EntityRegistry.KeyFor(_domain, typeof(T)));

  /// <summary>Sets <c>class</c> to an explicit registered key (for a vanilla class by name).</summary>
  public ExItemDef Class(string registeredKey) => Set("class", registeredKey);

  #endregion

  #region Scalars

  /// <summary>Sets <c>maxstacksize</c>.</summary>
  public ExItemDef MaxStackSize(int size) => Set("maxstacksize", size);

  /// <summary>Sets <c>materialDensity</c> (kg/m3; drives float/sink and shove behaviour).</summary>
  public ExItemDef MaterialDensity(int density) =>
    Set("materialDensity", density);

  /// <summary>Sets <c>storageFlags</c> (the inventory slots the item may be stored in).</summary>
  public ExItemDef StorageFlags(int flags) => Set("storageFlags", flags);

  /// <summary>Sets <c>heldTpIdleAnimation</c>, the third-person idle animation played while the item is held.</summary>
  public ExItemDef HeldTpIdleAnimation(string animation) =>
    Set("heldTpIdleAnimation", animation);

  /// <summary>Sets <c>heldRightReadyAnimation</c>, the first-person ready pose when the held item is raised.</summary>
  public ExItemDef HeldRightReadyAnimation(string animation) =>
    Set("heldRightReadyAnimation", animation);

  /// <summary>Sets <c>heldTpUseAnimation</c>, the third-person animation played when the held item is used.</summary>
  public ExItemDef HeldTpUseAnimation(string animation) =>
    Set("heldTpUseAnimation", animation);

  #endregion

  #region Shape / textures (references to art files)

  /// <summary>Sets the <c>shape</c> base reference (<c>{ "base": "domain:path" }</c>).</summary>
  public ExItemDef Shape(string baseShape) {
    Nested("shape")["base"] = baseShape;
    return this;
  }

  /// <summary>Sets the shape's <c>selectiveElements</c>. Matching is the engine's per-segment
  /// prefix rule.</summary>
  public ExItemDef ShapeSelectiveElements(params string[] elements) {
    Nested("shape")["selectiveElements"] = new JArray(elements);
    return this;
  }

  /// <summary>Adds a texture mapping <paramref name="key"/> -&gt; <c>{ "base": "domain:path" }</c>
  /// under <c>textures</c> (accumulates across calls).</summary>
  public ExItemDef Texture(
    string key,
    string baseTexture,
    params string[] overlays
  ) {
    var texture = new JObject { ["base"] = baseTexture };
    if (overlays.Length > 0)
      texture["overlays"] = new JArray(overlays);
    Nested("textures")[key] = texture;
    return this;
  }

  /// <summary>Shorthand for <c>Texture("all", baseTexture)</c>.</summary>
  public ExItemDef TextureAll(string baseTexture) =>
    Texture("all", baseTexture);

  /// <summary>Sets a texture mapping <paramref name="key"/> from a fully-formed POCO/anonymous
  /// object/token.</summary>
  public ExItemDef Texture(string key, object texture) {
    Nested("textures")[key] = texture as JToken ?? JToken.FromObject(texture);
    return this;
  }

  #endregion

  #region Variant groups / creative tabs

  /// <summary>Appends a <c>variantgroups</c> entry with explicit <paramref name="states"/>. Order is
  /// preserved; variant expansion is left to the vanilla loader.</summary>
  public ExItemDef VariantGroup(string code, params string[] states) {
    NestedArray("variantgroups")
      .Add(new JObject { ["code"] = code, ["states"] = new JArray(states) });
    return this;
  }

  /// <summary>Appends a <c>variantgroups</c> entry sourced from a worldproperty (<c>loadFromProperties</c>).</summary>
  public ExItemDef VariantGroupFromProperties(
    string code,
    string propertiesPath
  ) {
    NestedArray("variantgroups")
      .Add(
        new JObject { ["code"] = code, ["loadFromProperties"] = propertiesPath }
      );
    return this;
  }

  /// <summary>Appends a <c>variantgroups</c> entry sourced from a worldproperty with no
  /// <c>code</c>.</summary>
  public ExItemDef VariantGroupFromProperties(string propertiesPath) {
    NestedArray("variantgroups")
      .Add(new JObject { ["loadFromProperties"] = propertiesPath });
    return this;
  }

  /// <summary>Sets <c>skipVariants</c> - variant-code wildcards the loader must not expand.</summary>
  public ExItemDef SkipVariants(params string[] wildcards) =>
    Set("skipVariants", new JArray(wildcards));

  /// <summary>Adds a <c>creativeinventory.{tab}</c> selector list (accumulates across calls).</summary>
  public ExItemDef CreativeTab(string tab, params string[] selectors) {
    Nested("creativeinventory")[tab] = new JArray(selectors);
    return this;
  }

  /// <summary>Adds the item to both the <c>general</c> tab and this def's own mod tab with the
  /// same <paramref name="selectors"/>.</summary>
  public ExItemDef CreativeCommon(params string[] selectors) =>
    CreativeTab("general", selectors).CreativeTab(_domain, selectors);

  #endregion

  #region Shape/texture *ByType maps

  /// <summary>Maps a variant wildcard to a base shape reference, with optional axis rotations,
  /// under <c>shapebytype</c>.</summary>
  public ExItemDef ShapeByType(
    string wildcard,
    string baseShape,
    int? rotateX = null,
    int? rotateY = null,
    int? rotateZ = null
  ) {
    var shape = new JObject { ["base"] = baseShape };
    if (rotateX.HasValue)
      shape["rotateX"] = rotateX.Value;
    if (rotateY.HasValue)
      shape["rotateY"] = rotateY.Value;
    if (rotateZ.HasValue)
      shape["rotateZ"] = rotateZ.Value;
    Nested("shapebytype")[wildcard] = shape;
    return this;
  }

  /// <summary>Maps a variant wildcard to a texture key -&gt; base reference under
  /// <c>texturesByType</c> (accumulates keys per wildcard).</summary>
  public ExItemDef TextureByType(
    string wildcard,
    string textureKey,
    string baseTexture,
    params string[] overlays
  ) {
    var byType = Nested("texturesByType");
    if (byType[wildcard] is not JObject entry) {
      entry = new JObject();
      byType[wildcard] = entry;
    }
    var texture = new JObject { ["base"] = baseTexture };
    if (overlays.Length > 0)
      texture["overlays"] = new JArray(overlays);
    entry[textureKey] = texture;
    return this;
  }

  #endregion

  #region Behaviors

  /// <summary>Appends a collectible behavior by its registered name (a vanilla behavior, e.g.
  /// <c>"GroundStorable"</c>).</summary>
  public ExItemDef Behavior(string name) {
    NestedArray("behaviors").Add(new JObject { ["name"] = name });
    return this;
  }

  /// <summary>Appends a collectible behavior carrying a <c>properties</c> blob.</summary>
  public ExItemDef Behavior(string name, object properties) {
    NestedArray("behaviors")
      .Add(
        new JObject {
          ["name"] = name,
          ["properties"] =
            properties as JToken ?? JToken.FromObject(properties),
        }
      );
    return this;
  }

  /// <summary>Appends a collectible behavior by type, resolved to <typeparamref name="T"/>'s
  /// registered <c>{modid}.{ClassName}</c> key.</summary>
  public ExItemDef Behavior<T>()
    where T : CollectibleBehavior =>
    Behavior(EntityRegistry.KeyFor(_domain, typeof(T)));

  #endregion

  #region Model transforms

  /// <summary>Sets the <c>guiTransform</c> from a POCO/anonymous object/token.</summary>
  public ExItemDef GuiTransform(object transform) =>
    RootKey("guiTransform", transform);

  /// <summary>Sets the <c>guiTransform</c> from translation, rotation, origin and uniform
  /// scale.</summary>
  public ExItemDef GuiTransform(
    double tx,
    double ty,
    double tz,
    double rx,
    double ry,
    double rz,
    double ox,
    double oy,
    double oz,
    double scale
  ) => GuiTransform(Transform(tx, ty, tz, rx, ry, rz, ox, oy, oz, scale));

  /// <summary>Sets the <c>fpHandTransform</c> from a POCO/anonymous object/token. Deprecated;
  /// still read by the loader.</summary>
  public ExItemDef FpHandTransform(object transform) =>
    RootKey("fpHandTransform", transform);

  /// <summary>Sets the <c>fpHandTransform</c> from translation, rotation, origin and uniform scale.</summary>
  public ExItemDef FpHandTransform(
    double tx,
    double ty,
    double tz,
    double rx,
    double ry,
    double rz,
    double ox,
    double oy,
    double oz,
    double scale
  ) => FpHandTransform(Transform(tx, ty, tz, rx, ry, rz, ox, oy, oz, scale));

  /// <summary>Sets the <c>tpHandTransform</c> (held in third person) from a POCO/anonymous object/token.</summary>
  public ExItemDef TpHandTransform(object transform) =>
    RootKey("tpHandTransform", transform);

  /// <summary>Sets the <c>tpHandTransform</c> from translation, rotation, origin and uniform scale.</summary>
  public ExItemDef TpHandTransform(
    double tx,
    double ty,
    double tz,
    double rx,
    double ry,
    double rz,
    double ox,
    double oy,
    double oz,
    double scale
  ) => TpHandTransform(Transform(tx, ty, tz, rx, ry, rz, ox, oy, oz, scale));

  /// <summary>Sets the <c>groundTransform</c> (dropped on the ground) from a POCO/anonymous object/token.</summary>
  public ExItemDef GroundTransform(object transform) =>
    RootKey("groundTransform", transform);

  /// <summary>Sets the <c>groundTransform</c> from translation, rotation, origin and uniform scale.</summary>
  public ExItemDef GroundTransform(
    double tx,
    double ty,
    double tz,
    double rx,
    double ry,
    double rz,
    double ox,
    double oy,
    double oz,
    double scale
  ) => GroundTransform(Transform(tx, ty, tz, rx, ry, rz, ox, oy, oz, scale));

  private static JObject Transform(
    double tx,
    double ty,
    double tz,
    double rx,
    double ry,
    double rz,
    double ox,
    double oy,
    double oz,
    double scale
  ) =>
    new() {
      ["translation"] = new JObject {
        ["x"] = tx,
        ["y"] = ty,
        ["z"] = tz,
      },
      ["rotation"] = new JObject {
        ["x"] = rx,
        ["y"] = ry,
        ["z"] = rz,
      },
      ["origin"] = new JObject {
        ["x"] = ox,
        ["y"] = oy,
        ["z"] = oz,
      },
      ["scale"] = scale,
    };

  #endregion

  #region Recipes / attributes + escape hatch

  /// <summary>Sets the top-level <c>combustibleProps</c> from a POCO/anonymous object.</summary>
  public ExItemDef CombustibleProps(object props) =>
    Set("combustibleProps", props as JToken ?? JToken.FromObject(props));

  /// <summary>Sets the top-level <c>grindingProps</c> from a POCO/anonymous object.</summary>
  public ExItemDef GrindingProps(object props) =>
    Set("grindingProps", props as JToken ?? JToken.FromObject(props));

  /// <summary>Sets an arbitrary <c>attributes.{key}</c> entry from a POCO/anonymous
  /// object/token/collection.</summary>
  public ExItemDef Attribute(string key, object value) {
    Nested("attributes")[key] = value as JToken ?? JToken.FromObject(value);
    return this;
  }

  /// <summary>Merges every property of a POCO/anonymous object into <c>attributes</c> at once.
  /// Later calls overwrite by key.</summary>
  public ExItemDef Attributes(object poco) {
    JObject source =
      poco as JObject
      ?? JToken.FromObject(poco) as JObject
      ?? throw new ArgumentException(
        "Attributes(poco) needs an object with named properties.",
        nameof(poco)
      );
    JObject attributes = Nested("attributes");
    foreach (JProperty property in source.Properties())
      attributes[property.Name] = property.Value;
    return this;
  }

  /// <summary>Sets <c>attributes.handbook.groupBy</c>, the handbook variant grouping.</summary>
  public ExItemDef Handbook(params string[] groupBy) {
    Nested("attributes")["handbook"] = new JObject {
      ["groupBy"] = new JArray(groupBy),
    };
    return this;
  }

  /// <summary>Sets <c>attributes.handbook.exclude</c>, hiding the item from the survival
  /// handbook.</summary>
  public ExItemDef HandbookExclude() {
    JObject attributes = Nested("attributes");
    JObject handbook = attributes["handbook"] as JObject ?? new JObject();
    handbook["exclude"] = true;
    attributes["handbook"] = handbook;
    return this;
  }

  /// <summary>Adds a <c>{wildcard: value}</c> entry to a <c>{key}ByType</c> map under
  /// <c>attributes</c> (accumulates across calls).</summary>
  public ExItemDef AttributeByType(string key, string wildcard, object value) {
    JObject attrs = Nested("attributes");
    if (attrs[key] is not JObject map) {
      map = new JObject();
      attrs[key] = map;
    }
    map[wildcard] = value as JToken ?? JToken.FromObject(value);
    return this;
  }

  /// <summary>Adds a <c>{wildcard: value}</c> entry to a top-level <c>{key}ByType</c> map
  /// (accumulates).</summary>
  public ExItemDef RootKeyByType(string key, string wildcard, object value) {
    if (_root[key] is not JObject map) {
      map = new JObject();
      _root[key] = map;
    }
    map[wildcard] = value as JToken ?? JToken.FromObject(value);
    return this;
  }

  /// <summary>Sets an arbitrary top-level key to an arbitrary token. Read only when
  /// <paramref name="key"/> is a real itemtype key the object loader understands.</summary>
  public ExItemDef RootKey(string key, JToken token) => Set(key, token);

  /// <summary>Sets an arbitrary top-level key from a POCO/anonymous object.</summary>
  public ExItemDef RootKey(string key, object value) =>
    Set(key, value as JToken ?? JToken.FromObject(value));

  /// <summary>Obsolete name for <see cref="RootKeyByType(string, string, object)"/>.</summary>
  [Obsolete(
    "Raw writes a top-level key the game reads only when it is a real blocktype key; use RootKey, or Attribute for attributes.{key}"
  )]
  public ExItemDef RawByType(string key, string wildcard, object value) =>
    RootKeyByType(key, wildcard, value);

  /// <summary>Obsolete name for <see cref="RootKey(string, JToken)"/>.</summary>
  [Obsolete(
    "Raw writes a top-level key the game reads only when it is a real blocktype key; use RootKey, or Attribute for attributes.{key}"
  )]
  public ExItemDef Raw(string key, JToken token) => RootKey(key, token);

  /// <summary>Obsolete name for <see cref="RootKey(string, object)"/>.</summary>
  [Obsolete(
    "Raw writes a top-level key the game reads only when it is a real blocktype key; use RootKey, or Attribute for attributes.{key}"
  )]
  public ExItemDef Raw(string key, object value) => RootKey(key, value);

  #endregion

  /// <summary>The explicit states of a variant group by its code. Empty when absent or
  /// worldproperty-sourced.</summary>
  public string[] VariantStates(string groupCode) {
    if (_root["variantgroups"] is not JArray groups)
      return [];
    foreach (JToken g in groups)
      if ((string?)g["code"] == groupCode && g["states"] is JArray states)
        return states.Select(s => (string)s!).ToArray();
    return [];
  }

  /// <summary>The built itemtype JSON (a defensive clone, safe to mutate/serialize).</summary>
  public JObject ToJson() {
    var json = (JObject)_root.DeepClone();
    ShapeEntries.SpreadSelectiveElements(json);
    return json;
  }

  // Explicit implementation: an implicit one cannot covary the return type to JObject.
  JToken IExDef.ToJson() => ToJson();

  private ExItemDef Set(string key, JToken value) {
    _root[key] = value;
    return this;
  }

  private JObject Nested(string key) {
    if (_root[key] is not JObject obj) {
      obj = new JObject();
      _root[key] = obj;
    }
    return obj;
  }

  private JArray NestedArray(string key) {
    if (_root[key] is not JArray arr) {
      arr = new JArray();
      _root[key] = arr;
    }
    return arr;
  }
}
