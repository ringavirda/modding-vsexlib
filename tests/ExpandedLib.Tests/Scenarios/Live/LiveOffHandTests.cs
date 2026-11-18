#if GAME_GE_1_22
using ExpandedLib.Testing;
using ExpandedLib.Testing.Live;
using Vintagestory.API.Common;
using Xunit;

namespace ExpandedLib.Tests.Scenarios.Live;

/// <summary>How a scenario fills an off-hand slot.</summary>
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
}
#endif
