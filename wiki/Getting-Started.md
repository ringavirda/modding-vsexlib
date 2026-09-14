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
    "exlib": "0.8.0-preview.3"
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
(see `samples/HandMill/src/HandMill.csproj`) so exlib's own change history builds against itself
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

`samples/HandMill` in this repo is everything above, buildable and bootable: a `Code` mod depending
on `exlib` and on the `grains` module sample, five blocks, one config, and a full test suite. Read it
file by file rather than typing the snippets by hand - every one below (bar one labelled alternative)
is copied verbatim from it, so it compiles.

A code-first block is a class that implements `IExBlockDefProvider` and carries `[BlockRegister]`.
There is no `blocktypes/drive/crank.json` anywhere in the mod's `assets/` folder; the JSON the object
loader reads is built by `ExBlockDef` and injected in memory at load. The crank is the mill's
producer, a network node riding a vanilla mechanics shape with an orientation variant group:

```csharp
[BlockRegister]
public partial class BlockCrank : BlockNetworkNode, IExBlockDefProvider {
  public override string NetworkType => "mpenergy";

  public static IEnumerable<ExBlockDef> Definitions(string domain) =>
    [
      ExBlockDef
        .Create(domain, "drive", "drive/crank")
        .Class<BlockCrank>()
        .EntityClass<BlockEntityCrank>()
        .Material(EnumBlockMaterial.Wood)
        .MaxStackSize(64)
        .VariantGroup("type", "crank")
        .VariantGroup("orientation", "n", "e", "s", "w")
        .NetworkOriented()
        .ShapeByType("*-n", "game:block/wood/mechanics/crank", rotateY: 270)
        .ShapeByType("*-e", "game:block/wood/mechanics/crank", rotateY: 180)
        .ShapeByType("*-s", "game:block/wood/mechanics/crank", rotateY: 90)
        .ShapeByType("*-w", "game:block/wood/mechanics/crank", rotateY: 0)
        .CreativeCommon("*-n")
        .SingleCollisionBox(0.1875f, 0f, 0.1875f, 0.8125f, 0.625f, 0.8125f)
        .SingleSelectionBox(0.1875f, 0f, 0.1875f, 0.8125f, 0.625f, 0.8125f)
        .SideSolid(false)
        .SideOpaque(false),
    ];
}
```

`VariantGroup("type", "crank")` carries one state; it exists so `NetworkNodeContractCheck` finds an
`OrientationMap` to place from, the same reason the shaft (its `drive` family sibling) declares one -
see [First Machine](First-Machine) for the shaft and the rest of the mill.

`ExModSystem` registers it - and every other `[BlockRegister]`/`[BlockEntityRegister]`/
`IExBlockDefProvider` in the assembly, and loads `HandMillValues` - with nothing to write:

```csharp
public class HandMillModSystem : ExModSystem { }
```

The explicit form behind it, for a mod system that needs a different order:

```csharp
public class HandMillModSystem : ModSystem {
  public override void Start(ICoreAPI api) {
    HandMillValues.Load(api);
    EntityRegistry.RegisterAll(api, Mod, GetType().Assembly);
  }
}
```

## 5. State, clicks and a wind that survives a reload

The crank's block entity is a small state machine: winding adds drive, driving eases off as the run
spins up, and it unwinds on its own when left alone:

```csharp
[BlockEntityRegister]
public class BlockEntityCrank : BlockEntityNetworkNode, IMpEnergyProducer {
  [Persist]
  private float _windSeconds;

  public override string NetworkType {
    get => "mpenergy";
    set { }
  }

  public override void Initialize(ICoreAPI api) {
    base.Initialize(api);
    if (api.Side == EnumAppSide.Server)
      RegisterGameTickListener(Unwind, 1000);
  }

  public void Wind(float seconds) {
    _windSeconds = Math.Min(_windSeconds + seconds, seconds * 2f);
    MarkDirty();
  }

  public float DriveTorque(float speed) =>
    _windSeconds <= 0f
      ? 0f
      : HandMillValues.CrankTorque
        * Math.Max(0f, 1f - speed / ExlibValues.MpMaxSpeed);

  private void Unwind(float dt) {
    if (_windSeconds <= 0f)
      return;
    _windSeconds = Math.Max(0f, _windSeconds - dt);
    MarkDirty();
  }
}
```

`[Persist]` is the whole save/load story for `_windSeconds`: no `ToTreeAttributes`/
`FromTreeAttributes` override, no key to spell twice. The block answers a click by winding, through
`ExInteraction` rather than a hand-rolled `IPlayer`/`BlockSelection` guard:

```csharp
public override bool OnBlockInteractStart(
  IWorldAccessor world,
  IPlayer byPlayer,
  BlockSelection blockSel
) {
  if (
    world.BlockAccessor.GetBlockEntity(blockSel.Position)
    is not BlockEntityCrank crank
  )
    return base.OnBlockInteractStart(world, byPlayer, blockSel);
  if (ExInteraction.Of(world, byPlayer, blockSel).IsClient)
    return true;
  crank.Wind(HandMillValues.WindSeconds);
  return true;
}
```

See [Helpers & Renderers](Helpers-and-Renderers) "Declared state" and "Block-entity lookups and side
checks" for the full surface of both.

## 6. A config value and a command

The mill's tunables, generated into a typed `HandMillValues` accessor:

