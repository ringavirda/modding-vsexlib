# Framework Composition - form, process, membership
**Status** built, with two parts not built. All three seams are in the code: the process is a behaviour and
readiness a published contract (Seam 1), membership belongs to the cell (Seam 2), and the block-entity base
slot goes to form, with `BlockEntityMachineStation` carrying the container and window plumbing (Seam 3).
Not built: a readiness publisher for megablock footprint intactness, and a window on the rolling mill,
which is a station without one.
**Mod** exlib (every seam), every mod (every consumer)
**Owns** the three axes a machine is composed from, the seams between them, and the rule that decides
whether a capability is a base class or a behaviour.
**Depends on** [multiblock](multiblock.md) (the form vocabulary and the filler mechanism), 
[pipe-network](pipe-network.md), [mp-energy](mp-energy.md), [conventions](../conventions.md) (the
block-size vocabulary, the network families), the vanilla source (`vsapi`, `vsessentialsmod`,
`vssurvivalmod`)
**Extended by** [process-extension](process-extension.md) - this page composes a machine from three
axes; that one settles how a machine learns what it can *make*, and why it may never name a product.

---

## Role

A machine in these mods is three independent things at once: a **shape in the world**, a **process
that runs over time**, and a **member of one or more networks**. C# gives a class one base, so at most
one of the three can be inheritance: process and membership are behaviours a block entity carries,
and form keeps the slot.

This page fixes the axes and the seams between them so that a capability is added by composition,
and states the rule for when something is a base class at all.

---

## How it works

### The three axes

**Form** - how the machine occupies space. The vocabulary is
[conventions](../conventions.md)': `block`, `megablock` (more than one cell, via invisible fillers),
`multiblock` (a pattern the player builds by hand, guided by a projection). Forms compose: a boiler is
an RCC megablock whose construction is gated by a multiblock projection, and a footprint cell can host
real block-entity behaviours on the principal's behalf ([multiblock](multiblock.md), section
Behaviour-capable fillers).

**Process** - what the machine does over time: a periodic tick and the gate that decides whether it
runs. A process is indifferent to form. The same smelting process is equally at home in a
single-cell block, a megablock, or a hand-built multiblock, and nothing about a tick interval or a
production step depends on how many cells the machine covers.

**Membership** - which networks the machine belongs to. This is a **set, not a scalar**: the
converter is on the pipe network for its blast, the molten network for its tap, and the vanilla
mechanical network for its transmission. Membership is per network type, and within a type it is
per face and per cell.

### The rule

> A capability that a block **has** is a behaviour. A capability that a block **is** is a base class.

Membership is had, not been: a pipe is not a kind of network, it is a block that belongs to one. The
same is true of a process. Form is the one axis that genuinely describes what a block *is*, because
it determines placement, collision and breaking, which the engine resolves through the block class
itself.

Vanilla applies the same rule to the same problem. Its mechanical power system puts all network
state and the whole graph walk in a `BlockEntityBehavior`, `BEBehaviorMPBase`, and asks the block
only three questions through `IMechanicalPowerBlock` - `GetNetwork`, `HasMechPowerConnectorAt`,
`DidConnectAt` (`vssurvivalmod/Systems/MechanicalPower/Network/IMechanicalPowerBlock.cs`).
`BlockMPBase` exists but is a convenience; the contract is the interface.

### Seam 1 - form publishes readiness, process consumes it

A multiblock knows whether its pattern is complete. A process needs a gate. The two meet through a
published contract: whatever knows an answer implements `IProductionReadiness`, and the process reads
every publisher on the machine through `ProductionReadiness` and names none of them. The multiblock
form answers `IsReadyToProduce` from its own `CanRunProduction` and `StopsProductionWhenNotReady` from
`StopsProductionOnStructureLost`. The monitor tick drives the transitions through
`ProductionProcess.Start`/`.Stop`, which reach whatever process the machine carries and do nothing
when it carries none.

