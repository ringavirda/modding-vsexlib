# Getting Started

This page gets a third-party mod consuming `exlib`: declaring the runtime dependency, wiring a
project reference so you can call its APIs, and registering your first block.

## 1. Depend on exlib at runtime

`exlib` is a separate `Code` mod. In your mod's `modinfo.json`, add it under `dependencies`
with the minimum version you build against:

```json
{
  "type": "Code",
  "modid": "yourmod",
  "name": "Your Mod",
  "version": "1.0.0",
  "dependencies": {
    "game": "1.22.0",
    "exlib": "0.8.1"
  }
}
```

> ⚠ **A dependency is a minimum, not a pin.** The game accepts any installed `exlib` at or above the
> number you write, so a floor left at an old release lets a player satisfy it with an `exlib` that
> predates the method you are calling - and the failure arrives at world load as a missing member,
> not as a dependency error. Declare the version you actually compiled against, and raise it whenever
> you start calling something newer.

Declare the `game` floor the same way: the oldest version you support. This repo builds the whole
family against 1.20, 1.21 and 1.22 from one source tree and rewrites each `modinfo.json`'s game
version per target as it packs, so the shipped 1.20 zip declares `1.20.0` while the source declares
the current floor. If you target a single version, just name it.

At load time the game ensures `exlib` is present and loaded before your mod, so its
`ModSystem`s (the block-network manager, migration sweeper, healer, `/exmod` root) are already
up when your `Start`/`StartServerSide`/`StartClientSide` run.

> **Game versions.** `exlib` targets 1.22 but the family also builds and runs on 1.21 and 1.20
> via the `Legacy/` shim. If you only target 1.22 you can ignore the shim entirely; the public
> APIs on this wiki are the same across versions unless a page says otherwise.

## 2. Reference exlib at compile time

The project is called `ExpandedLib` and that is its root namespace, but the assembly it builds is
**`exlib.dll`** - the assembly name matches the mod id. That is the file the package ships, and
there is no `ExpandedLib.dll` anywhere outside `obj/`.

Reference the `ExpandedLib` NuGet package, with runtime assets excluded - the player installs
`exlib` as its own mod, so it must not ship a second copy inside your mod's output:

```xml
<ItemGroup>
  <PackageReference Include="ExpandedLib" Version="<latest>" ExcludeAssets="runtime" />
</ItemGroup>
```

> ⚠ **`ExcludeAssets="runtime"` is not optional, and omitting it fails silently.** Without it the SDK
> copies `exlib.dll` into your mod's output, and Vintage Story refuses to load a mod folder carrying a
> second assembly with `ModSystem`s in it - *"Found multiple .dll files with ModSystems and/or ModInfo
> attributes"*. Your mod is then simply absent from the loaded-mod list.

The package carries the config and lang source generators (as analyzers) and the whole
`GamePath`/provisioning/asset-glob build behind it, so a project referencing only `ExpandedLib`
needs no props of its own beyond a `TargetFramework` and an `<AssetDomain>` set to your modid - no
`$(GamePath)` to define, no `<Error>` target to write:

```xml
<PropertyGroup>
  <TargetFramework>net10.0</TargetFramework>
  <AssetDomain>yourmod</AssetDomain>
</PropertyGroup>
```

`$(AssetDomain)` is what turns on the asset copy - the whole `assets/` tree, not just its own
domain, since your `assets/` folder can hold overrides for other domains too - and selects which
domain's `lang/en.json` feeds the generated `{Domain}Lang` class (see [Source
Generators](Source-Generators)); without it, both stay inert. `$(GamePath)` still resolves the same
way the [Testing Harness](Testing-Harness) page assumes: from the `VINTAGE_STORY` environment
variable, or `-p:GamePath=...` on the command line.

Inside this monorepo the sample switches to a plain `ProjectReference` against the checkout instead
(see `samples/TwinTubBlower/src/TwinTubBlower.csproj`) so exlib's own change history builds against itself
without a release round-trip; nothing about that switch is part of the package's public contract.

## 3. Register your content

exlib is **attribute-driven**: you tag classes, and a `ModSystem` deriving `ExModSystem` registers
them all by reflection with no calls of its own to write. No manual
`api.RegisterBlockClass(...)` lists to maintain.

```csharp
using ExpandedLib.Registries;
using Vintagestory.API.Common;

[BlockRegister]                       // registers as "yourmod.BlockMachine"
public class BlockMachine : Block { }

[BlockEntityRegister]                 // registers as "yourmod.BlockEntityMachine" (+ short aliases)
public class BlockEntityMachine : BlockEntity { }

public class YourModSystem : ExModSystem { }
```

That single, empty class also registers any `[CommandRegister]`/`[SubCommandRegister]` class on
each side and any `[PreferenceRegister]` class on the client - see **[Commands](Commands)** for
adding one. If you need something to run in a particular order relative to registration (or aren't
deriving `ModSystem` at all), **[Registries](Registries)** documents the explicit `RegisterAll`
calls this class makes for you and the one ordering rule they carry.

See **[Source Generators](Source-Generators)** for the two generators exlib ships - typed config
accessors and typed lang keys. Read a block's JSON `attributes` with
`Attributes["..."].AsFloat()` as usual, or skip the JSON entirely and declare the block code-first
through `IExBlockDefProvider`.

### If other mods will name your types

Add an assembly-level domain marker so a cross-assembly lookup can resolve your classes:

```csharp
[assembly: ExDomain("yourmod")]
```

