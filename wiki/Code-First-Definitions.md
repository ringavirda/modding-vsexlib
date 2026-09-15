# Code-First Definitions

In Vintage Story a block is not only a C# class. The game builds it from a JSON file under
`assets/yourmod/blocktypes/`, and that file is where the shape, the textures, the drops, the
collision boxes, the creative-tab placement and the list of behaviours live. The class the file
names is constructed once per block and reads the rest back out of `Attributes` at runtime. Items
and recipes work the same way, from `itemtypes/` and `recipes/`.

Nothing checks the two halves against each other. Rename the class and the file still names the old
one. Add a variant in the file and the C# that switches on it never hears about it. Misspell an
attribute key and your code quietly reads the default it was never meant to see. None of that is a
compile error: the good case is a line in the log at world load, the bad case is a block that loads
and does half its job.

`ExpandedLib.Definitions` moves the file into the class. You build the same
blocktype/itemtype/recipe JSON the vanilla object loader consumes, in C#, co-located with the class
it configures, and exlib serializes the result and injects it before the loader runs. Everything
ends up as the same synthetic asset a hand-written file would produce, so variant expansion,
`*ByType` selection, the atlas, block-ID assignment and client sync all run unchanged - no `Block`
is ever constructed directly from a definition. What you gain is the compiler reading your content,
and a test suite that can check every definition before the game ever sees it.

You apply it by implementing `IExBlockDefProvider` on your block class and returning
`ExBlockDef.Create(...)` chains from it. The registration scan your mod system already runs finds
it, so there is no second call to remember. [Getting Started](Getting-Started) walks a first one end
to end.

## Provider interfaces

Three sibling interfaces, one per asset kind, each with a single `static abstract` factory - a
static method the interface demands, which lets exlib call it on the type itself without
constructing anything:

```csharp
public interface IExBlockDefProvider {
  static abstract IEnumerable<ExBlockDef> Definitions(string domain);
}

public interface IExItemDefProvider {
  static abstract IEnumerable<ExItemDef> Definitions(string domain);
}

public interface IExRecipeDefProvider {
  static abstract IEnumerable<ExRecipeDef> Definitions(string domain);
}
```

Implement `IExBlockDefProvider` on the block class itself, so the definition sits beside the code
that reads it. It returns an `IEnumerable` because one class can back several blocktype assets -
same `code`, distinct asset paths - which is what a pipe class backing `pipe/straight`, `pipe/bend`
and the rest needs. Items and recipes have no natural class to hang a definition on, so their
providers are usually stand-alone classes that implement `IExItemDefProvider` /
`IExRecipeDefProvider` and nothing else.

`domain` is the mod id the def registers into, which is what binds `ExBlockDef.Create` and
`Class<T>()` to the right asset domain and registered-class key. It is handed to the factory rather
than written into it, so a definition cannot drift from the mod it ships in. You are handed your own
mod id, unless the provider class carries `[ExDefDomain("othermod")]`: that is how one assembly
emits definitions into a domain other than its own.

## Discovery and injection

`EntityRegistry.RegisterAll(api, mod, asm)` - the same call that registers your
`[BlockRegister]`/`[ItemRegister]`/`[BlockEntityRegister]` classes - also scans `asm` for provider
implementors and feeds each one's `Definitions(domain)` result into `ExDefinitions`. No separate call
is needed; a class that implements a provider interface is discovered the moment your `ModSystem.Start`
calls `EntityRegistry.RegisterAll`. (A `ModSystem` is the game's entry point for a mod's code and
`Start` is its first phase; [Registries](Registries) covers the scan itself.)

`ExDefinitions` is the process-wide registry the discovered defs land in. Holding them is all it
does. Every def is keyed by its asset location, so registering the same location twice replaces
rather than duplicates - which is also how you deliberately override a definition another mod
declared.

`ExDefinitionModSystem` is the `ModSystem` that turns the registry into real assets. exlib ships it
and you never register it yourself; two of its lines decide when and where it runs:

```csharp
public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Server;
public override double ExecuteOrder() => 0.04;
```

