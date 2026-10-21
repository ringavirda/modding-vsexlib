using System.Linq;
using ExpandedLib.FastForwarding;
using ExpandedLib.Industry.Pipes;
using ExpandedLib.Networks;
using ExpandedLib.Testing;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>How members and networks form fast-forward groups, what a lookup in a member's tick records, and what raises the epoch.</summary>
public class FastForwardGroupTests {
  #region Fixtures

  private static TestWorld NewWorld() {
    var w = new TestWorld();
    w.RegisterNetwork("pipeflow", sys => new PipeFlowNetwork(sys));
    return w;
  }

  private static BlockPos Cell(int x) => new(x, 0, 0);

  private static BlockPos Member(int x) => new(x, 5, 0);

  private static void Lay(TestWorld w, params int[] xs) {
    foreach (int x in xs)
      w.PlaceNode(Cell(x), "pipeflow", "we");
  }

  private static FastForwardDriver Driver(TestWorld w) =>
    w.Networks.FastForward;

  #endregion

  #region Groups

  // Fails when network vertices are left out of the union, so members on one run stay apart.
  [Fact]
  public void Members_on_one_network_form_one_group() {
    var w = NewWorld();
    Lay(w, 0, 1, 10);
    Driver(w).Register(Member(0), null, [Cell(0)]);
    Driver(w).Register(Member(1), null, [Cell(1)]);
    Driver(w).Register(Member(10), null, [Cell(10)]);

    w.Tick();

    Assert.Equal(2, Driver(w).Groups.Count);
    Assert.Equal(
      [2, 1],
      Driver(w).Groups.Select(g => g.Members.Count).OrderDescending()
    );
  }

  // Fails when the record call is removed from GetNetworkAt, so a lookup in a member's tick leaves no coupling.
  [Fact]
  public void A_lookup_inside_a_tick_records_a_coupling() {
    var w = NewWorld();
    Lay(w, 0);
    FastForwardMember member = Driver(w).Register(Member(0));

    using (FastForward.Step(Driver(w), member.Pos))
      w.Networks.GetNetworkAt(Cell(0));

    Assert.Contains(Cell(0), member.Couplings);
    w.Networks.GetNetworkAt(Cell(0));
    Assert.Single(member.Couplings);
  }

  // Fails when groups are built for every network, so a run holding no member is a group.
  [Fact]
  public void A_run_without_a_member_is_no_group() {
    var w = NewWorld();
    Lay(w, 0, 1, 2);

    w.Tick();

    Assert.Empty(Driver(w).Groups);
  }

  // Fails when a rebuilt group takes the greatest member cursor instead of the least.
  [Fact]
  public void A_rebuilt_group_takes_the_least_cursor() {
    var w = NewWorld();
    Lay(w, 0, 1);
    w.AdvanceHours(10);
    Driver(w).Register(Member(0), 3.0, [Cell(0)]);
    Driver(w).Register(Member(1), 5.0, [Cell(1)]);

    w.Networks.ListenerTick(w.Accessor, 0.2f);

    FastForwardGroup group = Assert.Single(Driver(w).Groups);
    Assert.Equal(3.0 + 0.2 / 120.0, group.CursorHours, 9);
  }

  #endregion

  #region Epoch

  // Fails when a fracture does not raise the epoch, so the groups keep a run that no longer exists.
  [Fact]
  public void A_split_raises_the_epoch() {
    var w = NewWorld();
    Lay(w, 0, 1, 2);
    int before = Driver(w).Epoch;

    w.RemoveNode(Cell(1));

    Assert.True(Driver(w).Epoch > before);
  }

  #endregion
}
