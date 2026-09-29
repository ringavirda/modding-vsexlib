# Changelog - Expanded Library (`exlib`)

All notable changes to this mod are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/), and the project follows
[Semantic Versioning](https://semver.org/). For changes before this file existed,
see the git history.

## [Unreleased]

### Added

- **`InteractionLaw` and `ContainerLaw`** (ExpandedLib.Testing, `Laws/`), run by `BlockLaws.Run` as
  `interaction` and `container`: every cell of every block, clicked with an empty hand and with each
  item its right-click help names (keys held, the other items of the same action carried), throws
  nothing, logs nothing, keeps its block entity at its cell through a placement from a stack
  carrying `blockEntityAttributes` and a reload, and does something with each named item that the
  block or the held item takes; a stack a block entity accepts, by a click or into its inventory,
  is still held after a reload and dropped by the break. `BlockLaws.Run` takes item definitions
  beside block definitions for these laws' worlds. `RunFormed` asks the same of each cell of every
  multiblock stood up formed, one structure per combination of its variant groups other than its
  facing; a placement of the anchor or a filled cell that throws or logs is its finding, `Blocks`
  counts structures (`BlockLaws.Law.Unit`), and `BlockLaws.Law.Structures` names each one. The
  worlds load vanilla's ungraded ores, so a coke oven's hopper is offered the mined coals.
  `InteractionLaw` also names a right-click help line carrying both items and a `ShouldApply`, which
  the engine ignores on such a line.
- **`NeighbourLaw` and `NetworkLaw`** (ExpandedLib.Testing, `Laws/`), run by `BlockLaws.Run` as
  `neighbour` and `network`: every block, stood on a solid block, meets a solid block set and
  cleared on each free face of its cells, each change announced through
  `TestWorld.NotifyNeighbours`, and throws nothing, logs nothing and keeps each cell's code and
  entity tree, and a block without an entity places without a throw or a log; every pair of network
  members' connectors of one type, face to face, is walked through
  `BlockNetworkModSystem.GetConnectedNeighbors` from both sides, and joins from both or from
  neither; the network law's cases are the pairs joined from both sides. `NetworkLaw.RunPorts`, run
  as `network port`, stands each face a port (an `INetworkConnector` block the graph never holds)
  declares against every member connector facing it and counts the pairs whose walk reaches the
  port, so a guard floors them apart from the member pairs; a port face no member couples to is a
  finding.
- **`InfoLaw` and `ReloadLaw`** (ExpandedLib.Testing, `Laws/`), run by `BlockLaws.Run` as `info`
  and `reload`: every block entity's `GetBlockInfo`, read fresh, after 5 s of its own ticks and
  after `TestWorld.Reload`, throws nothing and logs nothing; the reload keeps the entity's class,
  its tree (by the reload tree comparison) and its info text.
- **`ExOmniRotatable` and `ExStairs`**: vanilla's slab behaviour (`OmniRotatable`) and stairs class
  (`BlockStairs`) for a block whose code has a dash, such as `slag-brickslab`. Vanilla builds the
  codes it places, rotates and flips to, and the stairs' drop and pick, from the code's first
  dash-segment, so its slab throws on placement and its stairs land nowhere; these build every code
  from the whole code and keep vanilla's groups, properties and rules. `GridOutputVariantCheck`
  reads `ExOmniRotatable`'s `rot` group.
- **`ExVariantCodes`**: `WithVariant` and `WithVariants`, a block's or item's code with variant
  groups replaced, built from the whole code where vanilla's `CodeWithVariant` and
  `CodeWithVariants` keep only the first dash-segment.
- **Block laws** (ExpandedLib.Testing, `Laws/`): `BlockLaws.Run` runs, over every block of a
  domain in every variant, `PlacementLaw` (a placement from every side and face lands a declared
  token), `BreakLaw` (`StructureBreaks`, plus no own-code drop beside a stage refund and no filler
  drop), `MultiblockLaw` (completes on the mods' registered blocks in every facing and reads its
  peripherals where it completes) and `MegablockLaw` (fillers stand exactly while the principal
  does, a world removal included).
- **`TestPlayer.Hotbar`, `GameMode` and `CtrlHeld`** (ExpandedLib.Testing): a real 12-slot hotbar
  that `GetHotbarInventory()` returns, the game mode `WorldData.CurrentGameMode` answers, and the
  entity's `CtrlKey` control. The world answers `PlayerByUid` for the player's uid with it, so the
  game's right-click construction pays from that hotbar. `TestWorld`'s `Collectibles` lists its
  registered items and blocks.
- **`StructureBreaks.Payment`** (ExpandedLib.Testing): `Survival`, `CreativeWithCtrl` and
  `Creative`, the three ways `StructureBreaks` pays a construction stage; `Spawn.Paid` names the one
  a break was built with.

- **`BlockSignalCensus`** (ExpandedLib.Testing) counts, per signal, the blocks of a domain that
  carry it: block class, entity class, behaviours, the groups placement writes, footprint, layout,
  nosnow cells, construction stages, network and mpenergy membership, a mechanical power
  connector, an inventory. `Run` reads a world's registered blocks, or stands a mod's code-first
  definitions up in one world first.
- **Five loaded checks** run from `/exmod verify`, never at load, over the new `ILoadedGame`
  (`AssetCheckSource` implements it; `LoadedOutput` is one stack a registry makes).
  `ObtainabilityCheck`: every recipe ingredient, construction stage ingredient and creative-listed
  block of a domain is made, one step deep, by a loaded recipe, exlib's process catalogues, a
  smelted, crushed or ground stack, a beehive kiln firing, another block type's drop, a world source
  or a mod's declaration; a creative-listed block counts as made when a block differing from it
  only in groups its placement writes is (an orientation group, a network node's `orientation`).
  `VanillaGridCollisionCheck`: no grid recipe matches a vanilla one's input. `GameReferencesCheck`:
  every `game:` code a domain names is loaded. `LoadedStageWildcardsCheck`: a stored construction
  wildcard over `game:` codes spans only its key's variant group. `CollectibleCollectionsCheck`: no
  collectible carries a null `CreativeInventoryTabs`, and no block a null `Variant`.
  `ExlibChecks.Loaded`, `LoadedFor` and `Verify` run them; `/exmod verify` calls `Verify`. A mod may
  see new Error lines from `/exmod verify` and `exmod smoke`.
- **`ExlibChecks.Exempt(domain, rule, code, reason)`** takes a finding a mod ships knowingly out of
  a check's errors, at load and in verify, into `CheckResult.Exempted`; an exemption that takes no
  finding is reported. **`ExlibChecks.Produces(domain, code, source)`** declares a code a mod's
  machine makes, which `ObtainabilityCheck` counts as made; a declaration matching no loaded block
  or item is reported. Both are dropped when a world starts loading.
- **Three grid recipe checks** run with the shipped checks, at load and from `/exmod verify`.
  `GridRecipeShapeCheck`: every ingredient key appears in the pattern, every pattern letter has a
  key, the pattern fills its width and height, and the grid is at most 3x3. `GridRecipeCollisionCheck`: no two grid recipes of the domains
  checked match the same input, counted as the game's matcher counts trimmed patterns, offsets,
  shapeless recipes over the input merged into stacks, wildcards, `allowedVariants` and named
  wildcards, with a code without a domain in its recipe file's. `GridOutputVariantCheck`: a
  recipe crafting a block oriented by `ExOrientable`, `HorizontalOrientable`, `NWOrientable`,
  `Pillar`, `OmniRotatable` or the `BlockStairs` class names the orientation its
  `creativeinventory` lists. `GridRecipes`
  (ExpandedLib.Testing) runs the three over a suite's definitions.
- **`SourceLaws.UnreadTunables`** (ExpandedLib.Testing): a value of a manageable config store
  that no source reads is named at its declaration. `StaleOnExchange` also names a block entity
  that writes a `MeshData` field with no `OnExchanged` override, and each such field an override
  neither assigns nor clears; `CachedTunables` reads a `ModSystem`'s `Start*`, `AssetsLoaded` and
  `AssetsFinalize`; `ContainerDialogPackets` names a `*Dialog*` type it cannot resolve instead of
  reading it as no dialog; `Key` throws `ArgumentException` on a line that is no finding.
- **`SourceLaws`** (ExpandedLib.Testing): four more source laws. A value of a manageable config
  store is read where it is used, not copied into a member at load (`CachedTunables`); a config
  value is read by more than `Lang.Get` arguments (`DisplayOnlyTunables`); a `BlockEntityContainer`
  that opens a `GuiDialogBlockEntity` handles `OnReceivedClientPacket` itself or through a base
  (`ContainerDialogPackets`); `SimpleParticleProperties` is built only in `ExParticles`
  (`InlineParticles`).
- **`SourceLaws`** (ExpandedLib.Testing): four source laws. A block entity that builds an animator,
  a renderer or a cached mesh for its facing overrides `OnExchanged` and calls the base; a part
  beside a mechanical network takes its frame from the network angle; `BlockFacing.FromCode` on a
  side state falls back to `FromFirstLetter`; a `SearchBlocks` result is checked before it is read.
- **`AxisSigns`** (ExpandedLib.Testing): every mechanical-power behaviour type of an assembly,
  placed in the four facings, gives one `AxisSign` per world axis, a unit on its discovery face's
  axis.
- **`OpenLayoutCells`** (ExpandedLib.Testing): in every layout column open to the sky, the first
  solid cell below the open run and the open cell above it carry `CellRoles.NoSnow`.
- **`AnimatorClips`** (ExpandedLib.Testing): every clip in a shipped shape ends in `Repeat` or
  `Hold`; a clip that ends leaves a block drawn only through its animator without a mesh.
- **`StageWildcardsCheck`** runs with the shipped checks, at load and from `/exmod verify`: every
  `ExRightClickConstructable` stage ingredient whose code holds `*` carries `storeWildCard`, keyed
  `wood` or `metal` and naming the one variant group its `*` spans; a `{key}` placeholder names one
  of the block's own variant groups or a key an earlier paid stage stores; a key on stage 0 is
  stored by stage 1. Such a stage throws or refunds nothing when the structure breaks.
  `ICheckSource.BlockTypes(domain)` hands a check the JSON blocktypes a domain ships; it yields
  nothing unless implemented, and `AssetCheckSource` and `RepoCheckSource` implement it.
  `StageWildcards` (ExpandedLib.Testing) runs the rules over a suite's definitions.
- **`CommentStyle` and `ShapeTextures`** (ExpandedLib.Testing): the comment style rules and the
  shipped-shape editor-path rule, which exlib, iiex and siex each kept a copy of, are checks a
  suite's guard calls: `CommentStyle` takes the sources `CommentStyle.Sources` reads from a list of
  folders, `ShapeTextures.EditorPaths` a mod's shapes folder.
- **`IMoltenCell.FlowRules`** (ExpandedLib.Industry): a molten cell can return `MoltenFlowRules`,
  its own rate in units per tick, minimum gap, conveying and horizontal-only flags. A connection
  whose two cells both return rules takes the smaller rate and the larger gap; below the gap nothing
  moves except into a drain fitting; a receiver farther from the nearest flow source takes the whole
  difference when both cells convey, a drain fitting and a downhill edge take the whole difference,
  and any other pair moves half; a horizontal-only cell exchanges no metal through its up and down
  faces with a cell that returns rules. The member defaults to null, and a connection where either
  cell returns null flows as before.
- **`IPipeVentSource`** (ExpandedLib.Industry): a pipe node's block can supply its own
  `IPipeVentStrategy` through `CreateVentStrategy`. `PipeNetwork` classifies each open face with
  its node's strategy, falling back to the one the "pipe" factory registered, and vents each
  strategy's faces through that strategy, so one world holds runs venting at different rates. Each
  network creates a block's strategy once and keeps it; a null strategy falls back to the
  factory's.
- **`CellRoles.NoSnow`**: a layout cell marked with it takes no weather snow while the structure
  stands, neither a snow layer on top nor a snow-covered variant. The structure's server-side block
  entity lists its marked cells in the new `NoSnowCells` as soon as its facing is known, keeps
  them while its chunk is unloaded and drops them when the core is broken; the marks are saved with
  the world. `NoSnowCells.Mark`/`Unmark` serve any other owner, keyed by the owner's position.
- **`StructureBreaks`** (ExpandedLib.Testing): stands up every code-first definition with filler
  offsets or construction stages, in every variant, and breaks it as a survival player from every
  cell at every construction stage. A break fails when it
  throws, leaves a cell standing, or drops other than the definition's `drops` plus the paid stages
  at the salvage ratio. `TestWorld.BreakRunsBlockHooks` and `TestWorld.RunsRemovalHooks` run the
  engine's break and removal hooks through the accessor, `TestWorld.RegisterClasses`/`RegisterClass`
  hold a real class registry, and
  `TestWorld.DefineBlock` builds one variant of a definition the way the game registers it.
- **`StructureBreaks.Run(TestWorld, include)`** (ExpandedLib.Testing): breaks the JSON blocks
  `TestWorld.LoadAssets` loaded that carry `fillerOffsets` or `ExRightClickConstructable` stages,
  under the same pass rules, all in that world, a later run standing its structures past the
  earlier run's. `StructureBreaks.Result.Spawned` lists every break with the stacks it spawned, on
  both paths.
- **Machine-sound volume**: `.exmod sound [0-1]` sets a per-player multiplier, `ExSounds.MachineVolume`,
  on every sound the `ExSounds` helpers play. One-shots the server plays reach each client in range
  over the `exlibSound` channel and are scaled there; one-shots and loops play as the game's Sound
  type, as vanilla machines do.
- **`ExSoundLoop`**: one machine's looping sound, loaded once on the client, started and stopped with
  the machine, following `MachineVolume`, and released for good by `Dispose`.
- **`SoundUse`** (ExpandedLib.Testing): fails a `PlayThrottled`/`PlayLoop` call whose interval is
  shorter than its clip, a type holding an `ILoadedSound` itself, and an `ExSoundLoop` that
  `OnBlockRemoved()` or `OnBlockUnloaded()` never disposes; `DirectSounds` fails a `PlaySound*` or
  `LoadSound` call made past `ExSounds`.
- **`[assembly: ExPublishedSaveKeys]`**: marks a mod whose bare block-entity keys (`{Xxx}`,
  `{xxx}`) are in published saves. A bare key claimed by a marked and an unmarked assembly is
  registered to the marked one in either load order; the unmarked type keeps its prefixed keys and
  one Notification names both.
- **`EntityRegistry.AliasBlockEntity`**: registers a load-only key for a block entity type and
  leaves the type saved under its primary key.
- **`ExSounds.ClipLengthMs`**, **`ExSounds.CeramicBreak`**, **`ExSounds.GearboxTurn`**,
  **`ExSounds.HeavyMetalHit`**.
- **`FailOnWarningsAttribute`** (ExpandedLib.Testing): `[assembly: FailOnWarnings]` fails a test that
  leaves an unexpected Warning, Error or Fatal entry in any `RecordingLogger` it created, in its
  class constructor or its body. `RecordingLogger.Expect(type, fragment)` declares the entries a test
  means to log; an expectation nothing matched fails too. A logger that receives an entry after its
  check, through a static that still holds it, is checked again with the next test. `ReportOnly =
  true` lists the offenders on standard error without failing them. Every `ILogger` NSubstitute
  creates, by `Substitute.For` in any form or as the made-up `Logger` of an API or world substitute,
  logs into a `RecordingLogger` the check reads as well.
- **`HarnessUse`** (ExpandedLib.Testing): source rules over a suite's tests. It names a
  `StructureComplete` written by reflection, directly or through a `PropertyInfo` or `FieldInfo`
  local bound to it by name, or through its setter from a test subclass, a `BlockBehaviors`
  assignment without `CollectibleBehaviors` on the same receiver, a generic helper in a test
  file constrained on a game type, and a static field typed as a tuple holding a game type.
- **`ExMeasure.TemperatureDelta`**: a temperature difference in Celsius degrees, shown in Fahrenheit
  in imperial without the 32 degree offset: a 10 C difference reads 18 F.
- **`FindingLists`** (ExpandedLib.Testing): `Assert` holds a guard's findings against its allowed
  list, permanent exceptions with their reasons, and its known list, defects awaiting a fix. A
  finding on neither list fails, and so does a known entry the rule does not report.
- **`ReleasedHistory.Register(mod, version, added)`** (ExpandedLib.Testing) records one release and
  the blocktypes it shipped for the first time, an empty list when it added none; `Releases` lists
  the rows and `ReleasedVersions.Compare` orders versions. exlib's seed carries 0.8.0, 0.8.1 and
  0.8.2, which shipped no code beyond `exlib:structurefiller`, and a guard fails when the newest
  release tag has no row.
- **`PlantedDefects`**, **`[PlantedDefect]`** and **`[CheckHelper]`** (ExpandedLib.Testing): a check
  proves it can fail. `PlantedDefects.Survey` sorts the public static members of a folder's check
  types into those a `[PlantedDefect]` test proves, helpers marked `[CheckHelper]` with a reason, and
  the rest, every public type a file declares included; a file named after no type is reported with
  them. A skipped test proves nothing. exlib's suite fails on a check member that is neither.
- **`[GuardOf]`**, **`PlantedDefects.Unproven`** and **`HarnessUse.UncalledGuards`**
  (ExpandedLib.Testing): a guard class in a suite's `Invariants` folder names the checks it calls, or
  proves its own rule with a `[PlantedDefect]` test. `Unproven` lists the guards that do neither or
  name a `[CheckHelper]`, and `UncalledGuards` fails a guard file that never calls a check it names.
- **`HarnessUse.UncalledPlants`** (ExpandedLib.Testing): names every `[PlantedDefect]` test that
  never reaches the member it names, in its body or through the helpers of its file it calls.
- **`Premise`** and **`HarnessUse.Unpremised`** (ExpandedLib.Testing): `Premise.NotEmpty` fails a
  guard whose corpus is empty and `Premise.Covers` one that misses part of a census, such as a
  domain's golden blocktypes. `Unpremised` names every guard file that calls neither.
- **`HarnessUse.SubstituteLoggers`** (ExpandedLib.Testing): names every `Substitute.For<ILogger>` in
  a suite's tests, whose entries no `Expect` can declare. exlib's tests log into a
  `RecordingLogger` instead and declare the Warnings and Errors they drive with `Expect`; exlib's
  guard fails on a new substitute logger outside its allowed list.
- **`RegistrySubCommand<T>.CallerLanguage`**: the language of the caller a `Set` override is
  answering, the server's own for the console, for `Lang.GetL`. It is valid only while `Set` runs
  for a dispatched command and throws `InvalidOperationException` outside it.

### Changed

- **`RepoPaths` reads a docs root from `exmod.json`** (ExpandedLib.Testing). A `docs` entry, a path
  relative to the repository root, moves the wiki (`RepoPaths.Wiki`) and each mod's docs
  (`RepoPaths.Docs`, now `<root>/<modId>`) to that folder; `RepoPaths.DocsRoot` names it. An entry
  naming a folder that does not exist throws `DirectoryNotFoundException` with the path. A manifest
  without the entry keeps `<mod path>/docs` and `wiki/`.
- **exlib's design pages and wiki source moved to exdocs** (`exdocs/exlib/design`,
  `exdocs/exlib/wiki`), a repository cloned beside this one; the wiki guards and the Sync Wiki
  workflow read them from there.
- **The block laws see facings and coverage.** `MultiblockLaw` raises each variant at the angle its
  `side` token gives, plus the offset its blocktype's first variant turns by, and names a variant
  whose layout turns elsewhere, so a machine turning every facing to one angle is found.
  `MegablockLaw` holds a `BlockFilledMegastructure` to the fillers of its own `StructureAngle`.
  `PlacementLaw` names a declared token no placement lands, network nodes aside.

- **Game install lookup** (`build/ExpandedLib.targets`): `$(GamePath)` is the environment override,
  else the nearest `.game/<slug>` holding `VintagestoryAPI.dll` from the repository upward, else
  `.game/<slug>` beside an `exmod.workspace.json` above the repository, else the repository's own.
  Auto-provisioning passes that path as `-Dest`, and the build prints it.
- `ExRightClickConstructable` refuses a payment that would take a stored wildcard key in two
  variants inside the stage that stores it, such as iron plates with steel rods in a first metal
  stage, and shows the player `exlib:ingameerror-construction-onematerial`. A creative player
  holding Ctrl pays nothing and is not refused.
- `StructureBreaks` (ExpandedLib.Testing) pays each stage through the block's own interaction and
  the game's `RightClickConstruction`, in survival, in creative with Ctrl held and in creative
  without it, each wildcard stage in the next allowed variant, and compares the refund to what was
  paid; it offers a storing stage its key in two variants first and fails a stage that takes them.
  A code-first run's stages past 0 now break three times, and a mismatch reads `(expected: ...)`
  where it read `(definition: ...)`. A wildcard ingredient without `allowedVariants` fails as not
  stood up.
- `StructureRig` (ExpandedLib.Testing) raises the cells the anchor reports missing, and `Missing`,
  `MissingReport` and `Complete`'s message come from the anchor's report. `Around` throws
  `InvalidOperationException` when the anchor turns its layout to another angle than the one
  given, and a new `Around(world, anchor, angle)` rigs a block that carries its layout in JSON.
- `BlockEntityMultiblockStructure.IncompleteBlockCount` and `CompletionTickMs` are `protected
  internal`. A subclass that overrides `CompletionTickMs` from another assembly keeps writing
  `protected override`.
- `GridRecipeCollisionCheck` reads tags from a loaded game on 1.22 and later: an ingredient with
  tags and no code takes the collectibles its tags meet, not every item of its class. The harness's
  `ReferencedCodes` reads its references from `GameReferencesCheck`, which now holds the extraction.
  `ICheckSource` gains `ItemTypes(domain)`, which yields nothing unless implemented. The harness
  resolves a class or behaviour key no scanned assembly registers through the install's vanilla
  registrations, so `BlockStairs` orients a grid output test-side as it does in game.
- `NetworkNodeContractCheck` knows a network node by a `class` key that resolves to a
  `BlockNetworkNode` as well as by an `ExOrientable` declaration in `network` mode, and a membership
  by a key that resolves to a `BEBehaviorNetworkMember` subclass as well as by the bare key; it also
  reports a class-known node whose `ExOrientable` is missing or not in `network` mode. A mod may see
  new Error lines at load, one per such definition. `ICheckSource` gains `BlockClass` and
  `BlockEntityBehaviorClass`, which answer null unless implemented; `AssetCheckSource` answers from
  the game's class registry and `RepoCheckSource` by reflection. The harness's `NetworkNodeContract`
  runs the check: its type-group finding names the block class when one resolves, and its scheme
  finding names the block's code in place of its class key.
- `DefinitionCatalogue.ItemPatterns` (ExpandedLib.Testing) expands variant groups through the same
  code as `DefinitionCatalogueCheck`.
- `RecipeCodes`, `MultiblockCodes` and `PinnedNetworkNodes` (ExpandedLib.Testing) run
  `RecipeCodesCheck`, `MultiblockCodesCheck` and `PinnedNetworkNodesCheck`, so a suite and the game
  apply one rule. `RecipeCodesCheck` reports a block output holding a wildcard. `MultiblockCodes`
  matches a layout code against the states a block declares, segment by segment, and an item code
  no longer provides a cell. `PinnedNetworkNodes` names a finding by the pinning block's code, not
  its file. `MultiblockCodes` and `PinnedNetworkNodes` throw `ArgumentException` when two sources
  share a domain.
- `DefinitionGoldens.WriteAll` (ExpandedLib.Testing) throws `InvalidOperationException` naming the
  `EXLIB_WRITE_GOLDENS` value when one of its fragments matches none of the domain's goldens, and
  writes nothing; a fragment starting with another domain is skipped.
- `DefinitionGoldens` (ExpandedLib.Testing) keeps an older series' goldens under
  `goldens-{series}` (`SeriesRoot`, `OlderSeries`): a series golden overrides the shared one there,
  and satisfies completeness for a def only that series has. A marker, the shared golden's path
  there with `AbsentSuffix` (`.absent`) appended, excuses a shared golden whose def the series
  lacks; a stale marker is an orphan. A series write leaves the shared goldens alone, writes and
  deletes series goldens and markers, and runs after the current series' write.
- A `TestWorld` (ExpandedLib.Testing) follows the engine by default. `World.Side` answers Server, and
  `ClientApi.World` is a client world whose `Side` answers Client, reading the same block accessor,
  lookups and logger; code handed `Api` runs its server branch, code handed `ClientApi` its client
  branch. `BreakBlock` runs the block's own `OnBlockBroken`, `SetBlock` over another block runs the
  replaced block's `OnBlockRemoved`, and `RemoveBlockEntity` removes the entity and runs its hook; a
  test that wants less sets `BreakRunsBlockHooks` or `RunsRemovalHooks` false. In a world holding a
  class registry, `Place` logs a Warning when the block's `EntityClass` names a class other than the
  one the placed entity's type is registered under.
- A network walk asks both cells of a pair whether they join (`AcceptsNeighbour`): a refusal from
  either side keeps them apart whichever one the walk or placement starts from. Before, only the
  source was asked, so a one-sided refusal could merge or split the pair depending on order.
- The incompatible-mods guard refuses Pipes and Power Expanded below 0.7.0 and Steelmaking Expanded
  below 0.10.0 only, and its message names the version to install. A mod the loader has loaded is
  judged by that version; one that failed to load by the newest copy in the Mods folders.
- `EntityRegistry.RegisterAll` registers a block entity's aliases first and its primary key last, so
  saves written from now on name `{modid}.BlockEntityXxx` instead of the bare `{xxx}`. The aliases
  stay registered and older saves load as before.
- `ExSounds.PlayThrottled` and `PlayLoop` never repeat a sound before its clip has finished, whatever
  interval the caller asks for. The pipe ambience plays at most one clip at a time per pipe.
- `TestWorld.LoadAssets` (ExpandedLib.Testing) loads the base game as the game does. The vanilla
  mods' mod systems register their block, item and entity classes first, and on 1.22 the tag
  converters read this load's tag registries, so the base `game` domain loads without a Warning or
  Error; a block naming a class nothing registers still logs its Error. exlib's own mod systems start
  before the mod's, so a mod block naming an `exlib.*` class resolves. Every system a load starts,
  the mod's own included, is disposed when the load ends, whether a `Start` threw or not, so the
  Harmony holds they took are released. Each load drops the code-first definitions an earlier
  load registered. A mod whose `modinfo.json` declares
  `"type": "content"` loads without a compiled assembly.
- `RegistryLawScanner.ForEach` (ExpandedLib.Testing) throws when the base type has no concrete
  subclass loaded, instead of passing a law that checked nothing.
- `WikiParity.Check` (ExpandedLib.Testing) reports a page from which no symbol resolved against the
  assembly, since nothing on it was checked; new overloads taking a `symbolFree` list name pages that
  name no API on purpose, and the 0.8.2 overloads keep their signatures, so a test assembly built
  against 0.8.2 still runs.
- `LangCallSites` (ExpandedLib.Testing) also reads the key a `...Key =>` property returns, such as a
  registry sub-command's `ListHeaderKey`.

### Removed

- `ExSounds.MePostHit`: its file is not in the game; `HeavyMetalHit` replaces it.

### Fixed

- **A structure's `nosnow` cells take snow as its chunk loads again.** Vanilla's snow catch-up
  (`WeatherSimulationSnowAccum`) runs on the chunk thread as a column loads, before the structure's
  block entity marks its cells again. The marks are keyed by the structure's position, stay while its
  chunk is unloaded, are saved with the world (`exlib:nosnowcells`) and are read back at
  `SaveGameLoaded`, before any chunk loads; a saved mark whose structure is gone when its column
  loads is released then.
- **`/exmod` replies reach each caller in their own language**: the root's help, `heal`, `verify`,
  `recipes` and `config`, the server console in the server's. `config` no longer prints its bare
  lang key on the server console.
- **`TestWorld`**: the accessor's `SpawnBlockEntity`, which a block's `OnBlockPlaced` calls, runs the
  spawned entity's `OnBlockPlaced` with the placing stack, as the engine's does; `Register(Block)`
  gives a block without sounds empty ones, as the engine's registration does, so vanilla code
  playing a block's placement sound (the wrench) no longer throws; `RegisterClass` and
  `RegisterClasses` take item and collectible-behaviour classes; the api's `ObjectCache` is a real
  dictionary and the server world's `SearchItems` and `SearchBlocks` match wildcards over the
  registries.
- **`TestWorld.LoadAssets`** loads the install's survival world properties before the mod's assets,
  as the game does, so a variant group that reads them (`"loadFromProperties":
  "abstract/horizontalorientation"`) registers every variant; it registered one block without the
  group's variant.
- **`StructureBreaks`** expects a block's own drops as the game's `Block.GetDrops` makes them: each
  block behaviour's drops, then its `drops` unless a behaviour prevents them, so a structure with
  vanilla's `HorizontalOrientable` is expected to drop its `dropBlockFace` variant (`north` by
  default); it read only `drops` and failed every other facing. A block class that overrides
  `GetDrops` is still held to its `drops` alone. A construction stage wildcard with
  no `allowedVariants` is paid in a variant the world holds that it matches, less its
  `skipVariants`, as the game takes any; it could not stand such a stage up, and still names the
  stage and the wildcard when the world holds none.