`AssetsLoaded` is the world-load phase where every mod's asset files are in memory and nothing has
been built from them yet; `ExecuteOrder` is the sort key that orders mod systems inside a phase. At
0.04 the injection runs below the game's own JSON patch loader (0.05), so injected assets stay
patchable by other mods; well below the object loader (0.2) that consumes them; and above 0 so base
assets are already indexed. It is server-only, because `blocktypes`/`itemtypes` loading and the object
loader are both server-only stages - **the client receives the resolved block and item types over the
network**, the same way it receives any other asset the server built. See [Lifecycle](Lifecycle) for
where this sits relative to everything else that happens at world load.

That injection is a deadline. A definition registered after it is never built;
[`LateDefinitionCheck`](Checks) names it and the fix in the log - unless
it re-registers a location injection already covered, replacing the built content with something the
loader never sees; the check tests location membership only, so that case stays silent.

## The builders

Three builders carry the asset kinds - `ExBlockDef`, `ExItemDef`, `ExRecipeDef` - and the smaller
ones after them are the pieces those compose with: multiblock layouts, grid recipes, ingredients,
code catalogues. All of them are fluent, so a definition is one expression and every method returns
the builder for the next.

### `ExBlockDef`

The block-side builder: `Create` starts a definition, each method writes one blocktype key, and the
fluent chain ends with `Location` (the synthetic asset path) and `ToJson()` (the built blocktype),
which are what exlib reads off it. Minimal:

```csharp
public static IEnumerable<ExBlockDef> Definitions(string domain) =>
  [ExBlockDef.Create(domain, "pebble")
    .Class<BlockPebble>()
    .Shape("game:block/basic/cube")
    .TextureAll("game:block/stone/rock/granite1")
    .CreativeCommon("*")];
```

A real call site, with everything a shipped block actually needs - the mpenergy transmission
mega-block:

```csharp
public static IEnumerable<ExBlockDef> Definitions(string domain) {
  ExBlockDef def = ExBlockDef
    .Create(domain, "mpenergy", "mpenergy/transmission")
    .Class<BlockTransmission>()
    .EntityClass<BlockEntityTransmission>()
    .Material(EnumBlockMaterial.Metal)
    .MiningTier(0)
    .Resistance(4.5f)
    .MaxStackSize(1)
    .NoDrops()
    .FillerOffsets([
      new FillerCellSpec(1, 0, 0),
      new FillerCellSpec(0, 1, 0),
      new FillerCellSpec(1, 1, 0),
    ])
    .Behavior<BlockBehaviorExOrientable>()
    .Behavior("BlockEntityInteract")
    .EntityBehavior("Animatable")
    .Construction(c => c.Stage(s => s.AddElements("Base")) /* ... */);
  // ...
}
```

`Create(domain, code)` places the asset at `blocktypes/{code}.json`; the three-argument overload
`Create(domain, code, assetName)` lets several blocktype files share one `code` (a pipe class backing
`pipe/straight`, `pipe/bend`, ...).

#### `ExBlockDef` methods by JSON section

Read the table from the blocktype JSON you already know - your own hand-written file, or one you
found in another mod - across to the method that writes it. Only the typed methods are listed;
anything not here goes through `Attribute`/`AttributeByType`/`RootKey`/`RootKeyByType` below.

Two of the rows carry most of the game's leverage and are worth a sentence each. A variant group
turns one definition into a family of blocks: declare a group `tier` with three states and a group
`orientation` with six, and the loader builds eighteen blocks whose codes are the states joined onto
the base code in declaration order (`pipe-cast-ns`). A `*ByType` key then varies one value across
that family: it holds wildcard patterns matched against the rendered code, so a single definition
gives a different shape per orientation without repeating anything else.

