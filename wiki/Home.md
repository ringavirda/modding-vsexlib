# Expanded Library Wiki

**Expanded Library** (`exlib`) is the shared framework mod behind the
_Fallenstar Expanded_ family for [Vintage Story](https://www.vintagestory.at/) -
currently [Iron Industry Expanded](https://github.com/ringavirda/modding-vsexmods/tree/main/mods/iiex) (`iiex`) and
[Steel Industry Expanded](https://github.com/ringavirda/modding-vsexmods/tree/main/mods/siex) (`siex`). It ships no
gameplay content of its own; it gives you batteries-included systems that are tedious to build
from scratch, so you build your own mod directly on it:

- a generic **block-network** graph (auto-orienting node blocks, merge/fracture handling, a
  single manager) - used for gas pipes, steam pipes and molten-metal canals;
- **multiblock structures** with completion monitoring, build-outline projection and an
  invisible filler block that gives mega-blocks real per-cell collision;
- a **production-machine** tick lifecycle base;
- **code-first definitions** - blocks, items and recipes built in C# and injected at load, with
  no `blocktypes/`/`itemtypes/` JSON of your own to hand-write;
- **attribute-driven registration** for blocks/items/entities/behaviours/commands, plus a
  source-generated, versioned, live-editable **config** system;
- a shared `/exmod` (server) and `.exmod` (client) command root, **recipe-cost** profiles,
  per-player **preferences**, **block migrations**, orphaned-BE **healing**, and a grab-bag
  of rotation / particle / sound / inventory **helpers**;
- a **module** system: an assembly that extends the framework or a mod built on it, driven through
  a host mod's lifecycle instead of carrying a `ModSystem` of its own.

This wiki documents both libraries the family publishes for reuse:

- **`exlib`** - the runtime framework mod other mods depend on and call. It ships as the Vintage
  Story mod artifact (`exlib_<version>.zip`) on the [releases page](https://github.com/ringavirda/modding-vsexlib/releases).
- **`exlib.testing`** (`ExpandedLib.Testing`) - a headless xUnit harness that loads the real game
  assemblies and exercises network/block-entity logic under `dotnet test`, no game launch
  required. It's a build-/test-time developer library, not something installed in the game: you
  consume it as the `ExpandedLib.Testing` NuGet package, by referencing the project from source, or
  from the `ExpandedLib.Testing.dll` published with each GitHub release. Its API still moves
  between releases, so pin the version you build against.

## Where to start

- Starting a new mod from scratch? Clone
  [exmod-starter](https://github.com/ringavirda/exmod-starter) - already wired to `exlib`, no
  setup of your own before you build and boot it.
- Adding one more piece to a mod you already have? `exmod scaffold <kind> <Name>` drops in a
  block, structure, network node or test project from this library's own `dotnet new` templates.
- New here? Read **[Getting Started](Getting-Started)** - declare the dependency, set up a
  project reference, and register your first attribute-marked block. It walks
  [`samples/HandMill`](https://github.com/ringavirda/modding-vsexlib/tree/main/samples/HandMill) and
  [`samples/Grains`](https://github.com/ringavirda/modding-vsexlib/tree/main/samples/Grains),
  a buildable, bootable, tested mod and the module it reads from, using the convenience layer end to
  end - read them alongside the page.
- Building a machine with a structure and a power line? **[First Machine](First-Machine)** finishes
  the walk Getting Started starts.
- Wondering when exlib does what during world load, and what's safe to call where? **[Lifecycle](Lifecycle)**.
- Wondering which types are the supported contract and which are internal plumbing? **[Supported
  API](Supported-API)**.
- Shipping a block, item or recipe from C# instead of JSON? **[Code-First Definitions](Code-First-Definitions)**.
- Building plumbing/wiring of any kind? **[Block Networks](Block-Networks)**.
- Building a furnace, boiler or other big machine? **[Multiblock Structures](Multiblock-Structures)**
  and **[Production Machines](Production-Machines)**.
- Writing a block entity and want its fields saved for free? **[Block Entities](Block-Entities)**.
- Adding to a process our mods already ship - a roll set, a mold, a crop, a die? **[Extending
  Processes](Extending-Processes)**. Our machines name no product; you declare one.
- Want config, commands or recipe tuning? **[Registries](Registries)**,
  **[Config System](Config-System)**, **[Commands](Commands)**, **[Recipe Costs](Recipe-Costs)**.
- Want the content guards (dangling codes, missing lang, ...) to run against your own source, or on
  demand rather than only at load? **[Checks](Checks)**.
- Writing tests? **[Testing Harness](Testing-Harness)** and **[Testing API Reference](Testing-API-Reference)**.

## A note on accuracy

These pages document the public surface a third-party mod consumes. Signatures are taken from
the source in this repository; when in doubt, the code in `src/` and
`src/ExpandedLib.Testing/` is the source of truth. Game-version differences (1.20 / 1.21 / 1.22)
are handled by the `Legacy/` shim and noted where they affect you.
