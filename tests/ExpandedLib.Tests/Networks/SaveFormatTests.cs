using System.Linq;
using ExpandedLib.Industry.Pipes;
using ExpandedLib.Networks;
using ExpandedLib.Testing;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// The on-disk format of a network node, against the shape a world saved before membership became a
/// behaviour: the exact key set, which keys are conditional, and which of the two writers that reach
/// the shared keys runs last.
/// </summary>
public class SaveFormatTests {
  /// <summary>Reaches the pipe's protected persistence hook and the display fields it writes over the
  /// network state.</summary>
  private sealed class DivergentPipe : BlockEntityPipe {
    public void SetDisplay(float temperature, string medium, float pressure) {
      Temperature = temperature;
      Medium = medium;
      Pressure = pressure;
    }

    public void SetSavedState(object? state) => _savedNetworkState = state;

    public void WriteNetworkState(ITreeAttribute tree, object? state) =>
      SerializeNetworkState(tree, state);
  }

  /// <summary>Every key a pre-membership world wrote for a pipe holding a live run: the four vanilla
  /// <c>BlockEntity</c> keys, the three <c>BlockEntityNetworkNode</c> ones, and the eight the pipe's
  /// network state serialises to.</summary>
  private static readonly string[] LoadedRunKeys =
  [
    "posx",
    "posy",
    "posz",
    "blockCode",
    "networkType",
    "orientation",
    "possibleOrientations",
    "vol",
    "max",
    "temp",
    "medium",
    "openings",
    "flow",
    "pressure",
    "feedPressure",
  ];

  /// <summary>The keys <c>IsNetworkStateMeaningful</c> gates: absent for a run holding nothing.</summary>
  private static readonly string[] ConditionalKeys =
  [
    "vol",
    "max",
    "openings",
    "flow",
    "feedPressure",
  ];

  /// <summary>The three keys both writers reach.</summary>
  private static readonly string[] SharedKeys = ["temp", "medium", "pressure"];

  private static readonly PipeNetworkState SavedRun = new() {
    Volume = 123f,
    MaxVolume = 300f,
    Temperature = 88f,
    MediumType = "Steam",
    OpeningsCount = 2,
    FlowRate = 4.5f,
    Pressure = 1.7f,
    FeedPressure = 2.3f,
  };

  private static TestWorld NewPipeWorld() {
    var w = new TestWorld();
    w.RegisterNetwork("pipe", sys => new StubNetwork(sys, "pipe"));
    return w;
  }

  /// <summary>The tree a pre-membership world holds for a pipe carrying a run, written key by key.</summary>
  private static TreeAttribute OldFormatTree(BlockPos pos) {
    var tree = new TreeAttribute();
    tree.SetInt("posx", pos.X);
    tree.SetInt("posy", pos.InternalY);
    tree.SetInt("posz", pos.Z);
    tree.SetString("blockCode", "test:pipe-ns-950");

    tree.SetString("networkType", "pipe");
    tree.SetString("orientation", "ns");
    tree.SetString("possibleOrientations", "[\"ns\",\"we\"]");

    tree.SetFloat("vol", SavedRun.Volume);
    tree.SetFloat("max", SavedRun.MaxVolume);
    tree.SetFloat("temp", SavedRun.Temperature);
    tree.SetString("medium", SavedRun.MediumType);
    tree.SetInt("openings", SavedRun.OpeningsCount);
    tree.SetFloat("flow", SavedRun.FlowRate);
    tree.SetFloat("pressure", SavedRun.Pressure);
    tree.SetFloat("feedPressure", SavedRun.FeedPressure);
    return tree;
  }

  private static string[] KeysOf(ITreeAttribute tree) =>
    tree.Select(kv => kv.Key).OrderBy(k => k).ToArray();

  private static string[] Sorted(params string[][] sets) =>
    sets.SelectMany(s => s).OrderBy(k => k).ToArray();

  #region Loading a pre-membership world

