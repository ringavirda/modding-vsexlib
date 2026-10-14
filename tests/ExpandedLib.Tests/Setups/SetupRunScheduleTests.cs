#if GAME_GE_1_22
using System.Collections.Generic;
using ExpandedLib.Scenarios;
using ExpandedLib.Testing;
using ExpandedLib.Tests.Scenarios;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests.Setups;

/// <summary>Which <c>during</c> actions the run reports due, against a world the test changes
/// between them (setups.md, The run).</summary>
public class SetupRunScheduleTests
{
  private const string Wait = "{\"wait\": {\"seconds\": 1}}";

  private static SetupFileModel Model() =>
    SetupFileModel.Parse(
      "{\"setup\": \"schedule\", \"description\": \"d\", \"mods\": [\"iiex\"], \"seconds\": 6, "
        + "\"steps\": [{\"place\": {\"id\": \"a\", \"code\": \"game:stone\", \"at\": [0, 0, 0]}}], "
        + "\"during\": ["
        + $"{{\"at\": 2, \"step\": {Wait}}}, "
        + "{\"every\": 1, \"when\": {\"at\": [1, 0, 0], \"block\": \"test:lever-on\"}, "
        + $"\"step\": {Wait}}}], "
        + "\"record\": {\"machines\": {\"a\": {\"series\": {\"state\": {\"words\": [\"idle\"]}}}}}}",
      "schedule.json"
    );

  // Fails when Due builds the whole list before any action runs.
  [Fact]
  public void An_action_s_step_can_make_a_later_when_hold_in_the_same_second()
  {
    var world = new SetupTestWorld();
    var run = new SetupRun(Model(), world, "1.22", []);
    var ran = new List<(int Second, string Kind)>();

    for (int second = 0; second < 4; second++)
      foreach (SetupDuring action in run.Due(second))
      {
        ran.Add((second, action.At != null ? "at" : "when"));
        if (action.At != null)
          world.Test.Place(
            new BlockPos(1, 0, 0),
            TestBlocks.Configure(new Block(), "test:lever-on", 1)
          );
      }

    Assert.Equal([(2, "at"), (2, "when"), (3, "when")], ran);
  }
}
#endif
