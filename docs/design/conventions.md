# Conventions, Units & Shared Rules

The single source of truth for exlib's own units, invariants and network semantics - the framework
half. Every mod doc built on exlib cites this file rather than restating these. The family's own
invariants, its per-mod layout, the shared simulation models and every design page under `machines/`,
`items/` and `mechanics/` outside the seven that document exlib's own networks live in exmods'
`docs/design/conventions.md` instead - that repository is the family's own source of truth for those.

---

## Units

| Quantity | Unit | Notes |
|---|---|---|
| Metal mass | **units (u)** | 100 u = 1 vanilla ingot |
| Fluid/gas volume | **litres (L)** *(live)* | A pipe segment holds 30 L *(live)*; a run's capacity = node count × 30 L. Litres everywhere, never m³ |
| Mechanical power | **MP** *(live)* | Vanilla MP network; the flywheel reservoir model is exlib's own (R8) |
| Flow | **L/s** | Per-tick flow, EMA-smoothed for the throughput readout *(live)* |
| Temperature | **°C** | One network-wide pipe temperature *(live)*; molten canals are per-cell |
| Pressure | **atm** | 1 atm = ambient |

exlib ships no medium of its own beyond the built-in taxonomy defaults (`assets/exlib/config/liquids.json`:
Air, Steam, Exhaust, Water); a family mod's own tunables (leak rates, throughput per tier, engine
efficiency) are its own numbers, recorded in exmods' conventions.

---

## Global invariants (named rules)

Referenced by name from the mod docs. exlib owns the network-shaped rules below; the family's own
invariants (recovery, forming, heat-balance gating, stock mass, code naming - R2 and R4 through R6, R9
and R10 in the numbering exmods still uses) live there, against the machines and materials that give
them content.

- **R1 - Single medium.** A pipe network carries one medium at a time (gas or water) with a
  unified Volume/Temperature/Pressure/MediumType pool *(live)*. Air, steam, exhaust (and the chemistry
  fractions) are gas media; water and the liquid fractions are liquid media.
- **R3 - Molten is per-cell.** Molten metal lives in per-cell molten canals (each block owns its
  metal, flows cell to cell) *(live)*. A merge/mix point is the only thing allowed to join two canals;
  exlib ships none itself - see [molten-network](mechanics/molten-network.md) for the graph, and
  exmods for what a family mod builds on top of it.
- **R7 - Nothing is hidden.** *(revised 2026-07-29)* Every machine's state must be legible to a player
  standing in front of it: temperature, pressure, what is in the pipe, how far through a cycle it is.
  Operation is in-world and verb-based - the player works the machine, not a menu. Windows are allowed
  where the interaction genuinely needs one, and every machine carries status UI. What the rule forbids
  is state that cannot be seen: no hidden timers, no invisible buffers, no "it is doing something, trust
  it".
- **R8 - Mechanical energy is conserved.** *(Added 2026-07-29; the network it governs has been live for
  months.)* A flywheel is a reservoir, not a battery: it stores `E = ½Iω²` in joules and every joule out
  came from a joule in, less friction. Torque balance is `I·dω/dt = τ_drive − τ_load − τ_friction`. No node
  may mint energy, and a run whose total inertia is zero has no speed rather than infinite speed. Owned by
  [mp-energy](mechanics/mp-energy.md). Two clauses of the original proposal are not implemented -
  governor throttling and over-speed burst (`IsOverSpeed` has zero call sites).

---

## Block-size vocabulary

- **block** - a single 1×1×1 block.
- **megablock** - occupies more than one cell via the **filler-block** mechanic; often a
  RightClickConstructable (RCC).
- **multiblock** - a structure the player builds by hand in a specific shape, guided by an in-world
  **projection**. Blocks and megablocks can be parts of a multiblock.

A block can be both - boilers are RCC megablocks whose construction is also gated by a multiblock
projection.

---

## Networks

Five transport-network families, all on exlib's shared block-network graph. The network logic lives
in exlib; the pipe blocks are per-mod tiers.

Three are registered graph types in code (`pipe`, `molten`, `mpenergy`); vanilla MP and the electrical grid
are not exlib networks - vanilla MP is the engine's own, and elex's grid is deferred.

- **Mechanical energy (`mpenergy`)** *(live)* - the flywheel-buffered energy reservoir the heavy machines draw
  from: joules, not torque-at-a-speed. A vanilla waterwheel or windmill is bridged in at the flywheel's hub
  face, and in iiex the player swaps that producer for a steam engine while the network stays identical.
  Governed by R8. Owned by [mp-energy](mechanics/mp-energy.md); blocks ship in iiex under
  `BlockNetworkEnergy/`.
