# First Machine

[Getting Started](Getting-Started) walks the crank alone: a node in two classes, a config value, a
command and a test. This page finishes the mill it belongs to - `samples/HandMill` - one rung per
section, in the order you'd build it: a plain transmission node, a filler mega-block with a hosted
membership, a designed multiblock with a production tick, and a JSON-only completion check. Every
snippet is copied verbatim from the sample.

::schematic{code="handmill:millcore-n" views="plan,iso"}

The wiki site renders the line above as a figure - two views of the mill core's layout, drawn from
its own definition. The GitHub wiki (and any other plain Markdown reader) shows it as the literal
text you see here instead.

```
# A #
# C #
# . #
```

`#` is the cobble wall either side, `A` the shaft cell the core couples to, `C` the core itself,
`.` the open front a player stands at. Read on for what builds that layout.

## The shaft: a node in two classes

The shaft is the plainest network node the framework has: a block that rides a vanilla shape, and a
block entity that is nothing but a small energy buffer. It shares the `drive` family with the crank
- `ExBlockDef.Create(domain, "drive", "drive/shaft")` beside the crank's `"drive/crank"` - the way
iiex's `mpenergy` blocks (its own shaft, bevel and transmission) are one family sharing one code:

```csharp
[BlockRegister]
public partial class BlockShaft : BlockNetworkNode, IExBlockDefProvider {
  public override string NetworkType => "mpenergy";

  public static IEnumerable<ExBlockDef> Definitions(string domain) =>
    [
      ExBlockDef
        .Create(domain, "drive", "drive/shaft")
        .Class<BlockShaft>()
        .EntityClass<BlockEntities.BlockEntityShaft>()
        .Material(EnumBlockMaterial.Wood)
        .MaxStackSize(64)
        .VariantGroup("type", "shaft")
        .VariantGroup("orientation", "ns", "we", "ud")
        .NetworkOriented()
        .ShapeByType("*-we", "game:block/wood/mechanics/axle", rotateY: 0)
        .ShapeByType("*-ns", "game:block/wood/mechanics/axle", rotateY: 90)
        .ShapeByType("*-ud", "game:block/wood/mechanics/axle", rotateZ: 90)
        .CreativeCommon("*-ns")
        .SingleCollisionBox(0.3125f, 0.3125f, 0f, 0.6875f, 0.6875f, 1f)
        .SingleSelectionBox(0.3125f, 0.3125f, 0f, 0.6875f, 0.6875f, 1f)
        .SideSolid(false)
        .SideOpaque(false),
    ];
}
```

Both defs render under `blocktypes/drive/`, so `blocktypes/drive/shaft.json` and
`blocktypes/drive/crank.json` sit beside each other in the built assets the same way iiex's
`blocktypes/mpenergy/shaft.json` and `blocktypes/mpenergy/transmission.json` do. Vanilla's own axle
mesh runs along X, so `we` is the unrotated mesh and `ns` the 90-degree Y turn - the opposite of what
you would guess from the letters alone.

```csharp
[BlockEntityRegister]
public class BlockEntityShaft : BlockEntityNetworkNode, IMpEnergyStorage {
  public override string NetworkType {
    get => "mpenergy";
    set { }
  }

  public float Inertia => HandMillValues.ShaftInertia;
}
```

Graph membership - joining, leaving, merging, fracturing - is entirely the `BlockEntityNetworkNode`
base's job; the shaft's own class exists only to answer `Inertia`, so a run of several shafts buffers
a little more than a bare crank-to-core line would. See [Block Networks](Block-Networks) for the base
class and `SceneDiagram`'s `c===` shorthand for testing a line like this headlessly.

Scaffolding a node like this one with `exmod scaffold node Shaft` leaves its golden missing: bless it once with `EXLIB_WRITE_GOLDENS=1 bash scripts/exmod.sh test latest`.

## The flywheel: a filler footprint and a hosted membership

The flywheel is a 3x3 vertical wheel: one real block (the hub) with eight invisible filler cells
giving the rest of the wheel real collision. It is not itself a `BlockNetworkNode` - it is a
`BlockFilledMegastructure` whose block entity hosts a `BEBehaviorNetworkMember` instead, the "form
and membership are independent axes" split from [Block Networks](Block-Networks):

