# Production Machines

A machine is a block that does something over time: a kiln advancing a smelt, a pump moving water,
a mill rolling a bar. Vintage Story has no idea what a machine is. What it gives you is a block
entity - the object the game attaches to one block position to hold that block's saved state - and
a tick listener you register on it by hand.

That listener is where the mistakes live. It has to be registered on the server only, or every
client simulates the machine too and the two answers drift apart. It has to be unregistered when
the block is broken and when its chunk (the cube of world the game loads and unloads as players
move) goes away, or the listener outlives the machine. Every tick has to ask whether the machine
may run at all - fuel, inputs, a structure still standing - and a machine that may not run usually
still has cooling or settling to do, so simply stopping the tick is the wrong answer. And the game
does not tick a block in an unloaded chunk at all: a player who walks away for a game day and comes
back finds the furnace exactly as it was, unless you wrote the catch-up yourself.

exlib puts all of that in one behaviour. `BEBehaviorProductionMachine` owns the tick. It registers
server-side when the block loads, stops itself on removal and unload, routes a tick the machine
cannot use to an idle path rather than dropping it, and can replay a bounded number of sub-ticks
for the game time the machine spent unloaded. You write the work and the gate; `Machines/` also
carries the two helpers such a machine tends to want, one for reading a
[block network](Block-Networks) through a connector face and one for a threshold that has to hold
before it fires.

How you reach it depends on what the block entity already is: with nothing else to be, derive from
`BlockEntityProductionMachine` and override two members; with the base class already spent, attach
the behaviour and publish the gate through `IProductionReadiness`. Both routes run the same code.
This is **not** multiblock-only - engines, sub-machines, furnaces and converters all build on it.

> **The base class is not the only way in.** C# allows one base class, so it is worth being clear
> about what deserves it. Form, process and membership are three independent axes: the base-class
> slot belongs to **form** (what the block *is*), while the production tick and network membership
> are **behaviours**, small objects attached to a block entity that the game initialises, saves and
> ticks along with it. `BlockEntityProductionMachine` below is the base a machine derives from when
> form has nothing else to claim; a block entity whose form is already spoken for - a container, a
> multiblock part - attaches the production behaviour and keeps its base. Do not spend the slot
> twice.

## `BlockEntityProductionMachine`

A base block entity that owns a server-side production tick. Two of its members are abstract, and
they are the two that describe your machine: what it does each tick, and when it may. Registration,
idle routing and teardown are already written.

```csharp
public abstract class BlockEntityProductionMachine : BlockEntity
{
    protected virtual int ProductionTickMs { get; }          // tick interval, default 1000ms
    protected abstract bool CanRunProduction { get; }        // operational gate
    protected virtual bool AutoStartProduction { get; }      // register tick in Initialize? default true

    protected abstract void OnProductionTick(float dt);      // runs server-side while CanRunProduction
    protected virtual void OnIdleProductionTick(float dt);   // runs instead when not operational (default no-op)

    protected void StartProductionTick();                    // idempotent, server-side
    protected void StopProductionTick();
}
```

Minimal machine:

```csharp
[BlockEntityRegister]
public class BlockEntityKiln : BlockEntityProductionMachine
{
    protected override bool CanRunProduction => HasFuel && HasInput;

    protected override void OnProductionTick(float dt)
    {
        // Advance the smelt. Runs once per ProductionTickMs while CanRunProduction is true.
    }
}
```

When `CanRunProduction` is `false` the tick routes to `OnIdleProductionTick` instead of stopping,
so you can still run cooldown/settling logic. Override `AutoStartProduction` to `false` if the
machine should register its tick only on a state change rather than on load (e.g. a machine that
is dormant until switched on); then call `StartProductionTick()` / `StopProductionTick()` yourself.
The tick is stopped automatically in `OnBlockRemoved` and `OnBlockUnloaded`.

`dt` is the seconds since the last tick, clamped to twice the tick interval, so a server that
stalled hands the machine a long step rather than an unbounded one. The listener is registered
server-side only, so no production code of yours runs on a client.

The tick itself lives in `BEBehaviorProductionMachine`, which this class hosts; a block entity whose
one base slot is already spent hosts the same behaviour instead of deriving from here:

```csharp
public class BlockEntityRollingMill : BlockEntityNetworkNode, IProductionReadiness
{
    // In the constructor, not Initialize: BlockEntity fans both FromTreeAttributes and Initialize
    // out over Behaviors, and a process added later misses whichever has already run.
    public BlockEntityRollingMill() => Behaviors.Add(new HostProcess(this));

    private sealed class HostProcess(BlockEntityRollingMill owner)
        : BEBehaviorProductionMachine(owner)
    {
        protected override int ProductionTickMs => 250;
        protected override void OnProductionTick(float dt) => owner.OnPassTick(dt);
    }

    public bool IsReadyToProduce => IsRolling;          // the gate, published not overridden
    public bool StopsProductionWhenNotReady => false;   // an idle mill keeps its clock
}
```

The gate goes through `IProductionReadiness` rather than an override, because a machine may publish
more than one answer and the process reads every publisher on the block entity - the block entity
itself and any of its behaviours:

| Publisher | Answers | Opt-out |
|---|---|---|
| `BlockEntityProductionMachine` | `CanRunProduction`, the gate you write | override the gate |
| `BlockEntityMultiblockStructure` | `StructureComplete` - a machine missing a cell is not a machine | `StopsProductionOnStructureLost` |
| `ExRightClickConstructable` ([Construction](Construction)) | `IsComplete` - a half-built machine does not run | `gatesProduction: false` |

