using ExpandedLib.Helpers;
using ExpandedLib.Industry.Benches;
using ExpandedLib.Industry.MechanicalPower;
using ExpandedLib.Machines;
using ExpandedLib.Networks;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests.Benches;

/// <summary>The mp bench base: its stroke clock against the run, the torque unit a subclass scales, and
/// the persisted stroke with its legacy key.</summary>
public class MpBenchTests {
  private sealed class TestBench : BlockEntityMpBench {
    public int Completed;
    public float Unit = 1000f;

    protected override MachineSlotSpec[] SlotSpecs => [];

    public override string InventoryClassName => "test-mpbench";

    protected override float JobTorqueNm => Unit;

    public override float StandingNm => 0f;

    public override float LoadTorque(float speed) => 0f;

    protected override void CompleteStroke() => Completed++;

    public void Begin(float seconds) => BeginStroke(seconds);

    public float Torque => AvailableTorque;

    public float Rate => Speed;

    public ItemStack? Make(string code, int quantity) =>
      Resolve(code, quantity);

    public void Throw(ItemStack? stack) => Eject(stack);
  }

  private static (TestWorld World, TestBench Bench, BlockPos Pos) Bench() {
    var world = new TestWorld();
    world.RegisterNetwork("mpshaft", n => new MpShaftNetwork(n));
    var pos = new BlockPos(0, 1, 0);
    var bench = new TestBench();
    world.Place(pos, TestBlocks.Configure(new Block(), "test:bench", 1), bench);
    world.Initialize(bench);
    return (world, bench, pos);
  }

  private static void Turn(
    TestWorld world,
    BlockPos pos,
    float speed,
    float driveNm
  ) {
    var net = (MpShaftNetwork)world.NetworkAt(pos)!;
    var store = new MpShaftStore();
    int node = store.AddNode(
      NodeKey.Centre(pos),
      store.AddGroup(speed),
      10f,
      0f
    );
    store.SetDrive(node, driveNm);
    ReflectionHelpers.SetProperty(net, "Store", store);
  }

  // Fails when the Speed <= 0 hold is dropped from AdvanceStroke: a stopped run then finishes the stroke.
  [Fact]
  public void A_stroke_completes_only_while_the_run_turns() {
    var (world, bench, pos) = Bench();
    bench.Begin(1f);

    Turn(world, pos, speed: 0f, driveNm: 0f);
    Assert.False(bench.AdvanceStroke(2f));
    Assert.True(bench.IsStroking);
    Assert.Equal(0, bench.Completed);

    Turn(world, pos, speed: 1f, driveNm: 0f);
    Assert.False(bench.AdvanceStroke(0.5f));
    Assert.Equal(0.5f, bench.Remaining, 3);
    Assert.True(bench.AdvanceStroke(0.5f));
    Assert.False(bench.IsStroking);
    Assert.Equal(1, bench.Completed);
  }

  // Fails when the run's sense changes the bench's speed: a reversed run reads as negative and holds the stroke.
  [Fact]
  public void A_run_turning_backwards_drives_the_stroke_too() {
    var (world, bench, pos) = Bench();
    Turn(world, pos, speed: -2f, driveNm: 0f);

    Assert.Equal(2f, bench.Rate, 3);
  }

  // Fails when the constant 1000 replaces the JobTorqueNm property in AvailableTorque.
  [Fact]
  public void Available_torque_is_counted_in_the_overridden_job_unit() {
    var (world, bench, pos) = Bench();
    Turn(world, pos, speed: 1f, driveNm: 4000f);

    bench.Unit = 1000f;
    Assert.Equal(4f, bench.Torque, 3);
    bench.Unit = 500f;
    Assert.Equal(8f, bench.Torque, 3);
  }

  [Fact]
  public void Available_torque_is_zero_on_a_stopped_run() {
    var (world, bench, pos) = Bench();
    Turn(world, pos, speed: 0f, driveNm: 4000f);

    Assert.Equal(0f, bench.Torque);
  }

  // Fails when the persisted key is renamed or dropped from the stroke clock.
  [Fact]
  public void The_stroke_survives_a_tree_round_trip() {
    var (_, bench, _) = Bench();
    bench.Begin(3f);
    var tree = new TreeAttribute();
    bench.ToTreeAttributes(tree);

    Assert.Equal(3f, tree.GetFloat("benchRemaining"));
    var (_, other, _) = Bench();
    other.FromTreeAttributes(tree, other.Api.World);
    Assert.Equal(3f, other.Remaining);
  }

  // Fails when the legacy shearRemaining key is dropped from the persisted member.
  [Fact]
  public void A_legacy_shear_save_loads_its_stroke() {
    var (_, bench, _) = Bench();
    var tree = new TreeAttribute();
    tree.SetFloat("shearRemaining", 2.5f);

    bench.FromTreeAttributes(tree, bench.Api.World);

    Assert.Equal(2.5f, bench.Remaining);
    Assert.True(bench.IsStroking);
  }

  [Fact]
  public void A_job_output_naming_nothing_resolves_to_no_stack() {
    var (world, bench, _) = Bench();
    world.RegisterItem("test:plate");
    world.Log.Expect(EnumLogType.Warning, "test:missing");

    Assert.Null(bench.Make("test:missing", 1));
    Assert.Equal(3, bench.Make("test:plate", 3)!.StackSize);
  }
}
