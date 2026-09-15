# First Machine

A machine is more than a block. It fills more than its own cell of the world, it keeps state that
has to survive a save and a reload, it takes power from an axle, and a player clicking one corner of
it expects something different from a click on the other side. Vintage Story gives you a block per
cell and a block entity - the object the game keeps beside a placed block to hold that block's state
and run its ticks - and nothing above that. Everything else is yours to write.

exlib's answer is the mega-block: one real block, called the principal, with invisible filler blocks
standing in the rest of its volume so the machine has real collision everywhere it looks solid, and
with every click on a filler routed back to the principal.
[Multiblock Structures](Multiblock-Structures) is the reference page for that system; this page is
the walkthrough.

It builds two machines, in order, from the samples in the exlib repository.
[Getting Started](Getting-Started) has already walked the first, `samples/TwinTubBlower`, as a
single block with a config value and a test. The first section here gives that block its footprint
and the drive port that turns it. The rest builds a second machine from nothing,
`samples/BurdenMaker`: a nine-cell stock house the player raises in five stages of right-click
construction, with two hoppers, a shared basin, and a click that means something different on every
cell.

Every snippet is copied verbatim from those samples, so you can open the file beside the section
that quotes it. Read [Getting Started](Getting-Started) first if you have not - this page assumes
the code-first block definitions and the attribute registration it covers.

## The blower's footprint: filler cells and a hosted mechanical-power port

The twin-tub blower is a bellows that pumps into the gas-pipe network it stands in, turned from
outside by an axle. In the world it is one real block - the principal, which is also the pipe node -
and five invisible filler cells that give the rest of its 1x2x3 volume real collision, so a player
cannot walk through the part of the machine that has no block of its own.

A filler cell can do more than stand there. One of these, the upper-rear one, hosts a
mechanical-power port. Mechanical power is the game's network of axles and gears, the one a windmill
or a water wheel drives; a port is where a machine couples to it. Hosting the port on a filler is
what lets an axle meet the machine at the height the model shows it, instead of at the principal's
own cell:

```csharp
private static readonly FillerBehaviorSpec MpPortWest =
  FillerBehaviorSpec.Of<BEBehaviorMPFillerPort>(
    "west",
    new { through = false }
  );

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

The footprint is drawn as text, one character per cell, and the legend above it says what each
character is. `Host` is the part that separates this from a plain filler: the cell carries a block
entity behaviour of its own - a component the game attaches to a block entity to add one piece of
behaviour without a subclass - configured by the face it faces and a small property bag.

`M` hosts the mechanical-power port, so an axle mounted on that cell's west face turns the bellows.
`p` hosts a pipe membership marked `passThrough`, which keeps a pipe run coupled two cells out
through the blower's own -Z face connected to the network instead of dead-ending at the first filler
it touches. `_` is a slab rather than a full cube, so the blower does not block the floor tile in
front of it. `0` is the principal, the one real block, and the cell every other cell is drawn
relative to.

`Slice` draws one vertical grid at a fixed X: rows run downwards from the top, columns run +Z. Its
siblings `Layer` (a floor plan at a fixed Y) and `Face` (an elevation at a fixed Z) read the same
way, and a layout may mix all three - see [Multiblock Structures](Multiblock-Structures) "One grid,
three uses".

Most blocks with a footprint derive from `BlockFilledMegastructure`, the base every other filler
footprint in this wiki subclasses, and it places and removes the fillers for them. The blower
cannot. That base is a plain `Block`, and the blower has to be a `BlockPipe` to be a node of the gas
network; a class gets one base. So it implements `IFillerHost` directly and drives the three
placement hooks itself:

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

`CanPlaceBlock` is the question the game asks before the block goes down; answering `false` with a
failure code is what tells the player there is no room, rather than swallowing the click; the code
is a key the game turns into the message on screen. `OnBlockPlaced` puts the fillers in once the
principal exists, and `OnBlockRemoved` takes them out again - the comment in it is the reason to
override that one rather than `OnBlockBroken`.

`FootprintCells` rotates the authored layout by `StructureAngle`, which the blower reads off its own
`orientation` variant. A variant is one axis of a blocktype that the game expands into a separate
block per value, so a blower facing east is its own block and knows its facing from its code. The
layout is authored once, facing north, and rotated on the way out, so the three calls above are the
whole placement and removal story at every facing.

On the block-entity side, the port cell is found the same way - a rotated offset from the principal,
looked up for its behaviour, rather than stored as a coupling of its own:

```csharp
private static readonly Vec3i MpPortCell = new(0, 1, 0);

private BEBehaviorMPFillerPort? Port() {
  BlockPos cell = ExOrientation.GlobalPos(
    Pos,
    MpPortCell.X,
    MpPortCell.Y,
    MpPortCell.Z,
    Angle
  );
  return Api
    .World.BlockAccessor.GetBlockEntity(cell)
    ?.GetBehavior<BEBehaviorMPFillerPort>();
}