```csharp
[BlockRegister]
public partial class BlockFlywheel
  : BlockFilledMegastructure,
    IFillerInteractionTarget,
    IExBlockDefProvider {
  public static IEnumerable<ExBlockDef> Definitions(string domain) =>
    [
      ExBlockDef
        .Create(domain, "flywheel")
        .Class<BlockFlywheel>()
        .EntityClass<BlockEntityFlywheel>()
        .Material(EnumBlockMaterial.Wood)
        .MaxStackSize(1)
        .Shape("game:block/wood/mechanics/largegear3")
        .Behavior("ExOrientable")
        .SideVariant()
        .ShapeByTypePerOrientation("game:block/wood/mechanics/largegear3", 0)
        .CreativeCommon("*-n")
        // A vertical 3x3 wheel: the principal is the hub, the eight fillers the rim, drawn as one
        // front elevation at the wheel's own Z.
        .FillerOffsets(
          StructureFootprint.Layout(f =>
            f.Origin(-1, 1)
              .Face(
                0,
                """
                ###
                #0#
                ###
                """
              )
          )
        )
        .SolidNonOpaque(),
    ];

  public override int StructureAngle =>
    ExOrientation.AngleFromSide(Variant["side"]);

  /// <summary>Every cell of the wheel answers the same way: a sneak-click brakes it.</summary>
  public bool OnFillerInteractStart(
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection principalSel,
    BlockPos clickedCell
  ) {
    if (!ExInteraction.Of(world, byPlayer, principalSel).Sneaking)
      return false;
    if (
      world.Side == EnumAppSide.Server
      && world.BlockAccessor.GetBlockEntity(principalSel.Position)
        is BlockEntityFlywheel wheel
    )
      wheel.Brake();
    return true;
  }
}
```

`OnFillerInteractStep`, `OnFillerInteractStop` and `GetFillerInteractionHelp` are no-ops on
`BlockFlywheel`, and `OnBlockInteractStart` only forwards to `OnFillerInteractStart` for the
principal cell itself - omitted here. `Face` draws a front elevation at a fixed Z, rows running -Y from the top, columns +X - see [Multiblock
Structures](Multiblock-Structures) "One grid, three uses" for how it and `Layer`/`Slice` share one
grid core. `0` marks the principal; every other drawn cell becomes a filler, so the wheel's rim carries
real collision without eight real blocks. Every cell answers a sneak-click the same way: it brakes the
run, forwarded from whichever rim cell was clicked by `BlockStructureFiller`/`IFillerInteractionTarget`.

The block entity hosts its membership in the constructor, not `Initialize`, because `BlockEntity` fans
`Initialize`/`FromTreeAttributes` out over `Behaviors` and a membership added later would miss
whichever of those has already run:

```csharp
[BlockEntityRegister]
public class BlockEntityFlywheel : ExBlockEntity, IMpEnergyStorage {
  private readonly HostMembership _membership;

  public BlockEntityFlywheel() {
    // Added in the constructor: BlockEntity fans Initialize and FromTreeAttributes out over
    // Behaviors, so a membership added later misses whichever has already run.
    _membership = new HostMembership(this);
    Behaviors.Add(_membership);
  }

  public float Inertia => HandMillValues.FlywheelInertia;

  public override void Initialize(ICoreAPI api) {
    // The axle passes through the hub: the wheel couples on both faces normal to its plane, in
    // the placed orientation.
    int angle = (Block as BlockFlywheel)?.StructureAngle ?? 0;
    _membership.Connectors =
    [
      ExOrientation.RotateFacing(BlockFacing.NORTH, angle),
      ExOrientation.RotateFacing(BlockFacing.SOUTH, angle),
    ];
    base.Initialize(api);
  }

  /// <summary>Stops the run this wheel is on: its stored energy is dropped to zero.</summary>
  public void Brake() {
    if (this.NetworkAt<MpEnergyNetwork>(Pos)?.State is { } state) {
      state.StoredEnergy = 0f;
      state.Speed = 0f;
    }
  }

  private sealed class HostMembership(BlockEntityFlywheel owner)
    : BEBehaviorNetworkMember(owner) {
    public override string NetworkType {
      get => "mpenergy";
      protected set { }
    }
  }
}
```

`GetBlockInfo` (the speed/energy readout a player sees on look) is omitted here. `Connectors` is set before `base.Initialize` runs, so the membership registers with the rotated faces
- `BEBehaviorNetworkMember.Initialize` reads them, not the other way round.