  [Fact]
  public void A_tree_written_before_membership_moved_still_loads_the_node_and_its_run() {
    var w = NewPipeWorld();
    var pos = new BlockPos(0, 0, 0);
    var be = new BlockEntityPipe();
    w.Place(pos, TestNetworkBlock.Create("pipe", "ns", id: 950), be);

    be.FromTreeAttributes(OldFormatTree(pos), w.World);
    w.Initialize(be);

    // The membership answers for the cell.
    Assert.IsAssignableFrom<BEBehaviorNetworkMember>(
      NetworkMembership.Resolve(w.Accessor, pos, "pipe")
    );

    // The run is back on the network, not merely on the block entity.
    BlockNetwork net = Assert.IsType<StubNetwork>(w.NetworkAt(pos));
    var restored = Assert.IsType<PipeNetworkState>(net.State);
    Assert.Equal(SavedRun.Volume, restored.Volume, 3);
    Assert.Equal(SavedRun.MediumType, restored.MediumType);
    Assert.Equal(SavedRun.FeedPressure, restored.FeedPressure, 3);
  }

  [Fact]
  public void The_wrench_rotation_choices_come_back_from_the_json_string_they_were_saved_as() {
    // Worlds saved before the string-array cutover hold possibleOrientations as one JSON string.
    var w = NewPipeWorld();
    var pos = new BlockPos(0, 0, 0);
    var be = new BlockEntityPipe();
    w.Place(pos, TestNetworkBlock.Create("pipe", "ns", id: 951), be);

    be.FromTreeAttributes(OldFormatTree(pos), w.World);

    Assert.Equal(new[] { "ns", "we" }, be.PossibleOrientations);
    Assert.Equal("ns", be.Orientation);
  }

  [Fact]
  public void A_node_loaded_from_the_old_string_is_written_back_as_a_string_array() {
    // The migration is one-way and silent; nothing rewrites a world wholesale.
    var w = NewPipeWorld();
    var pos = new BlockPos(0, 0, 0);
    var be = new BlockEntityPipe();
    w.Place(pos, TestNetworkBlock.Create("pipe", "ns", id: 955), be);
    be.FromTreeAttributes(OldFormatTree(pos), w.World);

    var written = new TreeAttribute();
    be.ToTreeAttributes(written);

    Assert.IsType<StringArrayAttribute>(written["possibleOrientations"]);
    Assert.Equal(
      new[] { "ns", "we" },
      ((StringArrayAttribute)written["possibleOrientations"]).value
    );
  }

  [Fact]
  public void The_rotation_choices_round_trip_through_the_new_shape() {
    var w = NewPipeWorld();
    var pos = new BlockPos(0, 0, 0);
    var be = new BlockEntityPipe();
    w.Place(pos, TestNetworkBlock.Create("pipe", "ns", id: 956), be);
    be.PossibleOrientations = ["ns", "we", "ud"];

    var written = new TreeAttribute();
    be.ToTreeAttributes(written);

    var reloaded = new BlockEntityPipe();
    w.Place(
      new BlockPos(1, 0, 0),
      TestNetworkBlock.Create("pipe", "ns", id: 957),
      reloaded
    );
    reloaded.FromTreeAttributes(written, w.World);

    Assert.Equal(new[] { "ns", "we", "ud" }, reloaded.PossibleOrientations);
  }

  #endregion

  #region The key set

  [Fact]
  public void A_loaded_node_writes_back_exactly_the_keys_it_read() {
    var w = NewPipeWorld();
    var pos = new BlockPos(0, 0, 0);
    var be = new BlockEntityPipe();
    w.Place(pos, TestNetworkBlock.Create("pipe", "ns", id: 952), be);
    be.FromTreeAttributes(OldFormatTree(pos), w.World);
    w.Initialize(be);

    Assert.NotEmpty(NetworkMembership.MembersOf(be));

    var written = new TreeAttribute();
    be.ToTreeAttributes(written);

    Assert.Equal(Sorted(LoadedRunKeys), KeysOf(written));
  }

  [Fact]
  public void An_empty_run_writes_neither_more_nor_fewer_of_the_conditional_keys() {
    // IsNetworkStateMeaningful gates on Volume > 0 || FlowRate > 0; temp, medium and pressure survive
    // as the pipe's own display writes.
    var w = NewPipeWorld();
    var pos = new BlockPos(0, 0, 0);
    var be = new BlockEntityPipe();
    w.Place(pos, TestNetworkBlock.Create("pipe", "ns", id: 953), be);
    w.Initialize(be);

    be.OnNetworkUpdate(new PipeNetworkState { Volume = 0f, FlowRate = 0f });

    var written = new TreeAttribute();
    be.ToTreeAttributes(written);

    Assert.Equal(
      Sorted(LoadedRunKeys.Except(ConditionalKeys).ToArray()),
      KeysOf(written)
    );
  }

