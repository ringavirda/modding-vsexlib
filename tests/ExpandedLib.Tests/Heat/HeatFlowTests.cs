using ExpandedLib.Industry.Heat;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>Exact approach, fire conductance, burn and cooling speed of the lumped heat model.</summary>
public class HeatFlowTests {
  const double PuddlingC = 293.5;
  const double PuddlingG = 674.6 + 176.3;

  [Fact]
  public void Approach_is_exact_across_step_sizes() {
    double one = HeatFlow.Approach(20d, 1470.8d, PuddlingC, PuddlingG, 0.25d);
    double sliced = 20d;
    for (int i = 0; i < 900; i++)
      sliced = HeatFlow.Approach(
        sliced,
        1470.8d,
        PuddlingC,
        PuddlingG,
        1d / 3600d
      );
    Assert.Equal(one, sliced, 0.01d);
  }

  [Fact]
  public void Approach_reaches_1400_in_1_04_h() {
    Assert.Equal(
      1400d,
      HeatFlow.Approach(20d, 1470.8d, PuddlingC, PuddlingG, 1.04d),
      1d
    );
    Assert.True(
      HeatFlow.Approach(20d, 1470.8d, PuddlingC, PuddlingG, 1.03d) < 1400d
    );
    Assert.True(
      HeatFlow.Approach(20d, 1470.8d, PuddlingC, PuddlingG, 1.05d) > 1400d
    );
  }

  [Fact]
  public void Approach_ignores_negative_hours() =>
    Assert.Equal(
      20d,
      HeatFlow.Approach(20d, 1470.8d, PuddlingC, PuddlingG, -0.1d)
    );

  [Fact]
  public void Approach_ignores_negative_conductance() =>
    Assert.Equal(20d, HeatFlow.Approach(20d, 1470.8d, PuddlingC, -1d, 1d));

  [Fact]
  public void Approach_ignores_zero_capacity() =>
    Assert.Equal(20d, HeatFlow.Approach(20d, 1470.8d, 0d, PuddlingG, 1d));

  [Fact]
  public void FireConductance_floors_the_span() =>
    Assert.Equal(500d * 3600d, HeatFlow.FireConductance(500d, 20d, 20d));

  [Fact]
  public void FireConductance_is_power_over_the_span() =>
    Assert.Equal(
      978750d / 1450.8d,
      HeatFlow.FireConductance(271.875d, 1470.8d, 20d),
      0.01d
    );

  [Fact]
  public void FireConductance_of_no_fire_is_zero() =>
    Assert.Equal(0d, HeatFlow.FireConductance(-5d, 1470.8d, 20d));

  [Fact]
  public void BurnUnits_follows_power_over_fuel_heat() =>
    Assert.Equal(
      1.20d,
      HeatFlow.BurnUnits(363.8d * 0.7474d, 1d, 40d, 20391d),
      0.005d
    );

  [Fact]
  public void BurnUnits_of_negative_power_is_zero() =>
    Assert.Equal(0d, HeatFlow.BurnUnits(-1d, 1d, 40d, 20391d));

  [Fact]
  public void CoolingSpeed_is_square_cube() {
    Assert.Equal(6.00d, HeatFlow.CoolingSpeed(10.90d, 600d), 0.01d);
    Assert.Equal(1.66d, HeatFlow.CoolingSpeed(10.90d, 28400d), 0.01d);
  }

  [Fact]
  public void CoolingSpeed_of_no_units_is_the_speed_at_100() =>
    Assert.Equal(10.90d, HeatFlow.CoolingSpeed(10.90d, 0d));
}
