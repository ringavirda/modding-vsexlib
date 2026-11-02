using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Industry.Machining;
using ExpandedLib.Testing;
using Xunit;

namespace WasherPunch.Tests;

/// <summary>
/// The drilling machine's table after one game load: the iron row from <c>config/machiningjobs</c>, the
/// meteoric row from the mod system's contributor, and the kind from <c>config/machinekinds</c>.
/// </summary>
[Collection(WorldCollection.Name)]
public class MachiningJobTests(LoadedWorld loaded) {
  private static IReadOnlyList<MachiningJob> Drill() =>
    MachiningJobRegistry.Shared.Jobs("drill");

  [Fact]
  public void The_iron_row_comes_from_the_json_table() {
    MachiningJob job = Assert.Single(
      Drill(),
      j => j.Input == "game:metalplate-iron"
    );

    Assert.Equal("washerpunch:drilledplate-iron", job.Output);
    Assert.Equal(0, job.MinTier);
    Assert.Equal(WasherPunchModSystem.DrilledKg, job.RemovedKg);
    Assert.Equal("wroughtiron", job.ResolvedMaterial);
  }

  [Fact]
  public void The_meteoric_row_comes_from_the_csharp_contributor() {
    MachiningJob job = Assert.Single(
      Drill(),
      j => j.Input == "game:metalplate-meteoriciron"
    );

    Assert.Equal("washerpunch:drilledplate-meteoriciron", job.Output);
    Assert.Equal(WasherPunchModSystem.MeteoricMinTier, job.MinTier);
    Assert.Equal(WasherPunchModSystem.DrilledKg, job.RemovedKg);
  }

  [Fact]
  public void Only_those_two_rows_are_filed_under_drill() {
    Assert.Equal(2, Drill().Count);
  }

  [Fact]
  public void Every_job_resolves_against_the_loaded_world() {
    IReadOnlyList<string> errors = MachiningJobs.Resolve(
      loaded.World.World.Collectibles
    );

    Assert.Empty(errors.Where(e => e.Contains("drill")));
  }

  [Fact]
  public void The_drill_kind_declares_its_step_at_the_rated_power() {
    Assert.True(
      MachineKindRegistry.Shared.TryGet(
        "washerpunch:drill",
        out MachineKind? kind
      )
    );

    MachineKindStep step = Assert.Single(kind!.Steps);
    Assert.Equal("drill", step.Step);
    Assert.Equal(360f, step.Power);
    Assert.Equal(10.7f, kind.RatedSpeed);
    Assert.Equal("bit", step.Tooling);
  }

  [Fact]
  public void The_sample_passes_the_machining_checks_that_apply() {
    TestWorld world = loaded.World;

    Assert.Empty(MachiningChecks.Offered());
    Assert.Empty(MachiningChecks.Envelopes(world));
    Assert.Empty(MachiningChecks.CuttingsMass(world));
  }
}
