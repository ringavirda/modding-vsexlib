# Multiblock Structures

A multiblock is a machine bigger than one block. Two quite different things go by that name, and
exlib carries both.

The first is a single block with an oversized model: a blower, a boiler, a press whose shape sticks
out into the cells around it. The game gives such a block collision and a selection box in its own
cell only, so the player walks straight through the half of the machine that is only a model, and
can build in the space it appears to occupy. exlib fills the rest of the shape with an invisible
**filler** block that has real collision, and reroutes everything a player can do to a filler cell
back to the block that owns it. A filler cell can also carry a behaviour of its own, so the corner
of a machine can be where its power port or its pipe connector lives.

The second is a machine the player builds out of ordinary blocks, to a design you publish: a blast
furnace of firebricks around a core, or the `SmokeStack` sample's 72-cell chimney. There you need to
know whether the player got it right, to keep the machine from running while it is wrong, and to
show what is missing rather than leave the player counting bricks. exlib gives you a base block
entity - a block entity being the object the game attaches to a placed block to hold its state and
run code - that re-checks the pattern on a timer, calls you on each transition between complete and
incomplete, and draws a hologram of the missing blocks when the player ctrl+shift+right-clicks.

Take either without the other, or both at once. Both start from a shape, drawn once as ASCII art in
C# or in JSON, and neither asks you to think about rotation: you author facing north and exlib
rotates the layout to the angle the block was placed at.

## One grid, three uses

You draw a shape as rows of characters, one character per world cell, and say in a legend what each
character means. Both builders on this page - `MultiblockLayoutBuilder.Layer`/`Slice`/`Face` for the
pattern a player must build, and `FillerLayoutBuilder.Layer`/`Slice`/`Face` for a mega-block's
footprint - draw over one core, `ExpandedLib.Structures.CellGrid`, so what you learn here serves
both.

The three verbs are three ways to cut the same box; pick whichever makes your shape easiest to read.
`Layer` is a floor plan, rows +Z and columns +X, one grid per Y level, and it suits anything wide and
low. `Slice` is a front elevation at a fixed X (rows -Y from the top, columns +Z) and `Face` the
same at a fixed Z, looking along -Z (rows -Y from the top, columns +X); both suit anything tall,
where a floor plan would mean a dozen near-identical grids. One layout may mix them: draw the floor
with `Layer` and add a chimney with `Slice` in the same builder.

```csharp
.Slice(0, """
           M##
           0##
           """)
```

draws a two-cell-tall, three-wide elevation at X=0: the top row's leftmost cell is `M`, the bottom
row's leftmost is the principal (`0`), the cell the block itself stands in and the origin every
other cell is measured from. `SymbolLegend<T>` is the matching legend core: it maps a grid's symbols
to whatever a builder resolves them into. It takes one policy for a symbol mapped twice - `Throw`
for a code-first layout, where a duplicate is a mistake, `Replace` for a filler footprint, where a
later declaration is meant to win - and it reports a symbol declared but never drawn, which is how a
typo in a grid surfaces at build time rather than in game.

## The filler system

A "mega-block" is one block whose model spans more than its own cell. The filler system gives it
collision everywhere it looks solid, by placing invisible `exlib:structurefiller` blocks in the
other footprint cells. They render nothing, they are solid, they are hidden from the handbook, and
every player-facing operation on one is forwarded to the block that owns the footprint.

### Declaring the footprint

The footprint is a list of cell offsets, and the block that owns it is the **principal**, or
controller. exlib reads that list through `IFillerHost`, which you rarely implement yourself:
`BlockFilledMegastructure` already does, by reading the block's `Attributes["fillerOffsets"]`, so a
block deriving from it only has to supply that attribute. Supply it in JSON, or code-first from an
`ExBlockDef` builder with `FillerOffsets(IEnumerable<FillerCellSpec>)`, as `BlockBoilerCornish`
does:

```csharp
.FillerOffsets(
  StructureFootprint.Layout(f =>
    f.Origin(-1, -5)
      .Slab('_', BlockFacing.DOWN)
      .Slab('M', BlockFacing.DOWN)
      .Solid('I')
      .Port('S', BlockFacing.UP, "pipe")
      .Port('E', BlockFacing.EAST, "pipe")
      .Layer(0, """
      # # E
      # # #
      ...
      """)
  )
)
```

