# Live-Server Scenarios
**Status** proposed 2026-09-23   **Mod** exlib's testing family (the live mod `exliblive`: the runner, the scenario player, the generic layer, the coverage probe, `/exmod scenario`), exlib (the creative sources), extools (`exmod scenario`), every mod's `tests/live/` files
**Owns** the two layers of live tests: generic scenarios derived from every registered block's definition, and mod-specific scenarios that test processes, factory lines among them; the scenario file format, its citations of the player guides and where the files live; the scenario player; the creative sources; the step kinds (placing, building, laying pipe; supplying power, fluids, molten metal and items; interacting; stepping time; reloading; breaking; expecting); plots, climates and positions; the coverage report of every overridden hook; the `[scenario]` output grammar and its closing lines; the failure detail and its suspect; the scratch world `exmod scenario` boots and what `-Keep` leaves running.
**Depends on** [multiblock](mechanics/multiblock.md) (the footprints, layouts, cell roles, monitor tick and missing-cell report the structure cases drive); [pipe-network](mechanics/pipe-network.md) (the pool the fluid sources feed); [mp-energy](mechanics/mp-energy.md) (the vanilla mechanical power the power source drives, bridged at the flywheel hub)

---

## Role

The harness runs a machine in a fake world. A scenario runs the same machine on a real dedicated
server: the engine's own chunks, block entity lifecycle, tick scheduler, network systems, weather,
save database, and a real server player placing, using and breaking blocks, with nothing faked and
nobody connected. It checks mechanics, not looks: rendering, animation, sound, particles and anything a
client computes are outside it.

Scenarios come in two layers.

- **Generic scenarios** are derived, never authored per block. The runner enumerates every block the
  loaded family mods register, with every variant and orientation, and runs the cases its definition
  implies: its block class, entity class, block behaviours, entity behaviours and attributes. Every
  block places, is used, ticks, survives a reload and breaks with its drops; every megablock,
  multiblock, construction, network member, mechanical-power member, container and `nosnow` layout gets
  the case for what it is. A new block gets them the day it is registered.
- **Mod-specific scenarios** are files a mod writes, and they test processes: the boiler raising
  pressure, the blower filling its run. The largest are factory lines, whole setups written from the
  player guides the mods ship and run the way the guide tells a player to run them.

The coverage report ties the two together. For each block it lists every hook its classes override or
implement from an exlib interface, and which case exercised it; a hook no case touched prints as
uncovered, and a run can be told to fail on one. That is the part that catches what nobody remembered to
test.

`exmod smoke` answers "does the server load this". A scenario answers "does this block stand up, run,
reload and come apart cleanly in a live world, does its process work, and does it work the way its
guide says".

---

## Settled rulings

Player (ruled 2026-09-23): every player verb, breaking included, runs as a real server player that the
runner builds through the engine's own classes, so every case runs autonomously. See The scenario
player.

Inputs (ruled 2026-09-23): power, fluids and the other inputs a process needs come from creative source
blocks in exlib, which are testing infrastructure and ordinary creative-mode blocks at once. See
Creative sources.

Layouts (ruled 2026-09-23): nobody builds reference layouts by hand. Factory lines are written by agents
from the guides the mods ship (the wiki pages and the handbook) and the old diagrams, with reasoning
filling what they leave out, and each line's run says whether it works. See Factory lines.

`-Keep` (ruled 2026-09-23): a player who joins a kept server is an admin, and the scratch server accepts
connections from localhost only.

Shipping (ruled 2026-09-23): the runner belongs to exlib's testing family and ships only once fallen has
confirmed that the feature works; until then extools stages it for runs. See Code.

Layers (ruled 2026-09-23): unit tests and content checks, the harness, and scenarios are three layers
that each run where their cost fits; scenarios add to the harness and replace none of it. See When each
layer runs.

---

## How it works

### The world

`exmod scenario` boots the server the way `exmod smoke` does: a scratch `--dataPath`, the staged mods,
the port passed as `--port`.

The port is the scenario server's own, 42498, not smoke's 42499, so a smoke run and a scenario run share
the machine; `-Port` moves it, which is how two lanes run scenarios at the same time. The install is the
one smoke boots for the series `-Version` names (default 1.22): `Resolve-SmokeServer` takes
`.game/<series>-server` before `.game/<series>`, which for 1.22 is 1.22.7 today, while builds and the
harness default to `.game/1.22` (1.22.6). The `run` line prints the booted version
(`GameVersion.OverallVersion`), so a result is read against the patch it ran on.

It writes `serverconfig.json` into the scratch data path before the first boot, so the server creates
this world:

| Setting | Value | What it buys |
|---|---|---|
| `Ip` | `127.0.0.1` | the server listens on loopback only (the key exists in `serverconfig.json` and defaults to null, every address); `exmod client` runs on the same machine and joins `localhost:<port>` |
| `WorldConfig.WorldType` | `superflat` | vscreativemod's `GenBlockLayersFlat` lays the layers of `creative/worldgen/layers.json` (claystone and two soils) and turns entity spawning off; vsessentialsmod's `GenMaps` still generates the climate map for a superflat world |
| `WorldConfig.PlayStyle` | `surviveandbuild` | the default playstyle; survival and the family mods load as in play |
| `WorldConfiguration.gameMode` | `creative` | a joining player can fly to a plot and look; the scenario player sets its own mode per step |
| `WorldConfiguration.snowAccum` | `true` (the default) | vanilla's snow accumulation runs, which the `nosnow` cases drive |
| `WorldConfiguration.temporalStorms`, `temporalStability` | `off`, `false` | nothing temporal lands in a plot |
| `PassTimeWhenEmpty` | `true` | without it `ServerSystemCalendar` stops the calendar while no client is playing, and every machine that reads game time stands still during an automated run. Tick listeners run either way |
| `WhitelistMode`, `AdvertiseServer` | `Off`, `false` | |
| `DefaultRoleCode` | `admin`, with `-Keep` only | a joining client can run `/exmod scenario`, `/tp` and `/gamemode` |

Commands written to the server's stdin run on the main thread as the console player
(`ServerConsolePlayer`), which holds every privilege and has no entity and no inventory. The cases
themselves act through the scenario player.

### Where the files live

| Repository | Scenarios | Factory lines | Schematics | Generic overrides, coverage allowlist |
|---|---|---|---|---|
| exlib | `tests/live/<name>.json` | | `tests/live/schematics/<file>.json` | `tests/live/generic.json`, `tests/live/coverage-allow.json` |
| exmods | `mods/<mod>/tests/live/<name>.json` | `mods/<mod>/tests/live/lines/<line>.json` | `mods/<mod>/tests/live/schematics/<file>.json` | `mods/<mod>/tests/live/generic.json`, `mods/<mod>/tests/live/coverage-allow.json` |

Not `tests/Scenarios`: that folder already holds the harness scenario tests, and a case-insensitive
filesystem would merge the two. Scenario files never ship. extools copies each mod's `tests/live/`
into `<dataPath>/Scenarios/<modid>/` before the boot, and the runner reads only from there
(`ICoreServerAPI.GetOrCreateDataPath("Scenarios")`), or from an explicit path. Generic scenarios have
no files.

A mod-specific scenario is named `<modid>/<path under tests/live without .json>`:
`iiex/boiler-side-feed`, `iiex/lines/cold-blast`. A file with `cases` names each case
`<modid>/<path>/<case>`, and a matrix combination appends its values: `iiex/blower-air/running[side=e]`.
A generic case is named `generic/<blocktype code>/<kind>`, as in `generic/iiex:boilercornish/megablock`.

### The file

JSON as the game reads its own assets (comments and unquoted keys allowed). An unknown key is an
error that names the file, the step index and the key, so a misspelt step never silently passes.

| Key | Type | Default | Meaning |
|---|---|---|---|
| `description` | string | required | one line on what the scenario proves |
| `follows` | array of citations | `[]` | the guides the file is written from, see Citations |
| `steps` | array | | the one case, in order |
| `cases` | object | | `name` to `{ steps, ...overrides }`; exactly one of `steps` and `cases` |
| `matrix` | object | none | axis name to a list of values; every combination runs as its own case |
| `plot` | `[x, y, z]` | `[16, 16, 16]` | plot size in blocks |
| `climate` | `{ temperature, rainfall }` | none | the plot's climate, see Climate |
| `timeout` | seconds | 120 | per case; a case still running then fails |
| `settle` | seconds | 3 | how long the runner waits after the last step before it closes the case |
| `allowErrors` | array of regex | `[]` | log errors the case expects, which then do not fail it |

A case in `cases` may restate any key but `description` and `cases`; its value overrides the file's,
and its `matrix` axes join the file's. Matrix substitution is textual on strings: `{side}` inside a
string is replaced by the value, and a string that is exactly `"{cell}"` takes the value itself, so
`"cell": "{cell}"` becomes `[0, 1, 0]`.

### Plots and coordinates

Each case runs on a plot of its own, never reused within a run. A plot takes whole chunk columns
(32-block squares), enough for its size plus an 8-block margin, so a reload of one plot never touches
another. Plots run in a row along +X from 32 blocks east of the world spawn. A case with a `climate`
gets a plot at the centre of the next map region no other plot touches (regions are 512 blocks), since
vanilla's weather works per region.

A plot's origin `(0, 0, 0)` is the first air cell above the ground at the plot's centre. Every plain
position in a file is plot-local; `y` below 0 is ground, which a step may replace. Before a case
starts, the runner loads the plot's chunk columns with `IWorldManagerAPI.LoadChunkColumnPriority`
(`ChunkLoadOptions.KeepLoaded`) and starts the case from `ChunkLoadOptions.OnLoaded`.

The runner remembers every cell a step placed. `complete` never replaces one of them.

When a case ends, its plot is set to air from `y = 0` up and every item entity in it is removed; a
failed case's plot is saved first (see When a step fails). Errors raised while clearing belong to the
case: a removal path that throws is a bug. With `keep`, no plot is cleared.

### Climate

Every region that holds a plot has a fixed climate while its plot stands: the scenario's `climate`, or
20 C and rainfall 0 for a plot that names none. The runner holds it through `IEventAPI.OnGetClimate`,
setting `Temperature`, `WorldGenTemperature`, `Rainfall` and `WorldgenRainfall` for every position in
the region and every `EnumGetClimateMode`. The handler is installed when the plot is allocated, before
its chunks load, so every weather snapshot the region takes is under it.

Machine numbers that read the ambient climate are then the same on every run, whatever the season, and
no rain or snow reaches a default plot. A snowing plot is `"climate": { "temperature": -10,
"rainfall": 1 }` plus an `advance` step. Vanilla's hourly snow snapshot
(`WeatherSimulationRegion.UpdateSnowAccumulation`) adds `rainfall / 3` snow levels per game hour to a
region colder than 1.5 C. `WeatherSimulationRegion.TickEvery25ms` catches up up to 480 missed hours in
one pass after a calendar jump. `WeatherSimulationSnowAccum` queues every loaded map chunk every 3 s,
computes each column's snow level off-thread in `UpdateSnowLayer` (which asks the column's top block
`AllowSnowCoverage` and `GetSnowCoveredVariant`), and applies it on the main thread.