Form publishes a readiness signal, process reads it. No process needs to know which kind of form
answered.

**Taking the process on is a separate choice from being a multiblock.** The tick lives in
`BEBehaviorProductionMachine`, and a multiblock that also produces derives from
`BlockEntityMultiblockMachine`, which hosts one. A multiblock that only has to be built derives from
the form alone (`BlockEntityMultiblock`) and carries no tick.

**Teardown ordering is the host's choice, and a hosted capability may not assume one.** A block
entity drops every listener it holds *before* either teardown call fans out to its behaviours, so a
host that calls `base` first does the rest of its own teardown with no listeners left, and one that
calls `base` last still has them. Both orderings ship: the multiblock form tears down after `base`,
the furnace core before it, because its fire must be out while the tick still exists. A behaviour
therefore does only order-independent work on those paths - the process forgets its tick handle,
which is idempotent and has no side effect - and a machine with work to finish first does it before
calling `base`.

**A host does not have to be a machine base class.** Anything deriving from `BlockEntity` can add a
`BEBehaviorProductionMachine` in its constructor and publish `IProductionReadiness` from whatever it
already knows, which is what frees the base slot for the axis that needs it.
`BlockEntityRollingMill` is the case: it spends its base on `BlockEntityMachineStation`, carries its
membership and its 250 ms pass clock as nested behaviours, and gets the bounded `dt` of the process.
`BlockEntityMpBench`, the base of the shear and the fastener benches, has the same shape. The machine
tools of the [machining line](https://github.com/ringavirda/modding-vsexmods/blob/main/docs/design/mechanics/machining-line.md),
each of which wants a timed job *and* a window inventory, want it too; they have no block entity yet.
`BlockEntitySmokeStack` registers its gas-graph node by hand in `Initialize` rather than carrying a
membership, because its base is the multiblock.

Readiness is not one signal, and this is what the seam carries:

| Kind | Answered by | Classes |
|---|---|---|
| pattern completion | `BlockEntityMultiblockStructure.StructureComplete` | 11: `BlockEntityMultiblock` and the ten multiblock machines |
| construction completion | `ExRightClickConstructable.IsReadyToProduce`, off the RCC stage count | transmission, engines, boilers |
| peer presence | `BlockEntityEngineSubmachine.CanRunProduction`, `Engine != null` | engine sub-machine |
| work in hand | `IsRolling`, `IsStroking` | rolling mill, `BlockEntityMpBench` |

Only the first is form. Construction completion is published by the RCC behaviour itself, so the
transmission, the engines and the boilers answer `CanRunProduction => true` and leave the gate to it.
Peer presence is topology and work in hand is the machine's own state, so a form-only provider retires
neither. Readiness also has two **levels**, not one: `CanRunProduction` asks *may I run this tick*,
while `StopsProductionOnStructureLost` asks *should I still be ticking at all*. A machine that answers
only the first is frozen with its state held rather than stopped, which is what the breached furnace
relies on (`BlockEntityFurnaceCore.StopsProductionOnStructureLost` answers `false`). The mill and the
benches answer `StopsProductionWhenNotReady` with `false` for the same reason: an idle machine keeps its
clock.

Not built: a readiness publisher for whether a megablock's footprint is intact. Nothing computes it
yet; it is new code, not a refactor.

### Seam 2 - a cell carries membership, not a block class

Membership belongs to the **cell**, not to a kind of block. The walk asks a resolver what participates
at a position and never names a block class:

```csharp
INetworkMember? source = NetworkMembership.Resolve(world, pos, networkType);
if (source == null || !CouplesFrom(world, pos, source))
  yield break;                       // BlockNetworkModSystem.GetConnectedNeighbors
```

`GetConnectedNeighbors` is the only walk there is. The fracture walk in `ReviewConnectivity` and
`RebuildFromRoot` both step through it rather than testing anything themselves, so there is a single
place a node is resolved and it names no class.

`Resolve` answers in two arms: a `BEBehaviorNetworkMember` on the cell's block entity whose network
type matches, else the block itself through `INetworkConnector`. Both produce the same
`INetworkMember`, and the neighbour side resolves through exactly the same call
(`BlockNetworkModSystem.IsValidNetworkNeighbour`), so a block carrying a membership is walked **to** as
well as **from**. Resolving only the block on the far side would leave the graph one-directional.

The block arm is there because a block **with no block entity in a loaded chunk** is a real state:
`BlockConverterIntake` is an `INetworkConnector` that is deliberately not a node and has no block
entity, and a node whose block entity was torn down while its chunk stayed resident goes on answering.
A chunk unload is not that case - it takes the block too, and the graph handles it separately (see
below).

Why the resolver rather than a base class. C# gives a class one base, so if a node had to *be* a
`BlockNetworkNode`, a block that spent its base elsewhere could only reach a network through
`INetworkConnector` - a connection target, never a member - and no megastructure could be a node at
all. A filler cell shows the same rule from the other side: it is a plain `Block`, yet it joins the
**vanilla** mechanical network, because vanilla resolves its nodes by behaviour. A cell - principal or
filler - carries one membership behaviour per network it joins, and [multiblock](multiblock.md), section
"A filler cell is a graph node when it declares one" is where a footprint cell declares its.

### Membership is addressed by network type, never by CLR type

`BlockEntity.Behaviors` is a plain `List<BlockEntityBehavior>`, so several membership behaviours can
coexist on one block entity. Vanilla's `BlockEntity.GetBehavior<T>()` returns the **first** match, which
is why exlib supplies its own accessor keyed on network type and face rather than reuse vanilla's.

This is the one place the framework goes past vanilla, which never needed it: a vanilla block entity
carries at most one `BEBehaviorMPBase`.

### A per-cell answer must be a plain public class member

`INetworkMember` is answered by two very different kinds of implementor, so two separate C# dispatch
rules decide whether an override is reached at all. Both fail the same way: the wrong shape compiles,
and the interface's default answers instead of the code that was written.

**A block declares the per-cell pair as ordinary public members.** `INetworkConnector` supplies
`INetworkMember.HasConnectorAt` and `INetworkMember.IsConnectionBroken` as *explicit* default
implementations, which is the only shape that compiles: redeclaring them non-explicitly hides the
base member rather than implementing it (`CS0108`, then `CS0535` on every implementor that answers
only `HasConnectorAt(face)`). Interface mapping searches the class hierarchy before falling
through to a default, so `BlockStructureFiller`'s plain public `NetworkTypeAt`/`HasConnectorAt` and
`BlockCastIronBevel`'s `override` of `BlockNetworkNode`'s virtual both outrank it. An *explicit*
`bool INetworkMember.HasConnectorAt(...)` on one of those classes would not, because nothing can
override an explicit implementation, and the per-cell answer would become unreachable.

**A behaviour subclass must override a base virtual.** Interface mapping is fixed at the class that
lists the interface. `BEBehaviorNetworkMember` lists `INetworkMember`, so a subclass declaring a
matching public member without re-listing the interface never enters the map, and the base's answer
keeps winning. That is why `BEBehaviorNetworkMember` declares every member a subclass may answer
differently - `NetworkType`, `NetworkTypeAt`, `HasConnectorAt`, `IsConnectionBroken`,
`IsNetworkEndPoint`, `AcceptsNeighbour` - as a `public virtual`, instead of inheriting any of them as
a default. `NetworkType` included: a hosted membership does not own its answer, and a copy of it
drifts (see below).

The network-membership tests guard both shapes against production classes, and ask each through an
`INetworkMember`-typed reference - the only way to exercise the map rather than the class member:
`NetworkMembershipAccessorTests` the block side against `BlockStructureFiller` and a `BlockNetworkNode`
subclass, `NetworkMembershipGraphRegistrationTests` the behaviour side against the membership
`BlockEntityPipe` hosts.

### Both arms of the resolver ask the same question

`NetworkMembership.Resolve` finds a membership behaviour first and the block second, and both arms
compare `NetworkTypeAt(world, pos)`, never the declared `NetworkType`. Keying the two on different
properties would make a membership whose type varies by cell, which is exactly what a filler cell is,
unfindable at a position where the equivalent block is found. `MemberOf(be, networkType)` is the
position-less accessor for callers holding only a block entity, and matches the declared type.

### A membership may state its own connector faces

`BEBehaviorNetworkMember.Connectors` holds the faces the membership couples on. Empty - the default - 
means the block answers, which is what every network node block relies on: a membership added to a
node block inherits its orientation without restating it. A non-empty set outranks the block, because
the two cases that need one have a block that cannot answer for the cell at all. A plain `Block` is no
`INetworkConnector`, so a membership on one would report no connector on any face and bridge nothing.
A filler cell's block is the single shared `exlib:structurefiller` singleton - always north, no
variants - so its port face comes from the footprint declaration instead.

Faces are stated either as an orientation string of single-letter side codes (`"ns"`, `"we"`,
`"nsewud"`, mapped through `BlockNetworkModSystem.SideToFace`) or as `BlockFacing`s directly, which is
what a filler hands over: its declared face arrives already rotated into the placed orientation. A
JSON `connectors` key reaches the same property through `Initialize`.

A set already configured in code therefore wins over a JSON declaration, and the disagreement is
logged - the same precedence and the same noise as a losing `networkType` declaration. The reason is
the rotation: a configured face names a face the cell actually exposes, while a declaration is written
in the unrotated frame, so a declaration that won would move the port.

### A hosted membership registers, and the engine fixes when

`BlockEntityNetworkNode` hosts a private nested `BEBehaviorNetworkMember`, `HostMembership`, that
forwards to it and does the graph registration. A nested type reaches its enclosing type's private and
protected members, so the node's own surface needs no change of visibility.

Persistence stays on the block entity. Vanilla fans behaviour persistence out over the block entity's
**own flat tree**, with no subtree (`BlockEntity.ToTreeAttributes`, `BlockEntity.FromTreeAttributes`),
so if a block entity and its behaviour both wrote network state they would write the same keys and the
second writer would win. Exactly one thing may write them, and it is the block entity - which is also
what keeps `BlockEntityPipe`'s conditional keys, and its rewrite of `temp`/`medium`/`pressure` *after*
`base.ToTreeAttributes`, working.

Three orderings the engine dictates, each of which silently produces a wrong answer if ignored:

- **The membership is added in the constructor.** `BlockEntity.FromTreeAttributes` and `.Initialize`
  both fan out over `Behaviors`; a membership added any later misses whichever has already run.
- **It never holds a copy of its network type.** Every point at which one could be taken is wrong.
  A derived field initializer runs before the base constructor body, so the constructor sees the
  declared default rather than the loaded value; `FromTreeAttributes` reaches the behaviours *before*
  it assigns the block entity's own `NetworkType`, so the tree fan-out is too early; and
  `FromTreeAttributes` runs again on **every** `MarkDirty(...)`, so even a value taken at `Initialize`
  drifts afterwards. `redrawOnClient` does not gate that: `BlockEntity.MarkDirty` calls
  `MarkBlockEntityDirty` unconditionally and only then, `if (redrawOnClient)`, adds the mesh redraw,
  so the bare no-argument call resyncs the tree just the same. The hosted membership therefore
  *overrides* `NetworkType` and reads its owner's live. `BlockEntityFluidIntake` is the case that
  proves all three: its `NetworkType` is a plain auto-property the save tree fills in, and it calls
  bare `MarkDirty()` once a second.
- **It reads the state to restore before `AddNode`, not after.** `AddNode` broadcasts, the broadcast
  reaches `OnNetworkUpdate`, and that clears the saved state it is about to restore.

Two more traps around the same method. `properties` is null on the programmatic path - 
`BlockEntityBehavior.Initialize` sets only `Api`, and only `BlockEntity.CreateBehaviors` assigns
`properties` - so a membership added in code must guard every read of it. And
`BlockEntityBehavior.Api` is assigned *only* by that same `Initialize`, while a block entity can be
handed an api without being initialised, so anything gated on the behaviour's own `Api` silently
never runs on that path; teardown reads `Blockentity.Api`, which is assigned before the behaviour
fan-out and is therefore set whenever the behaviour's is.

**A declared `networkType` loses to a block entity that already names one, and the disagreement is an
error.** The JSON key is for a membership with no other source - a plain block's, or a filler cell's.
A block entity's own answer is a compiled contract, and every node family but `BlockEntityFluidIntake`
implements the setter as a no-op, so a declaration that "won" would vanish on the way through and the
cell would register under the constant regardless; on the intake, which has a real setter, it would
win *and* be persisted by `ToTreeAttributes`. Both silent outcomes read to whoever wrote the JSON as
if it had taken effect, and they disagree with each other, so neither may be silent.

A membership that ends up naming no network at all is logged as an **error** and joins nothing.
Registering a blank type throws out of the network factory lookup, and this runs inside a chunk load,
so one bad declaration would take a world down; but a cell silently outside its run reads to a player
as a network that stopped working, which is not a warning.

Teardown is removal-only: `BEBehaviorNetworkMember.OnBlockRemoved` drops the node, and a chunk unload
leaves it registered, because deregistering on unload would fracture a live run every time a player
walked away. The file carries the `removal-only teardown:` marker that `TeardownSymmetryTests` reads as
an exemption, and the guard fails a file that carries the marker without overriding `OnBlockRemoved`,
so a stale marker cannot quietly exempt whatever lands in that file next.

Keeping the node is only half of it. An unloaded chunk hides the **block** as well as the block
entity, so neither arm of the resolver answers there and the cell reads to the walk as empty space - 
the block arm keeps a cell walkable across an absent block entity, not across an absent chunk. The
graph therefore suspends its fracture check rather than answering it whenever part of a network is
unreadable; see [pipe network](pipe-network.md), section 1, "An unloaded chunk suspends the fracture check".

### Seam 3 - the block-entity base slot belongs to form, and nothing else may squat in it

A machine that belongs to a network is not *a kind of network node*: a rolling mill is a machine that
belongs to one. If the base slot goes to `BlockEntityNetworkNode`, such a machine cannot be a
**container**, and a container is what a machine with a window is.

So a machine takes the base its form calls for and hosts the rest. The mill derives from
`BlockEntityMachineStation` - exlib's container-with-a-window-and-a-handshake - and carries its
membership and its pass clock as behaviours. `BlockEntityNetworkNode` is the base of the fifteen block
entities that are only nodes (pipes, valves, canals, shafts, the flywheel, the tuyere and the like); it
is a convenience for them, never a requirement.

**The container base carries the stacks.** The mill's roll set and piece are inventory slots, and the
station base persists them and maps their collectible ids - the mapping whose absence corrupts a
schematic paste.

**A machine that once kept loose stacks adopts them on load.** A save that holds the mill's stacks at
the tree root as `rmPiece`/`rmRollSet` is read by `BlockEntityRollingMill.MigrateLooseStacks`, a one-way
adopt into the slots. Without it such a mill comes back with its roll set gone and any piece mid-pass
destroyed - and the world looks fine, because nothing errors.

**Vanilla's multiblock model is not used.** `vsessentialsmod/Block/BlockMultiblock.cs` offers three
levels of multiblock modularity and an `IMultiBlock*` interface family whose every hook carries a
`Vec3i offset`. It needs **a blocktype per offset** (`multiblock-monolithic-{dx}-{dy}-{dz}`, generated
over a range) and puts **no block entity on a filler cell at all**. Ours puts the offset in the filler's
block entity - which is the only reason Seam 2 can host a graph node there, and the only reason a
footprint cell can carry a behaviour on its principal's behalf. Adopting their vocabulary would retract
that. Worth borrowing and not borrowed: their `is BlockMultiblock` recursion guards; a port forwarder
that reaches itself overflows the stack.

### What stays a base class

Little. Once membership, process and structure-completion are behaviours, a shared block-entity root
holds only what every block entity needs and none can be trusted to repeat by hand: teardown that
runs identically whether the block was removed or its chunk unloaded, disposal of renderers, dialogs
and looping sounds, and declared fields that round-trip through the attribute tree.

That last one is a class of bug, not an inconvenience. A field written only by a server tick, read
in `GetBlockInfo`, and absent from `ToTreeAttributes` reads zero on the client for ever - and pays for
a chunk re-tesselation to synchronise a value it never sends if it also calls `MarkDirty(true)`.
Declared fields are built: `ExBlockState` holds a block entity's persisted fields, declared once or
marked `[Persist]`, and `ExBlockEntity`, `ExBlockEntityContainer`, `ExBlockEntityBehavior` and the
machine bases read and write through it. `BlockEntityPressureValve._lastVentVolume` is such a field,
and `PressureValveReadoutTests` holds its round trip.

Not built: teardown and disposal in the shared root. Instead `TeardownSymmetryTests` requires whatever
cleans up in `OnBlockRemoved` to clean up in `OnBlockUnloaded` too, or to carry the removal-only marker.

---

## Consequences

- A filler cell can be a graph node: [multiblock](multiblock.md), section "A filler cell is a graph
  node when it declares one".
- `INetworkConnector` is not a mechanism **for membership**. A block that is *on* a network has a
  membership behaviour, and there is no second way. The interface is a **port** - a face another
  network may couple to, on a block that is not itself a graph member. A boiler (`BlockBoiler`) is a
  port: a connection target, not a node. The interface is also how a *block* answers
  `INetworkMember`, which is what keeps a cell resolvable when it has no block entity.
- The peripheral-block pattern is available, not required. Splitting a machine's connections across
  cells is often the right shape physically - an intake belongs where the pipe arrives - but it is not
  the only way to be on more than one network.

Unchanged by this page: every network's own physics and state model. This page fixes where membership
is recorded and how the graph is walked. It does not touch what flows.

---

## Code

Membership - the built axis, in `src/ExpandedLib/Networks` unless named.

| Type / member | Role |
|---|---|
| `INetworkMember` | the whole source-side contract, six members; what the walk holds |
| `INetworkConnector` | a port, and the block-side answer to `INetworkMember`; 12 implementors, of which only `BlockNetworkNode` is a graph node itself |
| `NetworkMembership.Resolve` | membership behaviour first, block second; the only place a node is resolved |
| `.MembersOf` / `.MemberOf` | every membership on a block entity, and the one declaring a network type |
| `.CouplesAt` | network-agnostic: does this cell expose a connector on this face, from whichever side answers |
| `BEBehaviorNetworkMember` | one membership; registers its cell and drops it on removal |
| `.Connectors` / `.DeclareConnectors` | the faces it couples on, empty to leave the answer to the block |
| `.ConfigureFromFiller` | takes a footprint cell's face, already rotated into the placed orientation |
| `.Initialize` / `.OnBlockRemoved` | the graph join, and the removal-only teardown |
| `BlockEntityNetworkNode.HostMembership` | private nested membership forwarding to its owner; persistence stays on the owner |
| `BlockNetworkModSystem.GetConnectedNeighbors` | the one walk; every other traversal steps through it |
| `.IsValidNetworkNeighbour` | the far side, resolved by the same call as the near side |
| `.GetOpenConnectorFaces` | open ends; does not gate its source (see [pipe network](pipe-network.md), section 1) |
| `.ReviewConnectivity` | fracture, settle, or suspend when part of the run is unreadable |
| `BlockNetworkNode` | abstract `Block`; 20 subclasses across exlib and the family mods; a convenience, not the contract |
| `BEBehaviorMPFillerPort` (`src/ExpandedLib.Industry/MechanicalPower`) | the same shape for vanilla MP |

Form - the freed base slot, in `src/ExpandedLib/Machines` unless named.

| Type / member | Role |
|---|---|
| `BlockEntityMachineStation` | container + window + packet handshake; the base a machine with slots takes |
| `.OnReceivedClientPacket` | **sealed**, so no override can drop the claim check by forgetting `base` |
| `.OnStationPacket` | where a machine adds its own actions, from `FirstMachinePacketId` |
| `.CreateDialog` | null leaves the machine windowless, which the mill, the shear and the fastener benches are |
| `MachineSlotSpec` / `MachineStationInventory` | slots as data; built through `InventoryGeneric`'s own slot-factory delegate |
| `BlockEntityRollingMill` (iiex) | container base, membership + process as behaviours |
| `.MigrateLooseStacks` | the one-way adopt of root-level `rmPiece`/`rmRollSet` into the slots |
| `BlockEntityMpBench` (iiex) | the same shape for the shear and the fastener benches |
| `BlockEntityDesignTable`, `BlockEntityWorkbench` (iiex) | stations with a window; keep only their slot rules and their own state |

Form and process, in `src/ExpandedLib/Machines` and `src/ExpandedLib/Structures`.

| Type / member | Role |
|---|---|
| `BEBehaviorProductionMachine` | the process: the tick, its gate, teardown and away-catch-up |
| `IProductionReadiness` / `ProductionReadiness` | one publisher's answer, and the two aggregates over every publisher on a machine |
| `ProductionProcess` | starts and stops a machine's process without naming the class that carries it |
| `BlockEntityProductionMachine` | hosts a process for a machine that is not a multiblock |
| `ExRightClickConstructable` (`src/ExpandedLib/Blocks/Construction`) | publishes construction completion as readiness, unless `gatesProduction` is false |
| `MachinePorts` | network access as extensions on any `BlockEntity`, so it is not a machine's to inherit |
| `BlockEntityMultiblockStructure` | the form: the pattern, its monitor tick, and the readiness it publishes |
| `BlockEntityMultiblockMachine` | the form plus a hosted process; what the ten multiblock machines derive from |
| `BlockEntityMultiblock` | the form alone, for a multiblock with no tick |
| `BlockStructureFiller` | a plain `Block`; hosts behaviours, and a cell of it is a node when it declares one |

Vanilla, as the worked example.

| Type / member | File | Role |
|---|---|---|
| `IMechanicalPowerBlock` | `vssurvivalmod/Systems/MechanicalPower/Network/IMechanicalPowerBlock.cs` | the entire block-side contract: 3 methods |
| `BEBehaviorMPBase` | `vssurvivalmod/Systems/MechanicalPower/BlockEntityBehavior/BEBehaviorMPBase.cs` | all network state and the walk, in a behaviour |
| `BEBehaviorMPBase.spreadTo` | same | resolves the neighbour by `GetBehavior<BEBehaviorMPBase>()`, then asks the block through the interface |
| `BlockMultiblock` + `IMultiBlock*` | `vsessentialsmod/Block/BlockMultiblock.cs` | three documented levels of multiblock modularity; every hook carries a `Vec3i offset` |
| `BlockMPMultiblockGear` / `BEMPMultiblock` | `vssurvivalmod/Systems/MechanicalPower/Block/BlockMPMultiblockGear.cs`, `.../BlockEntity/BEMultiblock.cs` | vanilla's own invisible filler and its principal back-pointer |