- **`LangCoverageCheck`** reads a lang key with a `*` inside it (`block-toolmold-*-fired-plate`) as
  the game's lang does, a wildcard over the whole key; it read such a key as a literal and reported
  every block the key names.
- **`TestPlayer`**: the entity's `RightHandItemSlot` answers the active slot.
- **A wrench turns a structure from any of its cells.** Vanilla's wrench asks the clicked block for
  `IWrenchOrientable`, which a filler did not answer, so its principal's rotate help did nothing
  there; `BlockStructureFiller` now answers with the principal's rotation, run at the principal's
  cell.
- **An open mold draws the metal around it.** A pour sized to its molds left metal in the canals: it
  conveyed past the near molds, and its return levelled by half the difference rounded down, so a
  one-unit gap never closed. `IMoltenCell.IsOpenMold` (default false) marks a drain fitting whose mold
  is present, has room, has not solidified and is set to pour. Each cell within
  `MoltenMoldDrawRadius` canal hops of one (`ExlibConfig`, default 3) hands a neighbour nearer to it the
  whole difference, capped by the flow rate and the neighbour's room, with no gap floor, while it holds
  more, and moves nothing back across that connection. Farther out the network conveys and levels as
  before, so a mold set far out on a large network pulls nothing across it.
- **`TestWorld.Reload` builds the reloaded block entity from the block's entity class** through the
  class registry, with its behaviours, as the game does on load; without a registered factory it
  made one of the old instance's type with no behaviours. The harness's air is solid on no side,
  as the game's is.