- **Molten-canal** *(live)* - per-cell metal, flows cell→cell, end caps recomputed on tesselation. The
  ladle is the only merge/mix point (R3). Owned by [molten-network](mechanics/molten-network.md).
- **Pipe (gas or water)** *(live)* - single medium per network (R1). Used for water, steam, compressed
  air, exhaust, coal gas and the chemistry fractions. Three material tiers of pipe block, ascending
  burst pressure: plated (iiex - hammered from iron plates, 2.5 atm), cast (iiex - from cast
  pipe-parts finished on the [boring machine](machines/boring-machine.md), 5 atm), and rolled
  ([hpex](machines/rolled-pipe.md) - 12 atm, curled from skelp on iiex's
  [bending roller](machines/bending-roller.md)). Each tier ships its own model at
  `{domain}:pipes/*`.
  Connectors read the adjacent cell; valves sever/flow; pressure valves overflow.

  Tiers do not all interconnect, and the rule is the joint, not the pressure *(live)*. Plated and cast
  pipe are square in section and bolted through flanges, so a plated run and a cast run join freely and
  the weakest segment caps the whole run's burst pressure. Rolled pipe is octagonal and welded, with no
  flange to bolt to, so an HP run couples only to another HP run: an HP main cannot be fed with cheap
  plated pipe.

  Implemented as a joint family registered per domain (`BlockPipe.RegisterJoint`) and enforced in
  exlib's `BlockNetworkNode.AcceptsNeighbour`, checked at the single point both the graph traversal and
  the open-end scan pass through. Two consequences:
  - A refused joint is not a seal. The unmated face reads as an open end, so the run leaks, which is
    what tells a player the two lines are not plumbed together.
  - Every fitting (valve, outlet, passthrough) is a `BlockPipe` subclass, so the rule blocks a rolled
    run from the cast tier's fittings too. The HP tier needs its own fittings, and until it has them a
    rolled run is segments and machine ports only. Machine ports are not pipes and are unaffected.
- **Mechanical power (MP)** *(live)* - vanilla MP network; drives the mechanical blower (iiex) and
  mechanical pump (iiex) as well as engine sub-machines.
- **Electrical (AC + DC)** - the elex tier (planned).

A content mod built on exlib is laid out its own way; the per-mod project skeleton, where a code-first
definition or a recipe provider lives, and the authoring copies under a family's own `workbench/` are
exmods' own convention, not exlib's to prescribe. exlib's own asset domain is `assets/exlib/`
(`<AssetDomain>` in `src/ExpandedLib.csproj`), laid out the same way any mod's is.

### Where handbook prose lives

A handbook page is authored as HTML in `docs/<mod>/handbook/NN-*.html` and ships as one long string
under a lang key in `assets/<mod>/lang/en.json`. The HTML is the source.

The copy across is not manual. `HandbookSync` (in `ExpandedLib.Testing`) joins the two trees on the `NN-`
ordering prefix, not the file name, so slugs are free to differ; `HandbookParityTests` fails on drift,
`EXLIB_WRITE_HANDBOOK=1` imports HTML → lang, and `EXLIB_EXPORT_HANDBOOK=1` writes back the other way when
the good copy turned out to be the shipped one. Titles stay hand-authored (a few words, no HTML source) and
translations are untouched: the sync changes values, never the key set, so the lang-parity guard still holds.

Line breaks in an authoring file are wrapping, not content - VTML collapses whitespace like HTML, and
so does the sync - so re-wrapping a page is a no-op. Attribute quotes are written `\"` because the files
predate the tooling and were meant to be pasted straight into JSON; the sync un-escapes them.

### How exlib is laid out (ruled 2026-09-05; supersedes the `Blocks/`-vs-top-level rule)

A top-level folder under `src/` is something a modder is doing, and it is one namespace.
Sub-folders organise files; they never add a namespace segment. A consumer needs one `using` per
activity, and a machine mod needs about six in total.

