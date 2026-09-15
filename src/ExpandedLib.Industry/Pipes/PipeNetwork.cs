using System;
using System.Collections.Generic;
using ExpandedLib.Catalogues;
using ExpandedLib.Helpers;
using ExpandedLib.Industry.Helpers;
using ExpandedLib.Networks;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Industry.Pipes;

/// <summary>
/// Concrete <see cref="BlockNetwork"/> for the pipe system: owns a single-medium
/// <see cref="PipeNetworkState"/> and implements production/consumption, pressure, merge/split
/// and tick logic.
/// </summary>
public class PipeNetwork : BlockNetwork {
  public override string NetworkType => "pipe";

  // Optional gas-vent strategy, supplied by the content mod. Null means every open end leaks.
  private readonly IPipeVentStrategy? _vent;

  // Medium policy (compatibility and priority), injected like the vent strategy.
  private readonly IMediumTaxonomy _taxonomy;

  public PipeNetwork(
    BlockNetworkModSystem system,
    IPipeVentStrategy? vent = null,
    IMediumTaxonomy? taxonomy = null
  )
    : base(system) {
    _vent = vent;
    _taxonomy = taxonomy ?? ExLiquids.Taxonomy;
  }

  /// <summary>Live pipe state, or <c>null</c> when empty.</summary>
  public new PipeNetworkState? State {
    get => base.State as PipeNetworkState;
    private set => base.State = value;
  }

  public override void RestoreState(object? state) {
    State = state as PipeNetworkState;
  }

  // Per-second throughput accumulators (litres), folded into State.FlowRate each tick.
  private float _producedAccum;
  private float _consumedAccum;

  // Displayed flow rate is smoothed (EMA); a drained run clears to empty only once idle for
  // EmptyClearDelaySeconds.
  private float _smoothedFlow;
  private float _secondsSinceFlow;
  private const float FlowSmoothingAlpha = 0.3f;
  private const float EmptyClearDelaySeconds = 3f;

  // In-game day stamp for evaporation; -1 until the first tick, unloaded time is uncharged.
  private double _lastEvapDays = -1;

  // Seconds the run has sat at/above its weakest pipe's burst pressure; at
  // PipeOverpressureSeconds a pipe bursts. Transient (a reload resets the grace).
  private float _overpressureSeconds;

  #region State inheritance

  public override void InheritStateFrom(BlockNetwork source) {
    if (source is not PipeNetwork other)
      return;
    State = other.State;
  }

  #endregion

  #region Gas pool

  /// <summary>
  /// Injects up to <paramref name="volume"/> L of gas, allowing overflow up to
  /// <paramref name="maxOutputPressure"/> x MaxVolume.
  /// </summary>
  /// <returns><c>true</c> if gas was accepted or the type or temperature changed.</returns>
  public bool TryProduceGas(
    float volume,
    float temperature,
    string gasType,
    IBlockAccessor blockAccessor,
    float maxOutputPressure = 1f,
    bool bypassLeakCap = false
  ) {
    State ??= new PipeNetworkState();
    // One medium per network; Volume > 0 guard lets a new medium claim pipes during the
    // empty-clear delay.
    if (State.Volume > 0f && !_taxonomy.Compatible(State.MediumType, gasType))
      return false;
    State.MaxVolume = Nodes.Count * ExlibValues.LitresPerPipe;

    // Choke is clamped by burst rating and, unless bypassLeakCap, the 1-atm leak cap.
    float ceilingPressure = Math.Min(
      maxOutputPressure,
      MinBurstPressure(blockAccessor)
    );
    if (State.IsLeaking && !bypassLeakCap)
      ceilingPressure = Math.Min(ceilingPressure, 1f);

    float ceiling = ceilingPressure * State.MaxVolume;
    // Bounded by headroom (pressure ceiling) and throughput (weakest segment, PerCallLimit).
    float actualVolume = Math.Min(
      Math.Min(volume, ceiling - State.Volume),
      PerCallLimit(blockAccessor)
    );

    if (actualVolume > 0 || State.Volume <= 0) {
      float totalVol = State.Volume + actualVolume;
      if (totalVol > 0) {
        State.Temperature =
          (State.Volume * State.Temperature + actualVolume * temperature)
          / totalVol;
      }

      if (State.Volume <= 0)
        State.MediumType = gasType;
      else if (actualVolume > 0)
        State.MediumType = _taxonomy.HigherPriority(State.MediumType, gasType);

      if (actualVolume > 0) {
        State.Volume += actualVolume;
        _producedAccum += actualVolume;
      }
      State.Pressure = PipeNetworkState.ComputeGasPressure(
        State.Volume,
        State.MaxVolume
      );
      BroadcastUpdate(blockAccessor);
      return true;
    }

    // Network is at its choke - only upgrade the gas type if needed.
    if (State.MediumType != gasType) {
      string upgraded = _taxonomy.HigherPriority(State.MediumType, gasType);
      if (upgraded != State.MediumType) {
        State.MediumType = upgraded;
        BroadcastUpdate(blockAccessor);
      }
    }

    return false;
  }

