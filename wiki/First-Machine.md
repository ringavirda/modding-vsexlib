# First Machine

[Getting Started](Getting-Started) walks the twin-tub blower alone: a code-first block, a config
value and a test. This page finishes that machine's own footprint, then builds a second, more
involved one - `samples/BurdenMaker` - one rung per section: a filler footprint hosting a
mechanical-power port, a designed multiblock with a five-stage right-click construction, a container
with per-cell acceptance rules, and cell-routed interaction. Every snippet is copied verbatim from
the samples.

## The blower's footprint: filler cells and a hosted mechanical-power port

The twin-tub blower is one real block (the principal, a gas-pipe node) with five invisible filler
cells giving the rest of its 1x2x3 volume real collision. One of those cells - the upper-rear one -
hosts a mechanical-power port instead of just standing there, so an axle on its face drives the
bellows:

```csharp
private static readonly FillerBehaviorSpec MpPortWest =
  FillerBehaviorSpec.Of<BEBehaviorMPFillerPort>("west");

private static readonly FillerBehaviorSpec PipeThrough =
  FillerBehaviorSpec.Of<BEBehaviorNetworkMember>(
    "north",
    new { networkType = "pipe", passThrough = true }
  );

.FillerOffsets(
  StructureFootprint.Layout(f =>
    f.Host('M', MpPortWest)
      .Host('p', PipeThrough)
      .Slab('_', BlockFacing.DOWN)
      .Origin(-2, 1)
      .Slice(
        0,
        """
        _ # M
        p p 0
        """
      )
  )
)
```

`Host` is what separates this from a plain filler: a drawn cell can carry its own behaviour, not
just occupy space. `M` hosts the MP port (an axle mounted on its west face turns the bellows); `p`
hosts a pipe membership marked `passThrough`, so a run coupled two cells out through the blower's own
-Z face still reaches its network instead of dead-ending at the first filler it touches; `_` is a
slab rather than a full cube, so the blower does not block the floor tile in front of it; `0` is the
principal. `Slice` draws one horizontal layer at a fixed Y, rows running +Z, columns +X - see
[Multiblock Structures](Multiblock-Structures) "One grid, three uses" for how it and `Layer`/`Face`
share one grid core.

`BlockTwinTubMPBlower` places and removes those cells itself, because `BlockFilledMegastructure` (the
base every other filler footprint in this wiki subclasses) is a plain `Block`, and the blower must
also be a `BlockPipe`. Implementing `IFillerHost` directly means driving the placement triad by hand:

```csharp
public override bool CanPlaceBlock(
  IWorldAccessor world,
  IPlayer byPlayer,
  BlockSelection blockSel,
  ref string failureCode
) {
  if (!base.CanPlaceBlock(world, byPlayer, blockSel, ref failureCode))
    return false;
  if (!StructureFillers.CanPlace(world, FootprintCells(blockSel.Position))) {
    failureCode = "notenoughspace";
    return false;
  }
  return true;
}

public override void OnBlockPlaced(
  IWorldAccessor world,
  BlockPos blockPos,
  ItemStack? byItemStack = null
) {
  base.OnBlockPlaced(world, blockPos, byItemStack);
  StructureFillers.PlaceFillers(world, blockPos, FootprintCells(blockPos));
}

public override void OnBlockRemoved(IWorldAccessor world, BlockPos pos) {
  // Runs on every removal path (a player break, an explosion, a worldedit delete), unlike
  // OnBlockBroken, so the reserved volume is never left behind.
  StructureFillers.RemoveFillers(world, pos, FootprintCells(pos));
  base.OnBlockRemoved(world, pos);
}
```

`FootprintCells` rotates the authored layout by `StructureAngle` (read off the `orientation` variant
the same way the shape rotations are), so the three calls above are the whole placement/removal
story at any facing. On the block-entity side, the port cell is found the same way - a rotated
offset from the principal, looked up for its behaviour, not stored as a coupling of its own:

```csharp
private static readonly Vec3i MpPortCell = new(0, 1, 0);

private float PortSpeed() {
  BlockPos cell = ExOrientation.GlobalPos(
    Pos,
    MpPortCell.X,
    MpPortCell.Y,
    MpPortCell.Z,
    Angle
  );
  var port = Api
    .World.BlockAccessor.GetBlockEntity(cell)
    ?.GetBehavior<BEBehaviorMPFillerPort>();
  return port is { IsTurning: true } ? port.Speed : 0f;
}
```