### Positions

Every `at` in a step takes one of these forms.

| Form | Resolves to |
|---|---|
| `[x, y, z]` | a plot-local cell |
| `{ "of": id }` | the block a step placed with that `id`; for a structure, its principal |
| `{ "of": id, "cell": [x, y, z] }` | a footprint cell in the footprint's own frame (the principal is `[0, 0, 0]`), turned by the block's `IFillerHost.StructureAngle` through `ExOrientation.RotateOffset`, as `StructureFillers.FootprintCells` turns it |
| `{ "of": id, "layout": [x, y, z] }` | a cell in the layout's authored frame, placed where the block entity's rotated `MultiblockStructure` puts it; any offset resolves, inside the layout or not |
| `{ "of": id, "port": [x, y, z] }` | the cell across the declared port face of footprint cell `[x, y, z]` (`FillerCell.PortFace`, already turned) |
| any of the above plus `"beyond": "n"` (`e`, `s`, `w`, `u`, `d`) | the neighbour across that face. For an `of` form naming a structure the face turns with it (`ExOrientation.RotateFacing`); otherwise it is a world face |

A structure's frame (principal, angle, footprint and layout cells) is taken when it is placed, so an
`of` position still resolves after the structure has been broken.

### Steps

A step is an object with one step kind and, optionally, the citation keys `guide` and `design` (see
Citations).

| Group | Kind | Shape |
|---|---|---|
| Placing | `place` | `{ id?, code, at, by?: "world" \| "player", facing?, complete?: "all" \| n, materials?: { wildcard: value }, tree?: { key: value } }` |
| | `complete` | `{ of, to?: "all" \| n, materials? }` |
| | `run` | `{ code, from, via?: [positions], to }` |
| | `paste` | `{ file, at, angle?: 0 \| 90 \| 180 \| 270, origin?: "BottomCenter" }` |
| Supplying | `power` | `{ at, face, speed, torque }` |
| | `source` | `{ at, face?, medium, rate, pressure?: 1, temperature?: 20 }` |
| | `insert` | `{ at, stack, count?: 1, temperature?, slot? }` |
| | `set` | `{ at, tree: { key: value } }` |
| | `call` | `{ at, method, args?: [...], returns? }` |
| Using | `interact` | `{ at, with?: { stack, count?, temperature? }, keys?: ["sneak" \| "ctrl" \| "sprint"], face?: "u", seconds?: 0, returns? }` |
| Time | `wait` | seconds, or `{ seconds }`, or `{ until, max }`, each with an optional `hold` |
| | `advance` | `{ hours }` |
| | `reload` | `{}` |
| | `repeat` | `{ times?, until?, max?: 20, steps }` |
| Breaking | `break` | `{ at, by?: "player" \| "world", with?: stack }` |
| Expecting | `expect` | see Expecting |

#### `place` and `complete`

`place` takes an exact block code (no wildcard). By default it sets the block through the world block
accessor with a stack of that block, `IBlockAccessor.SetBlock(blockId, pos, stack)`, which runs
`Block.OnBlockPlaced`: the block entity is created and initialised, and a megablock places its fillers
there. For a mechanical-power block it then calls `BlockMPBase.WasPlaced`, the network join vanilla's own
placement makes. `tree` writes block entity keys after placement, as `set` does. A target cell, or a
footprint cell, that is not air fails the step before anything is placed.

With `by: "player"` the scenario player places it the way a player does: it stands two blocks back from
the cell, looks along `facing` (default: toward the cell), holds a stack of the code and clicks the top
face of the block below (see The scenario player). The block's own placement decides the variant, so
`code` names the stack held and an `expect` checks what went down.

`complete` brings a placed structure up to `to` (default `"all"`); `place` with `complete` is the same
thing in one step. It works in two parts, each applied when the block has it.

- **Layout.** When the block entity is a `BlockEntityMultiblockStructure`, the runner reads the
  structure's own missing cells from the block entity (`IncompleteBlockCount`, which reports each as a
  `MissingCell` with its wanted code already rotated) and raises each one with the first registered
  block matching the wanted code (an alternation takes its first branch, through the same wildcard
  choice `StructureRig` uses; the step line names every code chosen). Three kinds of cell are left
  alone: a cell that wants air, a cell a step placed, and a cell the structure's connector table
  (`MultiblockConnectors.OutwardFacesAt`, turned as the block entity turns it) gives an outward face
  (the scenario places those itself, turned the way it means). The connector table is read rather than
  `MissingCell.OutwardFace`, which is empty on a cell whose code does not match yet. The runner then
  waits for the block entity's own monitor tick to set `StructureComplete`, at most two
  `CompletionTickMs` intervals plus one second, and fails with the missing-cell report otherwise.
- **Construction stages.** When the block entity carries `ExRightClickConstructable`, the scenario
  player builds it stage by stage the way a player does, through vanilla's own consumption: before each
  stage the runner fills the player's hotbar with a supply it generates, then the player right-clicks
  the block in survival, which reaches `BEBehaviorRightClickConstructable.OnBlockInteractStart` and
  `RightClickConstruction.OnInteract`. The supply holds, for each ingredient of the next stage, the first
  registered collectible that satisfies it, restricted by `materials` where the ingredient stores a
  wildcard (`"materials": { "metal": "iron" }`). Vanilla takes the stacks from the hotbar and records
  `StoredWildCards` from the variant of the stack it took, so later stages and the drops resolve the
  same way as in play. Survival matters: in creative with Ctrl held, vanilla skips the cost and stores
  oak and iron whatever the hotbar holds. `to: n` stops at `CurrentCompletedStage` n, which is how a
  part-built structure is made. What the hotbar holds after a stage is printed and cleared.

#### `run`

`run` lays pipe the way a player does: `code` is the family's straight segment, and the scenario player
places it by hand in every cell of the path from `from` through each `via` to `to`, in order. Each leg
runs along one axis; a leg that does not fails when the file loads. Pipes are self-orienting nodes
(`BlockPipe`), so every placement turns into the straight, bend or junction its neighbours call for, and
a path that ends beside an existing pipe joins it. The step then expects every cell of the path, the
cells across `from`'s and `to`'s outer faces included when they hold a pipe node, in one network. A
scenario never names a bend or junction code.

#### `paste`

`paste` loads a vanilla WorldEdit schematic from the scenario's own `schematics/` folder with
`BlockSchematic.LoadFromFile`, turns it with `TransformWhilePacked` around `origin` (an `EnumOrigin`
name, default `BottomCenter`) by `angle`, and places it the way `WorldEditWorkspace.PasteBlockData`
does: `Place` with `EnumReplaceMode.ReplaceAll`, `PlaceDecors`, then `PlaceEntitiesAndBlockEntities`.
That restores every block entity's saved tree and calls `OnPlacementBySchematic` on it, where vanilla
mechanical power rejoins its network (`BEBehaviorMPBase.tryConnect` on every face). Every pasted cell
counts as placed by a step.

#### `power` and `source`

Both place one of exlib's creative sources (see Creative sources) and write its settings, so what drives
a scenario is a block a player can place and look at in a `-Keep` session.

`power` places the power source across `face` of the cell at `at`, turned so its output faces the cell;
for an `of` position the face turns with the structure. `speed` and `torque` are in vanilla's own
mechanical-power units. A machine on exlib's mpenergy run takes it at its flywheel hub, through the
vanilla-MP bridge.

`source` places the fluid source on an open end of the pipe at `at`: the first connector face with no
node across it, in the order n, e, s, w, u, d, or `face`. The source caps that end, so the run does not
leak there. It feeds `medium` at `rate` (L/s) and `temperature` (C); for a liquid, `pressure` (atm) is
the commanded feed pressure, for a gas the output ceiling. A run holds one medium (R1), so a source into
a run holding another delivers nothing, and its block info and the case's closing lines say so.

#### `insert`, `set`, `call`

`insert` puts `count` of `stack` into the block entity's inventory (`IBlockEntityContainer.Inventory`)
through `ItemSlot.TryPutInto`, into `slot` or the slot the inventory itself picks, so its own
acceptance rules apply. `temperature` (C) is set on the stack first (`CollectibleObject.SetTemperature`),
which is how hot stock enters a line. A refusal fails the step.

`set` reads the block entity's tree (`ToTreeAttributes`), writes each key with the type the key already
has, loads it back (`FromTreeAttributes`) and marks the entity dirty. A key the tree does not hold fails
the step. It is for state that has no verb: a fill level, a source's settings. The treekeys goldens list
each block entity's keys, so a renamed key fails a guard before it fails a scenario.

`call` invokes a public instance method on the block entity, or on one of its behaviours when
`method` is `<BehaviourClass>.<Method>`, matched by name and argument count. Arguments convert to the
parameter types: `null`, numbers, strings (also enum names and asset codes), booleans,
`{ "stack": code, "count": n }` for an `ItemStack` or `ItemSlot` parameter (a `DummySlot` holding it),
and `"player"` for an `IPlayer` parameter (the scenario player). `returns` fails the step when the
return value differs. The step line prints the return value and what is left in a slot argument. It
reaches state that no gesture exposes; a file that follows a guide uses `interact` for what the guide
tells a player to do.

#### `interact`

The scenario player uses the block at `at` as a player does: it stands two blocks back facing the cell,
holds `with` in its active hotbar slot (an empty hand without it), holds `keys`, and right-clicks the
`face` (default the face toward it) at the face's centre. The interaction lasts `seconds`, stepped every
runner tick, as holding the mouse button does. `returns` checks what `OnBlockInteractStart` answered.
What is left in the hand is printed and then cleared.

#### `wait`, `advance`, `reload`, `repeat`

`wait` lets the live server run. A bare number or `{ seconds }` waits that long. `{ until, max }` polls
an expectation every runner tick and moves on when it holds, failing at `max` seconds with the last
value read. `hold` (one expectation or a list) is polled on the same ticks and fails the step the moment
it stops holding, which is how "never" and "stays" are asserted.

`advance` jumps the calendar by `hours` of game time (`IGameCalendar.Add`) and runs one runner tick.
Nothing ticks through the skipped hours: a machine that reads the calendar sees the gap on its next tick,
the path a chunk takes when it reloads, and vanilla's weather catches up its hourly snapshots.

`reload` saves and reloads the plot. The runner reads every block entity's tree in the plot, unloads the
plot's chunk columns (`IWorldManagerAPI.UnloadChunkColumn`, which runs `OnBlockUnloaded` and writes
the chunks to the save database) and loads them back, which builds new block entity instances,
`FromTreeAttributes` and `Initialize` from what was saved. It then fails when a block entity is gone,
has changed class, or holds a tree where a key is missing, has changed type, or is back at the value a
fresh instance writes although the live value differed; the last is a field that is written but never
read back, or never written. The tree comparison is the same code the harness's reload guard runs (see
Code). Structure frames and ids survive a reload; the block entities behind them are the new instances.