  /// <summary>Like <see cref="TryProduceGas"/> but returns the litres actually accepted.</summary>
  public float ProduceGasMeasured(
    float volume,
    float temperature,
    string gasType,
    IBlockAccessor blockAccessor,
    float maxOutputPressure = 1f,
    bool bypassLeakCap = false
  ) {
    float before = State?.Volume ?? 0f;
    TryProduceGas(
      volume,
      temperature,
      gasType,
      blockAccessor,
      maxOutputPressure,
      bypassLeakCap
    );
    return Math.Max(0f, (State?.Volume ?? 0f) - before);
  }

  /// <summary>Withdraws up to <paramref name="requestedVolume"/> litres of gas from the network.</summary>
  /// <returns>Litres actually consumed; 0 on a water run.</returns>
  public float TryConsumeGas(
    float requestedVolume,
    IBlockAccessor blockAccessor
  ) {
    if (State == null || State.IsLiquid)
      return 0f;

    // Bounded by pool volume and PerCallLimit.
    float available = Math.Min(
      Math.Min(requestedVolume, State.Volume),
      PerCallLimit(blockAccessor)
    );
    if (available > 0) {
      State.Volume -= available;
      _consumedAccum += available;
      State.Pressure = PipeNetworkState.ComputeGasPressure(
        State.Volume,
        State.MaxVolume
      );
      BroadcastUpdate(blockAccessor);
    }
    return available;
  }

  #endregion

  #region Liquid pool

  /// <summary>Injects up to <paramref name="volume"/> litres of water and sets the liquid pressure.</summary>
  /// <returns><c>true</c> if any water was accepted.</returns>
  public bool TryProduceLiquid(
    float volume,
    float temperature,
    float setPressure,
    IBlockAccessor blockAccessor
  ) {
    State ??= new PipeNetworkState();
    // One medium per network; mirrors the TryProduceGas guard.
    if (State.Volume > 0f && !_taxonomy.Compatible(State.MediumType, "Water"))
      return false;
    State.MaxVolume = Nodes.Count * ExlibValues.LitresPerPipe;
    // Pump's commanded pressure; applies once the line is brim-full, else pressure tracks the
    // fill ratio.
    State.FeedPressure = setPressure;

    // Headroom and throughput, the same pair the gas side takes.
    float actual = Math.Min(
      Math.Min(volume, State.MaxVolume - State.Volume),
      PerCallLimit(blockAccessor)
    );
    if (actual <= 0f) {
      // Brim-full: pressure still follows the feed pressure.
      State.Pressure = PipeNetworkState.ComputeLiquidPressure(
        State.Volume,
        State.MaxVolume,
        setPressure
      );
      return false;
    }

    float total = State.Volume + actual;
    if (total > 0)
      State.Temperature =
        (State.Volume * State.Temperature + actual * temperature) / total;

    State.MediumType = "Water";
    State.Volume = total;
    State.Pressure = PipeNetworkState.ComputeLiquidPressure(
      total,
      State.MaxVolume,
      setPressure
    );
    _producedAccum += actual;
    BroadcastUpdate(blockAccessor);
    return true;
  }

