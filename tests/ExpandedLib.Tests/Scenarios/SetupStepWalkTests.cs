using System.Linq;
using ExpandedLib.Scenarios;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ExpandedLib.Tests.Scenarios;

/// <summary>Every step a setup case runs, with its path.</summary>
public class SetupStepWalkTests {
  private static SetupFileModel Model(string changes) {
    var root = JObject.Parse(
      """
      {
        setup: "walk",
        description: "a walk",
        mods: [],
        seconds: 10,
        steps: [{ "place": { "id": "a", "code": "game:stone", "at": [0, 0, 0] } }],
        record: {}
      }
      """
    );
    root.Merge(
      JObject.Parse(changes),
      new JsonMergeSettings { MergeArrayHandling = MergeArrayHandling.Replace }
    );
    return SetupFileModel.Parse(root.ToString(), "walk.json");
  }

  // Fails when the walk skips the steps of a during action.
  [Fact]
  public void The_walk_yields_a_during_actions_steps_with_their_path() {
    SetupFileModel model = Model(
      """{ during: [{ at: 2, step: { "paste": { "file": "x", "at": [0, 0, 0] } } }] }"""
    );
    SetupStep paste = SetupStepWalk.Of(model).Single(s => s.Kind == "paste");
    Assert.Contains("during[0]", paste.Path);
  }

  // Fails when the walk skips a repeat body, or the main steps.
  [Fact]
  public void The_walk_yields_the_main_steps_and_a_repeat_body() {
    SetupFileModel model = Model(
      """{ steps: [{ "place": { "id": "a", "code": "game:stone", "at": [0, 0, 0] } }, { "repeat": { "times": 2, "steps": [{ "advance": { "hours": 1 } }] } }] }"""
    );
    Assert.Equal(
      ["place", "repeat", "advance"],
      SetupStepWalk.Of(model).Select(s => s.Kind)
    );
  }

  // Fails when the walk skips the steps a variant inserts after an entry.
  [Fact]
  public void The_walk_yields_a_variants_after_steps() {
    SetupFileModel model = Model(
      """
      {
        steps: [{ "place": { "id": "a", "code": "game:stone", "at": [0, 0, 0] } }],
        variants: { later: { after: { a: [{ "advance": { "hours": 1 } }] } } }
      }
      """
    );
    Assert.Contains(SetupStepWalk.Of(model), s => s.Kind == "advance");
  }

  // Fails when the walk skips the steps a variant replaces.
  [Fact]
  public void The_walk_yields_a_variants_replacing_step() {
    SetupFileModel model = Model(
      """
      {
        steps: [{ "place": { "id": "a", "code": "game:stone", "at": [0, 0, 0] } }],
        variants: { other: { replace: { a: { "place": { "id": "a", "code": "game:dirt", "at": [0, 0, 0] } } } } }
      }
      """
    );
    SetupStep[] places = [.. SetupStepWalk.Of(model).Where(s => s.Kind == "place")];
    Assert.Equal(2, places.Length);
    Assert.Contains(places, s => s.Path.Contains("replace"));
  }

  // Fails when the walk skips the during actions a variant brings of its own.
  [Fact]
  public void The_walk_yields_a_variants_own_during_steps() {
    SetupFileModel model = Model(
      """
      {
        variants: { busy: { during: [{ at: 2, step: { "advance": { "hours": 1 } } }] } }
      }
      """
    );
    SetupStep advance = SetupStepWalk.Of(model).Single(s => s.Kind == "advance");
    Assert.Contains("busy", advance.Path);
    Assert.Contains("during[0]", advance.Path);
  }

  // Fails when the walk skips the steps a variant's model runs after a layout entry.
  [Fact]
  public void The_walk_yields_the_steps_run_after_a_layout_entry() {
    SetupFileModel model = Model(
      """
      {
        layout: { legend: { "A": { id: "lay", code: "game:stone" } }, layers: { "0": ["A"] } },
        variants: { later: { after: { lay: [{ "advance": { "hours": 1 } }] } } }
      }
      """
    );
    SetupFileModel applied = model.ForVariant(model.Variants.Single());
    Assert.Single(applied.AfterEntry);
    Assert.Equal(
      2,
      SetupStepWalk.Of(applied).Count(s => s.Kind == "advance")
    );
  }
}