A block entity that fails to load does not come back missing. exlib's `BlockEntityHealModSystem` sweeps
every column on `ChunkColumnLoaded`, the event the reload raises, recreates a fresh block entity for a
block that has none and whose entity class carries exlib's `BlockEntityRegister`, and logs `Recreated
<n> orphaned block entit(ies) in chunk column <x>,<z>.` at Notification level. Such a block entity would
then pass the class check with a fresh tree, so that line, logged while a case runs, fails the case at
the step running then (see Errors and exceptions).

`repeat` runs its `steps` `times` times, or until `until` holds after a round, at most `max` rounds; an
`until` never met by `max` fails the step. Its inner steps print as `<n>.<round>.<m>`. "Charge in rounds
until it is full" is a `repeat`.

Server ticks come at real speed; a scenario never fast-forwards them. A machine receives the `dt` its
own listener interval gives it.

#### `break`

`break` takes the block at `at` down, and a filler forwards it to its principal as it does in play.

| `by` | Breaker |
|---|---|
| `player` (the default) | the scenario player in survival, holding `with` (an empty hand without it), through the server's own break path (see The scenario player): a held tool below the block's required mining tier refuses the break and fails the step |
| `world` | `IBlockAccessor.BreakBlock(pos, null)`, no player. Vanilla drops as for a survival player when the breaker is null, and vanilla and other mods break blocks this way (falling blocks, displaced liquids), so a null dereference here is a real crash |

Just before the break, the runner asks the block what it will drop (`Block.GetDrops` at the cell with
the breaker, plus `ExRightClickConstructable.GetConstructionDrops` for a construction). One runner tick
after it, the runner collects the item entities that appeared in the plot box (plus a 2-block margin),
removes them from the world and keeps both lists for `expect drops`.

#### Expecting

| Shape | Checks |
|---|---|
| `{ at, block: code }` | the block at `at` matches the code (wildcards as `WildcardUtil` reads them; `game:air` for empty) |
| `{ at, member: path, <test> }` | a public property or field of the block entity, walked by dots. A first segment naming one of its behaviours' classes selects that behaviour |
| `{ at, tree: key, <test> }` | a key of the block entity's `ToTreeAttributes` tree |
| `{ at, info: regex }` | a line of the block entity's `GetBlockInfo` (its behaviours' lines included), in the server's language, for the scenario player |
| `{ at, network: "pipe", member: name, <test> }` | the pipe network state at the cell: `Volume`, `MaxVolume`, `Pressure`, `Temperature`, `MediumType`, `FlowRate` |
| `{ at, network: "mpenergy", member: name, <test> }` | the mechanical-energy run at the cell, `MpEnergyNetwork.State`: `Speed` (rad/s), `Inertia`, `StoredEnergy`, `SupplyPower`, `DemandPower`, `Reversed` |
| `{ at, network: type, same: position }` | the cells at `at` and `same` are in one network of that type |
| `{ of, footprint: "filled" \| "air" }` | every footprint cell, the principal included, holds the principal or a filler naming it, or holds air |
| `{ drops: [{ code, count }], exact?: true }` | what the last `break` dropped, summed per code; `exact` also refuses any stack not listed |
| `{ drops: "declared" }` | what the last `break` dropped is what the block declared just before it: the same codes, and counts that differ by no more than the salvage rounding (`GameMath.RoundRandom`) |

A `<test>` is `equals` (booleans, strings, enum names, integers; a float within 1e-4), `matches` (a
regex over the value's string form), `min`, `max` (inclusive), or `min` with `max`. A passing
expectation prints the value it read, so a pass line is also a reading.

### The scenario player

Every run builds one server-side player, `Scenario` (uid `exlib-scenario`), out of the engine's own
classes, so a case can place, use and break as a survival player with nobody connected. It is built the
way `ConnectedClient.LoadOrCreatePlayerData` and `ServerMain`'s join handling build a joining player,
without the connection:

1. `ServerWorldPlayerData.CreateNew` and `Init(ServerMain)`, with `EntityPlayer.Code` set to the loaded
   player entity type (`ServerMain.GetEntityType(GlobalConstants.EntityPlayerTypeCode)`); the data is
   registered in `PlayerDataManager.WorldDataByUID`.
2. `new ServerPlayer(server, worlddata)`, the public constructor, which builds the inventory manager and
   the server player data. The player takes the `admin` role, so no privilege or land claim refuses it.
3. `ServerMain.PlayersByUid` maps its uid to it, so `IWorldAccessor.PlayerByUid` and
   `EntityPlayer.Player` resolve it.
4. The inventory system's own join handler, `ServerSystemInventory.OnPlayerJoin`, creates and opens the
   default player inventories (hotbar, backpack, crafting grid, mouse, character).
5. The entity is positioned at the parking cell and spawned with `ServerMain.SpawnEntity`.

The join path does more, and the rest is skipped on purpose. There is no `ConnectedClient`, so
`ConnectionState` reads `Offline` and `ServerMain.SendPacket` drops everything addressed to the player.
No other server system's `OnPlayerJoin` runs, and no `OnPlayerJoin`, `OnPlayerNowPlaying` or
`OnPlayerCreate` event fires, so no mod treats it as a client or sends it anything. No chunks are sent
and nobody is told it joined. It is in `IWorldAccessor.AllPlayers` and not in `AllOnlinePlayers`, which
reads connected clients.

It is the engine's own `IPlayer`: `IPlayer.IsInInteractionRangeOf`, the internal abstract member that
makes `IPlayer` unimplementable outside the game on 1.22, is implemented by `ServerPlayer` itself.

Its verbs mirror the server's packet handlers, which read a packet and a `ConnectedClient` and so cannot
be called:

| Verb | Mirrors | Calls |
|---|---|---|
| place | `ServerSystemBlockSimulation.TryModifyBlockInWorld`, place branch | `Block.TryPlaceBlock` with the active slot's stack, then `ServerEventManager.TriggerDidPlaceBlock` |
| break | the same, break branch | the mining-tier check against the held collectible's `GetToolTier` in survival, `ServerEventManager.TriggerBreakBlock`, then `CollectibleObject.OnBlockBrokenWith` for a held stack or `Block.OnBlockBroken` for an empty hand |
| interact | `ServerSystemBlockSimulation.HandleBlockInteract` | `IWorldAccessor.Claims.TryAccess` for use, `Block.OnBlockInteractStart`, `OnBlockInteractStep` each runner tick for the held time, `OnBlockInteractStop`, then `ServerEventManager.TriggerDidUseBlock` |

Between steps it is parked 16 blocks above the plot row in creative mode with `FreeMove`, so it neither
falls, goes hungry nor picks up a drop. A player step moves it into place (entity position, yaw and
pitch toward the target, `BlockSelection` with the face and hit position), sets survival unless the step
says otherwise, fills the active slot, runs, empties the slot and parks it again. At the end of the run
it is despawned and removed from `PlayersByUid`, `WorldDataByUID` and `PlayerDataByUid`.

**When an engine update breaks it.** Every member it touches is public in VintagestoryLib and referenced
at compile time, except two resolved by name at the start of a run: the internal `ServerMain.Systems`
field and the internal `ServerSystemInventory` type in it. A changed public member breaks the live mod's
build against `.game/1.22`; one that differs only in the booted install (see The world) throws a
missing-member exception at the player check, and a changed internal one fails the run's first check.
Before the first case the runner builds the player and checks it: `PlayerByUid` returns it,
`Entity.Player` returns it, its hotbar has an active slot, `WorldData.CurrentGameMode` takes survival
and reads it back, and `IsInInteractionRangeOf` holds for the cell under its feet. A failure prints
`[scenario] error player: <check>: <what was read or thrown> (game <version>)`. Every case then counts
as an error without running, since none may fall back to breaking as the world, and extools exits 1.

### Creative sources

Test inputs are real blocks in exlib's Industry module, in the creative inventory and in no recipe. A
creative player sets one by cycling its values with right-click and sneak-right-click, as vanilla's
creative rotor does; scenarios write the same tree keys directly.
Each shows its settings and what it has delivered in its block info.

| Block | Settings (tree keys) | Does |
|---|---|---|
| power source, `exlib:creativepower-{n,e,s,w,u,d}` | `speed` 0 to 3, `torque` 0 to 20, in vanilla's mechanical-power units | a vanilla mechanical-power producer whose output faces its variant's side, as `BlockCreativeRotor` is, without the rotor's 1.0 speed cap |
| power load, `exlib:creativeload-{n,e,s,w,u,d}` | `torque` 0 to 20 | a vanilla mechanical-power consumer on its variant's side that resists with the set torque; it reports the speed and power it takes, so an engine's output can be read |
| fluid source, `exlib:creativefluid` | `medium` (any registered medium: Water, Steam, Air, Exhaust today), `rate` 0 to 500 L/s, `pressure` atm, `temperature` C | a pipe node on all six faces that feeds its network every tick through `PipeNetwork.ProduceLiquidMeasured` or `ProduceGasMeasured`; steam, hot blast, exhaust and fuel gas are all this block |
| fluid drain, `exlib:creativedrain` | `rate` 0 to 500 L/s, `pressure` atm | a pipe node on all six faces that takes up to `rate` from its network while the network stands above `pressure`, the steady consumer a boiler or a gas main needs |
| molten source, `exlib:creativemolten-{n,e,s,w}` | `metal` (a molten metal code), `rate` units/s, `temperature` C | an `IMoltenCell` with `IsFlowSource` whose pool `PushMetalRaw` tops up every tick, so a canal carries from it as from a tap |
| mpenergy source, `exlib:creativempenergy` | `torque` 0 to 20 | an `IMpEnergyProducer` node that the generic mpenergy form places beside a member with no vanilla-MP bridge of its own (a shaft, a transmission, the rolling mill) and drives at the set torque |

Heat has no block. Heat enters every family process as fuel or as a hot input, so it is a temperature:
a fluid source's (steam, hot blast), a molten source's, or a stack's (`insert` and `interact` take a
`temperature`). Items have no block either; `insert` and `interact` feed them, the way a player would.

### Citations

A factory line, or any scenario written from a guide, says where each step comes from. `follows` lists
the guides of the whole file; a step's `guide` names the passage it enacts, and its `design` the settled
page that says the same thing. A citation is a path from the repository root, with an anchor:

| Cites | Form |
|---|---|
| a wiki page, a heading, an item of a numbered list under it | `mods/iiex/wiki/structures/blastcore.md#the-first-run/4` |
| a handbook entry, by the lang key its text is in | `lang:iiex:<key>` |
| an old diagram | `workbench/textures/diagrams/furnaces/diag-furnace-coldblast.png` |
| a design page and heading | `docs/design/machines/blast-furnace-cold.md#the-players-verbs` |

A citation that starts with `#` resolves against the first entry of `follows`. A heading's anchor is the
heading lower-cased, apostrophes dropped, and every other run of characters that are not letters or
digits turned into one hyphen, with none left at either end.
extools resolves every citation before the boot, fails the run on one that names a missing file, heading
or list item, and stages the cited passages beside the scenarios so that a failure can print them.

A failed step in a file with `follows` names a suspect by what the step cites. A step of a file without
`follows`, or of a generic case, stands on the design pages and this page, and its suspect is `mod`.

| The step cites | Suspect | Why |
|---|---|---|
| a design page (with or without a guide) | `mod` | a settled page says the same as the guide, and the mod does something else |
| a guide or a diagram, and no design page | `guide` | nothing settled backs the passage; the guide may promise what the mod never did, and a diagram is stale by nature |
| nothing | `scenario` | the step is the author's own reasoning or plumbing |

