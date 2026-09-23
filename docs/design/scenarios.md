# Live-Server Scenarios
**Status** proposed 2026-09-23   **Mod** exlib (the runner and `/exmod scenario`), extools (`exmod scenario`), every mod's `tests/live/` files
**Owns** the scenario file format and where scenario files live; the step kinds (placing a block, a completed structure, a pasted layout; supplying items, fluids, fuel and mechanical power; stepping time; breaking; expecting); plots, climates and positions; the `[scenario]` output grammar and its closing count line; the failure detail; the scratch world `exmod scenario` boots and what `-Keep` leaves running.
**Depends on** [multiblock](mechanics/multiblock.md) (the footprints, layouts, monitor tick and missing-cell report a `complete` step drives); [pipe-network](mechanics/pipe-network.md) (the pool a `source` step feeds); [mp-energy](mechanics/mp-energy.md) (the vanilla mechanical power a `power` step drives, bridged at the flywheel hub)

---

## Role

The harness runs a machine in a fake world. A scenario runs the same machine on a real dedicated
server: the engine's own chunks, block entity lifecycle, tick scheduler, network systems, weather and
break path, with nothing faked. It checks mechanics, not looks. No client is connected during an
automated run, so rendering, animation, sound, particles and anything a client computes are outside it.

`exmod smoke` answers "does the server load this". A scenario answers "does this machine stand up,
run, and come apart cleanly in a live world". A scenario file places single blocks, completed
structures or whole layouts pasted from a WorldEdit schematic, feeds them, lets time pass, breaks them
and checks what happened. Each run prints `[scenario]` lines and ends with one count line that
extools parses.

---

## How it works

### The world

`exmod scenario` boots the server the way `exmod smoke` does (a scratch `--dataPath`, the staged mods,
port 42499) and writes `serverconfig.json` into the scratch data path before the first boot, so the
server creates this world:

| Setting | Value | What it buys |
|---|---|---|
| `WorldConfig.WorldType` | `superflat` | vscreativemod's `GenBlockLayersFlat` lays the layers of `creative/worldgen/layers.json` (claystone and two soils) and turns entity spawning off; vsessentialsmod's `GenMaps` still generates the climate map for a superflat world |
| `WorldConfig.PlayStyle` | `surviveandbuild` | the default playstyle; survival and the family mods load as in play |
| `WorldConfiguration.gameMode` | `creative` | a joining player can fly to a plot and look; scenario breaks do not depend on it |
| `WorldConfiguration.snowAccum` | `true` (the default) | vanilla's snow accumulation runs, which the smoke stack scenario drives |
| `WorldConfiguration.temporalStorms`, `temporalStability` | `off`, `false` | nothing temporal lands in a plot |
| `PassTimeWhenEmpty` | `true` | without it `ServerSystemCalendar` stops the calendar while no client is playing, and every machine that reads game time stands still during an automated run. Tick listeners run either way |
| `WhitelistMode`, `AdvertiseServer` | `Off`, `false` | |
| `DefaultRoleCode` | `admin`, with `-Keep` only | a joining client can run `/exmod scenario`, `/tp` and `/gamemode` |

Commands written to the server's stdin run on the main thread as the console player
(`ServerConsolePlayer`), which holds every privilege and has no entity and no inventory.

### Where the files live

| Repository | Scenarios | Schematics |
|---|---|---|
| exlib | `tests/live/<name>.json` | `tests/live/schematics/<file>.json` |
| exmods | `mods/<mod>/tests/live/<name>.json` | `mods/<mod>/tests/live/schematics/<file>.json` |

Not `tests/Scenarios`: that folder already holds the harness scenario tests, and a case-insensitive
filesystem would merge the two. Scenario files never ship. extools copies each mod's `tests/live/`
into `<dataPath>/Scenarios/<modid>/` before the boot, and the runner reads only from there
(`ICoreServerAPI.GetOrCreateDataPath("Scenarios")`), or from an explicit path.

