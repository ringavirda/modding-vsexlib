using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Catalogues;
using ExpandedLib.Industry.Machining;
using ExpandedLib.Industry.MechanicalPower;
using ExpandedLib.Networks;
using ExpandedLib.Testing;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests.Machining;

/// <summary>The machine-tool host: its tick in game seconds, its state, its slots and its place on a real mpshaft run.</summary>
public sealed class BlockEntityMachineToolTests : System.IDisposable {
  // 822 W at 10 rad/s is 82.2 N*m; 1.0 kg of cast iron takes 185,800 J.
  private const double Joules = 185800d;
  private const double SecondsOfGameTime = 30d;
  private const double HoursPerRealSecond = SecondsOfGameTime / 3600d;
  private static readonly MachineKind Lathe = new(
    "test:lathe",
    10f,
    5f,
    false,
    [],
    [new MachineKindStep("turn", CutMotion.Rotary, 822f, "cutter", null, null)]
  );

  private static readonly MachineKind FixtureLathe = Lathe with {
    Code = "test:fixturelathe",
    Fixture = true,
  };

  private class Host : BlockEntityMachineTool {
    public MachineKind? KindIs = Lathe;
    public MpRunReading Drive = new(10f, 1000f);
    public bool OnRun;
    public readonly MachiningJobRegistry JobSet = new();
    public readonly MachineKindRegistry KindSet = new();
    public readonly CuttingMaterialRegistry MaterialSet =
      MachiningTestData.MaterialRegistry(MachiningTestData.Materials);

    protected override MachineKind? Kind => KindIs;

    protected override MachiningJobRegistry Jobs => JobSet;

    protected override CuttingMaterialRegistry Materials => MaterialSet;

    protected override MachineKindRegistry Kinds => KindSet;

    protected override MpRunReading ReadDrive() =>
      OnRun ? base.ReadDrive() : Drive;

    public void Join(BlockNetworkModSystem networks) =>
      NetworkSystem = networks;
  }

  private sealed class DrivenHost : Host, IMpEnergyProducer, IMpEnergyStorage {
    public float Inertia => 10f;

    public float DriveTorque(float speed) => 1000f * (1f - speed / 20f);

    public float DriveSlope(float speed) => -50f;
  }

  private readonly TestWorld _w = new();
  private readonly Item _cutter;
  private readonly Item _chips;
  private int _blockId = 700;

  public BlockEntityMachineToolTests() {
    TestLang.Init();
    _w.RegisterNetwork("mpshaft", sys => new MpShaftNetwork(sys));
    _w.RegisterItem("test:blank");
    _w.RegisterItem("test:blank2");
    _w.RegisterItem("test:turned");
    _w.RegisterItem("test:other");
    _w.RegisterItem("test:fixture");
    _w.RegisterItem("test:spent");
    _chips = _w.RegisterItem("test:chips");
    _chips.MaxStackSize = 8;
    _cutter = _w.RegisterItem("test:cutter");
    _cutter.Durability = 3;
    _cutter.Attributes = new JsonObject(
      JObject.FromObject(MachineTool.Tier(2, "cutter", "test:spent", 1))
    );
  }

  public void Dispose() => _w.Dispose();

  private MachiningJob Job(
    string input = "test:blank",
    string output = "test:turned",
    int minTier = 0,
    string? fixture = null,
    params MachiningByproduct[] byproducts
  ) {
    MachiningJob job = MachiningTestData.Job(
      "turn",
      input,
      output,
      material: "castiron"
    ) with {
      MinTier = minTier,
      Fixture = fixture,
      Byproducts = byproducts,
    };
    job.RemovedKg = 1f;
    job.ResolvedMaterial = "castiron";
    return job;
  }

  private Host Machine(
    MachineKind? kind = null,
    bool tool = true,
    string piece = "test:blank",
    Block? block = null,
    BlockPos? at = null,
    params MachiningJob[] jobs
  ) {
    var host = new Host { KindIs = kind ?? Lathe };
    host.JobSet.Contribute(
      MachiningTestData.Set(jobs.Length == 0 ? [Job()] : jobs)
    );
    BlockPos pos = at ?? new(_blockId, 0, 0);
    _w.Place(
      pos,
      block
        ?? TestBlocks.Configure(new Block(), $"test:lathe{_blockId}", _blockId),
      host
    );
    _blockId++;
    _w.Initialize(host);
    if (tool)
      host.Inventory[BlockEntityMachineTool.ToolSlot].Itemstack = new ItemStack(
        _cutter
      );
    if (piece.Length > 0)
      host.Inventory[BlockEntityMachineTool.WorkSlot].Itemstack = new ItemStack(
        _w.GetItem(new AssetLocation(piece))!
      );
    return host;
  }

