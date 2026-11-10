using ExpandedLib.Industry.Pipes;
using ExpandedLib.Networks;
using ExpandedLib.Testing;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// Covers the per-medium throughput of <see cref="BlockPipe"/>: a tier's value for one medium caps a
/// run carrying that medium, and every other medium reads the tier's own value.
/// </summary>
public class PipeMediumThroughputTests {
  #region Fixtures

  private const int Length = 12;

  // A tier of its own per test keeps the static registries from leaking between tests.
  private static TestWorld NewWorld(string tier) {
    var w = new TestWorld();
    w.RegisterNetwork("pipe", sys => new PipeNetwork(sys));
    for (int i = 0; i < Length; i++) {
      var pos = new BlockPos(i, 0, 0);
      var block = TestBlocks.Configure(
        new BlockPipe(),
        $"test:pipe-{tier}-{i}",
        300 + i,
        ("type", "straight"),
        ("tier", tier),
        ("orientation", "we")
      );
      block.SetNetworkTypeForTest("pipe");
      block.ApplyOrientationForTest("we");
      var be = new BlockEntityPipe();
      w.Place(pos, block, be);
      w.Attach(be);
      w.AddNode(pos, "pipe");
    }
    return w;
  }

  private static float Accepted(TestWorld w, string medium) {
    var net = Assert.IsType<PipeNetwork>(w.NetworkAt(new BlockPos(0, 0, 0)));
    return net.ProduceGasMeasured(1000f, 20f, medium, w.Accessor, 50f);
  }

  #endregion

  // Fails when the medium lookup is ignored and the tier value is read for air.
  [Fact]
  public void A_run_carrying_air_reads_the_tiers_air_value() {
    BlockPipe.RegisterThroughput("mediumair", () => 80f);
    BlockPipe.RegisterThroughput("mediumair", "Air", () => 30f);
    using var w = NewWorld("mediumair");

    Assert.Equal(30f, Accepted(w, "Air"), 3);
  }

  // Fails when the fallback is inverted and steam reads the air value, or the default.
  [Fact]
  public void The_same_tier_carrying_steam_reads_the_tier_default() {
    BlockPipe.RegisterThroughput("mediumsteam", () => 80f);
    BlockPipe.RegisterThroughput("mediumsteam", "Air", () => 30f);
    using var w = NewWorld("mediumsteam");

    Assert.Equal(80f, Accepted(w, "Steam"), 3);
  }

  // Fails when the fallback is inverted and a tier with no medium value reads something else.
  [Fact]
  public void A_tier_with_no_medium_value_reads_the_default_for_both() {
    BlockPipe.RegisterThroughput("mediumnone", () => 55f);
    using var air = NewWorld("mediumnone");
    Assert.Equal(55f, Accepted(air, "Air"), 3);

    using var steam = NewWorld("mediumnone");
    Assert.Equal(55f, Accepted(steam, "Steam"), 3);
  }
}
