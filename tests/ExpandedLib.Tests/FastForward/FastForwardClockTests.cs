using ExpandedLib.FastForwarding;
using ExpandedLib.Helpers;
using ExpandedLib.Industry.Pipes;
using ExpandedLib.Networks;
using ExpandedLib.Testing;
using NSubstitute;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>The fast-forward clock: what a machine reads as now, how a group's cursor follows the calendar, and the gap it leaves.</summary>
public class FastForwardClockTests {
  #region Fixtures

  private const double OneMachineSecond = 1.0 / GameTime.MachineSecondsPerHour;

  private static readonly BlockPos MemberPos = new(0, 5, 0);

  private static FastForwardGroup OneGroup(TestWorld w, double? cursor) {
    w.Networks.FastForward.Register(MemberPos, cursor);
    w.Networks.ListenerTick(w.Accessor, 0f);
    return Assert.Single(w.Networks.FastForward.Groups);
  }

  private static double Now(TestWorld w) => w.Calendar.TotalHours;

  #endregion

  #region NowHours

  // Fails when NowHours ignores the frame and always reads the calendar.
  [Fact]
  public void NowHours_reads_the_cursor_only_inside_a_frame() {
    var w = new TestWorld();
    w.AdvanceHours(10);

    Assert.Equal(10.0, GameTime.NowHours(w.World));
    using (FastForward.Replay(3.0))
      Assert.Equal(3.0, GameTime.NowHours(w.World));
    Assert.Equal(10.0, GameTime.NowHours(w.World));
  }

  #endregion

  #region Cursor

  // Fails when the cursor's rate is read from the calendar's speed instead of the fixed 120 machine seconds an hour.
  [Theory]
  [InlineData(30f)]
  [InlineData(60f)]
  public void The_cursor_moves_at_120_machine_seconds_an_hour(float speed) {
    var w = new TestWorld();
    w.Calendar.SpeedOfTime.Returns(speed);
    w.AdvanceHours(10);
    FastForwardGroup group = OneGroup(w, 1.0);
    double start = group.CursorHours;

    w.Networks.ListenerTick(w.Accessor, 0.2f);

    Assert.Equal(start + 0.2 / 120.0, group.CursorHours, 6);
  }

  // Fails when each call sets the cursor to the present, so a calendar jump never shows as a gap.
  [Fact]
  public void A_calendar_jump_becomes_the_gap() {
    var w = new TestWorld();
    FastForwardGroup group = OneGroup(w, null);
    w.AdvanceHours(1);

    w.Networks.ListenerTick(w.Accessor, 0.2f);

    Assert.Equal(1.0 - 0.2 / 120.0, group.Gap(Now(w)), 6);
  }

  // Fails when the 2 s clamp on one call's share is removed, so a stall's cut-off time is lost instead of left as gap.
  [Fact]
  public void A_stall_keeps_what_the_clamp_cut_as_gap() {
    var w = new TestWorld();
    FastForwardGroup group = OneGroup(w, null);
    w.AdvanceHours(5 * OneMachineSecond);

    w.Networks.ListenerTick(w.Accessor, 5f);

    Assert.Equal(2 * OneMachineSecond, group.CursorHours, 6);
    Assert.Equal(3 * OneMachineSecond, group.Gap(Now(w)), 6);
  }

  // Fails when the cursor is not held at the present, so a calendar slower than the machines' clock lets it run ahead.
  [Fact]
  public void The_cursor_never_passes_now() {
    var w = new TestWorld();
    FastForwardGroup group = OneGroup(w, null);
    w.AdvanceHours(0.3 * OneMachineSecond);

    w.Networks.ListenerTick(w.Accessor, 0.2f);
    w.Networks.ListenerTick(w.Accessor, 0.2f);

    Assert.Equal(Now(w), group.CursorHours, 6);
  }

  #endregion

  #region Pipe evaporation

  // Fails when ApplyEvaporation reads the calendar's days inside a frame, so a replay step charges the whole jump.
  [Fact]
  public void Pipe_evaporation_counts_cursor_days_in_a_frame() {
    var w = new TestWorld();
    w.RegisterNetwork("pipe", sys => new PipeNetwork(sys));
    w.Place(new BlockPos(0, 0, 0), ClosedNode.Create());
    w.AddNode(new BlockPos(0, 0, 0), "pipe");
    var run = Assert.IsType<PipeNetwork>(w.NetworkAt(new BlockPos(0, 0, 0)));
    run.RestoreState(
      new PipeNetworkState {
        Volume = ExlibValues.LitresPerPipe,
        MediumType = "Water",
      }
    );
    w.Tick();
    w.AdvanceHours(24);

    using (FastForward.Replay(6.0))
      w.Networks.ServerTick(w.Accessor, 0.2f);

    Assert.Equal(
      ExlibValues.LitresPerPipe - ExlibValues.EvaporationLitresPerDay / 4f,
      run.State!.Volume,
      3
    );
  }

  private sealed class ClosedNode : BlockNetworkNode {
    public override string NetworkType => "pipe";

    public override bool HasConnectorAt(BlockFacing face) => false;

    public static ClosedNode Create() =>
      TestBlocks.Configure(new ClosedNode(), "test:closednode", 1);
  }

  #endregion
}