  /// <summary>Like <see cref="TryProduceLiquid"/> but returns the litres actually accepted.</summary>
  public float ProduceLiquidMeasured(
    float volume,
    float temperature,
    float setPressure,
    IBlockAccessor blockAccessor
  ) {
    float before = State?.Volume ?? 0f;
    TryProduceLiquid(volume, temperature, setPressure, blockAccessor);
    return Math.Max(0f, (State?.Volume ?? 0f) - before);
  }

  /// <summary>Withdraws up to <paramref name="requestedVolume"/> litres of water from the network.</summary>
  /// <returns>Litres actually consumed; 0 on a gas run.</returns>
  public float TryConsumeLiquid(
    float requestedVolume,
    IBlockAccessor blockAccessor
  ) {
    if (State == null || !State.IsLiquid)
      return 0f;

    float available = Math.Min(
      Math.Min(requestedVolume, State.Volume),
      PerCallLimit(blockAccessor)
    );
    if (available > 0) {
      State.Volume -= available;
      _consumedAccum += available;
      // Below brim-full, pressure tracks the fill ratio.
      State.Pressure =
        State.Volume <= 0f
          ? 0f
          : PipeNetworkState.ComputeLiquidPressure(
            State.Volume,
            State.MaxVolume,
            State.FeedPressure
          );
      BroadcastUpdate(blockAccessor);
    }
    return available;
  }

  #endregion

  #region Merge / Split

  /// <summary>
  /// Volume ceiling for a merged or split pool: <see cref="PipeNetworkState.MaxVolume"/> for
  /// liquid, or burst pressure x MaxVolume for gas.
  /// </summary>
  private float PoolVolumeCeiling(
    bool liquid,
    float maxVolume,
    IBlockAccessor world
  ) => liquid ? maxVolume : ComputeMinBurstPressure(world) * maxVolume;

  public override void OnMerge(BlockNetwork other, IBlockAccessor world) {
    if (other is not PipeNetwork otherPipe)
      return;

    if (otherPipe.State == null) {
      if (State != null)
        State.MaxVolume = Nodes.Count * ExlibValues.LitresPerPipe;
      return;
    }

    if (State == null) {
      State = otherPipe.State;
      State.MaxVolume = Nodes.Count * ExlibValues.LitresPerPipe;
      State.Volume = Math.Min(
        State.Volume,
        PoolVolumeCeiling(State.IsLiquid, State.MaxVolume, world)
      );
      return;
    }

    State.MaxVolume = Nodes.Count * ExlibValues.LitresPerPipe;

    // Incompatible media cannot blend: the larger run wins and the smaller run's content is
    // discarded.
    if (!_taxonomy.Compatible(State.MediumType, otherPipe.State.MediumType)) {
      if (otherPipe.State.Volume > State.Volume)
        State = otherPipe.State;
      State.MaxVolume = Nodes.Count * ExlibValues.LitresPerPipe;
      State.Volume = Math.Min(
        State.Volume,
        PoolVolumeCeiling(State.IsLiquid, State.MaxVolume, world)
      );
      State.Pressure = State.IsLiquid
        ? PipeNetworkState.ComputeLiquidPressure(
          State.Volume,
          State.MaxVolume,
          State.FeedPressure
        )
        : PipeNetworkState.ComputeGasPressure(State.Volume, State.MaxVolume);
      return;
    }

    // Same medium: blend temperature volume-weighted and combine the volumes.
    float total = State.Volume + otherPipe.State.Volume;
    if (total > 0) {
      State.Temperature =
        (
          State.Volume * State.Temperature
          + otherPipe.State.Volume * otherPipe.State.Temperature
        ) / total;
    }

    // Gas runs resolve the dominant type by priority; water keeps its medium.
    if (!State.IsLiquid) {
      if (State.Volume <= 0)
        State.MediumType = otherPipe.State.MediumType;
      else if (otherPipe.State.Volume > 0)
        State.MediumType = _taxonomy.HigherPriority(
          State.MediumType,
          otherPipe.State.MediumType
        );
    }

    State.Volume = Math.Min(
      total,
      PoolVolumeCeiling(State.IsLiquid, State.MaxVolume, world)
    );
    if (State.IsLiquid) {
      // Feed pressure takes the stronger pump; run pressure follows the combined fill.
      State.FeedPressure = Math.Max(
        State.FeedPressure,
        otherPipe.State.FeedPressure
      );
      State.Pressure = PipeNetworkState.ComputeLiquidPressure(
        State.Volume,
        State.MaxVolume,
        State.FeedPressure
      );
    } else
      State.Pressure = PipeNetworkState.ComputeGasPressure(
        State.Volume,
        State.MaxVolume
      );
  }

