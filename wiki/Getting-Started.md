# Getting Started

This page builds a first mod on exlib from end to end: a block declared in C# instead of JSON,
state that survives a reload, a config value the player can edit while the server runs, a test that
checks the machine without launching the game, and the tool that builds and boots the result.

The mod is a real one. `samples/TwinTubBlower` in this repository is a complete, buildable,
bootable mod, and every snippet below is copied from it, so it compiles. Read the page beside the
sample: each step explains what the code does and why you would want it.

Nothing here assumes you have written a Vintage Story mod before. Where the game's own API comes up
(a mod system, a block entity, a variant group) it is explained the first time it appears.

## 1. Install exlib

exlib is two things at once: a mod the player installs into the game, and a set of packages your
project compiles against. [Installing](Installing) covers both, and the four edits that put them
into a project you already have. Two of those edits are worth repeating here, because getting
either wrong fails quietly rather than loudly.

Reference the `ExpandedLib` package with `ExcludeAssets="runtime"`. Without it your build copies
`exlib.dll` into your own mod's output, and the game refuses any mod folder carrying a second
assembly with mod systems in it. Nothing crashes; your mod is simply absent from the loaded list.

Declare the exlib version you compiled against in `modinfo.json`, the manifest every mod ships. The
sample's dependencies read:

```json
"dependencies": {
  "game": "1.22.0",
  "exlib": "0.8.2"
}
```

That number is a floor, not a pin: the game accepts any installed exlib at or above it. Leave the
floor at an old release and a player can satisfy it with an exlib that predates the method you
call, and the failure arrives at world load as a missing member rather than as a clear dependency
error.

## 2. Register your content

The game does not find your classes on its own. A `ModSystem` is the class the game constructs
when it loads your mod and calls at each phase of startup, and in a plain mod it is where you hand
the engine every block, block entity, item and behaviour class by name, one
`api.RegisterBlockClass(...)` call at a time. That list is yours to keep in step with your code, and
a class you forget does not error: the game falls back to the plain `Block` and your logic never
runs.

exlib is **attribute-driven** instead. You tag each class with what it is, and a `ModSystem`
deriving `ExModSystem` finds them all by reflection at the phase each one belongs to, with no calls
of its own to write:

```csharp
using ExpandedLib.Registries;
using Vintagestory.API.Common;

[BlockRegister]                       // registers as "yourmod.BlockMachine"
public class BlockMachine : Block { }

[BlockEntityRegister]                 // registers as "yourmod.BlockEntityMachine" (+ short aliases)
public class BlockEntityMachine : BlockEntity { }

public class YourModSystem : ExModSystem { }
```

That single, empty class covers more than blocks. It also loads your mod's config, and registers
any `[CommandRegister]`/`[SubCommandRegister]` class on each side and any `[PreferenceRegister]`
class on the client - see **[Commands](Commands)** for adding one. If you need something to run in a
particular order relative to registration, or you are not deriving `ModSystem` at all,
**[Registries](Registries)** documents the explicit `RegisterAll` calls this class makes for you and
the one ordering rule they carry.

Your block's own data still has to come from somewhere. Read a block's JSON `attributes` with
`Attributes["..."].AsFloat()` as usual, or skip the JSON entirely and declare the block code-first
through `IExBlockDefProvider`, which is what step 3 does. See **[Source
Generators](Source-Generators)** for the two generators exlib ships, typed config accessors and
typed lang keys: each turns a string you could mistype into a name the compiler checks.

### If other mods will name your types

Every registered class is keyed under an asset domain, the prefix that keeps codes from colliding
between mods: vanilla content is `game:...`, yours is `yourmod:...`. Add an assembly-level marker
naming yours, so a lookup arriving from another assembly can resolve your classes:

```csharp
[assembly: ExDomain("yourmod")]
```

It must equal your mod id. `EntityRegistry.RegisterAll` does not need it - that path keys off the mod
id directly - so a mod nobody else extends works without it. It matters when *another* mod names one
of your types, for example through `ExBlockDef.Class<T>()`: the key is resolved from the type's own
assembly, and without the marker that lookup produces a key nobody registered. The block half of that
failure is not logged, which is why the attribute is worth declaring up front.

