using System.Linq;
using ExpandedLib.Scenarios;
using ExpandedLib.Testing.Live;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests.Scenarios.Live;

/// <summary>The end-of-case pack check against the settle wait that precedes it.</summary>
public class PackSettleTests {
  private const string Place =
    """[{ "place": { "id": "a", "code": "game:stone", "at": [0, 0, 0] } }]""";
  private const string Pack =
    """{ pack: { scope: "stacks", gathered: {}, ends: { stacks: { "game:flint": 1 } } }, settle: 2 }""";

  private static (LiveFixture Fixture, ScenarioCaseRun Run) Go(
    System.Action<LiveFixture, ScenarioCaseRun>? onTick = null,
    System.Action<LiveFixture>? setup = null
  ) {
    var fixture = new LiveFixture();
    ScenarioCase scenario = LiveFixture.Case(Place, Pack);
    fixture.Context.Pack = SetupPack.Start(scenario.Model, _ => null);
    setup?.Invoke(fixture);
    var run = new ScenarioCaseRun(scenario, fixture.Context);
    foreach (bool _ in run.Run()) {
      fixture.Clock += 0.5;
      onTick?.Invoke(fixture, run);
    }
    return (fixture, run);
  }

  /// <summary>Fails when the pack is checked before the settle wait, or the settle does not give
  /// spawns: a stack that lands after the last step is missing at the end.</summary>
  [Fact]
  public void An_item_spawned_during_the_settle_counts_toward_the_ends_stack() {
    (LiveFixture fixture, ScenarioCaseRun run) = Go(setup: f => {
      Item flint = f.Test.RegisterItem("game:flint");
      var spawned = new EntityItem {
        EntityId = 7,
        Itemstack = new ItemStack(flint, 1),
      };
      spawned.Pos.SetPos(100.5, 50.5, 100.5);
      f.Test.World.GetEntitiesInsideCuboid(
          Arg.Any<BlockPos>(),
          Arg.Any<BlockPos>(),
          Arg.Any<ActionConsumable<Entity>>()
        )
        .Returns(call =>
          f.Clock >= 1.5
            ? new Entity[] { spawned }
              .Where(e => call.ArgAt<ActionConsumable<Entity>>(2)(e))
              .ToArray()
            : []
        );
    });

    Assert.True(run.Passed);
    Assert.Equal(1, fixture.Context.Pack!.Held("game:flint"));
  }

  /// <summary>Fails when the pack failure is reported as a mod failure, so a short
  /// <c>ends</c> declaration blames the mod.</summary>
  [Fact]
  public void A_short_ends_stack_reports_the_scenario_as_the_suspect() {
    (LiveFixture fixture, ScenarioCaseRun run) = Go();

    Assert.False(run.Passed);
    Assert.Equal(2, run.FailedStep);
    Assert.Equal(Suspect.Scenario, run.Suspect);
    Assert.Contains(
      fixture.Lines,
      l => l.Contains(" pack ") && l.Contains("FAIL:")
    );
  }

  /// <summary>Fails when the pack check runs after a settle failure and overwrites it: a short
  /// <c>ends</c> would replace the logged error's verdict.</summary>
  [Fact]
  public void A_settle_error_is_not_overwritten_by_the_pack_check() {
    (LiveFixture fixture, ScenarioCaseRun run) = Go(
      (f, _) => {
        if (f.Clock >= 1)
          f.Errors.Add(EnumLogType.Error, "late tick threw");
      }
    );

    Assert.False(run.Passed);
    Assert.Equal(2, run.FailedStep);
    Assert.Equal(Suspect.Mod, run.Suspect);
    Assert.Single(fixture.Lines, l => l.Contains("FAIL:"));
    Assert.Contains(fixture.Lines, l => l.Contains(" settle "));
    Assert.DoesNotContain(fixture.Lines, l => l.Contains(" pack "));
  }
}