  public override void OnSplitFragment(
    BlockNetwork original,
    IBlockAccessor world
  ) {
    if (original is not PipeNetwork origPipe || origPipe.State == null) {
      State = null;
      return;
    }

    int origCount = Math.Max(1, original.Nodes.Count);
    float maxVolume = Nodes.Count * ExlibValues.LitresPerPipe;
    bool liquid = origPipe.State.IsLiquid;
    // Fragment volume is proportional to node share, capped at the burst ceiling for gas.
    float frag = Math.Min(
      origPipe.State.Volume / origCount * Nodes.Count,
      PoolVolumeCeiling(liquid, maxVolume, world)
    );

    if (frag <= 0f) {
      State = null;
      return;
    }
    State = new PipeNetworkState {
      MaxVolume = maxVolume,
      Volume = frag,
      Temperature = origPipe.State.Temperature,
      MediumType = origPipe.State.MediumType,
      FeedPressure = origPipe.State.FeedPressure,
      Pressure = liquid
        ? PipeNetworkState.ComputeLiquidPressure(
          frag,
          maxVolume,
          origPipe.State.FeedPressure
        )
        : PipeNetworkState.ComputeGasPressure(frag, maxVolume),
    };
  }

  #endregion

  #region Tick

  public override void OnTick(
    IBlockAccessor blockAccessor,
    float dt,
    BlockNetworkModSystem manager
  ) {
    // Folds produce/consume peaks into instant flow and drops the per-tick topology caches; runs
    // ahead of the empty-State bail.
    float instantFlow = Math.Max(_producedAccum, _consumedAccum);
    _producedAccum = 0f;
    _consumedAccum = 0f;
    _minBurstCache = null;
    _minThroughputCache = null;

    if (State == null)
      return;

    SmoothFlow(instantFlow, dt);

    // ClearIfEmptyAndIdle is the only pass that clears State; every other pass mutates this
    // instance.
    PipeNetworkState state = State;
    var pass = new TickPass { Liquid = state.IsLiquid };

    RecomputePressureAndFlow(state, pass);
    ComputeLeakFractions(state, pass);
    ClassifyOpenings(blockAccessor, manager, state, pass);
    ApplyVentDraw(manager, state, pass);
    ApplyLeakLoss(dt, state, pass);
    ApplyEvaporation(manager, state, pass);
    RepressureAfterVentLeak(state, pass);
    ApplyPassiveCooling(state, pass, dt);
    ClearIfEmptyAndIdle(state, pass);

    if (pass.Changed)
      BroadcastUpdate(blockAccessor);

    TickOverpressureAndBurst(blockAccessor, manager, dt);
  }

  /// <summary>Smooths the displayed throughput (EMA) and tracks how long the run has been idle.</summary>
  private void SmoothFlow(float instantFlow, float dt) {
    _smoothedFlow += (instantFlow - _smoothedFlow) * FlowSmoothingAlpha;
    if (_smoothedFlow < 0.01f)
      _smoothedFlow = 0f;
    if (instantFlow > 0.01f)
      _secondsSinceFlow = 0f;
    else
      _secondsSinceFlow += dt;
  }

  /// <summary>Refreshes the broadcast max-volume, pressure and (smoothed) flow rate from the node set.</summary>
  private void RecomputePressureAndFlow(PipeNetworkState state, TickPass pass) {
    state.MaxVolume = Nodes.Count * ExlibValues.LitresPerPipe;
    // Gas pressure is the volume ratio; liquid pressure is the fill ratio until brim-full, then
    // the feed pressure.
    float newPressure = pass.Liquid
      ? PipeNetworkState.ComputeLiquidPressure(
        state.Volume,
        state.MaxVolume,
        state.FeedPressure
      )
      : PipeNetworkState.ComputeGasPressure(state.Volume, state.MaxVolume);
    if (Math.Abs(state.Pressure - newPressure) > 0.02f) {
      state.Pressure = newPressure;
      pass.Changed = true;
    }

    if (Math.Abs(state.FlowRate - _smoothedFlow) > 0.01f) {
      state.FlowRate = _smoothedFlow;
      pass.Changed = true;
    }
  }

