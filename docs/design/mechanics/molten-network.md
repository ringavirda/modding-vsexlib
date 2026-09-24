# Molten Network

**Status** live   **Mod** `exlib` owns the driver (`ExpandedLib.Industry.Molten.MoltenNetwork`, `IMoltenCell`,
`BEBehaviorMoltenCell`) and registers the `molten` network type (`IndustryModule.RegisterNetworkTypes`); `iiex`
owns every cell block (canal, start, tap, mold pedestal, barrel) and the content tunables.

**Owns**
- The molten flow driver: per-cell metal ownership, the distance-from-source BFS, the wavefront sort,
  `FlowEdge` level-equalisation, the per-cell flow rules, the downhill vertical edge and the per-tick
  thermal pass.
- Every molten tunable and its shipped value: `MoltenFlowRate`, `MoltenMinFlowAmount`, `MoltenCooldownDefault`,
  `CanalDefaultUnitCapacity`, `CanalDefaultDrainSpeed`, `MoldDefaultUnits`, `BarrelDefaultMaxUnits`,
  `MoltenCooldownSpeed` + the three per-container cooldown coefficients, `CanalSealClayCost` /
  `CanalUnsealClayRefund`.
- The per-cell capacity of each fitting (canal 50 / start 100 / tap 25 / pedestal 25 / barrel 800 / hosted
  cell 100) and where each number comes from.
- The two flow-blocking latches (`Sealed`, `Solidified`), the solidify rule, and the chisel-out recovery gate.
- The back-pressure chain: destination full -> canal backs up -> furnace stalls and counts a disruption.
- Molten merge/split semantics (they are no-ops) and the fact that no ladle is built.
- The two cluster-internal drivers (sand casting bed, sand casting cell) and how they differ from the network
  one.

**Depends on**
- [pipe network](pipe-network.md) - owns the shared block-network graph substrate (node add/remove, BFS
  fracture, per-second tick dispatch, connector reciprocity, `IsConnectionBroken` re-walk). Everything this
  page says about *graph* behaviour is that page's fact.
