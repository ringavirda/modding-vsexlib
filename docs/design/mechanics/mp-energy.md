# MP Energy Network
**Status** live   **Mod** exlib (graph + physics), iiex (every block)
**Owns** the `"mpenergy"` network: the one-spinning-shaft model (`E = 1/2Iω²`, `I*dω/dt = τ_drive - τ_load - τ_fric`), the four node contracts (`IMpEnergyProducer` / `IMpEnergyStorage` / `IMpEnergyConsumer` / `IMpEnergyDirection`), merge/split semantics, the vanilla-MP bridge at the flywheel hub and its torque curve, the transmission's two-network gear coupling, the direction flag, `MaxSpeed` and everything derived from it, the animation-speed convention, and every `Mp*` / `Flywheel*` / `ShaftInertia` config key.
**Depends on** [multiblock](multiblock.md) (the fillers the flywheel/transmission/mill reserve their volume with, and how a filler cell joins a run), [conventions](../conventions.md) (R7, the network-family list)

---

## Role

Vanilla MP has no storage. This network models energy in a spinning shaft rather than "power available", so
the machines that matter - a rolling pass, a hammer blow - can draw far more torque for two seconds than a
period prime mover produces continuously.

A flywheel is inertia, not a battery: a drive that cannot out-torque the load plus standing friction never
spins the wheel up at all, so a run can never be trickle-charged into a pulse.

At the iron tier the prime mover is a vanilla waterwheel or windmill, bridged into the run through the
flywheel's hub cell. In iiex the player swaps it for a steam engine; the network itself is unchanged.

---

## How it works

### One run = one lumped shaft

Every connected `"mpenergy"` node set is one `MpEnergyNetwork` with one `MpEnergyNetworkState`. All
quantities are SI: ω rad/s, I kg*m², τ N*m, E joules, P watts. Display conversion is `ExMeasure`'s job -
`ExMeasure.Speed` (rpm), `ExMeasure.Power`, `ExMeasure.Energy`, `ExMeasure.Charge` (charge %).

`BlockNetworkModSystem.ServerTick` ticks every live network once a second, and that `dt`, capped at 2 s, is
the `dt` handed to `OnTick`.

### The tick

`MpEnergyNetwork.OnTick` walks the node set once at the run's current speed and sums three things plus a
flag:

| Sum | From |
|---|---|
| `I` = Σ `Inertia` | every `IMpEnergyStorage` |
| `τ_drive` = Σ `DriveTorque(ω)` | every `IMpEnergyProducer` |
| `τ_load` = Σ `LoadTorque(ω)` | every `IMpEnergyConsumer` |
| `Reversed` | any `IMpEnergyDirection` with `IsReversed` |

All three sums clamp their per-node contribution at >= 0 (`Math.Max(0f, ...)`), so a node cannot supply
negative inertia or negative load. The run is reversed when any direction node reports reversed: two drives
fighting each other is a build error, not a state worth modelling.

A run with no inertia is dropped entirely: `if (inertia <= 0f)` nulls the state and broadcasts, so `State`
is `null` and every reader sees ω = 0.

### The integration step

`MpEnergyNetworkState.Step` is pure and unit-testable without a world:

```
τ_fric = frictionCoeff*ω + max(0, idleTorque)        // windage + standing resistance
τ_net  = τ_drive - τ_load - τ_fric
Δω     = τ_net / I, dt                              // (0 when I <= 0)
ω      = clamp(ω + Δω, 0, maxSpeed)
E      = 1/2Iω²                                        // EnergyAtSpeed
P_sup  = τ_drive*ω     P_dem = τ_load*ω              // display only
```

Everything follows from the sign of `τ_net`, with no special cases:

- The idle torque is a resistance, never a source, and ω is clamped at 0, so a shaft the drive cannot
  start stays stopped. This is the "not a battery" gate.
- Sustained under-torque winds ω down to a hard stall.
- A cut drive lets a charged wheel coast down at `b*ω + τ_idle`.
- A large `I` barely moves under a brief load spike or between engine strokes: that is the buffering.
- There is no over-speed mechanic. The `ω_max` clamp is the whole governor; a destructive over-speed burst
  is not modelled.