`allowAttach` (default `false`) decides whether other blocks may attach to that filler cell. Leave
it off for the body of a machine, so a player cannot hang a ladder off thin air; turn it on where
the shape is a platform or a wall you want treated as real.

### Placing and removing fillers

Something still has to put the fillers in the world when the machine is placed and take them out
when it is broken. `StructureFillers` is that helper. It resolves the declared offsets against a
position and a placement angle - you author them in the block's north orientation and it rotates
them for you - and then places, checks or clears the cells.

```csharp
public static class StructureFillers
{
    public static AssetLocation FillerCode { get; set; }   // default "exlib:structurefiller"

    public static List<FillerOffset> ReadOffsets(JsonObject? offsetsNode);
    public static List<FillerCell> FootprintCells(IFillerHost principal, BlockPos principalPos, int angle);
    public static bool CanPlace(IWorldAccessor world, IEnumerable<FillerCell> cells);
    public static void PlaceFillers(IWorldAccessor world, BlockPos principalPos, IEnumerable<FillerCell> cells);
    public static void RemoveFillers(IWorldAccessor world, BlockPos principalPos, IEnumerable<FillerCell> cells);
}

public readonly struct FillerOffset { public Vec3i Offset { get; } public bool AllowAttach { get; } }
public readonly struct FillerCell   { public BlockPos Pos { get; } public bool AllowAttach { get; } }
```

The flow in the controller block is two calls at placement and one at breaking. Check the footprint
is clear before you place anything, so a half-placed machine cannot exist, and clear the fillers on
break, so the player is not left with invisible solid cells in the air:

```csharp
// In TryPlaceBlock: bail if the footprint isn't clear.
var cells = StructureFillers.FootprintCells(this, blockSel.Position, placeAngle);
if (!StructureFillers.CanPlace(world, cells)) { failureCode = "notenoughspace"; return false; }
// ...place the controller block, then:
StructureFillers.PlaceFillers(world, blockSel.Position, cells);

// In OnBlockBroken: clear the fillers linked to this controller.
StructureFillers.RemoveFillers(world, pos, cells);
```

`PlaceFillers` and `RemoveFillers` are server-side. `RemoveFillers` only clears cells actually
linked to the given principal, so two mega-blocks standing shoulder to shoulder do not tear holes in
each other when one is broken.

> **Per-cell collision gotcha.** The filler block must declare `sidesolid: true` (and a real
> collision box) for the engine to treat each cell as solid. Without it the mega-block has only
> single-cell collision regardless of fillers.

### Per-cell interactions (optional)

By default a click anywhere on the footprint is a click on the controller, which is what you want
most of the time. When the cells should mean different things - a hatch at the front, a hopper on
top - the controller implements `IFillerInteractionTarget`. The filler then forwards the click along
with the position of the cell that was clicked, and you decide from there:

```csharp
public interface IFillerInteractionTarget
{
    bool OnFillerInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection principalSel, BlockPos clickedCell);
    bool OnFillerInteractStep(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection principalSel, BlockPos clickedCell);
    void OnFillerInteractStop(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection principalSel, BlockPos clickedCell);
    WorldInteraction[] GetFillerInteractionHelp(IWorldAccessor world, BlockSelection principalSel, IPlayer forPlayer, BlockPos clickedCell);
}
```

`BlockStructureFiller` and `BlockEntityStructureFiller`, the invisible block and its block entity,
reroute break, pick, drops, sounds, HUD info and interaction help to the controller with no code
from you. The block entity stores the `Principal` position link, which is how a filler knows whose
cell it is, plus optional network-port config (`PortFace`, `PortNetworkType`). That last pair is
what lets a filler cell expose a [network](Block-Networks) connector on the controller's behalf, so
a pipe or a power shaft joins the machine at the cell where its model actually has a socket, not at
the controller's own cell.

## Completion monitoring: `BlockEntityMultiblockStructure`