`TwinTubBlowerTests` confirms the shipped footprint hosts that port on the cell the code above
expects and that a pipe coupled two cells out through the pass-through cells still joins the
blower's own network - see [Multiblock Structures](Multiblock-Structures) for the fuller
`FillerBehaviorSpec`/`Host` surface.

## The burden maker: a designed multiblock stock house

`samples/BurdenMaker` is a nine-cell mega-block: two hoppers (ore and flux) over a shared basin with
one sliding gate between them. Opening the gate drops both hoppers together into the basin as one
stamped batch. It has no mechanism and needs no power - proportioning is a single ore-to-flux ratio,
built entirely from right-click construction and a container block entity.

```
# # #
# O #
```
```
# # #
. . .
```

`#` marks a filler cell, `O` the principal (the gate), `.` an open cell left for the player to reach
into the basin. The first grid is the upper layer (the two hoppers over the front half), the second
the lower layer (the basin, open at the back).

```csharp
[BlockRegister]
public partial class BlockBurdenmaker
  : BlockFilledMegastructure,
    IFillerHost,
    IFillerInteractionTarget,
    IExBlockDefProvider {
  public static IEnumerable<ExBlockDef> Definitions(string domain) =>
    [
      ExBlockDef
        .Create(domain, "burdenmaker", "ore/burdenmaker")
        .Class<BlockBurdenmaker>()
        .EntityClass<BlockEntityBurdenmaker>()
        .Material(EnumBlockMaterial.Ceramic)
        .MiningTier(0)
        .Resistance(3.5f)
        .MaxStackSize(1)
        .NoDrops()
        .FillerOffsets(
          StructureFootprint.Layout(f =>
            f.Origin(-1, -1)
              .Layer(
                0,
                """
                # # #
                # O #
                """
              )
              .Layer(
                1,
                """
                # # #
                . . .
                """
              )
          )
        )
        .Behavior("ExOrientable")
        .Behavior("BlockEntityInteract")
        .EntityBehavior("Animatable")
        .Construction(c =>
          c.Stage(s => s.AddElements("Root/Base"))
            .Stage(s =>
              s.Require("game:burnedbrick-{brick}", 12, "burdenmaker:rcc-ingredient-brick")
                .AddElements("Root/BaseExtension")
            )
            .Stage(s =>
              s.Require("game:burnedbrick-{brick}", 16, "burdenmaker:rcc-ingredient-brick")
                .AddElements("Root/HopperMasonry")
            )
            .Stage(s =>
              s.Require("game:metalplate-iron", 6, "burdenmaker:rcc-ingredient-hopperplate")
                .AddElements("Root/Hoppers")
            )
            .Stage(s =>
              s.Require("game:metalplate-iron", 3, "burdenmaker:rcc-ingredient-lidplate")
                .Require("game:ingot-iron", 2, "burdenmaker:rcc-ingredient-lidrails")
                .AddElements("Root/Lids")
            )
        )
        .VariantGroup("brick", "black", "brown", "cream", "gray", "orange", "red", "tan")
        .SideVariant()
        .CreativeTab("general", "*-cream-n")
        .CreativeTab("burdenmaker", "*-cream-n")
        .ShapeSpunPerOrientation("burdenmaker:ore/burdenmaker")
        .ShapeSelectiveElements("Root/Base/*")
        .Texture("fire1", "game:block/clay/brick/four/running/cream1", "game:block/clay/brick/four/running/{brick}1")
        .SingleSelectionBox(0f, 0f, 0f, 1f, 1f, 1f)
        .SingleCollisionBox(0f, 0f, 0f, 1f, 1f, 1f)
        .SideSolid(false)
        .SideOpaque(false),
    ];

  public override int StructureAngle =>
    ExOrientation.AngleFromSide(Variant["side"]);
}
```

