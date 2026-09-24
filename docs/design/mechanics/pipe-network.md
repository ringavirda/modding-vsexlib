# Pipe Network

**Status** live   **Mod** `exlib` owns the graph substrate and the pipe network itself - `BlockNetworkModSystem`,
`BlockNetwork`, `BlockNetworkNode`, `BlockEntityNetworkNode` and the membership types live in
`src/ExpandedLib/Networks`; `PipeNetwork`, `PipeNetworkState`, `BlockPipe`, `BlockEntityPipe`,
`BlockPipePassthrough` and `ChimneyVent` in `src/ExpandedLib.Industry/Pipes`. `iiex` owns the plated and cast
tiers, every other fitting, and the registration of the pipe network with its chimney vent; `siex` owns the
rolled tier (segments only).

**Owns**
- The shared block-network graph substrate used by every network in the suite: node add/remove, merge, BFS fracture detection, `RebuildFromRoot`, per-second tick dispatch, the `dt` catch-up clamp, open-connector (leak) detection, the connector-reciprocity rule, `AcceptsNeighbour`, and `IsConnectionBroken` re-walk. Other network pages cite this one for those facts.
- The pipe pool model: one medium per run, capacity `nodes x LitresPerPipe`, gas pressure as a volume ratio, liquid pressure as fill-ratio-then-pump-pressure.
- Uniform network temperature (no spatial gradient) and where phase change is allowed to happen.
- Burst pressure per material tier and the joint-family rule, including which blocks are exempt from both.
- Leak, chimney-vent, evaporation, passive-cooling, throughput-EMA and over-pressure-burst behaviour, and every constant behind them.
- The plain valve (in-line sever) and the pressure valve (directional overflow), including the gate constants.