- **A block with a `cover` group and a dash in its code takes snow.** Vanilla's `Block.OnLoaded`
  finds the free and snowed variants from the code's first dash-segment, so `slag-path-free` had
  none: weather never snowed it, and `BreakSnowFirst` broke a snowed one whole. A postfix on
  `Block.OnLoaded` finds them from the whole code.
- **`ExOrientable` places a block whose code has a dash.** The placed, dropped and picked codes
  were built from the code's first dash-segment, so `crafting-workbench` looked for
  `crafting-n` and every placement was refused with an error. A network node's fallback drop and
  its placed orientation had the same fault.
- **A network node of a type another provider defines places.** `AllowedOrientations` held only
  the types the node's own class declares, so a `BlockPipe` type declared elsewhere (a pipe
  indicator) was refused with `exlib-noorientation` from every side. A loaded node now adds its
  own type from the loaded blocks that differ from it only in `orientation`.
- **The placement law tells apart definitions that share a code.** Blocks sharing a code with
  other variant groups were judged as one blocktype, and a network node without an `orientation`
  group was taken to write one; it also names a stack that lands from nowhere with its refusal
  codes, and a placement that changes a group placement does not write.
- **`/exmod verify` reads code-first recipes and blocktypes.** The server unloads unpatched
  asset data once the world is up, and a code-first asset's origin loaded nothing back, so every
  check over them passed with nothing to read. The origin now keeps each asset's bytes.
- **`RecipeCodesCheck` accepts an output placeholder any state fills.** A `{name}` filled by a
  named ingredient with no `allowedVariants` takes whatever states the loaded game gives, and was
  reported as a code no block has. It now passes when some registered block matches it in any
  state.
- **`TestWorld.LoadAssets` gives each loaded block and item an id of its own.** Every one kept id 0,
  so the last block loaded replaced air in the world's id table. The load's classes now go into the
  world's own class registry, so a loaded block set in the world spawns its block entity with its
  declared behaviours.
- **Leaving a world and loading another no longer carries state over.** exlib's registries are
  process-wide, and the game keeps mod assemblies loaded between worlds, so a second world in the
  same client process met the first one's code-first definitions, catalogue contributors, pipe tier
  ratings, config, recipe-profile and preference registrations and a
  `MoltenMetal.TemperatureFormatter` a mod set, including those of a mod since disabled. Each is now
  returned to its fresh-process state when a world starts loading, at `StartPre` of exlib's own
  driver: on the server every time, on a client only when it joins a remote server
  (`ExWorldState.ResetsOnLoad`). A singleplayer client loads after its own server in the same
  process and keeps what that server loaded; its `AssetsFinalize` no longer empties and reloads the
  metal, liquid, material-role, process-route, process-job and bay-occupancy catalogues while that
  server ticks, and a mod enabled on the server alone keeps its entries for the session. At
  `Dispose` exlib now only releases its Harmony hold and, on the server, closes the sound channel: a
  singleplayer client's `Dispose` no longer removes exlib's Harmony patches, empties `NoSnowCells`
  or closes the server end of the sound channel while its server still runs. `ExHarmony.UnpatchAll`
  releases one `PatchOnce` hold on each assembly patched under the id, whoever took it, and removes
  the id's patches, categories included, only when none is left, so the patches come off with the
  last side's `Dispose`; `NoSnowCells` is emptied at the next world's load start; the channel closes
  from the Industry module instance that opened it. A `HarmonyFixture` disposed twice releases its
  hold once.
