# Expanded Library

**Expanded Library** (`exlib`) is a framework for building Vintage Story mods. It ships no
gameplay content of its own. It carries the systems that every technical mod ends up writing for
itself, so that a new mod can start at the interesting part: pipes and networks, big machines that
span many blocks, machines that tick and produce, blocks that save their own state, config that
players can edit, and a way to test all of it without launching the game.

I wrote it for my own mods, [Iron Industry Expanded](https://github.com/ringavirda/modding-vsexmods/tree/main/mods/iiex)
and [Steel Industry Expanded](https://github.com/ringavirda/modding-vsexmods/tree/main/mods/siex),
both still in development, and publish it so that anyone can build on the same base. Four small
sample mods, linked below, show each system in use.

## Why a framework

Vintage Story's modding API is a wide surface with little documentation, so most modders learn it
by reading other mods. While designing exlib I read the reference mods of this genre, and the same
pipe-network logic appeared in five of them, each written differently and each with its own bugs
around chunk edges, merging and reloads. Multiblock machines, saved block-entity state and
live-editable config repeated the same pattern. exlib is that shared code written once, tested,
and kept working across game versions, so you do not write it a sixth time.

## What you get

Each system below solves one problem you would otherwise meet on your own. The linked page teaches
it from the first line, on the assumption that you have not read this code before.

- **[Code-first definitions](Code-First-Definitions).** Hand-written `blocktypes/` JSON drifts
  from the C# that reads it, and a typo only shows up at world load. With exlib you describe a
  block, item or recipe in C#, with variants, shapes and behaviours as method calls, and the JSON
  is generated and injected at load. Your test suite can check every definition before the game
  ever sees it.
- **[Attribute registration](Registries).** The API wants a `RegisterBlockClass` call for every
  class, in a list you maintain by hand. exlib registers every class you tag with an attribute,
  plus commands and client preferences, from one empty mod system.
- **[Block networks](Block-Networks).** Pipes, wires and canals need a graph: which blocks join,
  what happens when a run is cut in two or two runs meet, how a node knows its orientation, what
  a chunk unload does to the network. exlib keeps one manager for all of that and your node block
  only says what it connects to and what flows.
- **[Multiblock structures](Multiblock-Structures).** A furnace or boiler that spans a dozen
  blocks needs its cells checked, its outline shown while the player builds it, and collision on
  every cell, not just the one that carries the block entity. exlib gives a structure its layout,
  its completion monitoring, a projected build outline, and invisible filler blocks with real
  collision that can also host behaviours such as a power port.
- **[Production machines](Production-Machines).** A machine that works over time needs a server
  tick, a gate for when it may run, and a way to catch up after the player was away. exlib
  provides the lifecycle; you write the work.
- **[Block entities that save themselves](Block-Entities).** Every field a block entity keeps
  across a reload needs writing and reading by hand, with a key spelled twice. exlib saves any
  field you mark `[Persist]` and restores it, on both sides.
- **[Construction](Construction).** The game's right-click construction lets a player raise a
  machine in stages from materials. exlib wraps it with staged rendering and salvage drops.
- **[Config](Config-System).** A config class becomes a typed accessor with generated code,
  validated against declared ranges, editable live from a command, and migrated when you rename a
  field.
- **[Commands](Commands), [recipe costs](Recipe-Costs).** One `/exmod` root for every mod's
  sub-commands, and a way to offer cheap and normal recipe levels without duplicating recipes.
- **[Migrations and healing](Migrations-and-Healing).** Renaming or removing a block breaks every
  save that holds it. exlib rewrites old codes on load and heals block entities left behind.
- **[Modules](Modules).** An assembly that extends the framework, or a mod built on it, can join
  exlib's lifecycle without a mod system of its own.
- **[Checks](Checks).** Content guards that catch a dangling code, a missing lang key or a
  malformed catalogue at load, in a test, or on demand from a command.
- **[Helpers and renderers](Helpers-and-Renderers).** Rotation math, particles, sounds,
  inventory counting, content gating and the small renderers a machine needs.
- **[Testing harness](Testing-Harness).** Load the real game assemblies, place blocks in a test
  world, tick them and assert, all under `dotnet test`.

The industry layer, `exlib.industry`, adds the systems the family mods are made of: gas and liquid
[pipe networks](Block-Networks), molten metal, mechanical-power ports for multiblocks, and a heat
balance. [Extending Processes](Extending-Processes) shows how another mod adds its own stock, dies,
molds and roll sets to those machines from a JSON file, without touching this code.

## Where to start

1. **[Installing](Installing)**: what to download, which package does what, and the four edits that
   put exlib into a project you already have.
2. **[Getting Started](Getting-Started)**: a first block with saved state, a config value and a
   test, read alongside the `TwinTubBlower` sample.
3. **[First Machine](First-Machine)**: a structure with a footprint and a power line, read alongside
   the `BurdenMaker` sample.
4. **[Lifecycle](Lifecycle)**: what exlib does at each phase of world load, and what is safe to call
   where.
5. **[Supported API](Supported-API)**: which types are the supported contract.

The samples are complete mods, each buildable, bootable and tested, in the
[exlib repository](https://github.com/ringavirda/modding-vsexlib/tree/main/samples):
[TwinTubBlower](https://github.com/ringavirda/modding-vsexlib/tree/main/samples/TwinTubBlower)
is a machine with a power port that feeds a pipe network,
[BurdenMaker](https://github.com/ringavirda/modding-vsexlib/tree/main/samples/BurdenMaker) a stock
house built in stages,
[PlatedPipes](https://github.com/ringavirda/modding-vsexlib/tree/main/samples/PlatedPipes) a pipe
tier, and [SmokeStack](https://github.com/ringavirda/modding-vsexlib/tree/main/samples/SmokeStack)
a multiblock chimney. The [starter repository](https://github.com/ringavirda/exmod-starter) ships
all four, wired and ready to boot.

## Accuracy

These pages describe the public surface a third-party mod uses. Signatures follow the
[source on GitHub](https://github.com/ringavirda/modding-vsexlib); where a page and the code
disagree, the code is right and the page is the bug, and the edit link at the foot of every page
leads to its text. Game versions 1.20, 1.21 and 1.22 are all supported; where they differ, the
page says so.