  private string? Piece(Host host) =>
    host.Inventory[BlockEntityMachineTool.WorkSlot]
      .Itemstack?.Collectible.Code.ToString();

  private void Second(int times = 1) {
    for (int i = 0; i < times; i++) {
      _w.AdvanceHours(HoursPerRealSecond);
      _w.FireBlockEntityTicks(1f);
    }
  }

  private void Turn(MpShaftNetwork run, int ticks) {
    for (int i = 0; i < ticks; i++)
      run.OnTick(_w.Accessor, 0.2f, _w.Networks);
  }

  private static string Chips(Host host) =>
    host.Inventory[BlockEntityMachineTool.ChipsSlot] is { Itemstack: { } s }
      ? $"{s.Collectible.Code}x{s.StackSize}"
      : "";

  // Fails when progress counts the tick's real seconds instead of the game seconds the calendar moved.
  [Fact]
  public void The_cut_counts_game_seconds_and_finishes_on_the_eighth_second() {
    Host host = Machine();
    Assert.True(host.TryStart());

    Second(7);
    Assert.Equal("test:blank", Piece(host));
    Assert.Equal(7 * 24660d / Joules, host.Progress, 4);

    Second();
    Assert.Equal("test:turned", Piece(host));
    Assert.Equal(0d, host.Progress);
  }

  // Fails when a piece swapped mid-cut keeps the joules the first one was paid.
  [Fact]
  public void A_swapped_piece_starts_its_cut_over() {
    Host host = Machine(jobs: [Job(), Job("test:blank2")]);
    host.TryStart();
    Second(3);

    host.Inventory[BlockEntityMachineTool.WorkSlot].Itemstack = new ItemStack(
      _w.GetItem(new AssetLocation("test:blank2"))!
    );
    Second();

    Assert.Equal(24660d / Joules, host.Progress, 4);
  }

  [Fact]
  public void A_piece_with_no_job_runs_idle_and_names_where_it_is_worked() {
    Host host = Machine(piece: "test:other");
    host.KindSet.Contribute(MachineKindSet(Lathe));
    var borer = Lathe with {
      Code = "test:borer",
      Steps = [new("bore", CutMotion.Rotary, 500f, "cutter", null, null)],
    };
    host.KindSet.Contribute(MachineKindSet(borer));
    host.JobSet.Contribute(
      MachiningTestData.Set(
        MachiningTestData.Job("bore", "test:other", "test:turned")
      )
    );
    host.TryStart();

    Second(2);

    Assert.Equal(
      ["exlib:machine-step-bore (test:machine-kind-borer)"],
      host.WorkedAt("test:other")
    );
    Assert.Equal(0d, host.Progress);
    Assert.Equal("test:other", Piece(host));
    Assert.Contains("machine-tool-info-elsewhere", host.InfoLine());
    Assert.Equal(0f, host.LoadTorque(5f));
  }

  // Fails when block info names a machine the job's machines list excludes.
  [Fact]
  public void Block_info_names_only_the_machines_that_take_the_job() {
    MachiningJob limited = MachiningTestData.Job(
      "turn",
      "test:other",
      "test:turned"
    ) with {
      Machines = ["test:planer"],
    };
    Host host = Machine(piece: "test:other", jobs: limited);
    host.KindSet.Contribute(MachineKindSet(Lathe));
    host.KindSet.Contribute(
      MachineKindSet(Lathe with { Code = "test:planer" })
    );

    Assert.Equal(
      ["exlib:machine-step-turn (test:machine-kind-planer)"],
      host.WorkedAt("test:other")
    );
  }

  // Fails when a job that never resolved still finishes, or loads the shaft, on a tick that costs nothing.
  [Fact]
  public void An_unresolved_job_neither_cuts_nor_loads_the_shaft() {
    MachiningJob unresolved = Job();
    unresolved.RemovedKg = 0f;
    unresolved.ResolvedMaterial = null;
    Host host = Machine(jobs: unresolved);
    host.TryStart();

    Second(3);

    Assert.Equal("test:blank", Piece(host));
    Assert.Equal(0f, host.LoadTorque(5f));
  }

