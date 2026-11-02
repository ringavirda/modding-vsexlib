using ExpandedLib.Industry.Machining;
using Xunit;

namespace ExpandedLib.Tests.Machining;

/// <summary>The machining job catalogue: JSON declarations, defaults, the key audit, the C# route.</summary>
public class MachiningJobLoaderTests {
  private const string DrillFile = """
    {
      "schema": 1,
      "jobs": [
        { "step": "drill", "input": "test:plate", "output": "test:plate-drilled",
          "material": "wroughtiron", "wear": 2, "minTier": 1, "fixture": "test:template",
          "machines": ["test:radial"],
          "byproducts": [ { "code": "test:chips", "count": 3 } ],
          "hand": { "machine": "workbench", "recipe": "plate-drilled" } }
      ]
    }
    """;

  [Fact]
  public void A_job_declared_in_json_keeps_every_field() {
    MachiningJobRegistry registry = MachiningTestData.JobRegistry(DrillFile);

    MachiningJob job = Assert.IsType<MachiningJob>(
      registry.Job("drill", "test:plate")
    );
    Assert.Equal("test:plate-drilled", job.Output);
    Assert.Equal(2, job.Wear);
    Assert.Equal(1, job.MinTier);
    Assert.Equal("test:template", job.Fixture);
    Assert.Equal(["test:radial"], job.Machines);
    Assert.Equal(
      new MachiningByproduct("test:chips", 3),
      Assert.Single(job.Byproducts)
    );
    Assert.Equal(new MachiningHand("workbench", "plate-drilled"), job.Hand);
  }

  [Fact]
  public void A_job_with_no_optional_keys_takes_the_defaults() {
    MachiningJobRegistry registry = MachiningTestData.JobRegistry(
      """
      { "schema": 1, "jobs": [ { "step": "turn", "input": "test:a", "output": "test:b" } ] }
      """
    );

    MachiningJob job = Assert.IsType<MachiningJob>(
      registry.Job("turn", "test:a")
    );
    Assert.Equal(1, job.Count);
    Assert.Equal(MachiningJob.DefaultWear, job.Wear);
    Assert.Null(job.Removed);
    Assert.Null(job.Hand);
    Assert.Empty(job.Machines);
  }

  [Fact]
  public void A_job_with_a_misspelt_key_is_reported_and_nothing_loads() {
    var registry = new MachiningJobRegistry();
    var errors = MachiningJobLoader.Load(
      [
        (
          "test:config/machiningjobs/typo.json",
          """{ "schema": 1, "jobs": [ { "step": "turn", "input": "a", "output": "b", "wears": 2 } ] }"""
        ),
      ],
      registry
    );

    Assert.Contains("wears", Assert.Single(errors));
    Assert.Empty(registry.All);
  }

  [Fact]
  public void A_job_with_no_step_is_rejected() {
    var errors = MachiningJobLoader.Load(
      [
        (
          "test:config/machiningjobs/nostep.json",
          """{ "schema": 1, "jobs": [ { "input": "a", "output": "b" } ] }"""
        ),
      ],
      new MachiningJobRegistry()
    );

    Assert.Contains("step", Assert.Single(errors));
  }

  [Fact]
  public void A_job_contributed_from_csharp_lands_beside_the_json_ones() {
    MachiningJobRegistry registry = MachiningTestData.JobRegistry(DrillFile);

    Assert.Empty(
      registry.Contribute(
        MachiningTestData.Set(
          MachiningTestData.Job("turn", "test:other", "test:other-turned")
        )
      )
    );

    Assert.Equal(["drill", "turn"], registry.Steps);
  }
}