A scenario is named `<modid>/<file stem>`. A file with `cases` names each case `<modid>/<stem>/<case>`,
and a matrix combination appends its values: `iiex/blower-break/idle[side=e cell=0,1,0]`.

### The file

JSON as the game reads its own assets (comments and unquoted keys allowed). An unknown key is an
error that names the file, the step index and the key, so a misspelt step never silently passes.

| Key | Type | Default | Meaning |
|---|---|---|---|
| `description` | string | required | one line on what the scenario proves |
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

Each case runs on a plot of its own, never reused within a run. Plots run in a row along +X, starting
32 blocks east of the world spawn, each `plot` wide plus an 8-block gap. A case with a `climate` gets a
plot at the centre of the next map region no other plot touches (regions are 512 blocks), since
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
| `{ "of": id }` | the principal of the structure a step placed with that `id` |
| `{ "of": id, "cell": [x, y, z] }` | a footprint cell in the footprint's own frame (the principal is `[0, 0, 0]`), turned by the block's `IFillerHost.StructureAngle` through `ExOrientation.RotateOffset`, as `StructureFillers.FootprintCells` turns it |
| `{ "of": id, "layout": [x, y, z] }` | a layout cell in the layout's authored frame, placed where the block entity's rotated `MultiblockStructure` puts it |
| `{ "of": id, "port": [x, y, z] }` | the cell across the declared port face of footprint cell `[x, y, z]` (`FillerCell.PortFace`, already turned) |
| any of the above plus `"beyond": "n"` (`e`, `s`, `w`, `u`, `d`) | the neighbour across that face. For an `of` form the face turns with the structure (`ExOrientation.RotateFacing`); for a plain position it is a world face |

A structure's frame (principal, angle, footprint and layout cells) is taken when it is placed, so an
`of` position still resolves after the structure has been broken.

### Steps

A step is an object with exactly one key, the step kind.

| Group | Kind | Shape |
|---|---|---|
| Placing | `place` | `{ id?, code, at, complete?: "all" \| n, materials?: { wildcard: value } }` |
| | `complete` | `{ of, to?: "all" \| n, materials? }` |
| | `paste` | `{ file, at, angle?: 0 \| 90 \| 180 \| 270, origin?: "BottomCenter" }` |
| Supplying | `power` | `{ at, face, speed: 0-10, torque: 1-10 }` |
| | `source` | `{ at, medium, rate, pressure?: 1, temperature?: 20 }` |
| | `insert` | `{ at, stack, count?: 1, slot? }` |
| | `set` | `{ at, tree: { key: value } }` |
| | `call` | `{ at, method, args?: [...], returns? }` |
| Time | `wait` | seconds, or `{ seconds }`, or `{ until, max }`, each with an optional `hold` |
| | `advance` | `{ hours }` |
| Breaking | `break` | `{ at, by?: "world" \| "player" }` |
| Expecting | `expect` | see Expecting |

#### `place` and `complete`

`place` takes an exact block code (no wildcard) and sets it through the world block accessor with a
stack of that block, `IBlockAccessor.SetBlock(blockId, pos, stack)`, which runs `Block.OnBlockPlaced`:
the block entity is created and initialised, and a megablock places its fillers there. For a
mechanical-power block it then calls `BlockMPBase.WasPlaced`, the network join vanilla's own placement
makes. It does not run `TryPlaceBlock` or `CanPlaceBlock`, since both want a player and a selection. A
target cell, or a footprint cell, that is not air fails the step before anything is placed.

`complete` brings a placed structure up to `to` (default `"all"`); `place` with `complete` is the same
thing in one step. It works in two parts, each applied when the block has it.