The suspect is where triage starts, not a verdict: the failure is read against the code and the cited
pages before an item is opened for it.

### Errors and exceptions

"No exception" is checked on every case without being written. For the whole run the runner listens on
the server logger's `ILogger.EntryAdded`. An `Error` or `Fatal` entry while a case runs fails the case
at the step running then, checked after every step and on every wait poll, unless it matches one of the
case's `allowErrors`. A block entity whose tick listener throws is caught by
`BlockEntity.TickingExceptionHandler`, which logs it as an error naming the position and the block, so a
machine that throws on its own tick fails the case it happens in and the detail says which code.
`BlockEntityHealModSystem`'s `Recreated` line is the one Notification entry that fails a case the same
way, since it means a block entity was lost and replaced (see `reload`). A throw the engine does not
catch stops the server; extools then finds no count line, fails the run and prints the log's last
errors. A warning never fails a case; it is listed in a failed case's detail.

An exception thrown by a step's own call is caught, fails the step, and prints its type, message and
first stack frames. After the last step the runner waits `settle` seconds, so a listener that throws on
the machine's next beat lands inside the case.

### Output

Every line starts with `[scenario]` and goes to `server-main.log` through `ICoreAPI.Logger.Notification`.
The lines are fixed English, never lang keys, since extools parses them; `/exmod verify`'s summary is a
lang string and parses only on an English server. When a joined player ran the command, the runner also
sends him the `case`, `pass`, `fail` and `done` lines in chat, with angle brackets escaped, since chat's
VTML cuts a line at one.

```
[scenario] run: <S> scenario(s), <C> case(s), budget <B> s, game <version>
[scenario] census <modid>: <N> blocktype(s) found, <G> in the goldens
[scenario] case <name> plot <x> <y> <z>
[scenario]   <n> <kind> <what> ok[: <value>]
[scenario]   <n> <kind> <what> FAIL: <reason>
[scenario]   | <detail>
[scenario] pass <name> (<t> s)
[scenario] fail <name> step <n> suspect <mod|guide|scenario> (<t> s)
[scenario] error <name>: <reason>
[scenario] coverage: <H> hook(s) on <B> blocktype(s), <V> covered, <A> allowed, <K> client, <P> unprobed, <U> uncovered.
[scenario]   uncovered <blocktype> <Type.Member>
[scenario] done: <C> case(s) run, <P> passed, <F> failed.
```

`plot` coordinates are absolute. `error` is a case that could not run: its file does not parse, it names
an unknown code, its plot never loaded, or the scenario player could not be built (`error player`); it
counts as failed. `budget` is the sum of the case timeouts, which extools adds to its own wait.

A run that proves nothing fails. A selection that matches no case prints `[scenario] error run: no case
selected` and the count line with 0 cases run. A generic run prints one `census` line per mod it walks,
before the first case: the blocktypes the walk found against the blocktype goldens extools counted
(`<dataPath>/Scenarios/census.json`, see `exmod scenario`), and a mod whose walk finds fewer prints
`[scenario] error census <modid>: <N> blocktype(s) found, the goldens hold <G>`, which fails the run
although its cases still run. A mod with no blocktype goldens has a census of 0 and is not checked.

The `coverage` lines appear only in a coverage run, just before the count line. The count line is always
the last line of a run; extools matches it with `\[scenario\] done: (\d+) case\(s\) run, (\d+) passed,
(\d+) failed\.` and the coverage line with `\[scenario\] coverage: .*, (\d+) uncovered\.`

### When a step fails

The case stops at the failing step (the rest print as skipped) and the runner writes a detail block
under it, enough to fix without a rerun:

- the step as written, and every position it resolved, plot-local and absolute, with the structure's
  angle;
- the expected and the read value, or the exception with its stack frames;
- the cited passages, each with its citation, and the reason for the suspect;
- for each cell the step touched and each footprint cell of the structure it names: the block code and
  the block entity's class;
- the tree of every block entity the case expected against, one line each;
- the missing-cell report of every multiblock in the plot (cell, wanted, actual, outward face);
- every error and warning logged during the case;
- what each creative source delivered;
- the plot saved as a schematic, `<dataPath>/WorldEdit/scenario-<case>.json`, which `/we import` pastes
  into a creative world, and the `/tp` command that reaches the plot.

### `-Keep`

`/exmod scenario run <names...> keep` leaves every plot standing and every source and climate running
after the run. extools leaves the server up for fallen to join from the same machine and look: each
`case` line carries the plot's coordinates. With several plots standing, a machine on an earlier plot
can raise an error during a later case; the later case's detail says how many plots stand.

### `exmod scenario`

```
exmod scenario [<name>...] [-Generic] [-Coverage] [-FailOnUncovered] [-Version <x.y>] [-Port <n>]
               [-Keep] [-Mods <dir>[,...]] [-Timeout <s>] [-KeepData] [-Verbose]
```

A name is a bare stem, `<modid>/<path>`, `<modid>/<path>/<case>`, or `generic/<code wildcard>` for part
of the generic layer (`generic/iiex:boilercornish*`). No name runs every mod-specific scenario of the
repository, factory lines included; `-Generic` adds the generic layer over the repository's own mods
(`generic/<modid>:*` for each). `-Coverage` switches the coverage probe on for the boot;
`-FailOnUncovered` implies it. extools:

1. builds the live mod from exlib's source (this checkout in exlib; the sibling exlib in the workspace
   from exmods, failing with a message when there is none), stages it beside the mods as smoke stages
   them, copies each mod's `tests/live/` into `<dataPath>/Scenarios/<modid>/`, resolves the citations,
   counts each mod's blocktype goldens (one file per blocktype under `tests/goldens/<modid>/blocktypes/`)
   into `<dataPath>/Scenarios/census.json`, writes `serverconfig.json`, and with `-Coverage` writes
   `<dataPath>/Scenarios/coverage.on`;
2. boots the server on its port (42498, or `-Port`) and waits for `Dedicated Server now running` in
   `Logs/server-main.log`;
3. writes `/exmod scenario run <names>` (with `keep` under `-Keep`, `failuncovered` under
   `-FailOnUncovered`) to the server's stdin;
4. follows the log and prints the `run`, `census`, `pass`, `fail`, `error`, `coverage` and `done` lines,
   with every line of a failed case and every `uncovered` line; `-Verbose` prints every step line;
5. waits for the count line up to the boot, the run's budget and 60 s more, or `-Timeout`;
6. prints every `[Error]` and `[Fatal]` line that falls outside a case (between a `case` line and its
   `pass` or `fail` line is inside);
7. without `-Keep`: writes `/stop`, copies the failed cases' schematics and the coverage report to
   `.game/.scenario-last/`, and exits 1 when the count line is missing or reports 0 cases run, a case
   failed, an `error census` line was printed, an error line fell outside a case, or `-FailOnUncovered`
   meets an uncovered hook; else 0;
8. with `-Keep`: prints the port, the data path and how to join (`exmod client`, Multiplayer,
   `localhost:<port>`), relays the terminal's lines to the server console until Ctrl+C, then writes
   `/stop`. The exit code is the one step 7 would give.

### When each layer runs

| Layer | What it is | When it runs |
|---|---|---|
| unit and content checks | xUnit tests of one type, and the `Checks` over a mod's JSON, shapes, lang and definitions | `exmod check`: every change, every game version the repository builds, and CI |
| harness | `TestWorld`, the rigs and scenes: real block code in a substituted world | `exmod check`, as above |
| scenarios | this page: a real dedicated server, 1.22 only | a machine's mod-specific scenarios at the end of a lane that changes that machine, before its review; the whole generic layer with `-Coverage` before fallen's in-game check and before a release. Never inside `exmod check` |

A bug a scenario finds gets its regression test in the harness when the harness can express it, so
every later `check` repeats it; it stays a scenario case only when it needs what the harness fakes (the
floor table under The generic layer). A hook the coverage report lists as uncovered gets a case in
whichever layer reaches it.

---

## The generic layer

### What a block is asked

The runner walks the block registry for the domains it is given (by default exlib and every mod that
depends on it) and reads each blocktype's signals from the registered blocks, never from a list.

| Signal | Read from | Case |
|---|---|---|
| any block | the registry | `lifecycle` |
| a footprint | `IFillerHost` with `fillerOffsets` | `megablock` |
| a layout | a `BlockEntityMultiblockStructure` entity with a `multiblockStructure` layout | `multiblock` |
| `nosnow` cells | the layout's cells marked `CellRole.NoSnow` | `nosnow` |
| construction stages | `ExRightClickConstructable` among the entity behaviours | `construction` |
| a mechanical-power connector | a cell (the principal or a hosted one) answering `IMechanicalPowerBlock.HasMechPowerConnectorAt` on some face for the power source, or hosting a `BEBehaviorMPBase` | `power` |
| an mpenergy membership | a network member of type `mpenergy` (the shafts, flywheels, transmissions and the machines on the run, such as the rolling mill), on the principal or a hosted cell | `power`, mpenergy form |
| a network membership | a `BlockNetworkNode`, or a `BEBehaviorNetworkMember` on the entity or a hosted cell | network checks inside `lifecycle` |
| an inventory | the entity implements `IBlockEntityContainer` | container checks inside `lifecycle` |

A megablock's hosted behaviours come from its footprint's specs (`StructureFillers.ReadOffsets`), so a
port on a filler cell counts as the block's. The orientation group is the one exlib's orientation
schemes read ([orientation schemes](mechanics/orientation-schemes.md)).

The items a block is used with are derived too: its construction ingredients, the first stacks of a
fixed palette its inventory accepts, vanilla's igniters (`BlockBehaviorCanIgnite.CanIgniteStacks`), and
the stacks its own interaction help names (`Block.GetPlacedBlockInteractionHelp`, read on the server; a
throw there is noted in the case and skipped, since the engine calls it only on a client).

A mod's `tests/live/generic.json` adjusts the layer per blocktype, every entry with a reason: a kind to
skip, the `materials` a construction takes, extra palette stacks. An entry without a reason, or naming
no registered blocktype, is an error.

### The floor, and where each part runs

A part of the floor that the harness already checks is not repeated live. A live case earns its server
time by reaching what the harness fakes.

