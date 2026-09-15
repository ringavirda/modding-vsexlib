using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Catalogues;
using ExpandedLib.Definitions;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Datastructures;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>Tests <see cref="ProcessItemEmitter"/>: a stage naming a code is a stopping point,
/// emitted as an item at inject time.</summary>
public class ProcessItemEmitterTests {
  private static ProcessRoute Route(string json) {
    Assert.True(
      ProcessRoute.TryParse(
        new JsonObject(JToken.Parse(json)),
        out ProcessRoute? route,
        out string? error
      ),
      error
    );
    return route!;
  }

  private const string BarRoute = """
    {
      "family": "shingledbar",
      "shape": "iiex:item/smithed/shingled-bar",
      "stages": [
        { "thickness": 3.0, "element": "ShingledBar1", "acceptedBy": [ "grooved" ] },
        { "thickness": 2.0, "element": "Grooved200", "acceptedBy": [ "grooved" ], "code": "iiex:rolledrod" }
      ]
    }
    """;

  private static List<ExItemDef> Emit(params string[] routes) =>
    [.. ProcessItemEmitter.Emit(routes.Select(Route), out _)];

  private static JObject Json(ExItemDef def) => def.ToJson();

  #region What becomes an item

  [Fact]
  public void A_stage_that_names_a_code_becomes_an_item() {
    ExItemDef def = Assert.Single(Emit(BarRoute));

    Assert.Equal("rolledrod", Json(def)["code"]!.ToString());
    Assert.Equal("iiex", def.Location.Domain);
  }

  [Fact]
  public void A_stage_with_no_code_is_a_render_only_intermediate_and_builds_nothing() {
    // The 3.0 entry stage is a state the piece passes through, not a thing it becomes.
    Assert.Single(Emit(BarRoute));
  }

  [Fact]
  public void The_owning_domain_comes_from_the_declared_code() {
    // A modder's products land in their own domain, not the framework's.
    ExItemDef def = Assert.Single(
      Emit(BarRoute.Replace("iiex:rolledrod", "othermod:splinerod"))
    );

    Assert.Equal("othermod", def.Location.Domain);
    Assert.Equal("splinerod", Json(def)["code"]!.ToString());
  }

  [Fact]
  public void A_stage_can_opt_out_and_build_nothing() {
    // A code with generate: false wires up an item the mod ships itself.
    Assert.Empty(
      Emit(
        BarRoute.Replace(
          "\"code\": \"iiex:rolledrod\"",
          "\"code\": \"iiex:rolledrod\", \"generate\": false"
        )
      )
    );
  }

  [Fact]
  public void A_code_in_the_vanilla_domain_is_never_generated_over() {
    // A declaration pointing at a vanilla item wires it up; it never builds over `game:`.
    List<ExItemDef> defs =
    [
      .. ProcessItemEmitter.Emit(
        [Route(BarRoute.Replace("iiex:rolledrod", "game:rod-iron"))],
        out List<string> skipped
      ),
    ];

    Assert.Empty(defs);
    Assert.Contains("game:rod-iron", Assert.Single(skipped));
  }

  [Fact]
  public void A_code_with_no_path_is_skipped_rather_than_failing_the_load() {
    // A code with a domain but no path is skipped; a blank code is not a stopping point at all.
    List<ExItemDef> defs =
    [
      .. ProcessItemEmitter.Emit(
        [Route(BarRoute.Replace("\"iiex:rolledrod\"", "\"iiex:\""))],
        out List<string> skipped
      ),
    ];

    Assert.Empty(defs);
    Assert.Single(skipped);
  }

  [Fact]
  public void A_blank_code_is_not_a_stopping_point_at_all() {
    Assert.Empty(Emit(BarRoute.Replace("\"iiex:rolledrod\"", "\"   \"")));
  }

  [Fact]
  public void One_code_declared_twice_yields_one_item() {
    // Two branches reaching the same product yield one item, not a duplicate itemtype.
    Assert.Single(
      Emit(
        BarRoute,
        BarRoute.Replace(
          "\"family\": \"shingledbar\"",
          "\"family\": \"castbillet\""
        )
      )
    );
  }

  #endregion

  #region What the generated item looks like

  [Fact]
  public void The_item_is_drawn_by_its_own_element_of_the_family_shape() {
    // The generated item renders as the stage element within the family's shape file.
    JObject shape = (JObject)Json(Assert.Single(Emit(BarRoute)))["shape"]!;

    Assert.Equal("iiex:item/smithed/shingled-bar", shape["base"]!.ToString());
    Assert.Equal(
      ["Grooved200"],
      shape["selectiveElements"]!.Select(e => e.ToString())
    );
  }

  [Fact]
  public void A_stage_with_no_element_takes_the_whole_shape_file() {
    JObject shape = (JObject)
      Json(
        Assert.Single(
          Emit(BarRoute.Replace("\"element\": \"Grooved200\", ", ""))
        )
      )["shape"]!;

    Assert.Equal("iiex:item/smithed/shingled-bar", shape["base"]!.ToString());
    Assert.Null(shape["selectiveElements"]);
  }

  [Fact]
  public void A_sparse_declaration_still_yields_a_working_item() {
    // A sparse declaration with no shape must still load as a reachable item.
    ExItemDef def = Assert.Single(
      Emit(
        """
        {
          "family": "bronzebar",
          "stages": [ { "thickness": 1.0, "acceptedBy": [ "flat" ], "code": "othermod:bronzeplate" } ]
        }
        """
      )
    );

    JObject json = Json(def);
    Assert.NotNull(json["shape"]);
    Assert.NotNull(json["creativeinventory"]);
    Assert.NotNull(json["maxstacksize"]);
  }

  #endregion

  #region The buildable set

  [Fact]
  public void The_generated_codes_are_public_so_a_guard_can_check_them() {
    Assert.Equal(
      ["iiex:rolledrod"],
      ProcessItemEmitter.GeneratedCodes([Route(BarRoute)])
    );
  }

  [Fact]
  public void An_opted_out_stage_is_not_in_the_generated_set() {
    Assert.Empty(
      ProcessItemEmitter.GeneratedCodes([
        Route(
          BarRoute.Replace(
            "\"code\": \"iiex:rolledrod\"",
            "\"code\": \"iiex:rolledrod\", \"generate\": false"
          )
        ),
      ])
    );
  }

  #endregion
}