  /// <summary>Particle density for leaks this tick, scaled by total leak rate: gas 1-8 L/s, water
  /// 1-5 L/s.</summary>
  private void ComputeLeakFractions(PipeNetworkState state, TickPass pass) {
    float gasLeakRate = Math.Min(
      Math.Max(0f, state.Volume - state.MaxVolume),
      ExlibValues.GasLeakRate
    );
    pass.GasLeakFrac = Math.Clamp(
      (gasLeakRate - 1f) / (ExlibValues.GasLeakRate - 1f),
      0f,
      4f
    );
    pass.WaterLeakFrac = Math.Clamp((state.Volume - 1f) / 4f, 0f, 1f);
  }

  /// <summary>
  /// Single pass over the nodes: classifies each open connector as vent or leak, counts
  /// consumers, and fires each leaking node's hooks.
  /// </summary>
  private void ClassifyOpenings(
    IBlockAccessor blockAccessor,
    BlockNetworkModSystem manager,
    PipeNetworkState state,
    TickPass pass
  ) {
    foreach (var pos in Nodes) {
      var be = blockAccessor.GetBlockEntity(pos);
      if (be is IPipeNode)
        pass.Consumers++;

      if (blockAccessor.GetBlock(pos) is not BlockNetworkNode node)
        continue;

      BlockFacing[] openFaces = manager.GetOpenConnectorFaces(
        blockAccessor,
        pos,
        node
      );
      if (openFaces.Length == 0)
        continue;

      int airOpen = 0;
      for (int i = 0; i < openFaces.Length; i++) {
        BlockFacing face = openFaces[i];
        BlockPos nPos = pos.AddCopy(face);
        Block neighbour = blockAccessor.GetBlock(nPos);
        // Vent classification comes from the content mod's strategy; a vent face does not count
        // as a leak.
        if (
          _vent != null
          && _vent.TryClassifyVent(
            blockAccessor,
            node,
            pos,
            face,
            neighbour,
            out BlockPos ventPos
          )
        ) {
          pass.ChimneyVents.Add(ventPos);
          continue;
        }
        if (neighbour.FirstCodePart() == "air")
          openFaces[airOpen++] = face;
      }
      if (airOpen == 0)
        continue;

      pass.TotalLeaks += airOpen;

      BlockFacing[] leakFaces =
        airOpen == openFaces.Length ? openFaces : openFaces[..airOpen];
      if (be is INetworkNode nodeEntity && state.Volume > 0) {
        // OnLeak sprays particles on a pipe (default no-op elsewhere); nodes also get the
        // open-connectors hook.
        nodeEntity.OnLeak(
          leakFaces,
          pass.Liquid,
          pass.Liquid ? pass.WaterLeakFrac : pass.GasLeakFrac
        );
        nodeEntity.OnOpenConnectorsChanged(leakFaces);
      }
    }

    if (state.OpeningsCount != pass.TotalLeaks) {
      state.OpeningsCount = pass.TotalLeaks;
      pass.Changed = true;
    }
  }

  /// <summary>Vent draw (gas only) via the content mod's strategy; a network with none vents
  /// nothing.</summary>
  private void ApplyVentDraw(
    BlockNetworkModSystem manager,
    PipeNetworkState state,
    TickPass pass
  ) {
    float vented =
      _vent?.Vent(pass.ChimneyVents, state, pass.Liquid, manager) ?? 0f;
    if (vented > 0f) {
      _consumedAccum += vented;
      pass.Changed = true;
    }
  }