| Floor | Harness | Live | What the live case reaches |
|---|---|---|---|
| places in every orientation and breaks with the declared drops | the break-every-cell guard of plan task B3, to be built before the generic layer: every filler host and construction, every cell, built and part-built; the definition goldens pin what is declared | `lifecycle`: every variant code set and broken, and the block placed and broken by the scenario player from each facing, drops read as item entities | the engine's placement and break paths with the real block entity lifecycle, network and mechanical-power managers, a real player's facing and item entities, for every block rather than the filler hosts alone |
| block info renders | a new guard: a fresh block entity of every block, `GetBlockInfo` with a test player | `lifecycle`: after the tick, the use and the reload, on the same plot | the state only a live world makes: joined networks, ticked counters, reloaded trees. It adds no wait |
| survives a save and reload | the treekeys goldens pin the keys a fresh instance writes; a new guard runs `reload`'s tree comparison over a fresh block entity of every block through `TestWorld.Reload`, the in-memory save, `OnBlockUnloaded`, new instance, `FromTreeAttributes` and `Initialize` round trip | `lifecycle`: `reload` | the save database, the engine's chunk unload and load (`TestWorld.UnloadChunkAt` only hides a chunk from the accessor), the engine's `Initialize` order over a whole column, and `BlockEntityHealModSystem` on the load |
| answers a neighbour change | none | `lifecycle`: a stone set and cleared on each free face of the principal | the engine's neighbour notification; it adds no wait |
| ticks for N seconds | `RunLive` in the machines' own scenario tests | `lifecycle`: 5 s shared by every code on the plot | real listeners, intervals and `dt` for every block, not only the machines that have harness scenarios |
| interacting raises no exception | none | `lifecycle`: the scenario player with an empty hand and each derived item, on the principal and each hosted cell | a real server player with real inventories and the server's use events, which the harness's substituted `TestPlayer` does not have |
| a network member joins and leaves | `NetworkNodeContract` checks the definition | `lifecycle`: the cell is in a network of its type (`BlockNetworkModSystem.GetNetworkAt`) after the place and after the reload, and in none after the break | the network system over the engine's own block entity lifecycle, for every member. The harness reaches real block entities only where a fixture places them (`PipeTestWorld.LiveRun` in iiex's tests places a `BlockEntityPipe` in every cell) |
| a mechanical-power member connects to a source | the `MechPower` double; the harness's `MpEnergyNetwork` over placed members (`CastIronShaftTests`) | `power`, and its mpenergy form | vanilla's network manager and propagation, which the harness replaces; an mpenergy run turned through the engine's ticks |
| a multiblock forms, and unforms when a cell is taken | `StructureRig` with stand-in blocks; `MultiblockCodes` | `multiblock`: four facings with real blocks | real registered blocks against the wildcards: a stand-in matches a code that no real block matches |
| a megablock places in four facings and breaks from every cell | B3's break-every-cell guard | `megablock`: four facings, the principal and one cell of each kind | the kinds route differently through the engine (a hosted behaviour joins real networks); identical plain fillers do not, so every cell stays with the harness |
| a construction completes every stage, and pays its drops at each | B3's break-every-cell guard, built and part-built | `construction`: the stage walk against the real registry, readiness after the last stage, one player break at the end | ingredients resolved against the real item registry; the per-stage drop sweep stays with the harness |
| a container accepts and returns items | a new guard over the inventory's acceptance and extraction | `lifecycle`: an accepted stack survives the reload and drops on the break | persistence and the break's drop path |
| a `nosnow` cell refuses snow | `NoSnowCellsTests`, `NoSnowPatchTests` | `nosnow`: vanilla accumulation over the formed structure, four facings | vanilla computes snow on its own scanner thread and meets `NoSnowPatch` there; thread and order exist only live |

### The cases

**`lifecycle`**, one per blocktype. Every variant code is laid out on one plot, 3 blocks apart (a
footprint's extent apart for a megablock), and beside them the scenario player places the blocktype's
first stack from each of the four facings (once for a block without an orientation group):

1. `place` each code; `place` by the player from each facing; `expect` each (the footprint `filled` for a
   megablock), and that each player placement went down as a variant of this blocktype;
2. `expect info` for each (no throw); for a network member, the cell in a network of its type;
3. for a container, `insert` the first palette stack its inventory accepts;
4. set and clear `game:rock-granite` on each free face of each principal;
5. `wait` 5 s;
6. `interact` on each principal and hosted cell with an empty hand, then with each derived item;
   `expect info` again;
7. `reload`; `expect info` again; the member back in its network; the stack still in the inventory;
8. `break` each code by the player, except the first code, which breaks by the world; `expect` the
   footprint `air`, `drops: "declared"` (the stack included), and a network member in no network.

**`megablock`**, one per footprint blocktype: four instances, one per facing of the first material
variant, on one plot. Each is broken by the player from the principal and from one cell of each kind its
footprint declares (plain, attachable, boxed, hosting a behaviour, port), a fresh instance per break;
after each, the footprint is `air` and the drops are `declared`.

**`multiblock`**, one per layout blocktype: four structures, one per facing, `complete` together. For
each legend glyph other than air and the core, one cell of that glyph in every structure is broken by the
player; all four must report `StructureComplete` false within two monitor intervals; the cell is
restored and all four must report complete again. An air-legend cell takes a stone for its round.

**`construction`**, one per blocktype with stages: `complete` stage by stage with generated materials,
`expect` the stage counter after each and `IsReadyToProduce` after the last, then one player `break` with
`drops: "declared"`.

**`power`**, one per blocktype with a connector, first facing: a `power` source on each connecting face
in turn (speed 1, torque 5); the member's network (`BEBehaviorMPBase.Network`) must turn within 5 s. Then
the cell hosting the mechanical-power behaviour is broken while it turns, and the source's network must
carry on without an error.

The mpenergy form, one per blocktype with an mpenergy membership, first facing: the member's run is
driven (how is Open), and `expect network: "mpenergy"` must read `Speed` above 0 within 5 s at the
member's cell. Then the member is broken while the run turns, and what stays of the run must carry on
without an error. A blocktype that has both a vanilla connector and an mpenergy membership (the
flywheel, whose hub is the vanilla-MP bridge) gets both forms.

**`nosnow`**, one per layout with `nosnow` cells, each in a region of its own at -10 C and rainfall 1:
four structures, one per facing, and a control brick. The role marks the block snow would lie on or turn
into, so the case watches two things per marked cell: the cell keeps its block (no snow-covered variant)
and the cell above it holds no snow layer. It jumps 24 game hours and waits until snow lies on the
control brick, holding the whole time that both stand for every marked cell and every structure is
complete, then holds them for two more monitor intervals. Written out as a file, the smoke stack's case
reads:

```json
{
  "description": "Snow falls on formed smoke stacks and never settles in their open columns; the stacks stay formed.",
  "plot": [8, 16, 8],
  "climate": { "temperature": -10, "rainfall": 1 },
  "timeout": 90,
  "matrix": { "side": ["n", "e", "s", "w"] },
  "steps": [
    { "place": { "id": "stack", "code": "siex:smokestack-intake-tier1-{side}", "at": [0, 0, 0], "complete": "all" } },
    { "expect": { "at": { "of": "stack" }, "member": "StructureComplete", "equals": true } },
    { "place": { "code": "game:refractorybricks-good-tier1", "at": [3, 0, 3] } },
    { "advance": { "hours": 24 } },
    { "wait": {
        "until": { "at": [3, 1, 3], "block": "game:snowlayer-*" },
        "hold": [
          { "at": { "of": "stack", "layout": [0, 0, 1] }, "block": "game:air" },
          { "at": { "of": "stack" }, "member": "StructureComplete", "equals": true }
        ],
        "max": 45 } },
    { "wait": {
        "seconds": 7,
        "hold": [
          { "at": { "of": "stack", "layout": [0, 0, 1] }, "block": "game:air" },
          { "at": { "of": "stack" }, "member": "StructureComplete", "equals": true }
        ] } }
  ]
}
```

The derived case puts the four facings on one plot and holds every marked cell rather than the one the
file names. The smoke stack's open column is its only snow candidate: the column's rain-map top is the
floor under it at layout `(0, -1, 1)`, the cell the role marks, so vanilla would lay snow in layout
`(0, 0, 1)`. A snow layer set with `SetBlock` never asks `AllowSnowCoverage`, which is why the case
drives accumulation. In a `-Keep` session fallen can stand in the plot and type vanilla's
`/debug snowaccum here <levels>`, which runs the same `UpdateSnowLayer` path for his chunk at once.

### Variants and orientations

`lifecycle` places every variant code, since its cost is the shared wait and the reload, not the count.
The structure cases (`megablock`, `multiblock`, `nosnow`) run the four orientations of the first variant
that is not an orientation; the other material variants differ in code and drops, which `lifecycle`
covers. `power` runs at the first orientation.

### Size and run time

From the iiex and siex goldens: 110 blocktypes (92 with a block entity), at most 1491 variant codes
(`skipVariants` trims the cobblestone canals), 33 footprints, 10 layouts (5 of them open to the sky,
which take `nosnow` cells), 8 constructions, 14 blocktypes naming mechanical power plus 21 hosted
mechanical-power ports, and about 40 network members. Serial times, estimated before anything is built:

| Case | Cases | Each | Total |
|---|---|---|---|
| `lifecycle` | 110 | about 13 s (5 s tick, the uses, reload, 3 s settle) | about 24 min |
| `megablock` | 33 | about 8 s | about 5 min |
| `multiblock` | 10 | about 30 s (monitor intervals per glyph) | about 5 min |
| `construction` | 8 | about 6 s | about 1 min |
| `power` | about 20 | about 10 s | about 3 min |
| `nosnow` | 5 | about 25 s (a fresh region each) | about 2 min |
| **generic, iiex and siex** | about 190 | | **about 40 min**, plus a minute's boot |

The coverage probe adds about a second at boot. The mod-specific layer adds its own files' time.

---

## Coverage report

### What is listed

For each blocktype, the hooks are the methods declared on exlib or family types along the class chains
of its block class, its collectible and block behaviours, its entity class, its entity behaviours and
the behaviours hosted on its filler cells. A method counts when it overrides a virtual or abstract member
first declared on a game type (`CollectibleObject`, `Block`, `BlockEntity`, `CollectibleBehavior`,
`BlockBehavior`, `BlockEntityBehavior`, and any vanilla class between them and the family class) or on
an exlib base class (`BlockEntityMultiblockStructure.OnStructureCompleted` is one). Property accessors
count. The runner finds them by reflection: `BindingFlags.DeclaredOnly` on each type, kept where
`MethodInfo.GetBaseDefinition()` differs from the method. A hook is named `<DeclaringType>.<Member>`, so
an exlib base's override shows under every block that inherits it.

The network side of a machine is interface implementations rather than overrides (`IPipeNode`,
`IMoltenCell`, the `IMpEnergy*` node contracts, `IProductionReadiness`, `IFillerHost`), so those count
too: for each interface declared in exlib's assemblies (ExpandedLib and ExpandedLib.Industry) that a type
along the same chains implements, every method `Type.GetInterfaceMap` maps it to that is declared on an
exlib or family type, explicit implementations included. Such a hook is named by its implementing
method, `<DeclaringType>.<Member>`, like an override. An interface member the type leaves to a default
body declared on the interface is not a hook of the block.

### How a hook counts as exercised

By observation, never by declaration. A case that declared the hooks it covers would go on claiming
them after a rename or a rewrite, and the report exists to catch exactly what people forget; it trusts
only calls.

The probe is a Harmony prefix. extools writes `<dataPath>/Scenarios/coverage.on` before the boot, and
the live mod reads it in `StartPre` and patches every hook in the scanned assemblies with one shared
prefix (`__originalMethod`, `__instance`), before any of them first runs: load-time hooks such as
`OnLoaded` are seen, and no caller has inlined a body before it was patched. The prefix resolves the
instance to a blocktype (a block itself; a block entity's `Block`; a behaviour's block or block entity;
a hosted behaviour's principal, through the filler that hosts it) and adds `(hook, blocktype, case)` to
a concurrent set, since vanilla calls `AllowSnowCoverage` from its snow thread. A hit outside any case
counts under the pseudo-case `load`.

The cost is the reason it is a switch. exlib, iiex and siex declare about 940 overrides in all, the
hooks among them, besides the interface implementations; Harmony patches each hook once at boot, on the
order of a millisecond each. Every call to a patched hook then pays one lookup and one set insert. Game
assemblies are never patched, so only the family's hooks pay it, on plots that hold a few blocks each. A
run without `-Coverage` patches nothing. A method Harmony cannot patch (an open generic, an extern) is
listed as `unprobed`, never as covered.

### Statuses

| Status | Meaning |
|---|---|
| covered | at least one case, or `load`, called it for this blocktype; the report names the first three and a count |
| allowed | no case called it, and the mod's `coverage-allow.json` names it with a reason |
| client | a dedicated server never calls it: its parameters name a client-only type (`ICoreClientAPI`, `ITesselatorAPI`, `ITerrainMeshPool`, `MeshData`, `IClientPlayer` and the like), or it sits on a short table of client-only hooks (`BlockEntity.OnReceivedServerPacket` among them). Fallen's eye covers these |
| unprobed | Harmony could not patch it |
| uncovered | server-reachable, never called, not allowed |

`coverage-allow.json` maps a blocktype code (wildcards allowed) to hooks and reasons:
`{ "iiex:boilercornish-*": { "BlockEntityBoiler.Explode": "the burst is covered by the harness's burst tests" } }`.
An entry without a reason is an error, as are entries that name a hook the blocktype does not have.

A hook that no case calls is the finding. `NoSnowPatch` notes that a block class overriding
`AllowSnowCoverage` without calling the base escapes it; such an override appears in the report, and
the `nosnow` case either calls it or it prints as uncovered.

### Where it goes

The runner writes `<dataPath>/Scenarios/coverage.json` (blocktype, hook, status, covering cases) and
`coverage.txt` (one line per hook, grouped by blocktype), prints the `coverage` count line and one
`uncovered` line per uncovered hook, and extools copies both files to `.game/.scenario-last/`. With
`failuncovered`, an uncovered hook fails the run through extools' exit code; the cases' own counts are
unchanged. A coverage run means both layers: `exmod scenario -Generic -Coverage`.

---

## The mod-specific layer

A mod-specific scenario tests a process: what the machine does with its inputs over time. What any
block of its kind does by existing (placing, breaking from its cells, being used, reloading, taking
power, refusing snow) is the generic layer's, so a file does not repeat it.

### The twin-tub blower fills its run

`iiex/tests/live/blower-air.json`. The blower's air is `IiexValues.TwinTubBlowerOutputPerSecond` (45
L/s) times the fraction of its speed between `TwinTubBlowerMinSpeed` (0.5) and `TwinTubBlowerMaxSpeed`
(1.5), capped at `TwinTubBlowerMaxPressure` (2.2 atm). The run is one straight pipe on the far end of
the pass-through cells, which couple it to the principal's own connector, capped by a drain that takes
what the run holds above 1 atm, as a furnace's tuyeres would.

```json
{
  "description": "The twin-tub blower fills its run with air once its shaft passes the minimum speed, and not before.",
  "plot": [8, 6, 10],
  "timeout": 60,
  "cases": {
    "below-minimum": {
      "steps": [
        { "place": { "id": "blower", "code": "iiex:furnace-twintubblower-n", "at": [0, 0, 1] } },
        { "place": { "id": "run", "code": "iiex:pipe-cast-straight-ns", "at": { "of": "blower", "cell": [0, 0, -2], "beyond": "n" } } },
        { "power": { "at": { "of": "blower", "cell": [0, 1, 0] }, "face": "w", "speed": 0.4, "torque": 5 } },
        { "wait": { "until": { "at": { "of": "blower" }, "tree": "blowerSpeed", "min": 0.3 }, "max": 15 } },
        { "wait": 10 },
        { "expect": { "at": { "of": "run" }, "network": "pipe", "member": "Volume", "max": 0 } }
      ]
    },
    "running": {
      "steps": [
        { "place": { "id": "blower", "code": "iiex:furnace-twintubblower-n", "at": [0, 0, 1] } },
        { "place": { "id": "run", "code": "iiex:pipe-cast-straight-ns", "at": { "of": "blower", "cell": [0, 0, -2], "beyond": "n" } } },
        { "place": { "code": "exlib:creativedrain", "at": { "of": "run", "beyond": "n" }, "tree": { "rate": 20, "pressure": 1 } } },
        { "power": { "at": { "of": "blower", "cell": [0, 1, 0] }, "face": "w", "speed": 1.5, "torque": 5 } },
        { "wait": {
            "until": { "at": { "of": "run" }, "network": "pipe", "member": "Pressure", "min": 1.5 },
            "hold": { "at": { "of": "run" }, "network": "pipe", "member": "Pressure", "max": 2.2 },
            "max": 30 } },
        { "expect": { "at": { "of": "blower" }, "tree": "blowerSpeed", "min": 0.5 } },
        { "expect": { "at": { "of": "run" }, "network": "pipe", "member": "MediumType", "equals": "Air" } }
      ]
    }
  }
}
```

The air enters at the plot's ambient 20 C, since the blower reads `GetClimateAt` for it.

### The smoke stack draws exhaust

`siex/tests/live/smokestack-vent.json`. A formed stack draws `SiexValues.SmokestackGasIntakeVolume`
(48 L) from the run at its intake every production tick and records it as `lastConsumedAmount`; an
unformed one draws nothing. The intake couples on the face its orientation names (`HasConnectorAt`),
north for `-n`; the fluid source caps the flue's far end.

```json
{
  "description": "A formed smoke stack draws exhaust from the run at its intake; an unformed one draws nothing.",
  "plot": [8, 16, 8],
  "timeout": 60,
  "cases": {
    "formed": {
      "steps": [
        { "place": { "id": "stack", "code": "siex:smokestack-intake-tier1-n", "at": [0, 0, 0], "complete": "all" } },
        { "place": { "id": "flue", "code": "iiex:pipe-cast-straight-ns", "at": { "of": "stack", "layout": [0, 0, 0], "beyond": "n" } } },
        { "source": { "at": { "of": "flue" }, "medium": "Exhaust", "rate": 60, "pressure": 1.5, "temperature": 300 } },
        { "wait": { "until": { "at": { "of": "stack" }, "tree": "lastConsumedAmount", "min": 1 }, "max": 15 } }
      ]
    },
    "unformed": {
      "steps": [
        { "place": { "id": "stack", "code": "siex:smokestack-intake-tier1-n", "at": [0, 0, 0] } },
        { "place": { "id": "flue", "code": "iiex:pipe-cast-straight-ns", "at": { "of": "stack", "layout": [0, 0, 0], "beyond": "n" } } },
        { "source": { "at": { "of": "flue" }, "medium": "Exhaust", "rate": 60, "pressure": 1.5, "temperature": 300 } },
        { "wait": 10 },
        { "expect": { "at": { "of": "stack" }, "tree": "structureComplete", "equals": false } },
        { "expect": { "at": { "of": "stack" }, "tree": "lastConsumedAmount", "max": 0 } }
      ]
    }
  }
}
```

### The Cornish boiler fed from its west port

`iiex/tests/live/boiler-side-feed.json`. Feedwater enters only through `Port(WEST, "pipe")` on footprint
cell `(-1, 1, 0)`. The `port` position finds the cell across that port's face whatever the facing; for
`-n` (`StructureAngle` 180) it lies east of the vessel, and a `we` straight pipe couples it, its far end
capped by the water source. The boiler's water, fire and pressure are read from its tree (`waterVolume`,
`lit`) and its public members (`IsConstructed`, `CanLightBed`, `InternalPressure`); the fire is worked
through its own verbs (`ToggleMainHatch`, `TryChargeBed`, `LightBed`). Two more cases pipe water to the
old front face and to the east side and expect nothing to enter.