| Section | Methods | JSON key(s) written |
| --- | --- | --- |
| Shape | `Shape` | `shape.base` |
| | `ShapeRotateYByType`, `ShapeSpunPerOrientation` | `shape.rotateYByType.{wildcard}` |
| | `ShapeSelectiveElements` | `shape.selectiveElements` |
| | `ShapeByType`, `ShapeByTypePerOrientation` | `shapebytype.{wildcard}` |
| Textures | `Texture`, `TextureAll` | `textures.{key}` |
| | `TextureByType` | `texturesByType.{wildcard}.{key}` |
| Variants | `VariantGroup`, `VariantGroupFromProperties`, `SideVariant` | `variantgroups[]` |
| | `SkipVariants` | `skipVariants` |
| Behaviors | `Behavior(name)`, `Behavior(name, props)`, `Behavior<T>()`, `NetworkOriented` | `behaviors[]` |
| | `EntityBehavior(name)`, `EntityBehavior(name, props)`, `EntityBehavior<T>()`, `Construction` | `entityBehaviors[]` |
| Attributes | `Attribute`, `Attributes` | `attributes.{key}` |
| | `Handbook` | `attributes.handbook.groupBy` |
| | `FillerOffsets` | `attributes.fillerOffsets` |
| | `FillerOffsetsByType` | `attributesByType.{typeWildcard}.fillerOffsets` |
| | `AttributeByType` | `attributes.{key}ByType.{wildcard}` |
| | `Multiblock`, `MultiblockLayout` | `attributes.multiblockStructure` (+ `multiblockFacings`/`multiblockRoles`/`multiblockConnectors` siblings for `MultiblockLayout`) |
| Collision | `CollisionBox`, `SelectionBox` | `collisionboxes[]` / `selectionboxes[]` |
| | `SingleCollisionBox`, `SingleSelectionBox` | `collisionbox` / `selectionbox` |
| | `SideSolid`, `SideOpaque`, `SideAo`, `EmitSideAo`, `NonSolid`, `SolidNonOpaque` | `sidesolid` / `sideopaque` / `sideAo` / `emitSideAo` |
| Drops | `NoDrops`, `Drop` | `drops` / `drops[]` |
| Creative inventory | `CreativeTab`, `CreativeCommon` | `creativeinventory.{tab}` |
| Handbook | `HandbookExclude` | `handbook.exclude` (top-level, not under `attributes`) |

A handful of scalars have their own one-line methods too (`Material`, `Resistance`, `MaxStackSize`,
`StorageFlags`, `Replaceable`, `MaterialDensity`, the `Held*`/`heldTp*` animation setters,
`WalkSpeedMultiplier`, `MiningTier`, `MineTool`, `RenderPass`, `FaceCullMode`, `DrawType`,
`LightAbsorption`, sound setters, `CombustibleProps`) - each named after the blocktype key it writes.
`RenderPass` and `DrawType` also take the `EnumChunkRenderPass`/`EnumDrawType` enum directly, and
`FaceCullMode` the `EnumFaceCullMode` enum; all three still take the raw string too, writing the same
value either way.

Everything in the table above except the collision/side/render/sound/drop/entity/megablock rows and
`ShapeRotateYByType`/`ShapeSpunPerOrientation`/`ShapeByTypePerOrientation`/`SideVariant`/
`NetworkOriented` (placed-block orientation) also exists on `ExItemDef`, writing the same JSON key -
see the `ExItemDef` section below for the list of what does not carry over and why.

#### `Attribute`/`RootKey`: the escape hatch, and the difference that matters

Two methods reach schema the typed API does not cover. They write to different places, and the
difference decides whether anything reads the value at all:

- **`Attribute(key, value)`** writes `attributes.{key}` - a key the block/behaviour code reads back
  through `Attributes["..."]` at runtime. Anything is a valid key here; a typo just means your own
  code never finds it.
- **`RootKey(key, value)`** writes a **top-level** key next to `code`, `shape`, `behaviors`, etc. This
  is read only if it is a real blocktype key the game's object loader itself understands (e.g.
  `combustibleProps`, `guiTransform`; see `KnownRootKeys`). A key that is not one of those is written
  into the JSON but never read by anything - `ExDefinitionModSystem` logs a Warning for it at
  injection, naming the def and the key, rather than letting the mistake sit silent. `Raw`/`RawByType`
  are the pre-Unreleased names, kept as `[Obsolete]` forwarders.

### `ExItemDef`

