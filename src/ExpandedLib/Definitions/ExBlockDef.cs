using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Blocks;
using ExpandedLib.Helpers;
using ExpandedLib.Registries;
using ExpandedLib.Structures;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Definitions;

/// <summary>A code-first block definition: a fluent builder producing the <see cref="JObject"/>
/// the vanilla object loader consumes for a <c>blocktypes/</c> asset.</summary>
public sealed class ExBlockDef : IExDef {
  private readonly string _domain;
  private readonly string _code;
  private readonly string _assetName;
  private readonly JObject _root = new();

  private ExBlockDef(string domain, string code, string assetName) {
    _domain = domain;
    _code = code;
    _assetName = assetName;
    _root["code"] = code;
  }

  /// <summary>Starts a block definition for <paramref name="code"/> in <paramref name="domain"/>.
  /// The synthetic asset is placed at <c>blocktypes/{code}.json</c>.</summary>
  public static ExBlockDef Create(string domain, string code) =>
    new(domain, code, code);

  /// <summary>Starts a block definition whose asset path differs from its <paramref name="code"/>.
  /// <paramref name="assetName"/> may include sub-folders.</summary>
  public static ExBlockDef Create(
    string domain,
    string code,
    string assetName
  ) => new(domain, code, assetName);

  /// <summary>The mod id / asset domain this block belongs to.</summary>
  public string Domain => _domain;

  /// <summary>The block's <c>code</c> field (may be shared across several defs).</summary>
  public string Code => _code;

  /// <summary>The synthetic asset location the loader keys on, unique per def.</summary>
  public AssetLocation Location =>
    new(_domain, "blocktypes/" + _assetName + ".json");

  #region Rendered codes

  /// <summary>The domain-qualified bare code, no variants: <c>iiex:furnace</c>.</summary>
  public string QualifiedCode => _domain + ":" + _code;

  /// <summary>This def's variant groups in declaration order. <see cref="ExVariantGroup.States"/>
  /// is empty for a worldproperty-sourced group.</summary>
  public IReadOnlyList<ExVariantGroup> VariantGroups {
    get {
      if (_root["variantgroups"] is not JArray groups)
        return [];

      var result = new List<ExVariantGroup>();
      foreach (JToken g in groups) {
        string? props = (string?)g["loadFromProperties"];
        // A codeless worldproperty group is named after the property path's last segment.
        string? name = (string?)g["code"] ?? props?.Split('/').Last();
        if (name == null)
          continue;

        string[] states = g["states"] is JArray arr
          ? [.. arr.Select(s => (string)s!)]
          : [];
        result.Add(new ExVariantGroup(name, states, props));
      }
      return result;
    }
  }

  /// <summary>The wildcard matching every variant of this def: <c>iiex:furnace-irontap-*</c>. A
  /// single-state group is baked in rather than wildcarded.</summary>
  public string Any =>
    QualifiedCode
    + string.Concat(
      VariantGroups.Select(g => g.States.Count == 1 ? "-" + g.States[0] : "-*")
    );

  /// <summary>The code with <paramref name="group"/> pinned to <paramref name="state"/> and every
  /// other varying group wildcarded.</summary>
  /// <exception cref="ArgumentException">The def declares no variant group named
  /// <paramref name="group"/>.</exception>
  public string WithVariant(string group, string state) {
    var groups = VariantGroups;
    if (!groups.Any(g => g.Name == group))
      throw new ArgumentException(
        $"'{QualifiedCode}' has no variant group '{group}' - it declares "
          + (
            groups.Count == 0
              ? "none at all"
              : string.Join(", ", groups.Select(g => g.Name))
          ),
        nameof(group)
      );

    return QualifiedCode
      + string.Concat(
        groups.Select(g =>
          g.Name == group ? "-" + state
          : g.States.Count == 1 ? "-" + g.States[0]
          : "-*"
        )
      );
  }

  #endregion

  #region Class binding (type-safe)

  /// <summary>Sets <c>class</c> to <typeparamref name="T"/>'s registered key (type-safe).</summary>
  public ExBlockDef Class<T>()
    where T : Block => Set("class", EntityRegistry.KeyFor(_domain, typeof(T)));

  /// <summary>Sets <c>class</c> to an explicit registered key (for a vanilla class by name).</summary>
  public ExBlockDef Class(string registeredKey) => Set("class", registeredKey);

