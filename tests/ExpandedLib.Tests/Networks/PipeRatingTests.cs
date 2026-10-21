using ExpandedLib.Industry.Pipes;
using ExpandedLib.Networks;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// Covers the ratings of the physical pipe network: the bore registry, the weep of the most loaded piece, its
/// wrench and replacement, and the burst of a piece held above its burst pressure, in <see cref="PipeFlowStore"/>.
/// </summary>
public class PipeRatingTests {
  #region Fixtures

  private const double Atmosphere = 101325;
  private const double SteamConstant = 461.5;
  private const double SteamC = 152;

  private static readonly FlowBore Main = new(
    0.25,
    5,
    FlowBore.BurstFor(5),
    4,
    0.03,
    BlockPipe.FlangedJoint
  );

  private static readonly FlowBore Small = new(
    0.076,
    8,
    FlowBore.BurstFor(8),
    3,
    0.03,
    BlockPipe.FlangedJoint
  );

  private static NodeKey Cell(int index) =>
    NodeKey.Centre(new BlockPos(index, 0, 0));

  private static int Chamber(PipeFlowStore store, int index, double atm) =>
    store.AddNode(
      Cell(index),
      0.1,
      0,
      atm * Atmosphere * 0.1 / (SteamConstant * (SteamC + 273.15)),
      SteamC,
      SteamConstant
    );

  private static void Run(PipeFlowStore store, double seconds) {
    for (double t = 0; t < seconds; t += 0.05)
      store.Step(0.05);
  }

  #endregion

  #region Registry

  // Fails when Register drops the class or keeps the earlier row on a second registration.
  [Fact]
  public void Register_ReplacesTheRowOfATierAndClass() {
    FlowBores.Clear();
    FlowBores.Register("t", "main", 0.25, 5, 6, 4, 0.03, "flanged");
    FlowBores.Register("t", "main", 0.25, 6, 7, 4, 0.03, "flanged");

    Assert.True(FlowBores.TryGet("t", "main", out var bore));
    Assert.Equal(6, bore.WorkingAtm);
    Assert.False(FlowBores.TryGet("t", "small", out _));
    Assert.Equal(1 + 1.25 * 1.5, FlowBore.BurstFor(2.5), 9);
  }

  #endregion

  #region Weep

  // Fails when every piece over its working pressure weeps, not only the most loaded, so the small piece weeps beside the main.
  [Fact]
  public void Weep_OnlyTheHighestWeeps() {
    var store = new PipeFlowStore();
    int a = Chamber(store, 0, 9);
    int b = Chamber(store, 1, 9);
    int c = Chamber(store, 2, 9);
    int main = store.AddEdge(a, b, 0.25, 1, FlowPipeClass.Cast);
    int small = store.AddEdge(b, c, 0.076, 1, FlowPipeClass.Cast);
    store.SetRating(main, Main);
    store.SetRating(small, Small);

    Run(store, 1);

    Assert.True(store.Load(small) > 1);
    Assert.True(store.IsWeeping(main));
    Assert.False(store.IsWeeping(small));
    Assert.False(store.IsBurst(main));
  }

  // Fails when a tie goes to the first edge added, so the same two pieces added the other way round weep at another place.
  [Fact]
  public void Weep_TieGoesByPosition() {
    bool EndWeeps(bool reversed) {
      var store = new PipeFlowStore();
      int a = Chamber(store, 0, 7);
      int b = Chamber(store, 1, 7);
      int c = Chamber(store, 2, 7);
      int end;
      int middle;
      if (reversed) {
        middle = store.AddEdge(b, c, 0.25, 1, FlowPipeClass.Cast);
        end = store.AddEdge(a, b, 0.25, 1, FlowPipeClass.Cast);
      } else {
        end = store.AddEdge(a, b, 0.25, 1, FlowPipeClass.Cast);
        middle = store.AddEdge(b, c, 0.25, 1, FlowPipeClass.Cast);
      }
      store.SetRating(end, Main);
      store.SetRating(middle, Main);
      store.Step(0.05);
      Assert.NotEqual(store.IsWeeping(end), store.IsWeeping(middle));
      return store.IsWeeping(end);
    }

    Assert.Equal(EndWeeps(false), EndWeeps(true));
  }

  // Fails when a wrench does not close an over-pressure weep, or a replacement is accepted for a sound piece.
  [Fact]
  public void Weep_WrenchRemakesAJoint() {
    var store = new PipeFlowStore();
    int a = Chamber(store, 0, 9);
    int b = Chamber(store, 1, 9);
    int edge = store.AddEdge(a, b, 0.25, 1, FlowPipeClass.Cast);
    store.SetRating(edge, Main);
    Run(store, 0.5);
    Assert.True(store.IsWeeping(edge));

    Assert.True(store.Remake(edge));
    Assert.False(store.IsWeeping(edge));
    Assert.False(store.Remake(edge));
    Assert.False(store.Replace(edge));
  }

  // Fails when a wrench closes a water hammer crack, or a replacement leaves it open.
  [Fact]
  public void Weep_CrackNeedsAReplacement() {
    var store = new PipeFlowStore();
    int header = store.AddNode(
      Cell(0),
      1,
      0,
      3 * Atmosphere / (SteamConstant * 425.15),
      152,
      SteamConstant
    );
    int pot = store.AddNode(
      Cell(1),
      0.1,
      0,
      0.0005,
      100,
      SteamConstant,
      0.3 * 0.1 * 1000,
      0,
      true
    );
    store.SetWall(pot, new PipeWall(50000, 0.6, 0), 20);
    int edge = store.AddEdge(header, pot, 0.05, 1, FlowPipeClass.Cast);
    store.AddOpening(pot, 1e-3, Atmosphere, FlowGas.Steam);
    Run(store, 2.5);
    Assert.True(store.IsCracked(edge));
    Assert.True(store.IsWeeping(edge));

    Assert.False(store.Remake(edge));
    Assert.True(store.IsWeeping(edge));
    Assert.True(store.Replace(edge));
    Assert.False(store.IsCracked(edge));
    Assert.False(store.IsWeeping(edge));
  }

  #endregion

  #region Burst

  // Fails when the piece to burst is not the highest loaded one, or the burst comes before 3 s above its burst pressure.
  [Fact]
  public void Burst_WeakestAtHighestPressure() {
    var store = new PipeFlowStore();
    int a = Chamber(store, 0, 7);
    int b = Chamber(store, 1, 7);
    int c = Chamber(store, 2, 7);
    int main = store.AddEdge(a, b, 0.25, 1, FlowPipeClass.Cast);
    int small = store.AddEdge(b, c, 0.076, 1, FlowPipeClass.Cast);
    store.SetRating(main, Main);
    store.SetRating(small, Small);

    Run(store, 2.5);
    Assert.False(store.IsBurst(main));
    Run(store, 1);

    Assert.True(store.IsBurst(main));
    Assert.False(store.IsBurst(small));
    Assert.Equal(0, store.FlowKgPerSecond(main));
    Assert.True(store.PressurePa(a) < 6 * Atmosphere);
  }

  #endregion
}