The item-side sibling of `ExBlockDef`, covering every blocktype method whose JSON key also exists on
an itemtype (same builder shape: `Class<T>()`, `Shape`, `Texture`/`TextureByType`, `ShapeByType`,
`VariantGroup`, `Behavior`/`Behavior<T>()`, `Handbook`/`HandbookExclude`, `SkipVariants`,
`Attribute`/`AttributeByType`, `RootKey`/`RootKeyByType`, `CreativeTab`/`CreativeCommon`, the four
model transforms, plus `CombustibleProps`/`GrindingProps`). A stand-alone provider, since a plain item
has no mod class to hang the definition on:

```csharp
public class DiagramItemDefinitions : IExItemDefProvider {
  public static IEnumerable<ExItemDef> Definitions(string domain) {
    // ...
    yield return ExItemDef
      .Create(domain, "diagram")
      .VariantGroup("type", [.. types])
      .Texture("base", $"{domain}:item/diag-base")
      .Behavior("GroundStorable", new { layout = "SingleCenter" })
      .Handbook("diagram-*")
      // ...
  }
}
```

Missing on purpose: anything that only makes sense for a *placed* block - world orientation
(`ShapeRotateYByType`, `ShapeSpunPerOrientation`, `ShapeByTypePerOrientation`, `SideVariant`,
`NetworkOriented`), the render/collision/face fields (`RenderPass`, `FaceCullMode`, `DrawType`,
`CollisionBox`/`SelectionBox`, `SideSolid`/`SideOpaque`/`SideAo`/`EmitSideAo`), block entities
(`EntityClass<T>()`, `EntityBehavior`), sounds (`sounds.*` is BlockType-only; an item's `HeldSounds`
is a separate, unrelated key), drops (`NoDrops`/`Drop`) and megablocks
(`FillerOffsets`/`FillerOffsetsByType`, `Construction`, `Multiblock`/`MultiblockLayout`). A reflection
test over `ExBlockDef` and `ExItemDef` (in the `Definitions` test suite) is the audit that keeps this
list honest as `ExBlockDef` grows.

### `ExRecipeDef` and `GridRecipeBuilder`

`ExRecipeDef` is the recipe-file builder: one def produces one
`recipes/{category}/{assetName}.json` asset, either an array (`Grid`/`Add`, callable repeatedly) or a
single object (`GridObject`/`Body`, callable once). The category is the `recipes/` sub-folder the
game sorts by (`grid`, `smithing`, and the rest), and a grid recipe is the crafting-grid kind: a
pattern of letters, one ingredient per letter, and an output. `GridRecipeBuilder` builds one grid-recipe object
(`{ name, ingredientPattern, ingredients, width, height, output }`); `IngredientBuilder` builds one
ingredient slot. A real call site, the rolling mill's grid recipe:

```csharp
private static ExRecipeDef RollingMill(string domain) =>
  ExRecipeDef
    .Create(domain, "grid", "rollingmill")
    .Grid(r =>
      r.Name("Rolling Mill")
        .Pattern("PRP,PGP,PHP")
        .Size(3, 3)
        .Ingredient("P", i => i.Item($"{domain}:castplate-heavy").Quantity(1))
        .Ingredient("R", Rod(2))
        .Ingredient("G", i => i.Item($"{domain}:{SpurGearItemDefinitions.Code}").Quantity(1))
        .Ingredient("H", Hammer)
        .OutputBlock($"{domain}:forming-rollingmill-we", 1)
    );
```

(`Rod` and `Hammer` are `ExIngredients` factories, brought in via `using static
ExpandedLib.Definitions.ExIngredients;` - see below.)

### `MultiblockBuilder`

A multiblock structure is a machine that fills many block positions while one of them carries the
logic, and the game checks the other cells against a declared layout. This builder writes that
layout - a block's `attributes.multiblockStructure` (`{ blockNumbers, offsets }`) - from explicit
`Number`/`At`/`Fill` calls: `Number` maps a block code to the digit that stands for it, `At` places
that digit at one offset from the core block, and `Fill` fills a cuboid of them. It validates at
build time that every offset's number is declared and that no cell is duplicated. A real call site,
the Bessemer converter control block:

```csharp
.Multiblock(m =>
  m.Number("siex:convertercontrol*", 1)
    .Number("siex:convertertransmission*", 2)
    .Number("siex:converterbessemer*", 3)
    .Number(ExCodes.Filler, 8)
    .At(0, 0, 0, 1)
    .At(0, -1, 0, 2)
    .Fill(-1, -1, 1, 1, 1, 1, 8)
    // ...
)
```

### `MultiblockLayoutBuilder` and `StructureLayout`

The higher-level alternative to `MultiblockBuilder`: draws the structure as ASCII diagrams instead
of listing offsets by hand, which is what you want as soon as a structure runs past a handful of
cells, since the diagram is also the thing a reader can check against the model. `ExpandedLib.Structures.CellGrid` is the grid core underneath it (also
usable directly for other ASCII-diagram DSLs, such as the filler footprint); `MultiblockLayoutBuilder`
adds the legend, role and connector vocabulary over it. `Layer` draws a floor plan, one grid per Y
level; `Slice` and `Face` draw a fixed-X or fixed-Z elevation instead, for a structure that stacks in
Y - a layout may mix all three. A real call site, the coke oven core:

```csharp
.MultiblockLayout(s =>
  s.Origin(-4, -2)
    .Legend('#', VanillaCodes.FireBricks)
    .Legend('c', IiexBlocks.FurnaceFirebox.Any)
    .Legend('-', VanillaCodes.FireSlab(BlockFacing.UP))
    .Legend('C', IiexBlocks.FurnaceCokeovencore.Any)
    .Legend('f', ExCodes.Filler)
    .Legend('a', VanillaCodes.Air)
    .Role('c', FurnaceCellRoles.Firebox)
    .Layer(0, """
    # # # # # # # # #
    ...
    """)
    // ...
)
```

A code carrying a whole horizontal side segment (`north`/`south`/`east`/`west`, or the letters) is
orientation-checked automatically: the required facing rotates with the structure. See
[Multiblock Structures](Multiblock-Structures) for the orientation-checking mechanism itself and the
filler system this builder's `Legend`/`Layer` vocabulary is shared with. A block's own
`attributes.multiblockLayout` is the JSON twin of this builder, for a machine that carries no C# at
all - see "From JSON only" on that page.

### `ExVariantGroup`

Not something you construct directly - it is the read model `ExBlockDef.VariantGroups` returns for
each `VariantGroup(code, states...)` / `VariantGroupFromProperties(...)` call, in declaration order
(the order states appear in the rendered code). `ExBlockDef.Any` and `WithVariant(group, state)` walk
this list to build wildcarded or pinned codes for you, so a layout legend or a recipe output never
hand-types a variant string that can drift from the definition:

```csharp
.VariantGroup("tier", BlockPipe.CastTier)
.VariantGroup("type", "pressurevalve")
.VariantGroup("orientation", "ns", "we", "ud", "sn", "ew", "du")
```

### `ExCodes` and `VanillaCodes`

Two catalogues of block-code strings that multiblock layouts and recipes are drawn from, mirroring
each other's purpose but not their construction: `ExCodes` forwards to the generated `ExlibBlocks`
table, so a name can never drift from the code it stands for (`ExCodes.Filler` is
`ExlibBlocks.Structurefiller.Code`, the invisible per-cell filler). `VanillaCodes` cannot work that
way - the game declares those blocks, not a def in this codebase - so every member is hand-kept, at
varying strictness (`VanillaCodes.RefractoryTier(3)` for one exact tier,
`VanillaCodes.Refractory` for any tier, `VanillaCodes.AnyBricks` for any masonry at all). Each mod in
the family keeps the equivalent of `ExCodes` for its own blocks (`IiexBlocks`, `SiexBlocks`,
generated the same way).

### `ExIngredients`

A small catalogue of `IngredientBuilder` factories for vanilla `game:` ingredients shared across the
mods' recipe files - `Hammer`, `Chisel`, `Plate(qty)`, `Rod(qty)`, `FireClay(qty)`, and their
steel-only siblings. After `using static ExpandedLib.Definitions.ExIngredients;`, pass one as a method
group (`.Ingredient("H", Hammer)`) or call the quantity factory (`.Ingredient("P", Plate(1))`). Only
mod-agnostic ingredients belong here; a mod's own item codes stay in its own ingredients helper.