- **A `Dispose` releases only the Harmony hold its own `Start` took.** `ExpandedLibModSystem`,
  an `ExModSystem` with `PatchHarmony` and `ExModuleHost` release a hold only when their `Start`
  reached `ExHarmony.PatchOnce`, and only once: a singleplayer client whose `Start` threw before it
  patched, or a system disposed twice, no longer unpatches exlib or a mod under its server.
- **A side start that throws releases its Harmony hold.** The game drops a system whose
  `StartServerSide` or `StartClientSide` throws and never disposes it, so its hold stayed for the
  rest of the process. `ExpandedLibModSystem`, an `ExModSystem` (its own hold and its modules') and
  `ExModuleHost` now release the holds their `Start` took there, then pass the exception on
  unchanged.
- **`HarmonyFixture`'s category form releases no hold.** It applies through
  `PatchCategoryWhenLoaded`, which takes none, so its `Dispose` reverts the id's patches only when no
  `PatchOnce` hold is left on the id; beside a holder of the same id, its category stays until that
  holder's last `UnpatchAll`.
- **`TestWorld`'s `ExchangeBlock` runs the block entity's `OnExchanged`**, with the new block, after
  the cell holds it and keeping the entity, as the engine's does; a part that rebuilds on an exchange
  no longer needs its test to call `OnExchanged` itself.
## [0.8.2] - 2026-09-15

### Added

- **One-sided filler ports.** `through: false` in a `BEBehaviorMPFillerPort`'s filler properties
  couples an axle on the port face alone: the cell refuses the opposite face and the network ends
  at the port, as at a vanilla consumer, instead of passing out of the machine's far side. The
  default is unchanged, a row of ports still joins into one line an axle drives from either end.
- **`BEBehaviorMPFillerPort.DrivenAngleRad`**: the coupled axle's angle as a turn about the axis
  running from the port into the machine, in the sense vanilla draws the axle, so a shaft clip
  phase-locked to it turns with the axle on every side. `CurrentAngleRad` alone reads mirrored on an
  east or south port.

### Fixed

- **The filler port's axis sign is vanilla's per axis again** (`[-1, 0, 0]` on X, `[0, 0, -1]` on
  Z), as `BEBehaviorMPSubmachineBase` has it; 0.8.1 signed it from the port face.
- **The twin-tub blower sample**: the server resolves the construction behaviour too, so a finished
  blower blows (0.8.1's server never saw it complete); the crank follows the axle in every
  orientation; the port is one-sided, so an axle on the far side no longer turns.

## [0.8.1] - 2026-09-15

### Added

- **Two more samples** (`samples/PlatedPipes`, `samples/SmokeStack`): the network example, one pipe
  tier with its own burst pressure, throughput and joint registered from config, and the multiblock
  example, a 72-cell chimney that draws gas off the network it is plumbed into and vents it as a
  plume. With the blower and the burden maker they are the four mods the starter ships.

### Changed

- **`ItemDie.Itemtype` and `MachineTool.Itemtype`** take an optional asset path, so a mod can file
  its die and tool itemtypes under a sub-folder like every other item.

### Fixed

- **A network node picks its fallback stack whatever it drops** (`BlockNetworkNode.OnPickBlock`):
  the block-info HUD crashed on a raised mega-block's filler, whose principal drops nothing.
- **A filler port's axle sign follows its own face** (`BEBehaviorMPFillerPort`): a port facing east
  or south read the axle's angle mirrored, so a machine on that side spun against its axle.
- **Composed lang keys fall back to exlib's own**: the multiblock outline hint and the passthrough
  brick names are looked up in the block's domain first and then in `exlib:`, which now ships them,
  so a third-party mod no longer shows raw keys.
- **The twin-tub blower sample** is raised through five right-click construction stages like the
  burden maker, turns east and west by the repository's rotation convention, sounds its bellows
  whenever they move, locks its bellows to the axle every render frame, throttles the
  bellows note to the clip, vents air and joins a pipe only at its outlet filler (the orientation
  letter named the wrong face for east and west), keeps the facing the player gave it (north and south no longer come
  out alike), cannot be wrench-rotated, wears the aligned shape, and moves, sounds and vents air at
  its outlet while an axle drives it.
- **The burden maker sample** wears its polished shape with the game's textures, poses the lid on
  the client when the gate opens, draws the ore, flux and burden levels in its hoppers and basin,
  and drains the hoppers into the basin over `BurdenmakerDrainSeconds` instead of at once.
- **The smoke stack sample** stays silent while venting plain air.
- A `shape.selectiveElements` list now reaches every `shapebytype` entry that names none, so a block
  or item that keys its shape by variant renders only the elements its definition selects in the game,
  as it already did in the wiki.

### Changed

- **The twin-tub blower and burden maker samples** (`samples/TwinTubBlower`, `samples/BurdenMaker`)
  replace `HandMill` and `Grains`: a mechanically driven pair of bellows on the gas-pipe network,
  and a designed multiblock stock house with its own five-stage right-click construction.

## [0.8.0] - 2026-09-14

The first stable release of the 0.8 line: the content of the three previews below, unchanged
since 0.8.0-preview.3. Steelmaking Expanded 0.9.8 and Pipes and Power Expanded 0.6.8 stay on
exlib 0.7.2; the new family (Iron Industry Expanded, Steel Industry Expanded) builds on this.

## [0.8.0-preview.3] - 2026-09-14

### Added

- **The hand mill and grains samples** (`samples/HandMill`, `samples/Grains`) replace
  `HelloExpanded` and `HelloModule`: a crank and a shaft on the mechanical-power network, a
  flywheel, a designed multiblock mill core, a JSON-only quern stand, and the grain-catalogue
  module the mill reads from.
- `BlockNetworkModSystem.RegisteredNetworkTypes`: every network type a factory has been
  registered for.
- **The incompatible-mods guard** (`Registries/IncompatibleMods`): a published mod built against
  exlib 0.7 that cannot load beside 0.8 is named at `StartPre` and reported to every joining
  player, rather than surfacing as a missing type at load.
- Eleven `dotnet new` templates alongside the existing headless test project - block, item,
  recipe, megablock, multiblock, node, block behaviour, entity behaviour, config section,
  migration and command - and `exmod scaffold <kind> <Name>` to add one to an existing mod.

### Changed

- The template payload moves to `templates/content/<kind>`: `dotnet new install
  ./templates/exlib-tests` becomes `dotnet new install ./templates/content/exlib-tests`.

- `ExpandedLib.Industry` registers `pipe`, `molten` and `mpenergy` itself, each with its own
  defaults, in its own `Start`; `BlockNetworkModSystem.RegisterNetworkType` now returns whether it
  replaced an earlier factory for the same type, and logs the replacement.
- The samples move to the family layout: `samples/<Name>/{src,tests,assets}`.
- A JSON multiblock layout's declared empty `fillerOffsets` is honoured rather than replaced by
  the derived footprint.
- The repository uses the standard .NET container layout: `src/ExpandedLib` (the mod, with its
  assets), `src/ExpandedLib.Industry`, `src/ExpandedLib.Testing`, `src/ExpandedLib.Generators`,
  `tests/ExpandedLib.Tests` with the test settings and coverage floors beside it; `samples/`,
  `templates/`, `build/`, `docs/` and `wiki/` stay. Nothing in the packages, the namespaces or a
  save changes; a source-mode consumer's project references follow the new paths.

## [0.8.0-preview.2] - 2026-09-07

### Added

- **Three more registration kinds** on the attribute rung: `[EntityRegister]`,
  `[EntityBehaviorRegister]` and `[CropBehaviorRegister]`, handled by `EntityRegistry.RegisterAll`
  in `Start` beside the six existing kinds.
- **`ExRecipeRegistry`** registers a `RecipeRegistryGeneric<T>` for a mod's own recipe type on both
  sides in `Start`, with the recipe asset load server-side in `AssetsLoaded`.
- **`ExWorldData` and `ExChunkData`**: typed per-world (`ISaveGame`) and per-chunk (`IWorldChunk`
  moddata) side-band data, keyed `{domain}:{key}`. A `ChunkColumnSweeperModSystem` that declares a
  version sweeps each column once per version and marks it in chunk moddata; a sweeper without one
  keeps sweeping on every load.
- **`LateDefinitionCheck`**: a code-first block, item or recipe definition registered after the
  injection pass at `AssetsLoaded` 0.04 is reported by name, with the remedy, from `AssetsFinalize`
  and `/exmod verify`.
- A second type claiming a bare `{ShortId}`/`{shortid}` block-entity alias key is logged as an
  error naming both types; the aliases themselves stay.
- `ModSystemOrderTests`: every exlib ModSystem overriding `AssetsLoaded` or `AssetsFinalize`
  declares an `ExecuteOrder` below the 0.1 consumer default.
- **`[ExCheckRegister]` and `ExCheckRegistry`**: a mod's own content check, a class exposing
  `static CheckResult Run(ICheckSource, string)`, registers through the same assembly scan as
  the other attributes and runs after the shipped checks in `AssetsFinalize` and `/exmod verify`,
  each isolated so a throw is one reported error.
- **`[ExRecipeProfile]`** beside `[ExConfigRegister]` on a recipe-cost config class: the config
  generator emits the mod's `RecipeProfile` registration from the accessors it already emits.
- `ExBlockDef` gains `GuiTransform`, `GroundTransform` and `FpHandTransform` with the item
  builder's overloads, and `TpHandTransform` gains the ten-double and object overloads that emit
  an origin; the seven-double overload keeps emitting translation, rotation and scale.
- Generator diagnostics: `EXLIB0002` names a mis-shaped `[ExRecipeProfile]` config at the
  attribute (the `#error` stays, so the build cannot be silenced into a dead profile);
  `EXLIB0003` warns when `AssetDomain` is set but no lang file feeds `ExLangKeyGenerator`;
  `EXLIB0004` warns when two lang keys sanitise to one member name.
- `LangCoverageCheck.Run(source, domain, allLocales)`: the harness's `LangCoverage.MissingNames`
  runs the same loop over every locale instead of carrying its own copy.
- `VersionPinTests` binds the `ExpandedLib*` versions in `templates/` and the `exlib` floor in
  Getting-Started to `src/modinfo.json`.

### Changed

- `ExpandedLibModSystem` runs at `ExecuteOrder` 0.06 and `ExModSystem` at an explicit 0.1, so the
  framework's `AssetsFinalize` (its five catalogue loads and the content checks) precedes every
  consumer's by order rather than by the dependency tie-break the Lifecycle page used to assert.
  The module driver stays at 0.03 and definition injection at 0.04.
- The content-check pass runs after exlib's own catalogue loads in `AssetsFinalize`, and its
  errors are logged at Error.
- `DeclareState` is virtual on `ExBlockEntity` and `ExBlockEntityContainer`, as on the other
  state hosts; a subclass with only `[Persist]` members needs no override.
- `JsonMultiblockLayout` no longer keeps every resolved block alive across worlds.
- The wiki parity check in ExpandedLib.Testing also fails a snippet that declares a member
  `virtual` or `override` where the code declares it `abstract`, and examines the containers
  section of the block-entities page.
- The sample mods set `AssetDomain` instead of hand-rolling the asset glob and the lang
  generator's input; Getting-Started and Source-Generators say a mod project sets it to its modid.
- A release's assets include the symbol packages beside the nupkgs.

### Fixed

- A machine station's window is disposed when it closes; a refused open (a duplicate window)
  no longer leaves a dialog the station can never open again, and no longer opens the inventory
  or sends the open packet.
- `IExModule.AssetsLoaded` is documented as running at the host's order, ahead of the JSON patch
  loader at 0.05, so an asset read there sees unpatched JSON; a catalogue read belongs in
  `AssetsFinalize`. The Lifecycle, Modules and Extending-Processes pages say the same, and the
  metal catalogue's patch caveat sits beside the route caveat.

## [0.8.0-preview.1] - 2026-09-07

### Added

- **Modules**, exlib's extension mechanism: `[assembly: ExModule("<id>")]` marks an assembly as a
  module, driven through the lifecycle of the mod named in `Host` (default `exlib`) instead of
  carrying a `ModSystem` of its own, ordered against the rest of its host's modules by `Requires`.
  `ExModules.For(api, host)` discovers and orders a host's modules, keeping only the ones whose
  shipping mod (`Mod`) is enabled; `ExModuleHost` owns one driver instance's entry-point instances
  and runs them, and their assembly's registries, through the same phases and order of operations
  as `ExModSystem` runs for a main assembly. `PatchHarmony` opts a module into Harmony patching
  under its own `<host>.<id>` id. `ExModules.IsLoaded(api, id)` and the `exlib:module:<id>`
  world-config flag let another mod or a JSON patch condition gate on a module the way
  `ExMods`/`exlib:mod:<id>` already do for a mod. `IExDefinitionContributor` runs at
  `AssetsLoaded` 0.04, right before injection, regardless of host order, so a module (or a main
  assembly) can register code-first definitions built from assets that are only readable once
  every mod's `Start` has run - Industry's metal-family emission moved here from its own
  `AssetsLoaded`. `ExpandedLib.Industry` is the first module, shipped inside the exlib mod folder
  (`exlib.industry.dll`); `samples/HelloModule` proves the third-party form, a module shipped as
  its own mod depending on exlib. See the wiki's [Modules](wiki/Modules.md) page.
- `Catalogues/AssetCatalogueLoader.cs` is public. It reads every domain's
  `config/<yours>/*.json` under a path prefix into a typed object and reports what failed to
  parse - the primitive `ContributedCatalogueLoader<TSet, TRegistry>` is built on. Both of those
  were already supported API while the loader under them was not, so a mod could derive the
  contributor base but not read a catalogue of its own.
- `ExBlockNames.AddVariantQualifier(variantGroup, langPrefix)`: the block-name decorator handles
  `material`, `rock` and `brick` itself, and any other variant group is now registered by the mod
  that owns it rather than hardcoded. `ExBlockNames` moved from the family layer to
  `ExpandedLib.Helpers` with it - nothing about it was family-specific except the one clause.
- `Registries/ExModSystem.cs`: an abstract `ModSystem` base that runs `ExConfig.LoadAll`,
  `EntityRegistry.RegisterAll`, `CommandRegistry.RegisterAll` and `PreferenceRegistry.RegisterAll`
  in the right phase and order for you (preferences before commands on the client), with an
  overridable, empty-by-default hook after each; `PatchHarmony` folds `ExHarmony.PatchOnce`/
  `UnpatchAll` in too. `Config/ExConfig.cs`'s `LoadAll(api, assembly)` finds every generated config
  accessor in an assembly by the new `Config/ExConfigAccessorAttribute.cs`, which
  `ExConfigGenerator` now stamps on every accessor it emits. `samples/HelloExpanded`'s mod system
  is now an empty class deriving `ExModSystem`.
- `TestWorld.LoadAssets(modPath, gamePath?)`: drives the game's own `AssetManager` and
  `ModRegistryObjectTypeLoader` against a mod's real assets and compiled classes, registering the
  resulting `Block`/`Item` instances (real, variant-resolved) into the `TestWorld`. Covers JSON and
  code-first mods alike, base `game` domain assets excepted (see
  `docs/internal/research/2026-09-06-asset-loading-spike.md`); one test on the HelloExpanded sample
  in `mods/exlib/tests/Harness/AssetLoadingTests.cs`, wiki section "Real assets" in
  `mods/exlib/wiki/Testing-Harness.md`.
- `exlib-verify` (`infra/tools/ExlibVerify`, packed as the `ExpandedLib.Verify` .NET tool): checks
  a JSON-only mod folder or zip without the game running - every asset under `assets/` parses,
  every `patches/*.json` op applies against its real target (run through the game's own
  `Tavis.JsonPatch` engine), every `config/handbook/*.json` lang key resolves, every recipe
  ingredient/output code resolves against the mod, the game, and any `--mods`. Never a false
  error: a `dependsOn`/`condition` this run cannot evaluate, a `loadFromProperties` variant group
  it cannot expand, or a code in an unloaded domain is reported informationally instead. See
  `mods/exlib/wiki/Checks.md`, "Without the game: exlib-verify".
- `Blocks/ExBlockEntityBehavior.cs` and `Blocks/ExBlockEntityContainer.cs`: the `ExBlockEntity`
  `[Persist]`/`Persisted` convenience for a `BlockEntityBehavior` and a `BlockEntityContainer`
  respectively, for a block entity whose base slot is already spent. `ExpandedLib.Testing.TreeKeys`
  gains `AssertDeclaresBaseKeys`, a guard for a subclass `DeclareState` override that skips its own
  `base.DeclareState(state)` call - a golden alone does not catch it, since `PersistScan`'s
  `[Persist]` scan always contributes its keys regardless of what the override chain does.
- `dotnet new exlib-tests` (`templates/exlib-tests/`, identity `ExpandedLib.Templates.Tests`): a
  headless test project scaffold - one `TestWorld` block test, one code-first-definition golden
  fact, one shipped-JSON-parses fact, all passing vacuously until the mod they're generated
  alongside has content for them to check.
- `.github/workflows/release.yml`: on a `v*` tag, runs the test gate, the Cake `Package` and
  `PackageTesting` tasks, and `dotnet pack` for `ExpandedLib` and `ExpandedLib.Testing`, then
  uploads the mod zips, the dev bundle and both nupkgs as GitHub release assets. `templates/ci/tests.yml`
  joins the existing `templates/ci/smoke.yml` for a third-party repository.
- `ExpandedLib.csproj` and `ExpandedLib.Testing.csproj` are packable: `PackageId`, `Version` read
  from `modinfo.json`, `Authors`, `Description`, `RepositoryUrl`, `PackageReadmeFile`,
  `PackageLicenseExpression`. Nothing is pushed to NuGet.org by this repo yet - see `release.yml`'s
  commented push step.
- `LICENSE`: the repository is MIT licensed. `PackageLicenseExpression` is set to MIT on
  `ExpandedLib`, `ExpandedLib.Testing` and `ExpandedLib.Verify`, and every packaged mod zip and
  the developer bundle now carry a copy as `LICENSE.txt`.
- The testing docs are rewritten as a whole for the current shape (four test projects, the
  template, the release workflow): `docs/internal/testing.md`, the wiki's `Testing-Harness.md` and
  `Testing-API-Reference.md`, and a new source-tree guard, `HarnessSurfaceTests`, that fails when a
  public harness type or a `Testing-API-Reference.md` table entry drifts from the other.
- `exlib.testing`-adjacent internal test seams (not part of the public API - iiex's and siex's own
  suites are the only callers): `DriveProductionTick`/`DriveIdleTick` on
  `BlockEntityProductionMachine`/`BEBehaviorProductionMachine`, `DriveMonitorTick`/
  `ApplyStructureRotation` on `BlockEntityMultiblockStructure`, and `SetNetworkTypeForTest`/
  `ApplyOrientationForTest` on `BlockNetworkNode`, replacing a `ReflectionHelpers` call at each of the
  family's own call sites with a compile-checked one.
- `samples/HelloExpanded`: a buildable, bootable, tested third-party mod using the convenience
  layer end to end (a code-first block, `[Persist]` state, `ExInteraction`/`ExInfo`, a config value,
  a command, two headless tests). The wiki's [Getting Started](wiki/Getting-Started.md) walk is
  rewritten from section 4 onward to read alongside it, copying every snippet from the sample so it
  compiles.
- `exlib.testing` gains `Rigs.MachineRig` (drive one machine to a condition: `RunUntil`, `RunLive`,
  `RunWhile`), `Rigs.RegistryLawScanner` (a law that must hold across every concrete subclass of a
  base type, in the whole loaded assembly closure), `Rigs.ResourceInvariant<TState>` (a randomised-
  operation invariant, reporting the failing sequence and seed), and `Rigs.StaticStateCollection`
  (`EveryCollectionNameHasADefinition` catches a bare xUnit `[Collection("...")]` name with no
  `[CollectionDefinition(...)]`, which otherwise silently stops serializing anything). See
  [Testing Harness](wiki/Testing-Harness.md).
- `exlib.testing` gains six supported doubles wired straight into `TestWorld`, so a first inventory,
  config or logging test needs no NSubstitute knowledge: `Doubles.TestPlayer` (a real hotbar slot
  behind a substituted `IPlayer`/`IServerPlayer`), `Doubles.TestInventory` (a real multi-slot
  `InventoryGeneric`), `Doubles.TestModLoader` (`Api.ModLoader`, with `IsModEnabled` and the
  `IsModLoaded`/`HasMod`/`HasModId` aliases some mods probe by reflection), `Doubles.WorldConfigBag`
  (`World.Config`'s real tree), `Doubles.ModConfigFiles` (real files under a temp directory backing
  `Api.LoadModConfig`/`StoreModConfig`), `Doubles.RecordingLogger` (`Api.Logger`/`World.Logger`,
  queryable as `Entries`/`Errors`/`Warnings` instead of NSubstitute's `Received()`). `TestWorld` gains
  `Player()`, `Log`, `Config`, `Mods`, `ConfigFiles` and is now `IDisposable`. See
  [Testing Harness](wiki/Testing-Harness.md) "Doubles".
- `exlib.testing` gains `Rigs.HarmonyFixture` (applies a mod's Harmony patches once and reverts them
  on dispose, built on `ExHarmony` so the fixture and the library agree on idempotence) and
  `Doubles.TestChannels` (a client/server channel pair that round-trips a packet through the real
  `SerializerUtil` and delivers it synchronously to the other side's handler; `TestWorld.Channels`
  memoises one per name and is what `Api.Network.RegisterChannel`/`ClientApi.Network.RegisterChannel`
  now hand out, so a `ModSystem` that registers a channel needs no test-only wiring). `TestWorld`
  gains `ClientApi` for exercising `StartClientSide`. See [Testing Harness](wiki/Testing-Harness.md)
  "Testing Harmony patches" and "Testing packets".
- `RepoPaths.Register(domain, modFolder)`: a domain whose zip ships from a different mod's folder
  can now be declared instead of hand-edited into the harness; an unregistered domain falls back to
  `mods/<domain>` rather than throwing. `Repo.ReleasedHistory`: the released-code/version/debt
  registry each mod's own test `ModuleInit` now feeds, so the harness itself carries no mod's
  shipping history; `ReleasedCodes`/`ReleasedVersions`/`ReleasedCodeDebt` are unchanged forwarders
  onto it. See [Testing Harness](wiki/Testing-Harness.md) "Repo paths and released-code history".
- `exmod codes <mod>`: regenerates `{Mod}Blocks.g.cs` through the standalone
  `infra/tools/BlockCodeEmitter` console tool - builds the mod, writes the table, rebuilds.
- `exmod smoke [-Version <x.y>] [-Mods <dir>[,<dir>...]] [-Timeout 180] [-KeepData]`: the smoke lane
  - boots the real dedicated server against every built mod (or the given ones), runs
  `/exmod verify`, and fails on a boot timeout, an `[Error]`/`[Fatal]` log line or a non-clean verify
  summary, printing the offending lines. Runs on an unusual fixed port (42499) so it never disturbs a
  game already running on the same machine. CI runs it in its own job after the test job; see
  `templates/ci/smoke.yml` for a mod outside this repo. `exmod provision game -Kind server` on Linux
  and macOS now redirects to `.game/<slug>-server` instead of overwriting a client install left over
  at `.game/<slug>` for another platform, and `-Dest <absolute path>` is used as given instead of
  being joined onto the repo root. See [Testing Harness](wiki/Testing-Harness.md) "The smoke lane".
- `exlib.testing`'s soft untested-public-surface report (`PublicSurfaceTests`) fell from 64 to 42:
  behaviour tests for the command registry and its sub-commands, preferences, recipe/config profiles,
  the right-click-construction wiring, block/item code migrations and removals, the legacy shims, and
  the handbook unit-conversion patch. See [Testing Harness](wiki/Testing-Harness.md) "Examples".

- ⛔ **Breaking (`exlib.testing`, the dev-only harness bundle): `BlockCodeEmitter` no longer writes
  from inside a test run.** `EXLIB_WRITE_BLOCKCODES=1 dotnet test <project>` is gone; regenerate a
  mod's `{Mod}Blocks.g.cs` with `exmod codes <mod>` instead. `CheckOrWrite` only compares now.

- `ExpandedLib.Testing.TreeKeys`: golden-checks a block entity's `ToTreeAttributes` key set, the
  save-format proof behind converting a hand-written pair to `[Persist]`/`Persisted` (see
  [Block Entities](wiki/Block-Entities.md) § Converting a hand-written pair).
- `ExpandedLib.Checks`: the seven content guards that used to run only in the xUnit harness
  (dangling recipe/multiblock codes, missing lang coverage, pinned network nodes, a network node or
  membership missing part of its contract, a base-code prefix collision), now runnable against the
  live game through `ExlibChecks.All(ICoreAPI)`/`AssetCheckSource`, or against a custom
  `ICheckSource`. `ExpandedLibModSystem.AssetsFinalize` runs them after the catalogues load and logs
  the results (`ExlibConfig.RunChecksOnLoad`, default on); `/exmod verify [mod]` runs them on
  demand. See [Checks](wiki/Checks.md).
- `IExConfigAccess` gains `ExportJson`/`ImportJson`, and `ExConfigSyncModSystem` carries every
  `Manageable` config's live values from the host to each joining client and after a live
  `/exmod config set`, so a client's display, handbook and predictions agree with the server's
  tunables instead of its own local file. An import never writes to the client's config file. See
  [Config-System](wiki/Config-System.md) "What the client sees".
- `Registries.ExMods`: `IsLoaded`, `Version`, `AtLeast` (game-standard semver compare) and
  `WhenLoaded` for reacting to another mod being installed, plus `FlagKey(modId)` - the
  `"exlib:mod:<modid>"` world-config flag `ExModsModSystem` sets for every enabled mod, so a JSON
  patch's `condition` can gate on another mod with no C# code. `Registries.ExHarmony`:
  `PatchOnce(mod, assembly)` applies an assembly's uncategorised `[HarmonyPatch]` classes once per
  process, `PatchCategoryWhenLoaded` applies a `[HarmonyPatchCategory]` group only when a required
  mod is loaded, and `UnpatchAll(mod)` tears down both. Replaces the copied Harmony bootstrap in
  exlib, iiex and siex. See [Registries](wiki/Registries.md) "Other mods" and "Harmony".
- `BlockEntityProductionMachine`, `BlockEntityMultiblockStructure`, `BlockEntityNetworkNode` and
  `BlockEntityMachineStation` each gain the `Persisted`/`DeclareState` pair `ExBlockEntity` already
  had, layered on top of whatever keys they already write by hand - a machine, multiblock, network
  node or station now declares extra saved fields the same way. `ExBlockState` gains `Tree(key,
  write, read)` for a value that manages its own multi-attribute or nested-tree serialization (a
  `MoltenCharge`, for instance) against the same tree every other field writes into.
- `[Persist]` (`ExpandedLib.Blocks.PersistAttribute`): mark a field or auto-property of a block
  entity and it is saved with no `DeclareState` entry at all. Supports `bool`, `int`, `long`,
  `float`, `double`, `string`, an enum (stored as its underlying `int`), `BlockPos`, `ItemStack`,
  and `IPersistable` - a value type that writes itself into a nested tree. `Legacy` names an older
  key, read only when the new one is absent and never written back. See
  [Block-Entities](wiki/Block-Entities.md).
- `ExRightClickConstructable` publishes `IProductionReadiness`: `IsReadyToProduce` is `IsComplete`
  and `StopsProductionWhenNotReady` is `true`, so a machine that carries the behaviour and hosts a
  production tick waits for construction with no gate to write by hand. `GatesProduction`
  (JSON `gatesProduction`, default `true`) opts a machine that must keep ticking while unfinished
  out of the gate; the definition builder's `Construction(...)` DSL gains a matching
  `GatesProduction(bool)` step. See [Construction](wiki/Construction.md) "Construction gates
  production".

- Every catalogue loader's `Load(ICoreAPI)` (metals, liquids, material roles, process routes,
  process jobs, bay occupancy) now returns a `Catalogues.CatalogueLoadReport`: files read,
  entries accepted, and the errors, each naming its asset. `AssetsFinalize` logs one summary
  line per catalogue plus one `Error` line per failure, instead of the bare per-error loop it
  had before. See [Extending-Processes](wiki/Extending-Processes.md) "What the log tells you".
- Every catalogue registry exposes a static `Contributors` (`Catalogues.CatalogueContributors`):
  register a code contribution once from `Start` and it is re-applied after every JSON read, so
  it survives the clear that precedes each `AssetsFinalize`. `MaterialRoleRegistry.RegisterContributor`
  /`ClearContributors` now forward to it. See [Extending-Processes](wiki/Extending-Processes.md)
  "From C#, and surviving the next load".
- `Definitions.KnownRootKeys`: the top-level keys the game's object loader reads for a block or
  item type, taken by reflection from the loader's own types. `ExDefinitionModSystem` now logs a
  Warning for every registered def's root key that is not one of them, naming the def and the key,
  instead of writing a mistyped key into the JSON with nothing to say it is never read.
- `MultiblockLayoutBuilder.Core(char)` marks the anchor glyph; `Build()` then throws naming the
  layout, the declared `Origin` and where the anchor actually landed when `Origin` is not its
  negation, instead of building a structure silently offset from the block the player placed.
  Optional - a layout that never calls `Core` is unchecked, as before.
- `ExDefinitions` gains a static `Logger`; re-registering an asset location from a different
  assembly than last time now logs a Notification naming the location and both assemblies, instead
  of replacing silently.
- `StructureFillers.CanPlace`/`PlaceFillers`/`RemoveFillers` log an Error, once per process, naming
  `FillerCode` when it fails to resolve to a registered block, instead of quietly refusing to place
  or remove anything.
- `EntityRegistry`'s cross-mod `Class<T>()`/`Behavior<T>()` domain fallback (an assembly with no
  `[assembly: ExDomain]` that was never registered) now logs a Warning naming the assembly, instead
  of silently resolving into the caller's own domain.
- `Structures.CellGrid`/`GridPlane`/`GridOptions` and `Structures.SymbolLegend<T>`: the grid-and-legend
  core the multiblock, filler and scene-diagram ASCII DSLs now all draw over. `MultiblockLayoutBuilder`
  gains `Slice(int x, string grid)` and `Face(int z, string grid)`, matching the elevation grids the
  filler layout already had; a layout may mix `Layer`/`Slice`/`Face` freely. See
  [Multiblock-Structures](wiki/Multiblock-Structures.md) "One grid, three uses".
- `exlib.testing` gains `Checks.LayoutTable.From(ExBlockDef)`/`.Rotated(def, angle)`, reading a
  code-first multiblock layout's emitted table back into a per-cell block code, and `Scenes.SceneGrid`,
  the grid-core derivation `SceneDiagram` now forwards to.
- A JSON-only megablock, no C# required: `BlockFilledMegastructure` is now registered as
  `ExFilledMegastructure` (previously an abstract base only), and `Structures.BlockEntityMultiblock`
  (registered `ExMultiblock`) is a concrete `BlockEntityMultiblockStructure` reading orientation from
  the block's own `side`/`orientation` variant and its incomplete/complete messages from
  `<domain>:multiblock-<blockpath>-incomplete`/`-complete`, falling back to the new
  `exlib:multiblock-incomplete`/`-complete` lang keys. `Structures.JsonMultiblockLayout` resolves a
  block's `attributes.multiblockLayout` ASCII grid - the JSON twin of `MultiblockLayoutBuilder` - into
  the same `multiblockStructure` and, absent an explicit `fillerOffsets`, the same derived footprint a
  code-first definition would emit. See [Multiblock-Structures](wiki/Multiblock-Structures.md)
  "From JSON only".
- `ExItemDef` gains every `ExBlockDef` method whose JSON key also exists on an itemtype:
  `Behavior`/`Behavior(name, props)`/`Behavior<T>()`, `Handbook`/`HandbookExclude`, `SkipVariants`,
  `ShapeByType`, `TextureByType`, `AttributeByType`, `RootKeyByType`/`RawByType`, the codeless
  `VariantGroupFromProperties(path)` overload, and positional `GuiTransform`/`FpHandTransform`/
  `TpHandTransform`/`GroundTransform` overloads taking translation, rotation, origin and scale
  directly. `ExBlockDef.RenderPass`/`DrawType` also take `EnumChunkRenderPass`/`EnumDrawType`
  directly, and `FaceCullMode` takes `EnumFaceCullMode` - all three write the same string the
  existing string overload does. See [Code-First-Definitions](wiki/Code-First-Definitions.md).
- `Registries.ReflectionScan` gains `GetCandidateTypes(IEnumerable<Assembly>)` (a deterministic,
  cross-assembly candidate scan) and `ForEachAttributed<TAttr, TInstance>` (the shared
  find-attribute/activate/register loop); `CommandRegistry`, `PreferenceRegistry` and
  `BlockMigrationModSystem`'s discovery now route through them instead of each keeping its own copy.
- `Helpers.ExOrientation.SegmentedCode`: splits a block code's path on `-` so a caller can index,
  rewrite and rejoin one dash-segment at a time. `MultiblockLayoutBuilder`'s orientation-segment scan
  and `MultiblockFacings`' rotation now share it.
- `Helpers.ExMeshCache` gains `GetOrCreateRef`/`DisposeGroup` for a cache of uploaded GPU
  `MultiTextureMeshRef`s grouped for disposal together, and `Helpers.ExHighlightSlots.Reserve(key)`
  hands out a stable, distinct `world.HighlightBlocks` slot id per key instead of a hand-picked
  literal.
- `Registries.RegistrySubCommand<T>`: derive once for a `/exmod <name>` sub-command over a keyed
  registry (list every code, show one, hand the rest to your own `Set`). `ConfigSubCommand` and
  `RecipesSubCommand` now derive from it instead of each keeping its own list/show/set command
  loop. See [Commands](wiki/Commands.md) "Your own /exmod sub-command for a registry".
- `Catalogues.ContributedCatalogueLoader<TSet, TRegistry>`: derive once for a hand-parsed JSON
  catalogue with C# contributors (one asset path, an unknown-key audit, a merge that reports its
  clashes, contributors re-run after every reload). `ProcessRouteLoader`, `ProcessJobLoader` and
  `BayOccupancyLoader` are now this base with their own schema; each file is parsed exactly once
  (the report used to parse twice, once to count and once to merge) and a clash is always named
  after the file it actually came from, never the family/machine/store name a mismatched count used
  to fall back to once any earlier file in the batch failed to parse. See
  [Extending-Processes](wiki/Extending-Processes.md) "Adding a catalogue of your own".