  // Fails when progress is reset only by a tick, so a piece swapped while the machine stands keeps the old joules.
  [Fact]
  public void A_piece_swapped_while_stopped_starts_from_nothing() {
    Host host = Machine(jobs: [Job(), Job("test:blank2")]);
    host.TryStart();
    Second(3);
    host.Stop();
    Assert.True(host.Progress > 0d);

    host.Inventory[BlockEntityMachineTool.WorkSlot].Itemstack = new ItemStack(
      _w.GetItem(new AssetLocation("test:blank2"))!
    );
    host.Inventory[BlockEntityMachineTool.WorkSlot].MarkDirty();

    Assert.Equal(0d, host.Progress);
  }

  [Fact]
  public void A_piece_no_machine_works_says_so() {
    Host host = Machine(piece: "test:other");

    Assert.Empty(host.WorkedAt("test:other"));
    Assert.Contains("machine-tool-info-nojob", host.InfoLine());
  }

  // Fails when the joules or the clock cursor are left out of the declared state.
  [Fact]
  public void Joules_and_the_cursor_survive_a_tree_round_trip() {
    Host host = Machine();
    host.TryStart();
    Second(3);
    var tree = new TreeAttribute();
    host.ToTreeAttributes(tree);

    Host reloaded = Machine(tool: true, piece: "test:blank");
    reloaded.FromTreeAttributes(tree, _w.World);

    Assert.True(reloaded.IsRunning);
    Assert.Equal(3 * 24660d / Joules, reloaded.Progress, 4);
    var cursor = new ExpandedLib.Helpers.GameClockCursor();
    cursor.Load(tree, "toolCursor");
    Assert.True(cursor.LastHours > 0d);
  }

  // Fails when the game time a reload missed is not credited, or is credited past the remainder.
  [Fact]
  public void A_reload_credits_the_hours_it_missed_clamped_to_the_remainder() {
    Host host = Machine();
    host.TryStart();
    Second(1);
    var tree = new TreeAttribute();
    host.ToTreeAttributes(tree);
    Host reloaded = Machine();
    reloaded.FromTreeAttributes(tree, _w.World);

    _w.AdvanceHours(0.001d);
    _w.FireBlockEntityTicks(1f);
    Assert.Equal(
      (24660d + 0.001d * 3600d * 822d) / Joules,
      reloaded.Progress,
      4
    );

    _w.AdvanceHours(5d);
    _w.FireBlockEntityTicks(1f);
    Assert.Equal("test:turned", Piece(reloaded));
  }

  // Fails when a tick on no run advances the cursor, which loses the gap the first live tick should credit.
  [Fact]
  public void A_tick_on_no_run_credits_nothing_and_the_next_live_tick_credits_the_gap() {
    Host host = Machine();
    host.TryStart();
    host.Drive = MpRunReading.None;

    Second(3);
    Assert.Equal(0d, host.Progress);

    host.Drive = new MpRunReading(10f, 1000f);
    Second();
    Assert.Equal(4 * 24660d / Joules, host.Progress, 4);
  }

  [Fact]
  public void A_stopped_machine_credits_nothing() {
    Host host = Machine();

    Second(2);
    host.TryStart();
    host.Stop();
    host.Stop();
    Second(2);

    Assert.Equal(0d, host.Progress);
    Assert.False(host.IsRunning);
  }

  // Fails when the wear, the byproducts or the spent drop are skipped at completion.
  [Fact]
  public void A_finished_cut_wears_the_tool_and_leaves_chips() {
    Host host = Machine(
      jobs: Job(byproducts: [new MachiningByproduct("test:chips", 2)])
    );
    host.TryStart();

    Second(8);

    Assert.Equal("test:chipsx2", Chips(host));
    ItemStack tool = host.Inventory[BlockEntityMachineTool.ToolSlot].Itemstack;
    Assert.Equal(2, _cutter.GetRemainingDurability(tool));
  }

