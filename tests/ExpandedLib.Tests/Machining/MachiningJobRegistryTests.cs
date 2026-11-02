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
}