- `docs/design/conventions.md` "Catalogue registry verbs": the naming law every catalogue registry
  follows (`Register`/`Contribute`/`Load`/`Clear`/`Contributors`), guarded by
  `CatalogueNamingTests`.
- `ExBlockAccess`: `BlockEntity<T>`/`TryGetBlockEntity<T>` for the `GetBlockEntity(pos) is X be`
  null-guard every machine wrote by hand, plus `Neighbour<T>`/`Neighbours<T>` for the one-step and
  around-a-position walks. See [Helpers-and-Renderers](wiki/Helpers-and-Renderers.md) "Finding
  block entities".
- `ExSide`: `IsServer`/`IsClient` over `ICoreAPI` and `IWorldAccessor`, for the
  `Api.Side == EnumAppSide.X` check every machine wrote by hand. See
  [Helpers-and-Renderers](wiki/Helpers-and-Renderers.md) "Which side".
- `ExInteraction.Of`/`Interaction`: reads what a click carried - held stack, tool, sneak, clicked
  face, side - without deciding which side acts on it. See
  [Helpers-and-Renderers](wiki/Helpers-and-Renderers.md) "Reading a click".
- `ExInfo`: `Lang`/`LangIf`/`Measure` extension methods on `StringBuilder` for a `GetBlockInfo`
  body's `Lang.Get` lines, including one that folds a value through `ExMeasure` for both display
  systems. See [Helpers-and-Renderers](wiki/Helpers-and-Renderers.md) "Block info lines".