private float PortSpeed() =>
  Port() is { IsTurning: true } port ? port.Speed : 0f;
```

`PortSpeed` asks the port how fast it is turning and gets 0 when nothing drives it. That number is
the machine's whole power input: the bellows animate, sound and push air into the pipe network in
proportion to it. `through = false`, back in the spec, makes the port one-sided. The cell
couples an axle on that face alone and the network ends there, where the default lets a row of ports
carry power straight through a machine and out the other side.

An animated part driven by the axle should read the port's `DrivenAngleRad`, which is the rotation
in the sense the axle is drawn on whichever side the port faces. `CurrentAngleRad` is the network's
own angle and reads mirrored on a west or north port, so a gear keyed to it turns backwards on half
the facings.

`TwinTubBlowerTests` confirms the shipped footprint hosts that port on the cell the code above
expects, and that a pipe coupled two cells out through the pass-through cells still joins the
blower's own network - see [Multiblock Structures](Multiblock-Structures) for the fuller
`FillerBehaviorSpec` and `Host` surface.

## The burden maker: a designed multiblock stock house

`samples/BurdenMaker` is a nine-cell mega-block: two hoppers, one for ore and one for flux, over a
shared basin with a sliding gate between them. A burden is the measured mix of ore and flux a
furnace is charged with, and this machine is where a player makes one. Load both hoppers, open the
gate, and what reaches the basin is one batch stamped with the proportions it was made at.

Nothing here turns and nothing needs power. The machine is a footprint, a right-click construction
in five stages, and a container block entity, which makes it the one to read for everything about a
multiblock except the power line above.

```
# # #
# O #
```
```
# # #
. . .
```

`#` marks a filler cell, `O` the principal (the gate), `.` an open cell. The first grid is the lower
layer, the basin, with the gate among its cells. The second is the layer above it: the two hoppers
fill the row behind the gate, and the three cells directly over the gate are left open so a player
can reach into the basin.

One `ExBlockDef` declares all of it. Read it from the top: what the block is and which two classes
run it, how it breaks and what it drops, the footprint drawn above, the behaviours it carries, the
five stages it is built in, and last the variants it expands into with the shape and textures each
one draws.

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

`Construction` lays out the five stages the player walks through after placing the block.
Right-click construction is the game's way of raising a block in stages instead of placing it whole:
the player holds the next material, right-clicks, and each satisfied stage reveals another part of
the model. Here the stages are the bare base, the base extension, the hopper masonry, the hopper
ironwork and the gate lids, and each `Stage` names both the materials it consumes and the group of
shape elements it reveals (`Root/BaseExtension`, `Root/HopperMasonry`, and so on).

`ShapeSelectiveElements` is what the placed shell shows before any stage completes, the bare basin
floor. `NoDrops`, together with the `GetDrops` override in the source's "Drops" region, is what
stops the machine dropping itself as an item when broken: what the player gets back is the
construction materials and whatever the hoppers and basin held, never the block.
[Construction (RCC)](Construction) covers the stages, the salvage fraction and the animator a
constructable block needs to be visible at all.

The `brick` variant group is why a burdenmaker comes in seven brick colours rather than one. A
variant group expands the definition into one block per value and substitutes that value wherever
the declaration writes `{brick}`, which here is the texture path alone: a burdenmaker built from red
brick differs from one built from cream brick by that one texture reference and nothing else. See
[Code-First Definitions](Code-First-Definitions) "Textures" for `Texture(string, string, string)`,
the two-pattern overload used here.

## Cell classification: the cell is the verb

The machine opens no dialog. Most containers in the game answer a right-click with an inventory
window; this one answers with whatever the cell under the crosshair means, so a click on a hopper
loads it and a click on the basin takes from it. Which cell was clicked is read back from its offset
to the principal, rotated into the frame the layout was authored in:

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

The inverse rotation (`-structureAngle`) is what makes the table above one table rather than four.
The two hoppers differ by X, so comparing raw world coordinates would read correctly on north and
put ore in the flux hopper on the facings where X and Z have swapped. Rotating the offset back into
the authored frame first means the cases are written once, for the facing you drew.

`Classify` takes three positions and an angle and touches nothing else - no world, no block entity -
so `BurdenmakerCellTests` checks the whole per-facing table with no world at all.

A click on the principal, the gate, arrives through `OnBlockInteractStart`, the hook the game calls
on a block the player right-clicks. A click on any other cell lands on a filler, which is not your
block at all, so it arrives through `IFillerInteractionTarget`: `BlockStructureFiller` forwards it
to the principal and carries the clicked cell's own position along with it.

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

Both paths meet in `HandleInteract`, and it returns `null` before construction completes so that an
early click falls through to the construction behaviour rather than being swallowed by a machine
that is not built yet. Once the machine is finished it switches on `Classify`'s answer: the gate
toggles, a hopper loads from the active hotbar slot - the one the player is holding - or gives back
when that slot is empty, and the basin only ever gives back. Ctrl moves the whole stack and a plain
click moves one item; sneak is left alone, since the game's own ground-storage placement already
claims sneak plus right-click.

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

