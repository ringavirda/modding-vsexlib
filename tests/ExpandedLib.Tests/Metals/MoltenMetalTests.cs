using ExpandedLib.Industry.Molten;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>The shared molten-metal value helper: the glow scale and metal-name formatting read by
/// canal cells, taps, pedestals and barrels.</summary>
// Reads the global ExMeasure.System through MoltenMetal's temperature formatter; must not run
// beside a class that flips it.
[Collection(ExMeasureCollection.Name)]
public class MoltenMetalTests {
  [Theory]
  [InlineData(20f, 0)] // cold
  [InlineData(499f, 0)] // just below the glow floor
  [InlineData(500f, 0)] // floor is exclusive (> GlowMinTemp)
  [InlineData(530f, 1)] // (530-500)/30 = 1
  [InlineData(800f, 10)] // (800-500)/30 = 10
  [InlineData(5000f, 24)] // clamped to the 24 ceiling
  public void GlowLevel_scales_from_the_glow_floor_and_clamps(
    float temp,
    int expected
  ) {
    Assert.Equal((byte)expected, MoltenMetal.GlowLevel(temp));
  }

  [Theory]
  [InlineData("game:ingot-iron", "Iron")] // ingot- prefix dropped, capitalised
  [InlineData("game:ingot-steel", "Steel")]
  [InlineData("iiex:slag", "Slag")] // non-ingot path used verbatim
  [InlineData("game:metalbit-copper", "Metalbit-copper")]
  public void DisplayName_strips_ingot_prefix_and_capitalises(
    string code,
    string expected
  ) {
    Assert.Equal(expected, MoltenMetal.DisplayName(code));
  }

  [Fact]
  public void Thresholds_are_ordered_hardened_below_liquid() {
    Assert.True(MoltenMetal.HardenedThreshold < MoltenMetal.LiquidThreshold);
  }

  [Fact]
  public void FormatTemperature_reads_cold_below_room_temperature() {
    // Below 21 C it prints the "cold" label (here the echoed lang key), not a number.
    Assert.Equal("exlib:metalstate-cold", MoltenMetal.FormatTemperature(15f));
  }

  [Fact]
  public void FormatTemperature_prints_the_rounded_value_when_warm() {
    // With no formatter injected (iiex wires its own ExMeasure in-game), the exlib default prints
    // the metric "650 deg C" form.
    Assert.StartsWith("650 ", MoltenMetal.FormatTemperature(650f));
  }

  #region SyncCooldownSpeed

  private const string Iron = "game:ingot-iron";

  // Below the game's own cooldown step on every version (1/150 h on 1.22, 1/85 h before).
  private const double UnderOneCooldownStep = 1.0 / 200.0;

  private static (TestWorld world, ItemStack stack) StampedAtZero(float speed) {
    var world = new TestWorld();
    world.RegisterItem(Iron, 1500f);
    return (world, MoltenMetal.CreateStack(world.World, Iron, 1000f, speed)!);
  }

  private static ITreeAttribute Tree(ItemStack stack) =>
    (ITreeAttribute)stack.Attributes["temperature"];

  // Fails when the sync rebases every call: the baseline moves to the current hour.
  [Fact]
  public void SyncCooldownSpeed_leaves_a_stack_at_its_stamped_rate_untouched() {
    var (world, stack) = StampedAtZero(24f);
    world.AdvanceHours(UnderOneCooldownStep);

    MoltenMetal.SyncCooldownSpeed(world.World, stack, 24f);

    Assert.Equal(0.0, Tree(stack).GetDouble("temperatureLastUpdate"));
    Assert.Equal(1000f, Tree(stack).GetFloat("temperature"));
    Assert.Equal(24f, Tree(stack).GetFloat("cooldownSpeed"));
  }

  // Fails when the sync never re-stamps: the rate stays 24 and the baseline at zero.
  [Fact]
  public void SyncCooldownSpeed_restamps_and_rebases_a_stack_at_another_rate() {
    var (world, stack) = StampedAtZero(24f);
    world.AdvanceHours(UnderOneCooldownStep);

    MoltenMetal.SyncCooldownSpeed(world.World, stack, 300f);

    Assert.Equal(300f, Tree(stack).GetFloat("cooldownSpeed"));
    Assert.Equal(
      UnderOneCooldownStep,
      Tree(stack).GetDouble("temperatureLastUpdate"),
      9
    );
    Assert.Equal(1000f, Tree(stack).GetFloat("temperature"));
  }

  #endregion
}
