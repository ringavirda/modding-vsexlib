using Xunit;

namespace ExpandedLib.Tests;

/// <summary>Pins the network tunable defaults in <see cref="ExlibValues"/> before <c>Load</c> runs.</summary>
public class ExlibNetworkDefaultsTests {
  [Fact]
  public void PipeNetworkDefaults_MatchPreMoveValues() {
    Assert.Equal(30f, ExlibValues.LitresPerPipe);
    Assert.Equal(8.0f, ExlibValues.GasLeakRate);
    Assert.Equal(10.0f, ExlibValues.LiquidLeakRate);
    Assert.Equal(50f, ExlibValues.EvaporationLitresPerDay);
    Assert.Equal(30f, ExlibValues.PipeOverpressureSeconds);
  }

  [Fact]
  public void MoltenNetworkDefaults_MatchPreMoveValues() {
    Assert.Equal(50, ExlibValues.MoltenFlowRate);
    Assert.Equal(10, ExlibValues.MoltenMinFlowAmount);
  }
}
