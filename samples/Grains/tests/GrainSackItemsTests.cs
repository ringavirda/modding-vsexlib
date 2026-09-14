using System.Linq;
using Xunit;

namespace Grains.Tests;

/// <summary>
/// <see cref="GrainSackItems.Emit"/>: one sack item per grain, coded <c>sack-&lt;code&gt;</c>,
/// carrying the shared linen sack shape.
/// </summary>
public class GrainSackItemsTests {
  private static readonly GrainDef[] Six =
  [
    new()
    {
      Code = "spelt",
      Grain = "game:grain-spelt",
      Flour = "game:flour-spelt",
    },
    new()
    {
      Code = "rice",
      Grain = "game:grain-rice",
      Flour = "game:flour-rice",
    },
    new()
    {
      Code = "flax",
      Grain = "game:grain-flax",
      Flour = "game:flour-flax",
    },
    new()
    {
      Code = "rye",
      Grain = "game:grain-rye",
      Flour = "game:flour-rye",
      Seconds = 8,
    },
    new()
    {
      Code = "amaranth",
      Grain = "game:grain-amaranth",
      Flour = "game:flour-amaranth",
    },
    new()
    {
      Code = "sunflower",
      Grain = "game:grain-sunflower",
      Flour = "game:flour-sunflower",
    },
  ];

  [Fact]
  public void Emits_one_sack_per_grain_coded_and_shaped() {
    var defs = GrainSackItems.Emit("grains", Six).ToList();

    Assert.Equal(6, defs.Count);
    Assert.Equal(
      [
        "sack-spelt",
        "sack-rice",
        "sack-flax",
        "sack-rye",
        "sack-amaranth",
        "sack-sunflower",
      ],
      defs.Select(d => d.Code)
    );
    Assert.All(defs, d => Assert.Contains("linensack", d.ToJson().ToString()));
  }
}
