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
}