### Changed

- The `ExpandedLib` package now carries the config and lang source generators (packed as analyzers
  under `analyzers/dotnet/cs/`) and the MSBuild plumbing behind `GamePath` resolution, provisioning,
  asset globs and version stamping (packed under `build/`), so a consumer references one package and
  needs no props of its own beyond a `TargetFramework`. Package versions for `ExpandedLib`,
  `ExpandedLib.Industry` and `ExpandedLib.Testing` are central, in `Directory.Packages.props` at the
  repo root, read once from `modinfo.json`.
- The framework no longer names its consumers' test assemblies: `InternalsVisibleTo` on `exlib.dll`
  grants only `ExpandedLib.Tests` and `ExpandedLib.Testing`. The internal seams `IronIndustryExpanded.Tests`
  and `SteelIndustryExpanded.Tests` reached directly now go through harness hooks
  (`MachineTestHooks`, `StructureTestHooks`, `NetworkNodeTestHooks` - see
  [Testing API Reference](wiki/Testing-API-Reference.md)) instead.
- ⛔ **Breaking: `ExpandedLib.Industry` is its own assembly and its own NuGet package.** The
  family's content layer - pipes, molten metal, mechanical power, metals, heat - now builds as
  `exlib.industry.dll` from `mods/exlib/industry/`, and ships beside `exlib.dll` inside the same
  exlib mod folder, so nothing changes for a player: one mod, one modinfo, one download. A mod
  that uses those types adds a reference to `ExpandedLib.Industry` alongside `ExpandedLib`; no
  namespace, type name or registered class key moved, so a save and a shipped blocktype JSON are
  unaffected. The framework assembly no longer references the domain layer in either direction,
  which the compiler now enforces and `IndustryBoundaryTests` proves.