  /// <summary>Sets <c>entityClass</c> to <typeparamref name="T"/>'s registered key (type-safe).</summary>
  public ExBlockDef EntityClass<T>()
    where T : BlockEntity =>
    Set("entityClass", EntityRegistry.KeyFor(_domain, typeof(T)));

  /// <summary>Sets <c>entityClass</c> to an explicit registered key.</summary>
  public ExBlockDef EntityClass(string registeredKey) =>
    Set("entityClass", registeredKey);

  #endregion

  #region Scalars

  /// <summary>Sets <c>blockmaterial</c> (e.g. <see cref="EnumBlockMaterial.Metal"/>).</summary>
  public ExBlockDef Material(EnumBlockMaterial material) =>
    Set("blockmaterial", material.ToString());

  /// <summary>Sets <c>resistance</c> (break resistance).</summary>
  public ExBlockDef Resistance(float resistance) =>
    Set("resistance", resistance);

  /// <summary>Sets <c>maxstacksize</c>.</summary>
  public ExBlockDef MaxStackSize(int size) => Set("maxstacksize", size);

  /// <summary>Sets <c>storageFlags</c> (the inventory slots the block's item may be stored in).</summary>
  public ExBlockDef StorageFlags(int flags) => Set("storageFlags", flags);

  /// <summary>Sets <c>replaceable</c>, the replace-priority the world uses when another block is
  /// placed over this one.</summary>
  public ExBlockDef Replaceable(int priority) => Set("replaceable", priority);

  /// <summary>Sets <c>materialDensity</c> (kg/m3-ish, drives float/sink and shove behaviour).</summary>
  public ExBlockDef MaterialDensity(int density) =>
    Set("materialDensity", density);

  /// <summary>Sets <c>heldTpIdleAnimation</c>, the third-person idle animation played while the
  /// block is held.</summary>
  public ExBlockDef HeldTpIdleAnimation(string animation) =>
    Set("heldTpIdleAnimation", animation);

  /// <summary>Sets <c>heldRightReadyAnimation</c>, the first-person ready pose when the held block is raised.</summary>
  public ExBlockDef HeldRightReadyAnimation(string animation) =>
    Set("heldRightReadyAnimation", animation);

  /// <summary>Sets <c>heldTpUseAnimation</c>, the third-person animation played when the held block is used.</summary>
  public ExBlockDef HeldTpUseAnimation(string animation) =>
    Set("heldTpUseAnimation", animation);

  /// <summary>Sets <c>walkspeedmultiplier</c>. Takes a <see cref="double"/> so the emitted number
  /// matches the JSON-parsed value exactly.</summary>
  public ExBlockDef WalkSpeedMultiplier(double multiplier) =>
    Set("walkspeedmultiplier", multiplier);

  /// <summary>Sets <c>requiredMiningTier</c>.</summary>
  public ExBlockDef MiningTier(int tier) => Set("requiredMiningTier", tier);

  /// <summary>Does nothing; <c>mineTool</c> is not a key the vanilla loader reads. Use
  /// <see cref="Material"/> and <see cref="MiningTier"/> instead.</summary>
  [Obsolete(
    "mineTool is not a blocktype key the game reads; use Material and MiningTier instead."
  )]
  public ExBlockDef MineTool(EnumTool tool) => this;

  /// <summary>Sets <c>drops</c> to an empty array.</summary>
  public ExBlockDef NoDrops() => Set("drops", new JArray());

  /// <summary>Appends one <c>drops</c> entry (<c>{ type, code[, quantity] }</c>). Accumulates
  /// across calls.</summary>
  public ExBlockDef Drop(string type, string code, int? quantity = null) {
    var entry = new JObject { ["type"] = type, ["code"] = code };
    if (quantity.HasValue)
      entry["quantity"] = quantity.Value;
    NestedArray("drops").Add(entry);
    return this;
  }

  #endregion

  #region Shape / textures (references to art files)

  /// <summary>Sets the <c>shape</c> base reference (<c>{ "base": "domain:path" }</c>). Order-independent
  /// with <see cref="ShapeRotateYByType"/>/<see cref="ShapeSelectiveElements"/> - all mutate one node.</summary>
  public ExBlockDef Shape(string baseShape) {
    NestedShape()["base"] = baseShape;
    return this;
  }

  /// <summary>Maps a variant wildcard to a Y rotation under the single shape's
  /// <c>rotateYByType</c>.</summary>
  public ExBlockDef ShapeRotateYByType(string wildcard, int degrees) {
    JObject shape = NestedShape();
    if (shape["rotateYByType"] is not JObject map) {
      map = new JObject();
      shape["rotateYByType"] = map;
    }
    map[wildcard] = degrees;
    return this;
  }

