using System.Reflection;
using ExpandedLib.Config;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>The belt reach tunable in <see cref="ExlibConfig"/>.</summary>
public class ExlibConfigBeltTests {
  [Fact]
  public void Belt_reach_defaults_to_five_blocks_before_Load() {
    Assert.Equal(5f, ExlibValues.MpBeltReachBlocks);
  }

  [Fact]
  public void Belt_reach_ranges_from_one_to_sixteen() {
    var range = typeof(ExlibConfig)
      .GetProperty(nameof(ExlibConfig.MpBeltReachBlocks))!
      .GetCustomAttribute<ExConfigRangeAttribute>()!;

    Assert.Equal(1.0, range.Min);
    Assert.Equal(16.0, range.Max);
  }
}