The second half of the page starts here, independent of everything above. When the machine is
something the player assembles out of ordinary blocks - a blast furnace, a cowper stove, a bessemer
control - subclass `BlockEntityMultiblockStructure`. It gives you the **form** alone: a monitor tick
that re-checks the pattern every few seconds and notices when it becomes complete or is breached,
and the answer published as readiness through `IProductionReadiness`, which anything that should run
only on a finished machine can ask. The three abstract members are what only you can answer: which
way the structure faces, and what to tell the player in each state.

```csharp
public abstract class BlockEntityMultiblockStructure : BlockEntity, IProductionReadiness
{
    public bool StructureComplete { get; protected set; }
    protected virtual int CompletionTickMs { get; }          // monitor interval, default 3000ms
    protected virtual bool CanRunProduction { get; }         // production runs only while complete
    protected virtual bool StopsProductionOnStructureLost { get; }  // false keeps a breached machine ticking

    public virtual void Interact(IPlayer byPlayer);          // toggle the build-outline projection

    // You implement these:
    protected abstract void UpdateStructureRotation();
    protected abstract string GetIncompleteMessage(int missingCount);
    protected abstract string GetCompleteMessage();

    // Optional hooks:
    protected virtual void OnStructureLost();                // complete -> incomplete
    protected virtual void OnStructureCompleted();           // incomplete -> complete
    protected virtual BlockPos GetGlobalPos(int localX, int localY, int localZ);

    protected void SetStructureAngle(int angle, int initAngleOffset = 0);   // canonical UpdateStructureRotation body
}
```

### From JSON only

A machine that is just a designed shape - no production tick, no per-cell behaviour - needs no C# at
all. The classes above are registered under names the game's own `blocktypes/` JSON can ask for.
Three rungs, shortest first:

1. **Zero-config**: `"class": "ExFilledMegastructure"` plus `"entityClass": "ExMultiblock"` and the
   `MultiblockStructure` behaviour give per-cell collision, the completion monitor and the
   incomplete/complete messages out of the box.
2. **Declarative**: an `attributes.multiblockLayout` ASCII grid, the JSON twin of
   `MultiblockLayoutBuilder`, states the shape in one place. `fillerOffsets` is derived from it, as
   every drawn cell but the principal's own, unless you declare your own. A structure the player
   builds out of real blocks rather than fillers must declare `"fillerOffsets": []`. Without that, a
   derived footprint reserves every drawn cell with an invisible filler and the player can place
   nothing where the design says to.

```json
{
  "class": "ExFilledMegastructure",
  "entityClass": "ExMultiblock",
  "behaviors": [{ "name": "MultiblockStructure" }],
  "attributes": {
    "multiblockLayout": {
      "origin": [1, 0],
      "legend": { "C": "mymod:kiln-core", "B": "mymod:kiln-brick" },
      "layers": [["BBB", "BCB", "BBB"], ["B.B", "...", "B.B"]],
      "core": "C"
    }
  }
}
```

Orientation follows the block's own `side` or `orientation` variant, the interchangeable segment of
a block code that says which way a placed block faces. The messages are your domain's own
`multiblock-<blockpath>-incomplete` and `-complete` lang keys when you declare them, else exlib's
own. A malformed `multiblockLayout` logs one Error naming the block and the problem, and the
structure simply never completes; it does not throw at chunk load, so one bad grid cannot take a
world down.

3. **The explicit API**: for anything the grid cannot say - roles, connectors, oriented parts, a
   production tick - drop to the code-first `ExBlockDef` builder above, or subclass
   `BlockEntityMultiblockStructure` yourself.

## Multiblocks that also produce: `BlockEntityMultiblockMachine`

Running a production tick is a separate choice from being a multiblock, and it is the reason there
are two base classes rather than one. A stone archway is a multiblock that produces nothing. A
furnace is both. Subclass `BlockEntityMultiblockMachine` when you want both: it hosts the same
[`BEBehaviorProductionMachine`](Production-Machines) every other machine in the framework runs,
registers the tick on load only when the structure is already complete, and lets the monitor tick
start and stop it as the structure is finished or breached. So a machine cannot run on a design the
player has half dismantled, and you never write that check. A structure that only has to be built
subclasses the form above and carries no tick at all.