## The mill core: a designed multiblock that also produces

The core is a `Block`, not a `BlockNetworkNode` and not a `BlockFilledMegastructure`: it draws its
shape from a code-first `MultiblockLayout` and its block entity subclasses
`BlockEntityMultiblockMachine`, so it is both a designed structure and a production machine at once.

```csharp
[BlockRegister]
public partial class BlockMillCore : Block, IExBlockDefProvider {
  public static IEnumerable<ExBlockDef> Definitions(string domain) =>
    [
      ExBlockDef
        .Create(domain, "millcore")
        .Class<BlockMillCore>()
        .EntityClass<BlockEntityMillCore>()
        .Material(EnumBlockMaterial.Stone)
        .MaxStackSize(1)
        .Shape("game:block/stone/quern/complete")
        .Texture("grindstoneup", "game:block/stone/rock/granite1")
        .Texture("baseside", "game:block/stone/rock/granite1")
        .Texture("baseup", "game:block/stone/rock/granite1")
        .Texture("wood", "game:block/stone/rock/granite1")
        .Behavior("MultiblockStructure")
        .Behavior("ExOrientable")
        .Behavior<BlockBehaviorGrainInfo>()
        .SideVariant()
        .CreativeCommon("*-n")
        // Cobble walls either side, the shaft cell behind, the front open for the player.
        .MultiblockLayout(l =>
          l.Origin(-1, -1)
            .Legend('#', "game:cobblestone-*")
            .Legend('A', $"{domain}:drive-shaft-*")
            .Legend('C', $"{domain}:millcore-*")
            .Connector('A', BlockFacing.SOUTH)
            .Role('A', MillCellRoles.Axle)
            .Core('C')
            .Layer(
              0,
              """
              # A #
              # C #
              # . #
              """
            )
        )
        .SolidNonOpaque(),
    ];
}
```

`Legend` maps a grid symbol to the code that must occupy it; `Connector` and `Role` are the pieces the
grid alone cannot say - which face of that cell the network couples through, and which cell a caller
can find back by name (`MillCellRoles.Axle`, a single-cell role declared once so the layout and the
block entity share one key). `Core` marks the principal. See [Multiblock
Structures](Multiblock-Structures) "The explicit API" for when a layout needs this over the plain JSON
grid, and `Texture(string, string)` is the two-argument overload; the quern shape's own texture keys
(`grindstoneup`, `baseside`, `baseup`, `wood`) come from `block/stone/quern/complete.json`, not from a
guess at `"all"`.

The block entity holds the grain/flour counters, couples to the shaft cell, and only grinds while the
structure is complete and the run is fast enough:

```csharp
[BlockEntityRegister]
public class BlockEntityMillCore
  : BlockEntityMultiblockMachine,
    IMpEnergyConsumer {
  private readonly HostMembership _membership;

  [Persist]
  private string _grainCode = "";

  [Persist]
  private int _grain;

  [Persist]
  private string _flourCode = "";

  [Persist]
  private int _flour;

  [Persist]
  private float _progress;

  public BlockEntityMillCore() {
    _membership = new HostMembership(this);
    Behaviors.Add(_membership);
  }

  private int Angle => ExOrientation.AngleFromSide(Block?.Variant?["side"]);

  public override void Initialize(ICoreAPI api) {
    // The shaft cell sits north of the core in the authored frame.
    _membership.Connectors =
    [
      ExOrientation.RotateFacing(BlockFacing.NORTH, Angle),
    ];
    base.Initialize(api);
  }

  protected override void UpdateStructureRotation() => SetStructureAngle(Angle);

  protected override string GetIncompleteMessage(int missingCount) =>
    Lang.Get("handmill:millcore-incomplete", missingCount);

  protected override string GetCompleteMessage() =>
    Lang.Get("handmill:millcore-complete");

  private float Speed =>
    this.NetworkAt<MpEnergyNetwork>(Pos)?.State?.Speed ?? 0f;

  private bool Grinding =>
    StructureComplete && _grain > 0 && Speed >= HandMillValues.MinGrindSpeed;

  /// <summary>The mill loads the run only while it is grinding.</summary>
  public float LoadTorque(float speed) =>
    Grinding ? HandMillValues.GrindTorque : 0f;

  protected override void OnProductionTick(float dt) {
    if (!Grinding)
      return;
    GrainDef? entry = GrainCatalogue.ForItem(_grainCode);
    if (entry == null)
      return;
    _progress += dt;
    if (_progress < entry.Seconds)
      return;
    _progress = 0f;
    _grain--;
    _flourCode = entry.Flour;
    _flour++;
    MarkDirty();
  }

  private sealed class HostMembership(BlockEntityMillCore owner)
    : BEBehaviorNetworkMember(owner) {
    public override string NetworkType {
      get => "mpenergy";
      protected set { }
    }
  }
}
```

