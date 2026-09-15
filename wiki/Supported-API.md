# Supported API

This page is the list of types your mod may build on, and the promise that comes with them. Every public type of `exlib.dll` is on it. A type that is public in the assembly but absent from this page carries `[EditorBrowsable(Never)]`: it is public because the game engine has to see it, not because a mod is meant to call it, and it can change in any release. A test in this repository fails when the assembly and this page disagree in either direction, so the list cannot quietly go stale.

The promise is a deprecation window, not permanence. A public member listed here is never deleted outright: it spends one full release marked `[Obsolete]`, with the attribute naming its replacement, and only then goes. Everything currently in that window is listed under [Obsolete members](#obsolete-members) at the foot of the page. Versions follow the same rule from 0.8.0 on: a minor bump may change a signature on this page, a patch bump does not.

The industry layer is the exception. It ships as a second assembly, `exlib.industry.dll`, beside the framework in the same mod folder and as the `ExpandedLib.Industry` package. Its types are public and meant to be reused, but they are the family's content layer rather than the framework: they change without notice, they are listed separately under [Extending Industry](#extending-industry), and the promise above does not cover them.

Read the page by activity. The sections are named for what you are doing - registering classes, persisting state, wiring a network - rather than by namespace, and each row names its type, says in one line what it is for, and links to the page that teaches it. Follow that link when you want to learn the system; the row itself is the answer when all you need to know is whether something is safe to depend on.

## Starting a mod

Two source-generated classes covering exlib's own assets: the framework's lang keys, and its config values. `ExlibValues.AmbientTemperature` is the kind of thing a machine reads from here. Your mod gets the same pair under its own domain's name once you set `<AssetDomain>` - see [Source Generators](Source-Generators).

| Type | What it is for | Page |
| --- | --- | --- |
| `ExlibLang` | Source-generated typed lang-key class for `assets/exlib/lang/en.json`: one `public const string` member per key. | this page |
| `ExlibValues` | Source-generated static accessor for `ExlibConfig`'s tunables: `ConfigFileName`, `Load(ICoreAPI)` and one read-only property per config value. | this page |

## Registering classes and commands

The game wants every block, item, behaviour, command and preference class handed to it by name, in a list you maintain and can silently fall out of step with. These types replace that list with attributes and one reflection pass. The neighbouring questions that come with registration are here too: registering a recipe type of your own, asking whether another mod is installed before gating content on it, and applying Harmony patches once per process rather than once per load.

| Type | What it is for | Page |
| --- | --- | --- |
| `ExKeyedRegistry<T>` | A process-wide, case-insensitive registry of items keyed by a string code derived from each item. | [Registries](Registries) |
| `ReflectionScan` | Shared reflection helper for the attribute-driven registries (EntityRegistry, CommandRegistry, PreferenceRegistry). | [Registries](Registries) |
| `ExMods` | The three rungs for reacting to another mod being installed: IsLoaded/AtLeast, WhenLoaded, and a world-config flag a JSON patch condition can gate on. | [Registries](Registries) |
| `ExHarmony` | The Harmony bootstrap every reference mod copied by hand: patch an assembly's uncategorised classes once per process (by mod id or by an explicit string id), apply a category only when a required mod is loaded, and unpatch cleanly. | [Registries](Registries) |
| `ExModSystem` | The zero-line registration rung: a `ModSystem` base whose `Start`/`StartServerSide`/`StartClientSide`/`AssetsFinalize` run the config, entity, command and preference registries for that phase and host the mod's own modules through `ExModuleHost`, then an empty overridable hook. | [Registries](Registries) |
| `ExModuleAttribute` | Declares an assembly as a module - an extension driven through the lifecycle of the mod named in `Host`, with a `Requires` order, a shipping `Mod` id and an opt-in Harmony patch. | [Modules](Modules) |
| `IExModule` | The entry point of a module: driven through the phases of its host's lifecycle, in the order `ExModules.For` gives it among the host's other modules. | [Modules](Modules) |
| `ExModuleInfo` | One discovered module: its id, host, shipping mod, requirements, assembly and entry points. | [Modules](Modules) |
| `ExModuleSet` | One host's modules in dependency order, and the errors that excluded any of them. | [Modules](Modules) |
| `ExModules` | Finds every module in the process and, per host, keeps only the ones whose shipping mod is enabled and orders the rest by `Requires`. | [Modules](Modules) |
| `ExModuleHost` | One driver instance's modules: owns their entry-point instances and runs them through the same registries and phases as a main assembly. | [Modules](Modules) |
| `BlockBehaviorRegisterAttribute` | Registers a BlockBehavior class. | [Registries](Registries) |
| `BlockEntityBehaviorRegisterAttribute` | Registers a BlockEntityBehavior class. | [Registries](Registries) |
| `BlockEntityRegisterAttribute` | Registers a BlockEntity class. | [Registries](Registries) |
| `BlockRegisterAttribute` | Registers a Block class. | [Registries](Registries) |
| `CollectibleBehaviorRegisterAttribute` | Registers a CollectibleBehavior class. | [Registries](Registries) |
| `CropBehaviorRegisterAttribute` | Registers a CropBehavior class. | [Registries](Registries) |
| `EntityBehaviorRegisterAttribute` | Registers an EntityBehavior class. | [Registries](Registries) |
| `EntityRegisterAttribute` | Registers an Entity class. | [Registries](Registries) |
| `EntityRegistry` | Reflection-driven class registration for mods built on ExpandedLib. | [Registries](Registries) |
| `ExDomainAttribute` | Declares the asset domain an assembly's registered classes and code-first definitions are keyed under, so KeyFor can resolve a key from a Type alone rather than from the domain of whoever is asking. | [Registries](Registries) |
| `ItemRegisterAttribute` | Registers an Item class. | [Registries](Registries) |
| `RegisterAttribute` | Base for the kind-specific registration attributes. | [Registries](Registries) |
| `CommandRegisterAttribute` | Marks an IExCommand class for automatic registration by RegisterAll; the class supplies an Register body and needs no wiring in the mod system. | [Commands](Commands) |
| `CommandRegistry` | Reflection-driven chat-command registration, the command-side counterpart to EntityRegistry. | [Commands](Commands) |
| `IExCommand` | A self-contained chat command. | [Commands](Commands) |
| `IExSubCommand` | A chat sub-command that attaches itself to an existing top-level command rather than creating its own, so one mod can hang options off another's command (e.g. iiex's `measure` under the library's `.exmod` root) without that command declaring them up front. | [Commands](Commands) |
| `RegistrySubCommand<T>` | A `/exmod <name>` sub-command over one keyed registry: no argument lists every code, a code shows that entry, further words are handed to `Set`. Derive once for a new `ExKeyedRegistry`-backed `/exmod` sub-command. | [Commands](Commands) |
| `SubCommandRegisterAttribute` | Marks an IExSubCommand class for automatic registration by RegisterAll, the sub-command counterpart to CommandRegisterAttribute: the registry resolves the ParentName command and lets the class attach itself. | [Commands](Commands) |
| `ExPreferences` | Store for per-player display preferences shared by every Expanded mod (e.g. iiex's metric/imperial unit system). | [Registries](Registries) |
| `IExPreference` | A single per-player, client-side display preference (e.g. the metric/imperial unit system). | [Registries](Registries) |
| `PreferenceRegisterAttribute` | Marks an IExPreference class for automatic registration by RegisterAll, so a mod system needs no hand-written wiring. | [Registries](Registries) |
| `PreferenceRegistry` | Reflection-driven preference registration for mods built on ExpandedLib, the preference-side counterpart to CommandRegistry. | [Registries](Registries) |
| `ExRecipeCosts` | Rewrites the ingredient quantities and grid output count of grid crafting recipes and right-click-construction (RCC) blocks to a named cost profile from a catalogue, giving a mod a balance toggle such as `cheap` against `normal`. | [Recipe-Costs](Recipe-Costs) |
| `ExRecipeRegistry` | Registers a RecipeRegistryGeneric&lt;T&gt; for a mod's own recipe type through ICoreAPI.RegisterRecipeRegistry, plus the server-side asset load into the list it returned. | [Registries](Registries) |
| `ExRecipeProfiles` | Process-wide registry of RecipeProfiles keyed by mod code, for mods that expose recipe-cost levels (`normal`, `cheap`, ...). | [Recipe-Costs](Recipe-Costs) |
| `RecipeCostEntry` | One managed recipe in a mod's cost catalogue: its kind, the wildcard code that locates it, and one self-contained RecipeProfileCost per cost profile (`normal`, `cheap`, ...). | [Recipe-Costs](Recipe-Costs) |
| `RecipeProfile` | A mod's registration with the shared recipe-cost framework: everything ExRecipeProfiles needs to read, fill, persist and apply that mod's cost catalogue, plus get and set the active level the `/exmod recipes <code> <level>` command flips. | [Recipe-Costs](Recipe-Costs) |
| `RecipeProfileCost` | Everything one cost profile (e.g. `cheap`) changes for a single recipe, self-contained: a profile is the complete cost picture of that recipe at that level. | [Recipe-Costs](Recipe-Costs) |

## Configuring a mod

Gameplay numbers a player can edit, without the string keys and unchecked casts of reading a JSON file by hand. You declare a config class; these types load and save it, hold each value to a declared range in both directions, expose it to the generic `/exmod config` command, reset a tunable whose meaning changed in a later release, and carry the server's copy to clients when the server owns the answer.

| Type | What it is for | Page |
| --- | --- | --- |
| `ConfigSyncPacket` | One mod's config section, carried server to client by `ExConfigSyncModSystem`: the host's `ExportJson` output, addressed by mod id so the receiving side can find the matching registered store. | [Config-System](Config-System) |
| `ExConfig` | Loads every generated config accessor in an assembly by reflection (`LoadAll`), so a `ModSystem` never names its own config types. | [Config-System](Config-System) |
| `ExConfigAccessorAttribute` | Stamped by `ExConfigGenerator` on every accessor class it emits, so `ExConfig.LoadAll` can find it. | [Config-System](Config-System) |
| `ExConfigDocument` | A shared, mod-sectioned config file under `ModConfig` (e.g. `ex_values.json` / `ex_recipes.json`): one file whose top-level keys are mod ids, each holding that mod's config object. | [Config-System](Config-System) |
| `ExConfigEditResult` | The result of an attempted config edit, with enough detail for the command to report it. | [Config-System](Config-System) |
| `ExConfigEditStatus` | Outcome category of an Set attempt. | [Config-System](Config-System) |
| `ExConfigMigration` | Declares that upgrading a mod into or past ToVersion resets the named config properties to their coded defaults, discarding the player's saved tuning for those keys only. | [Config-System](Config-System) |
| `ExConfigProfiles` | Process-wide registry of the config stores mods expose to the generic `/exmod config` command, keyed by mod id. | [Config-System](Config-System) |
| `ExConfigRangeAttribute` | Declares the valid numeric range for a config tunable, enforced by ExConfigRegister<T> both when a player edits it live (`/exmod config` rejects an out-of-range value) and on load (a file edited out of range is reset to the coded default). | [Config-System](Config-System) |
| `ExConfigRegister<T>` | Shared loader and saver for a mod's JSON gameplay tunables. | [Config-System](Config-System) |
| `ExConfigRegisterAttribute` | Marks a config POCO implementing `IExVersionedConfig` for which the `ExConfigGenerator` source generator emits a static accessor class. | [Config-System](Config-System) |
| `ExRecipeProfileAttribute` | Added alongside `[ExConfigRegister]` on a recipe-cost catalogue config to have `ExConfigGenerator` also emit that mod's `RecipeProfile` registration. | [Recipe-Costs](Recipe-Costs) |
| `IExConfigAccess` | Non-generic view over a config store (ExConfigRegister<T>) that the `/exmod config` command uses to list, read and set a mod's tunables by name without knowing the concrete config type. | [Config-System](Config-System) |
| `IExVersionedConfig` | A JSON config POCO that records the mod version it was last written under. | [Config-System](Config-System) |

## Defining blocks, items and recipes in code

Blocks, items and recipes described in C# instead of hand-written JSON under `assets/`, so a renamed variant or a mistyped shape path is a compile error rather than a surprise at world load. The builders here produce exactly what the game's object loader reads, and the layout types turn an ASCII diagram of a structure into the tables that loader expects. [Getting Started](Getting-Started) walks one block through from definition to boot.

| Type | What it is for | Page |
| --- | --- | --- |
| `ConstructionStage` | One construction stage: the shape elements it adds/removes and the materials it requires. | [Code-First-Definitions](Code-First-Definitions) |
| `ConstructionStages` | Typed builder for the `ExRightClickConstructable` behavior's `stages` table. | [Code-First-Definitions](Code-First-Definitions) |
| `ExBlockDef` | A code-first block definition: a fluent builder producing the JObject the vanilla object loader (`ModRegistryObjectTypeLoader`) consumes for a `blocktypes/` asset. | [Code-First-Definitions](Code-First-Definitions) |
| `ExCodes` | Catalogue of exlib's own block codes that multiblock layouts are drawn from. | [Code-First-Definitions](Code-First-Definitions) |
| `ExDefDomainAttribute` | Overrides the domain a definition provider's `Definitions(string)` factory is handed, so one assembly can emit into several domains instead of only its own mod id. | [Code-First-Definitions](Code-First-Definitions) |
| `ExDefinitions` | Process-wide registry of code-first block definitions. | [Code-First-Definitions](Code-First-Definitions) |
| `ExIngredients` | Vanilla crafting ingredients shared across the mods' code-first recipe files. | [Code-First-Definitions](Code-First-Definitions) |
| `ExItemDef` | Code-first item definition, the item-side sibling of ExBlockDef. | [Code-First-Definitions](Code-First-Definitions) |
| `ExRecipeDef` | Code-first recipe file, the recipe-side sibling of ExBlockDef and ExItemDef. | [Code-First-Definitions](Code-First-Definitions) |
| `ExVariantGroup` | One variant group of an ExBlockDef, as rendered into the block's code. | [Code-First-Definitions](Code-First-Definitions) |
| `GridRecipeBuilder` | Fluent builder for one grid (crafting-table) recipe object, the entries of a `recipes/grid/*.json` file. | [Code-First-Definitions](Code-First-Definitions) |
| `IExBlockDefProvider` | Implemented by a block class that authors its own code-first definitions, co-located with the class they configure. | [Code-First-Definitions](Code-First-Definitions) |
| `IExDef` | The common surface of a code-first definition - a block (ExBlockDef), item (ExItemDef) or recipe file (ExRecipeDef): the synthetic-asset AssetLocation the loader keys on, plus the built JSON payload. | [Code-First-Definitions](Code-First-Definitions) |
| `IExDefinitionContributor` | Implemented by a module or main assembly entry point that emits code-first definitions depending on loaded assets; run by ExDefinitions.RunContributors at AssetsLoaded 0.04, right before injection. | [Code-First-Definitions](Code-First-Definitions) |
| `IExItemDefProvider` | The item-side sibling of IExBlockDefProvider: implemented by a class that authors its own code-first item definitions, one ExItemDef per `itemtypes/` asset, co-located with the class they configure. | [Code-First-Definitions](Code-First-Definitions) |
| `IExRecipeDefProvider` | The recipe-side sibling of IExBlockDefProvider and IExItemDefProvider: implemented by a class that authors its own code-first recipe files, one ExRecipeDef per `recipes/{category}/` asset. | [Code-First-Definitions](Code-First-Definitions) |
| `IngredientBuilder` | Fluent builder for one recipe ingredient - a slot in a grid recipe, or a barrel or other recipe ingredient: `{ type, code[, name, allowedVariants, quantity, isTool] }`. | [Code-First-Definitions](Code-First-Definitions) |
| `KnownRootKeys` | The top-level keys the game's object loader reads for a block or item type, taken by reflection from the loader's target types. | [Code-First-Definitions](Code-First-Definitions) |
| `LayoutCell` | One parsed cell of a structure layout: its offset from the principal and the legend symbol. | [Code-First-Definitions](Code-First-Definitions) |
| `MultiblockBuilder` | Typed builder for a block's `multiblockStructure` attribute: the `blockNumbers` map and the `offsets` table. | [Code-First-Definitions](Code-First-Definitions) |
| `MultiblockLayoutBuilder` | Authors a `multiblockStructure` from ASCII layer diagrams: Legend maps characters to block codes, one Layer per Y level draws the build as a top-down grid (grid rules in StructureLayout). | [Code-First-Definitions](Code-First-Definitions) |
| `StructureLayout` | Parses the ASCII layer diagrams the multiblock and filler DSLs are authored with. | [Code-First-Definitions](Code-First-Definitions) |
| `VanillaCodes` | The catalogue of vanilla block codes the mod family's multiblock layouts are drawn from, named once here so that a typo in a Legend is a compile error rather than a `blockNumbers` entry matching no block, which throws nowhere and leaves the structure unable to complete. | [Code-First-Definitions](Code-First-Definitions) |

## Checking content

The guards that catch a dangling block code, a name with no translation, a recipe whose output nothing registers. They already run at load and from `/exmod verify` without you naming them, so the types here are for the other two cases: running the same guards in your own test suite, against a repository tree rather than a live game, and registering a check of your own that runs beside the shipped ones.

| Type | What it is for | Page |
| --- | --- | --- |
| `ICheckSource` | What a check reads: the codes, files and definitions of one or more domains, from the game's assets or from a repository tree. | [Checks](Checks) |
| `AssetCheckSource` | The in-game `ICheckSource`: codes off the live registries, recipes and lang off `ICoreAPI.Assets`, defs off `ExDefinitions`. | [Checks](Checks) |
| `CheckResult` | One check's findings for one domain: its name, the domain and the error lines found. | [Checks](Checks) |
| `ExlibChecks` | Runs every content check against one `ICheckSource` (or the live game) and logs the results. | [Checks](Checks) |
| `ExCheckRegisterAttribute` | Marks a class exposing `static CheckResult Run(ICheckSource, string)` for automatic registration by `ExCheckRegistry.RegisterAll`, so a mod's own content check runs alongside the eight shipped ones. | [Checks](Checks) |
| `ExCheckRegistry` | Reflection-driven registration for a mod's own content checks, the checks-side counterpart to `EntityRegistry`. | [Checks](Checks) |
| `DefinitionCatalogueCheck` | Checks that every code-first block definition actually produced a registered block. | [Checks](Checks) |
| `LateDefinitionCheck` | Checks that every registered block, item or recipe definition made the injection deadline. | [Checks](Checks) |
| `MultiblockCodesCheck` | Checks that every `multiblockStructure` layout cell names a block some mod registers. | [Checks](Checks) |
| `RecipeCodesCheck` | Checks that every grid recipe's block output names a block the mod registers. | [Checks](Checks) |
| `LangCoverageCheck` | Checks that every block code resolves to a name in every locale shipped. | [Checks](Checks) |
| `NetworkNodeContractCheck` | Checks the structural rules a network-node definition and a declared membership must obey. | [Checks](Checks) |
| `PinnedNetworkNodesCheck` | Checks that no shipped layout pins the orientation of a network node. | [Checks](Checks) |
| `CodePrefixCollisionCheck` | Checks that no block's base code is a proper prefix of another's at a `-` boundary. | [Checks](Checks) |

## Writing a block entity and persisting its state

A block entity is the object the game attaches to one placed block, ticks, and saves with the chunk around it. Keeping a field across a reload normally means writing it into a tree attribute and reading it back, with the key spelled once in each direction and no complaint when you get one half wrong. The bases and the `[Persist]` attribute here do it from the declaration alone. The construction types in the same section cover a block the player raises in stages from materials, which has state of its own to keep.

| Type | What it is for | Page |
| --- | --- | --- |
| `ExBlockEntity` | Block entity base that persists whatever it declares. | [Block-Entities](Block-Entities) |
| `ExBlockEntityBehavior` | Block-entity behaviour base that persists whatever it declares - the `ExBlockEntity` convenience for a block entity whose base slot is already spent. | [Block-Entities](Block-Entities) |
| `ExBlockEntityContainer` | Block entity base that persists whatever it declares, on top of vanilla's own inventory - the `ExBlockEntity` convenience for a block entity based on `BlockEntityContainer`. | [Block-Entities](Block-Entities) |
| `ExBlockState` | A block entity's persisted fields, declared once and read and written from that one declaration. | [Block-Entities](Block-Entities) |
| `PersistAttribute` | Marks a field or auto-property of a block entity as saved state, with no `DeclareState` entry needed. | [Block-Entities](Block-Entities) |
| `IPersistable` | A value that writes itself into a sub-tree; a `[Persist]` member of this type is stored under its key as a nested tree. | [Block-Entities](Block-Entities) |
| `PersistScan` | Finds every `[Persist]` member of a block entity's type by reflection and declares it into an `ExBlockState`. | [Block-Entities](Block-Entities) |
| `BlockBehaviorExOrientable` | Places a block wearing the `side` variant the player's look implies; the family's replacement for vanilla's `HorizontalOrientable`. | this page |
| `ConstructedAnimator` | Owns the animator and ExRightClickConstructable lifecycle shared by constructed, animator-rendered mega-blocks (boiler, engine, converter vessel, burdenmaker). | [Construction](Construction) |
| `ExConstructionIngredient` | A required material for a construction stage; port of vanilla `ConstructionIngredient` (1.20 and 1.21 only). | [Construction](Construction) |
| `ExConstructionStage` | One construction stage: shape elements it adds/removes and the materials it needs; port of vanilla `ConstructionStage` (1.20 and 1.21 only). | [Construction](Construction) |
| `ExRccSettings` | Player-tunable settings for the right-click construction system, supplied by each mod from its own config. | [Construction](Construction) |
| `ExRightClickConstructable` | exlib-owned right-click construction behavior referenced by the mega-blocks (engines, boilers, bessemer converter) under the JSON behavior name `ExRightClickConstructable`. | [Construction](Construction) |
| `ExRightClickConstruction` | Port of vanilla `RightClickConstruction`: the per-block construction state and logic (1.20 and 1.21 only). | [Construction](Construction) |

## Renaming and healing blocks

Renaming or removing a block breaks every save that holds it: the world comes back with a hole where the old code was, or with an item stack nothing can resolve. Declare the old-to-new mapping with these types and the migrator rewrites the world on load. The healer covers the other half, a block whose block entity was lost to a failed deserialization or a desync and which is left inert, often unbreakable and impossible to build over.

| Type | What it is for | Page |
| --- | --- | --- |
| `BlockMigrationModSystem` | Server-side world migrator for renamed or re-variantted blocks and items, and a purger for codes a mod drops. | [Migrations-and-Healing](Migrations-and-Healing) |
| `BlockMigrationModSystem.DeclaredRemap` | One declared `(oldCode, newCode)` block remap, before resolution against the world. | [Migrations-and-Healing](Migrations-and-Healing) |
| `CodeRelocation` | Builds remaps for a block that kept its shape but changed identity - a domain move, a rename or both - pairing an explicitly named historical base code with a live one and carrying every variant suffix across unchanged. | [Migrations-and-Healing](Migrations-and-Healing) |
| `IBlockCodeMigration` | Declares how block codes from an older version of a mod are rewritten to their current equivalents, for a renamed or re-varianted block. | [Migrations-and-Healing](Migrations-and-Healing) |
| `IBlockEntityMigration` | Optional companion to IBlockCodeMigration, implemented on the same class when the block carries block-entity state that must survive the swap (inventory, progress). | [Migrations-and-Healing](Migrations-and-Healing) |
| `IBlockRemoval` | Declares block codes purged from the world: deleted where they sit, and stripped from any container or player inventory holding them as an item stack. | [Migrations-and-Healing](Migrations-and-Healing) |
| `IItemCodeMigration` | The item counterpart of IBlockCodeMigration: declares how item codes from an older version of a mod are rewritten to their current equivalents, for an item that was renamed or moved domain. | [Migrations-and-Healing](Migrations-and-Healing) |
| `BlockEntityHealModSystem` | Server-side self-healer for orphaned block entities: a block still placed in the world whose BlockEntity was lost to a throwing deserialization or a desync, leaving it inert - no interaction, often unbreakable, impossible to build over. | [Migrations-and-Healing](Migrations-and-Healing) |

## Building a structure

A machine larger than one block needs its cells checked before it may run, an outline the player can see while building it, and collision on every cell rather than only the one holding the block entity. These types carry the layout, authored as ASCII diagrams rather than offset tables, the completion monitoring behind it, and the invisible filler blocks that reserve the rest of the footprint and can host a behaviour such as a power port.

| Type | What it is for | Page |
| --- | --- | --- |
| `BlockBehaviorMultiblockStructure` | Centralises the multiblock build-outline projection: Ctrl+Shift+right-click toggles the hologram of missing or incorrect blocks (routed to Interact) and contributes the help line. | [Multiblock-Structures](Multiblock-Structures) |
| `BlockEntityMultiblock` | Concrete BlockEntityMultiblockStructure for a JSON-only mega-block: orientation, completion monitoring and the incomplete/complete messages with no C# subclass. | [Multiblock-Structures](Multiblock-Structures) |
| `BlockEntityMultiblockMachine` | A multiblock that also runs a production process: the hand-built pattern of BlockEntityMultiblockStructure plus a server-side tick, gated by the readiness that form publishes and started and stopped by its monitor tick. | [Multiblock-Structures](Multiblock-Structures) |
| `BlockEntityMultiblockStructure` | Base block entity for the mod's multiblock machines (blast furnace, cowper stove, bessemer control). | [Multiblock-Structures](Multiblock-Structures) |
| `BlockEntityMultiblockStructure.MissingCell` | One unsatisfied footprint cell: what stands there, the wanted code, and the outward face it must open to when the mismatch is only orientation. | [Multiblock-Structures](Multiblock-Structures) |
| `BlockEntityStructureFiller` | Block entity for an invisible structure-filler block. | [Multiblock-Structures](Multiblock-Structures) |
| `BlockFilledMegastructure` | Shared base for a mega-block that occupies one grid cell but renders across a multi-cell footprint reserved with invisible BlockStructureFiller cells (real per-cell collision, with interaction/break/info rerouted to the principal). | [Multiblock-Structures](Multiblock-Structures) |
| `BlockStructureFiller` | Invisible, solid placeholder that fills the grid cells a mega-block visually occupies (see StructureFillers). | [Multiblock-Structures](Multiblock-Structures) |
| `CellGrid` | Turns rows of symbols into a cell list: one instance draws one plane (a floor level, an X slice or a Z face), and every layout DSL (multiblock, filler, the testing harness's scene diagrams) is a derivation over it. | [Multiblock-Structures](Multiblock-Structures) |
| `CellRole` | What a multiblock layout cell is for, as opposed to what block may occupy it: the layout records the code per cell, a role records the purpose, so a machine can ask its own drawing where its tuyeres are. | [Multiblock-Structures](Multiblock-Structures) |
| `CellRoles` | Facts about CellRole that both the layout builder and its consumers read. | [Multiblock-Structures](Multiblock-Structures) |
| `DuplicatePolicy` | What SymbolLegend does when a symbol is mapped twice: Throw for a code-first layout, Replace for a filler footprint or a test scene. | [Multiblock-Structures](Multiblock-Structures) |
| `FillerBehavior` | A single behaviour declared on a `fillerOffsets` cell: the registered class Code (e.g. `exlib.BEBehaviorMPFillerPort`), an optional north-orientation ConnectorFace (rotated into the placed orientation by FootprintCells) and optional Properties passed through to the behaviour. | [Multiblock-Structures](Multiblock-Structures) |
| `FillerBehaviorSpec` | One behaviour hosted by a footprint filler cell: a block-entity behaviour code plus, optionally, the block face it exposes a connector on and a Properties config blob. | [Multiblock-Structures](Multiblock-Structures) |
| `FillerCell` | A resolved world-space filler cell carrying its per-cell attachment flag and, when the cell is only partially filled, its collision/selection boxes already rotated into the placed orientation. | [Multiblock-Structures](Multiblock-Structures) |
| `FillerCellSpec` | One north-orientation footprint cell for a mega-block, authored in C#: the offset from the principal, whether other blocks may attach to the filler placed there, and any per-cell hosted FillerBehaviorSpec behaviours (MP/pipe ports). | [Multiblock-Structures](Multiblock-Structures) |
| `FillerLayoutBuilder` | Authors a mega-block `fillerOffsets` footprint from ASCII diagrams (grid rules in Layout and StructureLayout). | [Multiblock-Structures](Multiblock-Structures) |
| `FillerOffset` | A single structure-local filler cell as declared in the `fillerOffsets` JSON array: the offset from the principal (north orientation), whether other blocks may attach to the filler placed there, and an optional set of per-cell collision/selection boxes (north orientation) for footprint cells the mega-block only partially fills, such as a slab. | [Multiblock-Structures](Multiblock-Structures) |
| `GridOptions` | How a CellGrid reads its text: whether a space advances the column, which glyph means empty, and an optional anchor glyph. | [Multiblock-Structures](Multiblock-Structures) |
| `GridPlane` | Which plane a CellGrid draws: rows and columns map to two world axes, the depth argument to the third. | [Multiblock-Structures](Multiblock-Structures) |
| `IFillerHost` | A mega-block whose footprint cells are declared by its `fillerOffsets` attribute node. | [Multiblock-Structures](Multiblock-Structures) |
| `IFillerHostedBehavior` | A behaviour a mega-block can host on one of its footprint cells (see StructureFillers). | [Multiblock-Structures](Multiblock-Structures) |
| `IFillerInteractionTarget` | Optional contract for a principal (controller) block whose interactions depend on which cell of its mega-block footprint was clicked, not merely that some footprint cell was clicked. | [Multiblock-Structures](Multiblock-Structures) |
| `IMultiblockComponent` | A functional component of a multiblock machine (a molten tap, a charging hopper, a tuyere) whose own block entity is not the anchor but belongs to one. | [Multiblock-Structures](Multiblock-Structures) |
| `JsonMultiblockLayout` | Resolves a block's `multiblockLayout` ASCII grid into the same `multiblockStructure`/`fillerOffsets` a code-first definition would have emitted. | [Multiblock-Structures](Multiblock-Structures) |
| `MultiblockAnchorLink<T>` | A throttled resolver a functional component (a molten tap, a charging hopper, a tuyere) keeps to find and keep reading the multiblock anchor it belongs to. | [Multiblock-Structures](Multiblock-Structures) |
| `MultiblockCellRoles` | Reads the `multiblockRoles` attribute a code-first layout emits: CellRole to the authored (north-frame) offsets of the cells carrying that role. | [Multiblock-Structures](Multiblock-Structures) |
| `MultiblockConnectors` | Reads the `multiblockConnectors` attribute a code-first layout emits: for each authored (north-frame) offset, the outward faces that cell's occupant must expose a network connector on. | [Multiblock-Structures](Multiblock-Structures) |
| `MultiblockFacings` | Rotates the facing of a multiblock layout's oriented parts, so a structure can require a slab, stairs or door to be placed the right way round rather than merely be present. | [Multiblock-Structures](Multiblock-Structures) |
| `StructureFillers` | Helpers for the invisible mega-block footprint system. | [Multiblock-Structures](Multiblock-Structures) |
| `StructureFootprint` | Computes mega-block footprints (the `fillerOffsets` tables) from a compact description instead of listing every cell by hand. | [Multiblock-Structures](Multiblock-Structures) |
| `SymbolLegend<T>` | Maps a grid's symbols to whatever a caller resolves them into, with one duplicate-mapping rule and the "declared but never drawn" check every layout DSL needs. | [Multiblock-Structures](Multiblock-Structures) |

## Running a machine

A machine that works over time needs a server-side tick on a fixed interval, a gate saying whether it may run at all, and a decision about the game time it spent unloaded while the player was away. These types provide that lifecycle and leave you the work itself. The station types add the window a player loads and unloads the machine through, with slots that accept only what they should and an output slot nothing can be dropped into.

| Type | What it is for | Page |
| --- | --- | --- |
| `BEBehaviorProductionMachine` | The periodic work a machine does: a server-side tick on a fixed interval, gated each time by CanRunProduction, with a bounded `dt` and an opt-in replay of the game time the machine spent unloaded. | [Production-Machines](Production-Machines) |
| `BlockEntityMachineStation` | Base block entity for a machine the player works through a window: a container whose slots are declared as MachineSlotSpecs, plus the open/close handshake that keeps the server inventory in step with what the player sees. | [Production-Machines](Production-Machines) |
| `BlockEntityProductionMachine` | Base for a block entity whose whole reason to exist is periodic server-side production work - the standalone steam engines and their sub-machines. | [Production-Machines](Production-Machines) |
| `IProductionReadiness` | One answer to whether a machine may run its production process, published by whatever knows it: a multiblock's completed pattern, an RCC megablock's finished construction stages, a sub-machine's resolved master. | [Production-Machines](Production-Machines) |
| `ItemSlotMachineInput` | An input slot that accepts only stacks matching its predicate; a null predicate takes anything. | [Production-Machines](Production-Machines) |
| `ItemSlotMachineOutput` | A take-only output slot, so a finished piece cannot be overwritten by hand. | [Production-Machines](Production-Machines) |
| `MachinePorts` | Network-port access shared by every fixed machine that reads or feeds a block network through a connector face (boiler, engine, sub-machines, pumps, intake, converter, cowper). | [Production-Machines](Production-Machines) |
| `MachineSlotSpec` | What one slot of a machine station accepts. | [Production-Machines](Production-Machines) |
| `MachineStationInventory` | A machine station's inventory, built from the station's MachineSlotSpec array. | [Production-Machines](Production-Machines) |
| `ProductionProcess` | Drives the production process a machine carries, without naming the class that carries it. | [Production-Machines](Production-Machines) |
| `ProductionReadiness` | Reads the readiness a machine publishes. | [Production-Machines](Production-Machines) |

## Wiring a network

Pipes, wires and canals are a graph: which blocks join, what happens when a run is cut in two or two runs meet, how a node knows its orientation, what a chunk unload does to the run. One manager owns all of it. The types here are the two halves you meet - the graph model, and the block entities and blocks that sit in it - so your node only declares what it connects to and what flows.

| Type | What it is for | Page |
| --- | --- | --- |
| `BlockNetwork` | Abstract base for all live block-network instances. | [Block-Networks](Block-Networks) |
| `INetworkConnector` | A port: a face another network may couple to, on a block that is not itself a graph member - the lancashire boiler's water intake is one. | [Block-Networks](Block-Networks) |
| `INetworkMember` | One cell's participation in a block network, as the graph walk sees it. | [Block-Networks](Block-Networks) |
| `INetworkNode` | Base interface for block entities that participate in a block network (gas pipes, molten canals). | [Block-Networks](Block-Networks) |
| `BEBehaviorNetworkMember` | One network membership held by a block entity: which network it joins and which faces it couples on. | [Block-Networks](Block-Networks) |
| `BlockEntityNetworkNode` | Base block entity for any block that is a node in a BlockNetwork (gas pipes, molten canals). | [Block-Networks](Block-Networks) |
| `BlockNetworkModSystem` | Graph manager for all block networks: node add/remove, BFS fracture detection, and per-tick dispatch. | [Block-Networks](Block-Networks) |
| `BlockNetworkNode` | Base class for `Block` types that auto-orient from the surrounding blocks of the same network to form a connected run. | [Block-Networks](Block-Networks) |
| `NetworkMembership` | Finds which network a cell belongs to. | [Block-Networks](Block-Networks) |

## Loading a catalogue

A catalogue is data a mod ships as JSON that another mod can extend without touching code: process routes, material roles, pipe and canal media, storage occupancy. The loaders here read every domain's files, merge them, invoke the code contributions JSON cannot express, and report which asset failed rather than swallowing it. Reach for them when your own content is a table other mods should be able to add rows to - [Extending Processes](Extending-Processes) works one through end to end.

| Type | What it is for | Page |
| --- | --- | --- |
| `AssetCatalogueLoader` | Reads every domain's `config/<mine>/*.json` under a path into a typed object and tells the caller what failed - the plain primitive `ContributedCatalogueLoader<TSet, TRegistry>` builds on. | [Extending-Processes](Extending-Processes) |
| `AssetCatalogueLoader.ReadResult<T>` | One path's read: the parsed items, their sources, the file count, and one error per asset that did not parse. | [Extending-Processes](Extending-Processes) |
| `CatalogueContributors` | Code contributions to one catalogue, re-invoked after every load so a C# entry survives the clear that precedes each AssetsFinalize read. | [Extending-Processes](Extending-Processes) |
| `CatalogueLoadReport` | One catalogue's load outcome: files read, entries accepted, and the errors, each naming its asset. | [Extending-Processes](Extending-Processes) |
| `ContributedCatalogueLoader<TSet, TRegistry>` | The base every hand-parsed catalogue with C# contributors derives from: read, audit keys, parse once, merge, invoke contributors, report. | [Extending-Processes](Extending-Processes) |
| `ItemDie` | A die: the swappable tooling a heading, nail or rivet bench works with, carrying the job it does in a `machinejob` attribute. | [Extending-Processes](Extending-Processes) |
| `MachineTool` | The cutting tooling a machine is fitted with: a consumable carrying a hardness tier and nothing else. | [Extending-Processes](Extending-Processes) |
| `ProcessExtensions` | The public C# route into the process registries, for a mod that computes a spec at load or is happy to take a hard dependency on exlib. | [Extending-Processes](Extending-Processes) |
| `ProcessItemEmitter` | Builds an ExItemDef for every stopping point in the stage catalogue, injected through the same path `MetalFamilyEmitter` uses for metal families. | [Extending-Processes](Extending-Processes) |
| `ProcessItemRenames` | Rewrites held stacks of a stage product that has been renamed, from the `formerCodes` the stage declares. | [Extending-Processes](Extending-Processes) |
| `ProcessJob` | One terminal job: a piece goes in, one kind of thing comes out, and Count of them do. | [Extending-Processes](Extending-Processes) |
| `ProcessJobLoader` | Reads the terminal-job catalogue - every domain's `config/processjobs/*.json` - and populates ProcessJobRegistry from it. | [Extending-Processes](Extending-Processes) |
| `ProcessJobRegistry` | The merged catalogue of every terminal job, keyed by machine. | [Extending-Processes](Extending-Processes) |
| `ProcessJobSet` | Every terminal job one machine can do, as a mod declares them. | [Extending-Processes](Extending-Processes) |
| `ProcessRoute` | One stock family's route of ProcessStages - every state that family can be worked into, across every machine family that works it. | [Extending-Processes](Extending-Processes) |
| `ProcessRouteLoader` | Reads the stage catalogue - every domain's `config/processroutes/*.json` - and populates ProcessRouteRegistry from it. | [Extending-Processes](Extending-Processes) |
| `ProcessRouteRegistry` | The merged catalogue of every ProcessRoute in the world, keyed by stock family. | [Extending-Processes](Extending-Processes) |
| `ProcessStage` | One state of a piece part-way through a sequence process: the thickness it sits at, the shape element that draws it, the machine families that accept it, and the item code it becomes when it is a stopping point. | [Extending-Processes](Extending-Processes) |
| `SpecSchema` | The versioning contract every spec attribute carries. | [Extending-Processes](Extending-Processes) |
| `MaterialRoleCatalogue` | The `config/materialroles.json` file shape: one `materials` array of role entries, like LiquidCatalogue. | [Extending-Processes](Extending-Processes) |
| `MaterialRoleDef` | One material-role assignment: the data a machine reads to classify a flux, fuel, ore, scrap or charge item. | [Extending-Processes](Extending-Processes) |
| `MaterialRoleLoader` | Populates MaterialRoleRegistry at `AssetsFinalize`: clear, overlay every domain's `config/materialroles.json`, then invoke the registered code contributors that cover the mod-gated registrations JSON cannot express. | [Extending-Processes](Extending-Processes) |
| `MaterialRoleRegistry` | Process-wide catalogue of material-role assignments (MaterialRoleDef), consulted to tell flux, fuel, ore, scrap and charge apart. | [Extending-Processes](Extending-Processes) |
| `ExLiquids` | Process-wide catalogue of pipe and canal media (LiquidDef) and the single IMediumTaxonomy the pipe network reads. | [Block-Networks](Block-Networks) |
| `IMediumTaxonomy` | Medium policy the pipe network consults instead of hardcoded medium strings, injected like IPipeVentStrategy. | [Block-Networks](Block-Networks) |
| `LiquidCatalogue` | The `config/liquids.json` file shape: one wrapper object carrying the medium entries. | [Block-Networks](Block-Networks) |
| `LiquidCatalogueLoader` | Populates ExLiquids at `AssetsFinalize`: re-seed the built-ins, overlay every domain's `config/liquids.json`, invoke the code contributors. | [Block-Networks](Block-Networks) |
| `LiquidDef` | One pipe or canal medium descriptor: the data the network's compatibility, priority and phase-change logic reads in place of hardcoded medium strings. | [Block-Networks](Block-Networks) |
| `LiquidPhase` | Phase of a pipe/canal medium: gases share one mixable family, while a liquid mixes only with the same liquid. | [Block-Networks](Block-Networks) |
| `BayLayout` | A row of storage cells filled by occupants of declared length: three one-cell stacks, a two-cell stack beside a one-cell one, or a single three-cell stack all fill a row of three. | [Registries](Registries) |
| `BayOccupancy` | How many bay cells a stack of one item occupies. | [Registries](Registries) |
| `BayOccupancyLoader` | Reads the bay-occupancy catalogue - every domain's `config/bayoccupancy/*.json` - and populates BayOccupancyRegistry from it. | [Registries](Registries) |
| `BayOccupancyRegistry` | The merged catalogue of what every item occupies, keyed by store. | [Registries](Registries) |
| `BayOccupancySet` | One file's worth of occupancy rules, plus the store they are for. | [Registries](Registries) |
| `BayRun` | One occupant of a bay row: the cell its run starts at and how many cells it spans. | [Registries](Registries) |

## Helpers

The small things a machine ends up writing for itself: horizontal rotation math, a side check, particles and sounds, counting what a player is carrying, formatting a measurement for the look-at HUD, caching a tesselated mesh, hiding content behind a config gate. None of it is required to use exlib. Each entry is a few lines you do not have to get right a second time.

| Type | What it is for | Page |
| --- | --- | --- |
| `ExBlockAccess` | Typed block-entity lookups over `IBlockAccessor`: at a position, one step to a neighbour, or every matching neighbour around a position. | [Helpers-and-Renderers](Helpers-and-Renderers) |
| `ExChunkData` | Typed per-chunk side-band data over `IWorldChunk.GetModdata`/`SetModdata`, keyed `{domain}:{key}`. | [Migrations-and-Healing](Migrations-and-Healing) |
| `ExContentGate` | Config-gated "disable this content" toggle: hides registered blocks and items from the creative inventory and handbook. | [Helpers-and-Renderers](Helpers-and-Renderers) |
| `ExCreativeTabs` | Registers a mod's custom creative-inventory tab with the internal vanilla tab list. | [Helpers-and-Renderers](Helpers-and-Renderers) |
| `ExHighlightSlots` | Process-wide handout of distinct `world.HighlightBlocks` slot ids, so two features never collide by picking the same literal by hand. | [Helpers-and-Renderers](Helpers-and-Renderers) |
| `ExInfo` | `GetBlockInfo` line-builders: a plain `Lang.Get` line, the same guarded by a condition, and a measured value folded through `ExMeasure`. | [Helpers-and-Renderers](Helpers-and-Renderers) |
| `ExInteraction` | Builds an `Interaction` from an interact handler's own arguments. | [Helpers-and-Renderers](Helpers-and-Renderers) |
| `Interaction` | What a click carried - held stack, tool, sneak, face, side - without deciding which side acts on it. | [Helpers-and-Renderers](Helpers-and-Renderers) |
| `ExInventory` | Shared inventory queries for machine costs: counting and consuming player items that match a predicate, over either the whole inventory or the hotbar only. | [Helpers-and-Renderers](Helpers-and-Renderers) |
| `ExItems` | Shared item-stack lookups for interaction help. | [Helpers-and-Renderers](Helpers-and-Renderers) |
| `ExMeasure` | Display-unit formatting shared across the mods. | [Helpers-and-Renderers](Helpers-and-Renderers) |
| `ExMesh` | Small shared helpers for hand-tesselated block meshes. | [Helpers-and-Renderers](Helpers-and-Renderers) |
| `ExMeshCache` | Shared cache for tesselated block-entity meshes, keyed by everything that changes the mesh. | [Helpers-and-Renderers](Helpers-and-Renderers) |
| `ExOrientation` | Horizontal rotation math shared by the mod family's oriented blocks. | [Helpers-and-Renderers](Helpers-and-Renderers) |
| `ExOrientation.SegmentedCode` | A block code's path split on `-`, for code that indexes or rewrites one dash-segment at a time and rejoins the rest unchanged. | [Helpers-and-Renderers](Helpers-and-Renderers) |
| `ExOrientationScheme` | One declared set of orientation tokens, plus the rotation rule that set implies. | [Helpers-and-Renderers](Helpers-and-Renderers) |
| `ExOrientations` | The named orientation schemes every mod declares against, so a block references a set instead of listing states inline. | [Helpers-and-Renderers](Helpers-and-Renderers) |
| `ExShapeElements` | Prunes a loaded Shape to a chosen set of element paths - the mesh-side counterpart of a blocktype's `selectiveElements`, for a block entity that decides which parts to draw at runtime (a hearth showing only the pigs actually charged on it). | [Helpers-and-Renderers](Helpers-and-Renderers) |
| `ExSide` | The `Api.Side == EnumAppSide.X` / `World.Side == EnumAppSide.X` check every machine writes by hand. | [Helpers-and-Renderers](Helpers-and-Renderers) |
| `ExTree` | Helpers for reading values a block entity persisted into its attribute tree. | [Helpers-and-Renderers](Helpers-and-Renderers) |
| `ExWorldData` | Typed per-world side-band data over `ISaveGame.GetData`/`StoreData`, keyed `{domain}:{key}`. | [Migrations-and-Healing](Migrations-and-Healing) |
| `GameTime` | Advances a machine on game time (the world calendar) rather than real time. | [Helpers-and-Renderers](Helpers-and-Renderers) |
| `GraceTimer` | Accumulator for the "hold a condition for N seconds, then fire once" idiom used by boiler over-pressure and choke, engine over-pressure and pipe burst grace. | [Helpers-and-Renderers](Helpers-and-Renderers) |
| `MeasurementSystem` | The unit system used when formatting measurements for the look-at HUD / block info / handbook. | [Helpers-and-Renderers](Helpers-and-Renderers) |
| `SurfaceRenderer` | Base for renderers that draw a flat, textured horizontal surface (a liquid line) inside a block: boiler water, molten metal in canals, taps and molds. | [Helpers-and-Renderers](Helpers-and-Renderers) |
| `ToggleAnimator` | Rendering helper for a block entity that animates through BEBehaviorAnimatable and BlockEntityAnimationUtil but is not raised via RightClickConstructable (`ExpandedLib.Blocks.ConstructedAnimator` is the constructed equivalent). | [Helpers-and-Renderers](Helpers-and-Renderers) |

## Supporting older game versions

exlib targets 1.22, and the family also builds and runs on 1.21 and 1.20 from this one source tree. If you target 1.22 alone you can ignore this section.

Compiled only for the 1.20 and 1.21 targets: shims that let code written against the 1.22 API build
unchanged there (`mods/LegacyUsings.cs` brings them into scope on those targets). They are absent from
the 1.22 assembly; the guard that checks this page resolves them from source instead of reflection on
that target.

| Type | What it is for | Page |
| --- | --- | --- |
| `LegacyApi` | C# 14 extension members that fill in API members missing from the pre-1.22 game surface, so mod code written against 1.22 compiles unchanged on the legacy target frameworks. | [Getting Started](Getting-Started) |
| `LegacyApi120` | Shims for members that arrived in 1.21 (player-aware drop stacks, the tool-mold mesh angle), compiled for 1.20 only. | [Getting Started](Getting-Started) |
| `LegacyAnimUtil` | The animation-utility overloads the 1.22 API gained, such as the five-argument `CreateMesh`, for the earlier game versions. | [Getting Started](Getting-Started) |
| `LegacyLinq` | Polyfills of LINQ members added after net7.0, such as `Enumerable.Index()`. | [Getting Started](Getting-Started) |

## Extending Industry

The systems the family mods are made of, in a separate assembly and package, `exlib.industry.dll` / `ExpandedLib.Industry`, referenced alongside `ExpandedLib` by a mod that wants them: gas and liquid pipes, molten metal, mechanical-power ports for mega-blocks, metal descriptors and a process heat balance. Reference it when your blocks carry gas, liquid, metal or shaft power, and leave it out otherwise.

It is public and reusable, but it is the family's content layer rather than the framework: it changes without notice, and the one-release deprecation promise above does not cover it.

### `ExpandedLib.Industry.Pipes`

Gas and liquid runs: the pipe block and block entity you derive from, and the contracts a pipe opts into - burstable under pressure, throughput-limited, drawn on by a chimney.

| Type | What it is for | Page |
| --- | --- | --- |
| `BlockEntityPipe` | Block entity for all pipe blocks. | [Block-Networks](Block-Networks) |
| `BlockEntityPipePassthrough` | Block entity for the pipe passthrough: a plain pipe node, used as a gas consumer by adjacent machines. | [Block-Networks](Block-Networks) |
| `BlockPipe` | The base pipe block: a self-orienting node of the unified "pipe" network. | [Block-Networks](Block-Networks) |
| `BlockPipePassthrough` | Passthrough pipe: carries gas straight through a wall. | [Block-Networks](Block-Networks) |
| `ChimneyVent` | Pipe-network vent strategy: a vanilla chimney capping the top connector of an IChimneyVentable node draws gas out of the run and puffs smoke. | [Block-Networks](Block-Networks) |
| `IBurstablePipe` | A pipe-network block that can fail under over-pressure. | [Block-Networks](Block-Networks) |
| `IChimneyVentable` | Marker for a pipe-network node whose open top connector may be drawn through by a vanilla chimney, which acts as a sink rather than a leak. | [Block-Networks](Block-Networks) |
| `IPipeNode` | A block entity that participates in the pipe network as an addressable node: gas or liquid can be injected (TryProduce) or withdrawn (TryConsume) at its position, and the network's medium, temperature, pressure and volume can be read. | [Block-Networks](Block-Networks) |
| `IPipeVentStrategy` | Optional per-network strategy for gas vents: open connectors that draw gas away as a sink rather than leaking it, such as a chimney capping a vertical pipe. | [Block-Networks](Block-Networks) |
| `IThroughputLimitedPipe` | A pipe-network block that limits how much can move through a run per second. | [Block-Networks](Block-Networks) |
| `PipeNetwork` | Concrete BlockNetwork for the pipe system. | [Block-Networks](Block-Networks) |
| `PipeNetworkState` | Live state of a pipe run. | [Block-Networks](Block-Networks) |

### `ExpandedLib.Industry.Molten`

Molten metal as a medium that flows: the per-cell store a block entity holds, the canal network that moves it between cells, and the chisel interaction that recovers it once it has set.

| Type | What it is for | Page |
| --- | --- | --- |
| `BEBehaviorMoltenCell` | One molten-metal cell as a composable block-entity behaviour: holds a single cell's metal (amount, type, temperature) and the per-cell operations the molten system drives, so any block entity can be an IMoltenCell by composition, including a mega-block footprint cell hosted through IFillerHostedBehavior. | [Block-Networks](Block-Networks) |
| `ChiselOutcome` | What a chisel and hammer click resolved to on an IChiselableMolten holder. | [Block-Networks](Block-Networks) |
| `IChiselableMolten` | A block entity holding molten metal that, once solidified and cooled, can be chipped out with a chisel and hammer instead of breaking the whole block (canal cells, the molten barrel, the bessemer charge). | [Block-Networks](Block-Networks) |
| `IMoltenCell` | The per-cell contract the MoltenNetwork flow driver needs from each molten-canal block entity: stored metal (amount, type, temperature), capacity, the two flow-blocking latches, and two capability flags that stand in for concrete block-entity type checks - a flow source (the canal start, where the distance-from-start BFS roots) and a drain fitting (tap, mold pedestal) that accepts the final sub-minimum dregs so a run can empty completely. | [Block-Networks](Block-Networks) |
| `MoltenCellHost` | Addressing molten cells on a block entity that hosts more than one. | [Block-Networks](Block-Networks) |
| `MoltenCharge` | A body of molten metal held inside a machine or fitting: a temperature-tracked ItemStack carrier identifying the metal and its heat, plus a unit count. | [Block-Networks](Block-Networks) |
| `MoltenChisel` | The chip-solidified-metal-out interaction shared by every IChiselableMolten holder (canal cells, the molten barrel, the bessemer vessel): tool gating, not-ready feedback, the recovered drop, tool wear and sound. | [Block-Networks](Block-Networks) |
| `MoltenContents` | Round-trips the metal a carried barrel or tool-mold item holds through the stack's `blockEntityAttributes` tree. | [Block-Networks](Block-Networks) |
| `MoltenMetal` | Single source of truth for treating an ItemStack as a carrier of molten metal: creating the temperature-tracked stack, reading/writing temperature, classifying thermal state, the incandescent block-light scale, and player-facing metal/state formatting. | [Block-Networks](Block-Networks) |
| `MoltenNetwork` | Concrete BlockNetwork for the molten-canal system. | [Block-Networks](Block-Networks) |
| `MoltenState` | Coarse thermal state of a metal stack relative to its melting point. | [Block-Networks](Block-Networks) |

### `ExpandedLib.Industry.MechanicalPower`

The shaft-power side: the port a mega-block hosts on one footprint cell so an axle has something to couple to, and the producer, consumer and storage roles a node takes on an energy run.

| Type | What it is for | Page |
| --- | --- | --- |
| `BEBehaviorMPFillerPort` | A minimal mechanical-power node a mega-block hosts on one of its invisible footprint cells (see StructureFillers / IFillerHostedBehavior), giving the MP network a participant at the cell where an axle couples - the principal block, two cells away, cannot accept power at that face. | [Block-Networks](Block-Networks) |
| `BEBehaviorMPSubmachineBase` | Shared base for a mechanical-power node whose block renders a static body plus a vanilla-spun axle (the Bessemer transmission, the engine MP generator). | [Block-Networks](Block-Networks) |
| `IMpEnergyConsumer` | A node that loads the run: a heavy machine such as a rolling pass, hammer or crusher. | [Block-Networks](Block-Networks) |
| `IMpEnergyDirection` | A node that knows which way the run turns. | [Block-Networks](Block-Networks) |
| `IMpEnergyProducer` | A node that drives a mechanical-energy run, such as an engine generator or the flywheel's bridge to the vanilla MP network. | [Block-Networks](Block-Networks) |
| `IMpEnergyStorage` | A node that stores energy: a flywheel, or the small inherent inertia of a cast-iron shaft or gear. | [Block-Networks](Block-Networks) |
| `MPAnim` | Phase-lock math for mega-block parts (a rotor, a gear, a piston) that must turn in step with a mechanical-power axle rather than merely at a proportional speed. | [Block-Networks](Block-Networks) |
| `MpEnergyNetwork` | Concrete BlockNetwork for the mechanical-energy system: one shared reservoir that IMpEnergyProducer engines drive, IMpEnergyStorage flywheels buffer, and IMpEnergyConsumer machines load. | [Block-Networks](Block-Networks) |
| `MpEnergyNetworkState` | Live state of one mechanical-energy run modelled as a single spinning shaft: a drive applies torque, machines and friction resist it, and the net torque spins a lumped inertia up or down; stored energy is half the inertia times the squared speed. | [Block-Networks](Block-Networks) |

### `ExpandedLib.Industry.Metals`

The metal and alloy catalogue the molten system reads instead of parsing item codes, and the emitter that generates a whole item family for a metal that opts in.

| Type | What it is for | Page |
| --- | --- | --- |
| `MetalAlloyIngredient` | One ingredient of a MetalAlloySpec: a metal short code and its ratio band. | [Extending-Processes](Extending-Processes) |
| `MetalAlloySpec` | Inline alloy ratios for a MetalDef (the vanilla `AlloyRecipe` shape). | [Extending-Processes](Extending-Processes) |
| `MetalCatalogueLoader` | Populates MetalRegistry at `AssetsFinalize` in two passes: a derived baseline of one convention entry per metal in every loaded `worldproperties/block/metal` (vanilla and mods), so a metal shipping no MetalDef still has an entry, then an overlay of every domain's `config/metals/*.json` MetalDef, which enriches or replaces that entry. | [Extending-Processes](Extending-Processes) |
| `MetalDef` | One metal or alloy descriptor - the source of truth the molten system reads instead of string-munging item codes. | [Extending-Processes](Extending-Processes) |
| `MetalFamilyEmitter` | Generates the resource item family (ingot / plate / bits / rod / nails) for every MetalDef that opts in via GenerateItemFamily, plus that metal's tools from MetalToolEmitter. | [Extending-Processes](Extending-Processes) |
| `MetalRegistry` | Process-wide catalogue of MetalDefs, consulted by the molten system in place of item-code string surgery. | [Extending-Processes](Extending-Processes) |
| `MetalToolSpec` | Tool stats for a generated metal family (Tools). | [Extending-Processes](Extending-Processes) |

### `ExpandedLib.Industry.Heat`

One process heat balance, captured as the tick computed it, and the block-info readout a furnace or converter shows from it.

| Type | What it is for | Page |
| --- | --- | --- |
| `HeatBalance` | One evaluation of a process heat balance, captured as the tick computed it so block info reads the contributors without recomputing. | [Helpers-and-Renderers](Helpers-and-Renderers) |
| `HeatBalanceHud` | Formats the shared part of a furnace or converter heat-balance readout: internal temperature, the threshold it must clear, the heat-in and heat-loss ledger, and the blast state. | [Helpers-and-Renderers](Helpers-and-Renderers) |
| `HeatBalanceLedgerKeys` | The lang keys a HeatBalance ledger reads, supplied by the machine because exlib ships no lang of its own. | [Helpers-and-Renderers](Helpers-and-Renderers) |

### `ExpandedLib.Industry.Helpers`

Content helpers shared across the family mods: display names that carry a block's material variant, tool-mold drops, particles and sounds.

| Type | What it is for | Page |
| --- | --- | --- |
| `ExBlockNames` | Composes block display names that include the block's material variant: pipe metal ("Piping (Straight, Steel)"), canal rock, passthrough brick and refractory tier, so that same-shaped blocks of different materials are distinguishable in the inventory, handbook and look-at HUD. | [Helpers-and-Renderers](Helpers-and-Renderers) |
| `ExMoldDrops` | Reads a tool mold's cast-product templates off its block attributes: the vanilla `drop` (single) or `drops` (array) schema, resolved with the mold's own domain as the default for unqualified codes, matching `BlockEntityToolMold.GetMoldedStacks`. | [Helpers-and-Renderers](Helpers-and-Renderers) |
| `ExMoldGate` | Cross-mod hook for asking whether a tool-mold type is currently disabled by a config gate, without the asker referencing the mod that owns the molds. | [Helpers-and-Renderers](Helpers-and-Renderers) |
| `ExParticles` | Shared catalogue of particle effects for the mod family (iiex + siex). | [Helpers-and-Renderers](Helpers-and-Renderers) |
| `ExSounds` | Shared catalogue of sound asset locations and play helpers used across the mod family (iiex + siex). | [Helpers-and-Renderers](Helpers-and-Renderers) |

### `ExpandedLib.Industry.Materials`

The canonical role tokens the machines classify flux, fuel, ore, scrap and charge by.

| Type | What it is for | Page |
| --- | --- | --- |
| `Roles` | The canonical material-role tokens the machines classify by. | [Extending-Processes](Extending-Processes) |

## Obsolete members

A member on its way out is listed here, marked `[Obsolete]` in the code with its replacement named, for the one full release it stays deprecated before it is removed. If you call something in this table, the replacement column is the whole migration.

| Member | Replacement | Obsolete since | Removed in |
| --- | --- | --- | --- |
| `ExBlockDef.Raw(string, JToken)` / `Raw(string, object)` | `ExBlockDef.RootKey` | 0.8.0 | 0.9.0 |
| `ExBlockDef.RawByType(string, string, object)` | `ExBlockDef.RootKeyByType` | 0.8.0 | 0.9.0 |
| `ExItemDef.Raw(string, JToken)` / `Raw(string, object)` | `ExItemDef.RootKey` | 0.8.0 | 0.9.0 |
| `ExItemDef.RawByType(string, string, object)` | `ExItemDef.RootKeyByType` | 0.8.0 | 0.9.0 |
| `ExLiquids.Load(ICoreAPI)` | `LiquidCatalogueLoader.Load(ICoreAPI)` | 0.8.0 | 0.9.0 |
| `ExBlockDef.MineTool(EnumTool)` | none - `mineTool` is not a key the loader reads; use `Material` and `MiningTier` | 0.8.0 | 0.9.0 |