  /// <summary>Leak loss: a gas leak relieves pressure at a fixed rate regardless of opening
  /// count; a water leak drains at a fixed rate.</summary>
  private void ApplyLeakLoss(float dt, PipeNetworkState state, TickPass pass) {
    if (pass.TotalLeaks > 0 && state.Volume > 0f) {
      if (pass.Liquid) {
        float lost = Math.Min(state.Volume, ExlibValues.LiquidLeakRate * dt);
        state.Volume -= lost;
        if (state.Volume <= 0f)
          state.Pressure = 0f;
      } else {
        float lost = Math.Min(state.Volume, ExlibValues.GasLeakRate);
        state.Volume -= lost;
        float ambient = ExlibValues.PipeAmbientTemperature;
        if (state.Temperature > ambient)
          state.Temperature = Math.Max(ambient, state.Temperature - 5.0f);
      }
      pass.Changed = true;
    }
  }

  /// <summary>Natural evaporation of a water run, measured off the calendar; unloaded time is
  /// uncharged.</summary>
  private void ApplyEvaporation(
    BlockNetworkModSystem manager,
    PipeNetworkState state,
    TickPass pass
  ) {
    double nowDays = manager.ServerWorld?.Calendar?.TotalDays ?? -1;
    if (nowDays >= 0) {
      if (pass.Liquid && _lastEvapDays >= 0 && state.Volume > 0f) {
        float evap = (float)(
          ExlibValues.EvaporationLitresPerDay * (nowDays - _lastEvapDays)
        );
        if (evap > 0f) {
          state.Volume = Math.Max(0f, state.Volume - evap);
          if (state.Volume <= 0f)
            state.Pressure = 0f;
          pass.Changed = true;
        }
      }
      _lastEvapDays = nowDays;
    }
  }

  /// <summary>Recomputes gas pressure once venting and leaking are applied.</summary>
  private void RepressureAfterVentLeak(PipeNetworkState state, TickPass pass) {
    if (!pass.Liquid && (pass.ChimneyVents.Count > 0 || pass.TotalLeaks > 0))
      state.Pressure = PipeNetworkState.ComputeGasPressure(
        state.Volume,
        state.MaxVolume
      );
  }

  /// <summary>Passive cooling: a gas run always sheds heat toward ambient, regardless of
  /// <c>pass.Consumers</c>.</summary>
  private void ApplyPassiveCooling(
    PipeNetworkState state,
    TickPass pass,
    float dt
  ) {
    float ambient = ExlibValues.PipeAmbientTemperature;
    if (pass.Liquid || state.Volume <= 0 || state.Temperature <= ambient)
      return;

    state.Temperature = Math.Max(
      ambient,
      state.Temperature - ExlibValues.PipeGasCoolPerSecond * dt
    );
    pass.Changed = true;
  }

  /// <summary>Clears the state once the run has been drained and idle for
  /// <see cref="EmptyClearDelaySeconds"/>.</summary>
  private void ClearIfEmptyAndIdle(PipeNetworkState state, TickPass pass) {
    if (state.Volume <= 0 && _secondsSinceFlow >= EmptyClearDelaySeconds) {
      State = null;
      _smoothedFlow = 0f;
      pass.Changed = true;
    }
  }

  /// <summary>
  /// Over-pressure timer and burst: holding at burst pressure for PipeOverpressureSeconds bursts
  /// a pipe; relief resets the grace. Runs last in the tick.
  /// </summary>
  private void TickOverpressureAndBurst(
    IBlockAccessor blockAccessor,
    BlockNetworkModSystem manager,
    float dt
  ) {
    bool pressureFailure = false;
    if (State != null) {
      float minBurst = MinBurstPressure(blockAccessor);
      bool overPressure =
        !State.IsLiquid
        && State.Volume > 0f
        && minBurst < float.MaxValue
        && State.Pressure >= minBurst - 0.001f;

      if (overPressure) {
        _overpressureSeconds += dt;
        if (_overpressureSeconds >= ExlibValues.PipeOverpressureSeconds) {
          pressureFailure = true;
          _overpressureSeconds = 0f;
        }
      } else if (_overpressureSeconds > 0f)
        _overpressureSeconds = 0f;
    }

    // Each burst removes a node (fracturing the run) and drops the pipe's materials.
    if (State != null && pressureFailure) {
      foreach (var pos in CollectBursts(blockAccessor))
        ExecuteBurst(pos, blockAccessor, manager);
    }
  }