```json
{
  "description": "The Cornish boiler takes feedwater through its west port only, and boils to pressure on a bituminous bed.",
  "plot": [12, 8, 14],
  "timeout": 420,
  "cases": {
    "west-port": {
      "steps": [
        { "place": { "id": "boiler", "code": "iiex:boilercornish-n-boilerplate", "at": [0, 0, -3], "complete": "all", "materials": { "metal": "iron" } } },
        { "expect": { "at": { "of": "boiler" }, "member": "IsConstructed", "equals": true } },
        { "place": { "id": "feed", "code": "iiex:pipe-cast-straight-we", "at": { "of": "boiler", "port": [-1, 1, 0] } } },
        { "source": { "at": { "of": "feed" }, "medium": "Water", "rate": 40, "pressure": 1.0 } },
        { "wait": { "until": { "at": { "of": "boiler" }, "tree": "waterVolume", "min": 300 }, "max": 60 } },
        { "call": { "at": { "of": "boiler" }, "method": "ToggleMainHatch" } },
        { "call": { "at": { "of": "boiler" }, "method": "TryChargeBed", "args": ["player", { "stack": "game:ore-bituminouscoal", "count": 16 }] } },
        { "expect": { "at": { "of": "boiler" }, "member": "CanLightBed", "equals": true } },
        { "call": { "at": { "of": "boiler" }, "method": "LightBed" } },
        { "wait": {
            "until": { "at": { "of": "boiler" }, "member": "InternalPressure", "min": 0.5 },
            "hold": { "at": { "of": "boiler" }, "tree": "lit", "equals": true },
            "max": 300 } },
        { "expect": { "at": { "of": "boiler" }, "member": "InternalPressure", "min": 0.5, "max": 5 } }
      ]
    },
    "front-face": {
      "timeout": 60,
      "steps": [
        { "place": { "id": "boiler", "code": "iiex:boilercornish-n-boilerplate", "at": [0, 0, -3], "complete": "all", "materials": { "metal": "iron" } } },
        { "place": { "id": "feed", "code": "iiex:pipe-cast-straight-ns", "at": { "of": "boiler", "cell": [0, 0, 0], "beyond": "s" } } },
        { "source": { "at": { "of": "feed" }, "medium": "Water", "rate": 40, "pressure": 1.0 } },
        { "wait": 20 },
        { "expect": { "at": { "of": "boiler" }, "tree": "waterVolume", "max": 0 } }
      ]
    },
    "east-side": {
      "timeout": 60,
      "steps": [
        { "place": { "id": "boiler", "code": "iiex:boilercornish-n-boilerplate", "at": [0, 0, -3], "complete": "all", "materials": { "metal": "iron" } } },
        { "place": { "id": "feed", "code": "iiex:pipe-cast-straight-we", "at": { "of": "boiler", "cell": [1, 1, 0], "beyond": "e" } } },
        { "source": { "at": { "of": "feed" }, "medium": "Water", "rate": 40, "pressure": 1.0 } },
        { "wait": 20 },
        { "expect": { "at": { "of": "boiler" }, "tree": "waterVolume", "max": 0 } }
      ]
    }
  }
}
```

