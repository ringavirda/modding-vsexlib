# Expanded Library (`exlib`)

A framework for building Vintage Story mods: block networks (pipes, wires, canals), multiblock
structures with completion monitoring and a build outline, a production-machine tick lifecycle,
code-first block/item/recipe definitions, attribute-driven registration, a source-generated live
config system, a headless xUnit test harness that needs no game launch, and a module system for
extending the framework or another mod without carrying a `ModSystem` of your own. It ships no
gameplay content of its own: build your own mod on these networks, multiblocks and attribute-driven
registration, starting from [exmod-starter](https://github.com/ringavirda/exmod-starter),
scaffolding new pieces with `exmod scaffold`, and reading the
[wiki](https://github.com/ringavirda/modding-vsexlib/wiki) alongside your own code. It is also
the shared framework behind the
*Expanded* family
([Iron Industry Expanded](https://github.com/ringavirda/modding-vsexmods/tree/main/mods/iiex),
[Steel Industry Expanded](https://github.com/ringavirda/modding-vsexmods/tree/main/mods/siex)).

## What it provides

- **Block networks** (`Networks/`) - a generic connected-graph framework: the model
  (`BlockNetwork` + subclasses, the `I*Node`/`I*Connector` contracts) and the
  engine-facing shell (`BlockNetworkNode`, node block entities, the
  `BlockNetworkModSystem` manager) share the one folder and namespace. `iiex`
  registers both the "pipe" and the "molten" network on it.
- **Multiblock structures** (`Structures/`) - completion monitoring,
  build-outline projection (ctrl+shift+rmb), crash-safe incomplete-part highlighting,
  and the shared invisible `structurefiller` block that gives mega-block machines
  per-cell collision.
- **Production machines** (`Machines/`) - `BlockEntityProductionMachine` base
  (the tick lifecycle + operational gate) and `MachinePorts` helpers, shared by
  engines, furnaces, converters and sub-machines.
- **Block-entity healing** (`Migrations/`) - recreates a block entity that was
  lost while its block survived (a load failure or desync), automatically on chunk
  load and via `/exmod heal`.
- **Registries** (`Registries/`) - attribute-driven registration:
  - `Entities/` - `[BlockRegister]` / `[ItemRegister]` / `[BlockEntityRegister]` /
    `[BlockBehaviorRegister]` / `[BlockEntityBehaviorRegister]` /
    `[CollectibleBehaviorRegister]` for blocks, items, entities and behaviors.
  - `Commands/` - `[CommandRegister]` / `[SubCommandRegister]` building the shared
    `/exmod` (server) and `.exmod` (client) command root.
  - `Recipes/` - per-mod recipe-cost profiles switchable with `/exmod recipes`.
  - `Preferences/` - per-player display-preference store.
- **Config** (`Config/`) - generic versioned config store (`ExConfigRegister`) with
  source-generated value accessors, range-gated values, migrations and live
  `/exmod config` editing.
- **Block migrations** (`Migrations/`) - rewrites renamed/re-variantted block
  codes (and matching item stacks) in old saves as chunks load.
- **Data catalogues** (`Catalogues/`) - shared process, material-role, liquid and
  storage-occupancy catalogues, their loaders and code contributors, plus the load
  report every loader hands back.
- **Shared helpers** (`Helpers/`) - `ExOrientation` (rotation math),
  `ExParticles` / `ExSounds` (effect catalogues), `ExCreativeTabs`,
  `ExInventory` / `ExItems`, `ExBlockNames` (variant display names), `ExContentGate`
  (hide-from-creative/handbook + recipe removal), the shared `SurfaceRenderer`
  (`Rendering/`) and the unit-display system (`Measure/`).
- **Legacy support** (`Legacy/`) - shims/polyfills that let the family build and run
  against Vintage Story 1.21 and 1.20 alongside 1.22.
- **Modules** (`Registries/ExModuleAttribute.cs`, `ExModules`, `ExModuleHost`) - an assembly that
  extends the framework or a mod built on it without carrying a `ModSystem` of its own, driven
  through a host mod's lifecycle instead: `ExpandedLib.Industry` is the first, shipped inside this
  mod's own folder; a third party's own mod can be one too, depending on exlib. See the wiki's
  [Modules](https://github.com/ringavirda/modding-vsexlib/wiki/Modules) page.

## Start from the sample

Starting a new mod? Clone [exmod-starter](https://github.com/ringavirda/exmod-starter) - a fresh
project already wired to `exlib`, ready to build and boot with no setup of your own. Adding a
block, structure or network node to a mod you already have? `exmod scaffold <kind> <Name>` drops
one in from the `dotnet new` templates this library ships.

Working inside this repo, or want to read a sample before you write anything? `samples/TwinTubBlower`
is a third-party mod written against this library end to end: a mega-block reserving its own footprint
for a mechanical-power port, and a gas-pipe node that produces into the network it stands in.
`samples/BurdenMaker` is a second, unrelated mod alongside it: a 9-cell mega-block stock house raised
through a five-stage right-click construction and rendered through a permanent pose animation, with a
full headless test suite each. Both build and boot like any other mod here (`dotnet build
ExpandedLib.sln`, `exmod smoke`) - read them alongside the wiki rather than typing their snippets by
hand.

## What is supported

`ExpandedLib.*` outside `ExpandedLib.Industry` is the supported contract: every public type
there is listed on the wiki's [Supported API](https://github.com/ringavirda/modding-vsexlib/wiki/Supported-API) page, and a public type
missing from that list has been hidden from IntelliSense with `[EditorBrowsable(Never)]` because
the engine has to see it, not because a mod is meant to call it. `ExpandedLib.Industry` is also
public, but it is the family's own content layer and changes without notice.

## Layout

```
src/ExpandedLib/            the mod: ExpandedLib.csproj, modinfo.json, modicon.png, assets/, the framework sources
src/ExpandedLib.Industry/   the family's content layer, packaged alongside the framework
src/ExpandedLib.Testing/    the headless xUnit harness
src/ExpandedLib.Generators/ the config/lang source generators, referenced as an analyzer
tests/ExpandedLib.Tests/    the suite this repo's own gate runs
build/                      the MSBuild plumbing the package ships (GamePath resolution, provisioning, asset globs)
samples/                    TwinTubBlower and BurdenMaker, third-party mods written against this library
templates/                  the `dotnet new` templates a consumer installs (block, item, recipe, config, tests, ...)
docs/                       design pages for a contributor working on this repo's own mechanics
wiki/                       the GitHub wiki source, guarded by tests so it cannot drift from the code
scripts/                    the exmod launchers this repo checks in
dist/                       release output (zips, NuGet packages) - not checked in
```

## Packages

The mod ships as one download - one modinfo, one folder, `exlib.dll` and `exlib.industry.dll` - and
the module system means that is not a hard limit of two: any assembly, inside this folder or
shipped as its own mod, can declare `[assembly: ExModule]` and join exlib's lifecycle without a
`ModSystem` of its own. It ships as four NuGet packages: three a project references at compile time, plus the
templates exmod scaffold installs for you:

| Package | What it is |
| --- | --- |
| `ExpandedLib` | the framework: `exlib.dll`, the config/lang source generators, and the build/ plumbing (`GamePath` resolution, provisioning, asset globs) - a consumer needs no props of its own beyond a `TargetFramework` and an `AssetDomain` |
| `ExpandedLib.Industry` | this family's content layer: `exlib.industry.dll`, beside it in the same mod folder |
| `ExpandedLib.Testing` | the headless xUnit harness, for a test project rather than a mod |
| `ExpandedLib.Templates` | the `dotnet new` templates `exmod scaffold` installs for you |

`exlib-verify`, the JSON-only asset checker, is a .NET tool built from
[extools](https://github.com/ringavirda/modding-vsextools) rather than a package this repository ships - see
the wiki's [Checks](https://github.com/ringavirda/modding-vsexlib/wiki/Checks) page.

The three library packages are built for the current Vintage Story version only. The mod zips on the GitHub releases
page cover the older versions; the packages do not, because a mod targeting an older version
builds against a different .NET and a different game API.

None of them carries the game's own assemblies: a consuming project references
`VintagestoryAPI.dll` and friends from its own install, the same way any Vintage Story mod does.
At runtime the player installs this mod, and the game loads it like any other dependency.

## Building

A project resolves `exlib` one of two ways, switched on `$(ExlibRoot)`. Source mode - inside this
repo, or a workspace checkout with `ExlibRoot` pointing at it - builds against the checked-out
`ExpandedLib.csproj`/`ExpandedLib.Generators.csproj` directly, so exlib's own change history builds
against itself with no release round-trip. Package mode - `-p:ExlibRoot=` (empty), the default
outside this repo - restores the `ExpandedLib` NuGet package instead, which carries `exlib.dll`,
its XML docs, the generators packed as analyzers under `analyzers/dotnet/cs/`, and the `build/`
plumbing (`ExpandedLib.props`/`.targets`: `GamePath` resolution, provisioning, asset globs, version
stamping) that NuGet imports into the consuming project automatically. Central package versions for
`ExpandedLib`, `ExpandedLib.Industry` and `ExpandedLib.Testing` live once, in `Directory.Packages.props`
at the repo root.

```sh
dotnet build src/ExpandedLib/ExpandedLib.csproj                   # the framework
dotnet build src/ExpandedLib.Industry/ExpandedLib.Industry.csproj # the family layer
```

Every repo task goes through `scripts/exmod.sh` (`scripts/exmod.ps1` on Windows), a launcher that
forwards to the CLI in [extools](https://github.com/ringavirda/modding-vsextools), a sibling repository
checked out beside this one; see [CONTRIBUTING.md](https://github.com/ringavirda/modding-vsexlib/blob/main/CONTRIBUTING.md) for the command list and how
the launcher finds it.

## The workspace and the family

This repository stands alone - it clones, builds and tests with nothing else present. The family
mods that consume it, [Iron Industry Expanded and Steel Industry Expanded](https://github.com/ringavirda/modding-vsexmods),
live in their own repository and reference `ExpandedLib` as a NuGet package by default. A workspace
that checks out both repositories side by side, with a `Directory.Build.props` above them setting
`ExlibRoot`, switches the family's build onto this checkout's source instead - the daily loop for
changing exlib and the family together with no release round-trip.