  /// <summary>Per-tick working set threaded through the <see cref="OnTick"/> passes.</summary>
  private sealed class TickPass {
    public bool Changed;
    public bool Liquid;
    public int Consumers;
    public int TotalLeaks;
    public readonly List<BlockPos> ChimneyVents = new();
    public float GasLeakFrac;
    public float WaterLeakFrac;
  }

  // Cached; changes only with the node set, invalidated by OnTopologyChanged and once per tick
  // in OnTick.
  private float? _minBurstCache;

  // Same lifetime and invalidation as the burst cache above.
  private float? _minThroughputCache;

  /// <summary>Drops topology-derived caches when the manager changes the node set.</summary>
  public override void OnTopologyChanged() {
    _minBurstCache = null;
    _minThroughputCache = null;
  }

  /// <summary>The smallest throughput (L/s) across the run, the weakest-link rule.
  /// <see cref="float.MaxValue"/> when the run holds no throughput-limited blocks.</summary>
  private float MinThroughput(IBlockAccessor world) =>
    _minThroughputCache ??= ComputeMinThroughput(world);

  private float ComputeMinThroughput(IBlockAccessor world) {
    float min = float.MaxValue;
    foreach (var pos in Nodes)
      if (world.GetBlock(pos) is IThroughputLimitedPipe p)
        min = Math.Min(min, p.MaxThroughput);
    return min;
  }

  /// <summary>The most one call may move, bounded by the run's weakest segment; stateless,
  /// litres per second at the fixed 1000 ms tick.</summary>
  private float PerCallLimit(IBlockAccessor world) => MinThroughput(world);

  /// <summary>The weakest pipe's burst pressure (atm) across the run, capping how far the gas
  /// pool can be pressurised. <see cref="float.MaxValue"/> when the run holds no pipes.</summary>
  private float MinBurstPressure(IBlockAccessor world) =>
    _minBurstCache ??= ComputeMinBurstPressure(world);

  private float ComputeMinBurstPressure(IBlockAccessor world) {
    float minBurst = float.MaxValue;
    foreach (var pos in Nodes)
      if (world.GetBlock(pos) is IBurstablePipe p && p.CanBurst)
        minBurst = Math.Min(minBurst, p.BurstPressure);
    return minBurst;
  }

  // Fallback RNG, used only when no server world is available.
  private readonly Random _rand = new();

  /// <summary>Picks one random pipe past its over-pressure grace to fail this tick.</summary>
  private List<BlockPos> CollectBursts(IBlockAccessor world) {
    var result = new List<BlockPos>();
    if (State == null)
      return result;

    var pressureCandidates = new List<BlockPos>();

    foreach (var pos in Nodes) {
      if (world.GetBlock(pos) is not IBurstablePipe pipe || !pipe.CanBurst)
        continue;

      if (State.Pressure >= pipe.BurstPressure - 0.001f)
        pressureCandidates.Add(pos);
    }

    if (pressureCandidates.Count > 0) {
      Random rand = NetworkSystem?.ServerWorld?.Rand ?? _rand;
      result.Add(pressureCandidates[rand.Next(pressureCandidates.Count)]);
    }

    return result;
  }

  /// <summary>Breaks a failed pipe: drops its materials, removes it from the graph, and sets the
  /// cell to air.</summary>
  private static void ExecuteBurst(
    BlockPos pos,
    IBlockAccessor world,
    BlockNetworkModSystem manager
  ) {
    Block block = world.GetBlock(pos);
    if (block.BlockId == 0)
      return;

    var sworld = manager.ServerWorld;
    if (sworld != null) {
      ItemStack[]? drops = block.GetDrops(sworld, pos, null);
      if (drops != null) {
        foreach (var ds in drops)
          sworld.SpawnItemEntity(ds, pos.ToVec3d().Add(0.5, 0.5, 0.5));
      }

      // Server-spawned; effects broadcast to nearby clients.
      ExParticles.SteamPlume(sworld, pos, 18);
      ExSounds.PlayLocal(sworld, pos, ExSounds.SmallExplosion, 0.4f, 24f);
    }

    manager.RemoveNode(world, pos);
    world.SetBlock(0, pos);
  }

  #endregion
}