## 3. Your first block

The game builds every block from a JSON file under `assets/<domain>/blocktypes/`, and the C# class
that file names supplies only the behaviour. The two drift apart. Rename a variant, mistype a shape
path or drop a behaviour in the JSON and the C# still compiles; the mismatch surfaces at world load,
or later, as a block that is quietly not what the code expects.

A code-first block closes that gap by deleting the JSON. The class implements `IExBlockDefProvider`
and carries `[BlockRegister]`, and its `Definitions` method returns what the file would have held,
assembled by the `ExBlockDef` builder and injected into the object loader in memory at load. There
is no `blocktypes/furnace/twintubblower.json` anywhere in the mod's `assets/` folder. The definition
sits beside the class it configures, and a test can assert on it before the game ever sees it.

The blower is a mechanically driven pair of bellows: an axle from the game's mechanical-power
network turns it, and it pushes air into the gas-pipe network it stands in. One call below is
elided and one snippet is a labelled alternative; everything else is verbatim from the sample:

```csharp
[BlockRegister]
public partial class BlockTwinTubMPBlower
  : BlockPipe,
    IExBlockDefProvider,
    IFillerHost {
  private static readonly FillerBehaviorSpec MpPortWest =
    FillerBehaviorSpec.Of<BEBehaviorMPFillerPort>(
    "west",
    new { through = false }
  );

  public static new IEnumerable<ExBlockDef> Definitions(string domain) =>
    [
      ExBlockDef
        .Create(domain, "blower", "furnace/twintubblower")
        .Class<BlockTwinTubMPBlower>()
        .EntityClass<BlockEntityTwinTubMPBlower>()
        .Behavior("MultiblockStructure")
        .Material(EnumBlockMaterial.Ceramic)
        .MaxStackSize(1)
        .VariantGroup("type", "twintubblower")
        .VariantGroup("orientation", "n", "e", "s", "w")
        .NetworkOriented()
        .ShapeByType("*-n", "twintubblower:furnace/twintubmpblower", rotateY: 0)
        .ShapeByType("*-e", "twintubblower:furnace/twintubmpblower", rotateY: 90)
        .ShapeByType("*-s", "twintubblower:furnace/twintubmpblower", rotateY: 180)
        .ShapeByType("*-w", "twintubblower:furnace/twintubmpblower", rotateY: 270)
        .CreativeCommon("*-n")
        .FillerOffsets(/* the footprint hosting the MP port - see First Machine */)
        .SolidNonOpaque(),
    ];
}
```

Read that chain as a description of the block rather than a sequence of calls. `Class` and
`EntityClass` name the two C# classes above. A variant group expands one definition into several
real blocks, one per value, so the `orientation` group turns this definition into four:
`blower-twintubblower-n` through `blower-twintubblower-w`, each handed the same shape spun to match
by `ShapeByType`. `VariantGroup("type", "twintubblower")` carries a single state on purpose; it is
the family's discriminator, the same shape the framework uses whenever a second block joins a code
under `blower`. `NetworkOriented` gives the choice of which of the four to place to the pipe
network, so a blower lines itself up with the run rather than with wherever the player happened to
be looking. `CreativeCommon` puts one of them in the creative inventory, so the other three do not
clutter it.

`FillerOffsets` is the call with a structure behind it. The blower occupies a 1x2x3 box, and the
call reserves the cells the block itself does not stand in with invisible fillers that still carry
real collision. One of those cells hosts the mechanical-power port the axle couples to, because the
block proper, two cells away, cannot accept power at that face. [First Machine](First-Machine)
walks that call and the block's own `IFillerHost` placement triad in full.

`ExModSystem` registers that block, its block entity, every other `[BlockRegister]`,
`[BlockEntityRegister]` and `IExBlockDefProvider` in the assembly, and loads `TwinTubBlowerValues`,
with nothing to write:

```csharp
public class TwinTubBlowerModSystem : ExModSystem { }
```

The explicit form behind it, for a mod system that needs a different order, or that must run
something of its own between loading the config and registering the classes:

```csharp
public class TwinTubBlowerModSystem : ModSystem {
  public override void Start(ICoreAPI api) {
    TwinTubBlowerValues.Load(api);
    EntityRegistry.RegisterAll(api, Mod, GetType().Assembly);
  }
}
```

