# Orientation schemes

**Status** partial - the scheme registry, the network-oriented declaration and the layout connector check
are built; the valve appearance split is not, and the canal-end question is open
**Mod** exlib (the registry lives there; every mod declares against it)

**Owns** - the facts this page is canonical for:

* the complete inventory of orientation spellings in the suite, and which blocks use each;
* the one rotation rule that covers all of them, and the proof it does;
* why the layout DSL cannot get this right by parsing the code string, and what it needs instead;
* the valve/canal directed-axis question and what dropping it would and would not buy.

**Does not own** - cited only: the oriented-parts feature and `multiblockFacings`
([multiblock](multiblock.md)), the rotation convention itself, north 0° / west 90° (`ExOrientation`), 
the layout scratchpad ([layouts](https://github.com/ringavirda/modding-vsexmods/blob/main/workbench/layouts.md)).

---

## The problem

A multiblock layout can require a part to be placed the right way round: it reads the facing out of the block
code and rotates it with the structure. That works for a facing (`brickslabs-fire-south-free`), and the
furnace layouts rely on it.

Network nodes do not spell a facing. They spell the set of faces they connect, and there are at least six
different spellings for it. The spellings overlap - `ns` means one thing on a pipe and a different thing on a
valve, and nothing in the string says which. So the string cannot be parsed; the block has to declare its
scheme.

---

## The inventory

Every `orientation` and `side` variant group in the suite, grouped by the shape of its tokens, with the
`ExOrientations` scheme that declares the set:

| Scheme | Tokens | Declared by |
|---|---|---|
| **Face** (horizontal), `Face` | `n e s w` | `orientation`: tuyere, twin-tub blower, fluid intake, smokestack intake, molten-canal tap, steam hammer. `side`: every player-oriented block, through `ExBlockDef.SideVariant` (tall hopper, cowper intake, furnace cores, boilers, engines and the rest) |
| **Face** (all six), `FaceAll` | `n e s w u d` | pipe outlet |
| **SideWord** | `north east south west` | vanilla only: fire-brick slabs, the coke-oven door. No family block declares it; `ExBlockDef.SideVariant` writes the letters of `ExBlockDef.HorizontalSides` |
| **Axis**, `Axis` | `ns we ud` | pipe straight, pipe passthrough, pipe indicator, cast-iron shaft, cast-iron bevel |
| **Axis** (horizontal), `AxisFlat` | `ns we` | flywheel, rolling mill, shear, fastener benches, canal straight |
| **DirectedAxis** (horizontal), `DirectedAxisFlat` | `ns we ew sn` | no shipped block; `BlockMoltenCanal.PassOrEndOrientations` returns it for a canal end |
| **DirectedAxis** (+vertical), `DirectedAxis` | `ns we ud sn ew du` | valve, pressure valve |
| **Bend** - two adjacent faces, `PipeBend`, `CanalBend` | `nw se en ws` + `un us uw ue` + `dn ds dw de` | pipe bend (12), canal bend (4) |
| **Tee** - three faces, `PipeTee`, `CanalTee` | `uns uwe dns dwe nes esw swn wne dnu deu dsu dwu` | pipe T-junction (12), canal T (4) |
| **Cross** - four faces, `PipeCross`, `CanalCross` | `nswe nsud weud` | pipe X-junction (3), canal X (1) |

Spelling is not canonical. The bend writes `en`, not `ne`; `ws`, not `sw`. The tee writes `uns` in one family
and `dnu` in another for the same kind of arrangement. These came from whichever rotation of the shape was
authored first, and they are the reason a general parser cannot be written.

---

## One rule covers every scheme

> **Rotate each direction letter, preserving order. If the result is a declared token, use it. Otherwise use
> the unique declared token with the same face *set*.**

The algorithm needs nothing from a block except the list of tokens it declares, which the block already
writes in its `VariantGroup`.

Worked through every scheme at 90° (north -> west):

| Scheme | Input | Ordered rotation | Declared? | Result |
|---|---|---|---|---|
| Face | `n` | `w` | yes | `w` |
| Axis | `we` | `sn` | no | set `{s,n}` -> `ns` |
| DirectedAxis | `we` | `sn` | yes | `sn` - direction survives |
| Bend | `nw` | `ws` | yes | `ws` |
| Tee | `uwe` | `usn` | no | set `{u,s,n}` -> `uns` |
| Cross | `weud` | `snud` | no | set `{s,n,u,d}` -> `nsud` |

The two-step is what makes one rule enough. The ordered step preserves direction exactly where a scheme
declares both spellings; the set fallback repairs the non-canonical spellings everywhere else. Neither step
alone works: ordered-only breaks the axis and the tee, set-only cannot tell `ns` from `sn`.

The set fallback requires one token per face set within a scheme. True for every scheme above except the
directed ones, where the ordered step catches it first and the fallback is never reached.

---

## What exists today

The declared-scheme registry is `ExOrientations` (`src/ExpandedLib/Helpers/ExOrientations.cs`): twelve
named schemes, each an `ExOrientationScheme` whose `Rotate` is the two-step rule (ordered spelling first,
face-set fallback second). Every `BlockNetworkNode` def with an `orientation` group declares
`{"mode":"network","scheme":"<Name>"}` through `ExBlockDef.NetworkOriented`, which reads the scheme off
the block's own `orientation` states rather than taking a name, so a misspelt scheme is unrepresentable in a
code-first def, and a per-mod scheme-parity contract (`NetworkNodeContract.SchemeViolations`) catches
anything authored another way.

`ExOrientationScheme.Rotate` has no production caller: a layout cannot pin a node token (below), and the
tokens a layout does pin are rotated by `MultiblockFacings` through `ExOrientation.RotateOrientationToken`.

`ExOrientation.IsOrientationToken` / `RotateOrientationToken` handle Face, SideWord, Axis and Cross, and
canonicalise. Bend and Tee are not recognised by `MultiblockFacings` at all - `nw` and `uns` fail the
axis-pair grammar, so a layout pinning a bend would get no facings entry and be unchecked at every angle.
`MultiblockLayoutBuilder.Legend` closes that from the other end: it **refuses** any code carrying a
multi-letter token a declared scheme spells, bends and tees included, so no layout can reach it.
`LegendAnyFacing` is the documented opt-out.

The directed schemes are mishandled by the string path. `RotateOrientationToken("sn", 90)` returns `we`, not
`ew` - it canonicalises, because with only the string to go on it cannot know the block declares both. That
is tested as a known limit (`OrientationTokenTests`), and a layout needing it must use `LegendAnyFacing`.

---

## A multiblock must not orientation-check a self-orienting node

For most nodes a layout should not pin the orientation at all; pinning one would be actively harmful.

A network node's orientation is not the player's choice.
`BlockNetworkNode.OnNeighbourBlockChange` calls `RecalculateAndSyncOrientations`, and
`BlockMoltenCanal.PickBestOrientation` picks whichever declared token best matches the connectors around it.
The node re-orients itself whenever a neighbour changes.

Two consequences:

1. **The structure could never be completed.** If the layout demands `pipe-straight-fire-ns` and the node
   decides `we` from its connections, the cell is never satisfied - and the player cannot fix it, because
   placing it "the other way round" does not stick.
2. **A complete structure could silently come apart.** Connect a pipe somewhere else in the world, the node
   re-orients, the cell stops matching - and for a furnace, incomplete means extinguish. A player would
   experience their furnace going out because they plumbed something unrelated nearby.

The dividing line is not "facing vs node token". It is: who decides the orientation?

| Orientation decided by | Examples | Orientation-check? |
|---|---|---|
| The player, fixed at placement or by wrench | slabs, stairs, coke-oven doors, tall hopper, cowper intake, furnace cores | yes - this is what the feature is for |
| The network, from a free run of neighbours | pipe straight/bend/junction, passthrough, molten canal mid-run | no - pin the block, never the orientation |
| The network, but walled in by the structure | tuyere, pipe outlet - a single-faced node embedded in a furnace shell | yes, and it is needed - see below |

**No shipped layout pins a node.** Where a layout names one it names it by wildcard -
`IiexBlocks.FurnaceTuyere.Any`, `IiexCodes.PipeOutlet` (`iiex:pipe-outlet*`) - and states the facing it needs
with `Connector`: the cold blast furnace's two tuyeres, the cupola's one, the hot blast furnace's two. Two
guards keep it that way: the builder's refusal above, and `PinnedNetworkNodes`, which resolves each pinned
code to the def that provides it and fails when that def is network-oriented. Not built: a `Connector` mark
on the pipe-outlet cells of the cowper stove and hot blast furnace layouts.

### The embedded connector is the case that must be checked

A tuyere or a gas outlet sits in the furnace wall, with the furnace on one side and the player's pipework on
the other. Placed backwards, its connector faces the furnace interior and the blast pipe joins to nothing.

The node's own logic does not prevent it. `BlockNetworkNode.ComputeValidOrientations` marks a face forbidden
only when the neighbour is a compatible network block with no connector back. A plain solid neighbour -
refractory brick - falls through to a branch that only ever adds a face as required, and only when the
current orientation already points at it. So nothing stops a tuyere facing into the brick, and nothing later
turns it round.

Unlike a free-run pipe, the required facing here is a property of the layout geometry, not of the world: the
drawing knows which side of that cell is furnace interior, and that never changes, so the check is stable.

The residual risk is narrow: a single-faced node re-picks only when it gains a network neighbour on another
face, and every other face of an embedded tuyere is brick. Reachable only by deliberately plumbing into the
furnace shell.

Two ways to express it, differing in robustness rather than difficulty:

| | |
|---|---|
| **Pin the cardinal** | Uses the oriented-parts mechanism exactly as it stands. Cheap. Breaks if the node ever legitimately re-picks. Refused by the builder |
| **Require "connector faces out"** | The layout marks the cell; the check asks the node whether its connector points away from the structure. Immune to re-orientation as long as it still faces out, which is the actual requirement. Built: `MultiblockLayoutBuilder.Connector` |

The second is what the fiction means and what the player expects. It is also the subset test described below,
narrowed to one face.

**As built:** `MultiblockLayoutBuilder.Connector(char, params BlockFacing[])` marks a glyph's cells; the
faces are authored in the structure's north frame and emitted as `attributes.multiblockConnectors`, a
sibling of `multiblockStructure` alongside `multiblockFacings` and `multiblockRoles`, omitted entirely when
a layout marks nothing. `BlockEntityMultiblockStructure.IncompleteBlockCount` rotates each demanded face by
the structure's own init angle and asks the occupant through `INetworkMember.HasConnectorAt`; an occupant
that is not on a network answers nothing, so a brick cannot satisfy the mark. A misfaced cell is reported
apart from a missing one - the block is already there and wants turning, not fetching.

### What a layout actually wants from a node

Not "this pipe faces north" but "there is a pipe here that connects along this axis" - a subset test, not an
equality test. The node's chosen token contains the faces the structure needs; it may contain more.

That is a different feature from oriented parts, and it needs the same registry: given a scheme's token list,
"does token `T` include face `f`" is a lookup. It also composes correctly with self-orientation - a node that
re-orients to serve more connections still satisfies a subset check.

The connector check is built on that subset property: a passthrough wearing `ns` satisfies a demand for
north, so a legitimate re-pick by the network does not break a standing structure. The lookup goes through the
occupant rather than the scheme - `HasConnectorAt(face)` - which answers the same question and also covers a
node whose faces depend on runtime block-entity state.

---

## What it needs: a declared scheme, not a parsed string

Name the schemes once in `ExOrientations`, and have each block resolve its declared tokens to one of them. A
block lists its tokens in its `VariantGroup` as before; `ExBlockDef.NetworkOriented` finds the scheme with
exactly that set and throws when there is none:

```csharp
.VariantGroup("orientation", "ns", "we", "ud")
.NetworkOriented()                                  // resolves to ExOrientations.Axis
```

That buys three things at once:

1. **The layout builder gets the token list**, so the one rule above is implementable. A code's scheme is
   found by looking its token up in the registry rather than guessing from its shape.
2. **A mistyped token list fails the build.** A set that matches no scheme is refused at the def, so the
   pipe bend's 12 and the T-junction's 12, retyped per block, cannot drift apart unnoticed, and the canal's
   horizontal-only schemes are declared as their own sets rather than as a coincidentally-similar list.
3. **Ambiguity becomes detectable.** Two schemes sharing a token (`ns` in `Axis` and `DirectedAxis`) is
   exactly the case that cannot be resolved from the string. The builder refuses every multi-letter token any
   scheme declares, so a layout cannot pin such a token at all.

This is the same move `ExCodes` made for block codes, applied to variant states.

---

## The directed-axis question

The valve and the pressure valve are the only shipped blocks that declare directed tokens. `DirectedAxisFlat`
is also declared, and `BlockMoltenCanal.PassOrEndOrientations` returns it for a canal end, but no canal type
ships it; a canal end would have a genuine reason for it: an end points somewhere, and which way it points is
not cosmetic.

The valve's case is the one worth acting on. Its reversed spellings exist so a wrench can flip which side the
handle sits on - an appearance choice encoded as a distinct orientation. If the handle side can be held
somewhere other than the orientation variant (a block-entity flag driving a shape swap, or a second variant
group of its own), the valve collapses to plain `Axis` and:

* six variants become three, halving the valve's blocktype count;
* the valve stops being an exception in every rotation path that touches it;
* the flip no longer depends on the rotation path knowing the scheme: `ExOrientation.RotateOrientationToken`,
  which works from the string alone, canonicalises `sn` to `we` and loses it.

A canal end is a separate question, on different grounds: whether `ns` vs `sn` on a canal end encodes flow
direction (keep it) or merely which end the lip is on (fold it into appearance, like the valve).

---

## Cost, roughly

The registry, the two-step rule, the network-oriented declaration and the layout builder's refusal are built.
What remains:

| Step | Size |
|---|---|
| Valve appearance split | real work - a variant group moves, so goldens and a block migration |