  /// <summary>Sets the shape's <c>selectiveElements</c>.</summary>
  public ExBlockDef ShapeSelectiveElements(params string[] elements) {
    NestedShape()["selectiveElements"] = new JArray(elements);
    return this;
  }

  /// <summary>Sets one base shape and spins it for the four horizontal orientations via
  /// <c>rotateYByType</c>. <paramref name="offset"/> is added to every angle.</summary>
  public ExBlockDef ShapeSpunPerOrientation(string baseShape, int offset = 0) {
    Shape(baseShape);
    foreach (string side in HorizontalSides)
      ShapeRotateYByType(
        $"*-{side}",
        (ExOrientation.AngleFromSide(side) + offset + 360) % 360
      );
    return this;
  }

  /// <summary>The <c>shapebytype</c> form of <see cref="ShapeSpunPerOrientation"/>.</summary>
  public ExBlockDef ShapeByTypePerOrientation(string baseShape, int offset = 0) {
    foreach (string side in HorizontalSides)
      ShapeByType(
        $"*-{side}",
        baseShape,
        rotateY: (ExOrientation.AngleFromSide(side) + offset + 360) % 360
      );
    return this;
  }

  /// <summary>The four horizontal facings as single letters, matching the tokens network
  /// <c>orientation</c> groups use.</summary>
  private static readonly string[] HorizontalSides = ["n", "e", "s", "w"];