It must equal your mod id. `EntityRegistry.RegisterAll` does not need it - that path keys off the mod
id directly - so a mod nobody else extends works without it. It matters when *another* mod names one
of your types, for example through `ExBlockDef.Class<T>()`: the key is resolved from the type's own
assembly, and without the marker that lookup produces a key nobody registered. The block half of that
failure is not logged, which is why the attribute is worth declaring up front.

## 4. Your first block

`samples/TwinTubBlower` in this repo is everything above, buildable and bootable: a `Code` mod
depending on `exlib` alone, one block, one config, and a full test suite. Read it file by file rather
than typing the snippets by hand - every one below (bar one labelled alternative and one elided
footprint call) is copied verbatim from it, so it compiles.

A code-first block is a class that implements `IExBlockDefProvider` and carries `[BlockRegister]`.
There is no `blocktypes/furnace/twintubblower.json` anywhere in the mod's `assets/` folder; the JSON
the object loader reads is built by `ExBlockDef` and injected in memory at load. The blower is a
mechanically driven pair of bellows: a gas-pipe node that produces into the network it stands in,
riding its own shape with an orientation variant group:

```csharp
[BlockRegister]
public partial class BlockTwinTubMPBlower
  : BlockPipe,
    IExBlockDefProvider,
    IFillerHost {
  private static readonly FillerBehaviorSpec MpPortWest =
    FillerBehaviorSpec.Of<BEBehaviorMPFillerPort>("west");

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

`VariantGroup("type", "twintubblower")` carries one state; it is the family's discriminator, the
same shape the framework uses whenever a second block joins a code under `blower`. The
`FillerOffsets` call reserves the rest of the blower's 1x2x3 footprint and hosts the mechanical-power
port that drives it - [First Machine](First-Machine) walks that call and the block's own
`IFillerHost` placement triad in full.

`ExModSystem` registers it - and every other `[BlockRegister]`/`[BlockEntityRegister]`/
`IExBlockDefProvider` in the assembly, and loads `TwinTubBlowerValues` - with nothing to write:

```csharp
public class TwinTubBlowerModSystem : ExModSystem { }
```

The explicit form behind it, for a mod system that needs a different order:

```csharp
public class TwinTubBlowerModSystem : ModSystem {
  public override void Start(ICoreAPI api) {
    TwinTubBlowerValues.Load(api);
    EntityRegistry.RegisterAll(api, Mod, GetType().Assembly);
  }
}
```

## 5. State and a drive that survives a reload

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

`[Persist("blowerSpeed")]` is the whole save/load story for `_lastSpeed`: no `ToTreeAttributes`/
`FromTreeAttributes` override, no key to spell twice. `PortSpeed` (the axle lookup itself, through
the filler cell the port is hosted on) and `AmbientTemperature` are omitted here - see
[First Machine](First-Machine) for the footprint that cell sits in. See [Helpers &
Renderers](Helpers-and-Renderers) "Declared state" for the full surface `[Persist]` covers.

## 6. A config value

The blower's tunables, generated into a typed `TwinTubBlowerValues` accessor:

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

`Manageable = true` is what puts it on the generic switch: `/exmod config twintubblower
twintubbloweroutputpersecond 60` reads or writes it live, validated against the `[ExConfigRange]`
bound, with no code of this mod's own involved. `TwinTubBlowerValues.TwinTubBlowerMaxSpeed` (read
live in `SpeedFraction` above) is generated from the property name. See [Config
System](Config-System) and [Commands](Commands) for adding a `/exmod` sub-command of your own.

## 7. Test it

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

Run it with `dotnet test samples/TwinTubBlower/tests/TwinTubBlower.Tests.csproj`, or
`exmod test latest -Filter TwinTubBlower`.

## 8. Boot it

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

The counts above are process-global, not per-domain, and were taken booting both samples together:
the three blocks are the blower, the burden maker (see [First Machine](First-Machine)) and exlib's
own structure filler (`BlockStructureFiller`), injected once for every mod that places a filled
megastructure; the one item is the burden maker's own `burden`. See [First Machine](First-Machine)
for the rest of the footprint: the filler cells, the mechanical-power port, and a second, more
involved machine built the same way.

## 9. exmod in your repo

The whole toolchain above - build, test, smoke - is one script, not a set of raw `dotnet` commands
you assemble yourself. Copy `scripts/exmod.sh` and `scripts/exmod.ps1` from this repo into your
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

`exmod provision game` fetches the dedicated-server archive into `.game/`, no purchase needed to
build and test headlessly; `exmod build` compiles against it; `exmod test` runs your test project
the same way `dotnet test` does, but resolved from the manifest rather than named on the command
line; `exmod smoke` boots the real server with your built mod and fails on a boot timeout or an
`[Error]`/`[Fatal]` log line. Before the boot, `exmod provision mods` reads `exlib` out of your
`modinfo.json`'s `dependencies` and fetches it for the smoke to load alongside your own mod - a
workspace sibling checkout of exlib when there is one, otherwise a published release.

`exmod scaffold <kind> <Name>` (alias `g`) drops a compiling, tested block, item, recipe, megablock,
multiblock, node, blockbehavior, entitybehavior, config, migration or command into your mod from the
templates exlib ships as `ExpandedLib.Templates`: `exmod scaffold block Widget` lands a block and its
block entity in `src/Blocks` and `src/BlockEntities` with a test in `tests/`, and merges the lang keys
the generated code reads into `assets/<id>/lang/en.json` - each kind lands compiling with a test. A
block, item, recipe, megablock, multiblock or node is a code-first def: if your mod's test project
golden-checks the whole set (see **[Code-First Definitions](Code-First-Definitions)**, "Goldens"), the
new def's missing golden turns that check red until you bless it once with `EXLIB_WRITE_GOLDENS=1`.

## 10. Pick the system you need

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
