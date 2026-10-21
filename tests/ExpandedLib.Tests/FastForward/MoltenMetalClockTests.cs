using ExpandedLib.FastForwarding;
using ExpandedLib.Industry.Molten;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>A molten stack's temperature reads inside a fast-forward frame: cooled to the frame's time, by the game's own formula.</summary>
[Collection(ExMeasureCollection.Name)]
public class MoltenMetalClockTests {
  private const string Iron = "game:ingot-iron";
  private const float Speed = 24f;

  private static (TestWorld world, ItemStack stack) StampedAtZero() {
    var world = new TestWorld();
    world.RegisterItem(Iron, 1500f);
    return (world, MoltenMetal.CreateStack(world.World, Iron, 1000f, Speed)!);
  }

  // Cooling over h hours at 1000 C: h * Speed * (1000 / 200).
  private static float After(double hours) => 1000f - (float)hours * Speed * 5f;

  // Fails when the 200 in max(1, T / 200) is changed, so the replayed cooling departs from the game's.
  [Fact]
  public void The_replay_formula_matches_vanilla() {
    var (world, vanillaStack) = StampedAtZero();
    var (_, replayStack) = StampedAtZero();
    world.AdvanceHours(0.5);

    float vanilla = MoltenMetal.GetTemperature(world.World, vanillaStack);
    float replayed;
    using (FastForward.Replay(0.5))
      replayed = MoltenMetal.GetTemperature(world.World, replayStack);

    Assert.Equal(After(0.5), vanilla, 2);
    Assert.Equal(vanilla, replayed, 2);
  }

  // Fails when MoltenMetal reads Calendar.TotalHours in a frame, so a stack cools by the whole jump.
  [Fact]
  public void A_read_in_a_frame_cools_only_to_the_cursor() {
    var (world, stack) = StampedAtZero();
    world.AdvanceHours(1.0);

    float temperature;
    using (FastForward.Replay(0.01))
      temperature = MoltenMetal.GetTemperature(world.World, stack);

    Assert.Equal(After(0.01), temperature, 2);
  }

  // Fails when SetTemperature calls the game's in a frame, which stamps the calendar and puts the stack ahead of every later read.
  [Fact]
  public void A_set_in_a_frame_stamps_the_cursor() {
    var (world, stack) = StampedAtZero();
    world.AdvanceHours(0.001);

    using (FastForward.Replay(5.0))
      MoltenMetal.SetTemperature(world.World, stack, 1000f);
    float temperature;
    using (FastForward.Replay(5.01))
      temperature = MoltenMetal.GetTemperature(world.World, stack);

    Assert.Equal(After(0.01), temperature, 2);
  }
}