  /// <summary>Adds a texture mapping <paramref name="key"/> -&gt; <c>{ "base": "domain:path" }</c>
  /// under <c>textures</c> (accumulates across calls).</summary>
  public ExBlockDef Texture(
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
  public ExBlockDef TextureAll(string baseTexture) =>
    Texture("all", baseTexture);

  /// <summary>Sets a texture mapping <paramref name="key"/> from a fully-formed POCO/anonymous
  /// object/token. Overwrites any existing entry for the key.</summary>
  public ExBlockDef Texture(string key, object texture) {
    Nested("textures")[key] = texture as JToken ?? JToken.FromObject(texture);
    return this;
  }

  #endregion

  #region Model transforms

  /// <summary>Sets the <c>guiTransform</c> from a POCO/anonymous object/token.</summary>
  public ExBlockDef GuiTransform(object transform) =>
    RootKey("guiTransform", transform);

  /// <summary>Sets the <c>guiTransform</c> from translation, rotation, origin and uniform
  /// scale.</summary>
  public ExBlockDef GuiTransform(
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
  public ExBlockDef FpHandTransform(object transform) =>
    RootKey("fpHandTransform", transform);

  /// <summary>Sets the <c>fpHandTransform</c> from translation, rotation, origin and uniform
  /// scale.</summary>
  public ExBlockDef FpHandTransform(
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

  /// <summary>Sets the <c>tpHandTransform</c> from a POCO/anonymous object/token.</summary>
  public ExBlockDef TpHandTransform(object transform) =>
    RootKey("tpHandTransform", transform);

  /// <summary>Sets the <c>tpHandTransform</c> from translation, XYZ rotation in degrees and
  /// uniform scale, with no origin.</summary>
  public ExBlockDef TpHandTransform(
    double tx,
    double ty,
    double tz,
    double rx,
    double ry,
    double rz,
    double scale
  ) => Set("tpHandTransform", Transform(tx, ty, tz, rx, ry, rz, scale));

  /// <summary>Sets the <c>tpHandTransform</c> from translation, rotation, origin and uniform
  /// scale.</summary>
  public ExBlockDef TpHandTransform(
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
  public ExBlockDef GroundTransform(object transform) =>
    RootKey("groundTransform", transform);

  /// <summary>Sets the <c>groundTransform</c> from translation, rotation, origin and uniform
  /// scale.</summary>
  public ExBlockDef GroundTransform(
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

  // Emits { translation, rotation, scale }, no origin.
  private static JObject Transform(
    double tx,
    double ty,
    double tz,
    double rx,
    double ry,
    double rz,
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
      ["scale"] = scale,
    };

  // The four-key shape every other overload above emits, matching ExItemDef's transform object.
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

  #region Sounds / creative tabs

  /// <summary>Adds one <c>sounds.{type}</c> entry (e.g. <c>Sound("place", "game:block/anvil")</c>).</summary>
  public ExBlockDef Sound(string type, string assetPath) {
    Nested("sounds")[type] = assetPath;
    return this;
  }

  /// <summary>Sets the place/break/hit/walk <c>sounds</c> entries at once. Equivalent to four
  /// <see cref="Sound"/> calls.</summary>
  public ExBlockDef Sounds(
    string place,
    string breakSound,
    string hit,
    string walk
  ) =>
    Sound("place", place)
      .Sound("break", breakSound)
      .Sound("hit", hit)
      .Sound("walk", walk);

  /// <summary>The metal machine sound set (anvil place/break/hit, stone walk) shared by the pipes, boilers,
  /// engines and converters.</summary>
  public ExBlockDef MetalSounds() =>
    Sounds(
      "game:block/anvil",
      "game:block/anvil",
      "game:block/anvil",
      "game:walk/stone"
    );

  /// <summary>Adds a per-tool hit/break sound override under <c>sounds.byTool.{tool}</c>.</summary>
  public ExBlockDef SoundByTool(EnumTool tool, string hit, string breakSound) {
    var byTool = Nested("sounds");
    if (byTool["byTool"] is not JObject tools) {
      tools = new JObject();
      byTool["byTool"] = tools;
    }
    tools[tool.ToString()] = new JObject {
      ["hit"] = hit,
      ["break"] = breakSound,
    };
    return this;
  }

  /// <summary>Adds a per-variant sound override under <c>sounds.{type}ByType</c>
  /// (accumulates).</summary>
  public ExBlockDef SoundByType(string type, string wildcard, string assetPath) {
    JObject sounds = Nested("sounds");
    string key = type + "ByType";
    if (sounds[key] is not JObject map) {
      map = new JObject();
      sounds[key] = map;
    }
    map[wildcard] = assetPath;
    return this;
  }

  /// <summary>Adds a <c>creativeinventory.{tab}</c> selector list (accumulates across calls).</summary>
  public ExBlockDef CreativeTab(string tab, params string[] selectors) {
    Nested("creativeinventory")[tab] = new JArray(selectors);
    return this;
  }

  /// <summary>Adds the block to both the <c>general</c> tab and this def's own mod tab with the
  /// same <paramref name="selectors"/>.</summary>
  public ExBlockDef CreativeCommon(params string[] selectors) =>
    CreativeTab("general", selectors).CreativeTab(_domain, selectors);

  #endregion

  #region Variant groups + *ByType maps

  /// <summary>Appends a <c>variantgroups</c> entry with explicit <paramref name="states"/>. Order
  /// is preserved.</summary>
  public ExBlockDef VariantGroup(string code, params string[] states) {
    NestedArray("variantgroups")
      .Add(new JObject { ["code"] = code, ["states"] = new JArray(states) });
    return this;
  }

  /// <summary>Appends a <c>variantgroups</c> entry sourced from a worldproperty.</summary>
  public ExBlockDef VariantGroupFromProperties(
    string code,
    string propertiesPath
  ) {
    NestedArray("variantgroups")
      .Add(
        new JObject { ["code"] = code, ["loadFromProperties"] = propertiesPath }
      );
    return this;
  }

  /// <summary>Appends a codeless <c>variantgroups</c> entry sourced from a worldproperty, with no
  /// <c>code</c>.</summary>
  public ExBlockDef VariantGroupFromProperties(string propertiesPath) {
    NestedArray("variantgroups")
      .Add(new JObject { ["loadFromProperties"] = propertiesPath });
    return this;
  }

  /// <summary>Declares the <c>side</c> variant group as an explicit four-state letter list. The
  /// states must be explicit.</summary>
  public ExBlockDef SideVariant() => VariantGroup("side", HorizontalSides);

  /// <summary>Declares this block's orientation as the network's to pick, via the
  /// <c>ExOrientable</c> behaviour in <c>network</c> mode. Call it after the orientation variant
  /// group.</summary>
  /// <exception cref="InvalidOperationException">The declared states set-equal no scheme in
  /// <see cref="ExOrientations.All"/>.</exception>
  public ExBlockDef NetworkOriented() {
    string[] states = VariantStates(
      Blocks.BlockBehaviorExOrientable.OrientationVariant
    );
    ExOrientationScheme scheme =
      ExOrientations.Resolve(states)
      ?? throw new InvalidOperationException(
        $"{_domain}:{_code} declares orientation states [{string.Join(",", states)}], which "
          + "set-equal no scheme in ExOrientations.All. Declare the group before NetworkOriented(), "
          + "and declare the scheme in ExOrientations if the set is genuinely new."
      );

    return Behavior(
      "ExOrientable",
      new { mode = "network", scheme = scheme.Name }
    );
  }

  /// <summary>Sets <c>skipVariants</c> - variant-code wildcards the loader must not expand (e.g. rock types a
  /// block doesn't ship a texture for).</summary>
  public ExBlockDef SkipVariants(params string[] wildcards) =>
    Set("skipVariants", new JArray(wildcards));

  /// <summary>Maps a variant wildcard (e.g. <c>*-straight-we-*</c>) to a base shape reference, with
  /// optional axis rotations, under <c>shapebytype</c> (expansion stays in vanilla).</summary>
  public ExBlockDef ShapeByType(
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
  public ExBlockDef TextureByType(
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

  /// <summary>Appends a block behavior by its registered name (a vanilla behavior, e.g.
  /// <c>"Lockable"</c>).</summary>
  public ExBlockDef Behavior(string name) {
    NestedArray("behaviors").Add(new JObject { ["name"] = name });
    return this;
  }

  /// <summary>Appends a block behavior carrying a <c>properties</c> blob.</summary>
  public ExBlockDef Behavior(string name, object properties) {
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

  /// <summary>Appends a block behavior by type, resolved to <typeparamref name="T"/>'s registered
  /// <c>{modid}.{ClassName}</c> key.</summary>
  public ExBlockDef Behavior<T>()
    where T : BlockBehavior =>
    Behavior(EntityRegistry.KeyFor(_domain, typeof(T)));

  /// <summary>Appends a block-entity behavior by its registered name (a vanilla behavior, e.g.
  /// <c>"Animatable"</c>).</summary>
  public ExBlockDef EntityBehavior(string name) {
    NestedArray("entityBehaviors").Add(new JObject { ["name"] = name });
    return this;
  }

  /// <summary>Appends a block-entity behavior carrying a <c>properties</c> blob. Prefer
  /// <see cref="Construction"/> for the RCC behavior.</summary>
  public ExBlockDef EntityBehavior(string name, JObject properties) {
    NestedArray("entityBehaviors")
      .Add(new JObject { ["name"] = name, ["properties"] = properties });
    return this;
  }

  /// <summary>Appends a block-entity behavior by type, resolved to <typeparamref name="T"/>'s
  /// registered key. Safe across assemblies.</summary>
  public ExBlockDef EntityBehavior<T>()
    where T : BlockEntityBehavior =>
    EntityBehavior(EntityRegistry.KeyFor(_domain, typeof(T)));

  /// <summary>Appends a block-entity behavior by type, carrying a <c>properties</c> blob.</summary>
  public ExBlockDef EntityBehavior<T>(JObject properties)
    where T : BlockEntityBehavior =>
    EntityBehavior(EntityRegistry.KeyFor(_domain, typeof(T)), properties);

  #endregion

  #region Physics / collision / render

  /// <summary>Appends a <c>collisionboxes</c> cuboid (0..1 block-local coordinates).</summary>
  public ExBlockDef CollisionBox(
    float x1,
    float y1,
    float z1,
    float x2,
    float y2,
    float z2
  ) {
    NestedArray("collisionboxes").Add(Box(x1, y1, z1, x2, y2, z2));
    return this;
  }

  /// <summary>Appends a <c>selectionboxes</c> cuboid (0..1 block-local coordinates).</summary>
  public ExBlockDef SelectionBox(
    float x1,
    float y1,
    float z1,
    float x2,
    float y2,
    float z2
  ) {
    NestedArray("selectionboxes").Add(Box(x1, y1, z1, x2, y2, z2));
    return this;
  }

  /// <summary>Sets the single <c>collisionbox</c> object (one cuboid) rather than the
  /// <c>collisionboxes</c> array.</summary>
  public ExBlockDef SingleCollisionBox(
    float x1,
    float y1,
    float z1,
    float x2,
    float y2,
    float z2
  ) => Set("collisionbox", Box(x1, y1, z1, x2, y2, z2));

  /// <summary>Sets the single <c>selectionbox</c> object (one cuboid) rather than the
  /// <c>selectionboxes</c> array.</summary>
  public ExBlockDef SingleSelectionBox(
    float x1,
    float y1,
    float z1,
    float x2,
    float y2,
    float z2
  ) => Set("selectionbox", Box(x1, y1, z1, x2, y2, z2));

  /// <summary>Sets <c>sidesolid</c> for all faces at once (<c>{ "all": value }</c>).</summary>
  public ExBlockDef SideSolid(bool all) =>
    Set("sidesolid", new JObject { ["all"] = all });

  /// <summary>Sets <c>sidesolid</c> from a per-face POCO (<c>{ all, up, down, north, ... }</c>).</summary>
  public ExBlockDef SideSolid(object faces) =>
    Set("sidesolid", faces as JToken ?? JToken.FromObject(faces));

  /// <summary>Sets <c>sideopaque</c> for all faces at once (<c>{ "all": value }</c>).</summary>
  public ExBlockDef SideOpaque(bool all) =>
    Set("sideopaque", new JObject { ["all"] = all });

  /// <summary>Sets <c>sideopaque</c> from a per-face POCO (<c>{ all, down, ... }</c>).</summary>
  public ExBlockDef SideOpaque(object faces) =>
    Set("sideopaque", faces as JToken ?? JToken.FromObject(faces));

  /// <summary>Sets <c>sideAo</c> (ambient-occlusion contribution) for all faces at once.</summary>
  public ExBlockDef SideAo(bool all) =>
    Set("sideAo", new JObject { ["all"] = all });

  /// <summary>Sets <c>emitSideAo</c> (whether the block casts ambient occlusion onto neighbours)
  /// for all faces at once. Distinct from <see cref="SideAo"/>.</summary>
  public ExBlockDef EmitSideAo(bool all) =>
    Set("emitSideAo", new JObject { ["all"] = all });

  /// <summary>Sets <c>renderpass</c> (e.g. <c>"OpaqueNoCull"</c>).</summary>
  public ExBlockDef RenderPass(string pass) => Set("renderpass", pass);

  /// <summary>Sets <c>renderpass</c> from the enum (type-safe).</summary>
  public ExBlockDef RenderPass(EnumChunkRenderPass pass) =>
    RenderPass(pass.ToString());

  /// <summary>Sets <c>faceCullMode</c> (e.g. <c>"NeverCull"</c>).</summary>
  public ExBlockDef FaceCullMode(string mode) => Set("faceCullMode", mode);

  /// <summary>Sets <c>faceCullMode</c> from the enum (type-safe).</summary>
  public ExBlockDef FaceCullMode(EnumFaceCullMode mode) =>
    FaceCullMode(mode.ToString());

  /// <summary>Sets <c>drawtype</c> (e.g. <c>"json"</c> for a shape-driven block, or <c>"empty"</c>).</summary>
  public ExBlockDef DrawType(string drawType) => Set("drawtype", drawType);

  /// <summary>Sets <c>drawtype</c> from the enum (type-safe).</summary>
  public ExBlockDef DrawType(EnumDrawType drawType) =>
    DrawType(drawType.ToString());

  /// <summary>Sets <c>lightAbsorption</c>.</summary>
  public ExBlockDef LightAbsorption(int absorption) =>
    Set("lightAbsorption", absorption);

  /// <summary>Marks the block non-solid and non-opaque on all faces.</summary>
  public ExBlockDef NonSolid() => SideSolid(false).SideOpaque(false);

  /// <summary>Solid but non-opaque on all faces.</summary>
  public ExBlockDef SolidNonOpaque() => SideSolid(true).SideOpaque(false);

  private static JObject Box(
    float x1,
    float y1,
    float z1,
    float x2,
    float y2,
    float z2
  ) =>
    new() {
      ["x1"] = x1,
      ["y1"] = y1,
      ["z1"] = z1,
      ["x2"] = x2,
      ["y2"] = y2,
      ["z2"] = z2,
    };

  #endregion

  #region Arbitrary attributes + escape hatch

  /// <summary>Sets an arbitrary <c>attributes.{key}</c> entry from a POCO/anonymous
  /// object/token/collection.</summary>
  public ExBlockDef Attribute(string key, object value) {
    Nested("attributes")[key] = value as JToken ?? JToken.FromObject(value);
    return this;
  }

  /// <summary>Merges every property of a POCO/anonymous object into <c>attributes</c> at once.
  /// Later calls overwrite by key.</summary>
  public ExBlockDef Attributes(object poco) {
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
  public ExBlockDef Handbook(params string[] groupBy) {
    Nested("attributes")["handbook"] = new JObject {
      ["groupBy"] = new JArray(groupBy),
    };
    return this;
  }

  /// <summary>Sets <c>attributes.handbook.exclude</c>, hiding the block from the survival
  /// handbook.</summary>
  public ExBlockDef HandbookExclude() {
    JObject attributes = Nested("attributes");
    JObject handbook = attributes["handbook"] as JObject ?? new JObject();
    handbook["exclude"] = true;
    attributes["handbook"] = handbook;
    return this;
  }

  /// <summary>Sets <c>attributes.fillerOffsets</c>, the mega-block's invisible per-cell collision
  /// reservation. Validated: no duplicate cell, none at the principal origin.</summary>
  public ExBlockDef FillerOffsets(IEnumerable<FillerCellSpec> cells) {
    Nested("attributes")["fillerOffsets"] = SerializeFillerCells(cells);
    return this;
  }

  /// <summary>Per-type footprint: sets <c>attributesByType.{typeWildcard}.fillerOffsets</c>.
  /// Accumulates; call once per type wildcard.</summary>
  public ExBlockDef FillerOffsetsByType(
    string typeWildcard,
    IEnumerable<FillerCellSpec> cells
  ) {
    if (_root["attributesByType"] is not JObject byType) {
      byType = new JObject();
      _root["attributesByType"] = byType;
    }
    if (byType[typeWildcard] is not JObject attrs) {
      attrs = new JObject();
      byType[typeWildcard] = attrs;
    }
    attrs["fillerOffsets"] = SerializeFillerCells(cells);
    return this;
  }

  /// <summary>Shared with <see cref="ExpandedLib.Structures.JsonMultiblockLayout"/>.</summary>
  internal static JArray SerializeFillerCells(IEnumerable<FillerCellSpec> cells) {
    var list = cells as IReadOnlyList<FillerCellSpec> ?? cells.ToArray();
    StructureFootprint.Validate(list);

    var array = new JArray();
    foreach (FillerCellSpec cell in list) {
      var entry = new JObject {
        ["x"] = cell.X,
        ["y"] = cell.Y,
        ["z"] = cell.Z,
      };
      if (cell.Behaviors is { Count: > 0 }) {
        var behaviors = new JArray();
        foreach (FillerBehaviorSpec b in cell.Behaviors) {
          var behavior = new JObject { ["code"] = b.Code };
          if (b.Face != null)
            behavior["face"] = b.Face;
          if (b.Properties != null)
            behavior["properties"] = JToken.FromObject(b.Properties);
          behaviors.Add(behavior);
        }
        entry["behaviors"] = behaviors;
      }
      if (cell.CollisionBoxes is { Count: > 0 } boxes)
        // One box writes `collisionBox`, several `collisionBoxes`.
        entry[boxes.Count == 1 ? "collisionBox" : "collisionBoxes"] =
          boxes.Count == 1
            ? Corners(boxes[0])
            : new JArray(boxes.Select(Corners));
      if (cell.AllowAttach)
        entry["allowAttach"] = true;
      if (cell.PortFace != null)
        entry["portFace"] = cell.PortFace;
      if (cell.PortNetworkType != null)
        entry["portNetwork"] = cell.PortNetworkType;
      array.Add(entry);
    }
    return array;
  }

  // Just the two corners, in vanilla's own lowercase spelling.
  private static JObject Corners(Cuboidf box) =>
    new() {
      ["x1"] = box.X1,
      ["y1"] = box.Y1,
      ["z1"] = box.Z1,
      ["x2"] = box.X2,
      ["y2"] = box.Y2,
      ["z2"] = box.Z2,
    };

  /// <summary>Appends the exlib right-click construction behavior, its staged material/shape
  /// table authored through a typed builder.</summary>
  public ExBlockDef Construction(Action<ConstructionStages> configure) {
    var stages = new ConstructionStages();
    configure(stages);
    return EntityBehavior(nameof(ExRightClickConstructable), stages.Build());
  }

  /// <summary>Sets <c>attributes.multiblockStructure</c> from a typed builder. See
  /// <see cref="MultiblockBuilder"/>.</summary>
  public ExBlockDef Multiblock(Action<MultiblockBuilder> configure) {
    var structure = new MultiblockBuilder();
    configure(structure);
    Nested("attributes")["multiblockStructure"] = structure.Build();
    return this;
  }

  /// <summary>Sets <c>attributes.multiblockStructure</c> from an ASCII layer diagram. See
  /// <see cref="MultiblockLayoutBuilder"/>.</summary>
  public ExBlockDef MultiblockLayout(Action<MultiblockLayoutBuilder> configure) {
    var layout = new MultiblockLayoutBuilder();
    configure(layout);
    Nested("attributes")["multiblockStructure"] = layout.Build();
    // Oriented legends ride in a sibling attribute: multiblockStructure must stay vanilla's schema.
    if (layout.BuildFacings() is JObject facings)
      Nested("attributes")["multiblockFacings"] = facings;
    // Cell roles and connector demands ride in siblings for the same reason.
    if (layout.BuildRoles() is JObject roles)
      Nested("attributes")["multiblockRoles"] = roles;
    if (layout.BuildConnectors() is JObject connectors)
      Nested("attributes")["multiblockConnectors"] = connectors;
    return this;
  }

  /// <summary>Sets the top-level <c>combustibleProps</c> from a POCO/anonymous object. Lives at
  /// the block root, not under <c>attributes</c>.</summary>
  public ExBlockDef CombustibleProps(object props) =>
    Set("combustibleProps", props as JToken ?? JToken.FromObject(props));

  /// <summary>Adds a <c>{wildcard: value}</c> entry to a <c>{key}ByType</c> map under
  /// <c>attributes</c> (accumulates across calls).</summary>
  public ExBlockDef AttributeByType(string key, string wildcard, object value) {
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
  public ExBlockDef RootKeyByType(string key, string wildcard, object value) {
    if (_root[key] is not JObject map) {
      map = new JObject();
      _root[key] = map;
    }
    map[wildcard] = value as JToken ?? JToken.FromObject(value);
    return this;
  }

  /// <summary>Sets an arbitrary top-level key to an arbitrary token. Read only when
  /// <paramref name="key"/> is a real blocktype key the object loader understands.</summary>
  public ExBlockDef RootKey(string key, JToken token) => Set(key, token);

  /// <summary>Sets an arbitrary top-level key from a POCO/anonymous object.</summary>
  public ExBlockDef RootKey(string key, object value) =>
    Set(key, value as JToken ?? JToken.FromObject(value));

  /// <summary>Obsolete name for <see cref="RootKeyByType(string, string, object)"/>.</summary>
  [Obsolete(
    "Raw writes a top-level key the game reads only when it is a real blocktype key; use RootKey, or Attribute for attributes.{key}"
  )]
  public ExBlockDef RawByType(string key, string wildcard, object value) =>
    RootKeyByType(key, wildcard, value);

  /// <summary>Obsolete name for <see cref="RootKey(string, JToken)"/>.</summary>
  [Obsolete(
    "Raw writes a top-level key the game reads only when it is a real blocktype key; use RootKey, or Attribute for attributes.{key}"
  )]
  public ExBlockDef Raw(string key, JToken token) => RootKey(key, token);

  /// <summary>Obsolete name for <see cref="RootKey(string, object)"/>.</summary>
  [Obsolete(
    "Raw writes a top-level key the game reads only when it is a real blocktype key; use RootKey, or Attribute for attributes.{key}"
  )]
  public ExBlockDef Raw(string key, object value) => RootKey(key, value);

  #endregion

  /// <summary>The explicit states of a variant group by its code. Empty when the group is absent
  /// or sourced from a worldproperty.</summary>
  public string[] VariantStates(string groupCode) {
    if (_root["variantgroups"] is not JArray groups)
      return [];
    foreach (JToken g in groups)
      if ((string?)g["code"] == groupCode && g["states"] is JArray states)
        return states.Select(s => (string)s!).ToArray();
    return [];
  }

  /// <summary>The built blocktype JSON (a defensive clone, safe to mutate/serialize).</summary>
  public JObject ToJson() {
    var json = (JObject)_root.DeepClone();
    ShapeEntries.SpreadSelectiveElements(json);
    return json;
  }

  // Explicit implementation: an implicit one cannot covary the return type to JObject.
  JToken IExDef.ToJson() => ToJson();

  private ExBlockDef Set(string key, JToken value) {
    _root[key] = value;
    return this;
  }

  // Returns the JObject at `key`, creating it if absent.
  private JObject Nested(string key) {
    if (_root[key] is not JObject obj) {
      obj = new JObject();
      _root[key] = obj;
    }
    return obj;
  }

  // Returns the single `shape` object, creating it if absent.
  private JObject NestedShape() => Nested("shape");

  // Returns the JArray at `key`, creating it if absent.
  private JArray NestedArray(string key) {
    if (_root[key] is not JArray arr) {
      arr = new JArray();
      _root[key] = arr;
    }
    return arr;
  }
}