`Construction` lays out the five right-click stages a player walks through after placing the block:
the bare base, the base extension, the hopper masonry, the hopper ironwork, then the gate lids - each
`Stage` requiring its own materials and revealing the shape's own named element group
(`Root/BaseExtension`, `Root/HopperMasonry`, ...) once satisfied. `ShapeSelectiveElements` is what the
placed shell shows before any stage completes - the bare basin floor - and `NoDrops` plus a raised,
never-placed block (see "Drops" in the source) is what makes it a right-click-construction machine
rather than an ordinary placed one; see [Construction (RCC)](Construction) for the stage/element/tool
surface in full.

The `brick` variant group parameterises the shape's own `fire1` texture slot, so a burdenmaker built
from red brick differs from one built from cream brick by nothing but that one texture reference -
see [Code-First Definitions](Code-First-Definitions) "Textures" for `Texture(string, string, string)`,
the two-pattern overload used here.

## Cell classification: the cell is the verb

The machine has no GUI. A click on any of its nine cells does something different, and which cell
was clicked is read back from its offset to the principal, rotated into the block's own authored
frame:

```csharp
public enum BurdenmakerCell {
  OreHopper,
  FluxHopper,
  Gate,
  Bunker,
  Outside,
}

public static BurdenmakerCell Classify(
  BlockPos principal,
  BlockPos clicked,
  int structureAngle
) {
  Vec3i local = ExOrientation.RotateOffset(
    clicked.X - principal.X,
    clicked.Y - principal.Y,
    clicked.Z - principal.Z,
    -structureAngle
  );

  return (local.X, local.Y, local.Z) switch {
    ( >= -1 and <= 0, 1, -1) => BurdenmakerCell.OreHopper,
    (1, 1, -1) => BurdenmakerCell.FluxHopper,
    (0, 0, 0) => BurdenmakerCell.Gate,
    ( >= -1 and <= 1, 0, -1 or 0) => BurdenmakerCell.Bunker,
    _ => BurdenmakerCell.Outside,
  };
}
```

The inverse rotation (`-structureAngle`) is required: the two hoppers differ by X, so comparing raw
world coordinates would put ore in the flux hopper on the facings where X and Z have swapped, while
reading correctly on north. `Classify` is static and world-free, so `BurdenmakerCellTests` checks the
whole per-facing table with no world at all.

A click on the principal (the gate) arrives through the block's own `OnBlockInteractStart`; a click on
any other cell of the footprint arrives through `IFillerInteractionTarget`, which
`BlockStructureFiller` forwards to the principal with the clicked cell's own position carried along:

```csharp
public override bool OnBlockInteractStart(
  IWorldAccessor world,
  IPlayer byPlayer,
  BlockSelection blockSel
) =>
  HandleInteract(world, byPlayer, blockSel, blockSel.Position)
  ?? base.OnBlockInteractStart(world, byPlayer, blockSel);

bool IFillerInteractionTarget.OnFillerInteractStart(
  IWorldAccessor world,
  IPlayer byPlayer,
  BlockSelection principalSel,
  BlockPos clickedCell
) =>
  HandleInteract(world, byPlayer, principalSel, clickedCell)
  ?? base.OnBlockInteractStart(world, byPlayer, principalSel);
```

`HandleInteract` returns `null` before construction completes, so an early click still falls through
to the RCC behaviour rather than being swallowed by a machine that is not built yet. Once
constructed, it switches on `Classify`'s answer: the gate toggles, a hopper takes from or gives back
to the active hotbar slot (whole stack under Ctrl, one item otherwise - sneak is left alone, since
vanilla ground-storage placement already claims sneak + right-click), and the basin only ever gives
back, never takes:

```csharp
switch (cell) {
  case BurdenmakerCell.Gate:
    if (!be.ToggleGate(out string? error) && error != null)
      (byPlayer as IServerPlayer)?.SendIngameError(error);
    break;

  case BurdenmakerCell.OreHopper:
    if (active?.Empty == false)
      be.TryLoadOre(active, wholeStack);
    else
      GiveBack(world, byPlayer, sel, be.TryTakeOre());
    break;

  case BurdenmakerCell.Bunker:
    GiveBack(world, byPlayer, sel, be.TryWithdrawBurden());
    break;
}
```