- **Layout.** When the block entity is a `BlockEntityMultiblockStructure`, the runner reads the
  structure's own missing cells (its completion demand, rotated) and raises each one with the first
  registered block matching the wanted code (an alternation takes its first branch, as `StructureRig`
  does; the step line names every code chosen). Three kinds of cell are left alone: a cell that wants
  air, a cell a step placed, and a cell whose demand carries an outward connector face (the scenario
  places those itself, turned the way it means). The runner then waits for the block entity's own
  monitor tick to set `StructureComplete`, at most two `CompletionTickMs` intervals plus one second,
  and fails with the missing-cell report otherwise.
- **Construction stages.** When the block entity carries `ExRightClickConstructable`, the runner
  advances it stage by stage through the construction's own ingredient consumption, fed from a supply
  it generates: for each ingredient, the first registered collectible that satisfies it, restricted by
  `materials` where the ingredient stores a wildcard (`"materials": { "metal": "iron" }`). Stored
  wildcards are recorded as a player's hotbar records them, so later stages and the drops resolve the
  same way. `to: n` stops at `CurrentCompletedStage` n, which is how a part-built structure is made.

#### `paste`

`paste` loads a vanilla WorldEdit schematic from the scenario's own `schematics/` folder with
`BlockSchematic.LoadFromFile`, turns it with `TransformWhilePacked` around `origin` (an `EnumOrigin`
name, default `BottomCenter`) by `angle`, and places it the way `WorldEditWorkspace.PasteBlockData`
does: `Place` with `EnumReplaceMode.ReplaceAll`, `PlaceDecors`, then `PlaceEntitiesAndBlockEntities`.
That restores every block entity's saved tree and calls `OnPlacementBySchematic` on it, where vanilla
mechanical power rejoins its network (`BEBehaviorMPBase.tryConnect` on every face). Every pasted cell
counts as placed by a step.

A layout comes from a creative build: mark its corners with the WorldEdit wand, `/we export <file>`,
which writes `<dataPath>/WorldEdit/<file>.json`, and copy that file to `tests/live/schematics/`.

#### `power`

`power` drives the cell at `at` through its `face`: it places vanilla's creative rotor on the far side
of that face, `game:creativerotor-<face as a word>` (`west` for `w`), whose output (`BlockCreativeRotor`, on its `side`'s
opposite) points back into the cell. For an `of` position the face turns with the structure. `speed`
and `torque` are the rotor's own two settings, the ones a player cycles with shift+right-click and
right-click, written to its tree keys `s` and `p`: speed in tenths of a revolution per second (0-10),
torque in steps of vanilla's torque factor 0.5 (1-10). A machine on exlib's mpenergy run takes the same
rotor at its flywheel hub, through the vanilla-MP bridge.

#### `source`

`source` holds the pipe network at `at` supplied, as a pump or a blower does. Every runner tick it calls
the network's own producer entry, `PipeNetwork.ProduceLiquidMeasured` for a liquid medium or
`ProduceGasMeasured` for a gas, with `rate` (L/s) times the tick's seconds at `pressure` (atm) and
`temperature` (C). It runs until the case ends, and the case's closing lines report the litres it
delivered. A run holds one medium (R1), so a source into a run holding another delivers nothing, and
its first tick's line says so. An open pipe end leaks, which spends part of the rate.

#### `insert`, `set`, `call`

`insert` puts `count` of `stack` into the block entity's inventory (`IBlockEntityContainer.Inventory`)
through `ItemSlot.TryPutInto`, into `slot` or the slot the inventory itself picks, so its own
acceptance rules apply. A refusal fails the step.

`set` reads the block entity's tree (`ToTreeAttributes`), writes each key with the type the key already
has, loads it back (`FromTreeAttributes`) and marks the entity dirty. A key the tree does not hold fails
the step. It is for state that has no verb: a fill level, the creative rotor's settings. The treekeys
goldens list each block entity's keys, so a renamed key fails a guard before it fails a scenario.