| Folder | Namespace | A modder who is... |
|---|---|---|
| `Registries/` | `ExpandedLib.Registries` | registering blocks, items, behaviours, commands, preferences, recipe profiles; asking about other mods; patching with Harmony |
| `Config/` | `ExpandedLib.Config` | declaring a config class, its ranges, migrations and live editing; syncing it to clients |
| `Definitions/` | `ExpandedLib.Definitions` | writing block, item, recipe and layout definitions in C# |
| `Blocks/` | `ExpandedLib.Blocks` | writing a block entity: declared state, orientation, right-click construction |
| `Migrations/` | `ExpandedLib.Migrations` | renaming or removing codes in old saves, healing lost block entities |
| `Structures/` | `ExpandedLib.Structures` | building a multiblock or megablock |
| `Machines/` | `ExpandedLib.Machines` | building a machine that ticks, with ports, readiness and stations |
| `Networks/` | `ExpandedLib.Networks` | building a connected network: the graph model and the engine-facing nodes together |
| `Catalogues/` | `ExpandedLib.Catalogues` | shipping or extending data catalogues: processes, materials, liquids, storage, their loaders, reports and contributors |
| `Checks/` | `ExpandedLib.Checks` | verifying content in the game or in a test |
| `Helpers/` | `ExpandedLib.Helpers` | everything content-neutral that saves a few lines: orientation, meshes, inventories, units, rendering |
| `Legacy/` | `ExpandedLib.Legacy` | supporting 1.20 and 1.21 from one source tree |
| `industry/<Pack>/` | `ExpandedLib.Industry.<Pack>` | reusing the family's content layer: pipes, molten, mechanical power, metals, heat. Its own project beside `src/`, shipping `exlib.industry.dll` inside the same mod folder |
| `build/` | *(none)* | shipping the MSBuild plumbing itself: `ExpandedLib.props`/`.targets` and `LegacyUsings.cs`, packed under `build/` in the nupkg so `dotnet pack`'s own convention wires them into a consuming project with no manual `<Import>` |

Rules with teeth: a folder that would hold one file is not a folder (the file goes beside its
subject); a sub-folder appears at four files; a type's folder is decided by the activity that reaches
for it first, not by its base class - `BlockNetworkNode` sits in `Networks/` beside `BlockNetwork`
because a modder building a network wants both. The retired rule split each family across a
model folder and a `Blocks/` shell folder and asked consumers to import both; the split is gone.

`exmod.json` at the repo root names this repo's own mods, samples and test projects; `RepoPaths`
and `exmod` read it, falling back to the `mods/<id>` convention where the file is absent.

`testing/` is one namespace, `ExpandedLib.Testing`, laid out the same way: `World/` (the
fake world and its blocks), `Scenes/` (the layout DSL), `Rigs/` (drivers for machines and
structures), `Doubles/` (stand-ins), `Checks/` (the validators), `Repo/` (this repository's own
history and paths). `tests/` mirrors `src/` folder for folder.

`samples/` holds two: `HelloExpanded`, a mod built against exlib end to end (a block, a config
value, a command, two tests), and `HelloModule`, a module shipped as its own mod that `HelloExpanded`
depends on, proving the third-party module shape (see the wiki's Modules page).

### Catalogue registry verbs (ruled 2026-09-06)

Every catalogue registry (`ProcessRouteRegistry`, `ProcessJobRegistry`, `BayOccupancyRegistry`,
`MaterialRoleRegistry`, `MetalRegistry`, `ExLiquids`) reads the same four verbs the same way:
`Register` declares one entry from code, `Contribute` merges a whole parsed file's worth and reports
its clashes, `Load` (always on the loader, never the registry) reads assets and repopulates the
registry, and `Clear` empties it; `Contributors` is the static hook a `Load` re-runs after its own
read, so a `Register` from `Start` survives the clear that precedes every reload. No catalogue
registry declares a public `Add*` or `Load*` member of its own - `CatalogueNamingTests` guards it.

The medium set is data-driven *(live)*: each medium is a `LiquidDef` in exlib's liquid taxonomy
(code, gas/liquid phase, merge priority, boil/condense points). A mod adds a medium by shipping one
JSON entry; the built-in four (Air / Steam / Exhaust / Water) reproduce the old hardcoded behaviour
exactly. The taxonomy is open by design - which further media the family ships is exmods' own plan,
not exlib's.

---

## Everything else lives in exmods

The units, invariants and simulation models a machine mod actually builds against - metal recovery,
the furnace class tree, refractory chemistry, distillation, the per-mod project skeleton and every
design page under `machines/`, `items/`, `deferred/` and the rest of `mechanics/` - are the family's
own, recorded in exmods' `docs/design/`. exlib's own mechanics pages (the seven under
[mechanics/](mechanics/) this repository carries) are cited from `src/`, `industry/` and `testing/`
directly; nothing else here claims them.