- The metal catalogue and the generated metal item family are loaded by the domain layer's own
  `IndustryModule` rather than by exlib's mod systems calling into it, through the new companion
  assembly mechanism below. Its driver runs at ExecuteOrder 0.03, under `ExDefinitionModSystem`'s
  0.04 so the emitted items are registered before that system injects them, and under
  `ExpandedLibModSystem`'s default 0.1 so metals still load before liquids and material roles.
- `scripts/exmod.ps1` is split from one 900-line file into a dispatcher plus one file per
  lifecycle stage under `scripts/exmod/` (`provision.ps1`, `src.ps1`, `run.ps1`, `dist.ps1`,
  `windows.ps1`); each command registers itself next to its own implementation, so `exmod`
  prints a grouped command list and `exmod help <command>` prints one command in detail.
  `scripts/exmod.sh` is unchanged. See [Contributing](../../CONTRIBUTING.md) "Running things".
- ⛔ **Breaking: construction now gates production by default.** A machine whose blocktype carries
  `ExRightClickConstructable` and hosts a production tick used to tick through its own unfinished
  construction unless it named the gate itself; the behaviour's new `IProductionReadiness` answer
  now waits for it on every such machine. A machine that must keep ticking while unfinished sets
  `gatesProduction: false` in its `entityBehaviors` properties (or `.GatesProduction(false)` in the
  `Construction(...)` builder).
- `FillerLayoutBuilder` no longer refuses a footprint that mixes `Layer`/`Slice`/`Face` grids; the
  grid core tracks every drawn cell regardless of which call drew it, so mixing is simply supported.
- ⛔ **Breaking: `ExBlockDef.Raw`/`RawByType` and `ExItemDef.Raw` are renamed `RootKey`/`RootKeyByType`.**
  The old names are kept as `[Obsolete]` forwarders for one release (see
  [Supported API](wiki/Supported-API.md) "Obsolete members"). `Raw` writes a top-level key the
  object loader reads only when it is a real blocktype/itemtype key; the new name says so.
- ⛔ **Breaking: `ExLiquids.Load(ICoreAPI)` moves to `LiquidCatalogueLoader.Load(ICoreAPI)`.** A
  registry does not read assets under the catalogue naming law; `ExLiquids.Load` is kept as an
  `[Obsolete]` forwarder for one release (see [Supported API](wiki/Supported-API.md) "Obsolete
  members"). `ProcessRouteRegistry` now stores its families in an `ExKeyedRegistry<ProcessRoute>`
  instead of a hand-rolled dictionary, with no visible change; `ProcessJobRegistry`,
  `BayOccupancyRegistry` and `MaterialRoleRegistry` keep their own dictionaries because each key
  holds a list of entries, which `ExKeyedRegistry<T>`'s one-value-per-key shape does not fit.

- ⛔ **Breaking: an unrecognised JSON key is now a load error, not a silently dropped field.**
  Every catalogue asset (`config/metals/*.json`, `config/liquids.json`,
  `config/materialroles.json`, `config/processroutes/*.json`, `config/processjobs/*.json`,
  `config/bayoccupancy/*.json`) is bound strictly: a misspelt or retired key fails that one
  file, named with the file and the key, rather than loading with the field quietly ignored.
  Every warning and error also now names the asset it came from.
- ⛔ **Breaking: the family half of exlib moved to `ExpandedLib.Industry.*`.** Pipe, molten and
  mechanical-power networks, the metal, heat and molten-material catalogues, and their shared
  helpers now live under `ExpandedLib.Industry.Pipes`, `.Molten`, `.MechanicalPower`, `.Metals`,
  `.Heat`, `.Helpers` and `.Materials`. Type names and registered codes are unchanged - only the
  namespace moved - so a `using` fix is the whole cost of the update. `ExpandedLib.Industry.*` is
  public and reusable but carries no stability promise: it is the family's content layer, not
  the framework contract.
- `CellRole` is now a string-keyed `readonly record struct` (`CellRole.Of(key)`) instead of an
  exlib-declared enum; a machine's roles are declared by the mod that owns it (see iiex's
  `FurnaceCellRoles`). exlib itself declares no roles.