```csharp
[ExConfigRegister("handmill.json", "handmill", Manageable = true)]
public class HandMillConfig : IExVersionedConfig {
  public string? ConfigVersion { get; set; }

  [ExConfigRange(1, 120)]
  public int WindSeconds { get; set; } = 10;

  [ExConfigRange(1f, 500f)]
  public float CrankTorque { get; set; } = 40f;

  [ExConfigRange(0.1f, 100f)]
  public float GrindTorque { get; set; } = 15f;

  [ExConfigRange(0.1f, 50f)]
  public float MinGrindSpeed { get; set; } = 1f;

  [ExConfigRange(0.01f, 100f)]
  public float ShaftInertia { get; set; } = 0.5f;

  [ExConfigRange(1f, 1000f)]
  public float FlywheelInertia { get; set; } = 40f;
}
```

`Manageable = true` is what puts it on the generic switch: `/exmod config handmill windseconds 20`
reads or writes it live, validated against the `[ExConfigRange]` bound, with no code of this mod's
own involved. `HandMillValues.WindSeconds` (read live in `BlockCrank.OnBlockInteractStart` above) is
generated from the property name.

The command that reads the mill's inputs lives in the `grains` module instead of in `HandMill`
itself, because it prints the catalogue any mill (or any other mod's machine) reads from, not
anything specific to this mill:

```csharp
[SubCommandRegister(Side = EnumAppSide.Server)]
public sealed class GrainsSubCommand : IExSubCommand {
  public string ParentName => "exmod";

  public void Register(ICoreAPI api, Mod mod, IChatCommand parent) {
    parent
      .BeginSubCommand("grains")
      .WithDescription(Lang.Get("grains:command-grains-desc"))
      .HandleWith(args =>
        TextCommandResult.Success(
          string.Join(
            "\n",
            GrainCatalogue.All.Select(g =>
              $"{g.Code}: {g.Grain} -> {g.Flour}, {g.Seconds}"
            )
          )
        )
      )
      .EndSubCommand();
  }
}
```

`/exmod grains` now prints one line per catalogue entry, whether or not `HandMill` is even installed.
See [Config System](Config-System) and [Commands](Commands) for everything else either surface
offers.

## 7. Test it

`samples/HandMill/tests` drives the crank headlessly through [Testing Harness](Testing-Harness)'s
`TestWorld`, with no game launch:

```csharp
public class CrankTests {
  [Fact]
  public void DriveTorque_is_zero_before_winding() {
    var be = new BlockEntityCrank();
    Assert.Equal(0f, be.DriveTorque(0f));
  }

  [Fact]
  public void DriveTorque_equals_CrankTorque_at_rest_after_winding() {
    var be = new BlockEntityCrank();
    be.Wind(10);
    Assert.Equal(HandMillValues.CrankTorque, be.DriveTorque(0f));
  }

  [Fact]
  public void DriveTorque_is_zero_at_the_run_burst_speed() {
    var be = new BlockEntityCrank();
    be.Wind(10);
    Assert.Equal(0f, be.DriveTorque(ExlibValues.MpMaxSpeed));
  }

  [Fact]
  public void The_wind_survives_a_tree_round_trip() {
    var world = new TestWorld();
    Block block = TestBlocks.Configure(
      new BlockCrank(),
      "handmill:drive-crank-e",
      1,
      ("orientation", "e")
    );
    var be = new BlockEntityCrank();
    world.Place(new BlockPos(0, 0, 0), block, be);
    world.Initialize(be);
    be.Wind(10);

    var tree = new TreeAttribute();
    be.ToTreeAttributes(tree);

    var restored = new BlockEntityCrank { Pos = be.Pos, Block = be.Block };
    restored.FromTreeAttributes(tree, world.World);

    Assert.Equal(HandMillValues.CrankTorque, restored.DriveTorque(0f));
  }
}
```

Run it with `dotnet test samples/HandMill/tests/HandMill.Tests.csproj`, or
`exmod test latest -Filter HandMill`.

## 8. Boot it

`exmod smoke -Mods samples/HandMill/src/bin/Debug/Mods/mod` (the default smoke lane already includes
it, alongside `grains`) launches the real dedicated server against the built mods, runs the content
checks and `/exmod verify`, and fails on a boot timeout or an `[Error]`/`[Fatal]` log line - the same
lane this repo's CI runs on every mod, now covering the one you just read:

```
[exlib] modules hosted by exlib: grains, industry
[exlib] Injected 5 code-first block definition(s).
[exlib] Injected 6 code-first item definition(s).
[exlib] check DefinitionCatalogue (handmill): 0 error(s)
[exlib] check MultiblockCodes (handmill): 0 error(s)
[exlib] check RecipeCodes (handmill): 0 error(s)
[exlib] check LangCoverage (handmill): 0 error(s)
[exlib] check NetworkNodeContract (handmill): 0 error(s)
[exlib] check PinnedNetworkNodes (handmill): 0 error(s)
[exlib] check CodePrefixCollision (handmill): 0 error(s)
```

The five blocks are the crank, the shaft, the flywheel, the mill core and the quern stand; the six
items are `grains`' sacks, one per catalogued grain. `grains` is listed among the modules hosted by
exlib because it ships as its own mod carrying no `ModSystem` of its own - see
[Modules](Modules). See [First Machine](First-Machine) for the rest of the mill: the shaft, the
flywheel, the designed multiblock core and the JSON-only quern stand.

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