## Definitions that depend on loaded assets

A provider's `Definitions(domain)` runs the moment `EntityRegistry.RegisterAll` discovers it, which
can be before every mod's own assets are readable. That is fine for a definition that is complete in
source, and wrong for one built from a catalogue another mod's JSON contributes to: at `Start` time
the catalogue may not exist yet, so the definition comes out short and nothing says so.
`IExDefinitionContributor` is the interface for that case:

```csharp
public interface IExDefinitionContributor {
  void Contribute(ICoreAPI api);
}
```

`EntityRegistry.RegisterAll` discovers an implementor the same way it discovers a provider, but the
work is deferred: `ExDefinitions.RunContributors` instantiates and runs each one at `AssetsLoaded`
0.04, right before injection, regardless of which mod or module `Start` discovered it in - the one
point in the phase sequence guaranteed to run after every `Start` has returned, so a contributor can
read a catalogue `AssetCatalogueLoader` assembles from every domain's JSON and register definitions
from it. Server-only, the same as `ExDefinitionModSystem` itself.

Industry's metal-family item emission is a contributor for exactly this reason: the metals it builds
items for come from `config/metals/` across every domain, so the list is not known until every mod's
assets are in. See [Modules](Modules) for a module's own use of it.

## The cross-mod rule

Class-typed methods (`Class<T>()`, `EntityClass<T>()`, `Behavior<T>()`, `EntityBehavior<T>()`) resolve
`T`'s registered key from `T`'s own assembly, not from the definition's domain, so naming a class from
a dependency yields the key that mod actually registered. That resolution needs the dependency's
assembly to declare `[assembly: ExDomain("itsmodid")]` - see **[Getting Started](Getting-Started)**,
"If other mods will name your types", for the full explanation and the failure mode when it is
missing.

## Goldens

A golden test pins a definition's output: the JSON it emits today is committed to the repository,
and the test fails when a later edit changes it, so an accidental change to a shipped block arrives
as a diff to read rather than as a bug report. `DefinitionGoldens` (in `exlib.testing`) is the
oracle every migrated def is checked against: it
collects every def a mod assembly declares (without touching the live `ExDefinitions` registry) and
compares each one's emitted JSON to a committed golden file under `goldens/{domain}/{Location.Path}`.
`EXLIB_WRITE_GOLDENS=1` reblesses every golden in a run; a narrower value
(`EXLIB_WRITE_GOLDENS=iiex/blocktypes/furnace/blastcore`) blesses one file at a time.

For a def migrated from an existing hand-written blocktype, `DefinitionParity` checks the two are
*semantically* the same JSON - numbers compare type-agnostically, `multiblockStructure` and
`fillerOffsets` compare as sets of cells rather than ordered arrays - so a def is free to reorder
what the schema treats as unordered without breaking parity. See **[Testing Harness](Testing-Harness)**
for wiring a parity/golden test project.

## Pitfalls

- A duplicate asset location (the same `{domain}:{blocktypes|itemtypes|recipes}/...` path registered
  twice) replaces silently when both registrations come from the same assembly; when they come from
  different assemblies, `ExDefinitions` logs a Notification naming the location and both assemblies -
  see `ExDefinitions.Logger`.
- A mistyped root key passed to `RootKey` is written into the JSON but never read by the game, since
  only a real blocktype/itemtype key means anything to the object loader; `ExDefinitionModSystem` logs
  a Warning for it at injection, naming the def and the key (`KnownRootKeys`).
- A `MultiblockLayout`'s declared `Origin` must land the glyph marked `Core` on the layout's own
  `(0,0,0)`; `Build()` throws naming the layout, the declared origin and where the anchor actually
  landed when it does not. Unmarked layouts are not checked.
- A missing `[assembly: ExDomain]` breaks only cross-mod `Class<T>()`/`Behavior<T>()` naming - a mod
  nobody else extends works fine without it, but the fallback resolves to the wrong domain and
  `EntityRegistry` logs a Warning naming the assembly.
- The injection (`ExDefinitionModSystem.AssetsLoaded`) is server-only, so a client-only build of a mod
  never sees its own code-first defs - the client always receives them resolved from the server.