The old front face is the principal's declared `feedwaterFace` (`"south"`), turned by `StructureAngle`
as `BlockBoiler.FeedwaterWorldFace` turns it. The heat-up is `BoilerHeatUpSeconds` (180 s) on a
bituminous bed, so the `west-port` case runs about four minutes. A line written from the boiler's guide
works the same verbs through `interact`.

### Factory lines

A factory line is a whole working setup, written by an agent from what the mods ship to players and run
the way a player is told to run it. The sources, in the order an author reads them:

| Source | Where | Standing |
|---|---|---|
| the wiki guides | `mods/<mod>/wiki/structures/*.md`, `mods/<mod>/wiki/mechanics/*.md`, `Getting-started.md` | what a player is told; cited as `guide` |
| the handbook | `mods/<mod>/assets/<mod>/config/handbook/*.json`, its text in `mods/<mod>/assets/<mod>/lang/en.json` | the same guides in game; cited by lang key |
| the old diagrams | `workbench/textures/diagrams/<group>/diag-*.png` (energymp, finished-items, furnaces, machines, molten, pipes, sandcasting-patterns, structures) and the layout scratchpad `workbench/layouts.md` | stale, a generic reference for how parts sit together; the goldens win where they disagree |
| the design pages | `docs/design/machines/*.md`, `docs/design/processes/*.md` | settled; cited as `design` where they say the same as the guide |
| the goldens | `mods/<mod>/tests/goldens/<mod>/blocktypes/**` | the exact codes, variant groups and footprints; never cited, never a claim |

The author enacts each instruction of the guide as the step a player's gesture maps to (`place`, `run`,
`interact`, `break`, `insert` for a container the guide says to fill), cites it, and fills what the guide
leaves out (a quantity, a route for the pipe, which cell a part goes in) from a design page, a diagram or
its own reasoning, citing what it used. Expectations are what the guide promises the player will see.
Layout geometry comes from the structure's own frame (`layout`, `cell` and `port` positions and
`complete`), never from a hand-built schematic. A failed line then points at the mod, the guide or the
file by its suspect.

`iiex/tests/live/lines/cold-blast.json` is the first. It follows the cold blast furnace's guide from an
empty plot to pig iron in the canal. Its codes are the goldens'; the parts' cells are the design page's
functional cells; the hopper's top cell and the Ctrl gesture that pours a whole stack are the design
page's verbs, which the guide does not mention; the pipe route and the charge quantities (a third coke
by count, six rounds) are the author's.

```json
{
  "description": "A cold blast furnace built, charged, blown and tapped the way its guide tells a player runs pig iron and slag into its canals.",
  "follows": [
    "mods/iiex/wiki/structures/blastcore.md",
    "workbench/textures/diagrams/furnaces/diag-furnace-coldblast.png"
  ],
  "plot": [16, 12, 16],
  "timeout": 1800,
  "steps": [
    { "place": { "id": "bf", "code": "iiex:furnace-blastcore-tier1-n", "at": [2, 0, 0] },
      "guide": "#building-it/1" },
    { "place": { "id": "tuyere-n", "code": "iiex:furnace-tuyere-n", "at": { "of": "bf", "layout": [0, 2, -2] } },
      "guide": "#building-it/4", "design": "docs/design/machines/blast-furnace-cold.md#functional-cells-read-off-the-drawing" },
    { "place": { "id": "tuyere-s", "code": "iiex:furnace-tuyere-s", "at": { "of": "bf", "layout": [0, 2, 2] } },
      "guide": "#building-it/4", "design": "docs/design/machines/blast-furnace-cold.md#functional-cells-read-off-the-drawing" },
    { "place": { "id": "irontap", "code": "iiex:furnace-irontap-w", "at": { "of": "bf", "layout": [2, 1, 0] } },
      "guide": "#building-it/5", "design": "docs/design/machines/blast-furnace-cold.md#functional-cells-read-off-the-drawing" },
    { "place": { "id": "slagtap", "code": "iiex:furnace-slagtap-e", "at": { "of": "bf", "layout": [-2, 1, 0] } },
      "guide": "#building-it/5", "design": "docs/design/machines/blast-furnace-cold.md#functional-cells-read-off-the-drawing" },
    { "place": { "id": "hopper", "code": "iiex:hopper-tall-e", "at": { "of": "bf", "layout": [-1, 6, 0] } },
      "guide": "#building-it/6" },
    { "complete": { "of": "bf" }, "guide": "#building-it/7" },
    { "expect": { "at": { "of": "bf" }, "member": "StructureComplete", "equals": true }, "guide": "#building-it/7" },

    { "place": { "id": "ironcanal", "code": "iiex:molten-canal-start-fire-n", "at": { "of": "bf", "layout": [3, 0, 0] }, "by": "player", "facing": "w" },
      "guide": "#the-first-run/1", "design": "docs/design/machines/blast-furnace-cold.md#products-pools-and-taps" },
    { "place": { "id": "slagcanal", "code": "iiex:molten-canal-start-fire-n", "at": { "of": "bf", "layout": [-3, 0, 0] }, "by": "player", "facing": "e" },
      "guide": "#the-first-run/1", "design": "docs/design/machines/blast-furnace-cold.md#products-pools-and-taps" },

    { "repeat": { "times": 6, "steps": [
        { "interact": { "at": { "of": "hopper", "cell": [0, 1, 0] }, "with": { "stack": "game:coke", "count": 32 }, "keys": ["ctrl"] },
          "guide": "#the-charge", "design": "docs/design/machines/blast-furnace-cold.md#the-players-verbs" },
        { "wait": { "until": { "at": { "of": "hopper" }, "info": "^Empty" }, "max": 120 } },
        { "interact": { "at": { "of": "hopper", "cell": [0, 1, 0] }, "with": { "stack": "iiex:burden", "count": 64 }, "keys": ["ctrl"] },
          "guide": "#the-charge", "design": "docs/design/machines/blast-furnace-cold.md#the-players-verbs" },
        { "wait": { "until": { "at": { "of": "hopper" }, "info": "^Empty" }, "max": 240 } }
      ] },
      "guide": "#the-first-run/2" },

    { "place": { "id": "blower", "code": "iiex:furnace-twintubblower-n", "at": { "of": "bf", "layout": [-7, 0, 4] } },
      "guide": "#blowing-it" },
    { "run": { "code": "iiex:pipe-plated-straight-ns",
               "from": { "of": "bf", "layout": [0, 2, -2], "beyond": "n" },
               "via": [{ "of": "bf", "layout": [-4, 2, -3] }, { "of": "bf", "layout": [-4, 2, 3] }],
               "to": { "of": "bf", "layout": [0, 2, 2], "beyond": "s" } },
      "guide": "#blowing-it" },
    { "run": { "code": "iiex:pipe-plated-straight-ns",
               "from": { "of": "blower", "cell": [0, 0, -2], "beyond": "n" },
               "via": [{ "of": "bf", "layout": [-7, 0, 0] }, { "of": "bf", "layout": [-7, 2, 0] }],
               "to": { "of": "bf", "layout": [-5, 2, 0] } },
      "guide": "#blowing-it" },
    { "expect": { "at": { "of": "tuyere-n" }, "network": "pipe", "same": { "of": "tuyere-s" } }, "guide": "#blowing-it" },
    { "power": { "at": { "of": "blower", "cell": [0, 1, 0] }, "face": "w", "speed": 1.5, "torque": 5 },
      "guide": "#the-first-run/3" },
    { "wait": { "until": { "at": { "of": "tuyere-n" }, "network": "pipe", "member": "Pressure", "min": 1.2 }, "max": 60 },
      "guide": "#the-first-run/3", "design": "docs/design/mechanics/heat-balance.md#blast-demand-derived-from-the-burden-not-from-the-furnace" },

    { "interact": { "at": { "of": "irontap" } }, "guide": "#the-first-run/4", "design": "docs/design/machines/blast-furnace-cold.md#the-players-verbs" },
    { "interact": { "at": { "of": "slagtap" } }, "guide": "#what-goes-wrong" },
    { "expect": { "at": { "of": "irontap" }, "member": "IsPlugged", "equals": false }, "guide": "#the-first-run/4" },
    { "interact": { "at": { "of": "irontap" }, "with": { "stack": "game:torch-basic-lit-up" } },
      "guide": "#the-first-run/4", "design": "docs/design/machines/blast-furnace-cold.md#the-players-verbs" },
    { "wait": { "until": { "at": { "of": "bf" }, "member": "State", "equals": "Firing" }, "max": 30 },
      "guide": "#the-first-run/5", "design": "docs/design/machines/blast-furnace-cold.md#ignition-running-going-out" },
    { "wait": {
        "until": { "at": { "of": "bf" }, "member": "State", "equals": "Melting" },
        "hold": { "at": { "of": "bf" }, "member": "State", "matches": "^(Firing|Melting)$" },
        "max": 900 },
      "guide": "#the-first-run/5" },
    { "wait": { "until": { "at": { "of": "ironcanal" }, "member": "CellMetalType", "matches": "pigiron" }, "max": 120 },
      "guide": "#the-first-run/6", "design": "docs/design/machines/blast-furnace-cold.md#products-pools-and-taps" },
    { "wait": { "until": { "at": { "of": "slagcanal" }, "member": "CellMetalType", "matches": "slag" }, "max": 120 },
      "guide": "#the-first-run/6" }
  ]
}
```

It runs in real time: the harness's own campaign reaches Melting within 900 s, so the line takes up to
about twenty-five minutes. Read by suspect: a furnace that never reaches Firing points at the mod, since
the design page settles what lights it; one that fires and never melts points at the guide, which names
no quantities and leaves the charge to the author; a hopper that never empties points at the file.

The next lines follow the same shape: the hot blast furnace with its cowper stoves and stack, the steam
plant from boiler to engine to a power load, the Bessemer converter fed by a molten source, the rolling
line fed hot stock by `insert` with a `temperature`.

---

## Numbers

| Value | Number |
|---|---|
| Server port and address | 42498 on 127.0.0.1 (`-Port` moves the port); smoke keeps 42499 |
| Runner tick (step loop, wait polls, interaction steps) | 100 ms |
| Default plot | 16 x 16 x 16 blocks, in whole chunk columns (32 blocks) with an 8-block margin; the row starts 32 blocks east of the world spawn |
| Map region, the unit of `climate` | 512 blocks |
| Default climate | 20 C, rainfall 0 |
| Default case timeout / settle / wait `max` / `repeat` `max` | 120 s / 3 s / 60 s / 20 rounds |
| `complete`'s wait for `StructureComplete` | 2 x `CompletionTickMs` + 1 s |
| Drop collection | one runner tick after the break; plot box plus 2 blocks |
| Float `equals` tolerance | 1e-4 |
| Scenario player | name `Scenario`, uid `exlib-scenario`, role `admin`; parked 16 blocks above the plot row; stands 2 blocks from its target |
| Power source / load range | speed 0 to 3, torque 0 to 20, vanilla units |
| Fluid source / drain rate | 0 to 500 L/s |
| Generic tick | 5 s |
| Generic spacing | 3 blocks between single blocks; a footprint's extent between megablocks |
| Generic neighbour block | `game:rock-granite` |
| Generic `power` source | speed 1, torque 5; the network must turn within 5 s |
| Generic `nosnow` climate | -10 C, rainfall 1, 24 game hours |
| Failure detail | trees cut at 2000 characters each; the first 20 log entries; 8 stack frames each |
| Coverage report | the first 3 covering cases named per hook, and a count |
| extools' margin over boot plus budget | 60 s |