## 4. State and a drive that survives a reload

A `Block` is one shared object for every copy of that block in the world, so it can hold nothing
that belongs to a single placement. What one blower knows, such as how fast its axle is turning,
lives in a block entity: the object the game attaches to one position, and ticks, saves and loads
with the chunk around it.

Saving that state is normally hand work. You override `ToTreeAttributes` and `FromTreeAttributes`
and copy each field into and out of a tree attribute, the nested key-and-value structure the game
serializes a block entity into, spelling the key once in each direction. Forget the reading half or
mistype the key in it and nothing complains: the field just comes back at its default after every
reload.

The blower's block entity samples its driving axle once a second and pushes air into its own pipe
network, scaled by how fast that axle is turning:

```csharp
[BlockEntityRegister]
public class BlockEntityTwinTubMPBlower : BlockEntityPipe {
  private static readonly Vec3i MpPortCell = new(0, 1, 0);

  // Written server-side and serialized because the client cannot read the port behaviour's live
  // state and needs it for the HUD.
  [Persist("blowerSpeed")]
  private float _lastSpeed;

  public override void Initialize(ICoreAPI api) {
    base.Initialize(api);
    if (api.Side == EnumAppSide.Server)
      RegisterGameTickListener(OnBlowTick, 1000);
  }

  private void OnBlowTick(float dt) {
    float speed = PortSpeed();
    if (speed != _lastSpeed) {
      _lastSpeed = speed;
      MarkDirty();
    }
    ProduceAir(speed, dt);
  }

  public float ProduceAir(float speed, float dt) {
    float fraction = SpeedFraction(speed);
    if (fraction <= 0f || dt <= 0f)
      return 0f;
    if (NetworkSystem?.GetNetworkAt(Pos) is not PipeNetwork net)
      return 0f;

    float before = net.State?.Volume ?? 0f;
    net.TryProduceGas(
      TwinTubBlowerValues.TwinTubBlowerOutputPerSecond * fraction * dt,
      AmbientTemperature,
      "Air",
      Api.World.BlockAccessor,
      maxOutputPressure: TwinTubBlowerValues.TwinTubBlowerMaxPressure
    );
    return GameMath.Max(0f, (net.State?.Volume ?? 0f) - before);
  }

  public static float SpeedFraction(float speed) {
    float min = TwinTubBlowerValues.TwinTubBlowerMinSpeed;
    float max = TwinTubBlowerValues.TwinTubBlowerMaxSpeed;
    if (speed <= min)
      return 0f;
    if (max <= min)
      return 1f;
    return GameMath.Clamp((speed - min) / (max - min), 0f, 1f);
  }
}
```

`[Persist("blowerSpeed")]` is the whole save and load story for `_lastSpeed`: no
`ToTreeAttributes`/`FromTreeAttributes` override, no key to spell twice, and the field comes back on
both sides. The rest of the class is ordinary game API. `RegisterGameTickListener` asks the game to
call `OnBlowTick` every 1000 milliseconds, and the side check keeps that on the server, where
production belongs. `MarkDirty` tells the game the entity's saved state changed, which both stores
it and sends it to the clients watching the block; that send is the reason the speed is persisted at
all, as the comment on the field says.

`PortSpeed` (the axle lookup itself, through the filler cell the port is hosted on) and
`AmbientTemperature` are omitted here - see
[First Machine](First-Machine) for the footprint that cell sits in. See [Helpers &
Renderers](Helpers-and-Renderers) "Declared state" for the full surface `[Persist]` covers.

## 5. A config value

The numbers that decide how a machine plays - its throughput, its pressure ceiling, the speed band
it works over - should not be frozen in the code. Baked into C#, rebalancing costs a rebuild and a
release, and a server owner who finds your blower too strong has nothing to turn. Read by hand from
a JSON file, every value is a string key and an unchecked cast.

In exlib a config is a plain class: you declare each property with its default and its legal range,
and a source generator emits a typed accessor beside it, one read-only property per config property,
named after it. These are the blower's tunables, generated into that `TwinTubBlowerValues` accessor:

