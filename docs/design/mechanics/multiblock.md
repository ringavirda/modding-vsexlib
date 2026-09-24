# Multiblock & Filler Structures
**Status** live   **Mod** exlib (the whole system; every mod authors layouts against it)
**Owns** the ASCII layout DSL (`Origin` / `Core` / `Legend` / `Layer` / `Slice` / `Face`, the `'.'`/`' '`/`'O'` glyph rules, `@(a|b)` alternation) and its JSON form, the origin-is-the-negation-of-the-core rule, oriented-part rotation and the trapdoor caveat, cell roles (what a cell is for, attached to glyphs), connector marks, the invisible-filler footprint system (per-cell collision, interaction/break/info rerouting, `allowAttach`, partial collision boxes), behaviour-capable filler cells, the declarative filler port and the connector-versus-node choice, the completion/projection machinery, and the rule that a filler cell is a graph node exactly when it declares a membership.
**Depends on** [mp-energy](mp-energy.md) (the `BEBehaviorMPFillerPort` a hosted cell carries, and the rolling mill's drive-line cells - the canonical case of the graph-node rule), [conventions](../conventions.md) (block / megablock / multiblock vocabulary), [workbench/layouts.md](https://github.com/ringavirda/modding-vsexmods/blob/main/workbench/layouts.md) (the layout scratchpad, in exmods)

---

## Role

exlib solves two separate problems:

- **Megablock** - one block that renders across many cells. The engine resolves collision per cell, so
  the surrounding cells must be filled or the player walks through a boiler. Invisible filler blocks
  reserve the volume and forward every player-facing operation to the principal.
- **Multiblock** - a structure the player builds by hand in a specific shape (a furnace, a boiler
  chamber), guided by an in-world projection of what is missing. A declared cell -> block-code table that
  a monitor tick checks against the world.

A block can be both, and several furnace cores are: a reserved footprint of its own plus a layout of
player-placed cells around it (`conventions.md`, section Block-size vocabulary). It is a choice, not a
progression - the Cornish boiler is a megablock and nothing else, because its own art carries the masonry
([Cornish boiler](https://github.com/ringavirda/modding-vsexmods/blob/main/docs/design/machines/boiler-cornish.md)).

Both DSLs replace hand-typed coordinate arrays with a drawing, validated at load. A duplicate offset, a
`w` with no `blockNumbers` entry, or an origin off by one is reported at load rather than failing silently
as a structure that never completes.

---

## How it works

### The drawing model

`CellGrid` turns rows of glyphs into `LayoutCell(X, Y, Z, Symbol)` for one `GridPlane`. Both builders draw
through it (`ThreePlaneDraw`), one grid per plane kind. Three projections:

| Grid call | `GridPlane` | Grid = | Rows run | Columns run | Origin pair |
|---|---|---|---|---|---|
| `Layer(y, grid)` | `Horizontal` | one Y level (floor plan, top-down) | `+Z` | `+X` | `(xLeft, zTop)` |
| `Slice(x, grid)` | `SliceX` | one X level (side elevation) | `-Y` (down) | `+Z` | `(zLeft, yTop)` |
| `Face(z, grid)` | `FaceZ` | one Z level (front elevation, looking along `-Z`) | `-Y` (down) | `+X` | `(xLeft, yTop)` |

Glyph rules, identical in all three (`GridOptions` defaults):

| Glyph | Meaning |
|---|---|
| `.` | empty cell - advances the column, emits nothing |
| space / tab | separator, ignored entirely (grids are spaced for readability) |
| anything else | a cell carrying that legend symbol |

Leading and trailing blank lines are trimmed (`CellGrid.Add`) so a raw C# string literal does not shift Z;
a blank line in the middle is a genuine empty row. Two cells drawn at one position in one plane kind throw.

`Face` covers thin-in-Z structures whose face lies in the X-Y plane (the flywheel disc, the shear), which
neither of the other two can draw in-plane.

`StructureLayout.Parse` is the same horizontal read as a standalone call; neither builder uses it.

### The multiblock DSL

`MultiblockLayoutBuilder`, reached from `ExBlockDef.MultiblockLayout(...)`.

```csharp
.MultiblockLayout(s =>
  s.Origin(-3, -1)
   .Core('C')
   .Legend('#', "game:refractorybricks-good-tier*")
   .Legend('C', "iiex:furnace-blastcore-*")
   .Legend('c', "@(air|coalpile)")
   .Layer(0, """
             . . # . # .
             # # # C # #
             . . # . # .
             """))
```

- **`Origin(xLeft, zTop)`** - where the top-left glyph of every grid sits. Cell `(col, row)` of a `Layer`
  becomes `X = xLeft + col`, `Z = zTop + row`. Defaults to `(0, 0)`.
- **`Core(symbol)`** - names the anchor glyph, the block the player places; `Build()` checks that it
  lands on `(0,0,0)` (see below).
- **`Legend(symbol, code)`** - maps a glyph to a block code, wildcards allowed. `'.'` and space are
  rejected as symbols, and a glyph may be declared only once. Codes go through oriented-part detection -
  see below.
- **`LegendAnyFacing(symbol, code)`** - same, but never rotates the code's side segment.
- **`Role(symbol, role)`** - optional; marks what a glyph's cells are for - see Cell roles below.
- **`Connector(symbol, faces)`** - optional; demands a network connector on the named faces - see
  Connector marks below.
- **`Layer(y, grid)`** / **`Slice(x, grid)`** / **`Face(z, grid)`** - one grid each; any order.
- **`@(a|b)` alternation** - not an exlib feature. It is vanilla `WildcardUtil` regex-alternation syntax
  passed through untouched; matching happens in `WildcardUtil.Match` inside
  `BlockEntityMultiblockStructure.IncompleteBlockCount`. Inside `@(...)` the text is a regular expression
  over the path, so a wildcard there is `.*`, never `*`. Used for fuel cells (`VanillaCodes.CoalBed`,
  iiex's `IiexCodes.ChargeShaft` and `HearthCell`) and brick families (`VanillaCodes.AnyBricks`,
  `VanillaCodes.RefractoryOrFire`).

`Build()` assigns each distinct legend code a private block number `w` starting at 1, in declaration
order, then feeds every parsed cell to `MultiblockBuilder`. An unknown symbol throws naming its coordinate.

> Numbering is per code, not per glyph (`MultiblockLayoutBuilder.Build`), so several glyphs may share one
> code - the thing cell roles depend on. It must stay per code because `blockNumbers` is a JSON object
> keyed by code: numbering per glyph would emit two numbers for two glyphs on one code but only one entry,
> the first silently overwritten. Every cell holding the lost number would then stop being required by
> `IncompleteBlockCount` (`WantedCodeAt` returns null and the cell is skipped) and make vanilla's own
> `InCompleteBlockCount` throw `KeyNotFoundException` (it does a bare `BlockCodes[w]`).

`MultiblockBuilder` is the validating layer:

| Guard | Throws on | Where |
|---|---|---|
| duplicate cell | two offsets at the same `(x,y,z)`, across every grid | `At` (`ArgumentException`) |
| unresolved block number | an offset whose `w` has no `blockNumbers` entry | `Build` (`InvalidOperationException`) |
| inverted `Fill` range | `x2 < x1` etc. - would silently emit zero cells | `Fill` (`ArgumentException`) |

The emitted JSON has the hand-written schema, so vanilla's `MultiblockStructure` deserialiser reads it
unchanged. The game treats `offsets` as an unordered set and `w` as a private index, so the generated table
is behaviour-identical to any hand ordering of the same cells.

### The JSON form

A block of class `ExFilledMegastructure` (`BlockFilledMegastructure`) may carry its layout as
`attributes.multiblockLayout` instead: `origin` as `[a, b]`, `legend` as one-character keys to codes,
`layers` as arrays of row strings (the array index is Y), and an optional one-character `core`.
`JsonMultiblockLayout.Resolve`, run from `OnLoaded`, replays it through the same `MultiblockLayoutBuilder`,
writes `multiblockStructure`, and, when the block declares no `fillerOffsets`, derives a plain footprint
from every drawn cell but the origin. A malformed layout logs an error naming the block, and the structure
never completes. The JSON form has no `Slice`, `Face`, `Role` or `Connector`, and writes no
`multiblockFacings`. Its block entity can be `ExMultiblock` (`BlockEntityMultiblock`), which takes its
angle from the `side` or `orientation` variant and its messages from
`<domain>:multiblock-<path>-incomplete|complete`, falling back to exlib's own.

### Origin is the negation of the core

> Origin is the **negation** of the core glyph's `(col, row)`, so the core lands on the anchor's own
> `(0,0,0)`. Getting this wrong builds the whole structure offset from the block you placed.

In the cold blast furnace, `C` sits at column 3, row 2 of layer 0, so `Origin(-3, -2)`. In the heating
furnace `C` is at column 6, row 2, so `Origin(-6, -2)`.

Both DSLs check the rule. `FillerLayoutBuilder.Build()` throws when the principal glyph `'O'`/`'0'` is
drawn anywhere but the origin. `MultiblockLayoutBuilder.Build()` checks it when the layout names its
anchor with `Core(symbol)`: it throws when the core glyph has no `Legend`, is never drawn, or lands off
`(0,0,0)`, and the message names the `Origin` that would be right. A layout that declares no `Core` is not
checked; every shipped drawn layout declares one.

### Oriented parts

Vanilla's `MultiblockStructure` rotates a structure's offsets through `InitForUse(angle)` but never its
codes, so a cell wanting `brickslabs-fire-south-free` demands a south-facing slab at every structure angle -
wrong three times out of four. A layout writing `-*` instead accepts any rotation, which lets a "completed"
furnace still have visible gaps in its walls.

1. Detection. `MultiblockLayoutBuilder.AddLegend` records the index of every whole dash-separated segment
   of the code's path that a Y rotation moves (`FindOrientationSegments`, through
   `ExOrientation.RotatesUnderY`): a horizontal side word, full (`north`) or a single letter (`n`). Every
   such segment is recorded, because block codes put the orientation at the end and a material name
   earlier could otherwise shadow it. Only whole segments count, so `westward` is never mistaken for a
   facing. A multi-letter network token (`ns`, `nswe`) is refused outright - see Connector marks.
2. Emission. The table `code -> segment indices` rides in a sibling attribute
   `attributes.multiblockFacings`, not inside `multiblockStructure` - that object is deserialised by
   vanilla and must stay exactly its schema (`ExBlockDef.MultiblockLayout`). It is keyed by the full
   domained code form (`AssetLocation.ToString()`), because `ToShortString()` elides `game:` and would
   never match a vanilla block at runtime. The authored facing itself is not stored - it is already in the
   code, the one copy that cannot drift.
3. Check time. `MultiblockFacings.Rotate(code, angle)` swaps each listed segment for its rotated token
   (`MultiblockFacings.RotateSegments`, `ExOrientation.RotateOrientationToken`). Full words stay words,
   single letters stay letters; vertical `up`/`down` never move under a Y rotation, so they are never
   recorded. A listed index that is out of range or no longer names an orientation leaves the code
   unchanged.

The angle used is `_structureInitAngle` = `_currentAngle + initAngleOffset`, i.e. the angle actually handed
to `InitForUse` - not `_currentAngle` (`BlockEntityMultiblockStructure.SetStructureAngle`,
`WantedCodeAt`). The Bessemer converter control's `+180` frame is the case where using `_currentAngle`
would face every part backwards.

A layout that declares nothing oriented emits no attribute and gets `MultiblockFacings.None`.

Live users: most shipped layouts - taps, charge doors, hearths, hoppers, fire-brick slabs and sealing
bricks named by side.

### Cell roles

A layout records what block may occupy a cell, not what the cell is for, and at runtime only the code
string survives - the authored glyph is gone. A role carries that fact, and `Role(glyph, role)` puts it in
the drawing.

```csharp
.Legend('a', "game:air")            // what may occupy the cell
.Legend('A', "game:air")            // same code...
.Role('A', FurnaceCellRoles.Flue)   // ...different role
```

A `CellRole` is a string key, minted with `CellRole.Of(key, single)` by the mod that owns the machine;
equality is by key alone. exlib declares one, `CellRoles.NoSnow`. iiex's `FurnaceCellRoles` declares the
furnaces' nine: `Chargeable`, `Firebox`, `Tuyere`, `GasOutlet`, `MetalTap`, `SlagTap`, `Pool`, `Flue` and
`Damper`. The key is emitted into the block's attributes, so its spelling is fixed once it ships.

Roles attach to glyphs, never to codes. `game:air` is simultaneously the vent shaft, the flue column
and the tap alcove in shipped layouts, so "the air cells are the flue" is not expressible. The author gives
each role its own glyph and several glyphs may share one code - the cold blast furnace's two tuyere glyphs
`Y` and `T` are one tuyere code. Several glyphs may equally share one role.

One glyph may carry several roles, because a cell can be two things at once. siex's hot blast furnace
marks its crucible course `p` both `Chargeable` and `Pool`: burden rests on it while the furnace runs and
metal freezes onto it when the furnace is put out. A cell holds exactly one glyph, so two overlapping roles
cannot be split across two of them. Distinct roles accumulate rather than overwrite (restating one is
idempotent), so last-writer-wins drift is absent by construction.

`CellsAccepting` answers "which cells would take this block", the right question when the caller has a
block in hand, but it couples the caller to a block code that a retype can move out from under it. A role
says what the layout knows the cell is for, independent of what fills it. Both stay.

exlib's own role, `CellRoles.NoSnow`, goes on the block weather snow would lie on or turn into, never the air
above it: the smokestack's floor under its open column, the cores of the cold blast furnace, the cupola, the
heating furnace and the crucible furnace, and the hearth cells of the first two. On the server a structure
marks those cells in `NoSnowCells`, a thread-safe registry the snow simulation reads off the main thread, from
`SetStructureAngle` (placement, a wrench turn) and from `Initialize` (a structure loaded from the save), and
unmarks them in `OnBlockRemoved` and `OnBlockUnloaded`; a server's load start empties it
(`ExWorldState.ResetOnLoad`), never a `Dispose`. `NoSnowPatch` postfixes `Block.AllowSnowCoverage` to false
and `Block.GetSnowCoveredVariant` to the block itself at a marked cell. A block class that overrides either
without calling the base escapes the patch, and a snow layer that settled before the mark stays.

| | |
|---|---|
| **Authoring** | `MultiblockLayoutBuilder.Role(char, CellRole)` |
| **Roles** | `CellRole`, a record struct keyed by string, minted with `CellRole.Of(key, single)` by the mod that owns the machine; exlib's own is `CellRoles.NoSnow`, the furnaces' are `FurnaceCellRoles` in iiex |
| **Arity** | `CellRole.Of(key, single: true)`, read through `CellRoles.IsSingleCell` and enforced when the layout builds (`MultiblockLayoutBuilder.ValidateRoleArity`) |
| **Emission** | sibling attribute `attributes.multiblockRoles` (`ExBlockDef.MultiblockLayout`), role key to authored offsets |
| **Reading** | `MultiblockCellRoles.FromAttributes`, `MultiblockCellRoles.CellsOf(role)` |
| **Runtime** | `BlockEntityMultiblockStructure.CellsWithRole(role)` to world `BlockPos`, cached; `LocalCellsWithRole` for authored offsets |
| **Snow** | `NoSnowCells` (`Mark`, `Unmark`, `IsMarked`), `NoSnowPatch` |

### Connector marks - what a cell must open onto

A third sibling. A cell marked `Connector` is satisfied only by an occupant exposing a network connector on
each of the named faces; a code match alone is not enough. It exists because a network node re-picks its
own orientation from its neighbours, so pinning its variant in the legend states a fact the node is free
to contradict - the structure can be left uncompletable, or a complete one broken when the player plumbs
something nearby.

| | |
|---|---|
| **Authoring** | `MultiblockLayoutBuilder.Connector(char, params BlockFacing[])`, authored in the north frame |
| **Emission** | sibling attribute `attributes.multiblockConnectors`, face letter -> authored offsets, omitted when nothing is marked |
| **Reading** | `MultiblockConnectors.FromAttributes`, `OutwardFacesAt(authoredOffset)` - total and never-throwing, as `MultiblockCellRoles` is |
| **Runtime** | rotated by `_structureInitAngle` inside `IncompleteBlockCount`, asked through `INetworkMember.HasConnectorAt`; `ConnectorFacesAt(worldCell)` answers the same set for a report |
| **Refusal** | `MultiblockLayoutBuilder.Legend` throws on a code pinning a multi-letter token; `PinnedNetworkNodes` (ExpandedLib.Testing) catches the single-letter cases per mod |

Satisfied by a superset: a passthrough wearing `ns` answers a demand for north, so a legitimate re-pick does
not break a standing structure. An occupant that is not on a network answers nothing, so a brick dropped
into a connector cell cannot satisfy the mark. The rig mirrors the same rule - `StructureRig.Missing` counts
by code *and* connector, because a rig that counted by code alone would raise a footprint the machine then
refuses, and `Complete()` would throw "0 of N cells unsatisfied".

Live users: the tuyere cells of the cold blast furnace, the cupola and the hot blast furnace.

The rule for what earns a role: a role exists only where code asks the layout "where are my X cells?".
A block that finds its own core - a charge door, a hopper, a hearth, a filler, the core itself - needs none,
because that lookup runs the other way, through `FindAnchorOwning<T>` or a `MultiblockAnchorLink<T>`.
Absent for that reason: `Core`, `Filler`, `ChargeDoor`, `Hopper`, `Hearth`, and `ShaftCentre` (a geometric
point, not a cell set). The player-built chimney needs no `StackBase` either - it starts at the highest
`Flue` cell.

Rotation is inherited, not reimplemented. The table stores the offsets the author drew, in the
north-default frame. Vanilla's `InitForUse` builds `TransformedOffsets` by walking `Offsets` in order and
rotating each one, leaving the two lists index-aligned and `Offsets` itself untouched. `CellsWithRole`
therefore matches the authored offset in `Offsets` and reads the same index out of `TransformedOffsets`,
so there is no second copy of the rotation maths and no choice between `_currentAngle` and
`_structureInitAngle`.

Build-time guards. `Legend` throws at the call (`ArgumentException` for a reserved or repeated symbol,
`InvalidOperationException` for a network token); everything else is an `InvalidOperationException` from
`Build()`:

| Guard | Why |
|---|---|
| `'.'` or space as a `Legend` symbol | both are grid syntax |
| one glyph, two codes | the cell gets one number, so one code silently stops being required |
| a `Legend` code carrying a multi-letter direction token | only a network node spells one, and a node re-picks its own orientation; `LegendAnyFacing` is the opt-out |
| a grid glyph with no `Legend` | the cell has no code to require |
| role or connector on a glyph with no `Legend` | the glyph is not in the drawing's alphabet; the role would answer empty for ever |
| role or connector on a glyph no grid draws | same silent empty set by the other route, and for a connector the worse half of it: an undrawn demand reads as a structure with no facing requirement at all, which completes with the node backwards. Checked per glyph, so a role two glyphs share still fails when one is dropped |
| `Core` glyph with no `Legend`, never drawn, or off `(0,0,0)` | the structure would be built offset from the placed block |
| a single-cell role drawn on != 1 cell | the furnace reads `MetalTap`/`SlagTap` as one cell, and with a second it silently answers no tap at all. Counted over drawn cells, so two glyphs sharing the role fails too (`ValidateRoleArity`) |

The shaft/firebox split is not an exlib guard: iiex's `FurnaceLayoutRoleTests` fails any iiex layout that
marks both `Chargeable` and `Firebox`, since a furnace holds a burden column or a fuel bed, never both.

Arity is part of a role's meaning. `MetalTap` and `SlagTap` are the two single-cell roles - a hearth is
drained at one point, and the furnace reads each as one nullable cell (`BlockEntityFurnaceCore.MetalTapPos`,
`SlagTapPos`). Everything else is a genuine set: a shaft is a column, a hearth has two tuyeres, and a stack
throttled at both ends is one layout with two `Damper` cells. Unmarked is the default and the loose end:
tightening a role later is a build error the author sees, while loosening one silently breaks every reader
that takes it as one cell.

The reader is total; the builder is the gate. `MultiblockCellRoles.FromAttributes` skips anything it
cannot parse - a blank role key, a coordinate that is not an `int` - rather than throwing, because it is
re-read in `SetStructureAngle`, which the server monitor tick reaches whenever the angle changes. A throw
there is a repeating exception on a live block entity mid-session. Every coordinate is type-checked
rather than cast (`LayoutAttribute.Coord`). `MultiblockConnectors.FromAttributes` reads the same way and
also skips a key that is not a side letter. The price is that a hand-edited attribute loses cells
silently; a code-first layout is the only supported route.

Additive, and it must stay that way. A layout that calls no `Role` emits no attribute at all and gets
`MultiblockCellRoles.None`. `Adding_roles_changes_nothing_about_the_structure_a_layout_emits` pins the
stronger statement at source: the same drawing with and without `Role` calls emits byte-identical
`multiblockStructure` JSON, and a layout that does mark something moves its golden only inside
`multiblockRoles`.

Live users: the six iiex furnace layouts (cold blast, cupola, puddling, heating, crucible, coke oven) and
siex's hot blast furnace mark furnace roles; the smokestack marks only `NoSnow`; the cowper stove marks
none. The readers are on iiex's `BlockEntityFurnaceCore` and its subclasses:

| consumer | reads |
|---|---|
| `ChargeableCells` | `Chargeable` |
| `FireboxCells` | `Firebox` |
| `PoolCells` | `Pool` |
| `ScanForOutlets` -> `_tuyeres` | `Tuyere` |
| `ScanForOutlets` -> `_gasOutlets` | `GasOutlet` |
| `MetalTapPos` | `MetalTap` |
| `SlagTapPos` | `SlagTap` |
| `ShaftBox` | `Chargeable` or `Firebox`, authored offsets |
| `BlockEntityFireboxFurnace`, `BlockEntityCrucibleFurnace` (the stack and its draught) | `Flue`, `Damper` |

An absence needs no declaring: the drawing states it. A furnace whose drawing has no tuyere or outlet
answers an empty set, and `MetalTapPos` and `SlagTapPos` are nullable for the same reason: a single-cell
role a layout does not mark answers no cell rather than inventing one - the puddling furnace has neither.

The role<->code cross-check does not generalise. `Tuyere`, `MetalTap` and `SlagTap` sit on glyphs whose code
names one block (and for a tap, one facing), so a test can play the role off `CellsAccepting` and catch a
`Role()` hung on the wrong glyph; iiex's `FurnaceRoleCellsTests` does so at all four facings. `Pool` cannot
be checked that way: its hearth-cell code also admits everything the shaft glyph admits. What stands in
for it is a structural relation: the pool is the course directly below the floor of the burden column, on
the cold furnace and the cupola it holds no burden cell, and every role cell must pass `OwnsCell`.

The shaft box is derived too. `BlockEntityFurnaceCore.ShaftBox` is the bounding box of `Chargeable` or
`Firebox` - the two never share a layout, so the union is whichever one the drawing carries and no
per-branch virtual is needed. `ShaftBounds()` re-sorts the rotated corners per component, and both it and
`ShaftBox` are nullable, because two corners cannot express "no cells" - everything that re-sorts would
normalise an "impossible" pair back into a box at the origin. Load order: vanilla assigns `Block` in
`CreateBehaviors` immediately before `FromTreeAttributes` on every load path, so a block entity restoring a
save does know its own block; what it lacks that early is `Api`.

### The trapdoor caveat

Trapdoors cannot be orientation-checked by this mechanism. `game:trapdoor` keeps both its facing and its
open/closed state in the block entity, not in the block code, so no code match can see either. Slabs,
stairs, doors and charge doors all carry theirs in the code and do work. A layout cell wanting a trapdoor
can only ever require a trapdoor, in any rotation and any state; no shipped layout names one.

A second trap in the same family: the code must actually exist. `game:trapdoor-iron-down*` matches
nothing - vanilla's real variant chain narrows to `trapdoor-plate-iron-1` - and `game:claybricks-fire*`
matches nothing because vanilla's variant order is `{state}-{type}`, i.e. `claybricks-good-fire`.

### Completion, projection and the missing-blocks report

`BlockEntityMultiblockStructure` is the base for every multiblock anchor:

- A server-side monitor tick every `CompletionTickMs` (default 3000 ms) recomputes the rotation and
  flips `StructureComplete`. On completion it calls `OnStructureCompleted` and starts whatever process the
  machine carries (`ProductionProcess`); on loss it calls `OnStructureLost` and stops the process when
  `StopsProductionWhenNotReady` (default true). A form-only multiblock has no process and the two calls are
  no-ops.
- `IncompleteBlockCount` walks the same `TransformedOffsets` vanilla does and matches with the same
  `WildcardUtil.Match`, with two additions: the wanted code is run through `MultiblockFacings.Rotate` first
  (`WantedCodeAt`), and a matching occupant of a connector cell must also open on each demanded face. The
  number -> code map is rebuilt from the public `BlockNumbers` because vanilla keeps its own private, and
  cached until the angle changes.
- Projection is Ctrl+Shift+right-click on an incomplete anchor or on any functional component of one
  (a tap, hopper, tuyere), routed through `BlockBehaviorMultiblockStructure.TryToggleProjection` to
  `Interact`, which re-checks completion, draws the hologram and chats an exact shopping list: missing
  blocks by count, then every misfaced connector cell by position and face. A component resolves its
  anchor through `IMultiblockComponent.ResolveOwningAnchor`. On a complete structure the gesture is not
  consumed. Add the behaviour before any other right-click behaviour so its `PreventSubsequent` wins.
- `HighlightIncompleteSafe` is a crash-safe reimplementation of vanilla's `HighlightIncompleteParts`,
  which does `SearchBlocks(wantedCode)[0]` and throws `IndexOutOfRangeException` when a wildcard resolves to
  nothing. A wrong block is tinted red, an empty slot in the wanted block's colour, falling back to a
  neutral blue.
- Air-satisfied and filler cells are excluded from the shopping list, because the player does not gather
  them (`IsAutoFilled`).
- `OwnsCell(worldCell)` is the reverse lookup that disambiguates two adjacent structures whose component
  scan boxes overlap; `FindAnchorOwning<T>` is the bounded scan a component uses, and
  `MultiblockAnchorLink<T>` keeps its result, re-scanning at most once a second while it has none.

### Invisible fillers

`BlockStructureFiller` is one shared exlib block (`exlib:structurefiller`, `StructureFillers.FillerCode`)
authored code-first in `BlockStructureFiller.Definitions`: `json` drawtype over `exlib:block/empty`,
side-solid but not opaque, `lightAbsorption 0`, `replaceable 500`, `resistance 45`, no drops, excluded
from the handbook, full-cube collision and selection.

`sidesolid: true` is what gives the megablock real per-cell collision; drawing nothing is what makes it
invisible. Everything else it does is rerouting to the principal:

| Operation | Reroute |
|---|---|
| interact start/step/stop | to the principal, cell-aware via `IFillerInteractionTarget` |
| interaction help | the principal's, cell-aware via `IFillerInteractionTarget.GetFillerInteractionHelp` |
| getting broken / broken | to the principal (breaking any cell breaks the whole machine); the cell clears itself if the principal's break left it |
| drops | always `[]` - the principal owns all drops |
| pick block | the principal's `OnPickBlock` |
| look-at info | the principal's `GetPlacedBlockInfo`, and the BE's `GetBlockInfo` (`BlockEntityStructureFiller.GetBlockInfo`) |
| sounds | the principal's, so the invisible footprint is not silent |

Two subtleties in the interaction reroute (`BlockStructureFiller.OnBlockInteractStart`): a player holding a
placeable block is building, not driving the machine, so the forward is skipped - except for liquid
containers, which the principal still needs to see. And an unhandled click on an `allowAttach` cell returns
`false` so the engine places normally, while on a non-buildable cell it is swallowed so nothing drops onto
the invisible face.

Per-cell knobs, all from the `fillerOffsets` entry (`StructureFillers.ReadOffsets`):

| Key | Default | Effect |
|---|---|---|
| `x, y, z` | - | offset from the principal, in the block's north/authored frame |
| `allowAttach` | `false` | whether other blocks may attach here (`BlockStructureFiller.CanAttachBlockAt`). Default off, or torches and vines would hang on the invisible footprint |
| `collisionBox` / `collisionBoxes` | full cube | partial-fill cuboids for a cell the megablock only half occupies - a slab. `collisionBoxes` wins over `collisionBox`. Selection matches collision so the player cannot target solid-looking empty space |
| `behaviors` | none | see below |
| `portFace` / `portNetwork` | none | a passive network port - see The declarative filler port |

Rotation. `StructureFillers.FootprintCells(principal, pos, angle)` rotates the offset
(`ExOrientation.RotateOffset`), the partial boxes (`RotateBoxes`, pivoting on the cell centre), each
declared behaviour's connector face and the port face into the placed orientation. The caller passes the
angle; `BlockFilledMegastructure.StructureAngle` reads it from the `side` or `orientation` variant and is
overridable.

Lifecycle triad - `CanPlace` -> `PlaceFillers` -> `RemoveFillers`. `CanPlace` wants every cell empty or
replaceable by the filler; a cell already holding a filler is not (vanilla replaces only a block whose
`replaceable` is 6000 or more), so two footprints never overlap. `PlaceFillers` and `RemoveFillers` run on
the server only, and `RemoveFillers` clears only a cell that holds a filler linked to this principal, so a
neighbouring structure's fillers are never disturbed. With the filler block unregistered, the first call
logs one error per world and nothing is placed. `BlockFilledMegastructure` (registered
`ExFilledMegastructure`) folds this triad for blocks that can inherit from it - `CanPlaceBlock` (failure
code `notenoughspace`), `OnBlockPlaced` with an `OnFootprintPlaced` hook, and `OnBlockRemoved`. Blocks that
already have a base class (the flywheel is a `BlockNetworkNode`) call the three helpers from the same three
overrides - `BlockFlywheel` is the canonical hand-rolled copy.

### The footprint DSL

`StructureFootprint.Layout(...)` -> `FillerLayoutBuilder` is the filler-side counterpart of the multiblock
DSL, on the same `CellGrid` reader:

| Member | Meaning |
|---|---|
| `Origin(a, b)` | axis pair depends on the grid kind: `(xLeft,zTop)` for `Layer`, `(zLeft,yTop)` for `Slice`, `(xLeft,yTop)` for `Face` |
| `Solid(ch)` / `Attach(ch)` | register a glyph as plain / attach-allowing; `'#'` and `'+'` are the defaults |
| `Slab(ch, face)` | register a glyph as a cell filled only on the half against `face`, emitting the matching `collisionBox` |
| `Host(ch, ...specs)` | register a glyph as an attach-allowing cell that hosts behaviours |
| `Port(ch, face, networkType)` | register a glyph as a plain filler carrying a passive network port on `face` |
| `Layer(y,...)` / `Slice(x,...)` / `Face(z,...)` | the three grid kinds |
| `'O'` / `'0'` | the principal, for readability - skipped, never becomes a filler |

Registering a glyph again replaces its earlier meaning, the two defaults included.

Build-time guards (`FillerLayoutBuilder.Build`):

- two cells drawn at one position in one grid kind throw (`CellGrid.Add`);
- `'O'` drawn anywhere but `(0,0,0)` throws, naming the coordinate - a misplaced grid;
- an unregistered glyph throws;
- then `StructureFootprint.Validate` rejects a cell at the principal origin and any duplicate cell across
  the grid kinds.

`StructureFootprint.Rectangle(halfWidth, depth)` is a computed shortcut for a linear footprint: `depth` rows
along `+Z`, `2*halfWidth+1` columns emitted centre-out (`0, +1, -1, +2, -2, ...`), principal skipped, every
flanking column opting into attachment. No shipped block uses it - every current footprint is a
hand-drawn `Layout` grid.

Per-variant footprints go through `ExBlockDef.FillerOffsetsByType(typeWildcard, cells)`, which writes
`attributesByType.{wildcard}.fillerOffsets` - the flywheel's `normal` 3x3x1 vs `large` 5x5x2 discs.

### Behaviour-capable fillers

A footprint cell can host real block-entity behaviours on the principal's behalf, because the principal,
two cells away, cannot accept a connection at the face where an axle physically couples.

Declared as `FillerBehaviorSpec(Code, Face?, Properties?)`, or `FillerBehaviorSpec.Of<T>(face, properties)`,
which takes the behaviour's registered key from its type; serialised as `{ code[, face][, properties] }` in
the cell's `behaviors` array (`ExBlockDef.SerializeFillerCells`), read back by `StructureFillers.ReadOffsets`.

Instantiation lives in `BlockEntityStructureFiller.ApplyHostedBehaviors`: resolve the class code through
`Api.ClassRegistry`, call `IFillerHostedBehavior.ConfigureFromFiller(principal, rotatedFace, properties)`
before `Initialize` so the behaviour's `SetOrientations` sees the right face, then attach and initialise.
Unknown class codes log a warning and are skipped.

Re-applying is safe: being handed the declaration set already applied returns without touching the live
behaviours, and a genuine change detaches the previous set with `OnBlockRemoved` first, so a detached
behaviour deregisters whatever it registered rather than leaving it behind.

Three sync traps are handled explicitly:

- `SetHostedBehaviors` is called by `PlaceFillers` right after the principal link is set, so an MP port
  joins the network at placement rather than at the next reload.
- The load order is `FromTreeAttributes` -> `Initialize`, so a behaviour created in `Initialize` missed the
  base class's tree-routing loop. The tree is kept in `_savedTree` and replayed to each behaviour -
  client only, because on the server the behaviour establishes its own state and a stale saved
  `NetworkId` would fight it. The consequence is that a hosted behaviour's *saved* state is never read back
  on the server; a membership is unaffected because it persists nothing and re-registers from its
  declaration.
- When a megablock is placed while a client is watching, the filler block is set first (client creates the
  BE with no behaviours) and `HostedBehaviors` arrives a moment later as a sync update, after `Initialize`
  has already run. `FromTreeAttributes` re-runs `ApplyHostedBehaviors` in that case.

`BlockStructureFiller` then advertises the hosted behaviour to the two foreign networks:

- exlib pipe/molten via `INetworkConnector.NetworkTypeAt` / `HasConnectorAt(world, pos, face)`, reading
  `PortNetworkType` / `PortFace` off the BE;
- vanilla MP via `IMechanicalPowerBlock.HasMechPowerConnectorAt`, which needs an MP behaviour on the cell
  and accepts the declared face or its opposite - an axle couples along an axis, so a player can run it
  straight through the cell and attach from either side. A declaration whose properties set
  `through: false` accepts its own face only. It reads every hosted declaration's face, not just an MP
  one's, so a cell hosting a network membership *and* an MP port would offer an axle the membership's face
  too.

Live users: the flywheel's hub cells hosting `BEBehaviorMPFillerPort` north and south, the twin-tub
blower's west MP port and its two pipe pass-through cells, the rolling mill's drive-line cells, and the sand
casting bed's per-cell `BEBehaviorMoltenCell` with different `{capacity, drainFitting}` per slot.

### The declarative filler port

A *passive* port is the lighter of the two arms, and it is declared on the drawing rather than hosted:

```csharp
f.Port('S', BlockFacing.UP, "pipe")     // steam leaves through the top of this cell
 .Port('E', BlockFacing.EAST, "pipe")   // flue gas leaves eastward from this one
```

`FillerLayoutBuilder.Port` registers the glyph as a plain, non-attaching filler and records
`(face letter, networkType)`; the pair rides through `FillerCellSpec` -> `fillerOffsets[].portFace` /
`portNetwork` (`ExBlockDef.SerializeFillerCells`), is read back by `StructureFillers.ReadOffsets` and is
rotated into the placed orientation with the rest of the footprint. `BlockStructureFiller` then answers
`INetworkConnector.NetworkTypeAt` / `HasConnectorAt` off the placed cell's `PortFace` / `PortNetworkType` -
the same two fields a hosted behaviour would have had to be instantiated to provide.

The face is authored in the north frame, so a machine reads it back off its own footprint rather than
writing it a second time in code; iiex's `BlockBoiler.PortWorldFaceAt` is the worked example - it finds the
cell at a declared offset, takes that cell's `PortFace`, and rotates it, so the face a machine probes across
and the face the cell answers on cannot drift apart.

**Connector or node - which arm to pick.** They are not interchangeable, and the choice is per machine:

| | A declared **port** (connector) | A hosted **`BEBehaviorNetworkMember`** (node) |
|---|---|---|
| What the cell is | skin. It answers for the principal and is invisible to the graph | a member of the graph in its own right |
| Costs | two strings in the footprint | a block entity behaviour instantiated, registered and torn down per cell |
| Pick it when | the machine is the thing on the network and the cell is only where a pipe touches it | the run has to **pass through** the footprint, or the cell must be reachable as a node from more than one side |
| Live examples | the Cornish boiler's steam and exhaust cells ([Cornish boiler](https://github.com/ringavirda/modding-vsexmods/blob/main/docs/design/machines/boiler-cornish.md)) | the rolling mill's drive line, where a shaft runs straight through, and the twin-tub blower's pipe run (section A filler cell is a graph node) |

A connector cannot be probed from the principal. `ConnectedNetwork` runs its reciprocal test from the
block entity's own cell, and a port two cells away is not that cell - so a machine reading a footprint port
uses `ConnectedNetworkAt<TNet>(portCell, face)` instead (`MachinePorts`). Reading from the principal
answers `null` on a correctly plumbed machine, silently.

### A filler cell is a graph node when it declares one

A footprint cell joins an exlib `BlockNetwork` the way any other cell does: by carrying a
`BEBehaviorNetworkMember` for that network. The graph resolves a cell's participation through
`NetworkMembership.Resolve` - the memberships on its block entity first, the block second - so a filler
being a plain `Block` does not keep it out, and one can bridge two nodes on opposite sides of itself. A
cell that declares no membership is not a node: the footprint stays empty space to the graph unless a cell
says otherwise.

The declaration is an ordinary hosted behaviour, so nothing new carries it:

```csharp
f.Host('P', FillerBehaviorSpec.Of<BEBehaviorNetworkMember>(
  "north",
  new { networkType = "pipe", passThrough = true }));
```

- `networkType` names the graph the cell joins. Without it the cell logs an error and joins nothing,
  because registering a blank type throws inside a chunk load (`BEBehaviorNetworkMember.Initialize`).
- the cell's `face`, rotated into the placed orientation by `FootprintCells`, is the face it couples
  on; `passThrough` adds the opposite face too, so a run passes straight through the cell - the same
  axis rule an axle follows (`BEBehaviorNetworkMember.ConfigureFromFiller`).
- a `connectors` string in the properties is written in the *unrotated* frame and does not turn with
  the structure, so a footprint that rotates states its `face` instead; where both are given the face wins
  and a differing string is logged.

**A membership and a port on one cell.** `PortFace`/`PortNetworkType` mean a face another network couples
*to* on a cell that is not itself a node, as both boilers' steam cells are. The two arms compose per
network type - a membership answers for the network it names, the port for any other - and where both name
the same one **the membership wins**: it is the cell's own participation and the thing that registered the
node, so a port cannot move a node's faces. A membership that states no face of its own falls through to
the port's, which turns an existing port cell into a node without restating where it couples.

A footprint cell answers *only* through its block entity, where an ordinary node block has the block
arm to fall back on as well. That difference is narrower than it looks: an unload takes the block too,
so both kinds of cell are equally invisible while their chunk is away. The graph answers that for both
by suspending its fracture check rather than acting on it, so a run bridged through a footprint cell is
left whole until the chunk returns - see [pipe network](pipe-network.md), section 1.

The rolling mill is the shipped case on the `mpenergy` graph. It needs a drive line through its footprint
so it can be driven from either shaft end and chained with other stands, and its two `m` cells host
`passThrough` `BEBehaviorNetworkMember` declarations facing east and west (`BlockRollingMill`). See
[mp-energy](mp-energy.md).

### `workbench/layouts.md` - the scratchpad

[workbench/layouts.md](https://github.com/ringavirda/modding-vsexmods/blob/main/workbench/layouts.md), in
exmods, is the working surface for layouts, split by mod. Every drawing there is copied from code except
the drafts, each marked as one in its section. The goldens are the truth: where a copy and its golden
disagree, the golden is right.

Goldens live at `mods/<mod>/tests/goldens/<domain>/blocktypes/...` in exmods. Draft a layout there, then
paste it into the owning block.

---

## Numbers

Everything in this system is a hard-coded constant or a build-time rule; there is no config surface for
multiblocks or fillers. A layout is content, authored in C# and pinned by a golden.

| Constant | Value | Where | What it does |
|---|---|---|---|
| `StructureFillers.FillerCode` | `exlib:structurefiller` | `StructureFillers.FillerCode`, set in `ExpandedLibModSystem.Start` | settable `static` property, so a fork could point it elsewhere; nothing does |
| filler `sidesolid` | `true` | `BlockStructureFiller.Definitions` | required - this is what gives per-cell collision |
| filler `sideopaque` | `false` | same | so the footprint does not cull neighbour faces |
| filler `lightAbsorption` | `0` | same | an invisible cell must not cast shadow |
| filler `replaceable` | `500` | same | below vanilla's 6000 threshold, so `CanPlace` refuses a cell another footprint's filler holds |
| filler `resistance` | `45.0` | same | never actually mined - the break reroutes to the principal |
| filler drawtype / shape | `json` / `exlib:block/empty` | same | renders nothing |
| default `allowAttach` | `false` | `StructureFillers.ReadOffsets`, `BlockEntityStructureFiller.AllowAttach` | opt-in per cell |
| default partial boxes | `null` (full cube) | `StructureFillers.ReadOffsets` | `collisionBoxes` wins over `collisionBox` |
| `CompletionTickMs` | `3000` | `BlockEntityMultiblockStructure.CompletionTickMs` | `protected virtual`, overridable per machine |
| first block number `w` | `1`, incrementing per distinct code in legend-declaration order | `MultiblockLayoutBuilder.Build` | private index; the game only cares that it resolves |
| reserved legend symbols | `'.'`, `' '` | `MultiblockLayoutBuilder.Legend` | throw if used as a legend symbol |
| cell roles defined | exlib 1 (`NoSnow`); iiex 9 | `CellRoles`, `FurnaceCellRoles` | emitted only when a layout marks something |
| single-cell roles | 2 - `MetalTap`, `SlagTap` | `FurnaceCellRoles` | minted `single: true`; every other role is a set of any size |
| principal glyphs | `'O'`, `'0'` | `FillerLayoutBuilder.Build` | skipped at origin, throw elsewhere |
| default filler glyphs | `'#'` plain, `'+'` attach | `FillerLayoutBuilder` | overridable via `Solid`/`Attach` |
| "no principal" sentinel | `(-1,-1,-1)` | `BlockEntityStructureFiller.ToTreeAttributes` | in the save tree |
| projection gesture | Ctrl + Shift + RMB | `BlockBehaviorMultiblockStructure.IsProjectionGesture` | |
| anchor re-scan | 1000 ms | `MultiblockAnchorLink<T>.Resolve` | while no anchor is cached |
| wrong-block highlight | RGBA `215,94,94,0x60` | `BlockEntityMultiblockStructure.HighlightIncompleteSafe` | red |
| unresolvable-slot highlight | RGBA `94,94,215,0x60` | same | neutral blue fallback |
| projection help key | `<domain>:blockhelp-mulblock-struc-show` | `BlockBehaviorMultiblockStructure.ProjectionHelp` | resolved against the block's own domain, else exlib's |
| missing-report lang keys | `ExlibLang.StructureMissingHeader` / `...Line`, `ExlibLang.StructureMisfacingHeader` / `...Line` | `BlockEntityMultiblockStructure.ShowMissingBlocksReport` | exlib owns them; generated accessors, so a rename is a compile error |

Shipped drawn layouts: 9 ([workbench/layouts.md](https://github.com/ringavirda/modding-vsexmods/blob/main/workbench/layouts.md)
lists them per mod), plus the Bessemer converter still in coordinate form.

---

## Code

Paths are under `src/ExpandedLib/`.

### Authoring (compile-time)

| Type / member | File | Role |
|---|---|---|
| `ExBlockDef.MultiblockLayout(cfg)` | `Definitions/ExBlockDef.cs` | the entry point; writes `attributes.multiblockStructure` + the siblings `multiblockFacings`, `multiblockRoles` and `multiblockConnectors` |
| `ExBlockDef.Multiblock(cfg)` | same | raw coordinate form, for a layout not yet drawn (the Bessemer converter) |
| `ExBlockDef.FillerOffsets(cells)` | same | writes `attributes.fillerOffsets` |
| `ExBlockDef.FillerOffsetsByType(wc, cells)` | same | per-variant footprint |
| `MultiblockLayoutBuilder` | `Definitions/MultiblockLayoutBuilder.cs` | `Origin`/`Core`/`Legend`/`LegendAnyFacing`/`Role`/`Connector`/`Layer`/`Slice`/`Face`; `RefuseNetworkToken`, `FindOrientationSegments`, `BuildFacings`, `BuildRoles`, `BuildConnectors`, `ValidateRoleArity`, `ValidateRoles` |
| `CellRole` / `CellRoles` | `Structures/CellRole.cs` | the role key and its arity; `CellRoles.NoSnow`, `CellRoles.IsSingleCell` |
| `MultiblockBuilder` | `Definitions/MultiblockBuilder.cs` | `Number`/`At`/`Fill`/`Build` + the three guards |
| `CellGrid`, `GridPlane`, `GridOptions`, `ThreePlaneDraw` | `Structures/` | the grid reader both builders draw through |
| `StructureLayout` | `Definitions/StructureLayout.cs` | `LayoutCell`; `Parse`, one horizontal read standalone |
| `StructureFootprint` | `Structures/StructureFootprint.cs` | `Layout`, `Rectangle`, `Validate` |
| `FillerLayoutBuilder` | `Structures/FillerLayoutBuilder.cs` | `Origin`/`Solid`/`Attach`/`Slab`/`Host`/`Port`/`Layer`/`Slice`/`Face` |
| `FillerCellSpec`, `FillerBehaviorSpec` | `Structures/StructureFootprint.cs` | the typed footprint records |
| `JsonMultiblockLayout` | `Structures/JsonMultiblockLayout.cs` | the JSON form, resolved on load |

### Runtime

| Type / member | File | Role |
|---|---|---|
| `BlockEntityMultiblockStructure` | `Structures/BlockEntityMultiblockStructure.cs` | the form alone: monitor tick, completion, projection, missing report, and the readiness it publishes |
| `.UpdateStructureRotation` | same | abstract - every anchor implements it, normally by calling `SetStructureAngle` |
| `.SetStructureAngle(angle, offset)` | same | the canonical body: reload the JSON, `InitForUse(angle+offset)`, drop the caches, re-read the siblings, mark `NoSnow` cells, drop a stale projection |
| `.OnStructureCompleted` / `.OnStructureLost` | same | the two hooks a machine overrides |
| `.IsReadyToProduce` / `.StopsProductionWhenNotReady` | same | the readiness a process reads; see [framework composition](framework-composition.md) |
| `.CellsAccepting(code)` / `.CellsWithRole(role)` / `.LocalCellsWithRole(role)` / `.ConnectorFacesAt(cell)` | same | the layout-derived cell queries; the first two cached and dropped in `SetStructureAngle` |
| `.OwnsCell` / `.FindAnchorOwning<T>` | same | component -> anchor reverse lookup |
| `MultiblockAnchorLink<T>` | `Structures/MultiblockAnchorLink.cs` | a component's throttled, cached anchor lookup |
| `BlockEntityMultiblockMachine` | `Structures/BlockEntityMultiblockMachine.cs` | the form plus a hosted production process; what a multiblock that also runs derives from |
| `BlockEntityMultiblock` | `Structures/BlockEntityMultiblock.cs` | registered as `"ExMultiblock"`; the concrete anchor for a JSON-only structure |
| `BlockBehaviorMultiblockStructure` | `Structures/BlockBehaviorMultiblockStructure.cs` | registered as `"MultiblockStructure"`; `TryToggleProjection` is the one shared entry point |
| `MultiblockFacings` | `Structures/MultiblockFacings.cs` | `FromAttributes`, `Rotate`, `RotateSegments` |
| `MultiblockCellRoles` | `Structures/MultiblockCellRoles.cs` | `FromAttributes`, `CellsOf` - authored offsets, never world ones; skips what it cannot parse |
| `MultiblockConnectors` | `Structures/MultiblockConnectors.cs` | `FromAttributes`, `OutwardFacesAt` - authored faces, rotated by the caller |
| `NoSnowCells` / `NoSnowPatch` | `Structures/` | the no-snow registry and the two snow postfixes |
| `StructureFillers` | `Structures/StructureFillers.cs` | `ReadOffsets`, `FootprintCells`, `CanPlace`/`PlaceFillers`/`RemoveFillers` |
| `BlockStructureFiller` | `Structures/BlockStructureFiller.cs` | the invisible block + all rerouting |
| `BlockEntityStructureFiller` | `Structures/BlockEntityStructureFiller.cs` | `Principal`, `AllowAttach`, `CollisionBoxes`, `PortFace`/`PortNetworkType`, `HostedBehaviors` |
| `BlockFilledMegastructure` | `Structures/BlockFilledMegastructure.cs` | registered as `"ExFilledMegastructure"`; the shared place/remove triad and the JSON layout; `StructureAngle` is virtual |
| `IFillerHost` / `IFillerHostedBehavior` / `IFillerInteractionTarget` / `IMultiblockComponent` | `Structures/` | the four contracts |
| `ExOrientation.RotateOffset` / `RotateFacing` / `RotateSideWord` / `RotateOrientationToken` / `GlobalPos` | `Helpers/ExOrientation.cs` | all structure rotation goes through these |

### Where a caller hooks in

To make a megablock: implement `IFillerHost` (or inherit `BlockFilledMegastructure`), supply the angle,
declare the footprint with `StructureFootprint.Layout(...)` and `.FillerOffsets(...)`, then wire
`CanPlaceBlock` -> `StructureFillers.CanPlace`, `OnBlockPlaced` -> `PlaceFillers`, `OnBlockRemoved` ->
`RemoveFillers` before `base` (`BlockFlywheel` is the copyable shape). Override `GetDrops` - the base will
otherwise drop the block and the RCC materials.

To make a multiblock: derive the BE from `BlockEntityMultiblockStructure`, implement
`UpdateStructureRotation` via `SetStructureAngle`, add `{ "name": "MultiblockStructure" }` to the block
before any other right-click behaviour, and author the layout with `.MultiblockLayout(...)`. A JSON-only
structure uses `ExFilledMegastructure` with `ExMultiblock` and `attributes.multiblockLayout`.

To put a port on a footprint cell: `f.Host('M', FillerBehaviorSpec.Of<BEBehaviorMPFillerPort>("west"))`,
then read it back from the principal with `GetBehavior<BEBehaviorMPFillerPort>()` at the rotated cell
(`ExOrientation.GlobalPos(Pos, hx, hy, hz, angle)`) - iiex's `BlockEntityFlywheel`.

---

## Gotchas

- `Origin` is checked in the multiblock DSL only when the layout names its anchor with `Core`. Without
  it, a wrong `Origin` builds the whole structure offset from the placed block and reads as "the structure
  never completes". The filler DSL always checks, through its `'O'` glyph.
- `'.'` advances the column; a space does not. `.` is an empty cell that still moves `+X`; a space is a
  pure separator (`CellGrid.Add`). Swapping them shifts every glyph after it on that row.
- `Layer`, `Slice` and `Face` grids may be mixed in one builder, but they share the one `Origin` pair, which
  each kind reads as its own axis pair.
- A role cannot be keyed by code, only by glyph. `game:air` is the vent shaft, the flue and the tap
  alcove in shipped layouts. Give each role its own glyph pointing at the same code.
- Block numbers are per code, not per glyph. Numbering per glyph would produce two `w`s and one
  `blockNumbers` entry for two glyphs sharing a code; the lost number's cells would silently stop being
  required, and vanilla's own `InCompleteBlockCount` would throw on them. The shape of `blockNumbers` is
  what forces it (`MultiblockLayoutBuilder.Build`).
- `multiblockFacings` is keyed by the full domained code. Writing the key as the author typed it
  (`ToShortString()`) drops `game:` and silently never matches a vanilla block, so the rotation becomes a
  no-op with no error (`MultiblockLayoutBuilder.AddLegend`, `MultiblockFacings.Rotate`).
- Use `_structureInitAngle`, never `_currentAngle`, for facing rotation. They differ whenever a machine
  passes an `initAngleOffset` - the Bessemer converter control's `+180`.
- A wildcard code with `*` cannot be orientation-checked usefully - `-*` matches every rotation by
  construction. Oriented legends must name a concrete facing.
- The JSON form writes no `multiblockFacings`, so a side-named legend code in `attributes.multiblockLayout`
  is required at its authored facing at every structure angle.
- A megablock must refuse placement when its volume is not clear, or the fillers silently fail to spawn
  and the machine has a hole in its collision that blocks can be placed inside (`StructureFillers.CanPlace`
  from `CanPlaceBlock`).
- Remove the fillers in `OnBlockRemoved`, before `base`. It runs on every removal path; `OnBlockBroken`
  runs only on a player break, so fillers cleared there are left behind as invisible solid cells whenever
  the block goes any other way.
- Hosted behaviours need the client re-apply paths. Without the `_savedTree` replay and the
  `FromTreeAttributes` re-apply, a client-side MP port never joins its network and the driven part never
  turns - the failure is purely visual and easy to miss in a headless test
  (`BlockEntityStructureFiller.ApplyHostedBehaviors`, `FromTreeAttributes`).
- `ConfigureFromFiller` must run before `Initialize` or the behaviour's `SetOrientations` sees the
  unrotated default face (`BlockEntityStructureFiller.ApplyHostedBehaviors`).
- Vanilla's `HighlightIncompleteParts` crashes on a wildcard that resolves to nothing. Always use the
  safe reimplementation (`BlockEntityMultiblockStructure.HighlightIncompleteSafe`).

---

## Open

- Wooden structures should record the wood they are built from, render in it, and return it when
  broken. A player request: "It would be nice if wooden structures could record the type of wood that
  they are built from and change textures/return items when broken appropriately." Right-click
  construction has no design page of its own, so it is held here. Today a stage's `storeWildCard` keeps
  one value per key (`ExConstruction.StoredWildCards`), so every wood stage sharing `wood` refunds the last
  wood paid, and no shape reads the stored value. Vanilla precedent: `EntityBoatConstruction` reads
  `StoredWildCards["wood"]` for the boat's textures and the finished boat's variant. It belongs to iiex and
  siex and waits on the testing and port infrastructure. Not designed.
- `LegendAnyFacing` has zero call sites in content. It exists for the "world-absolute facing" case (a
  chimney that must always vent north) that has not arisen. Untested against a real layout.
- `ShaftCentre` will never be a role. It is a geometric point with no layout meaning, but it is still a
  hand-written offset (iiex's `BlockEntityFurnaceCore.ShaftCentre` and its overrides) that has to land in
  the `c` column, and nothing checks that it does.
- `CellsAccepting` has no production caller, only tests. It is still the capability that answers "may this
  block stand here" for a caller holding a block, and the oracle the role tests are pinned against.
- Nothing checks a role against the code its glyph carries. A layout could mark a brick cell
  `Chargeable` and the build would pass. The tests that play a role off `CellsAccepting` cover the tuyeres
  and the taps only: `Pool` shares what its code admits with the shaft glyph, and `Flue` cells are air
  among other air cells. Every such role needs a structural relation invented for it instead.
- The cowper and smokestack layouts have firebox-ish cells and no `Firebox` role, and that holds: they are
  not `BlockEntityFurnaceCore` machines, nothing asks them for a fuel-bed cell set, and a role with no
  consumer is speculative. The boilers are outside this question entirely - neither declares a layout, and
  the Cornish's fuel bed is a behaviour on its block entity rather than a cell anything could mark
  ([firebox](https://github.com/ringavirda/modding-vsexmods/blob/main/docs/design/machines/firebox.md),
  section The pool is a behaviour, not a block feature).
- Nothing in exlib parses or validates `@(a|b)`. It is vanilla `WildcardUtil` syntax, so a malformed
  alternation fails as "this cell can never be satisfied" with no error.
- The Bessemer converter is still in coordinate form (`ExBlockDef.Multiblock`), so it does not benefit
  from the drawing, the legend or the oriented-part check. Its drafted grid is in
  [workbench/layouts.md](https://github.com/ringavirda/modding-vsexmods/blob/main/workbench/layouts.md),
  section Bessemer converter.
- Nothing checks that a footprint's hosted-port cells agree with the principal's own hard-coded cell
  list. The flywheel states its hub coordinates twice (the `M` cells of `BlockFlywheel`'s footprints and
  `BlockEntityFlywheel.HubCells`) with no cross-check - see [mp-energy](mp-energy.md).
- Filler footprints and multiblock layouts are validated separately and never against each other. A
  megablock that is also part of a multiblock could declare a filler cell where its own layout demands a
  player-placed block, and neither builder would notice. Only the JSON form derives one from the other.