- ⛔ **Breaking: `mods/exlib/src` is reorganised so one top-level folder is one namespace** (see
  [Supported-API](wiki/Supported-API.md), [conventions](../../docs/design/conventions.md) "How
  exlib is laid out"). Type names, members and registered codes are unchanged - only folders,
  namespaces and `using`s moved:
  `ExpandedLib.Registries.{Entities,Commands,Preferences,Recipes}` and `ExpandedLib.{Commands,Preferences}`
  -> `ExpandedLib.Registries`; `ExpandedLib.Registries.Config` -> `ExpandedLib.Config`;
  `ExpandedLib.{Processes,Materials,Fluids,Storage}` and the catalogue loader/report classes that
  stayed in `ExpandedLib.Registries` -> `ExpandedLib.Catalogues`;
  `ExpandedLib.Blocks.{Behaviors,Construction}` -> `ExpandedLib.Blocks`;
  `ExpandedLib.Blocks.{Migrations,Healing}` -> `ExpandedLib.Migrations`;
  `ExpandedLib.Blocks.Structures` -> `ExpandedLib.Structures`;
  `ExpandedLib.Blocks.Machines` -> `ExpandedLib.Machines`;
  `ExpandedLib.Blocks.Networks` -> `ExpandedLib.Networks`; `ExpandedLib.Renderers` and
  `ExpandedLib.Patches` -> `ExpandedLib.Helpers`.
- ⛔ **Breaking (`exlib.testing`, the dev-only harness bundle): `ExpandedLib.Testing.Doubles` is
  folded into `ExpandedLib.Testing`.** The test doubles (`StubNetwork`, `TestNetworkBlock`,
  `CapturingNode`, `SeverableNode` and the rest) move from `Doubles/` namespace into the harness's
  single namespace; the folder stays, only the `using` goes. `using ExpandedLib.Testing;` is the
  whole cost of the update.
- `MetalRegistry.DefaultRecoveryFallback` replaces the removed `ExlibConfig.MetalRecoveryFallback`:
  the drop-item fallback for an unresolved molten solid drop is content knowledge, not a
  framework default, so a dependent mod now owns its own fallback (iiex's `IiexConfig`).
- The published API boundary is enforced: every public type of `exlib.dll` outside
  `ExpandedLib.Industry` is now either listed on the new [Supported
  API](wiki/Supported-API.md) page or marked `[EditorBrowsable(EditorBrowsableState.Never)]`
  (public because the engine instantiates it by reflection, not because a mod should call it).
  A guard test keeps the two in step. Going forward, a public member on the supported surface is
  removed only after one full release spent marked `[Obsolete]` naming its replacement.
- `AssetCatalogueLoader`, `ExConfigFiles`, `ExDefinitionOrigin`, `ExJson`, `ExPreferencesConfig`,
  `ExSyntheticAsset`, `FillerSlab` and `NetworkHighlightRequest` are `internal` on the framework
  surface this page covers.
- A tuned `MetalRecoveryFallback` under the `exlib` section of `ex_values.json` is not carried into
  the new `iiex` key - the field moved mods, not just section, so it must be set again there.
- `ExpandedLibModSystem`, `ExDefinitionModSystem`, `ChunkColumnSweeperModSystem`,
  `NetworkHighlightModSystem`, `ExmodCommand`, `ConfigSubCommand`, `HealSubCommand`,
  `MeasureSubCommand`, `NetworkSubCommand`, `RecipesSubCommand`, `HandbookUnitPatch`,
  `ExlibConfig`, `MeasurePreference` and the generated `ExlibBlocks` / `Structurefiller` types
  are marked `[EditorBrowsable(EditorBrowsableState.Never)]`: public because the engine has to
  see them, not part of the supported surface.
- New wiki pages: [Supported API](wiki/Supported-API.md) (the published boundary),
  [Code-First Definitions](wiki/Code-First-Definitions.md) and [Lifecycle](wiki/Lifecycle.md).

### Fixed

- `exlib-verify` reported a patch aimed at a code-first definition as a missing target. exlib
  injects one synthetic blocktype/item/recipe asset per definition before the patch loader runs,
  so those files exist in a running game and never on disk - siex's iiex refractory-pipe compat
  patch alone produced 27 errors for patches that apply correctly. A target missing from a domain
  whose mod ships an assembly is now an informational finding naming why it cannot be checked
  headlessly; a target missing from a JSON-only domain is still an error.
- ⛔ **Breaking: `AssetCheckSource.Domains` now covers only exlib and the mods that depend on it.**
  It used to answer every loaded mod, vanilla's own `game`/`survival`/`creative` included, whose own
  incomplete non-English locales alone produced on the order of 164,000 findings on a full-tree
  smoke run. `/exmod verify <domain>` still checks any domain named explicitly, in or out of scope.
- ⛔ **Breaking: `LangCoverageCheck` now guards the `en` locale only.** An unresolved `en` key is
  what renders raw on screen; a gap in some other shipped locale falls back to English instead, and
  tracking parity across a mod's other locales moved to `ExpandedLib.Testing.LangCoverage` and each
  mod's own `LangParityTests` (a repository-time concern the running game never needs to fail over).
- `MultiblockCodesCheck` matches a layout's wanted code against the registry segment by `-`
  rather than only trimming a trailing `*`, so a wildcard in the middle of a reference
  (`furnace-blastcore-*-n`, any tier) or more than one in one reference
  (`furnace-firebox-*-*`) resolves against the concrete variant it means instead of reporting a
  false dangling code.
- `ExBlockDef.MineTool`/`ExItemDef.MineTool` are now a no-op, `[Obsolete]`: `mineTool` was never a
  key the loader reads, and the mining-tool preference the callers wanted is already carried by
  `Material` (the per-material mining-speed table) and `MiningTier`; the three iiex call sites and
  the sample drop the dead call. `HandbookExclude` now sets `attributes.handbook.exclude`, matching
  the handbook system's own read, instead of a top-level `handbook` key nothing reads. Two dead
  `RootKey("temperatureDamage", ...)` calls on the wrought-ball and stock items are removed for the
  same reason - no vanilla key by that name exists. Found by the sample's smoke run and fixed as
  Task E2's boundary re-check.
- `KnownRootKeys` walked only public instance members, missing several loader fields the vanilla
  types declare `private` with a `[JsonProperty]` (`CollisionBox`/`SelectionBox`, the deprecated
  `heldTpIdleAnimation`); it now walks every type in the hierarchy with `DeclaredOnly` to see them
  too. The known-key audit also stopped flagging any `*ByType` root key as unknown - vanilla's own
  loader (`RegistryObjectType.solveByType`) resolves that suffix generically for any field name
  before binding, so the un-suffixed name need not itself be a member KnownRootKeys can see. Between
  the two, the smoke lane's `root key '...' is not a ... key the game reads` warnings drop from
  several hundred to zero.

### Repository

- exlib is now its own repository, `ringavirda/exlib`, extracted with full history from the
  `modding-vsexpanded` monorepo and flattened to the root (`src/`, `industry/`, `testing/`,
  `generators/`, `tests/`, `build/`, `assets/`, `wiki/`, `docs/` beside it; `samples/` and
  `templates/exlib-tests/` unchanged). The family mods (iiex, siex) stay in `modding-vsexpanded`,
  now `exmods`, and reference this repository by NuGet package or, inside a workspace checking out
  both, by project.

## [0.7.3] - 2026-08-13

Six public subsystems landed between 0.7.0 and 0.7.2 without a changelog entry; they are
recorded here together with 0.7.3's own packaging work.

### Added

- **Code-first definitions** (`ExBlockDef` / `ExItemDef` / `ExRecipeDef`). Blocks, items
  and recipes are authored in C# and injected as synthetic assets at `ExecuteOrder 0.04` -
  above the base index, below the JSON patch loader (0.05) and the object loader (0.2) - so
  vanilla variant expansion, the atlas, block-id assignment and other mods' JSON patches all
  still apply. Fluent builders with derived codes, an ASCII multiblock layout DSL validated
  at load, and type-safe class binding.
- **Process extension contract** (`StageLadderRegistry`, `ProcessJobRegistry`, `SpecSchema`,
  `ProcessExtensions`, `ItemDie`). Merged catalogues read from `config/stageladders/*.json`
  and `config/processjobs/*.json`, load-order-independent, with a versioned spec format:
  an absent `schema` reads as 1, an older one falls back, and a **newer one is refused** with
  an error naming both versions.
- **Metal, material-role, liquid and heat catalogues** (`MetalRegistry`, `MaterialRoleRegistry`,
  `ExLiquids`, `HeatBalance`), each backed by a JSON catalogue a dependent mod contributes to.
- **XML documentation now ships** (`exlib.xml`, beside the dll in every zip), so a consumer
  gets IntelliSense over the public surface instead of bare signatures.
- **Source generators are distributed** in the `exlib-testing` bundle under `analyzers/`.
  The config-accessor recipe in the wiki previously could not compile outside this repository.
- **`[assembly: ExDomain]`** declares the domain an assembly's registered classes are keyed
  under, so `Class<T>()` / `Behavior<T>()` resolve correctly across assemblies.
- **`[ExDefDomain]`** lets one assembly emit definitions into more than one domain.
- **`BlockPipe.Tier`** - a pipe's family, read from its `tier` variant, with
  `PlatedTier` / `CastTier` / `RolledTier` naming the three this project ships. A pipe that
  declares no tier takes the default rating, throughput and joint, which is what every
  fitting does.

### Changed

- ⛔ **Breaking: a pipe's tier is a variant, not its domain.** `RegisterBurst`,
  `RegisterThroughput` and `RegisterJoint` are keyed on the tier name rather than the mod id,
  and `BlockPipe.Segments` takes the tier as a second argument
  (`Segments(domain, tier)`; pass `null` for an untiered family). A mod may now ship several
  tiers under one domain, which one domain per tier made impossible. Callers registering with
  `Mod.Info.ModID` must pass their tier name instead - the registration is otherwise silently
  unused and every segment falls back to the defaults.
- ⛔ **Breaking: `BlockPipePassthrough.Passthroughs` takes the tier too**
  (`Passthroughs(domain, tier)`, `null` for an untiered family). Two tiers shipping a passthrough
  apiece carried one code between them, so under a single domain the later registration replaced
  the earlier with no error; the tier is what keeps both. The sheet texture is now selected by tier
  rather than by domain.
- **Assembly identity is real.** Every release previously shipped `AssemblyVersion` and
  `FileVersion` `1.0.0.0`; both are now read from the mod's own `modinfo.json`.
  `AssemblyVersion` stays `major.minor.0.0` so a patch does not break a dependent's binding.
- **Type-safe class binding works across assemblies.** `EntityRegistry.KeyFor` resolves the
  domain from the *type's* assembly rather than the caller's. Naming a class from a dependency
  previously produced a key nobody had registered - which compiles, fails at world load, and on
  the block half is not logged.

### Fixed

- **An unregistered network type no longer takes a world down.** `AddNode` runs inside chunk
  load; a mistyped `networkType` threw out of it. It now logs an error naming the block, the
  position, the requested type and the registered types, and adds no node.
- **The test harness runs outside this repository.** Its path helpers probed upward for a file
  literally named `VintageStory.sln` and threw everywhere else, which disabled the goldens, the
  block-code table and the handbook sync for any outside consumer. Any `.sln`/`.slnx`/`.git`
  now marks a root, overridable with `EXLIB_REPO_ROOT`.
- Nineteen XML doc references that pointed at nothing, surfaced by enabling the doc file.

## [0.7.0] - 2026-06-21

### Added

- **Orphaned block-entity healer.** A server-side system that recreates a block
  entity when a block is left in the world without one - e.g. a block entity
  discarded on chunk load (a load exception) or lost to a server desync, which
  otherwise leaves an inert, often unbreakable block. It runs automatically as
  chunks load (and once over already-loaded chunks at startup), scoped to block
  entities registered through the mod's attribute system so vanilla/other-mod
  entities are never touched.
- **`/exmod heal` command.** Sweeps the loaded chunks and recreates orphaned block
  entities on demand, for an operator who does not want to wait for the automatic
  on-load pass. Server-side, gated behind the `/exmod` root's `controlserver`.
- **Config framework.** A generic, versioned per-mod config store with
  source-generated value accessors, version-reset migrations, and legacy file-name
  renaming. Values can be marked manageable and edited live via
  `/exmod config <mod> [value] [new]` - applied immediately, no world reload.
- **Min/max range gates** on config values: out-of-range edits are rejected with a
  clear message.
- **Recipe-cost profiles.** A per-mod catalogue framework that rebalances grid and
  right-click-construction ingredient quantities, switchable with
  `/exmod recipes <mod> <level>`.
- **Content-gating helper** (`ExContentGate`) for hiding a block/item from creative
  and the handbook and removing its recipes - the framework behind smex's mold
  toggle.
- **Command framework.** Attribute-driven `[CommandRegister]` / `[SubCommandRegister]`
  registration under a shared `/exmod` (server) and `.exmod` (client) root, so
  dependent mods hang their own sub-commands off one root.
- **Production-machine base** (`BlockEntityProductionMachine`) and machine-port
  helpers, shared by engines, furnaces, converters and sub-machines.
- **Legacy support framework.** Shims and polyfills that let the family build and run
  against Vintage Story 1.21 and 1.20 alongside 1.22.
- **Russian and Ukrainian** translations.

### Changed

- **Internal reorganization** into `Blocks/{Networks,Structures,Machines,Migrations,Construction,Healing}`,
  `Registries/{Entities,Commands,Config,Preferences,Recipes}`, `Helpers`,
  `Renderers` and `Legacy`.
- **Registration attributes split.** The single `[EntityRegister]` became
  kind-specific `[BlockRegister]`, `[ItemRegister]`, `[BlockEntityRegister]`,
  `[BlockBehaviorRegister]`, `[BlockEntityBehaviorRegister]` and
  `[CollectibleBehaviorRegister]`, each validating that the class derives from the
  expected base type.
- **Right-click-construction salvage:** the ratio of materials dropped when a
  partially-built or finished structure is broken is now configurable.
- **Multiblock structures read live config changes** without a world reload.

### Fixed

- Right-click-constructable blocks ignored their last construction stage when
  computing dropped materials.
- Non-pipe network blocks could incorrectly burst.
- Block display-name ordering and assorted localization issues.
- `/exmod config` value display formatting.

## [0.6.0] - 2026-06-16

### Added

- **Command framework.** A shared `/exmod` (server) and `.exmod` (client) command
  root, with a server-side version and privilege handling, so dependent mods hang
  their sub-commands off one root.
- **Measurement helpers** (metric/imperial) and a **per-player preference registry**,
  with a handbook patch that converts displayed measurements to the player's units.
- **Network-highlight** subcommand and a **base surface renderer**.
- **Source generators** that bake block/item JSON attributes into generated class
  members.
- **Config migrations** from older versions.

### Changed

- The **structure filler** mirrors the principal block's block-info.

## [0.5.1] - 2026-06-14

### Changed

- The **migration system** now also covers items held in inventories, not just placed
  blocks.

### Fixed

- **Right-click-constructable** wildcard handling and the names shown for missing
  materials.

## [0.5.0] - 2026-06-13

The first standalone release of the shared library, extracted from Steelmaking
Expanded (internal `0.1.0` groundwork promoted to `0.5.0`).

### Added

- **Block-network framework** (nodes, connectors, graph) backing gas pipes and molten
  canals.
- **Multiblock structure framework** with right-click construction.
- **Attribute-driven registration** for blocks, items and behaviors.
- **World-migration system** for updating old blocks.
- Shared **particle, sound and orientation** catalogues and helpers.