`call` invokes a public instance method on the block entity, or on one of its behaviours when
`method` is `<BehaviourClass>.<Method>`, matched by name and argument count. Arguments convert to the
parameter types: `null`, numbers, strings (also enum names and asset codes), booleans,
`{ "stack": code, "count": n }` for an `ItemStack` or `ItemSlot` parameter (a `DummySlot` holding it),
and `"caller"` for an `IPlayer` parameter (the joined player who ran the command, else null). `returns`
fails the step when the return value differs. The step line prints the return value and what is left in
a slot argument. This is how a scenario works a machine's verbs, the hatches, the bed, the light,
without a player at the block.

#### `wait` and `advance`

`wait` lets the live server run. A bare number or `{ seconds }` waits that long. `{ until, max }` polls
an expectation every runner tick and moves on when it holds, failing at `max` seconds with the last
value read. `hold` (one expectation or a list) is polled on the same ticks and fails the step the moment
it stops holding, which is how "never" and "stays" are asserted.

`advance` jumps the calendar by `hours` of game time (`IGameCalendar.Add`) and runs one runner tick.
Nothing ticks through the skipped hours: a machine that reads the calendar sees the gap on its next tick,
the path a chunk takes when it reloads, and vanilla's weather catches up its hourly snapshots.

Server ticks come at real speed; a scenario never fast-forwards them. A machine receives the `dt` its
own listener interval gives it.

#### `break`

`break` calls `IBlockAccessor.BreakBlock(pos, player)` on the world accessor, the call the engine's own
break path ends in: `Block.OnBlockBroken`, `GetDrops`, `OnBlockRemoved`, the neighbour updates. A filler
forwards it to its principal as it does in play.

| `by` | Breaker |
|---|---|
| `world` | no player (null). Vanilla drops as for a survival player when the breaker is null, and vanilla and other mods break blocks this way (falling blocks, displaced liquids), so a null dereference here is a real crash |
| `player` | the player who ran the command, switched to survival for the step and back after. Fails when the command came from the console |
| absent | `player` when the command came from a joined player, else `world` |

The engine creates players only for connected clients: `IPlayer` cannot be implemented outside the
game on 1.22 (it declares an internal abstract member), and the console player has no entity or
inventory. An automated run therefore breaks as the world; fallen covers the player path by running
the same scenario from his client in a `-Keep` session. The step line names the breaker.

One runner tick after the break, the runner collects the item entities that appeared in the plot box
(plus a 2-block margin), removes them from the world and keeps them for `expect drops`.

#### Expecting

| Shape | Checks |
|---|---|
| `{ at, block: code }` | the block at `at` matches the code (wildcards as `WildcardUtil` reads them; `game:air` for empty) |
| `{ at, member: path, <test> }` | a public property or field of the block entity, walked by dots. A first segment naming one of its behaviours' classes selects that behaviour |
| `{ at, tree: key, <test> }` | a key of the block entity's `ToTreeAttributes` tree |
| `{ at, info: regex }` | a line of the block entity's `GetBlockInfo` (its behaviours' lines included), in the server's language, with the caller as the player (null from the console) |
| `{ at, network: "pipe", member: name, <test> }` | the pipe network state at the cell: `Volume`, `MaxVolume`, `Pressure`, `Temperature`, `MediumType`, `FlowRate` |
| `{ of, footprint: "filled" \| "air" }` | every footprint cell, the principal included, holds the principal or a filler naming it, or holds air |
| `{ drops: [{ code, count }], exact?: true }` | what the last `break` dropped, summed per code; `exact` also refuses any stack not listed |

A `<test>` is `equals` (booleans, strings, enum names, integers; a float within 1e-4), `min`, `max`
(inclusive), or `min` with `max`. A passing expectation prints the value it read, so a pass line is
also a reading.

### Errors and exceptions