  #endregion

  #region The write order

  [Fact]
  public void The_network_state_writes_the_three_keys_the_pipe_writes_again() {
    var be = new DivergentPipe();
    var fromState = new TreeAttribute();

    be.WriteNetworkState(fromState, SavedRun);

    foreach (string key in SharedKeys)
      Assert.Contains(key, KeysOf(fromState));
  }

  [Fact]
  public void The_pipes_display_fields_are_written_after_the_network_state() {
    // BlockEntityPipe.ToTreeAttributes rewrites temp, medium and pressure after base runs
    // SerializeNetworkState.
    var w = NewPipeWorld();
    var pos = new BlockPos(0, 0, 0);
    var be = new DivergentPipe();
    w.Place(pos, TestNetworkBlock.Create("pipe", "ns", id: 954), be);
    be.SetSavedState(SavedRun);
    be.SetDisplay(11f, "Water", 9f);

    var written = new TreeAttribute();
    be.ToTreeAttributes(written);

    Assert.Equal(11f, written.GetFloat("temp"), 3);
    Assert.Equal("Water", written.GetString("medium"));
    Assert.Equal(9f, written.GetFloat("pressure"), 3);

    // The state's own five keys are untouched by the second writer.
    Assert.Equal(SavedRun.Volume, written.GetFloat("vol"), 3);
    Assert.Equal(SavedRun.FeedPressure, written.GetFloat("feedPressure"), 3);
  }

  #endregion
  #region The pipe flow node entry

  private static NodeKey Cell(int x) => NodeKey.Centre(new BlockPos(x, 0, 0));

  private static PipeFlowNetwork RunOf(TestWorld w, int nodes) {
    var net = new PipeFlowNetwork(w.Networks);
    for (int i = 0; i < nodes; i++)
      net.Store.AddNode(Cell(i), 0.02, 0, 0.001 * (i + 1), 20 + i);
    for (int i = 1; i < nodes; i++)
      net.Store.AddEdge(i - 1, i, 0.05, 1, FlowPipeClass.Cast);
    return net;
  }

  // Fails when Write drops the damage byte.
  [Fact]
  public void A_pipe_node_entry_round_trips_every_field_under_pf_and_the_slot() {
    var host = new TreeAttribute();
    var saved = new PipeNodeSave(0.5, 0.25, 3, 120, 90, 0.75, 2);

    PipeFlowSave.Write(host, 3, saved);

    Assert.Equal("pf3", PipeFlowSave.Key(3));
    Assert.NotNull(host.GetTreeAttribute("pf3"));
    Assert.True(PipeFlowSave.TryRead(host, 3, out var read));
    Assert.Equal(saved, read);
    Assert.False(PipeFlowSave.TryRead(host, 4, out _));
  }

  // Fails when TryRead accepts any format version.
  [Fact]
  public void An_entry_of_another_format_version_restores_nothing() {
    var w = new TestWorld();
    var net = RunOf(w, 1);
    var host = new TreeAttribute();
    net.SaveNode(Cell(0), host);
    host.GetTreeAttribute("pf0")!.SetInt("v", PipeFlowSave.FormatVersion + 1);
    var fresh = new PipeFlowNetwork(w.Networks);

    Assert.False(fresh.RestoreNode(Cell(0), host, 0.02, 0));
    Assert.Equal(0, fresh.Store.NodeCount);
  }

  // Fails when RestoreNode adds the node with the steam gas constant whatever the run's gas, so an air node saved at 1 atm reloads at 1.61 atm.
  [Fact]
  public void Reload_KeepsTheGasConstantOfTheRun() {
    var w = new TestWorld();
    var live = new PipeFlowNetwork(w.Networks);
    live.Store.AddNode(
      Cell(0),
      0.02,
      0,
      101325 * 0.02 / (287 * 293.15),
      20,
      287
    );
    var host = new TreeAttribute();
    live.SaveNode(Cell(0), host);

    var rebuilt = new PipeFlowNetwork(w.Networks);
    Assert.True(rebuilt.RestoreNode(Cell(0), host, 0.02, 0, 287));

    Assert.Equal(
      live.Store.PressurePa(0),
      rebuilt.Store.PressurePa(rebuilt.Store.IndexOf(Cell(0))),
      3
    );
  }