(The flux hopper's case, symmetric with ore, is omitted here.) `GetPlacedBlockInteractionHelp` and
`IFillerInteractionTarget.GetFillerInteractionHelp` route through the same `Classify` call to show
the right hint - "add one ore", "take" - on whichever cell the player is looking at.

## The container: per-cell acceptance, not a fixed list

`BlockEntityBurdenmaker` holds three fixed slot ranges - ore, flux, basin - and answers what belongs
in each through `MaterialRoleRegistry` rather than a hand-written item list, so an ore contributed by
another mod's `materialroles.json` is accepted exactly as the shipped one is:

```csharp
public static bool IsOre(ItemStack? stack) =>
  stack?.Collectible?.Code is { } code
  && MaterialRoleRegistry.IsRole(Roles.IronOre, code);

public static bool IsFlux(ItemStack? stack) =>
  stack?.Collectible?.Code is { } code
  && MaterialRoleRegistry.IsRole(Roles.Flux, code);

private class InventoryBurdenmaker(int size) : InventoryGeneric(size, null, null) {
  public override bool CanContain(ItemSlot sink, ItemSlot from) {
    int index = GetSlotId(sink);
    ItemStack? stack = from?.Itemstack;
    return index switch {
      >= OreFirst and < FluxFirst => IsOre(stack),
      >= FluxFirst and < BunkerFirst => IsFlux(stack),
      // The basin is filled by the gate, never by hand or by a chute.
      _ => false,
    };
  }
}
```

Opening the gate is the one mutating step that is not a load or a take: both hoppers drain at once
into a single stamped `ItemStack`, and the basin must be empty first - that is what marks batch
boundaries with no batch state machine of its own, since pouring onto a leftover batch would average
two stamps into one:

```csharp
public bool ToggleGate(out string? errorCode) {
  errorCode = null;
  if (_gateOpen) {
    _gateOpen = false;
    ApplyPose();
    MarkDirty(true);
    return true;
  }

  int ore = OreUnits;
  int flux = FluxUnits;
  if (ore + flux == 0) {
    errorCode = "burdenmaker-nothingloaded";
    return false;
  }
  if (BurdenUnits > 0) {
    errorCode = "burdenmaker-emptybunker";
    return false;
  }

  ItemStack? batch = MakeBurden(ore, flux);
  if (batch == null) {
    errorCode = "burdenmaker-nothingloaded";
    return false;
  }

  ClearRange(OreFirst, OreSlots);
  ClearRange(FluxFirst, FluxSlots);
  StoreBurden(batch);

  _gateOpen = true;
  ApplyPose();
  MarkDirty(true);
  return true;
}
```

`MakeBurden` stamps the batch's actual ore/flux fractions onto the stack (`Burden.Write`), read back
later by the tooltip and by `GetBlockInfo`'s "would make burden at N% flux" preview - both graded
through the same config-tunable flux bands (`BurdenMakerValues.BurdenProfiles`) so a player can retune
what counts as under-, standard- or over-fluxed without recompiling. `ApplyPose` (the permanent
animation RCC needs since it draws no mesh of its own once built) and the load/take helpers are
omitted here.

## The tests

`BurdenmakerCellTests` checks the whole `Classify` table above at all four facings with no world at
all - a pure function over three `BlockPos`es and an angle needs nothing else.
`BurdenmakerInteractionTests` and `BurdenmakerTests` use [Testing Harness](Testing-Harness)'s
`TestWorld` to drive loading, taking and the gate through a real block entity. `MaterialRoleSeedTests`
(under `tests/Invariants`) checks that `MaterialRoleSeeds` - the headless stand-in for
`assets/burdenmaker/config/materialroles.json`, which the harness has no asset pipeline to load -
mirrors that file entry for entry, so a role typo in the shipped JSON cannot pass silently.

## The checks the smoke prints

Booting `exmod smoke` with both `twintubblower` and `burdenmaker` loaded (see [Getting
Started](Getting-Started) section 8) runs every content check in [Checks](Checks) against both
domains. Two of them matter most for a mega-block like this: `MultiblockCodes` confirms the
footprint's own filler cells resolve once every def is injected, and `LangCoverage` confirms every
`block-`/`blockdesc-` key the def implies is actually shipped - a missing one otherwise renders as
its own raw key in-game with nothing in the build to catch it. A clean run reads `0 error(s)` on
every line, for both domains, the same as any other mod in the family.