`StoredEnergy` is written every step from ω and I, so it is a derived readout, not an independent
accumulator:

| Quantity | Formula |
|---|---|
| `CapacityFor(I, ω_max)` | `1/2*I*ω_max²` |
| `DeriveSpeed(E, I)` | `sqrt(2E/I)`, 0 when `I <= 0` |
| `EnergyAtSpeed(I, ω)` | `1/2Iω²` |

Capacity is derived, never declared: storage nodes contribute inertia only, so a full reservoir is exactly a
flywheel spinning at `ω_max`.

### Torque governs, not power

`IMpEnergyProducer.DriveTorque(speed)` returns a torque-speed curve, not a flat power: a flat power would let
an under-powered drive buffer its way past any load, a curve lets it stall. `IMpEnergyConsumer.LoadTorque(speed)`
is the mirror.

The forming consumers' implementations are independent of speed (`BlockEntityRollingMill.LoadTorque`,
`BlockEntityShear.LoadTorque`, `BlockEntityFastenerBench.LoadTorque`): plastic deformation resists the same
however fast the rolls turn, unlike the friction term, which eases off as ω falls. That asymmetry lets a pass
drag a run all the way to a stall instead of settling at a slower equilibrium.

### Direction

ω is unsigned; the whole balance is magnitudes. Direction rides alongside as `State.Reversed`, entering the
network only at the bridge: `BlockEntityFlywheel.IsReversed` hands on `BEBehaviorMPFillerPort.IsReversed` of
the first turning hub port. Its one consumer is the rolling mill: `BlockEntityRollingMill.DriveReversed`
swaps `InputDeck`/`OutputDeck`.

### The vanilla-MP bridge

