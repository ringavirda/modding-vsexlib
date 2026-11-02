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
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Industry.Machining;

/// <summary>
/// A machine tool on an "mpshaft" run: one piece is mounted in the work slot, a tool is fitted, and while the
/// machine is engaged and its drive turns the cut is paid in joules, counted in game seconds. The job comes from
/// <see cref="MachiningJobRegistry"/> by the piece's code and a step the block's <see cref="MachineKind"/> declares.
/// A finished job puts its result in the work slot, its byproducts in the chips slot, and wears the tool.
/// </summary>
/// <remarks>
/// Slots: work 0, tool 1, chips 2 (take-only) and, when the kind declares a fixture, fixture 3. The work slot
/// holds one piece. The run is read at <see cref="DriveCell"/> through <see cref="ReadDrive"/>.
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

  // Real milliseconds between ticks; a tick credits the game time elapsed since the last one.
  private const int TickMs = 250;

  private const string CursorKey = "toolCursor";

  private readonly HostMembership _membership;
  private readonly GameClockCursor _cursor = new();
  private bool _running;
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

  /// <summary>Fraction of the current job's energy delivered, 0 to 1; 0 when nothing is mounted or no job applies.</summary>
  public double Progress {
    get {
      (MachiningJob? job, MachineKindStep? step) = CurrentJob();
      if (job is null || step is null)
        return 0d;
      float energy = MachineCut.Energy(job, step.Motion, Materials);
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
      .String("toolPiece", () => _piece, v => _piece = v)
      .String("toolStep", () => _step, v => _step = v)
      .Double("toolJoules", () => _joules, v => _joules = v)
      .Enum("toolVerdict", () => _verdict, v => _verdict = v)
      .Tree(
        CursorKey,
        t => _cursor.Save(t, CursorKey),
        (t, _) => _cursor.Load(t, CursorKey)
      );

  public override void Initialize(ICoreAPI api) {
    base.Initialize(api);
    Inventory[WorkSlot].MaxSlotStackSize = 1;
    Inventory.SlotModified += OnSlotModified;
    if (api.Side == EnumAppSide.Server)
      RegisterGameTickListener(OnCutTick, TickMs);
  }

  /// <summary>Engages the machine: the cut counts game time from now.</summary>
  /// <returns>False when it is already engaged or its block names no declared kind.</returns>
  public bool TryStart() {
    if (_running || Kind is null)
      return false;
    _running = true;
    _cursor.Advance(GameTime.NowHours(Api.World));
    SyncPiece();
    MarkDirty();
    return true;
  }

  /// <summary>Disengages the machine; the joules already delivered stay with the piece.</summary>
  public void Stop() {
    if (!_running)
      return;
    _running = false;
    _cursor.Reset();
    MarkDirty();
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
    foreach (MachineKindStep step in kind.Steps)
      if (
        Jobs.Job(step.Step, code) is { } job
        && MachineCut.OffersTo(job, kind)
      )
        return (job, step);
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
      MarkDirty();
  }

  private void OnCutTick(float dt) {
    if (!_running || Kind is not { } kind)
      return;
    bool changed = SyncPiece();

    MpRunReading reading = ReadDrive();
    if (reading == MpRunReading.None) {
      if (changed)
        MarkDirty();
      return;
    }
    double gameSeconds = _cursor.Advance(GameTime.NowHours(Api.World)) * 3600d;
    float speed = MathF.Abs(reading.SpeedRadS);

    (MachiningJob? job, MachineKindStep? step) = CurrentJob();
    MachineCutVerdict verdict = Evaluate(job, kind, step, speed);
    if (verdict != _verdict) {
      _verdict = verdict;
      changed = true;
    }
    if (verdict == MachineCutVerdict.Ready) {
      float energy = MachineCut.Energy(job!, step!.Motion, Materials);
      if (energy <= 0f) {
        if (changed)
          MarkDirty();
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
      changed |= credit > 0d;
      if (credit >= remaining) {
        Complete(job!);
        changed = true;
      }
    }
    if (changed)
      MarkDirty();
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
        .Select(step => (Step: step, Job: Jobs.Job(step, code)))
        .Where(x => x.Job is not null)
        .Select(x =>
          (
            x.Step,
            Kinds: Kinds
              .Offering(x.Step)
              .Where(k => MachineCut.OffersTo(x.Job!, k))
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
        _running
          ? "exlib:machine-tool-info-cutting"
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