**Depends on**
- [molten network](molten-network.md) - the other consumer of the same graph substrate.
- `ExpandedLib.Catalogues.ExLiquids` / `assets/*/config/liquids.json` - the medium catalogue: which codes exist, their phase, their boil/condense points and volume factors. This page states only how the pipe network consults the catalogue, never what is in it.
- The iiex steam machines that produce into and consume from a run, and their own rates -
  [Cornish boiler](https://github.com/ringavirda/modding-vsexmods/blob/main/docs/design/machines/boiler-cornish.md),
  [Watt engine](https://github.com/ringavirda/modding-vsexmods/blob/main/docs/design/machines/engine-watt.md),
  [pumps & fluid intake](https://github.com/ringavirda/modding-vsexmods/blob/main/docs/design/machines/pumps.md).

---

## Role

One network type carries air, steam, exhaust and water between machines:

- A blower or boiler pushes into a run; a furnace tuyere, engine or cowper draws from it.
- The run's pressure gates progression: a blast furnace demands a pressure derived from its burden, and the pipe tier the player can build caps what any blower can deliver. Pipe strength is the tier gate.
- Leaks, bursts and closed valves are the failure surface, all local and all visible.

A run is one homogeneous pool, not a fluid simulation: no flow direction inside a run, no per-cell pressure, no temperature gradient. Everything spatial happens at the edges of a run - where it is produced into, drawn from, vented, split by a valve, or refused by an incompatible joint.

---

## How it works

### 1. The shared graph substrate

Every network in the suite - pipe, molten, mpenergy - is a `BlockNetwork` subclass managed by one `BlockNetworkModSystem`. The manager does graph work only; all typed state lives in the subclass.

| operation | member | behaviour |
|---|---|---|
| register a type | `BlockNetworkModSystem.RegisterNetworkType` | `RegisterNetworkType(name, factory)` in `ModSystem.Start`; a later factory for the same type replaces the earlier one. exlib's industry module registers all three (`IndustryModule.RegisterNetworkTypes`); iiex replaces `pipe` with one carrying its chimney vent (`IronIndustryExpandedModSystem.Start`) |
| add a node | `BlockNetworkModSystem.AddNode` | isolated -> new network; otherwise joins `adjacentNetworks[0]` and merges the rest into it, each merge gated by `BlockNetwork.CanMerge` |
| remove a node | `BlockNetworkModSystem.RemoveNode` | removes, then hands the rest to `ReviewConnectivity` |
| review connectivity | `BlockNetworkModSystem.ReviewConnectivity` | walks from any node (`WalkFromAnyNode`); all reached -> `Settle`, same instance kept; some unreached and every node readable -> `Fracture`, each component rebuilt as its own network with `OnSplitFragment`; some unreached and any node behind an unloaded chunk -> **suspended**, network left whole |
| rebuild | `BlockNetworkModSystem.RebuildFromRoot` | BFS-discovers everything reachable, tears down overlapping networks, and preserves the old root network's state via `InheritStateFrom` |
| tick | `BlockNetworkModSystem.StartServerSide`, `BlockNetworkModSystem.ServerTick` | one server listener at 1000 ms, `dt` clamped to 2 s; resumes suspended reviews (`ResumeSuspendedReviews`) then dispatches `OnTick` to every live network |
| broadcast | `BlockNetwork.BroadcastUpdate` | pushes the typed state payload to every `INetworkNode` BE in the run |

Connectivity is reciprocal and four-way gated. `BlockNetworkModSystem.IsValidNetworkNeighbour` is the single chokepoint that both the traversal and the leak scan go through. Source and neighbour are resolved identically, by `NetworkMembership.Resolve` - a membership behaviour on the cell's block entity, else the block itself - so neither side names a block type and a block that spent its base class elsewhere still walks. A neighbour connects only when all of:

1. `Resolve` finds a membership there for the source's `NetworkTypeAt(world, sourcePos)`,
2. it exposes a connector on the touching face - `HasConnectorAt(world, pos, facing.Opposite)`,
3. `source.AcceptsNeighbour(neighbourBlock)` and `neighbour.AcceptsNeighbour(sourceBlock)` are both true - the physical-joint test, asked of both sides so a refusal holds whichever cell the walk starts from,
4. it is neither an `IsNetworkEndPoint` nor severed - `BlockNetworkModSystem.CouplesFrom`.

A connector face that passes 1-4 nowhere, and that the source block does not accept as a non-network
connection (`BlockNetworkNode.IsValidNonNetworkConnection`, false unless a block overrides it), is an **open
end** (`BlockNetworkModSystem.GetOpenConnectorFaces`). The pipe tick sorts open ends by what they face
(`PipeNetwork.ClassifyOpenings`): a face the vent strategy claims is a vent, a face onto air is a leak, and a
face onto any other block is neither - it moves nothing and does not count towards `OpeningsCount`.

The **source** cell is gated differently by the two consumers, deliberately. The traversal asks `CouplesFrom` of the source as well (`BlockNetworkModSystem.GetConnectedNeighbors`), so an endpoint or a severed cell yields no graph neighbours at all. The open-end scan does not: whether a face is open is a physical fact rather than a graph one, and a closed valve, a solidified canal and a pressure valve all still meet the pipe they touch. Gating the scan on the source too would cap a closed tap's inlet a second time over its own end-cap mesh (`BlockEntityMoltenCanalTap.OnTesselation`) and report every endpoint's coupled face as a leak.

Connectors read the adjacent cell, not their own. Two position-aware members carry that: `INetworkMember.NetworkTypeAt(world, pos)` and `INetworkMember.HasConnectorAt(world, pos, face)`, which `INetworkConnector` supplies for a block as an explicit default forwarding to `INetworkConnector.HasConnectorAt(face)`. A node block answers the type from `BlockNetworkNode.NetworkType` and the connector from its `orientation` variant (`BlockNetworkNode.HasConnectorAt`); a per-cell connector - a megablock structure filler exposing a port on exactly one footprint cell - declares them as plain public members that outrank the default, reads the block entity at `pos`, and stays inert everywhere else. So a machine need not be a network node to be plumbed in: a structure block implementing `INetworkConnector` is a valid connection target but is never added to the graph (`INetworkConnector`).

The machine side of the same rule is `MachinePorts`: `be.ConnectedNetwork<PipeNetwork>(face)` resolves the
network in the cell across the connector face, and returns `null` unless the cell over there presents a
connector back - asked of whatever speaks for it, a membership or the block
(`BlockNetworkModSystem.GetConnectedNetworkAcross`, `NetworkMembership.CouplesAt`). A pipe merely sitting
adjacent with its connectors pointing elsewhere is not plumbed in. The fixed machines on a pipe run (boilers,
the engine and its sub-machines, the pumps, the steam condenser, the pressure valve, the converter, the cowper)
all resolve their ports through `GetConnectedNetworkAcross`.

State survives unload: `BlockEntityNetworkNode` serialises the last broadcast state (`BlockEntityNetworkNode.ToTreeAttributes`, `FromTreeAttributes`, `SerializeNetworkState`) and the cell's membership injects it back into the freshly built network on `Initialize`, capturing it before `AddNode` can null it (`BEBehaviorNetworkMember.Initialize`, `SavedNetworkState`). The block entity stays the only writer: vanilla fans behaviour persistence over that same flat tree, so a membership that persisted anything would collide with the keys already there. `SaveFormatTests` holds the saved keys fixed.

#### An unloaded chunk suspends the fracture check

The walk cannot tell "there is nothing here" from "I cannot see here." `IBlockAccessor.GetBlock` answers the air block for an unloaded chunk rather than null (`IBlockAccessor.GetBlock`), and `GetBlockEntity` answers null, so an unloaded cell resolves to no member at all - exactly like an empty one. Nothing about the resolver changes that: the block arm keeps a node walkable across a block entity dropped on its own, not across a chunk that went away.

The graph outlives the unload - nothing calls `RemoveNode` there (`BEBehaviorNetworkMember.OnBlockRemoved`, `BlockEntitySmokeStack.OnBlockRemoved`) - so the node set stays right while the cells behind it are invisible. What breaks is the **fracture check**, which reads unreachable as gone. Left alone it splits a run around a player who walked away, and the run stays split: the returning cell finds its position already in a network and never re-joins (`BEBehaviorNetworkMember.Initialize`).

So the check is **suspended rather than answered** when it comes up short and any node of the network is unreadable (`BlockNetworkModSystem.ReviewConnectivity`). The network is left whole and the positions the walk could not read are recorded against it; `ServerTick` re-decides it once at least one of them is back (`BlockNetworkModSystem.ResumeSuspendedReviews`). Three things follow from the shape:

1. Positions are recorded, not chunk coordinates. Asking `GetChunkAtBlockPos(pos)` is dimension-aware by contract; deriving a chunk key by hand means reproducing the engine's convention, and vanilla's mechanical-power version of this gets it wrong - `spreadTo` divides raw `.Y` rather than `.InternalY` (`BEBehaviorMPBase.spreadTo`). Several missing chunks are simply several positions, and each review recomputes the whole set, so a partial return shrinks it.
2. The re-decision runs on the tick, not on `Event.ChunkDirty`. A chunk event fires while the chunk's block entities may not be back, and a footprint cell answers *only* through its block entity, so a review in that window would read the chunk as loaded and the cell as absent and split a healthy run. The tick cannot land inside a load - both are main-thread - and it needs no reason filter and no module-level "everything is loaded" flag, the pair that lets vanilla's re-trigger go stale (`MechanicalPowerMod.Event_ChunkDirty`). It is idempotent: a review is a recomputation from the current world, so running it again on an unchanged world changes nothing.
3. A network entirely behind unloaded chunks is covered by the same rule, not a special case - no node is readable, so nothing is decided. The cost when nothing is suspended is one dictionary count per second.

The trade-off is deliberate: while a run is suspended, a break that really did cut it keeps both halves
sharing one pool until the chunk returns. Answering the check instead would shred the unloaded half into one
network per cell, permanently.

### 2. One medium per run

`PipeNetworkState` is a single pool: `Volume`, `MaxVolume`, `Temperature`, `MediumType`, `Pressure`, `FeedPressure`, `OpeningsCount`, `FlowRate`.

The medium is claimed by the first producer and held until the run is empty. Both producers refuse a run carrying an incompatible medium only while it physically holds something:

```
if (State.Volume > 0f && !_taxonomy.Compatible(State.MediumType, gasType)) return false;
```
- `PipeNetwork.TryProduceGas` (gas) and `PipeNetwork.TryProduceLiquid` (liquid). The guard is on `Volume > 0`,
  not on the label: a drained run keeps its label for a few seconds as a display ghost (see section 6) and a
  new medium must be able to re-claim it.

Compatibility comes from the injected `IMediumTaxonomy`, defaulting to `ExLiquids.Taxonomy`: an empty run
accepts anything; two gases always mix; two liquids mix only if identical; gas and liquid never mix
(`IMediumTaxonomy.Compatible`). When gases mix, the dominant label is the higher `Priority`
(`IMediumTaxonomy.HigherPriority`). Which codes exist, and their priorities, boil points and volume factors,
is `ExLiquids`' fact, not this page's.

On merge, incompatible media cannot blend: the larger run wins outright and the smaller's contents are discarded (`PipeNetwork.OnMerge`).

### 3. Capacity and pressure

```
MaxVolume = Nodes.Count x LitresPerPipe

gas    Pressure = Volume / MaxVolume, uncapped     (PipeNetworkState.ComputeGasPressure)
liquid Pressure = Volume / MaxVolume  while below capacity
                = FeedPressure        once brim-full  (PipeNetworkState.ComputeLiquidPressure)
```

A gas is compressible and may sit above 1 atm; a liquid cannot be packed past `MaxVolume`, so once the line
is full its pressure is whatever the pump commands (`FeedPressure`, set by the producing pump in
`PipeNetwork.TryProduceLiquid`).

A gas producer's injection is clamped by two ceilings (`PipeNetwork.TryProduceGas`):

- its own choke, `maxOutputPressure` (a blower's rating), and
- `MinBurstPressure` - the weakest burstable pipe in the whole run.

and additionally by 1 atm if the run is leaking, unless `bypassLeakCap` is set. That clamp, not the leak loss,
is what stops a leaking run building pressure. Every produce and consume call is also bounded by the run's
throughput (see [A run gates its own throughput](#1-a-run-gates-its-own-throughput-and-the-gate-is-a-weakest-link-walk)).

### 4. Uniform temperature

`PipeNetworkState.Temperature` is one network-wide value (default 20 °C). Every producer blends it
volume-weighted into the existing pool (`PipeNetwork.TryProduceGas`, `PipeNetwork.TryProduceLiquid`), merges
blend the same way (`PipeNetwork.OnMerge`), and split fragments copy it (`PipeNetwork.OnSplitFragment`).
`IPipeNode.Temperature` states the rule: uniform across the run, with no gradient.

The network itself never changes phase. Two passive effects cool a run - a gas leak drops it 5 °C, and passive
cooling sheds `PipeGasCoolPerSecond` (2 °C/s, dt-scaled) toward `PipeAmbientTemperature` whenever the run holds
gas above ambient - but steam only becomes water at an active device that supplies the cooling: the steam
condenser, which draws steam from one face and injects condensate into the water line crossing its other two
(`BlockEntitySteamCondenser.Process`). The condenser consults `CondensationTarget`/volume factor through the
taxonomy and falls back to iiex's own steam-expansion default (`SteamExpansionFactor`) when the def leaves it
open.

### 5. Burst pressure by tier, and joints

Two orthogonal per-tier axes, both registered by **tier** from each mod's `ModSystem.Start`. The tier is the
block's own `tier` variant (`BlockPipe.Tier`), the high-order segment of its code:
`pipe-{tier}-{type}-{orientation}`. `BlockPipe.ResetForWorld` clears every registration when a world starts
loading, before the tiers' mods register again.

**Rating** - `BlockPipe._burstByTier`, filled by `BlockPipe.RegisterBurst` and resolved by the block's own tier in `BlockPipe.BurstPressure`:

| tier | domain | key | value | registration | config |
|---|---|---|---|---|---|
| plated | `iiex` | `PlatedPipeBurstPressure` | 2.5 | `IronIndustryExpandedModSystem.Start` | `IiexConfig.PlatedPipeBurstPressure` |
| cast | `iiex` | `CastPipeBurstPressure` | 5.0 | `IronIndustryExpandedModSystem.Start` | `IiexConfig.CastPipeBurstPressure` |
| rolled | `siex` | `RolledPipeBurstPressure` | 12 | `SteelIndustryExpandedModSystem.Start` | `SiexConfig.RolledPipeBurstPressure` |
| (no tier, or unregistered) | - | `DefaultBurstPressure` | 5, hard-coded | - | `BlockPipe.DefaultBurstPressure` |

The rating doubles as the tier's buffer size: a run holds `burst x pipes x LitresPerPipe`, so the plated tier is both the low-pressure tier and the small-buffer one.

Only a block whose class is `BlockPipe` itself participates: `CanBurst => GetType() == typeof(BlockPipe)`
(`BlockPipe.CanBurst`). That is the four segments and the pipe indicator, which iiex declares with the base
class (`PipeIndicatorDefinitions`), so an indicator bursts and caps throughput at its tier like a segment.
Every fitting is a subclass and is therefore exempt by default - it neither bursts nor caps the run's pressure
or throughput (`BlockPipe.MaxThroughput`). Outlet and passthrough additionally override
`BurstPressure => float.MaxValue` (`BlockPipeOutlet.BurstPressure`, `BlockPipePassthrough.BurstPressure`). The
outlet names no tier and would take the default anyway; the passthroughs are tiered, but for identity rather
than rating - see below.

**Joint** - `BlockPipe._jointByTier`, two families:

| family | constant | tiers | registration |
|---|---|---|---|
| `flanged` | `BlockPipe.FlangedJoint` | plated, cast - both square in section, bolted through flanges - **and every untiered `BlockPipe` subclass** (outlet, tuyere, twin-tub MP blower, steam hammer), which take the flange by default (`BlockPipe.JointFamily`) and so are reachable from either | `IronIndustryExpandedModSystem.Start` |
| `welded` | `BlockPipe.WeldedJoint` | rolled - octagonal and welded, no flange to bolt to | `SteelIndustryExpandedModSystem.Start` |

```csharp
public override bool AcceptsNeighbour(Block neighbour) =>
    neighbour is not BlockPipe other || other.JointFamily == JointFamily;
```
- `BlockPipe.AcceptsNeighbour`. Rolled pipe joins only rolled pipe. Anything that is not a `BlockPipe` - a
  machine port, a condenser, a fluid intake - is unaffected, because those are ports on a machine, not
  lengths of run. Two consequences:

- Because the valve, pressure valve, outlet, passthrough, indicator, tuyere, blower and steam hammer are all
  `BlockPipe` subclasses, a rolled run cannot reach any of them. siex ships no fittings of its own, so a
  rolled run is segments, machine ports and the fittings that are not `BlockPipe` (fluid intake, steam
  condenser, injector).
- A refused joint is an open end, but not a leak: the neighbour is not air, so `PipeNetwork.ClassifyOpenings`
  does not count it, `IsLeaking` stays false, and the two runs simply stay apart. The refusal is checked in
  `IsValidNetworkNeighbour`, which asks both sides: a refusal from either holds whichever cell the walk starts
  from, so the pair cannot be connected from one direction and open from the other
  (`BlockNetworkNode.AcceptsNeighbour`, `BlockNetworkModSystem.IsValidNetworkNeighbour`).

**Burst mechanics.** A run that sits at or above its weakest burstable pipe's rating with nowhere to vent accumulates `_overpressureSeconds`; at `PipeOverpressureSeconds` one random qualifying pipe fails (`PipeNetwork.TickOverpressureAndBurst`, `PipeNetwork.MinBurstPressure`, `PipeNetwork.CollectBursts`). Any relief that drops the pressure below the rating resets the grace (`PipeNetwork.TickOverpressureAndBurst`), and the timer is transient - a reload resets it (`PipeNetwork._overpressureSeconds`). Failure drops the pipe's items, puffs steam, pops, removes the node (fracturing the run) and sets the cell to air (`PipeNetwork.ExecuteBurst`). Burst selection prefers the world RNG so a seeded world is deterministic (`PipeNetwork.CollectBursts`).

### 6. Tick order

`PipeNetwork.OnTick` runs a fixed pass sequence, and the order matters:

| # | pass | effect |
|---|---|---|
| 0 | fold accumulators, drop `_minBurstCache` and `_minThroughputCache` | runs even for a null state |
| 1 | `SmoothFlow` | EMA of throughput, idle timer |
| 2 | `RecomputePressureAndFlow` | refresh `MaxVolume`, pressure, displayed flow |
| 3 | `ComputeLeakFractions` | particle density only |
| 4 | `ClassifyOpenings` | one pass over nodes: count `IPipeNode` consumers, classify each open face as vent (strategy) or leak (air), fire `OnLeak`/`OnOpenConnectorsChanged`, refresh `OpeningsCount` |
| 5 | `ApplyVentDraw` | the injected `IPipeVentStrategy` pulls gas out through vents |
| 6 | `ApplyLeakLoss` | leak volume loss |
| 7 | `ApplyEvaporation` | calendar-based water loss |
| 8 | `RepressureAfterVentLeak` | gas only |
| 9 | `ApplyPassiveCooling` | gas only, whenever the run holds gas above ambient |
| 10 | `ClearIfEmptyAndIdle` | drops `State` once drained and idle for `EmptyClearDelaySeconds` |
| 11 | `TickOverpressureAndBurst` | last, after the broadcast, so it never mutates the node set while another pass reads it |

The vent strategy is injected per network at registration, so the network core never hard-wires a policy.
exlib ships `ChimneyVent`, and iiex injects it with its own draw rate when it registers the pipe network
(`IronIndustryExpandedModSystem.Start`). It classifies a vanilla-or-modded chimney (matched by code substring,
`ChimneyVent.IsChimney`) capping the top connector of an `IChimneyVentable` fitting as a vent, and draws
`ChimneyGasDrawRate` L/s per chimney with smoke and a fire-roar loop. A network with no strategy vents
nothing - every open end onto air is a leak.

### 7. Plain valve = in-line sever

`BlockEntityValve` is a normal pipe node while open; closed, it severs the run at its own cell:

```csharp
public override bool IsConnectionBroken() => !_open;   // BlockEntityValve.IsConnectionBroken
```

`IsConnectionBroken` is consulted by the graph in two places, both through `BlockNetworkModSystem.CouplesFrom`
- the traversal's early bail for the source cell (`GetConnectedNeighbors`) and the neighbour test
(`IsValidNetworkNeighbour`) - so a closed valve is isolated from both sides. Toggling does an explicit
`RemoveNode` + `AddNode` so the split or re-merge happens immediately rather than at the next rebuild
(`BlockEntityValve.ToggleOpen`).

Closing also discards the cached pool (`BlockEntityValve.DiscardNetworkPool`, called from `ToggleOpen`).
Without that, the pressurised state the valve cached while open would serialise into the now-isolated
single-cell network and be restored on load - into a cell whose `MaxVolume` is one pipe - bursting it.
`BlockEntityValve.FromTreeAttributes` drops it again before `Initialize` can capture it.

The state is shown by holding the shape's `open` animation pose; the animator is rebuilt on wrench rotation,
but only on a real orientation change, because a network re-walk can re-exchange to an equivalent variant and
would otherwise reset the pose (`BlockEntityValve.OnExchanged`).

### 8. Pressure valve = directional overflow

`BlockEntityPressureValve` extends the pipe BE and is a one-way relief between two different runs. Its block
is a network endpoint (`BlockPressureValve.IsNetworkEndPoint`), so the walk never continues through it: the
valve sits in a one-cell network of its own and the runs on its two faces stay separate. It ticks once a
second (`BlockEntityPressureValve.OnTick`) and:

1. Reads the input face `orientation[0]` and output face `orientation[1]`; a wrench flip swaps `ns` <-> `sn`
   to reverse the direction (the six `orientation` variants of `BlockPressureValve`).
2. Resolves both sides through `GetConnectedNetworkAcross`, so a pipe adjacent but not facing the valve is not
   plumbed in.
3. Runs `OverflowGas` then `OverflowLiquid`.

**Gas** (`BlockEntityPressureValve.OverflowGas`):

```
allowed = gatePressure x inState.MaxVolume
if inState.Volume <= allowed                          -> nothing
if the output run carries water                       -> nothing
excess = inState.Volume - allowed
```
- Output leaking: feed it only `min(excess, GasLeakRate)` with `bypassLeakCap: true`, so the trickle flows
  straight through and out.
- Output pressurised: gas flows downhill only - if `outPressure >= inPressure` nothing moves, otherwise it
  moves `min(excess, equalise)` where `equalise` is the exact amount that balances the two runs' pressures,
  with the output ceiling additionally capped at the input pressure as a double guard. Without the downhill
  rule a loop would pump itself up until its pipes burst.
- No output network at all: the face is an open end and vents to atmosphere at `min(excess, GasLeakRate)` with
  particles and a swoosh.

**Liquid** (`BlockEntityPressureValve.OverflowLiquid`): once `inState.Pressure` (the pump-set feed pressure on
a brim-full line) tops the gate, water moves into the output's free space, or sprays out capped at
`LiquidLeakRate` when there is no output run. A gas-carrying output takes no water.

The gate is player-dialled in `GatePressureStep` increments between `MinGatePressure` and the valve's own
material rating (`BlockEntityPressureValve.AdjustGatePressure`), and clamped to that range again on load.
`MaxGatePressure` reads `BlockPressureValve.BurstPressure`, which resolves through the per-tier registry off
the valve's own `tier` variant - `cast` or `plated` - so it tops out at 5 or 2.5 atm even though the valve
itself never bursts (`CanBurst` is false for any subclass).

---

## Numbers

### exlib config - `ExlibConfig.cs`, file `ModConfig/ex_values.json`, section `exlib`

| key | value | what it does |
|---|---|---|
| `LitresPerPipe` | `30` | litres one pipe holds at 1 atm; run capacity is this x node count. Range-guarded >= 1 because it divides pressure |
| `GasLeakRate` | `8.0` | gas lost per leaking tick, and the cap on a pressure valve's and a steam condenser's vent-to-atmosphere |
| `LiquidLeakRate` | `10.0` | water drained per second while leaking |
| `EvaporationLitresPerDay` | `50` | water lost per in-game day, measured off the calendar |
| `PipeOverpressureSeconds` | `30` | grace at burst pressure before a pipe lets go |
| `PipeGasCoolPerSecond` | `2.0` | °C/s a gas run sheds toward ambient, dt-scaled, whenever it holds gas above ambient |
| `PipeAmbientTemperature` | `20` | °C passive cooling and the gas-leak drop stop at |

### Per-tier content config

| key | value | what it does |
|---|---|---|
| `PlatedPipeBurstPressure` | `2.5` | iiex tier rating (and buffer multiplier) |
| `PlatedPipeThroughput` | `50` | L/s the plated tier passes; the run's cap is the weakest segment |
| `CastPipeBurstPressure` | `5.0` | iiex tier rating |
| `CastPipeThroughput` | `120` | L/s the cast tier passes |
| `ChimneyGasDrawRate` | `16.0` | L/s one chimney draws through a ventable fitting |
| `RolledPipeBurstPressure` | `12` | siex tier rating |
| `RolledPipeThroughput` | `250` | L/s the rolled tier passes |

The first five are `IiexConfig`, the last two `SiexConfig`. Throughput caps only blocks that can burst:
fittings and ports are exempt - a tuyere is the machine's intake, not a length of main.

### Hard-coded - not config

| constant | value | where | note |
|---|---|---|---|
| network tick interval | `1000 ms` | `BlockNetworkModSystem.StartServerSide` | makes every "per tick" rate a per-second rate |
| `dt` catch-up clamp | `2 s` | `BlockNetworkModSystem.ServerTick` | stops a rejoin leaping the burst grace in one step |
| `FlowSmoothingAlpha` | `0.3` | `PipeNetwork` | EMA weight for the displayed throughput |
| `EmptyClearDelaySeconds` | `3` | `PipeNetwork` | how long a drained run keeps its medium label |
| flow-EMA cutoff | `0.01 L` | `PipeNetwork.SmoothFlow` | below this the EMA snaps to 0 |
| `DefaultBurstPressure` | `5` | `BlockPipe` | fallback for a tier that never registered a rating |
| `DefaultThroughput` | `120 L/s` | `BlockPipe` | fallback for a tier that never registered a throughput |
| gas-leak temperature drop | `5 °C`, floor `PipeAmbientTemperature` | `PipeNetwork.ApplyLeakLoss` | per tick, not dt-scaled |
| gas leak particle ramp | `1 -> GasLeakRate`, clamp `0..4` | `PipeNetwork.ComputeLeakFractions` | density only |
| water leak particle ramp | `1 -> 5 L`, clamp `0..1` | `PipeNetwork.ComputeLeakFractions` | density only |
| pressure broadcast epsilon | `0.02 atm` | `PipeNetwork.RecomputePressureAndFlow` | also the HUD sync threshold (`BlockEntityPipe.OnNetworkUpdate`) |
| flow broadcast epsilon | `0.01 L/s` | `PipeNetwork.RecomputePressureAndFlow` | |
| brim-full liquid epsilon | `0.001 L` | `PipeNetworkState.ComputeLiquidPressure` | |
| burst-pressure comparison epsilon | `0.001 atm` | `PipeNetwork.TickOverpressureAndBurst`, `PipeNetwork.CollectBursts` | |
| `MinGatePressure` | `0 atm` | `BlockEntityPressureValve` | pressure-valve floor |
| `GatePressureStep` | `0.25 atm` | `BlockEntityPressureValve` | per interaction |
| default gate pressure | `1 atm` | `BlockEntityPressureValve` | also what a save without a stored gate reads back as |
| default state temperature | `20 °C` | `PipeNetworkState.Temperature` | |
| chimney fire-loop restart | the `Fire` clip's length, 9260 ms | `ChimneyVent.Vent`, `ExSounds.ClipLengthMs` | the loop replays once the clip has finished |
| pipe ambience chance / range | `0.08` per second, 7 m, vol 0.25 | `BlockEntityPipe.OnAmbientTick` | gas above 1 atm bubbles, water trickles |

### Pipe geometry (code-first defs, shared by all three tiers)

Every tier calls the same `BlockPipe.Segments(domain, tier)` factory, each provider passing its own tier -
iiex via `PlatedPipeDefinitions` and `CastPipeDefinitions`, siex via `RolledPipeDefinitions`. Four blocktypes
per tier: straight, bend, tjunction, xjunction (`BlockPipe.Straight`, `Bend`, `TJunction`, `XJunction`).
Collision/selection is a 5/16->11/16 core. Max stack: 16 straight, 8 for the rest. Each tier ships its own
shapes at `{domain}:pipe/{tier}/*` (`BlockPipe.Asset`) - a tier is a different model, not a tint. The plated
and cast providers add the tier's two passthroughs from `BlockPipePassthrough.Passthroughs`; rolled has none.

A null `tier` yields the same four blocktypes with no tier axis and the default rating, throughput and joint. That is what `BlockPipe.Definitions` uses to derive `AllowedOrientations` (the map reads only `type` and `orientation`, so no tier is needed and none is invented), and what a consumer shipping one pipe family gets.

---

## Code

| type | role |
|---|---|
| `BlockNetworkModSystem` | the graph manager: `RegisterNetworkType`, `GetConnectedNetworkAcross`, `AddNode`, `RemoveNode`, `RebuildFromRoot`, `GetOpenConnectorFaces`, `GetConnectedNeighbors`, `IsValidNetworkNeighbour` |
| `BlockNetwork` | abstract base: `Nodes`, `State`, `OnTick`, `OnMerge`, `OnSplitFragment`, `InheritStateFrom`, `OnTopologyChanged` |
| `BlockNetworkNode` | abstract block base: orientation/placement/wrench, `AcceptsNeighbour`, `HasConnectorAt`, `IsNetworkEndPoint`, `IsValidNonNetworkConnection` |
| `BlockEntityNetworkNode` | block entity base: hosts the cell's membership and persists orientation and network state; `IsConnectionBroken`, `OnNetworkUpdate` |
| `BEBehaviorNetworkMember`, `NetworkMembership` | a cell's membership, and the resolver that asks a membership first, then the block |
| `INetworkConnector` | the extension point for a machine port. Implement it to be a valid pipe target without joining the graph |
| `IPipeNode` | the extension point for a producer/consumer. Implement it to inject/withdraw without inheriting `BlockEntityPipe` |
| `IBurstablePipe` | opt into the burst model: `CanBurst` + `BurstPressure` |
| `IThroughputLimitedPipe` | opt into the throughput cap: `MaxThroughput` |
| `IPipeVentStrategy` | the injected vent policy; `ChimneyVent` is exlib's implementation, injected by iiex |
| `IMediumTaxonomy` | the injected medium policy; `ExLiquids.Taxonomy` is the default |
| `PipeNetwork` | the pool: `TryProduceGas`, `ProduceGasMeasured`, `TryConsumeGas`, `TryProduceLiquid`, `ProduceLiquidMeasured`, `TryConsumeLiquid`, `OnTick`, `MinBurstPressure`, `MinThroughput` |
| `PipeNetworkState` | the state object + the two pressure formulas (`ComputeGasPressure`, `ComputeLiquidPressure`) |
| `MachinePorts` | `be.ConnectedNetwork<TNet>(face)` / `be.NetworkAt<TNet>(pos)` - the one place the "port = the cell across the face" rule lives |
| `BlockPipe` | segments + the burst, throughput and joint registries |
| `BlockEntityPipe` | `IPipeNode` delegating to the network; leak particles (`OnLeak`); HUD (`GetBlockInfo`) |
| `BlockEntityValve` / `BlockValve` | in-line sever |
| `BlockEntityPressureValve` / `BlockPressureValve` | directional overflow |

**Blocks on the pipe network**

| block | mod | class | notes |
|---|---|---|---|
| pipe straight/bend/T/X | iiex (plated, cast), siex (rolled) | `BlockPipe` | burstable |
| pipe indicator | iiex (plated, cast) | `BlockPipe` | burstable and throughput-limited at its tier, like a segment |
| valve, pressure valve | iiex (plated and cast variants) | `BlockValve`, `BlockPressureValve` | the pressure valve is a network endpoint |
| outlet | iiex | `BlockPipeOutlet` | `IChimneyVentable`; `BurstPressure = MaxValue`; names no tier |
| passthrough, passthrough-bend | iiex (plated **and** cast) | `BlockPipePassthrough` | `IChimneyVentable`; `BurstPressure = MaxValue`; **tiered** - see section 5 |
| fluid intake | iiex | `BlockFluidIntake` | a `BlockNetworkNode`, not a `BlockPipe`; its BE is a bare `BlockEntityNetworkNode`, not an `IPipeNode` |
| steam condenser, injector | iiex | `BlockSteamCondenser`, `BlockInjector` | `INetworkConnector` only |
| boiler, engine, engine fluid pump, manual fluid pump, jet condenser | iiex | `BlockBoiler`, `BlockEngine`, `BlockEngineFluidPump`, `BlockManualFluidPump`, `BlockEngineJetCondenser` | machine ports |
| tuyere, twin-tub MP blower, steam hammer | iiex | `BlockTuyere`, `BlockTwinTubMPBlower`, `BlockSteamHammer` | `BlockPipe` subclasses - non-bursting, and flanged, so they refuse a rolled run |
| Lancashire boiler, converter intake, cowper intake, engine air blower | siex | `BlockBoilerLancashire`, `BlockConverterIntake`, `BlockCowperStoveIntake`, `BlockEngineAirBlower` | machine ports |
| smokestack intake | siex | `BlockSmokeStackIntake` | its BE (`BlockEntitySmokeStack`) is an `IPipeNode` that registers its own cell as a pipe node by hand |

---

## Gotchas

1. Never gate a tick pass on `pass.Consumers == 0`. `ClassifyOpenings` increments `Consumers` for every
   `IPipeNode` block entity, and `BlockEntityPipe` implements `IPipeNode`, so "no consumers" is unsatisfiable
   on any run containing a segment; a pass behind that guard is dead code in game. `ApplyPassiveCooling`
   runs regardless of `pass.Consumers` and its doc comment says so. A headless test of tick-time node
   classification needs real block entities in the run: a run of bare blocks has `Consumers` 0 and passes
   whether the feature works or not. iiex's `PipeTestWorld.LiveRun`, its one pipe-run fixture, attaches a
   `BlockEntityPipe` to every cell.

2. Gas leak loss is not dt-scaled; liquid leak loss is. `ApplyLeakLoss` uses `LiquidLeakRate * dt` for water
   but a bare `GasLeakRate` for gas. With the `dt` clamp at 2 s, a catch-up tick leaks twice as much water but
   the same gas.

3. The leak rates' doc comments do not describe `ApplyLeakLoss`. `ExlibConfig.GasLeakRate` and
   `ExlibConfig.LiquidLeakRate` are documented as a rate per open-ended connector; the loss pass takes one
   fixed amount per network however many ends are open (Gotcha 4). The leak also does not stop a leaking run
   building pressure: that outcome comes from the 1-atm production clamp in `PipeNetwork.TryProduceGas`. The
   only calculation that looks at the volume above 1 atm is `ComputeLeakFractions`, which sizes particles.

4. Leak loss is per-network, not per-opening. One open end and twenty leak the same volume
   (`PipeNetwork.ApplyLeakLoss`); `TotalLeaks` only gates whether it leaks and how dense the particles are. Bulk
   venting needs a chimney or a stack.

5. `"gas"` is a dead network type. The doc comments on `BlockNetwork.NetworkType`, `BlockNetworkNode.NetworkType`
   and `INetworkNode.NetworkType` give `"gas"` as an example network type. Only `"pipe"`, `"molten"` and
   `"mpenergy"` are ever registered (`IndustryModule.RegisterNetworkTypes`, and iiex's replacement `pipe` in
   `IronIndustryExpandedModSystem.Start`).

6. A `RemoveNode` that does not fracture keeps the same network instance (`BlockNetworkModSystem.Settle`).
   Anything cached on the instance survives. `PipeNetwork` handles this by overriding `OnTopologyChanged` to
   drop `_minBurstCache` and `_minThroughputCache`; `MoltenNetwork` does not override it at all - see
   [molten network](molten-network.md) Gotcha 3.

7. `MinBurstPressure` is `float.MaxValue` when the run holds no burstable pipes. A run of only machine ports
   and passthroughs therefore has no production ceiling from the pipe side (`TryProduceGas` clamps against
   `MaxValue`) and can never burst (`TickOverpressureAndBurst`). The same run has no throughput cap either:
   `MinThroughput` is `float.MaxValue` too.

8. `PoolVolumeCeiling` bypasses the cache. Merge/split call `ComputeMinBurstPressure` directly, walking every
   node, while the hot path uses `MinBurstPressure`. Correct, but an O(N) walk in the merge path.

9. A closed valve must clear its saved pool, and it must do so twice. Once on close
   (`BlockEntityValve.ToggleOpen`) and again in `BlockEntityValve.FromTreeAttributes` before `Initialize` runs.
   Miss either and a reload restores a pressurised pool into a one-cell network and bursts it.

10. The pressure valve is not a valve. It never toggles and it never bursts; it is a scheduled once-per-second
    transfer between two adjacent runs. It is a network endpoint, so its two sides are always separate runs
    and the valve itself is a one-cell network in between. Two sides that meet again elsewhere are one run,
    and the valve then moves nothing: gas only flows downhill, and one run has one pressure.

11. The pressure valve's ceiling comes from a tier, not from its own strength. `MaxGatePressure` reads
    `BurstPressure` off the block, which resolves from `_burstByTier[Tier]`: 5 for the `cast` variant, 2.5
    for `plated`. The valve is exempt from bursting, so this is a rating borrowed from that tier's plain pipe.
    The one recipe (`CastPipeRecipeDefinitions`) outputs the cast variant and consumes a **plated** segment,
    so a crafted valve is built from one tier and rated at the other; no recipe outputs a plated valve or
    pressure valve.

12. Chimney vents are matched by code substring. `block?.Code?.Path?.Contains("chimney")`
    (`ChimneyVent.IsChimney`). Any block whose path contains "chimney", from any mod, becomes a gas sink on the
    top connector of a ventable fitting.

13. `bypassLeakCap` is easy to misuse. It lifts the 1-atm clamp on a leaking run (`PipeNetwork.TryProduceGas`).
    It is only correct for a caller that has already hand-limited its volume to the leak rate; anything else
    pressurises a leaking line.

14. Incompatible merges destroy content silently. Joining a water run to a gas run keeps only the larger pool
    and discards the smaller (`PipeNetwork.OnMerge`). No warning, no drop, no particle.

15. `OnTopologyChanged` runs on the primary network only during a merge (`BlockNetworkModSystem.AddNode`); the
    merged-away networks are simply dissolved.

16. Node registration happens when the block entity's membership initialises (`BEBehaviorNetworkMember.Initialize`),
    not in `OnBlockPlaced`. Calling `AddNode` from `OnBlockPlaced` would trigger a redundant O(N) broadcast and
    freeze the server on large networks (`BlockNetworkNode.OnBlockPlaced`).

17. A node added beside an unloaded chunk never merges with what is over there. `AddNode` resolves neighbours
    through the same blind walk, so the new cell forms a network of its own - and the neighbour, already in
    the graph from before its chunk left, skips `AddNode` on its return (`BEBehaviorNetworkMember.Initialize`),
    so nothing ever joins the two. Separate from the suspended fracture check, which only defers a *split*;
    suspension records the network's own nodes, and this cell is not one of them. A player interaction cannot
    reach it (the chunks around a player are loaded), but a tick-driven `RemoveNode` + `AddNode` at the edge of
    the loaded area can: `BlockEntityMoltenCanal.ResyncNetworkNode` and `BlockEntityValve.ToggleOpen`. Not
    built: the fix, if it is ever worth one, is to make `AddNode` merge for a position it already holds and
    call it unconditionally on load, rather than to widen suspension.

18. `RebuildFromRoot` drops the nodes it cannot see. It tears down every overlapping network including their
    unreadable nodes and rebuilds only what the walk reached (`BlockNetworkModSystem.RebuildFromRoot`), so
    cells behind an unloaded chunk leave the graph entirely and rejoin by `AddNode` when their chunk returns.
    Self-healing, and its one caller is a player-driven local action (`BlockEntityMoltenCanal.ClearSolidified`),
    but the run's state is redistributed in the meantime.

---

## Throughput, bore and stored heat

### 1. A run gates its own throughput, and the gate is a weakest-link walk

The run carries a per-run flow cap, derived from the tier as a weakest-link walk over
`IThroughputLimitedPipe.MaxThroughput` (`PipeNetwork.MinThroughput`, `PipeNetwork.PerCallLimit`) - the
structural mirror of `MinBurstPressure`, cached the same way and invalidated by the same `OnTopologyChanged`.
Every produce and consume call is bounded by it, as well as by headroom and availability. Precedent in the
same substrate: `MoltenFlowRate` = 50 u/s gates the molten network per transfer.

The machine-side rates stand. The machines' own per-second constants (tuyere 14 L/s, cowper 24, smokestack 48,
chimney 16 ...) gate what a machine draws; the run's cap gates what the plumbing can carry.

### 2. Bore is not built - and if it ever is, it is orthogonal to tier

No large-bore pipe family. Three reasons, any one sufficient:

* [Fluid tank](https://github.com/ringavirda/modding-vsexmods/blob/main/docs/design/machines/fluid-tank.md)
  sets it out: "there is no such thing as a node with its own capacity", and making one means changing
  thirteen sites, the highest-risk route. A large-bore segment is a capacity-bearing node.
* Physically backwards. Hoop stress is `σ = pr/t`, so at the same wall a wider bore bursts at lower
  pressure. Bore and burst are not independent, and `PoolVolumeCeiling` multiplies capacity by the burst
  rating, so a fatter pipe would silently buy more over-pressure headroom.
* Cost against value: a full mirror is +20 blocktypes, +394 variants, +20 goldens, +18 shapes, while no recipe
  makes a cast or a rolled segment.

The numbers to size it do not exist yet.
[Gas system, section 9](https://github.com/ringavirda/modding-vsexmods/blob/main/docs/design/mechanics/gas-system.md#9-open)
lists the 48 L/s exhaust retune, burner consumption, grade calorific values and holder capacity as underived,
so a bore set sized against the 48 L/s figure would be sized against an acknowledged placeholder. Build the
gate, measure, then decide.

> If bore is ever added, it must be orthogonal to tier, never a fourth rung on the pressure ladder.
> The largest pipework in a works carried the lowest pressure: blast, gas and exhaust run 24-160 L/s at
> 1.25-2.75 atm, while steam runs 30-32 L/s at up to 12 atm. Friction loss goes as `Δp proportional to Q²/D⁵`,
> so halving a bore multiplies the drop x32, which is why a service with only a few psi of head must buy area
> instead. A large-bore plated blast main and a small-bore rolled steam pipe are both correct; folding bore
> into the pressure ladder produces "the strongest pipe is also the fattest", which is false.

### 3. Stored gas cools - including gas parked in a pipe run

Passive cooling fires on any run holding gas above ambient (Gotcha 1), so a main loses heat exactly as a
holder does and a long run cannot be a gasholder that stores gas hot. The price
[gas system](https://github.com/ringavirda/modding-vsexmods/blob/main/docs/design/mechanics/gas-system.md#5-buffering---allowed-and-priced-in-heat)
sets for buffering ("smoothness or temperature, never both") therefore holds universally, and the holder is
not strictly worse than pipe at its own job. It also gives run length its first consequence: a long main costs
flame temperature.

## Open

- No pressure drop along a run. Length is free for pressure: a one-block run and a 300-block run
  behave identically apart from capacity. Length is not free for temperature - see Stored gas cools, above.
  Whether pressure should also fall with distance remains undecided.
- siex has segments but no fittings. The welded joint means a rolled run cannot use iiex's valve / pressure
  valve / outlet / passthrough / indicator (`BlockPipe.AcceptsNeighbour`). Until siex ships its own, a rolled
  run is segments, machine ports and the non-`BlockPipe` fittings.
- Gas leak loss is not dt-scaled (Gotcha 2) and the two leak paths should probably agree.
- Burst selection is uniform-random among qualifying pipes (`PipeNetwork.CollectBursts`). There is no notion of the pipe nearest the producer, or of fatigue.
- Only one pipe fails per burst event, then the grace resets - so a run held over-pressure loses one pipe every `PipeOverpressureSeconds`, indefinitely.
- Phase change is condenser-only. The taxonomy already exposes passive `TryCondensation`/`TryVaporisation` (`IMediumTaxonomy`), but nothing in `PipeNetwork.OnTick` calls them - a steam line below 100 °C does not condense on its own.
- A suspended review suspends the *whole* network, not the part it cannot see. A component of readable nodes with no cell adjacent to an unreadable one is provably isolated and could be split off at once, leaving only the fog and what touches it waiting. Not built, because a partial split needs the surviving network to be *debited* by whatever the fragment takes: `OnSplitFragment` hands a fragment its proportional share on the assumption the original is dissolved, so splitting one component off a surviving network would create material from nothing in all three subclasses. That is a new state-partition contract on `BlockNetwork`, not a graph change. Until then, a break in a loaded area is deferred whenever any part of the same run is away.
