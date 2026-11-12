using System.Linq;
using ExpandedLib.Industry.Machining;
using Xunit;

namespace ExpandedLib.Tests.Machining;

/// <summary>The registry's key: a job is one (step, input), and an input may carry several steps.</summary>
public class MachiningJobRegistryTests {
  // Fails when the (step, input) match ignores the step.
  [Fact]
  public void One_input_with_a_drill_job_and_a_turn_job_keeps_both() {
    var registry = new MachiningJobRegistry();

    Assert.Empty(
      registry.Contribute(
        MachiningTestData.Set(
          MachiningTestData.Job("drill", "test:pipe", "test:pipe-drilled"),
          MachiningTestData.Job("turn", "test:pipe", "test:pipe-faced")
        )
      )
    );

    Assert.Equal(["drill", "turn"], registry.StepsFor("test:pipe"));
    Assert.Equal("test:pipe-faced", registry.Job("turn", "test:pipe")!.Output);
    Assert.Equal(
      "test:pipe-drilled",
      registry.Job("drill", "test:pipe")!.Output
    );
  }

  [Fact]
  public void A_second_job_on_one_step_and_input_with_another_output_is_reported_and_the_first_stands() {
    var registry = new MachiningJobRegistry();
    registry.Contribute(
      MachiningTestData.Set(
        MachiningTestData.Job("drill", "test:a", "test:one")
      )
    );

    var conflicts = registry.Contribute(
      MachiningTestData.Set(
        MachiningTestData.Job("drill", "test:a", "test:two")
      )
    );

    Assert.Contains("test:two", Assert.Single(conflicts));
    Assert.Equal("test:one", registry.Job("drill", "test:a")!.Output);
  }

  [Fact]
  public void An_identical_redeclaration_is_a_no_op() {
    var registry = new MachiningJobRegistry();
    MachiningJobSet set = MachiningTestData.Set(
      MachiningTestData.Job("drill", "test:a", "test:one")
    );
    registry.Contribute(set);

    Assert.Empty(registry.Contribute(set));
    Assert.Single(registry.All);
  }

  [Fact]
  public void Jobs_and_steps_follow_declaration_order() {
    var registry = new MachiningJobRegistry();
    registry.Contribute(
      MachiningTestData.Set(
        MachiningTestData.Job("plane", "test:a", "test:a1"),
        MachiningTestData.Job("bore", "test:b", "test:b1"),
        MachiningTestData.Job("plane", "test:c", "test:c1")
      )
    );

    Assert.Equal(["plane", "bore"], registry.Steps);
    Assert.Equal(
      ["test:a", "test:c"],
      registry.Jobs("plane").Select(j => j.Input)
    );
    Assert.Empty(registry.Jobs("turn"));
    Assert.Null(registry.Job("turn", "test:a"));
  }

  private static MachiningJob Fixtured(string output, string? fixture) =>
    MachiningTestData.Job("turn", "test:blank", output) with {
      Fixture = fixture,
    };

  // Fails when the lookup ignores the fixture, or the claim check keys on step and input alone.
  [Fact]
  public void Jobs_on_one_input_pick_by_their_fixture() {
    var registry = new MachiningJobRegistry();

    Assert.Empty(
      registry.Contribute(
        MachiningTestData.Set(
          Fixtured("test:flat", "test:set-flat"),
          Fixtured("test:grooved", "test:set-grooved"),
          Fixtured("test:wide", "test:set-wide")
        )
      )
    );

    Assert.Equal(
      "test:grooved",
      registry.Job("turn", "test:blank", "test:set-grooved")!.Output
    );
    Assert.Equal(
      "test:wide",
      registry.Job("turn", "test:blank", "test:set-wide")!.Output
    );
    Assert.Null(registry.Job("turn", "test:blank", "test:set-other"));
    Assert.Null(registry.Job("turn", "test:blank"));
    Assert.Equal(3, registry.JobsFor("turn", "test:blank").Count);
  }

  [Fact]
  public void The_fixtureless_job_is_the_fallback_for_any_laid_fixture() {
    var registry = new MachiningJobRegistry();
    registry.Contribute(
      MachiningTestData.Set(Fixtured("test:plain", null))
    );

    Assert.Equal(
      "test:plain",
      registry.Job("turn", "test:blank", "test:set-flat")!.Output
    );
    Assert.Equal("test:plain", registry.Job("turn", "test:blank")!.Output);
  }

  // Fails when a fixture-less job and a fixtured one on one (step, input) stop conflicting, in either order.
  [Fact]
  public void A_fixtureless_duplicate_still_conflicts() {
    var bareFirst = new MachiningJobRegistry();
    bareFirst.Contribute(MachiningTestData.Set(Fixtured("test:plain", null)));
    Assert.Contains(
      "test:flat",
      Assert.Single(
        bareFirst.Contribute(
          MachiningTestData.Set(Fixtured("test:flat", "test:set-flat"))
        )
      )
    );
    Assert.Single(bareFirst.All);

    var fixturedFirst = new MachiningJobRegistry();
    fixturedFirst.Contribute(
      MachiningTestData.Set(Fixtured("test:flat", "test:set-flat"))
    );
    Assert.Contains(
      "test:plain",
      Assert.Single(
        fixturedFirst.Contribute(
          MachiningTestData.Set(Fixtured("test:plain", null))
        )
      )
    );
    Assert.Single(fixturedFirst.All);
  }

  [Fact]
  public void A_second_job_under_one_fixture_with_another_output_is_reported() {
    var registry = new MachiningJobRegistry();
    registry.Contribute(
      MachiningTestData.Set(Fixtured("test:flat", "test:set-flat"))
    );

    Assert.Contains(
      "test:other",
      Assert.Single(
        registry.Contribute(
          MachiningTestData.Set(Fixtured("test:other", "test:set-flat"))
        )
      )
    );
    Assert.Equal(
      "test:flat",
      registry.Job("turn", "test:blank", "test:set-flat")!.Output
    );
  }
}