"No exception" is checked on every case without being written. For the whole run the runner listens on
the server logger's `ILogger.EntryAdded`. An `Error` or `Fatal` entry while a case runs fails the case at
the step running then, checked after every step and on every wait poll, unless it matches one of the
case's `allowErrors`. A block entity whose tick listener throws is caught by
`BlockEntity.TickingExceptionHandler`, which logs it as an error, so a machine that throws on its own
tick fails the case it happens in. A throw the engine does not catch stops the server; extools then
finds no count line, fails the run and prints the log's last errors. A warning never fails a case; it
is listed in a failed case's detail.

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
[scenario] run: <S> scenario(s), <C> case(s), budget <B> s
[scenario] case <name> plot <x> <y> <z>
[scenario]   <n> <kind> <what> ok[: <value>]
[scenario]   <n> <kind> <what> FAIL: <reason>
[scenario]   | <detail>
[scenario] pass <name> (<t> s)
[scenario] fail <name> step <n> (<t> s)
[scenario] error <name>: <reason>
[scenario] done: <C> case(s) run, <P> passed, <F> failed.
```

`plot` coordinates are absolute. `error` is a case that could not run: its file does not parse, it names
an unknown code, or its plot never loaded; it counts as failed. `budget` is the sum of the case timeouts,
which extools adds to its own wait. The count line is always the last line of a run; extools matches it
with `\[scenario\] done: (\d+) case\(s\) run, (\d+) passed, (\d+) failed\.`

### When a step fails

The case stops at the failing step (the rest print as skipped) and the runner writes a detail block
under it, enough to fix without a rerun:

- the step as written, and every position it resolved, plot-local and absolute, with the structure's
  angle;
- the expected and the read value, or the exception with its stack frames;
- for each cell the step touched and each footprint cell of the structure it names: the block code and
  the block entity's class;
- the tree of every block entity the case expected against, one line each;
- the missing-cell report of every multiblock in the plot (cell, wanted, actual, outward face);
- every error and warning logged during the case;
- what each `source` delivered;
- the plot saved as a schematic, `<dataPath>/WorldEdit/scenario-<case>.json`, which `/we import` pastes
  into a creative world, and the `/tp` command that reaches the plot.

### `-Keep`

`/exmod scenario run <names...> keep` leaves every plot standing and every source, climate and power
step running after the run. extools leaves the server up for fallen to join and look: each `case` line
carries the plot's coordinates. With several plots standing, a machine on an earlier plot can raise an
error during a later case; the later case's detail says how many plots stand.

### `exmod scenario`

```
exmod scenario [<name>...] [-Version <x.y>] [-Keep] [-Mods <dir>[,...]] [-Timeout <s>] [-KeepData] [-Verbose]
```

A name is a bare stem, `<modid>/<stem>` or `<modid>/<stem>/<case>`; none runs every scenario of the
repository. extools:

1. stages the mods as smoke does, copies each mod's `tests/live/` into `<dataPath>/Scenarios/<modid>/`
   and writes `serverconfig.json`;
2. boots the server and waits for `Dedicated Server now running` in `Logs/server-main.log`;
3. writes `/exmod scenario run <names>` (with `keep` under `-Keep`) to the server's stdin;
4. follows the log and prints the `run`, `pass`, `fail`, `error` and `done` lines, with every line of a
   failed case; `-Verbose` prints every step line;
5. waits for the count line up to the boot, the run's budget and 60 s more, or `-Timeout`;
6. prints every `[Error]` and `[Fatal]` line that falls outside a case (between a `case` line and its
   `pass` or `fail` line is inside);
7. without `-Keep`: writes `/stop`, copies the failed cases' schematics to `.game/.scenario-last/`, and
   exits 1 when the count line is missing, a case failed, or an error line fell outside a case, else 0;
8. with `-Keep`: prints the port, the data path and how to join (`exmod client`, Multiplayer,
   `localhost:42499`), relays the terminal's lines to the server console until Ctrl+C, then writes
   `/stop`. The exit code is the one step 7 would give.

---

## Worked examples

### The twin-tub blower broken from every cell

`iiex/tests/live/blower-break.json`. `BlockTwinTubMPBlower` is a pipe node and a megablock of six cells:
the principal, two pipe pass-through cells behind it, and an upper row holding the mechanical-power port
(`BEBehaviorMPFillerPort`, west face in the north frame), a plain filler and a slab. Its definition
declares no construction stages, so the matrix has no stage axis; a structure with stages adds
`"stage": [0, 2, "all"]` and `"complete": "{stage}"`. Each of the 48 cases breaks one cell of a fresh
blower, idle or turning, and checks that the whole footprint went, that one blower dropped, and, through
`settle`, that nothing threw a moment later.

```json
{
  "description": "The twin-tub blower breaks cleanly from every cell at every facing, idle or turning, and drops itself once.",
  "plot": [8, 6, 8],
  "timeout": 40,
  "matrix": {
    "side": ["n", "e", "s", "w"],
    "cell": [[0, 0, 0], [0, 0, -1], [0, 0, -2], [0, 1, 0], [0, 1, -1], [0, 1, -2]]
  },
  "cases": {
    "idle": {
      "steps": [
        { "place": { "id": "blower", "code": "iiex:furnace-twintubblower-{side}", "at": [0, 0, 0] } },
        { "expect": { "of": "blower", "footprint": "filled" } },
        { "break": { "at": { "of": "blower", "cell": "{cell}" } } },
        { "expect": { "of": "blower", "footprint": "air" } },
        { "expect": { "drops": [{ "code": "iiex:furnace-twintubblower-*", "count": 1 }] } }
      ]
    },
    "turning": {
      "steps": [
        { "place": { "id": "blower", "code": "iiex:furnace-twintubblower-{side}", "at": [0, 0, 0] } },
        { "power": { "at": { "of": "blower", "cell": [0, 1, 0] }, "face": "w", "speed": 5, "torque": 3 } },
        { "wait": { "until": { "at": { "of": "blower" }, "tree": "blowerSpeed", "min": 0.01 }, "max": 15 } },
        { "break": { "at": { "of": "blower", "cell": "{cell}" } } },
        { "expect": { "of": "blower", "footprint": "air" } },
        { "expect": { "drops": [{ "code": "iiex:furnace-twintubblower-*", "count": 1 }] } }
      ]
    }
  }
}
```

The drop code is a wildcard because the blocktype declares no drops, so the placed facing's own code
drops; which code it should be is the blower's design, not this file's.

### The smoke stack under snow

`siex/tests/live/smokestack-snow.json`. The open column (`'a'` in `BlockSmokeStackIntake`'s layout, at
layout `(0, y, 1)` for `y` 0 to 10) is open to the sky, so the column's rain-map top is the refractory
course at layout `(0, -1, 1)`, and vanilla would put a snow layer in layout `(0, 0, 1)`. The stack marks its
open cells with the `nosnow` cell role, and the structure refuses snow at them inside vanilla's own snow
path (postfixes on `Block.AllowSnowCoverage` and `Block.GetSnowCoveredVariant`), so snow never lands
there to be cleared.

The scenario drives vanilla's accumulation, not a placed snow block: a direct `SetBlock` of a snow layer
never asks `AllowSnowCoverage`, and would test nothing. The plot's region is held at -10 C and rainfall
1 (see Climate), a formed stack stands in it, and `advance` jumps 24 game hours. Vanilla then makes 24
hourly snapshots, eight snow levels in all and more than a full cover, the next 3-second sweep queues
the plot's chunks, and the snow is applied within seconds. The case waits until snow lies on a control
brick in the same plot (without it, a climate hook that failed would pass the case) while holding the
open cell air and the stack formed, then holds both for two more monitor intervals.

```json
{
  "description": "Snow falls on a formed smoke stack's plot and never settles in its open column; the stack stays formed.",
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

The core stands at plot `y = 0`, so the layout's floor course (layer -1) replaces the top ground row and
the whole column stands on it. In a `-Keep` session fallen can also stand in the plot and type vanilla's
`/debug snowaccum here <levels>`, which runs the same `UpdateSnowLayer` path for his chunk at once.

### The Cornish boiler fed from its west port

`iiex/tests/live/boiler-side-feed.json`. Feedwater enters only through `Port(WEST, "pipe")` on footprint
cell `(-1, 1, 0)`. The `port` position finds the cell across that port's face whatever the facing; for
`-n` (`StructureAngle` 180) it lies east of the vessel, and a `we` straight pipe couples it. The
boiler's water, fire and pressure are read from its tree (`waterVolume`, `lit`) and its public members
(`IsConstructed`, `CanLightBed`, `InternalPressure`); the fire is worked through its own verbs
(`ToggleMainHatch`, `TryChargeBed`, `LightBed`). Two more cases pipe water to the old front face and to
the east side and expect nothing to enter.

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
        { "call": { "at": { "of": "boiler" }, "method": "TryChargeBed", "args": [null, { "stack": "game:ore-bituminouscoal", "count": 16 }] } },
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
as `BlockBoiler.FeedwaterWorldFace` turns it. The feed pipe's far end is open, so the run leaks; the
source's rate covers the leak, and its closing line reports the litres delivered. The heat-up is
`BoilerHeatUpSeconds` (180 s) on a bituminous bed, so the `west-port` case runs about four minutes.

---

## Numbers

| Value | Number |
|---|---|
| Server port | 42499, smoke's |
| Runner tick (step loop, wait polls, source feed) | 100 ms |
| Default plot | 16 x 16 x 16 blocks; 8-block gap; first plot 32 blocks east of the world spawn |
| Map region, the unit of `climate` | 512 blocks |
| Default climate | 20 C, rainfall 0 |
| Default case timeout / settle / wait `max` | 120 s / 3 s / 60 s |
| `complete`'s wait for `StructureComplete` | 2 x `CompletionTickMs` + 1 s |
| Drop collection | one runner tick after the break; plot box plus 2 blocks |
| Float `equals` tolerance | 1e-4 |
| Failure detail | trees cut at 2000 characters each; the first 20 log entries; 8 stack frames each |
| extools' margin over boot plus budget | 60 s |

Units are the conventions' own: seconds of real time for `wait` and `timeout`, game hours for `advance`,
litres and L/s, atm, C.

---

## Code (proposed)

### exlib

| Type | Place | Role |
|---|---|---|
| `ScenarioSubCommand` | `Registries/Commands/` | `/exmod scenario run <names...> [keep]`, server side, the `/exmod` group's `controlserver` privilege |
| `ScenarioFile` | `Scenarios/` | parse, validate (unknown keys named), expand the matrix, substitute |
| `ScenarioRunner` | `Scenarios/` | the queue of cases, the 100 ms main-thread loop, `ILogger.EntryAdded` capture, the output lines and the failure detail |
| `ScenarioPlot` | `Scenarios/` | allocation, climate hold, chunk loading, the placed-cell set, the schematic snapshot, clearing |
| `ScenarioPosition` | `Scenarios/` | the position forms and the structure frames |
| `IScenarioStep`, `ScenarioSteps` | `Scenarios/Steps/` | one internal class per step kind; the registry through which `ExpandedLib.Industry` adds `source` and the `network` expectation, since it owns `PipeNetwork` |
| `IScenarioHost` | `Scenarios/` | the seam (world, block accessor, clock, log feed, chunk loader, climate) through which the harness drives the same steps in `TestWorld` |

Three existing pieces are shared rather than copied. `StructureRig`'s cell resolution (rotation, the
oriented-part table, the wildcard choice) moves into ExpandedLib, where the rig and the runner both call
it. `ExRightClickConstruction`'s ingredient consumption takes a list of slots, so a player's hotbar and a
generated supply go through one routine and record wildcards the same way.
`BlockEntityMultiblockStructure` exposes its missing cells and its rotated layout cells internally.

