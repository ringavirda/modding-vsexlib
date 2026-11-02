using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Industry.Machining;
using Vintagestory.API.Common;
using Xunit;

namespace ExpandedLib.Tests.Machining;

/// <summary>The removed mass a job derives from its items' units and its material's density.</summary>
public class MachiningJobResolveTests {
  private const float CastIron25Units = 25f / 2.5f * 0.000244140625f * 7200f;

  private static (
    MachiningJobRegistry Jobs,
    CuttingMaterialRegistry Materials
  ) Registries(params MachiningJob[] jobs) {
    var registry = new MachiningJobRegistry();
    registry.Contribute(MachiningTestData.Set(jobs));
    return (
      registry,
      MachiningTestData.MaterialRegistry(MachiningTestData.Materials)
    );
  }

  private static CollectibleObject[] Items(
    int inUnits,
    int outUnits,
    string? material = null
  ) =>
    [
      MachiningTestData.Collectible("test:blank", inUnits, material),
      MachiningTestData.Collectible("test:worked", outUnits),
    ];

  [Fact]
  public void The_removed_mass_is_the_unit_difference_over_the_voxel_density_in_cast_iron() {
    var (jobs, materials) = Registries(
      MachiningTestData.Job("turn", "test:blank", "test:worked", 1, "castiron")
    );

    Assert.Empty(MachiningJobs.Resolve(Items(100, 75), jobs, materials));

    Assert.Equal(17.578125f, jobs.Job("turn", "test:blank")!.RemovedKg, 3);
    Assert.Equal(CastIron25Units, jobs.Job("turn", "test:blank")!.RemovedKg, 3);
  }

  // Fails when the count is left out of the formula.
  [Fact]
  public void A_count_of_two_takes_both_outputs_off_the_input() {
    var (jobs, materials) = Registries(
      MachiningTestData.Job("turn", "test:blank", "test:worked", 2, "castiron")
    );

    Assert.Empty(MachiningJobs.Resolve(Items(175, 75), jobs, materials));

    Assert.Equal(CastIron25Units, jobs.Job("turn", "test:blank")!.RemovedKg, 3);
  }

  [Fact]
  public void A_declared_removed_mass_overrides_the_units() {
    var job = MachiningTestData.Job(
      "turn",
      "test:blank",
      "test:worked",
      1,
      "castiron"
    ) with {
      Removed = 4f,
    };
    var (jobs, materials) = Registries(job);

    Assert.Empty(MachiningJobs.Resolve(Items(100, 75), jobs, materials));

    Assert.Equal(4f, jobs.Job("turn", "test:blank")!.RemovedKg);
  }

  // Fails when the density is read from the wrong material.
  [Fact]
  public void Each_job_reads_the_density_of_its_own_material() {
    var (jobs, materials) = Registries(
      MachiningTestData.Job("turn", "test:blank", "test:worked", 1, "castiron"),
      MachiningTestData.Job(
        "plane",
        "test:blank",
        "test:worked",
        1,
        "wroughtiron"
      )
    );

    Assert.Empty(MachiningJobs.Resolve(Items(100, 75), jobs, materials));

    Assert.Equal(
      25f / 2.5f * 0.000244140625f * 7200f,
      jobs.Job("turn", "test:blank")!.RemovedKg,
      3
    );
    Assert.Equal(
      25f / 2.5f * 0.000244140625f * 7810f,
      jobs.Job("plane", "test:blank")!.RemovedKg,
      3
    );
    Assert.Equal(
      "wroughtiron",
      jobs.Job("plane", "test:blank")!.ResolvedMaterial
    );
  }

  [Fact]
  public void The_input_item_names_the_material_when_the_job_does_not() {
    var (jobs, materials) = Registries(
      MachiningTestData.Job("turn", "test:blank", "test:worked")
    );

    Assert.Empty(
      MachiningJobs.Resolve(Items(100, 75, "wroughtiron"), jobs, materials)
    );

    Assert.Equal(
      "wroughtiron",
      jobs.Job("turn", "test:blank")!.ResolvedMaterial
    );
  }

  private static IReadOnlyList<string> ResolveWith(
    MachiningJob job,
    params string[] extraItems
  ) {
    var (jobs, materials) = Registries(job);
    var items = new List<CollectibleObject>(Items(100, 75));
    foreach (string code in extraItems)
      items.Add(MachiningTestData.Collectible(code, 1));
    return MachiningJobs.Resolve(items, jobs, materials);
  }

  private static MachiningJob WithChips(params string[] codes) =>
    MachiningTestData.Job(
      "turn",
      "test:blank",
      "test:worked",
      1,
      "castiron"
    ) with {
      Byproducts = [.. codes.Select(c => new MachiningByproduct(c, 1))],
    };

  // Fails when a byproduct that names no item passes, which leaves the chips slot to refuse it forever.
  [Fact]
  public void A_byproduct_naming_no_item_is_reported() {
    var errors = ResolveWith(WithChips("test:ghost"));

    Assert.Contains("test:ghost", Assert.Single(errors));
  }

  // Fails when two byproduct items pass, which the one chips slot cannot hold.
  [Fact]
  public void Byproducts_of_two_items_are_reported() {
    var errors = ResolveWith(
      WithChips("test:chips", "test:dust"),
      "test:chips",
      "test:dust"
    );

    Assert.Contains("more than one", Assert.Single(errors));
  }

  [Fact]
  public void Byproducts_of_one_item_resolve() =>
    Assert.Empty(
      ResolveWith(WithChips("test:chips", "test:chips"), "test:chips")
    );

  // Fails when a fixture that names no item passes.
  [Fact]
  public void A_fixture_naming_no_item_is_reported() {
    MachiningJob job = MachiningTestData.Job(
      "turn",
      "test:blank",
      "test:worked",
      1,
      "castiron"
    ) with {
      Fixture = "test:template",
    };

    Assert.Contains("test:template", Assert.Single(ResolveWith(job)));
    Assert.Empty(ResolveWith(job, "test:template"));
  }

  [Fact]
  public void A_job_that_cannot_resolve_is_reported_by_name() {
    var (jobs, materials) = Registries(
      MachiningTestData.Job("turn", "test:blank", "test:worked", 1, "castiron"),
      MachiningTestData.Job(
        "plane",
        "test:missing",
        "test:worked",
        1,
        "castiron"
      ),
      MachiningTestData.Job("bore", "test:blank", "test:worked", 1, "brass"),
      MachiningTestData.Job("slot", "test:blank", "test:worked"),
      MachiningTestData.Job("drill", "test:blank", "test:worked", 4, "castiron")
    );

    var errors = MachiningJobs.Resolve(Items(100, 75), jobs, materials);

    Assert.Equal(4, errors.Count);
    Assert.Contains(errors, e => e.Contains("test:missing"));
    Assert.Contains(errors, e => e.Contains("brass"));
    Assert.Contains(
      errors,
      e => e.StartsWith("slot") && e.Contains("material")
    );
    Assert.Contains(
      errors,
      e => e.StartsWith("drill") && e.Contains("not above 0")
    );
  }
}