```csharp
public abstract class BlockEntityMultiblockMachine : BlockEntityMultiblockStructure
{
    protected virtual int ProductionTickMs { get; }          // tick interval, default 1000ms
    protected virtual bool AutoStartProduction { get; }      // register on load if already complete
    protected virtual int MaxAwayCatchupSteps { get; }       // 0 disables the unloaded-time replay

    protected abstract void OnProductionTick(float dt);
    protected virtual void OnIdleProductionTick(float dt);
}
```

`ProductionTickMs` is how often your work runs; raise it for anything that need not look smooth.
`AutoStartProduction` decides whether a machine found complete when its chunk loads starts working
at once, which suits a furnace but not a machine that stays dormant until a player switches it on.
`MaxAwayCatchupSteps` caps the replay of time that passed while nobody was there, so a player
returning after a week gets a plausible amount of output rather than a week of it; 0 turns the
replay off. A minimal subclass:

```csharp
[BlockEntityRegister]
public class BlockEntityBlastFurnace : BlockEntityMultiblockMachine
{
    protected override void UpdateStructureRotation()
        => SetStructureAngle(ExOrientation.AngleFromSide(Block.Variant["side"]));

    protected override string GetIncompleteMessage(int missingCount)
        => Lang.Get("siex:blastfurnace-incomplete", missingCount);

    protected override string GetCompleteMessage()
        => Lang.Get("siex:blastfurnace-complete");

    protected override void OnProductionTick(float dt) { /* smelt while complete */ }
}
```

### How completion is wired

The pattern itself - which cell must hold which block - is not exlib's format. It is a vanilla
**`multiblockStructure`** JSON definition, the game's own, referenced by your block; the layout
builders on this page write one for you. Your job is to tell exlib which way it faces, and that is
all `UpdateStructureRotation` is for. `SetStructureAngle` is its canonical body: it loads that JSON,
calls the engine's `InitForUse` at the angle you give, and clears any stale projection so a rotated
machine does not keep showing the old one. From there the monitor tick re-checks completeness every
`CompletionTickMs` and calls `OnStructureCompleted` or `OnStructureLost` only on a transition, never
on every tick, so those two are safe places to play a sound or send a message.

> **`GetGlobalPos` / `_currentAngle` invariant.** The base `GetGlobalPos(angle)` is equivalent to
> `InitForUse(angle)`, so the angle you pass to `SetStructureAngle` must match the structure's
> `_currentAngle`. A mismatch shows up as the build outline appearing rotated 180 deg.

## The build-outline behaviour

A player who has mis-built a structure needs to see where, and the answer is a hologram of the
missing blocks, toggled on ctrl+shift+right-click. That toggle lives in
`BlockBehaviorMultiblockStructure`, a `BlockBehavior` rather than a base class - a component listed
in the block's definition, so it works whatever your block already derives from. List it **before**
any other consumer of right-click, or that consumer swallows the click first:

```jsonc
"behaviors": [ { "name": "MultiblockStructure" } ]
```

It calls back into the block entity's `Interact`, which re-checks completeness and then either shows
the outline of the missing parts or, if the structure has since been finished, clears it. The
highlighting is a reimplementation rather than a call into vanilla's, and the reason is a crash:
vanilla's `HighlightIncompleteParts` throws `IndexOutOfRange` when a wanted `blockNumbers` code
resolves to no block at all, taking the client down. The base class falls back to a neutral tint.

> **`blockNumbers` validity.** Every offset in your `multiblockStructure` definition needs a
> `blockNumbers` entry that resolves to at least one real block, or the build outline (and vanilla
> highlighting) misbehaves.

## Orientation-checked parts

Many vanilla blocks carry their facing in their code: a slab is `brickslabs-fire-south-free`, and
the `south` is which way it points. That makes a trap easy to fall into. Vanilla rotates a
structure's **offsets** through `InitForUse(angle)` but never its **codes**, so a layout asking for
`brickslabs-fire-south-free` demands a *south*-facing slab at every structure angle - wrong three of
the four ways the player can place the machine. The usual escape is `-*`, "any rotation", and it is
worse than it looks: a structure built with it reports itself complete while its walls still have
visible gaps, because a slab facing outward satisfies the check as happily as one facing in.

