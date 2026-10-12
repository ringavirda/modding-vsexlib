using System;
using System.IO;
using System.Linq;
using ExpandedLib.Testing;
using ExpandedLib.Testing.Live;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Xunit;

namespace ExpandedLib.Tests.Scenarios.Live;

/// <summary>The runner's own bookkeeping around a run, over a substitute server.</summary>
public class RunnerTests {
  private static IServerEventAPI Finished(bool keep) {
    var api = Substitute.For<ICoreServerAPI>();
    var events = Substitute.For<IServerEventAPI>();
    var manager = Substitute.For<IWorldManagerAPI>();
    manager.RegionSize.Returns(512);
    api.Event.Returns(events);
    api.WorldManager.Returns(manager);
    api.Logger.Returns(new RecordingLogger());
    _ = new ScenarioRunner(api, [], keep, null);
    return events;
  }

  /// <summary>Fails when a finished run lets go of the climate hold with keep set (V89), or keeps
  /// it without (V90).</summary>
  [Fact]
  public void A_finished_run_drops_the_climate_hold_unless_it_keeps_the_plots() {
    Finished(keep: false).Received(1).OnGetClimate -=
      Arg.Any<OnGetClimateDelegate>();
    Finished(keep: true).DidNotReceive().OnGetClimate -=
      Arg.Any<OnGetClimateDelegate>();
  }

  private sealed class Run : IDisposable {
    private readonly string _dir = Path.Combine(
      Path.GetTempPath(),
      "exlib-runner-" + Guid.NewGuid().ToString("N")
    );
    private Action<float>? _tick;
    private long _now;

    public ICoreServerAPI Api { get; } = Substitute.For<ICoreServerAPI>();

    public IServerEventAPI Events { get; } = Substitute.For<IServerEventAPI>();

    public RecordingLogger Log { get; } = new();

    public Run() {
      var manager = Substitute.For<IWorldManagerAPI>();
      manager.RegionSize.Returns(512);
      var world = Substitute.For<IServerWorldAccessor>();
      world.ElapsedMilliseconds.Returns(_ => _now);
      world.DefaultSpawnPosition.Returns(new EntityPos(0, 100, 0));
      Api.Event.Returns(Events);
      Api.WorldManager.Returns(manager);
      Api.World.Returns(world);
      Api.Logger.Returns(Log);
      Api.GetOrCreateDataPath("Scenarios").Returns(_dir);
      Events
        .RegisterGameTickListener(Arg.Any<Action<float>>(), Arg.Any<int>())
        .Returns(call => {
          _tick = call.Arg<Action<float>>();
          return 1L;
        });
      Directory.CreateDirectory(Path.Combine(_dir, "exlib"));
    }

    public void Write(string name, string text) =>
      File.WriteAllText(Path.Combine(_dir, name + ".json"), text);

    public void WriteSample() =>
      Write(
        "exlib/sample",
        File.ReadAllText(
          Path.Combine(RepoPaths.Root, "tests", "live", "sample.json")
        )
      );

    public ScenarioRunner Start(
      string[] names,
      IServerPlayer? chat = null,
      PlotAllocator? plots = null,
      ClimateHold? climate = null
    ) => new(Api, names, false, chat, plots, climate, withPlayer: false);

    public void Tick(int times, long now = 0) {
      _now = now;
      for (int i = 0; i < times; i++)
        _tick!(0.1f);
    }

    public string[] Lines =>
      [
        .. Log
          .Entries.Where(e => e.Type == EnumLogType.Notification)
          .Select(e => e.Message),
      ];

    public void Dispose() {
      if (Directory.Exists(_dir))
        Directory.Delete(_dir, true);
    }
  }

  /// <summary>Fails when a name with no file is not counted as run (V93) or as failed (V94), or
  /// prints no error line (V95).</summary>
  [Fact]
  public void A_name_with_no_file_is_a_failed_case_in_the_done_line() {
    using var run = new Run();
    var runner = run.Start(["exlib/missing"]);
    run.Tick(2);
    Assert.False(runner.Active);
    Assert.Contains(
      run.Lines,
      l => l.StartsWith("[scenario] error exlib/missing: no scenario file")
    );
    Assert.Contains(
      "[scenario] done: 1 case(s) run, 0 passed, 1 failed.",
      run.Lines
    );
  }