(The flux hopper's case, symmetric with ore, is omitted here.) Interaction help is the hint the game
prints while a player looks at a block, and it has to match what the click will do.
`GetPlacedBlockInteractionHelp` and `IFillerInteractionTarget.GetFillerInteractionHelp` route
through the same `Classify` call as the click itself, so "add one ore" or "take" appears on
whichever cell the player is looking at and cannot drift from the behaviour.

## The container: per-cell acceptance, not a fixed list

A block entity that holds items owns an inventory: a flat array of slots the game saves, syncs and
lets hoppers and chutes reach into. `BlockEntityBurdenmaker` divides one inventory into three fixed
slot ranges, ore, flux and basin, so which tank a slot belongs to is a property of its index rather
than of what happens to be sitting in it.

What each range accepts is the interesting part. `CanContain` is the question the engine asks before
any stack moves into a slot, from a player or from a machine, and answering it with a hand-written
list of item codes would mean no other mod could ever feed this machine. Asking
`MaterialRoleRegistry` instead - the catalogue of which items count as ore, flux, fuel or scrap -
means an ore contributed by another mod's `materialroles.json` is accepted exactly as the shipped
one is. [Extending Processes](Extending-Processes) is how that catalogue is contributed to.

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

Opening the gate is the one step that is neither a load nor a take. It fixes the batch: the ore
and flux the hoppers hold at that moment become the mix every slice of this batch is stamped with,
and a server tick then moves the hoppers' contents down into the basin over
`BurdenmakerDrainSeconds`, so the player watches the burden go down rather than seeing it jump.
The gate refuses to open with nothing loaded, and refuses to pour onto a finished batch still in
the basin, since two batches averaged into one stack would carry the wrong stamp; a batch that is
still draining may be closed and reopened. Both refusals come back as an error code rather than a
bare `false`, which is what lets the caller put a message on the player's screen:

```csharp
public bool ToggleGate(out string? errorCode) {
  errorCode = null;

  if (_gateOpen) {
    _gateOpen = false;
    StopDrain();
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
  if (BurdenUnits > 0 && !_batchInProgress) {
    errorCode = "burdenmaker-emptybunker";
    return false;
  }

  if (!_batchInProgress) {
    float total = ore + flux;
    _batchMix = new BurdenMix(ore / total, flux / total);
    _batchTotal = ore + flux;
    _batchInProgress = true;
  }

  _gateOpen = true;
  ApplyPose();
  StartDrain();
  MarkDirty(true);
  return true;
}
```

`DrainTick` runs every 250 ms on the server, takes one slice from the hoppers in the batch's own
ore-to-flux proportion, and merges it into the basin's stack, which carries the batch's ore and
flux fractions as attributes (`Burden.Write`) wherever it goes. The tooltip reads them back, and so
does the "would make burden at N% flux" preview `GetBlockInfo` prints while a player looks at the
machine. Both grade that number through the same flux bands, `BurdenMakerValues.BurdenProfiles`,
which come from the mod's [config](Config-System), so a server can retune what counts as under-,
standard- or over-fluxed without a recompile. `ApplyPose`, the drain helpers and the load and take
helpers are omitted here; a constructable block draws no mesh of its own once built, so one
animation clip must always be running for it to be visible at all, and `ApplyPose` is what keeps
the right one running.

## The tests

A machine like this is worth testing at three levels, and the sample has one test class for each.

`BurdenmakerCellTests` checks the whole `Classify` table above at all four facings with no world at
all - a pure function over three `BlockPos`es and an angle needs nothing else, which is the payoff
for keeping the classification static. `BurdenmakerInteractionTests` and `BurdenmakerTests` use
[Testing Harness](Testing-Harness)'s `TestWorld` to drive loading, taking and the gate through a
real block entity, with no game running. `MaterialRoleSeedTests` (under `tests/Invariants`) checks
that `MaterialRoleSeeds` - the headless stand-in for
`assets/burdenmaker/config/materialroles.json`, which the harness has no asset pipeline to load -
mirrors that file entry for entry, so a role typo in the shipped JSON cannot pass silently.

## The checks the smoke prints

Tests cover what your code does. The rest of a mod - whether a code it names resolves, whether a
string it needs was shipped - is only decidable once the game has loaded everything, which is what
the smoke boot is for. Booting `exmod smoke` with both `twintubblower` and `burdenmaker` loaded (see
[Getting Started](Getting-Started) section 7) runs every content check in [Checks](Checks) against
both domains.

Two of them matter most for a mega-block. `MultiblockCodes` confirms the footprint's own filler
cells resolve once every def is injected. `LangCoverage` confirms every `block-` and `blockdesc-`
key the def implies was actually shipped; a missing one shows the player its raw key in game, and
nothing in the build would otherwise catch it. A clean run reads `0 error(s)` on every line, for
both domains, the same as any other mod in the family.