```csharp
[ExConfigRegister("twintubblower.json", "twintubblower", Manageable = true)]
public class TwinTubBlowerConfig : IExVersionedConfig {
  public string? ConfigVersion { get; set; }

  [ExConfigRange(1f, 1000f)]
  public float TwinTubBlowerOutputPerSecond { get; set; } = 45f;

  [ExConfigRange(0.1f, 50f)]
  public float TwinTubBlowerMaxPressure { get; set; } = 2.2f;

  [ExConfigRange(0f, 50f)]
  public float TwinTubBlowerMinSpeed { get; set; } = 0.5f;

  [ExConfigRange(0.1f, 50f)]
  public float TwinTubBlowerMaxSpeed { get; set; } = 1.5f;
}
```

`[ExConfigRegister]` names the file the values are stored in and the section of it this mod owns.
`[ExConfigRange]` is the bound the value is held to in both directions: a file edited out of range
is reset to the coded default at load, and a live edit outside it is refused. `IExVersionedConfig`
adds the one property `ConfigVersion`, recording the mod version the file was last written under, so
a later release can reset a tunable whose meaning changed instead of reading an old number as if it
still meant the same thing.

`Manageable = true` is what puts it on the generic switch: `/exmod config twintubblower
twintubbloweroutputpersecond 60` reads or writes it live, validated against the `[ExConfigRange]`
bound, with no code of this mod's own involved. `TwinTubBlowerValues.TwinTubBlowerMaxSpeed` (read
live in `SpeedFraction` above) is generated from the property name. See [Config
System](Config-System) and [Commands](Commands) for adding a `/exmod` sub-command of your own.

## 6. Test it

A mod is awkward to test because its logic usually lives inside a block entity that needs a running
world to exist at all. Two things answer that, and the first is free: keep the decisions that are
pure arithmetic out of the instance, as `SpeedFraction` is above, and a test can call them straight.
`samples/TwinTubBlower/tests` drives the bellows headlessly, with no game launch:

```csharp
public class TwinTubBlowerTests {
  [Theory]
  [InlineData(0f, 0f)]
  [InlineData(0.5f, 0f)] // at the minimum the bellows barely move
  [InlineData(1.0f, 0.5f)] // halfway between min and max
  [InlineData(1.5f, 1f)] // rated speed
  [InlineData(4f, 1f)] // over-driven: capped, never more than rated
  public void Output_scales_linearly_between_the_min_and_max_axle_speed(
    float speed,
    float expected
  ) {
    Assert.Equal(expected, BlockEntityTwinTubMPBlower.SpeedFraction(speed), 3);
  }
}
```

Each row is one point of the output curve, both ends of the clamp included, so a later change to
the speed band cannot quietly turn the blower into an on-off switch. Run it with
`dotnet test samples/TwinTubBlower/tests/TwinTubBlower.Tests.csproj`, or
`exmod test latest -Filter TwinTubBlower`.

The second answer is the [Testing Harness](Testing-Harness), for behaviour that genuinely needs a
world: it loads the real game assemblies, gives you a world to place blocks in, and lets you tick a
block entity and assert on what it did.

## 7. Boot it

Tests prove your arithmetic. They do not prove the game will load your mod. A definition the object
loader rejects, a block code nothing registers, a name with no translation: none of that shows up
under `dotnet test`, and all of it shows up at world load. The cheap way to catch it is to boot the
real server and read the log.

`exmod smoke -Mods src/ExpandedLib/bin/Debug/Mods/mod,samples/TwinTubBlower/src/bin/Debug/Mods/mod`
launches the real dedicated server against the built mods, runs the content checks and
`/exmod verify`, and fails on a boot timeout or an `[Error]`/`[Fatal]` log line - the same lane this
repo's CI runs on every mod, now covering the one you just read:

```
[exlib] modules hosted by exlib: industry
[exlib] Injected 3 code-first block definition(s).
[exlib] Injected 1 code-first item definition(s).
[exlib] Injected 2 code-first recipe file(s).
[exlib] check DefinitionCatalogue (twintubblower): 0 error(s)
[exlib] check LateDefinition (twintubblower): 0 error(s)
[exlib] check MultiblockCodes (twintubblower): 0 error(s)
[exlib] check RecipeCodes (twintubblower): 0 error(s)
[exlib] check LangCoverage (twintubblower): 0 error(s)
[exlib] check NetworkNodeContract (twintubblower): 0 error(s)
[exlib] check PinnedNetworkNodes (twintubblower): 0 error(s)
[exlib] check CodePrefixCollision (twintubblower): 0 error(s)
```