  // Fails when SaveNode writes no damage, or the network drops the saved damage of a restored node, so a reloaded weeping joint is whole.
  [Fact]
  public void Reload_KeepsAWeepingJoint() {
    var w = new TestWorld();
    var live = new PipeFlowNetwork(w.Networks);
    var rating = new FlowBore(
      0.25,
      5,
      FlowBore.BurstFor(5),
      4,
      0.03,
      BlockPipe.FlangedJoint
    );
    const double gas = 9 * 101325 * 0.1 / (461.5 * 425.15);
    for (int i = 0; i < 3; i++)
      live.Store.AddNode(Cell(i), 0.1, 0, i < 2 ? gas : 0.0001, 152);
    int weeping = live.Store.AddEdge(0, 1, 0.25, 1, FlowPipeClass.Cast);
    int quiet = live.Store.AddEdge(1, 2, 0.25, 1, FlowPipeClass.Cast);
    live.Store.SetRating(weeping, rating);
    for (int i = 0; i < 10; i++)
      live.Store.Step(0.05);
    Assert.True(live.Store.IsWeeping(weeping));
    Assert.False(live.Store.IsWeeping(quiet));
    var hosts = new TreeAttribute[3];
    for (int i = 0; i < 3; i++) {
      hosts[i] = new TreeAttribute();
      live.SaveNode(Cell(i), hosts[i]);
    }

    var rebuilt = new PipeFlowNetwork(w.Networks);
    for (int i = 0; i < 3; i++)
      Assert.True(rebuilt.RestoreNode(Cell(i), hosts[i], 0.1, 0));
    int rWeeping = rebuilt.Store.AddEdge(0, 1, 0.25, 1, FlowPipeClass.Cast);
    int rQuiet = rebuilt.Store.AddEdge(1, 2, 0.25, 1, FlowPipeClass.Cast);
    rebuilt.OnTick(w.Accessor, 0.05f, w.Networks);

    Assert.True(rebuilt.Store.IsWeeping(rWeeping));
    Assert.False(rebuilt.Store.IsWeeping(rQuiet));
  }

  // Fails when SaveNode writes something other than the live store (a zeroed gas stands for the threshold-gated display copy), or when RestoreNode overwrites a node the store already holds.
  [Fact]
  public void Reload_RestoresEachNodeOnce() {
    var w = new TestWorld();
    var live = RunOf(w, 4);
    live.Store.SetSource(0, 0.0005);
    live.Store.Step(0.2);
    live.Store.Publish();
    live.Store.Step(0.2);
    live.Store.Step(0.2);
    Assert.NotEqual(
      live.Store.GasKg(1),
      live.Store.PublishedPressurePa(1) * 0.02 / (461.5 * 293.15)
    );
    var hosts = new TreeAttribute[4];
    for (int i = 0; i < 4; i++) {
      hosts[i] = new TreeAttribute();
      live.SaveNode(Cell(i), hosts[i]);
    }

    var rebuilt = new PipeFlowNetwork(w.Networks);
    for (int i = 0; i < 4; i++) {
      Assert.True(rebuilt.RestoreNode(Cell(i), hosts[i], 0.02, 0));
      Assert.False(rebuilt.RestoreNode(Cell(i), hosts[i], 0.02, 0));
    }

    Assert.Equal(4, rebuilt.Store.NodeCount);
    for (int i = 0; i < 4; i++) {
      int node = rebuilt.Store.IndexOf(Cell(i));
      Assert.Equal(live.Store.GasKg(i), rebuilt.Store.GasKg(node));
      Assert.Equal(
        live.Store.TemperatureC(i),
        rebuilt.Store.TemperatureC(node)
      );
    }

    live.Store.Step(0.2);
    double now = live.Store.GasKg(2);
    Assert.False(live.RestoreNode(Cell(2), hosts[2], 0.02, 0));
    Assert.Equal(now, live.Store.GasKg(2));
  }

  #endregion
}