  // Fails when the spent drop is lost instead of dropped at the block while the chips are taken by another item.
  [Fact]
  public void A_worn_out_tool_drops_its_spent_item_at_the_block_when_the_chips_hold_another() {
    Host host = Machine();
    host.Inventory[BlockEntityMachineTool.ChipsSlot].Itemstack = new ItemStack(
      _chips,
      1
    );
    host.Inventory[BlockEntityMachineTool.ToolSlot]
      .Itemstack.Attributes.SetInt("durability", 1);
    host.TryStart();

    Second(8);

    Assert.Null(host.Inventory[BlockEntityMachineTool.ToolSlot].Itemstack);
    Assert.Equal(
      "test:spent",
      _w.Spawns.Single().Stack.Collectible.Code.ToString()
    );
  }

  // Fails when a full chips slot does not hold the cut back.
  [Fact]
  public void Byproducts_that_do_not_fit_the_chips_stop_the_cut() {
    Host host = Machine(
      jobs: Job(byproducts: [new MachiningByproduct("test:chips", 3)])
    );
    host.Inventory[BlockEntityMachineTool.ChipsSlot].Itemstack = new ItemStack(
      _chips,
      7
    );
    host.TryStart();

    Second(2);

    Assert.Equal(MachineCutVerdict.ChipsFull, host.Verdict);
    Assert.Equal(0d, host.Progress);
    Assert.Contains("machine-tool-info-chips", host.InfoLine());
  }

  [Fact]
  public void Byproducts_of_two_items_cannot_share_one_chips_slot() {
    Host host = Machine(
      jobs: Job(
        byproducts:
        [
          new MachiningByproduct("test:chips", 1),
          new MachiningByproduct("test:spent", 1),
        ]
      )
    );
    host.TryStart();

    Second();

    Assert.Equal(MachineCutVerdict.ChipsFull, host.Verdict);
  }

  [Fact]
  public void Chips_of_another_item_hold_the_cut_back_even_when_there_is_room() {
    Host host = Machine(
      jobs: Job(byproducts: [new MachiningByproduct("test:chips", 1)])
    );
    host.Inventory[BlockEntityMachineTool.ChipsSlot].Itemstack = new ItemStack(
      _w.GetItem(new AssetLocation("test:spent"))!,
      1
    );
    host.TryStart();

    Second();

    Assert.Equal(MachineCutVerdict.ChipsFull, host.Verdict);
  }

  [Fact]
  public void A_stopped_drive_names_no_drive_and_credits_nothing() {
    Host host = Machine();
    host.Drive = new MpRunReading(0f, 1000f);
    host.TryStart();

    Second(2);

    Assert.Equal(MachineCutVerdict.NoDrive, host.Verdict);
    Assert.Equal(0d, host.Progress);
    Assert.Contains("machine-tool-info-nodrive", host.InfoLine());
  }

  [Fact]
  public void The_tool_verdicts_reach_the_block_info() {
    Host noTool = Machine(tool: false);
    Assert.Contains("machine-tool-info-notool", noTool.InfoLine());

    Host tier = Machine(jobs: Job(minTier: 3));
    Assert.Contains("machine-tool-info-tier", tier.InfoLine());

    Host wrong = Machine();
    Item bit = _w.RegisterItem("test:bit");
    bit.Attributes = new JsonObject(
      JObject.FromObject(MachineTool.Tier(2, "bit"))
    );
    wrong.Inventory[BlockEntityMachineTool.ToolSlot].Itemstack = new ItemStack(
      bit
    );
    Assert.Contains("machine-tool-info-wrongtool", wrong.InfoLine());

    Host stopped = Machine();
    Assert.Contains("machine-tool-info-stopped", stopped.InfoLine());

    Host cutting = Machine();
    cutting.TryStart();
    Second();
    Assert.Contains("machine-tool-info-cutting", cutting.InfoLine());

    Assert.Null(Machine(piece: "").InfoLine());
  }

  [Fact]
  public void A_job_with_a_fixture_cuts_only_with_it_in_place() {
    Host host = Machine(FixtureLathe, jobs: Job(fixture: "test:fixture"));
    host.TryStart();

    Second();
    Assert.Equal(MachineCutVerdict.NoFixture, host.Verdict);
    Assert.Equal(0d, host.Progress);
    Assert.Contains("machine-tool-info-nofixture", host.InfoLine());

    host.Inventory[BlockEntityMachineTool.FixtureSlot].Itemstack =
      new ItemStack(_w.GetItem(new AssetLocation("test:fixture"))!);
    Second();
    Assert.Equal(MachineCutVerdict.Ready, host.Verdict);
    Assert.True(host.Progress > 0d);
  }