  /// <summary>Fails when a file the reader refuses is not counted as a failed case (V96).</summary>
  [Fact]
  public void A_file_the_reader_refuses_is_a_failed_case_in_the_done_line() {
    using var run = new Run();
    run.Write("exlib/bad", "{ not json");
    run.Start(["exlib/bad"]);
    run.Tick(2);
    Assert.Contains(
      run.Lines,
      l => l.StartsWith("[scenario] error exlib/bad:")
    );
    Assert.Contains(
      "[scenario] done: 1 case(s) run, 0 passed, 1 failed.",
      run.Lines
    );
  }

  /// <summary>Fails when the world spawn is read where the run starts instead of when its first case
  /// does, while the world has no spawn yet.</summary>
  [Fact]
  public void The_world_spawn_is_read_when_the_first_case_starts() {
    using var run = new Run();
    run.WriteSample();
    EntityPos? spawn = null;
    run.Api.World.DefaultSpawnPosition.Returns<EntityPos>(_ =>
      spawn ?? throw new NullReferenceException()
    );
    var runner = run.Start(["exlib/sample"]);
    spawn = new EntityPos(0, 100, 0);
    run.Tick(2);
    Assert.True(runner.Active);
    Assert.DoesNotContain(run.Lines, line => line.Contains("error"));
  }

  /// <summary>Fails when a case whose plot never loads is not counted as run (V97) or as failed
  /// (V98), or is not abandoned so the run can finish (V99).</summary>
  [Fact]
  public void A_plot_that_never_loads_abandons_its_case_and_counts_it_failed() {
    using var run = new Run();
    run.WriteSample();
    var runner = run.Start(["exlib/sample"]);
    run.Tick(2);
    Assert.True(runner.Active);
    run.Tick(1, now: 61_000);
    Assert.Contains(
      "[scenario] error exlib/sample: its plot never loaded",
      run.Lines
    );
    run.Tick(1, now: 61_000);
    Assert.False(runner.Active);
    Assert.Contains(
      "[scenario] done: 1 case(s) run, 0 passed, 1 failed.",
      run.Lines
    );
  }

  /// <summary>Fails when the chat filter lets the run or error lines through (V100) or drops the
  /// done line (V101).</summary>
  [Fact]
  public void Chat_gets_the_done_line_and_not_the_run_or_error_lines() {
    using var run = new Run();
    var player = Substitute.For<IServerPlayer>();
    player.ConnectionState.Returns(EnumClientState.Playing);
    run.Start(["exlib/missing"], player);
    run.Tick(2);
    player
      .Received(1)
      .SendMessage(
        GlobalConstants.GeneralChatGroup,
        Arg.Is<string>(m => m.Contains("done: 1 case(s) run")),
        EnumChatType.Notification
      );
    player
      .Received(1)
      .SendMessage(
        Arg.Any<int>(),
        Arg.Any<string>(),
        Arg.Any<EnumChatType>(),
        Arg.Any<string>()
      );
  }

  /// <summary>Fails when a second run builds its own plot allocator and lands on the first
  /// run's plots (V102).</summary>
  [Fact]
  public void Runs_sharing_an_allocator_take_plots_east_of_the_earlier_runs() {
    using var run = new Run();
    run.WriteSample();
    var plots = new PlotAllocator(0, 0);
    int first = new PlotAllocator(0, 0).Next(16, 16).MinColumnX;
    run.Start(["exlib/sample"], plots: plots);
    run.Tick(1);
    run.Start(["exlib/sample"], plots: plots);
    run.Tick(1);
    Assert.Equal(
      first + 2 * PlotAllocator.Columns(16),
      plots.Next(16, 16).MinColumnX
    );
  }

  /// <summary>Fails when each run registers the climate handler again (V103).</summary>
  [Fact]
  public void Runs_sharing_a_climate_hold_register_its_handler_once() {
    using var run = new Run();
    var climate = new ClimateHold(512);
    new ScenarioRunner(run.Api, [], true, null, null, climate);
    new ScenarioRunner(run.Api, [], true, null, null, climate);
    run.Events.Received(1).OnGetClimate += Arg.Any<OnGetClimateDelegate>();
  }
}
