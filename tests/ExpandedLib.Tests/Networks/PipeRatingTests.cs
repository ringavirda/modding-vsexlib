using System.Collections.Generic;
using ExpandedLib.Industry.Pipes;
using ExpandedLib.Networks;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// Covers the ratings of the physical pipe network: the bore registry, the weep of the most loaded piece, its
/// wrench and replacement, the burst of a piece held above its burst pressure, the drop of a centred piece beyond its
/// span and the freeze of a standing water line, in <see cref="PipeFlowStore"/>.
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

  private static int IceNode(
    PipeFlowStore store,
    int index,
    double fill,
    double temperatureC,
    bool lagged = false
  ) {
    int node = store.AddNode(
      Cell(index),
      0.001,
      0,
      1e-7,
      temperatureC,
      SteamConstant,
      fill * 0.001 * 1000,
      0,
      true
    );
    store.SetWall(node, new PipeWall(10, 5, 5, lagged), temperatureC);
    store.SetAmbient(node, -10);
    return node;
  }

  private static void Seconds(PipeFlowStore store, int seconds) {
    for (int t = 0; t < seconds; t++)
      store.Step(1);
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

  // Fails when each weep opens a new opening and a wrench only closes it, so weep and wrench cycles pile up dead openings.
  [Fact]
  public void Weep_CyclesReuseTheirOpening() {
    var store = new PipeFlowStore();
    int a = Chamber(store, 0, 9);
    int b = Chamber(store, 1, 9);
    int edge = store.AddEdge(a, b, 0.25, 1, FlowPipeClass.Cast);
    store.SetRating(edge, Main);
    Run(store, 0.5);
    Assert.True(store.Remake(edge));
    int openings = store.OpeningCount;
    Run(store, 0.5);
    Assert.True(store.IsWeeping(edge));

    Assert.True(store.Remake(edge));
    Run(store, 0.5);

    Assert.True(store.IsWeeping(edge));
    Assert.Equal(openings, store.OpeningCount);
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

  #region Span

  // Fails when a centred piece drops one cell early or late, or the content of a dropped piece stays in the run.
  [Fact]
  public void Span_DropsBeyondN() {
    var store = new PipeFlowStore();
    var nodes = new int[7];
    for (int i = 0; i < nodes.Length; i++)
      nodes[i] = Chamber(store, i, 1);
    for (int i = 0; i < nodes.Length - 1; i++)
      store.SetRating(
        store.AddEdge(nodes[i], nodes[i + 1], 0.25, 1, FlowPipeClass.Cast),
        Main
      );
    for (int i = 1; i < nodes.Length; i++)
      store.SetSupported(nodes[i], false);

    Run(store, 0.2);

    for (int i = 0; i <= 4; i++)
      Assert.False(store.IsDropped(nodes[i]));
    Assert.True(store.IsDropped(nodes[5]));
    Assert.True(store.IsDropped(nodes[6]));
    Assert.Equal(0, store.GasKg(nodes[5]));
    Assert.True(store.OpeningLedgerKg < 0);
    Assert.True(store.IsBurst(4));
  }

  // Fails when the span is measured from the first supported cell only, so a piece between two supports drops.
  [Fact]
  public void Span_CountsFromTheNearestSupport() {
    var store = new PipeFlowStore();
    var nodes = new int[9];
    for (int i = 0; i < nodes.Length; i++)
      nodes[i] = Chamber(store, i, 1);
    for (int i = 0; i < nodes.Length - 1; i++)
      store.SetRating(
        store.AddEdge(nodes[i], nodes[i + 1], 0.25, 1, FlowPipeClass.Cast),
        Main
      );
    for (int i = 1; i < nodes.Length - 1; i++)
      store.SetSupported(nodes[i], false);

    Run(store, 0.2);

    for (int i = 0; i < nodes.Length; i++)
      Assert.False(store.IsDropped(nodes[i]));
  }

  // Fails when the span of the smallest class at a piece is not the one that counts: the main's piece 4 cells out also carries a small edge and drops at 3.
  [Fact]
  public void Span_TheSmallestClassAtAPieceCounts() {
    var store = new PipeFlowStore();
    var nodes = new int[6];
    for (int i = 0; i < nodes.Length; i++)
      nodes[i] = Chamber(store, i, 1);
    for (int i = 0; i < 4; i++)
      store.SetRating(
        store.AddEdge(nodes[i], nodes[i + 1], 0.25, 1, FlowPipeClass.Cast),
        Main
      );
    store.SetRating(
      store.AddEdge(nodes[4], nodes[5], 0.076, 1, FlowPipeClass.Cast),
      Small
    );
    for (int i = 1; i < nodes.Length; i++)
      store.SetSupported(nodes[i], false);

    Run(store, 0.2);

    Assert.False(store.IsDropped(nodes[3]));
    Assert.True(store.IsDropped(nodes[4]));
  }

  #endregion

  #region Frost

  // Fails when the freeze comes before 60 s, ignores the 90 % fill, or does not crack the weakest piece of the frozen stretch.
  [Fact]
  public void Freeze_StandingLineBursts() {
    var store = new PipeFlowStore();
    int a = IceNode(store, 0, 0.95, -5);
    int b = IceNode(store, 1, 0.95, -5);
    int c = IceNode(store, 2, 0.95, -5);
    int main = store.AddEdge(a, b, 0.25, 1, FlowPipeClass.Cast);
    int small = store.AddEdge(b, c, 0.076, 1, FlowPipeClass.Cast);
    store.SetRating(main, Main);
    store.SetRating(small, Small);
    int dry = IceNode(store, 3, 0.5, -5);

    Seconds(store, 58);
    Assert.False(store.IsIced(a));
    Assert.False(store.IsCracked(main));
    Seconds(store, 4);

    Assert.True(store.IsIced(a));
    Assert.True(store.IsIced(c));
    Assert.False(store.IsIced(dry));
    Assert.True(store.IsCracked(main));
    Assert.False(store.IsCracked(small));
    Assert.False(store.Remake(main));
    Assert.True(store.Replace(main));
  }

  // Fails when a piece replaced in a stretch that is still iced cracks again as another node of the stretch thaws.
  [Fact]
  public void Freeze_ReplacedPieceStaysSoundWhenANodeThaws() {
    var store = new PipeFlowStore();
    int a = IceNode(store, 0, 0.95, -5);
    int b = IceNode(store, 1, 0.95, -5);
    int c = IceNode(store, 2, 0.95, -5);
    int main = store.AddEdge(a, b, 0.25, 1, FlowPipeClass.Cast);
    int small = store.AddEdge(b, c, 0.076, 1, FlowPipeClass.Cast);
    store.SetRating(main, Main);
    store.SetRating(small, Small);
    Seconds(store, 62);
    Assert.True(store.IsCracked(main));
    Assert.True(store.Replace(main));

    store.SetAmbient(c, 20);
    Seconds(store, 600);

    Assert.False(store.IsIced(c));
    Assert.True(store.IsIced(b));
    Assert.False(store.IsCracked(main));
    Assert.False(store.IsCracked(small));
  }

  // Fails when a piece replaced in an iced stretch cracks again as a node of another stretch freezes.
  [Fact]
  public void Freeze_ReplacedPieceStaysSoundWhenAnotherStretchFreezes() {
    var store = new PipeFlowStore();
    int a = IceNode(store, 0, 0.95, -5);
    int b = IceNode(store, 1, 0.95, -5);
    int main = store.AddEdge(a, b, 0.25, 1, FlowPipeClass.Cast);
    store.SetRating(main, Main);
    int late = IceNode(store, 5, 0.95, 8, true);
    Seconds(store, 62);
    Assert.True(store.IsCracked(main));
    Assert.True(store.Replace(main));
    Assert.False(store.IsIced(late));

    Seconds(store, 600);

    Assert.True(store.IsIced(late));
    Assert.False(store.IsCracked(main));
  }

  // Fails when DamageOf or RestoreDamage drops the ice or the crack, so a reloaded frozen stretch is whole.
  [Fact]
  public void Freeze_DamageSurvivesARebuild() {
    var store = new PipeFlowStore();
    int a = IceNode(store, 0, 0.95, -5);
    int b = IceNode(store, 1, 0.95, -5);
    int c = IceNode(store, 2, 0.95, -5);
    int main = store.AddEdge(a, b, 0.25, 1, FlowPipeClass.Cast);
    int small = store.AddEdge(b, c, 0.076, 1, FlowPipeClass.Cast);
    store.SetRating(main, Main);
    store.SetRating(small, Small);
    Seconds(store, 62);
    var saved = new Dictionary<NodeKey, byte>();
    foreach (var (key, node) in store.Nodes)
      saved[key] = store.DamageOf(node);

    var rebuilt = new PipeFlowStore();
    for (int i = 0; i < 3; i++)
      rebuilt.AddNode(Cell(i), 0.001, 0, 1e-7, -5, SteamConstant);
    int rMain = rebuilt.AddEdge(0, 1, 0.25, 1, FlowPipeClass.Cast);
    int rSmall = rebuilt.AddEdge(1, 2, 0.076, 1, FlowPipeClass.Cast);
    rebuilt.SetRating(rMain, Main);
    rebuilt.SetRating(rSmall, Small);
    rebuilt.RestoreDamage(saved);

    Assert.True(rebuilt.IsIced(0));
    Assert.True(rebuilt.IsIced(2));
    Assert.True(rebuilt.IsCracked(rMain));
    Assert.True(rebuilt.IsWeeping(rMain));
    Assert.False(rebuilt.IsCracked(rSmall));
    Assert.False(rebuilt.IsWeeping(rSmall));
  }

  // Fails when a line that warms above 0 C stays iced.
  [Fact]
  public void Freeze_ThawsAboveZero() {
    var store = new PipeFlowStore();
    int a = IceNode(store, 0, 0.95, -5);
    Seconds(store, 62);
    Assert.True(store.IsIced(a));

    store.SetAmbient(a, 20);
    Seconds(store, 600);

    Assert.False(store.IsIced(a));
  }

  // Fails when lagging does not slow the cooling, so the lagged line freezes with the bare one.
  [Fact]
  public void Freeze_LaggingDelaysIt() {
    var store = new PipeFlowStore();
    int bare = IceNode(store, 0, 0.95, 8);
    int lagged = IceNode(store, 1, 0.95, 8, true);

    Seconds(store, 150);

    Assert.True(store.IsIced(bare));
    Assert.False(store.IsIced(lagged));
    Seconds(store, 450);
    Assert.True(store.IsIced(lagged));
  }

  #endregion
}
