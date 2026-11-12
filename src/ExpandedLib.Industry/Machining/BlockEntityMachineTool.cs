using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ExpandedLib.Blocks;
using ExpandedLib.Catalogues;
using ExpandedLib.Helpers;
using ExpandedLib.Industry.MechanicalPower;
using ExpandedLib.Machines;
using ExpandedLib.Networks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Industry.Machining;

/// <summary>
/// A machine tool on an "mpshaft" run: while engaged and driven it pays the mounted piece's cut in joules, counted in
/// game seconds, for the job <see cref="MachiningJobRegistry"/> gives the piece's code and a step its
/// <see cref="MachineKind"/> declares; a finished job leaves its result, byproducts and tool wear.
/// </summary>
/// <remarks>
/// A kind that declares <see cref="MachineFeed.Held"/> feed, the default, cuts only while a player holds its feed control
/// through <see cref="Hold"/>; a <see cref="MachineFeed.Self"/> kind is started and stopped.
/// Slots: work 0, tool 1, chips 2 (take-only) and, when the kind declares a fixture, fixture 3. The work slot
/// holds one piece. The run is read at <see cref="DriveCell"/> through <see cref="ReadDrive"/>.
/// Each tick credits its real seconds times the calendar's speed of time and speed multiplier; an unloaded chunk runs no
/// tick and a calendar jump adds no seconds, so neither credits, and a world with no calendar credits nothing.
/// </remarks>
public abstract class BlockEntityMachineTool
  : BlockEntityMachineStation,
    IMpEnergyConsumer {
  /// <summary>Slot index of the mounted piece.</summary>
  public const int WorkSlot = 0;

  /// <summary>Slot index of the fitted tool.</summary>
  public const int ToolSlot = 1;

  /// <summary>Slot index of the chips: byproducts and a worn tool's drop. Take-only.</summary>
  public const int ChipsSlot = 2;

  /// <summary>Slot index of the fixture, present only when the kind declares one.</summary>
  public const int FixtureSlot = 3;

  // Real milliseconds between ticks; a tick credits its own real seconds scaled to game seconds.
  private const int TickMs = 250;

  /// <summary>Milliseconds of <c>World.ElapsedMilliseconds</c> a hold lasts without a refresh; the cut tick drops a hold older than this.</summary>
  public const int HoldLapseMs = 1000;

  private readonly HostMembership _membership;
  private bool _running;
  private bool _cutting;
  private readonly Dictionary<string, long> _holds = [];
  private string? _piece;
  private string? _step;
  private double _joules;
  private MachineCutVerdict _verdict;

  /// <summary>The machine tool's place on the mpshaft run; carries no state, reads the live network each tick.</summary>
  private sealed class HostMembership(BlockEntityMachineTool owner)
    : BEBehaviorNetworkMember(owner) {
    public override string NetworkType {
      get => "mpshaft";
      protected set { }
    }
  }

  protected BlockEntityMachineTool() {
    // Added in the constructor: BlockEntity fans FromTreeAttributes and Initialize out over Behaviors.
    _membership = new HostMembership(this);
    Behaviors.Add(_membership);
  }

  /// <summary>The network manager this machine is registered with; the setter writes through for test injection.</summary>
  public BlockNetworkModSystem? NetworkSystem {
    get => _membership.NetworkSystem;
    protected set => _membership.NetworkSystem = value;
  }

  public override string InventoryClassName => "machinetool";

  /// <summary>The machine kind of this block, or null when its block names none that is declared.</summary>
  protected virtual MachineKind? Kind => MachineKind.Of(Block);

  /// <summary>The jobs the machine cuts from.</summary>
  protected virtual MachiningJobRegistry Jobs => MachiningJobRegistry.Shared;

  /// <summary>The materials the jobs' energies come from.</summary>
  protected virtual CuttingMaterialRegistry Materials =>
    CuttingMaterialRegistry.Shared;

  /// <summary>The machine kinds block info names as working a piece.</summary>
  protected virtual MachineKindRegistry Kinds => MachineKindRegistry.Shared;

  /// <summary>The run the machine's node is on, read at <see cref="DriveCell"/>.</summary>
  /// <returns><see cref="MpRunReading.None"/> while the node is on no run, as after a load before the network re-forms.</returns>
  protected virtual MpRunReading ReadDrive() =>
    MpRun.Read(NetworkSystem, NodeKey.Centre(DriveCell));

  /// <summary>The cell whose mpshaft node carries the drive; the machine's own cell by default. A block whose node sits on a structure filler names that cell.</summary>
  protected virtual BlockPos DriveCell => Pos;

  /// <summary>Whether the machine is engaged.</summary>
  public bool IsRunning => _running;

  /// <summary>Whether the last cut tick credited the cut. Set on the server and synced; false while stopped, undriven, starved or unable to cut.</summary>
  public bool IsCutting => _cutting;

  /// <summary>Fraction of the current job's energy delivered, 0 to 1; 0 when nothing is mounted or no job applies.</summary>
  public double Progress {
    get {
      (MachiningJob? job, MachineKindStep? step) = CurrentJob();
      if (job is null || step is null)
        return 0d;
      float energy = MachineCut.GameEnergy(job, step, Materials);
      return energy <= 0f ? 0d : Math.Clamp(_joules / energy, 0d, 1d);
    }
  }

  /// <summary>The verdict of the last tick while engaged; <see cref="MachineCutVerdict.Ready"/> before the first.</summary>
  public MachineCutVerdict Verdict => _verdict;

  protected override MachineSlotSpec[] SlotSpecs {
    get {
      List<MachineSlotSpec> specs =
      [
        MachineSlotSpec.AnyInput(),
        MachineSlotSpec.Input(stack => MachineTool.KindOf(stack) != null),
        MachineSlotSpec.Output(),
      ];
      if (Kind?.Fixture == true)
        specs.Add(MachineSlotSpec.AnyInput());
      return [.. specs];
    }
  }

  protected override void DeclareState(ExBlockState state) =>
    state
      .Bool("toolRunning", () => _running, v => _running = v)
      .Bool("toolCutting", () => _cutting, v => _cutting = v)
      .String("toolPiece", () => _piece, v => _piece = v)
      .String("toolStep", () => _step, v => _step = v)
      .Double("toolJoules", () => _joules, v => _joules = v)
      .Enum("toolVerdict", () => _verdict, v => _verdict = v);

  public override void FromTreeAttributes(
    ITreeAttribute tree,
    IWorldAccessor worldForResolving
  ) {
    bool was = _cutting;
    base.FromTreeAttributes(tree, worldForResolving);
    if (Api is { Side: EnumAppSide.Client } && was != _cutting)
      OnCuttingChanged(_cutting);
  }

  /// <summary>Called on the client when <see cref="IsCutting"/> flips, with its new value.</summary>
  /// <param name="cutting">Whether the machine is now cutting.</param>
  protected virtual void OnCuttingChanged(bool cutting) { }

  public override void Initialize(ICoreAPI api) {
    base.Initialize(api);
    Inventory[WorkSlot].MaxSlotStackSize = 1;
    Inventory.SlotModified += OnSlotModified;
    if (api.Side == EnumAppSide.Server)
      RegisterGameTickListener(OnCutTick, TickMs);
  }

  /// <summary>Adds the mounted piece's item mesh at its step's anchor, turned with the block, and leaves the block's own
  /// mesh to be drawn. The piece after a cut is drawn at the anchor of the step whose job yields it. Adds nothing when
  /// the work slot is empty or no anchor applies.</summary>
  /// <returns>Always false.</returns>
  public override bool OnTesselation(
    ITerrainMeshPool mesher,
    ITesselatorAPI tesselator
  ) {
    if (
      Api is ICoreClientAPI capi
      && Inventory[WorkSlot].Itemstack is { } piece
      && MountAnchor(piece) is { } anchor
    ) {
      CompositeShape? rotation = Block?.Shape;
      MachineToolRenderer.AddPiece(
        capi,
        mesher,
        tesselator,
        piece,
        MachineAnchors.Compose(
          anchor,
          new Vec3f(
            rotation?.rotateX ?? 0f,
            rotation?.rotateY ?? 0f,
            rotation?.rotateZ ?? 0f
          )
        )
      );
    }
    return false;
  }

  private MachineAnchor? MountAnchor(ItemStack piece) {
    if (Kind is not { } kind)
      return null;
    string? name = CurrentJob().Step?.Anchor;
    if (name is null) {
      string code = piece.Collectible.Code.ToString();
      foreach (MachiningJob job in Jobs.All)
        if (
          string.Equals(job.Output, code, StringComparison.OrdinalIgnoreCase)
          && kind.StepOf(job.Step)?.Anchor is { } made
        ) {
          name = made;
          break;
        }
    }
    foreach (MachineAnchor anchor in kind.Anchors)
      if (string.Equals(anchor.Name, name, StringComparison.OrdinalIgnoreCase))
        return anchor;
    return null;
  }

  /// <summary>Engages a self-fed machine: the cut counts game time from now, tick by tick while loaded.</summary>
  /// <returns>False when it is already engaged, its block names no declared kind or its kind is held.</returns>
  public bool TryStart() {
    if (_running || Kind is not { Feed: MachineFeed.Self })
      return false;
    _running = true;
    SyncPiece();
    MarkDirty();
    return true;
  }

  /// <summary>Disengages the machine and, on a held kind, drops every hold; the joules already delivered stay with the piece.</summary>
  public void Stop() {
    _holds.Clear();
    if (!_running)
      return;
    _running = false;
    _cutting = false;
    MarkDirty();
  }

  /// <summary>Begins or refreshes <paramref name="playerUid"/>'s hold on the feed control of a held kind and engages
  /// the machine. The hold lapses on the cut tick once <see cref="HoldLapseMs"/> pass without a refresh. Holds are not
  /// saved. Does nothing on a self-fed kind or a block that names no declared kind. Server side.</summary>
  /// <param name="playerUid">Unique id of the holding player.</param>
  public void Hold(string playerUid) {
    if (Kind is not { Feed: MachineFeed.Held } || Api is null)
      return;
    _holds[playerUid] = Api.World.ElapsedMilliseconds;
    if (_running)
      return;
    _running = true;
    SyncPiece();
    MarkDirty();
  }

  /// <summary>Ends <paramref name="playerUid"/>'s hold; the machine disengages with the last one and keeps its joules.
  /// Does nothing for a player who holds nothing.</summary>
  /// <param name="playerUid">Unique id of the releasing player.</param>
  public void Release(string playerUid) {
    if (!_holds.Remove(playerUid) || _holds.Count > 0)
      return;
    _running = false;
    _cutting = false;
    MarkDirty();
  }

  private void DropLapsedHolds() {
    long now = Api.World.ElapsedMilliseconds;
    foreach (string uid in _holds.Keys.ToList())
      if (now - _holds[uid] >= HoldLapseMs)
        _holds.Remove(uid);
  }

  private bool SetCutting(bool cutting) {
    if (_cutting == cutting)
      return false;
    _cutting = cutting;
    return true;
  }

  #region Network

  /// <inheritdoc/>
  public float LoadTorque(float speed) {
    if (!_running || Kind is not { } kind)
      return 0f;
    (MachiningJob? job, MachineKindStep? step) = CurrentJob();
    if (
      job is null
      || step is null
      || MachineCut.Energy(job, step.Motion, Materials) <= 0f
    )
      return 0f;
    return Evaluate(job, kind, step, 1f) == MachineCutVerdict.Ready
      ? MachineCut.LoadTorque(kind, step, true)
      : 0f;
  }

  /// <inheritdoc/>
  public float StandingNm => _running ? Kind?.StandingNm ?? 0f : 0f;

  #endregion

  #region The cut

  private (MachiningJob? Job, MachineKindStep? Step) CurrentJob() {
    string? code = Inventory[WorkSlot].Itemstack?.Collectible?.Code?.ToString();
    if (code is null || Kind is not { } kind)
      return (null, null);
    string? fixture = kind.Fixture
      ? Inventory[FixtureSlot].Itemstack?.Collectible?.Code?.ToString()
      : null;
    foreach (MachineKindStep step in kind.Steps) {
      MachiningJob? job =
        Jobs.Job(step.Step, code, fixture)
        ?? Jobs.JobsFor(step.Step, code).FirstOrDefault();
      if (job is not null && MachineCut.OffersTo(job, kind))
        return (job, step);
    }
    return (null, null);
  }

  private MachineToolSpec? ToolSpec() =>
    MachineTool.TryParse(
      Inventory[ToolSlot]
        .Itemstack
        ?.Collectible
        ?.Attributes
        ?[MachineTool.AttributeKey],
      out MachineToolSpec spec,
      out _
    )
      ? spec
      : null;

  private MachineCutVerdict Evaluate(
    MachiningJob? job,
    MachineKind kind,
    MachineKindStep? step,
    float speed
  ) {
    bool fixture =
      job?.Fixture is { } code
      && kind.Fixture
      && string.Equals(
        Inventory[FixtureSlot].Itemstack?.Collectible?.Code?.ToString(),
        code,
        StringComparison.OrdinalIgnoreCase
      );
    return MachineCut.Verdict(
      job,
      kind,
      ToolSpec(),
      fixture,
      job is null || ChipsAccept(job.Byproducts),
      speed
    );
  }

  private bool SyncPiece() {
    string? piece = Inventory[WorkSlot]
      .Itemstack?.Collectible?.Code?.ToString();
    string? step = CurrentJob().Step?.Step;
    if (piece == _piece && step == _step)
      return false;
    _piece = piece;
    _step = step;
    _joules = 0d;
    return true;
  }

  private void OnSlotModified(int slot) {
    if (slot == WorkSlot && SyncPiece())
      MarkDirty(true);
  }

  private void OnCutTick(float dt) {
    if (Kind is not { } kind)
      return;
    if (kind.Feed == MachineFeed.Held) {
      DropLapsedHolds();
      if (_holds.Count == 0) {
        if (_running || _cutting) {
          _running = false;
          _cutting = false;
          MarkDirty();
        }
        return;
      }
    }
    if (!_running)
      return;
    bool changed = SyncPiece();
    bool redraw = changed;

    MpRunReading reading = ReadDrive();
    if (reading == MpRunReading.None) {
      changed |= SetCutting(false);
      if (changed)
        MarkDirty(redraw);
      return;
    }
    double gameSeconds = Api.World.Calendar is { } calendar
      ? dt * (double)calendar.SpeedOfTime * calendar.CalendarSpeedMul
      : 0d;
    float speed = MathF.Abs(reading.SpeedRadS);

    (MachiningJob? job, MachineKindStep? step) = CurrentJob();
    MachineCutVerdict verdict = Evaluate(job, kind, step, speed);
    if (verdict != _verdict) {
      _verdict = verdict;
      changed = true;
    }
    bool cutting = false;
    if (verdict == MachineCutVerdict.Ready) {
      float energy = MachineCut.GameEnergy(job!, step!, Materials);
      if (energy <= 0f) {
        changed |= SetCutting(false);
        if (changed)
          MarkDirty(redraw);
        return;
      }
      double remaining = energy - _joules;
      double credit = MachineCut.Credit(
        MachineCut.LoadTorque(kind, step, true),
        speed,
        gameSeconds,
        remaining
      );
      _joules += credit;
      cutting = credit > 0d;
      changed |= cutting;
      if (credit >= remaining) {
        Complete(job!);
        changed = true;
      }
    }
    changed |= SetCutting(cutting);
    if (changed)
      MarkDirty(redraw);
  }

  private void Complete(MachiningJob job) {
    ItemStack? result = Stack(job.Output, job.Count);
    if (result is null) {
      Api.Logger.Warning(
        "[exlib] {0} job output '{1}' names no item or block; the cut produced nothing.",
        Block?.Code?.Path ?? "machine tool",
        job.Output
      );
      _joules = 0d;
      return;
    }
    Inventory[WorkSlot].Itemstack = result;
    Inventory[WorkSlot].MarkDirty();
    foreach (MachiningByproduct byproduct in job.Byproducts)
      if (Stack(byproduct.Code, byproduct.Count) is { } chips)
        PutInChips(chips);
    if (MachineToolWear.Apply(Inventory[ToolSlot], job.Wear) is { } spent)
      PutInChips(spent);
    _piece = result.Collectible.Code.ToString();
    _step = CurrentJob().Step?.Step;
    _joules = 0d;
  }

  private ItemStack? Stack(string code, int count) {
    var location = new AssetLocation(code);
    CollectibleObject? collectible =
      (CollectibleObject?)Api.World.GetItem(location)
      ?? Api.World.GetBlock(location);
    return collectible is null ? null : new ItemStack(collectible, count);
  }

  private bool ChipsAccept(IReadOnlyList<MachiningByproduct> byproducts) {
    if (byproducts.Count == 0)
      return true;
    string code = byproducts[0].Code;
    if (
      byproducts.Any(b =>
        !string.Equals(b.Code, code, StringComparison.OrdinalIgnoreCase)
      ) || Stack(code, 1) is not { } one
    )
      return false;
    return ChipsRoom(one, byproducts.Sum(b => b.Count));
  }

  private bool ChipsRoom(ItemStack stack, int count) {
    ItemStack? held = Inventory[ChipsSlot].Itemstack;
    if (held is null)
      return count <= stack.Collectible.MaxStackSize;
    return held.Collectible.Equals(stack.Collectible)
      && held.StackSize + count <= held.Collectible.MaxStackSize;
  }

  private void PutInChips(ItemStack stack) {
    ItemSlot chips = Inventory[ChipsSlot];
    if (!ChipsRoom(stack, stack.StackSize)) {
      Api.World.SpawnItemEntity(stack, Pos.ToVec3d().Add(0.5, 1.0, 0.5));
      return;
    }
    if (chips.Empty)
      chips.Itemstack = stack;
    else
      chips.Itemstack.StackSize += stack.StackSize;
    chips.MarkDirty();
  }

  #endregion

  #region Block info

  /// <summary>The steps and machine kinds that work the piece with code <paramref name="code"/>, as "step (kind, kind)" entries in step order; empty when no declared job takes it.</summary>
  /// <param name="code">Item code of the piece.</param>
  public IReadOnlyList<string> WorkedAt(string code) =>
    [
      .. Jobs.StepsFor(code)
        .Select(step =>
          (
            Step: step,
            Kinds: Kinds
              .Offering(step)
              .Where(k =>
                Jobs.JobsFor(step, code).Any(j => MachineCut.OffersTo(j, k))
              )
              .ToList()
          )
        )
        .Where(x => x.Kinds.Count > 0)
        .Select(x =>
          $"{NameOf("exlib:machine-step-", x.Step)} ({string.Join(", ", x.Kinds.Select(k => NameOf(k.Code)))})"
        ),
    ];

  private static string NameOf(string key, string fallback) =>
    Lang.GetIfExists(key + fallback) ?? fallback;

  private static string NameOf(string kindCode) {
    var location = new AssetLocation(kindCode);
    return Lang.GetIfExists($"{location.Domain}:machine-kind-{location.Path}")
      ?? kindCode;
  }

  /// <summary>The block-info line for the mounted piece: the step and its percent, why the machine will not cut it, or where it is worked.</summary>
  /// <returns>The line, or null when the work slot is empty or the block names no kind.</returns>
  public string? InfoLine() {
    ItemStack? held = Inventory[WorkSlot].Itemstack;
    if (held is null || Kind is not { } kind)
      return null;

    (MachiningJob? job, MachineKindStep? step) = CurrentJob();
    if (job is null || step is null) {
      IReadOnlyList<string> worked = WorkedAt(held.Collectible.Code.ToString());
      return worked.Count == 0
        ? Lang.Get("exlib:machine-tool-info-nojob", held.GetName())
        : Lang.Get(
          "exlib:machine-tool-info-elsewhere",
          held.GetName(),
          string.Join("; ", worked)
        );
    }

    MachineCutVerdict verdict = _running
      ? _verdict
      : Evaluate(job, kind, step, 1f);
    return verdict switch {
      MachineCutVerdict.NoTool => Lang.Get("exlib:machine-tool-info-notool"),
      MachineCutVerdict.WrongTool => Lang.Get(
        "exlib:machine-tool-info-wrongtool",
        step.Tooling
      ),
      MachineCutVerdict.TierShort => Lang.Get(
        "exlib:machine-tool-info-tier",
        job.MinTier
      ),
      MachineCutVerdict.NoFixture => Lang.Get(
        "exlib:machine-tool-info-nofixture",
        Stack(job.Fixture!, 1)?.GetName() ?? job.Fixture
      ),
      MachineCutVerdict.ChipsFull => Lang.Get("exlib:machine-tool-info-chips"),
      MachineCutVerdict.NoDrive => Lang.Get("exlib:machine-tool-info-nodrive"),
      _ => Lang.Get(
        _running ? "exlib:machine-tool-info-cutting"
        : kind.Feed == MachineFeed.Held ? "exlib:machine-tool-info-held"
        : "exlib:machine-tool-info-stopped",
        NameOf("exlib:machine-step-", step.Step),
        (int)(Progress * 100d)
      ),
    };
  }

  public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc) {
    base.GetBlockInfo(forPlayer, dsc);
    string? line = InfoLine();
    if (line != null)
      dsc.AppendLine(line);
  }

  #endregion
}