  [Fact]
  public void The_load_is_the_cut_torque_while_cutting_and_the_standing_torque_only_while_engaged() {
    Host host = Machine();
    Assert.Equal(0f, host.LoadTorque(5f));
    Assert.Equal(0f, host.StandingNm);

    host.TryStart();

    Assert.Equal(82.2f, host.LoadTorque(0f), 3);
    Assert.Equal(5f, host.StandingNm);
  }

  [Fact]
  public void A_block_with_no_declared_kind_does_not_start() {
    Host host = Machine();
    host.KindIs = null;

    Assert.False(host.TryStart());
    Assert.Equal(0f, host.LoadTorque(5f));
    Assert.Equal(0f, host.StandingNm);
    Assert.Null(host.InfoLine());
    Assert.Equal(0d, host.Progress);
  }

  [Fact]
  public void A_machine_already_engaged_does_not_start_again() {
    Host host = Machine();

    Assert.True(host.TryStart());
    Assert.False(host.TryStart());
  }

  [Fact]
  public void An_output_that_names_nothing_leaves_the_piece_and_logs() {
    _w.Log.Expect(EnumLogType.Warning, "names no item or block");
    Host host = Machine(jobs: Job(output: "test:missing"));
    host.TryStart();

    Second(8);

    Assert.Equal("test:blank", Piece(host));
    Assert.Equal(0d, host.Progress);
  }

  // Fails when the tool slot takes an item that declares no tool kind, or the work slot takes a stack.
  [Fact]
  public void The_slots_take_what_they_are_for() {
    Host host = Machine();
    var plain = new DummySlot(
      new ItemStack(_w.GetItem(new AssetLocation("test:other"))!)
    );
    var kinded = new DummySlot(new ItemStack(_cutter));

    Assert.False(
      host.Inventory[BlockEntityMachineTool.ToolSlot].CanHold(plain)
    );
    Assert.True(
      host.Inventory[BlockEntityMachineTool.ToolSlot].CanHold(kinded)
    );
    Assert.Equal(
      1,
      host.Inventory[BlockEntityMachineTool.WorkSlot].MaxSlotStackSize
    );
    Assert.Equal(3, host.Inventory.Count);
    Assert.Equal(4, Machine(FixtureLathe).Inventory.Count);
  }

  // Fails when the host reads the drive at a constant instead of the run: the cut would neither slow nor stall.
  [Fact]
  public void On_a_real_run_the_cut_slows_the_run_and_stalls_past_the_drives_torque() {
    var host = new DrivenHost { OnRun = true };
    host.JobSet.Contribute(MachiningTestData.Set(Job()));
    var at = new BlockPos(0, 0, 0);
    _w.Place(at, TestNetworkBlock.Create("mpshaft", "we", 802), host);
    _w.Initialize(host);
    host.Join(_w.Networks);
    host.Inventory[BlockEntityMachineTool.ToolSlot].Itemstack = new ItemStack(
      _cutter
    );
    host.Inventory[BlockEntityMachineTool.WorkSlot].Itemstack = new ItemStack(
      _w.GetItem(new AssetLocation("test:blank"))!
    );
    var run = (MpShaftNetwork)_w.NetworkAt(at)!;

    Turn(run, 300);
    float free = MpRun.Read(_w.Networks, NodeKey.Centre(at)).SpeedRadS;
    host.TryStart();
    Turn(run, 300);
    float cutting = MpRun.Read(_w.Networks, NodeKey.Centre(at)).SpeedRadS;

    Assert.True(free > 15f, $"free={free}");
    Assert.True(cutting < free - 1f);
    Assert.True(cutting > 0f);

    Second();
    Assert.Equal(82.2d * cutting * 30d / Joules, host.Progress, 4);

    host.KindIs = Lathe with {
      Steps = [new("turn", CutMotion.Rotary, 40000f, "cutter", null, null)],
    };
    Turn(run, 300);
    Assert.Equal(0f, MpRun.Read(_w.Networks, NodeKey.Centre(at)).SpeedRadS, 3);
  }

  private static MachineKindSet MachineKindSet(MachineKind kind) =>
    new(1, [kind]);
}