The flywheel is a storage node, a producer and a direction node (`BlockEntityFlywheel`). It does not talk to
the vanilla MP network itself: its hub footprint cell(s) host a `BEBehaviorMPFillerPort` (the `'M'` glyph in
`BlockFlywheel`'s footprints), a real `BEBehaviorMPBase` participant on the vanilla graph, whose speed the
flywheel BE reads back and converts:

```
BlockEntityFlywheel.BridgeDriveTorque(hubSpeed, maxTorque, ratedHubSpeed)
  = 0                                    when ratedHubSpeed <= 0 or hubSpeed <= 0
  = maxTorque, clamp(hubSpeed/rated, 0, 1)
```

Linear up to rated, flat above it, so an over-driven axle never pushes harder than rated. At rest this is
full torque, so the bridge can start a stopped load; if that full torque is still below `τ_load + τ_idle`,
the shaft never spins up.

`BlockEntityFlywheel.HubAxleSpeed` takes the fastest turning hub port. Hub cells are hard-coded per size:
normal `(0,1,0)`, large `(0,2,0)` and `(0,2,1)` - two hubs on the large wheel so an axle couples from either
shaft face. Each hub declares both a north and a south port spec (`BlockFlywheel.MpNorth`, `MpSouth`), and a
through port (`BEBehaviorMPFillerPort.Through`, the default) also connects the opposite end of its axis at
`Initialize`, so a row of ports merges into one vanilla network and power passes straight through.

`BlockEntityFlywheel.DriveTorque(shaftSpeed)` ignores the mpenergy shaft speed; the `ω_max` clamp in `Step`
is the only governor.

### Transmission: coupling two separate runs

A transmission is not a graph node. `BlockEntityTransmission` reads the mpenergy network on each of its two
port cells via `GetNetworkAt` and projects them onto a gear constraint without ever merging them
(`TryCouple`, `ReadSides`). South port is `+Z`, north port is `-Z` in the machine's own frame.

`MpEnergyNetworkState.CoupleRatio`:

```
E_total = (1/2*I_s*ω_s² + 1/2*I_n*ω_n²), clamp(retention, 0, 1)
I_comb  = I_s + I_n / ratio²                                    // north reflected to the south side
ω_s     = min(sqrt(2*E_total / I_comb), maxSpeed)
ω_n     = ω_s / ratio
```

Energy is conserved exactly when `retention = 1` and nothing clamps; that is the test invariant. Because
`ratio >= 1`, `ω_n <= ω_s`, so the single clamp on ω_s keeps the constraint valid. Speed, not `StoredEnergy`,
is the source of truth, which makes it robust to a stale energy field. A no-op when either side has no
inertia. `TryCouple` refuses when the two ports resolve to the same network (`ReferenceEquals`, a run looped
back through the transmission).

`retention = 1 - MpGearMeshLoss*dt` (`TryCouple`), so the mesh loss is a per-second fraction, dt-scaled at
the coupling tick.

### Merge and split

| Event | Behaviour | Member |
|---|---|---|
| Merge | pool `Inertia` and `StoredEnergy`, clamp E to the merged capacity `1/2*(I1+I2)*ω_max²`, re-derive ω; a side with no state takes the other's | `MpEnergyNetwork.OnMerge` |
| Split | each fragment is seeded with its own inertia, `E*(I_frag / I_orig)` and the ω derived from them; a fragment with no inertia has no state | `MpEnergyNetwork.OnSplitFragment` |
| Fragment inertia | summed straight off the fragment's storage nodes, used only at split time; the next tick recomputes it | `MpEnergyNetwork.FragmentInertia` |
| Rebuild | `InheritStateFrom` hands the whole state object across | `MpEnergyNetwork.InheritStateFrom` |

### Client sync and animation

The network broadcast only reaches server BEs, so the flywheel round-trips the run state onto its own tree
(`SerializeNetworkState` / `DeserializeNetworkState`) and pushes it to clients on a throttled `MarkDirty`
(`OnNetworkUpdate`). The throttle fires when the displayed speed moves by >= 2 % of full scale or on a stop.
The transmission does the same for its two side speeds (`BlockEntityTransmission.SyncSideSpeeds`), since it
is not a graph node and receives no broadcast.

Clips are authored as one revolution, so the animation playback multiplier is the shaft's revolutions per
second, `ω / 2π` (`EnergyAnim.SpinSpeed`). A shaft below 1 % of ω_max reads as stopped and rests on `idle`
(`EnergyAnim.IsTurning`); one clip must always be active or the animator drops the suppressed mesh back to
the static shape (`BlockEntityFlywheel.ApplySpin`).

Bevel branch spin sign falls out of the mitre pair rather than a lookup:
`ω_branch = -sign(branchFace, its own axis), ω_driver`, so the two branches of one bevel turn opposite ways
(`EnergyAnim.BranchSpinSign`).

---

## Numbers

### Framework constants - exlib config, `ex_values.json` (domain `exlib`)

Read live each tick through `ExlibValues` (`MpEnergyNetwork.FrictionCoeff`, `IdleTorque`, `MaxSpeed`), so
retuning needs no rebuild.

| Key | Value | What it does |
|---|---|---|
| `MpFrictionCoeff` | `0.05` | windage/bearing coefficient `b` (N*m per rad/s); the speed-proportional drain that winds an unpowered run down |
| `MpIdleTorque` | `0.5` | standing-resistance floor `τ_idle` (N*m); range `[0, 1000]`. With the load, the threshold below which a drive never spins up |
| `MpMaxSpeed` | `2.0` | burst speed `ω_max` (rad/s); range `[0.1, 1000]`. Capacity `= 1/2Iω_max²` scales with its square |
| `MpGearMeshLoss` | `0.02` | transmission mesh loss, fraction of coupled energy lost per second; range `[0, 1]`; 0 = lossless |

### Content constants - iiex config, `ex_values.json` (domain `iiex`)

| Key | Value | What it does |
|---|---|---|
| `FlywheelInertiaNormal` | `10` | `I` of the 3x3x1 disc, the reference; range `[0.01, 1e6]` |
| `FlywheelInertiaLarge` | `150` | `I` of the 5x5x2 disc - 15x the normal, since a disc's `I` scales with `R⁴*t`; so 15x the energy and 15x the spin-up; range `[0.01, 1e6]` |
| `FlywheelBridgeChargePower` | `1.0` | bridge drive torque (N*m) at/above rated axle speed; range `[0, 1e6]` |
| `FlywheelBridgeRatedAxleSpeed` | `1.0` | axle speed at which the bridge delivers full torque (vanilla MP rated speed is ~1); range `[0.01, 1000]` |
| `ShaftInertia` | `0.5` | `I` a single cast-iron shaft (or bevel) segment adds - the "Buffer" node's rotating mass; range `[0, 1e6]` |

### Hard-coded - not config

| Constant | Value | Where | Note |
|---|---|---|---|
| Network tick interval | `1000 ms` | `BlockNetworkModSystem` | the `dt` of every `OnTick`, shared by all network families; `ServerTick` caps `dt` at 2 s |
| Sync threshold | `0.02, MpMaxSpeed` | `BlockEntityFlywheel.OnNetworkUpdate`, `BlockEntityTransmission.SyncSideSpeeds` | literal `0.02f` in both places, not a shared symbol |
| `EnergyAnim.StoppedFraction` | `0.01` | `EnergyAnim` | `private const`; below 1 % of ω_max a shaft reads as stopped |
| Transmission ratios | `x2 -> 2`, `x4 -> 4`, `clutch -> 1` | `BlockEntityTransmission.Ratio` | a `switch` on the `kind` variant, not config - the ratio is chosen by which block is built |
| Transmission tick | `250 ms` | `BlockEntityTransmission.ProductionTickMs` | the coupling runs 4x per network tick |
| Mill pass tick | `250 ms` | `BlockEntityRollingMill.PassTickMs` | reads the live network so progress stays in step with the load it imposes |
| Bench stroke tick | `250 ms` | `BlockEntityMpBench.StrokeTickMs` | the shear's and the fastener benches' stroke clock |
| Flywheel hub cells | normal `(0,1,0)`; large `(0,2,0)`,`(0,2,1)` | `BlockEntityFlywheel.NormalHubs`, `LargeHubs` | `static readonly` tuples that must mirror `BlockFlywheel`'s footprint by hand |
| `BEBehaviorMPFillerPort.DefaultResistance` | `0.5` | `BEBehaviorMPFillerPort` | vanilla-MP load a hosted port presents when its spec sets no `resistance` |
| Port turning epsilon | `0.001` | `BEBehaviorMPFillerPort.IsTurning`, `IsReversed` | vanilla-network speed below which the port reads as stopped |
| Block-info power gate | `> 1 W` | `BlockEntityFlywheel.GetBlockInfo` | supply/demand line is suppressed below this |
| Bevel gear item | `"iiex:bevelgear"` | `BlockCastIronBevel.GearItemCode` | `const string` |

The mill's own balance levers (`RollingLoadTorque`, `RollingTempC`, `RollingRollRadius`, ...) are documented
on its page, but `RollingLoadTorque` (0.34) is derived from this page's numbers: one bridge drive (1 N*m)
less friction at ω_max (`0.05*2 + 0.5 = 0.6`) leaves 0.4 N*m of headroom, and the mill's declared demand
takes 85 % of it. Change `MpFrictionCoeff`, `MpIdleTorque`, `MpMaxSpeed` or `FlywheelBridgeChargePower` and
the mill's calibration moves with them.

The mill's demand is declared, not computed from the bite, so re-cutting its schedule never moves its draw on
this network. Only the declared torque and the piece's heat move it: `RollingPass.LoadTorque` stiffens
`RollingLoadTorque` by the flow stress of a piece below rolling heat. A consumer that declares what it takes
is the shape to copy for the next one.

---

## Code

### exlib - the model

| Type / member | Role |
|---|---|
| `MpEnergyNetwork` | `BlockNetwork` subclass; `NetworkType => "mpenergy"` |
| `.OnTick` | the node walk + integrate + broadcast |
| `.OnMerge` / `.OnSplitFragment` | reservoir pooling and proportional split |
| `.State` (typed) | shadows `BlockNetwork.State` so base code and the typed accessor share one object |
| `MpEnergyNetworkState` | `Speed`, `Inertia`, `StoredEnergy`, `SupplyPower`, `DemandPower`, `Reversed` |
| `.Step` | the simulation; pure static, no world needed |
| `.CoupleRatio` | rigid-gear projection of two runs |
| `.CapacityFor` / `.DeriveSpeed` / `.EnergyAtSpeed` | pure helpers |
| `IMpEnergyProducer.DriveTorque(ω)` | implement to drive a run |
| `IMpEnergyStorage.Inertia` | implement to buffer a run |
| `IMpEnergyConsumer.LoadTorque(ω)` | implement to load a run |
| `IMpEnergyDirection.IsReversed` | implement to set the run's direction |
| `BEBehaviorMPFillerPort` | the vanilla-MP participant a footprint cell hosts; `Speed`/`IsTurning`/`IsReversed`/`CurrentAngleRad`/`DrivenAngleRad`/`Through` |
| `BlockNetworkModSystem.GetNetworkAt` | how a machine reads a run from its own tick (the transmission, the mill's pass tick, the benches' stroke) |
| `IndustryModule.RegisterNetworkTypes` | `RegisterNetworkType("mpenergy", () => new MpEnergyNetwork(networks))` |

### iiex - the blocks

| Type | Notes |
|---|---|
| `BlockFlywheel` | `normal` 3x3x1 / `large` 5x5x2 x `ns`/`we`; footprints `NormalFootprint`, `LargeFootprint`; `StructureAngle` |
| `BlockEntityFlywheel` | storage + producer + direction; `BridgeDriveTorque` is pure and pinned by tests |
| `BlockCastIronShaft` | `ns`/`we`/`ud`, so a run can climb; using a bevel gear on one swaps it for a bevel |
| `BlockEntityCastIronShaft` | pass-through node + `Inertia => IiexValues.ShaftInertia` |
| `BlockCastIronBevel` | `HasConnectorAt => true` on every face - the junction; `GetDrops` returns the shaft + gear |
| `BlockEntityCastIronBevel` | inherits the shaft's inertia; `GearedFaces` derived live from connected neighbours, mesh composed in `OnTesselation` |
| `BlockTransmission` | `x2`/`x4`/`clutch` x 4 sides; 2x2 footprint; three-stage RCC; clutch lever routed from the `(1,1,0)` cell |
| `BlockEntityTransmission` | `TryCouple`, `SyncSideSpeeds`, `ToggleEngaged` |
| `BlockRollingMill` | the consumer principal; its two `m` footprint cells host a `BEBehaviorNetworkMember` with `passThrough` that carries the drive line |
| `BlockEntityRollingMill` | `LoadTorque`, `AdvancePass` |
| `BlockEntityMpBench` | the shear's and the fastener benches' consumer base: a hosted mpenergy membership, the stroke clock, `LoadTorque` |
| `EnergyAnim` | `SpinSpeed`, `IsTurning`, `BranchSpinSign` - pure, so the convention is pinned headless |

### Where a caller hooks in

| To add | Contract |
|---|---|
| a machine that draws from the network | derive from `BlockNetworkNode` with `NetworkType => "mpenergy"`; give its BE a place on the graph (`BlockEntityNetworkNode`, or a hosted `BEBehaviorNetworkMember` with `NetworkType` `"mpenergy"` as `BlockEntityMpBench` does) and `IMpEnergyConsumer`; return the resisting torque from `LoadTorque(speed)` and 0 while idle. Advance the operation on its own tick by reading `(NetworkSystem?.GetNetworkAt(Pos) as MpEnergyNetwork)?.State?.Speed` - the pattern in `BlockEntityRollingMill.OnPassTick` and `BlockEntityMpBench` |
| a prime mover | implement `IMpEnergyProducer` and return a torque curve, not a constant power. To sit on a vanilla axle, copy the flywheel's hub pattern: a footprint cell hosting `exlib.BEBehaviorMPFillerPort`, then read `port.Speed` back |
| storage | implement `IMpEnergyStorage`. Capacity, spin-up and buffering all fall out of `I` |

---

## Gotchas

- A run with no storage node has no state at all. `OnTick` nulls `State` when `Σ I <= 0`. A mill wired to a
  bare bridge with no flywheel and no shaft segment sees `Speed == 0` and can never roll. Every practical run
  needs at least one shaft segment (`ShaftInertia = 0.5`) or a wheel.
- A filler cell is a graph node exactly when it declares one. The walk resolves a cell through
  `NetworkMembership.Resolve`, which reads the memberships on its block entity before the block, so a
  footprint cell hosting a `BEBehaviorNetworkMember` bridges a run the same way a node block does. The
  rolling mill's drive line runs through its two `m` cells this way. See [multiblock](multiblock.md).
- The transmission must not become a node. If it did, its two sides would merge into a single run and the
  ratio would be meaningless. `ReferenceEquals(south, north)` in `TryCouple` guards the degenerate loop-back
  case.
- Direction is any-reversed-wins. Two bridges turning opposite ways on one run read as reversed, silently
  flipping the mill's feed deck. Not detected, not reported (`MpEnergyNetwork.OnTick`).
- Hub cells are duplicated by hand. `BlockEntityFlywheel.NormalHubs`/`LargeHubs` must mirror the `'M'` glyph
  in `BlockFlywheel`'s footprints (`NormalFootprint`, `LargeFootprint`). Nothing checks this; a wheel whose
  hub moved would silently never charge.
- `SpinSpeed` assumes every clip is authored as exactly one revolution. A clip authored as two revolutions
  animates at half the true speed with no error anywhere. The ratio transmissions additionally rely on one
  north revolution per `cycle` with the south gear geared up inside the clip (`BlockEntityTransmission.UpdateSpin`
  drives `cycle` at the north speed) - an art-side invariant with no code guard.
- The 2 % sync step is a duplicated literal, not a shared constant (`BlockEntityFlywheel.OnNetworkUpdate`,
  `BlockEntityTransmission.SyncSideSpeeds`), and `EnergyAnim.StoppedFraction` is a different number (1 %).
  Harmless, since the animation threshold being tighter than the sync threshold is the safe direction.
- `CoupleRatio` models only the speed constraint. Torque is summed per-network in `OnTick` and never reflected
  across the gear. The gameplay effect (a heavy machine on the slow side is fed from a bigger reservoir) is
  real; no torque gain is computed.
- Vertical `ud` shafts exist, so a `ud` bevel must too, or a run could climb and never turn off. Both blocks
  declare all three orientations for exactly this reason (`BlockCastIronShaft`, `BlockCastIronBevel`).

---

## Idle draw - not built

An idle machine on the run costs power: every connected consumer contributes a standing torque whether or
not it is working. Not built: every consumer's `LoadTorque` returns 0 when idle and the idle torque is a
single per-network constant, `MpIdleTorque`.

The escape from a standing draw is to declutch the branch, which needs nothing new: the clutch is a
`BlockTransmission` variant - a 2x2 footprint with a lever cell and persisted `_engaged`, and a disengaged
clutch does not couple (`BlockEntityTransmission.ShouldCouple`), leaving the two runs fully independent. The
lever belongs to the transmission, a block the player sites, not to a per-machine engaged flag inside every
bench. Historically, line shafting's no-load loss was the defining inefficiency of a shafted mill, and
fast-and-loose pulleys existed so an idle machine could be thrown off the line.

`MpIdleTorque` is the standing-resistance floor; under this design it becomes per connected consumer
instead of one per-network constant. It makes the wide hall's power cost, steel roll sets' "a bigger plant"
gate, the nail machine's bank argument and the flywheel's reason to exist mechanically true.

Consequence to design for: a machine built and walked away from drains the run forever unless it is behind a
clutch, so the clutch's engaged state needs a readout - the open item below. The torque value itself is
calibration.

## Open

- Pulsed supply is not built. A producer `Power(phase)`, so a bare engine visibly labours between strokes,
  has no implementation; the bridge is a smooth torque curve.
- No speed droop on the bridge. `BlockEntityFlywheel.DriveTorque` ignores its `shaftSpeed` argument, so a
  fully-charged run still shows full supply power in block info.
- The flywheel cannot drive back into vanilla MP. The bridge is one-way (vanilla -> mpenergy); the design
  wants both directions.
- The consumers are the rolling mill, the shear, the nail cutter and the riveter. The steam hammer runs on
  steam, not on this network, and no pulveriser or stamp is built, so the transmission's ratios and the
  large flywheel have nothing yet that justifies them in play.
- The clutch's engaged state has no readout beyond the lever pose and the two shafts visibly turning at
  different speeds. Under R7 ("nothing is hidden") that is probably enough, but it has not been checked
  against the rule's wording.
- Every number above is a first-pass calibration, not a playtested one. The only worked example is the
  mill's, `IiexConfig.RollingLoadTorque`; `MpGearMeshLoss` in particular has never been exercised against a
  chain of transmissions.