Every publisher has to answer yes for the tick to reach `OnProductionTick`; one no sends it to the
idle path. The opt-out column is the other half of the interface: whether losing readiness also
unregisters the tick. That answer is reduced the other way round, so a single publisher that wants
to keep its clock keeps it for the whole machine.

Away catch-up is off until you ask for it. A machine that should keep working while nobody is near
sets `MaxAwayCatchupSteps` above zero: on its first tick after loading, the process measures the
game time since the tick it last simulated and replays that gap as sub-ticks of
`AwayCatchupStepSeconds` each, up to that many steps, each one gated exactly as a live tick is. The
cap is the point. A furnace left for a season replays a bounded stretch instead of a season of
ticks in one frame, and the player gets some of the progress rather than all of it.

The stamp that makes this work is the calendar time of the last simulated tick.
`BlockEntityProductionMachine` saves it for you, as `pm_lastHours`. A host that attaches the
behaviour itself must save the process's `LastTickHours` in its own `ToTreeAttributes` and hand it
back through `RestoreLastTickHours` - a behaviour's tree lands in the block entity's flat tree, so
exactly one of the two may write that key.

## Machine ports

A machine that feeds a pipe should not be part of the run it feeds. A boiler that joined the graph
would hold the run's steam, and be split with it every time a player cut the line. So a fixed
machine is usually **not** a network node - it's a `INetworkConnector` whose outlet/intake
face touches a pipe. The network it interacts with lives in the cell on the **far side** of that
face. `MachinePorts` are extension methods that resolve it:

```csharp
public static class MachinePorts
{
    public static BlockNetworkModSystem? NetworkSystem(this BlockEntity be);
    public static TNet? ConnectedNetwork<TNet>(this BlockEntity be, BlockFacing connectorFace) where TNet : BlockNetwork;
    public static TNet? NetworkAt<TNet>(this BlockEntity be, BlockPos pos) where TNet : BlockNetwork;
}
```

```csharp
protected override void OnProductionTick(float dt)
{
    // The steam network plumbed into the machine's north outlet (null if nothing is connected there).
    var steam = this.ConnectedNetwork<PipeNetwork>(BlockFacing.NORTH);
    if (steam is null) return;
    steam.State /* ... draw steam, push condensate ... */;
}
```

`ConnectedNetwork` performs the reciprocal-connector test: it returns the network only if the
neighbour across `connectorFace` actually exposes a matching connector back. Nothing there gives
null rather than an exception, and that is the ordinary case - the player has not plumbed the
machine yet - so resolve the port on each tick and return early, as the example does. Its sibling
`ConnectedNetworkAt` answers from a cell that is not the machine's own, which is what a multiblock
uses when the port sits on one of its filler cells.

These are extensions on any `BlockEntity`, so reading a port is not something a machine inherits: a
plain block entity calls them exactly as a production machine does.

> Air blower, engine, boiler outlet and converter intake are all `INetworkConnector` ports, not
> nodes - they read/write the network in the adjacent cell rather than being part of the graph.

## `GraceTimer`

A machine that reacts the instant a threshold is crossed flickers. Pressure wobbles over the burst
limit for a single tick, the boiler explodes, and the player never had a warning or a chance to
vent. `GraceTimer` is the accumulator that fixes it: hold a condition for N seconds, then fire once.
Over-pressure, choke and burst grace are all this shape.

```csharp
public struct GraceTimer
{
    public float Elapsed { get; }          // seconds the condition has held continuously
    public bool IsCounting { get; }

    public bool Update(bool active, float dt, float threshold);   // true once when Elapsed >= threshold, then resets
    public void Reset();
    public float Remaining(float threshold);
    public void ToTree(ITreeAttribute tree, string key);
    public void FromTree(ITreeAttribute tree, string key);
}
```

```csharp
private GraceTimer _overPressure;

protected override void OnProductionTick(float dt)
{
    if (_overPressure.Update(pressure > BurstLimit, dt, GraceSeconds))
        Explode();   // fires exactly once after pressure stays over the limit for GraceSeconds
}
```

Any tick with `active: false` resets the accumulator, so the condition must hold *continuously*.
Persist it with `ToTree`/`FromTree` so a near-burst boiler doesn't reset its grace across a reload.

## Saved state

Everything a machine knows between ticks - its temperature, how far a smelt has got, what is in it -
has to survive the player logging out, and by hand that means writing each field into the save tree
and reading it back with the key spelled the same both times. `BlockEntityProductionMachine`
carries the same `Persisted`/`DeclareState` convenience as
`ExBlockEntity` (see [Block Entities](Block-Entities)), layered on top of the `pm_lastHours`
away-catch-up stamp it already writes by hand. Three rungs, in the order to reach for them:

1. **Attribute.** Mark the field `[Persist]` and write nothing else - the key defaults to the
   field name with its leading underscore stripped.
   ```csharp
   [Persist] private float _tempC;
   ```
2. **`Persisted`.** Override `DeclareState` for anything the attribute can't express (a computed
   getter/setter pair, a custom key, a value with its own tree shape).
   ```csharp
   protected override void DeclareState(ExBlockState s) =>
       s.Float("temp", () => _tempC, v => _tempC = v);
   ```
3. **Hand-written.** Override `ToTreeAttributes`/`FromTreeAttributes` yourself and call `base` -
   nothing here forces the other two rungs; a machine that already has the pair keeps it.

## Related pages

- [Block Networks](Block-Networks) - what `ConnectedNetwork<TNet>` returns.
- [Multiblock Structures](Multiblock-Structures) - `BlockEntityMultiblockMachine` is the multiblock
  that hosts the same process.
- [Block Entities](Block-Entities) - `ExBlockEntity`, `ExBlockState`, `[Persist]` and `IPersistable`.
