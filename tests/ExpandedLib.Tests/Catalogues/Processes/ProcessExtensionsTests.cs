using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Catalogues;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>Tests <see cref="ProcessExtensions"/>, the C# route into the process registries
/// that JSON also reaches.</summary>
public class ProcessExtensionsTests {
  private static ProcessExtensions Fresh() =>
    new(new ProcessRouteRegistry(), new ProcessJobRegistry());

  #region Sequences

  [Fact]
  public void A_route_built_in_code_lands_where_a_declared_one_would() {
    ProcessExtensions ex = Fresh();

    Assert.Empty(
      ex.AddStages(
        "bronzebar",
        [
          new ProcessStage(2.0f, "Bronze200", ["flat"], null),
          new ProcessStage(1.0f, "Bronze100", ["flat"], "othermod:bronzeplate"),
        ],
        shape: "othermod:item/bronze-bar"
      )
    );

    ProcessRoute route = ex.Routes.Route("bronzebar")!;
    Assert.Equal("othermod:item/bronze-bar", route.Shape);
    Assert.Equal(
      ["Bronze200", "Bronze100"],
      route.RungsFor("flat").Select(s => s.Element)
    );
  }

  [Fact]
  public void Code_and_json_contribute_to_one_route() {
    // A mod may add a branch to a family declared in JSON; both write the same registry.
    ProcessExtensions ex = Fresh();
    ProcessRouteLoader.Load(
      [
        (
          "iiex:bloom.json",
          """
          {
            "family": "bloom",
            "stages": [ { "thickness": 2.0, "acceptedBy": [ "grooved" ] } ]
          }
          """
        ),
      ],
      ex.Routes
    );

    ex.AddStages("bloom", [new ProcessStage(2.0f, null, ["serrated"], null)]);

    Assert.Equal(
      ["grooved", "serrated"],
      ex.Routes.Route("bloom")!.StageAt(2.0f, "serrated")!.AcceptedBy
    );
  }

  [Fact]
  public void A_code_route_clash_is_reported_exactly_as_a_json_one_is() {
    ProcessExtensions ex = Fresh();
    ex.AddStages("bloom", [new ProcessStage(2.0f, "Mine", ["grooved"], null)]);

    var conflicts = ex.AddStages(
      "bloom",
      [new ProcessStage(2.0f, "Theirs", ["grooved"], null)]
    );

    Assert.Single(conflicts);
    Assert.Equal(
      "Mine",
      ex.Routes.Route("bloom")!.StageAt(2.0f, "grooved")!.Element
    );
  }

  [Fact]
  public void A_malformed_route_is_refused_by_the_same_rules_as_a_declared_one() {
    // The code route builds a declaration and hands it to the same parser as JSON.
    ProcessExtensions ex = Fresh();

    Assert.Throws<System.ArgumentException>(() =>
      ex.AddStages("bloom", [new ProcessStage(2.0f, null, [], null)])
    );
  }

  #endregion

  #region Terminals

  [Fact]
  public void A_job_added_in_code_is_found_by_the_machine() {
    ProcessExtensions ex = Fresh();

    Assert.Empty(
      ex.AddJobs(
        "shear",
        [new ProcessJob("othermod:strip", "othermod:rivet", 6, null, null, 0f)]
      )
    );

    Assert.Equal(6, ex.Jobs.Job("shear", "othermod:strip", null, null)!.Count);
  }

  [Fact]
  public void A_job_clash_is_reported_and_the_first_stands() {
    ProcessExtensions ex = Fresh();
    ex.AddJobs("shear", [new ProcessJob("a:b", "a:first", 1, null, null, 0f)]);

    var conflicts = ex.AddJobs(
      "shear",
      [new ProcessJob("a:b", "a:second", 2, null, null, 0f)]
    );

    Assert.Single(conflicts);
    Assert.Equal("a:first", ex.Jobs.Job("shear", "a:b", null, null)!.Output);
  }

  [Fact]
  public void A_job_yielding_nothing_is_refused_by_the_same_rule() {
    ProcessExtensions ex = Fresh();

    Assert.Throws<System.ArgumentException>(() =>
      ex.AddJobs("shear", [new ProcessJob("a:b", "a:c", 0, null, null, 0f)])
    );
  }

  #endregion

  [Fact]
  public void The_shared_surface_is_the_one_the_loaders_fill() {
    // A mod calling the API and a mod shipping JSON reach the same registries.
    Assert.Same(ProcessRouteRegistry.Shared, ProcessExtensions.Shared.Routes);
    Assert.Same(ProcessJobRegistry.Shared, ProcessExtensions.Shared.Jobs);
  }
}