exlib closes that off. A legend code containing a whole horizontal side segment (`north`, `south`,
`east`, `west`, or the letters `n`, `s`, `e`, `w`) is **orientation-checked**: the facing it demands
rotates with the structure. You keep authoring in the north-default frame, and all four placements
work.

```csharp
.Legend('i', "game:brickslabs-fire-south-free")  // south at angle 0, east at 90, ...
.Legend('-', "game:brickslabs-fire-up-free")     // vertical - a Y rotation cannot move it
.Legend('#', "game:claybricks-good-fire")        // no facing - untouched
.LegendAnyFacing('x', "mod:thing-north")         // opt out: accept the literal code at any angle
```

How it works, and why it costs almost nothing:

- `MultiblockLayoutBuilder` records **which dash-segment** of each oriented code is the facing word, into
  a sibling attribute `attributes.multiblockFacings`. It is a *sibling* rather than a member of
  `multiblockStructure`, because that object is deserialised by vanilla's own `MultiblockStructure` and
  must stay exactly its schema.
- `MultiblockFacings.Rotate` swaps that segment for the structure-rotated one
  (`ExOrientation.RotateSideWord`, the string counterpart of `RotateFacing` and sharing its convention:
  a part authored `north` reads as the side whose `AngleFromSide` equals the structure's angle).
- `BlockEntityMultiblockStructure.IncompleteBlockCount` replaces vanilla's `InCompleteBlockCount` and
  routes every wanted code through that rotation. The build outline shares the same resolver, so the
  tint and the count can never disagree - and an oriented slot now resolves to a **real, correctly-facing
  block**, so the outline colours from the right variant and the missing-blocks report names it.

Three things to know:

- **Only whole segments count.** `westward` or a `-we-` axis token is not mistaken for a facing, and the
  *last* matching segment wins, because block codes put the orientation at the end.
- **Codes are keyed in full domained form.** `AssetLocation.ToShortString()` elides `game:`, so a table
  keyed the way an author typed it would never match a vanilla block at runtime.
- **Trapdoors cannot be checked this way.** `game:trapdoor` keeps its facing and open/closed state in its
  *block entity*, not its code, so no code match can see it. Slabs, stairs, doors and `cokeovendoor` all
  carry theirs in the code and work fine.

A layout that declares no oriented part emits no attribute at all, and none of this machinery runs
for it.

## Cell roles

A structure's code often needs to find a particular cell of the pattern: where the tuyeres go, where
to spawn smoke, which cell takes fuel. Hard-coded coordinates break the moment the machine is
rotated or the layout is edited. A `CellRole` is the alternative: an open string key naming what a
cell is for, attached to a glyph in the layout and read back as world positions, already rotated.
exlib declares no roles of its own; mint the ones your machine needs with `CellRole.Of`, mark a
glyph with `MultiblockLayoutBuilder.Role`, and read them through
`BlockEntityMultiblockStructure.CellsWithRole`:

```csharp
public static readonly CellRole Tuyere = CellRole.Of("tuyere");
// ...
.Role('t', Tuyere)
// ...
IReadOnlyList<BlockPos> tuyereCells = CellsWithRole(Tuyere);
```

Pass `single: true` to `CellRole.Of` when a layout may give the role at most one cell, such as a
single fuel hatch. The arity is then enforced at build time, and your code may call
`CellsWithRole(role).Single()` without guarding against a second one.

## Related pages

- [Production Machines](Production-Machines) - the tick lifecycle `BlockEntityMultiblockMachine` builds on, and how to host it when your base class is already spent.
- [Block Networks](Block-Networks) - how a structure joins a pipe or power graph. One that is also a network node must call `AddNode`/`RemoveNode` itself.
- [Code-First Definitions](Code-First-Definitions) - the `ExBlockDef` builder that carries `MultiblockLayout` and `FillerOffsets`.
- [Helpers & Renderers](Helpers-and-Renderers) - `ExOrientation` for the rotation math, `SurfaceRenderer` for fluid surfaces.