### extools

`exmod/run.ps1`: `Invoke-Scenario`, sharing a scratch-server helper factored out of `Invoke-Smoke` (boot,
readiness wait, stdin, log follow, stop, cleanup), registered with `Add-ExmodCommand -Group run -Name
scenario`.

---

## Gotchas

- A paste restores what the exported block entities held: fuel, fill levels, construction stage,
  `StructureComplete`, a lit fire. Export a layout cold (unlit, empty) unless the case means to start warm.
- A pasted tree carries the exporting world's network ids. Vanilla mechanical power ignores a saved id
  on the server and rejoins in `OnPlacementBySchematic`; exlib's pipe, molten and mpenergy members must
  rejoin the same way, and exlib's paste test checks it before a layout relies on it.
- An open pipe end leaks ([pipe-network](mechanics/pipe-network.md)), so a feed run with an open far end
  delivers less than its source's rate.
- A snow layer set with `SetBlock` bypasses `AllowSnowCoverage` and `GetSnowCoveredVariant`; only the
  climate plus `advance` path exercises what snow does in play.
- Weather is per region, so every `climate` case gets a region of its own. A region that has already
  taken snow snapshots covers a fresh plot's chunks as they load
  (`WeatherSimulationSnowAccum.TryImmediateSnowUpdate`), before the case has placed anything.