Each `check` line is one of the content guards in [Checks](Checks) run over a domain, and zero
errors on every one of them is what a clean boot looks like. The counts above are process-global,
not per-domain, and were taken booting both samples together:
the three blocks are the blower, the burden maker (see [First Machine](First-Machine)) and exlib's
own structure filler (`BlockStructureFiller`), injected once for every mod that places a filled
megastructure; the one item is the burden maker's own `burden`. See [First Machine](First-Machine)
for the rest of the footprint: the filler cells, the mechanical-power port, and a second, more
involved machine built the same way.

## 8. exmod in your repo

Every step above needs the same things arranged first: a game install to compile against, a
dedicated server to boot, the exlib zip beside your own mod, your assets copied into the output. The
whole toolchain - build, test, smoke - is one script rather than a set of raw `dotnet` commands you
assemble yourself. Copy `scripts/exmod.sh` and `scripts/exmod.ps1` from this repo into your
own (the launcher finds `pwsh`, the dispatcher does the rest), and add an `exmod.json` at your
repo's root naming your mod:

```json
{
  "solution": "YourMod.sln",
  "mods": {
    "yourmod": { "path": "." }
  }
}
```

From there the commands come in the order you need them. `exmod provision game` fetches the
dedicated-server archive into `.game/`, so building and testing headlessly needs no purchase on that
machine. `exmod build` compiles against it. `exmod test` runs your test project the way `dotnet
test` does, resolved from the manifest rather than named on the command line. `exmod smoke` boots
the real server with your built mod and fails on a boot timeout or an `[Error]`/`[Fatal]` log line.
Before that boot, `exmod provision mods` reads `exlib` out of your `modinfo.json`'s `dependencies`
and fetches it for the smoke to load alongside your own mod: a workspace sibling checkout of exlib
when there is one, otherwise a published release.

`exmod scaffold <kind> <Name>` (alias `g`) drops a compiling, tested starting point into your mod
from the templates exlib ships as `ExpandedLib.Templates`: a block, item, recipe, megablock,
multiblock, node, blockbehavior, entitybehavior, config, migration or command. `exmod scaffold block
Widget` lands a block and its block entity in `src/Blocks` and `src/BlockEntities` with a test in
`tests/`, and merges the lang keys the generated code reads into `assets/<id>/lang/en.json`. A
block, item, recipe, megablock, multiblock or node is a code-first def: if your mod's test project
golden-checks the whole set (see **[Code-First Definitions](Code-First-Definitions)**, "Goldens"), the
new def's missing golden turns that check red until you bless it once with `EXLIB_WRITE_GOLDENS=1`.

## 9. Pick the system you need

Each row below is a job. The page behind it teaches the system that does it, from the first line, on
the assumption that you have not read the code.

| You want to... | Read |
| --- | --- |
| Build a machine with a structure and a power line | [First Machine](First-Machine) |
| Build pipes / wires / canals (anything that connects into a network) | [Block Networks](Block-Networks) |
| Build a multi-cell machine (furnace, boiler) with completion + build outline | [Multiblock Structures](Multiblock-Structures) |
| Run periodic server-side work on a block entity | [Production Machines](Production-Machines) |
| Use the vanilla right-click-construction flow with salvage drops | [Construction (RCC)](Construction) |
| Ship gameplay tunables players can edit live | [Config System](Config-System) |
| Add a `/exmod` (server) or `.exmod` (client) sub-command | [Commands](Commands) |
| Offer cheap/normal recipe-cost levels | [Recipe Costs](Recipe-Costs) |
| Rotation math, particles, sounds, inventory counting, content gating | [Helpers & Renderers](Helpers-and-Renderers) |
| Rename/remove blocks in old saves without orphaning them | [Migrations & Healing](Migrations-and-Healing) |
| Unit/integration-test all of the above headlessly | [Testing Harness](Testing-Harness) |