Units are the conventions' own: seconds of real time for `wait` and `timeout`, game hours for `advance`,
litres and L/s, atm, C.

---

## Code (proposed)

### The live mod, in exlib's testing family

`src/ExpandedLib.Testing.Live`, assembly `ExpandedLib.Testing.Live`, a mod of its own beside the harness
project `src/ExpandedLib.Testing`. It builds for 1.22 only, since it references VintagestoryLib's server
classes. It stays out of every release until fallen rules it ready to ship; extools builds and stages it
for `exmod scenario`.

What the mod folder holds: the assembly and its `modinfo.json` (`type` `code`, mod id `exliblive`,
`side` `Server` with `requiredOnClient` false, so a client joining a `-Keep` server needs only exlib,
and dependencies on the game and on `exlib` at exlib's own version). Nothing else: exlib's own mod
folder supplies exlib's two assemblies at run time.

What the assembly references, each without copying it (`Private` false): the game's `VintagestoryAPI`,
`VintagestoryLib`, `VSSurvivalMod`, `VSEssentials`, `0Harmony` (the coverage probe) and `Newtonsoft.Json`
(the scenario files), and the projects ExpandedLib and ExpandedLib.Industry. Industry is referenced
directly, since `expect network` reads `PipeNetwork` and `MpEnergyNetwork`, so every step kind lives in
the live mod and none is registered from outside it.

What it must not reference: ExpandedLib.Testing, NSubstitute or xunit. ExpandedLib.Testing is a library
for test processes, not a mod: it has no `modinfo.json`, it depends on NSubstitute and
`xunit.extensibility.core`, which a server does not carry, and its `HarnessModuleInitializer` registers
`VsAssemblyResolver` on `AppDomain.AssemblyResolve` in whatever process loads it, which in a server
under the repository answers a failed assembly lookup from `.game/1.22` rather than from the install
that booted. Code both need lives in ExpandedLib instead, internal, with exlib's `InternalsVisibleTo`
naming `ExpandedLib.Testing.Live` beside `ExpandedLib.Testing`.

One `ModSystem`, `ScenarioModSystem`, reads `coverage.on` in `StartPre` and, on the server, registers
`ScenarioSubCommand` into exlib's `/exmod` group through `CommandRegistry.RegisterAll` over its own
assembly.

| Type | Place | Role |
|---|---|---|
| `ScenarioModSystem` | root | the coverage switch at `StartPre`, the command registration |
| `ScenarioSubCommand` | `Commands/` | `/exmod scenario run <names...> [keep] [failuncovered]`, server side, the `/exmod` group's `controlserver` privilege, a `SubCommandRegister` sub-command of exlib's group |
| `ScenarioFile` | `Scenarios/` | parse, validate (unknown keys named), expand the matrix, substitute, resolve citations against the staged passages |
| `ScenarioRunner` | `Scenarios/` | the queue of cases, the 100 ms main-thread loop, `ILogger.EntryAdded` capture, the census check, the output lines, the failure detail and its suspect |
| `ScenarioPlot` | `Scenarios/` | allocation in chunk columns, climate hold, chunk loading and reloading, the placed-cell set, the schematic snapshot, clearing |
| `ScenarioPosition` | `Scenarios/` | the position forms and the structure frames |
| `ScenarioPlayer` | `Scenarios/` | building, checking, parking and removing the scenario player; its place, break and interact verbs |
| `IScenarioStep`, `ScenarioSteps` | `Scenarios/Steps/` | one internal class per step kind, and the internal table of kinds |
| `GenericScenarios` | `Scenarios/Generic/` | reads each blocktype's signals and `generic.json`, and builds its cases out of the same steps |
| `CoverageProbe`, `CoverageReport` | `Scenarios/Coverage/` | hook enumeration, the Harmony prefix installed from `StartPre` when `coverage.on` exists, the statuses, the allowlist, the report files |
| `IScenarioHost` | `Scenarios/` | the seam (world, block accessor, clock, log feed, chunk loader, climate, player) through which exlib's test project drives the steps the harness host supports in `TestWorld` |

Three existing pieces are shared rather than copied, and one is not needed.

- `BlockEntityMultiblockStructure` already computes the rotated demand from the live block:
  `IncompleteBlockCount` (protected today) with its `MissingCell` report, `CompletionTickMs` (protected)
  and the connector table (`MultiblockConnectors`, private) become internal, and the block entity also
  exposes its rotated layout cells for `layout` positions. `complete` and the failure detail's
  missing-cell report read these.
- The alternation choice `StructureRig` makes (`FirstAlternative`, `FirstBranch`) moves into
  ExpandedLib, where the rig and the runner both call it; the rig's stand-in naming (`Concretize`, which
  also turns `*` into a stand-in letter) stays in the rig. `StructureRig` is rebased on the block's own
  report: `Around` raises the cells the placed block entity reports missing, instead of re-deriving the
  demand from `ExBlockDef.ToJson()`, so it also rigs a block defined in JSON only.
- `reload`'s tree comparison (a key missing, a type changed, a value back at a fresh instance's) is one
  internal routine in ExpandedLib, which the live `reload` and the harness's reload guard both call.
- The construction walk shares nothing: it drives vanilla's `RightClickConstruction` through the
  scenario player's hotbar. exlib's `ExRightClickConstruction` builds only on the 1.20 and 1.21 lanes and
  plays no part in a 1.22 run.

`ExpandedLib.Testing` gains the harness guards the floor names as new, as checks each mod's test project
runs over its own definitions: `GetBlockInfo` on a fresh block entity of every block; a container's
acceptance and extraction; the reload tree comparison through `TestWorld.Reload` over a fresh block
entity of every block. The break-every-cell guard the floor leans on is plan task B3's, built in its own
lane; the generic layer waits for it.

### The harness host

`IScenarioHost` over `TestWorld` runs a scenario's steps where the harness can mean the same thing, and
refuses the rest when the file loads, with an error naming the step kind, so a file never half-passes
in the harness.

| Supported over `TestWorld` | Refused, and what the harness lacks |
|---|---|
| `place` by the world, `complete` of a layout, `set`, `call` (without a `"player"` argument), `insert`, `expect` `block`, `member`, `tree`, `info` (for a `TestWorld.Player`), `network`, `footprint`, `wait` (block entity time through `AdvanceBlockEntityTime`, network ticks through `Tick`), `advance` (`AdvanceHours`), `reload` (`TestWorld.Reload` per block entity, the in-memory round trip), `repeat` | `place` by the player, `run`, `interact`, `complete` of construction stages, `break` either way (the harness's player, `TestPlayer`, is a substitute with one `DummySlot` and a substituted inventory manager, so vanilla's construction consumption finds no hotbar, and the harness has no server event manager for the verbs to trigger; the accessor's `BreakBlock` runs only the block entity's `OnBlockBroken` and `OnBlockRemoved`), `expect drops` (no item entities and no `GetDrops` on the break), `paste` (no schematic placement), `power` (it would meet the `MechPower` double, not vanilla's network manager) and `source` (the harness's pipe fixtures feed a run directly), a `climate` (no weather) |

### The creative sources, in exlib

`ExpandedLib.Industry`, under `Creative/`: `BlockCreativePower` with `BEBehaviorCreativePower` (a
`BEBehaviorMPBase` producer), `BlockCreativeLoad` with `BEBehaviorCreativeLoad` (a consumer), and
`BlockEntityCreativeFluid`, `BlockEntityCreativeDrain` and `BlockEntityCreativeMolten`, each with its
definition, shape, lang and handbook lines, and each listed in `wiki/Supported-API.md`. They ship with
exlib on its usual rule, once fallen has seen them in game.

### extools

`exmod/run.ps1`: `Invoke-Scenario`, sharing a scratch-server helper factored out of `Invoke-Smoke` (boot,
readiness wait, stdin, log follow, stop, cleanup), registered with `Add-ExmodCommand -Group run -Name
scenario`; the live mod's build and staging; the citation resolver.

---

## Gotchas

- A paste restores what the exported block entities held: fuel, fill levels, construction stage,
  `StructureComplete`, a lit fire. Export a layout cold (unlit, empty) unless the case means to start warm.
- A pasted tree carries the exporting world's network ids. Vanilla mechanical power ignores a saved id
  on the server and rejoins in `OnPlacementBySchematic`; exlib's pipe, molten and mpenergy members must
  rejoin the same way, and exlib's paste test checks it before a layout relies on it.
- An open pipe end leaks ([pipe-network](mechanics/pipe-network.md)); `source` caps the end it takes,
  and any other open end of a run spends part of the rate.
- A snow layer set with `SetBlock` bypasses `AllowSnowCoverage` and `GetSnowCoveredVariant`; only the
  climate plus `advance` path exercises what snow does in play.
- Weather is per region, so every `climate` case gets a region of its own. A region that has already
  taken snow snapshots covers a fresh plot's chunks as they load
  (`WeatherSimulationSnowAccum.TryImmediateSnowUpdate`), before the case has placed anything.
- Time is real. A furnace campaign the harness runs in `RunLive(540)` takes nine minutes live. A factory
  line runs a campaign start to finish once; its variations stay in the harness.
- The scenario player's `PlayerName` and `IpAddress` read the client it does not have and return null.
  A mod that reads `PlayerName` from the acting player throws only in a scenario; the stack trace names
  the property.
- The player verbs mirror the server's handlers, so a change to `TryModifyBlockInWorld` or
  `HandleBlockInteract` in an engine update does not reach them by itself. The live mod's source cites
  both, and an engine update is read against them.
- A `world` break carries no player, so a `GetDrops` that reads the breaker's tool sees none.
- `advance` moves the calendar for the whole world: every standing plot sees the jump.
- `reload`'s tree check compares against a fresh instance's values, so a key whose live value happens to
  equal its default passes unexamined. The check finds fields that never persist, not every wrong value.
- Coverage counts a call, not a correct result: a hook called in a failed case is covered, and the case's
  own failure is what reports it.
- A hook reached only by one event (an explosion, an entity landing on the block) stays uncovered until a
  case causes that event or the allowlist names it with a reason.

---

## Left out of the first version

- Anything a client receives or draws: packets, sync, rendering, animation, sound, particles, and the
  client half of an interaction.
- Mining time: a player break is instant, after the tier check.
- Fast-forwarded ticks. Cases run one at a time. Running every `lifecycle` plot through one shared tick
  and one reload, with errors attributed by the position their log entry names, would bring the generic
  layer to about ten minutes.
- Items and other non-block collectibles in the generic layer and in the coverage report.
- Step kinds registered by other mods through a public API; the step kinds are an internal table of the
  live mod.
- Hand-drawn grids in a scenario, and exporting a plot by command (WorldEdit covers it).
- Item entities as inputs (a hopper or chute fed by thrown items).
- Runs on 1.20 and 1.21.
