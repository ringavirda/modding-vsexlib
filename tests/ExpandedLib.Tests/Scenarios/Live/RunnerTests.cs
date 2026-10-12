using ExpandedLib.Testing;
using ExpandedLib.Testing.Live;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Xunit;

namespace ExpandedLib.Tests.Scenarios.Live;

/// <summary>The runner's own bookkeeping around a run, over a substitute server.</summary>
public class RunnerTests {
  private static IServerEventAPI Finished(bool keep) {
    var api = Substitute.For<ICoreServerAPI>();
    var events = Substitute.For<IServerEventAPI>();
    var manager = Substitute.For<IWorldManagerAPI>();
    manager.DefaultSpawnPosition.Returns([0, 100, 0]);
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
}
