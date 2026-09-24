using System.Linq;
using ExpandedLib.Checks;
using ExpandedLib.Structures;
using ExpandedLib.Testing;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary><see cref="BlockSignalCensus"/> over planted blocks.</summary>
public class BlockSignalCensusTests {
  private static Block Host(string type, string side, int id) {
    Block host = TestBlocks.Configure(
      new BlockFilledMegastructure(),
      $"{type}-{side}",
      id,
      ("side", side)
    );
    host.Attributes = new JsonObject(
      JToken.Parse("""{ "fillerOffsets": [ { "x": 1, "y": 0, "z": 0 } ] }""")
    );
    return host;
  }

  // Fails when a signal counts every block read rather than the blocks carrying it, or counts
  // variants as blocktypes.
  [Fact]
  [PlantedDefect(typeof(BlockSignalCensus), nameof(BlockSignalCensus.Run))]
  public void A_signal_counts_its_blocktypes_and_variants_apart() {
    TestModDomain.Register();
    var world = new TestWorld();
    world.Register(Host("exlib:probe", "north", 1));
    world.Register(Host("exlib:probe", "south", 2));
    world.Register(TestBlocks.Configure(new Block(), "exlib:plain", 3));
    world.Register(Host("game:other", "north", 4));

    BlockSignalCensus.Result census = BlockSignalCensus.Run(world, "exlib");

    Assert.Equal(3, census.Blocks);
    Assert.Equal(2, census.Blocktypes);
    Assert.Equal(
      new BlockSignalCensus.Count("footprint", 1, 2),
      census["footprint"]
    );
    Assert.Equal(new BlockSignalCensus.Count("layout", 0, 0), census["layout"]);
    Assert.Equal(BlockSignals.AllNames(), census.Counts.Select(c => c.Signal));
  }

  // Fails when the stood-up world skips exlib's structure filler, or the filler, which answers
  // for mechanical power only through a hosted behaviour, counts as a connector.
  [Fact]
  public void A_world_stood_up_from_definitions_holds_the_structure_filler() {
    BlockSignalCensus.Result census = BlockSignalCensus.Run(
      "exlib",
      [],
      [typeof(ExpandedLibModSystem).Assembly]
    );

    Assert.Equal(1, census.Blocks);
    Assert.Equal(1, census["entity class"].Blocks);
    Assert.Equal(0, census["mp connector"].Blocks);
  }
}