`TryLoad`, `TakeFlour` and `GetBlockInfo` (loading a grain, handing over flour, the look readout)
are omitted here.

`IMpEnergyConsumer.LoadTorque` is how the mill draws on the run: read by the network the same way
`IMpEnergyProducer.DriveTorque` is read on the crank's side, both through `NetworkAt<MpEnergyNetwork>`.
`GrainCatalogue.ForItem` is the one thing the mill consumes from the `grains` module - see
[Modules](Modules) for the module itself and why the lookup crosses assemblies cleanly.

## The quern stand: the JSON-only rung

The stand needs no C# at all: a designed structure of two cells (the stand itself, a vanilla quern
placed on top), built entirely from the zero-config classes:

```json
{
  "code": "quernstand",
  "class": "ExFilledMegastructure",
  "entityClass": "ExMultiblock",
  "behaviors": [{ "name": "MultiblockStructure" }, { "name": "ExOrientable" }],
  "variantgroups": [{ "code": "side", "loadFromProperties": "abstract/horizontalorientation" }],
  "shape": { "base": "game:block/basic/cube" },
  "textures": { "all": { "base": "game:block/stone/rock/granite1" } },
  "creativeinventory": { "general": ["*-north"], "handmill": ["*-north"] },
  "sidesolid": { "all": true },
  "sideopaque": { "all": true },
  "attributes": {
    "fillerOffsets": [],
    "multiblockLayout": {
      "origin": [0, 0],
      "legend": { "C": "handmill:quernstand-*", "Q": "game:quern-*" },
      "layers": [["C"], ["Q"]],
      "core": "C"
    }
  }
}
```

`"fillerOffsets": []` matters here: this is a designed structure of other real blocks, not filler
cells, so a derived footprint (every drawn cell but the principal's own) would reserve the quern's own
cell with an invisible filler and the player could never place one there. See [Multiblock
Structures](Multiblock-Structures) "From JSON only" for the rung this is and why a declared empty list
is honoured rather than replaced. With no lang keys of its own, the stand reads exlib's own
`multiblock-incomplete`/`-complete` messages - the zero-config rung all the way down.

## The tests

`ShaftLineTests` builds a crank-then-three-shafts line with `SceneDiagram`'s `c===` shorthand: one
network, four nodes, `Wind` then `Step` shows the speed rise and, once the wind runs out, coast back
toward zero. A straight line like this needs nothing but a diagram - see [Testing
Harness](Testing-Harness) "Integration tests with `Scene` and `SceneDiagram`".

`MillCoreTests` and `FlywheelTests` use `StructureRig` instead, because their structures are real
designed shapes, not a line of identical cells: the rig places a real stand-in block in every layout
cell the layout demands and lets the anchor's own monitor tick observe completion, rather than the
test forcing `StructureComplete = true` by hand. Forcing the flag would prove nothing about the
layout, the rotation or the connector faces being right; the rig proves all three, because a wrong one
of them shows up as a structure that never completes on its own. `QuernStandTests` uses the same rig
around the JSON-only stand, confirming the zero-config classes complete exactly the same way a
hand-written one does.

## The checks the smoke prints

Booting `exmod smoke` with `handmill` and `grains` loaded (see [Getting Started](Getting-Started)
section 8) runs every content check in [Checks](Checks) against both domains. Two of them matter most
for a mill like this: `MultiblockCodes` confirms every legend entry names a code that actually exists
once every def is injected, and `NetworkNodeContract` confirms every network node and membership
declares the pieces the run needs to place and couple at all - a node or membership with no
`type` variant group, for instance, has nothing for its `OrientationMap` to place from, and
`TryPlaceBlock` refuses it silently at runtime with no check to say why. A clean run reads
`0 error(s)` on every line, for both domains, the same as any other mod in the family.