- [density rule](https://github.com/ringavirda/modding-vsexmods/blob/main/docs/design/mechanics/density-rule.md)
  (exmods) - what one *unit* of metal is worth. This page never converts units to mass.
- [materials](https://github.com/ringavirda/modding-vsexmods/blob/main/docs/design/materials.md) (exmods) -
  metal identities, melting points, per-metal liquid/hardened thresholds, recovery drops.

---

## Role

Liquid metal has to get from a furnace tap-hole to a mold without becoming an inventory item. The molten
network is the transport layer: a run of brick or cobblestone canal blocks, each holding its own charge of
metal, that a furnace pours into at one end and that drains into a barrel, a tool mold or a casting bed at the
other.

Two consequences:

1. No bucket step. The furnace does not emit an item; it hands units down its tap into the canal start, and
   the run carries them. Nothing is ever picked up in a liquid state.
2. Failure is visible and local. A canal that goes cold plugs at that block, glows, blocks flow, and is
   chipped out with a chisel, instead of the whole run silently voiding its contents.

The network is not a pool. It provides connectivity and a driver; the metal itself lives in the blocks. That
is what makes merge and split free (`MoltenNetwork.CanMerge`, `OnMerge`, `OnSplitFragment`) and what lets two
different metals stand side by side in one graph without mixing.

---

## How it works

### 1. Cells own their metal

The network stores no metal. Every node is an `IMoltenCell` exposing amount / type / temperature / capacity,
the two latches, two capability flags, its flow rules, and four operations:

| member | meaning |
|---|---|
| `CellAmount` | units held (liquid, or solidified once latched) |
| `CellMetalType` | full item code, e.g. `game:ingot-iron`; `""` when empty |
| `CellTemperature` | °C |
| `MaxUnitCapacity` | units |
| `Sealed` | clay-sealed manual valve |
| `Solidified` | frozen plug |
| `IsFlowSource` | roots the distance BFS (the canal start) |
| `AcceptsSubMinimumFlow` | drain fitting (tap / pedestal): a levelling edge into it moves the whole difference, not half, so a run empties into it; a flow rule's gap floor does not apply to it |
| `FlowRules` | this cell's `MoltenFlowRules`, read every tick; null (the default member) leaves its edges on the network defaults - see section 3 |
| `EnsureMetalStack` / `PushMetalRaw` / `DrainMetal` / `UpdateThermal` | the per-tick operations |

Two implementations exist:

- `BlockEntityMoltenCanal` (iiex) - a real graph node, registered in the shared network. Start / tap /
  pedestal subclass it.
- `BEBehaviorMoltenCell` (exlib) - the same contract as a composable behaviour, so an invisible megablock
  footprint filler can be a molten cell. Hosted cells are never registered in the molten graph; the principal
  that owns the cluster drives its own flow. That is how a casting bed stays an isolated internal network while
  the canal outside carries a different metal. One block entity can host several cells under distinct `key`
  prefixes; `MoltenCellHost.MoltenCell(key)` selects one.

Temperature is carried on a server-side single-item `ItemStack` so the VS time-based cooling model applies.
The carrier is not saved: the cell saves its amount, type and temperature, and `EnsureMetalStack` rebuilds
the carrier lazily on load.

### 2. Ordering: distance-from-source wavefront

Each tick `MoltenNetwork.OnTick`:

1. Resolves every `IMoltenCell` under the network's own `Nodes` set.
2. Fetches the cached distance-from-source map (`GetDistanceFromStart`).
3. Sorts cells descending by distance - farthest first, position breaking ties (`CompareFlowOrder`).
4. Calls `EnsureMetalStack` on all of them.

The distance map is a multi-source BFS rooted at every cell with `IsFlowSource == true`
(`BuildDistanceFromStart`). It walks horizontals only, requires the block to expose a connector on the face,
and requires the neighbour to be both in `Nodes` and an `IMoltenCell`. Cells unreachable from any source,
including a cell reached only through a vertical edge, are absent and sort as `int.MaxValue`, i.e. "farthest".

The map is cached and rebuilt only when a cheap topology signature changes (`ComputeTopologySignature`):
`(cell count, XOR-folded position hashes, XOR-folded flow-source hashes)`.

The sort's only job is to decide which side of an undirected horizontal edge drives it - each such edge is
driven exactly once, by the cell farther from the source. It does not set the direction of transfer; see
Gotchas.

### 3. `FlowEdge` - the transfer rule

`MoltenNetwork.FlowEdge(a, b, maxFlow, rules, distFromStart, world, downhillOnly)`:

```
if aCap <= 0 or bCap <= 0                  -> nothing
diff = |a.CellAmount - b.CellAmount|
if diff == 0                               -> nothing
giver    = the cell with more units
receiver = the other
if downhillOnly and a is not the giver     -> nothing
if receiver has metal of a DIFFERENT type  -> nothing (no mixing)
whole = receiver.AcceptsSubMinimumFlow or downhillOnly
if the edge has rules:
  if diff < rules.MinFlowGap and not receiver.AcceptsSubMinimumFlow -> nothing
  whole = whole or (rules.Conveys and distance(receiver) > distance(giver))
step = diff       if whole
       diff / 2   otherwise (integer division)
transfer = min(step, maxFlow)              maxFlow = rules.FlowRate, else MoltenFlowRate
if transfer <= 0                           -> nothing
accepted = receiver.PushMetalRaw(transfer, giver type, giver temp)
giver.DrainMetal(accepted)
```

A levelling pair moves half the difference, so it never overshoots the midpoint; integer halving settles a
pair at a difference of at most 1 unit and stops there. `MoltenMinFlowAmount` is not applied anywhere in the
network driver; on an edge without rules a small difference still closes.

The flow loop walks all six faces, each gated by `HasConnectorAt`. A vertical edge is driven only from the
upper cell (face `DOWN`) with `downhillOnly`: it runs one way, moves the whole difference (capped at
the edge's rate) and never pumps metal uphill; an edge whose rules are horizontal-only moves nothing
vertically. No shipped canal fitting exposes an up or down connector
(every canal, start, tap and pedestal orientation is horizontal), so no shipped run has a vertical edge.

`PushMetalRaw` clamps to the receiver's free space, refuses a type mismatch or a solidified cell, and
volume-weight-averages the temperature of the two charges (`BlockEntityMoltenCanal.PushMetalRaw`, mirrored in
`BEBehaviorMoltenCell.PushMetalRaw`).

Cells that are `Sealed` or `Solidified` are skipped on both sides before `FlowEdge` is reached, and both
latches also sever the *graph* at that position via `BlockEntityMoltenCanal.IsConnectionBroken`.

**Per-cell flow rules.** A cell can return a `MoltenFlowRules` record from `IMoltenCell.FlowRules`. A
connection applies rules only when both cells return them, combined per edge (`MoltenNetwork.EdgeRules`): the
smaller `FlowRate`, the larger `MinFlowGap`, conveying only if both convey, horizontal-only if either is. When
either cell returns null the edge runs on the defaults above, so a cell's floor or `HorizontalOnly` holds only
against a neighbour that returns rules too.

| member | unit | effect on the edge |
|---|---|---|
| `FlowRate` | metal units per connection per tick | caps the move in place of `MoltenFlowRate`; 0 or less moves nothing |
| `MinFlowGap` | metal units | a difference below it moves nothing, except into a drain fitting; the floor is on the difference, before halving; 1 or less sets no floor |
| `Conveys` | - | a receiver farther than the giver from the nearest flow source, by the section 2 distance map, takes the whole difference; toward the source or between equal distances, half. A cell no source reaches counts as farthest |
| `HorizontalOnly` | - | no metal crosses the edge vertically |

A vertical edge with rules that are not horizontal-only keeps the downhill rule: the whole difference, capped
by the rule's `FlowRate` and floored by its `MinFlowGap`.

The network tick runs at 1 s (`BlockNetworkModSystem.StartServerSide`) and the driver does not read `dt`, so
`MoltenFlowRate` is units per connection per tick, which is per second. This is the number the design's
single 50 u/s throughput refers to.

### 4. Thermal pass and solidification

After the flow pass, every cell gets `UpdateThermal` (`BlockEntityMoltenCanal.UpdateThermal`):

1. Read the live temperature off the carrier stack.
2. Re-stamp the temperature and the cooldown rate every tick. Setting the temperature rebases the cooling
   baseline to the current value, so a live `/exmod config iiex MoltenCooldownSpeed ...` applies to metal
   already standing in the world, and an unchanged rate is a no-op. A canal uses `MoltenCooldownSpeed`; a
   hosted cell uses its declared `cooldownSpeed`, default `MoltenCooldownDefault`.
3. If `SolidifiesWhenCold` and `temp < meltingPoint`, latch `Solidified` and retesselate.

`SolidifiesWhenCold` is `true` for plain canals and inherited by start / tap / pedestal. Hosted cells take it
from their filler declaration (`solidifies`, default true).

A solidified cell is chiselable only once `IsHardened` - below `hardenedThreshold x meltingPoint`; before that
the block reports `iiex:canal-cooling` and refuses with `iiex-canaltoohot`. Chiselling clears the cell, lifts
the latch and calls `RebuildFromRoot` so it rejoins the run (`BlockEntityMoltenCanal.ClearSolidified`).
Recovery goes through `MoltenChisel.BuildRecovery` at 5 units per bit (`GetSolidifiedDrop`). The canal asks
for no fallback: a metal whose solid drop cannot be resolved as an item yields no drop. Only callers that pass
`slagFallback: true` (the sand casting bed's harvest, the converter) fall back to the metal's
`RecoveryFallback` or `MetalRegistry.DefaultRecoveryFallback`.

Manual severing is the clay seal: `SetSealed` flips the latch and re-walks the graph at that position
(`ResyncNetworkNode`), and a sealed canal caps *every* connector face visually regardless of neighbours
(`RefreshOpenConnectorFaces`).

### 5. Back-pressure - full destination stalls the furnace

There is no explicit back-pressure code. It falls out of the fact that cells own their metal and every push
returns what was actually accepted:

| step | code | what happens when the far end is full |
|---|---|---|
| pedestal / tap drains its own cell into the mold or barrel | `BlockEntityMoltenCanalMoldPedestal.OnServerTick`, `BlockEntityMoltenCanalTap.DrainInto` | `currentUnits >= maxUnits` -> nothing drains, the fitting's cell stops emptying |
| `FlowEdge` into that cell | `MoltenNetwork.FlowEdge` | `PushMetalRaw` clamps to free space -> `accepted` shrinks to 0, upstream cell keeps its metal |
| the run fills back to the start | - | each cell reaches `MaxUnitCapacity` in turn |
| furnace tap hands metal down | `BlockEntityFurnaceTap.TryPourMetal` | `CanReceiveOrSoak` still passes so the pour keeps *heating* a brim-full start (`BlockEntityMoltenCanalStart.CanReceiveOrSoak`, `ReceiveLiquidMetal`), but `PushMetalRaw` accepts 0 |
| furnace drain | `BlockEntityShaftFurnace.DrainIronTap` | `accepted == 0` -> the pool is never drawn down |
| furnace pool caps | `BlockEntityShaftFurnace.LiquidCapacityReached` | pooled metal or slag reaches its maximum -> true (only for a furnace with pool cells) |
| melt cycle stops | `BlockEntityFurnaceCore.OnProductionTick` | no new metal is rendered |
| extinguish timer runs | `BlockEntityFurnaceCore.OnProductionTick`, `ExtinguishThresholdDefault` / `ExtinguishThresholdSevere` | it counts as one disruption; alone that is a 30 s grace, but combined with any second disruption the threshold drops to 0 and the fire goes out immediately |

The cinder notch runs the same chain for slag (`BlockEntityShaftFurnace.DrainSlagTap`).

The "soak" branch: a brim-full canal start being poured onto does not cool and plug, because the pour keeps
raising its temperature without adding volume (`BlockEntityMoltenCanal.SoakHeat`). Without it, a stalled run
would freeze solid at the one cell the player can least afford to lose.

### 6. What merges - and the missing Ladle

Graph merge and split are no-ops because there is no pooled state to redistribute; `CanMerge` only checks the
other network is also a `MoltenNetwork`.

Metal itself never merges implicitly: `FlowEdge` refuses a transfer whenever the receiver already holds a
*different* metal code, and `PushMetalRaw` refuses the same. Two metals sit side by side in one run without
mixing.

The design allows one merge and mixing point, the ladle ([conventions](../conventions.md), the per-cell molten
rule; exmods [ladle](https://github.com/ringavirda/modding-vsexmods/blob/main/docs/design/machines/ladle.md) and
[materials](https://github.com/ringavirda/modding-vsexmods/blob/main/docs/design/materials.md)).

Not built: no block, block entity, item or behaviour implements a ladle. Every alloying and merging fact in
the design docs is design only, and nothing in the molten network mixes anything.

### 7. The other two copies of the driver

Two megablocks run their own cluster-internal flow instead of joining the graph, because their cells are
hosted behaviours rather than nodes:

| driver | code | ordering | pull rate | flow rate |
|---|---|---|---|---|
| `MoltenNetwork` | `MoltenNetwork.OnTick` | farthest-from-source first; horizontal edge driven by the farther cell, vertical edge by the upper | n/a (fed by pour) | `MoltenFlowRate`, half the difference; an edge with flow rules per section 3 |
| Sand casting bed | `BlockEntitySandCastingBed.OnServerTick` | basin-outward (Manhattan distance from the basin), edge driven nearer->farther, and only where both ends are carved | `PullRatePerTick = 25`, hard-coded, from any adjacent molten cell on a horizontal face of the basin (`PullFromNeighbours`) | `MoltenFlowRate`, the whole difference |
| Sand casting cell | `BlockEntitySandCastingCell.OnServerTick` | single hosted cell | `PullRatePerTick = 25`, hard-coded, from the launder face only (`PullFromLaunder`) | n/a |

The bed's `FlowEdge` (`BlockEntitySandCastingBed.FlowEdge`) differs from the network's in three ways: it
moves the whole difference rather than half, it keeps the `MoltenMinFlowAmount` floor for any receiver that
is not a drain fitting, and a drain fitting never gives metal back, so a mold hoards its charge until it
hardens.

---

## Numbers

### exlib config - `ExlibConfig`, file `ModConfig/ex_values.json`, section `exlib`

| key | value | what it does |
|---|---|---|
| `MoltenFlowRate` | `50` | max units across one canal connection per tick (= per second) on an edge without flow rules |
| `MoltenMinFlowAmount` | `10` | minimum transfer in the sand casting bed's `FlowEdge` unless the receiver is a drain fitting; the network driver does not read it |
| `MoltenCooldownDefault` | `24` | cooldown speed stamped on a carrier stack when a caller gives none |
| `MetalLiquidThreshold` | `0.8` | fraction of melting point above which metal reads *liquid* |
| `MetalHardenedThreshold` | `0.3` | fraction of melting point below which metal reads *hardened* (chiselable) |
| `MetalGlowMinTemp` | `500` | below this °C, hot metal emits no block light |

The liquid and hardened thresholds are defaults a metal's own descriptor may override (`MetalRegistry`); the
cell glow (`MoltenMetal.GlowLevel`) reads the global `MetalGlowMinTemp`.

### iiex config - `IiexConfig`, file `ModConfig/ex_values.json`, section `iiex`

| key | value | what it does |
|---|---|---|
| `MoltenCooldownSpeed` | `24` | base cooldown speed for every molten container this mod owns |
| `BarrelCooldownCoefficient` | `1` | multiplier for a standalone molten barrel |
| `TapMoldCooldownCoefficient` | `1` | multiplier for a mold parked under a canal tap |
| `MoldPedestalCooldownCoefficient` | `1` | multiplier for a mold on a pedestal |
| `MoltenAmbientTemperature` | `20` | ambient the molten system cools toward |
| `CastMoldHeatSinkFraction` | `0.7` | fraction of pour temperature a cast-iron mold body soaks as its own glow |
| `CastMoldBodyCooldownPerSecond` | `25` | °C/s a cast-iron mold body sheds that glow |
| `ClayMoldHeatCeiling` | `1100` | pour temperature above which a small fired-clay tool mold shatters |
| `EnhanceVanillaMolds` | `false` | opt vanilla clay molds into the enhanced spill/burn/render handling |
| `MoldBurnMinTemperature` | `200` | mold-content °C that burns a bare-handed carrier |
| `MetalRecoveryFallback` | `iiex:slag` | installed at load as `MetalRegistry.DefaultRecoveryFallback`; used only by recoveries that ask for a fallback |
| `CanalDefaultUnitCapacity` | `50` | per-canal-block capacity |
| `CanalDefaultDrainSpeed` | `20` | tap drain speed (units per 1 s tick) when the block sets no `drainSpeed` |
| `MoldDefaultUnits` | `100` | mold capacity when the mold sets no `requiredUnits` |
| `BarrelDefaultMaxUnits` | `800` | barrel capacity fallback |
| `CanalSealClayCost` | `4` | fire clay to seal a straight canal |
| `CanalUnsealClayRefund` | `2` | fire clay returned when breaking the seal |
| `TapDrainPerTick` | `50` | pool units a furnace tap considers per drain tick |
| `TapIronStackFactor` | `0.6` | fraction of those units offered through the iron taphole, so at most 30 u/s of iron |
| `TapSlagStackFactor` | `0.8` | fraction offered through the cinder notch, so at most 40 u/s of slag |

### Cooldown derives from charge volume, not a per-container constant

> Heat loss from any vessel - ladle, converter, hearth, canal, barrel, mould - is a function of its
> surface-to-volume ratio, not a number chosen per block.

This is the square-cube law. Heat loss scales with surface area; heat content scales with volume, so the
cooling rate goes as area / volume proportional to 1/L - double a vessel's linear size and it cools at half
the rate.

The three per-container cooldown coefficients are therefore the wrong shape. They give the same numbers as a
derived factor does today, and diverge the instant any vessel is resized - at which point nothing fails, the
numbers are merely wrong. One factor should explain all of them:

| vessel | why it behaves as it does |
|---|---|
| canal | thin section, huge surface per unit -> chills fast. That is what bounds canal-cast part size. |
| crucible hearth | a block-scale pool -> holds heat through a campaign |
| ladle / converter | 3x3x3 of charge, radiating only from its surface -> barely cools |

Cooling is not a global knob: big vessels hold heat, so the player's lever against "molten metal solidifies
too fast" is to build the bigger machine.

Not built: every container still cools at `MoltenCooldownSpeed` times its coefficient. Each fitting re-stamps
its cooldown rate against the current temperature every tick, so a rate derived per tick from the current
charge volume would apply correctly to metal already standing in the world - a partly-drained ladle cools
faster as it empties. The parked-barrel literal `300f` and the `BarrelCooldownCoefficient` it bypasses
(Gotcha 8) both dissolve into this.

### Per-cell capacities (derived, not separate config keys)

| cell | capacity | code |
|---|---|---|
| plain canal (straight / bend / T / X) | `CanalDefaultUnitCapacity` = 50 | `BlockEntityMoltenCanal.MaxUnitCapacity` |
| canal start | `x 2` = 100 | `BlockEntityMoltenCanalStart.MaxUnitCapacity` |
| canal tap | `ceil(/2)` = 25 | `BlockEntityMoltenCanalTap.MaxUnitCapacity` |
| mold pedestal | `ceil(/2)` = 25 | `BlockEntityMoltenCanalMoldPedestal.MaxUnitCapacity` |
| molten barrel | `maxUnits` attribute = 800 | `BlockMoltenBarrel.MaxUnits`, `BlockEntityMoltenBarrel.MaxUnitAmount` |
| parked mold (tap or pedestal) | `requiredUnits` attribute, default 100 | `BlockEntityMoltenCanalTap.AddMold`, `BlockEntityMoltenCanalMoldPedestal.AddMold` |
| hosted cell (`BEBehaviorMoltenCell`) | `capacity` property, default 100 | `BEBehaviorMoltenCell.DefaultCapacity` |
| hosted cell with a rammed pattern | the runtime override wins over the declaration | `BEBehaviorMoltenCell.SetCapacity` |

### Hard-coded - not config

| constant | value | code | note |
|---|---|---|---|
| network tick interval | `1000 ms` | `BlockNetworkModSystem.StartServerSide` | makes `MoltenFlowRate` a per-second rate |
| `dt` catch-up clamp | `2 s` | `BlockNetworkModSystem.ServerTick` | the molten driver does not read `dt` |
| sand-bed / sand-cell `PullRatePerTick` | `25` | `BlockEntitySandCastingBed`, `BlockEntitySandCastingCell` | differs from the design's single 50 u/s |
| barrel chisel-out bit size | `10 u` per bit | `BlockEntityMoltenBarrel.ChiselOut` | |
| barrel break-drop bit size | `5 u` per bit (the `MoltenChisel` default) | `BlockEntityMoltenBarrel.GetMetalDrops` | |
| tap barrel-fill cooldown speed | `300f` | `BlockEntityMoltenCanalTap.OnServerTick` | a *parked barrel* ignores `BarrelCooldownCoefficient` |
| pour-tally idle timeout | `5000 ms` | `BlockEntityMoltenCanalStart.PourTallyTimeoutMs` | |
| pour / drain sound throttle | one clip length | `ExSounds.PlayThrottled` with `ExSounds.ClipLengthMs` | |
| `BEBehaviorMoltenCell.DefaultCapacity` | `100` | `BEBehaviorMoltenCell` | |
| glow scale | `(T - MetalGlowMinTemp) / 30`, clamped 0-24 | `MoltenMetal.GlowLevel` | |

---

## Code

| type | mod | role |
|---|---|---|
| `MoltenNetwork : BlockNetwork` | exlib | the driver; `NetworkType => "molten"` |
| `MoltenNetwork.OnTick` | exlib | collect -> order -> flow -> cool. The whole model is here |
| `MoltenNetwork.FlowEdge` | exlib | the transfer rule (private static; reads its two cells, the edge's rules and the distance map) |
| `MoltenNetwork.EdgeRules` | exlib | combines two cells' `MoltenFlowRules` into the edge's, or null when either has none |
| `MoltenNetwork.BuildDistanceFromStart` | exlib | multi-source BFS, horizontals only |
| `MoltenNetwork.ComputeTopologySignature` | exlib | the cache-invalidation fingerprint |
| `IMoltenCell` | exlib | the extension point. Implement this to be carried by the network |
| `MoltenFlowRules` | exlib | a cell's flow rules: rate, gap floor, conveying, horizontal-only |
| `BEBehaviorMoltenCell` | exlib | `IMoltenCell` + `IFillerHostedBehavior`; config via `ConfigureFromFiller` |
| `MoltenCellHost` | exlib | selects one of several hosted cells on a block entity by declared key |
| `BlockEntityMoltenCanal` | iiex | the node implementation; `IsConnectionBroken` is `Sealed || Solidified` |
| `BlockEntityMoltenCanalStart` | iiex | `IsFlowSource = true`; `ILiquidMetalSink` - the furnace-facing entry point |
| `BlockEntityMoltenCanalTap` | iiex | drains its own cell into a parked barrel/mold (`DrainInto`); a closed tap severs itself |
| `BlockEntityMoltenCanalMoldPedestal` | iiex | same, for a small tool mold; clay heat gate in `OnServerTick` |
| `BlockEntityMoltenBarrel` | iiex | not an `IMoltenCell` and not a network node - an `ExBlockEntity` that is an `ILiquidMetalSink` + `IChiselableMolten` |
| `BlockEntityFurnaceTap` | iiex | the furnace-side tap-hole and cinder notch; `TryPourMetal` pours into the start under its spout |
| `MoltenMetal` | exlib | temperature/cooldown/classification helpers shared by every fitting |
| `MoltenChisel.BuildRecovery` | exlib | the one recovery-drop builder |
| `IndustryModule.RegisterNetworkTypes` | exlib | `networks.RegisterNetworkType("molten", () => new MoltenNetwork(networks))` |

Tests: `MoltenFlowTests` (iiex; half-difference levelling, settling, no floor on a small difference, no mixing),
`MoltenFlowRulesTests` (exlib; conveying, vertical faces, the gap floor, the rate, per-edge combining, the
no-rules fallback), `MoltenInvariantTests` (conservation and cell bounds under random flow), `CastingScenarioTests` (a run casting
at a pedestal and a barrel tap).

**Where a caller hooks in**

- *To be transported*: implement `IMoltenCell` on a `BlockEntityNetworkNode` whose block reports
  `NetworkType == "molten"`, or add `BEBehaviorMoltenCell` and drive it yourself.
- *To receive a pour from a furnace or a held crucible*: implement vanilla `ILiquidMetalSink` (see
  `BlockEntityMoltenCanalStart` and `BlockEntityMoltenBarrel`).
- *To be chiselable*: implement `IChiselableMolten`; the ritual (sound, give/spawn) lives in
  `MoltenChisel.TryChisel`, invoked from the block.
- *To gate a pour by temperature*: `ClayHeatGate.WouldShatter` (called from
  `BlockEntityMoltenCanalMoldPedestal.OnServerTick`).

---

## Gotchas

1. Levelling compares raw amounts, not fill ratios. `diff` is `|a.CellAmount - b.CellAmount|`. With the
   shipped unequal capacities (start 100, canal 50, tap 25) equal *amounts* are very different *ratios*.

2. The ordering does not set the flow direction. The comment in `MoltenNetwork.OnTick` says the
   farthest-first order "drains the run toward the source". It does not: direction is decided purely by which
   cell has more units. Metal flows back into the canal start whenever the downstream cell holds more. The
   sort only picks which endpoint executes each undirected horizontal edge.

3. `MoltenNetwork` never overrides `OnTopologyChanged`. `PipeNetwork` does; molten does not, so the distance
   cache is invalidated only by its own signature (`GetDistanceFromStart`). The signature covers cell count,
   positions and which cells are sources - not which faces each cell exposes. A `RemoveNode` that does not
   fracture keeps the same network instance (`BlockNetworkModSystem.Settle`), so a change that only alters
   connectors can leave a stale distance map.

4. The solidify latch trips at 100 % of the melting point, but chisel-out needs < 30 %. `UpdateThermal`
   latches at `temp < meltPoint` while `IsHardened` uses `MetalHardenedThreshold x meltPoint`. Between those
   a cell is a plug that reads `iiex:canal-cooling` and refuses the chisel. For iron (1482 °C) that is the
   whole span from 1482 down to ~445 °C. `CellState` can still classify the metal as Liquid (above
   `0.8 x meltPoint` = ~1186 °C) while `Solidified` is already latched - the two are independent by design.

5. The molten barrel is not a cell. `BlockEntityMoltenBarrel` extends `ExBlockEntity` - it has no
   `IMoltenCell`, is not a network node, and is filled only by a tap draining into it or a direct pour. Do not
   expect network flow to reach it.

6. A closed tap or pedestal severs itself from the run. `IsConnectionBroken()` returns true when not pouring
   (`BlockEntityMoltenCanalTap.IsConnectionBroken`, `BlockEntityMoltenCanalMoldPedestal.IsConnectionBroken`),
   so a closed fitting does not merely stop delivering - it drops off the graph and its own cell stops
   filling. Toggling re-walks the graph via the `IsPouring` setter.

7. A canal's capacity is not per block. `BlockEntityMoltenCanal.MaxUnitCapacity` returns
   `CanalDefaultUnitCapacity` for every canal piece; no canal def sets a `maxUnits` attribute and none is read.

8. The parked-barrel cooldown ignores its own coefficient. The tap passes a literal `300f` when creating the
   barrel's content stack (`BlockEntityMoltenCanalTap.OnServerTick`) while a parked *mold* uses
   `MoltenCooldownSpeed x TapMoldCooldownCoefficient`. A parked barrel cools at a fixed slow rate, so
   `BarrelCooldownCoefficient` has no effect on metal poured through a tap - only on a standalone barrel.

9. The world accessor is resolved once and cached forever (`MoltenNetwork.GetWorld`), on the assumption that
   a network never moves between worlds.

10. `IsFlowSource` is the *canal start*, not the furnace. The furnace tap is not part of the molten graph at
    all; it finds the start block by offset, one step opposite its facing and one down
    (`BlockEntityFurnaceTap.SpoutPos`), and calls `ILiquidMetalSink` on it. Move the start and the run has no
    BFS root: every cell then sorts as `int.MaxValue` and edge ownership degenerates to position order.

---

## Open

- Throughput is several numbers, not one. The design sets a single 50 u/s for tap, bed pull and canal.
  Shipping code has canal-to-canal at `MoltenFlowRate = 50`, tap-to-container at `CanalDefaultDrainSpeed = 20`,
  sand-bed and sand-cell pull at a hard-coded `25`, the furnace hand-down at `TapDrainPerTick = 50` scaled by
  `TapIronStackFactor` (30 u/s of iron) or `TapSlagStackFactor` (40 u/s of slag), and the mold pedestal with no
  rate cap at all - it drains `min(CellAmount, space)` in one tick
  (`BlockEntityMoltenCanalMoldPedestal.OnServerTick`). Unifying these is unbuilt work.
- No ladle. The one mixing block, and with it every alloying rule in exmods' materials page, does not exist in
  code. `BEBehaviorMoltenCell` makes a hosted-cell vessel cheap, but nothing uses it for one yet.
- `FlowEdge` exists twice (network, casting bed) with different orderings and different rules (half against
  whole difference, no floor against `MoltenMinFlowAmount`), and the casting cell pulls without either. There
  is no shared primitive.
- No cross-cell heat conduction. Cells exchange heat only when metal actually moves (via the weighted average
  in `PushMetalRaw`) or when a pour soaks a full cell. A standing run cools cell-by-cell independently.
- The topology signature does not cover connector orientation (Gotcha 3). Whether to add it, or to override
  `OnTopologyChanged`, is undecided.
