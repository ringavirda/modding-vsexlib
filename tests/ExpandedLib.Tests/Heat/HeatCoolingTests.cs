using ExpandedLib.Industry.Heat;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>Linear cooling toward an ambient.</summary>
public class HeatCoolingTests {
  [Fact]
  public void CoolsAtTheRatePerHour() =>
    Assert.Equal(1000f - 480f * 0.5f, HeatCooling.Cool(1000f, 20f, 480f, 0.5d));

  [Fact]
  public void NeverFallsBelowAmbient() =>
    Assert.Equal(20f, HeatCooling.Cool(100f, 20f, 480f, 10d));

  [Fact]
  public void RateZeroOrNegativeKeepsTheTemperature() {
    Assert.Equal(700f, HeatCooling.Cool(700f, 20f, 0f, 5d));
    Assert.Equal(700f, HeatCooling.Cool(700f, 20f, -50f, 5d));
  }

  [Fact]
  public void NoElapsedTimeKeepsTheTemperature() {
    Assert.Equal(700f, HeatCooling.Cool(700f, 20f, 100f, 0d));
    Assert.Equal(700f, HeatCooling.Cool(700f, 20f, 100f, -1d));
  }

  [Fact]
  public void ABodyBelowAmbientIsNotWarmed() =>
    Assert.Equal(-10f, HeatCooling.Cool(-10f, 20f, 100f, 1d));
}