- Time is real. A furnace campaign the harness runs in `RunLive(540)` takes nine minutes live. A live
  scenario checks that a line starts and holds; the full campaign stays in the harness.
- A `world` break carries no player, so a `GetDrops` that reads the breaker's tool sees none.
- `info` runs `GetBlockInfo` on the server, where the engine never calls it; a throw there fails the
  `info` expectation. `member` and `tree` read the same state without it.
- `advance` moves the calendar for the whole world: every standing plot sees the jump.

---

## Left out of the first version

- Player verbs by interaction (right-click, held use) and tool breaks (`OnBlockBrokenWith`, mining time).
  Scenarios reach machines through `call`, `insert` and `set`.
- Anything a client receives or draws: packets, sync, rendering, animation, sound, particles.
- Fast-forwarded ticks. Cases run one at a time.
- Step kinds registered by other mods through a public API; the registry is internal to exlib's
  assemblies.
- Hand-drawn grids in a scenario, and exporting a plot by command (WorldEdit covers it).
- Item entities as inputs (a hopper or chute fed by thrown items).
- Runs on 1.20 and 1.21. The command builds on every lane and answers on the legacy ones that scenarios
  need 1.22; the schematic placement calls differ there.
- A generated sweep that breaks every shipped filler host from every cell. The harness guard owns that
  class; a scenario file names its blocks.

---

## Open

- Breaking without a joined player. An automated run breaks as the world; the player path runs only when
  fallen issues the command from his client. The alternative is a `ServerPlayer` built through
  VintagestoryLib's own constructor with a spawned entity, which leans on engine internals.
- Test sources. The first version drives power with vanilla's creative rotor and water with a runner-held
  network feed, and ships no blocks. Creative source blocks in exlib (a pump and a gas source a player
  could also use in creative) would put the same supply in fallen's hands.
- Where layouts come from: fallen exports reference builds from creative into `tests/live/schematics/`,
  or scenarios compose them from `place` and `complete` steps. The factory line is composed from steps
  until a reference build exists.
- The `-Keep` admin role. Every client that joins the scratch server while it runs is an admin; the
  server is reachable from the local network on port 42499 for that time.
- Where the runner ships: in the released exlib, where any exlib modder's server admin can run
  scenarios, or in a dev-only module that extools stages.
