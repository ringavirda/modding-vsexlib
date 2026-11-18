#if GAME_GE_1_22
using System;
using System.IO;
using ExpandedLib.Testing;
using ExpandedLib.Testing.Live;
using Vintagestory.API.Common;
using Xunit;

namespace ExpandedLib.Tests.Scenarios.Live;

/// <summary>How a scenario fills an off-hand slot and which allow lists a run loads.</summary>
public class LiveOffHandTests
{
  // Fails when Put writes the slot without the check.
  [Fact]
  public void Put_RefusesAsScenarioStepFailure()
  {
    var world = new TestWorld();
    var stack = new ItemStack(world.RegisterItem("test:plain-lump"));
    var slot = new DummySlot(null);

    var e = Assert.Throws<StepFailure>(() => LiveOffHand.Put(slot, stack));

    Assert.Equal(Suspect.Scenario, e.Suspect);
    Assert.Contains("test:plain-lump", e.Message);
    Assert.True(slot.Empty);
  }

  // Fails when Load reads the first subfolder only.
  [Fact]
  public void Load_UnionsSubfolders()
  {
    string root = Path.Combine(
      Path.GetTempPath(),
      "offhand-live-" + Guid.NewGuid().ToString("N")
    );
    try
    {
      foreach (
        (string mod, string line) in new[]
        {
          ("a", "game:coke fill"),
          ("b", "iiex:pig fill as scrap"),
        }
      )
      {
        Directory.CreateDirectory(Path.Combine(root, mod));
        File.WriteAllText(
          Path.Combine(root, mod, OffHandRule.FileName),
          line + "\n"
        );
      }
      Directory.CreateDirectory(Path.Combine(root, "c"));

      var allowed = LiveOffHand.Load(root);

      Assert.Equal(2, allowed.Count);
      Assert.Equal("fill", allowed["game:coke"]);
      Assert.Equal("fill as scrap", allowed["iiex:pig"]);
    }
    finally
    {
      Directory.Delete(root, true);
    }
  }
}
#endif
