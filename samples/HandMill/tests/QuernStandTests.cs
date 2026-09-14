using System.IO;
using ExpandedLib.Structures;
using ExpandedLib.Testing;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;

namespace HandMill.Tests;

/// <summary>
/// The quern stand: the JSON-only rung. Its declared empty <c>fillerOffsets</c> stays empty, and it
/// completes once a quern sits above it.
/// </summary>
public class QuernStandTests {
  private static JObject ShippedJson() =>
    JObject.Parse(
      File.ReadAllText(
        Path.Combine(
          RepoPaths.Assets("handmill"),
          "blocktypes",
          "quernstand.json"
        )
      )
    );

  [Fact]
  public void Resolves_to_the_zero_config_classes() {
    JObject json = ShippedJson();
    Assert.Equal("ExFilledMegastructure", (string)json["class"]!);
    Assert.Equal("ExMultiblock", (string)json["entityClass"]!);
  }

  private static Block Stand() {
    var block = TestBlocks.Configure(
      new BlockFilledMegastructure(),
      "handmill:quernstand-north",
      1
    );
    block.Attributes = new JsonObject((JObject)ShippedJson()["attributes"]!);
    return block;
  }

  [Fact]
  public void The_declared_empty_fillerOffsets_stays_empty() {
    var world = new TestWorld();
    Block stand = Stand();
    stand.OnLoaded(world.Api);

    Assert.Empty(StructureFillers.ReadOffsets(stand.Attributes?["fillerOffsets"]));
  }

  [Fact]
  public void A_rig_completes_once_a_quern_stands_on_it() {
    var world = new TestWorld();
    Block stand = Stand();
    stand.OnLoaded(world.Api);

    var be = new BlockEntityMultiblock();
    var pos = new BlockPos(0, 10, 0);
    world.Place(pos, stand, be);
    world.Initialize(be);

    world.AdvanceBlockEntityTime(3000);
    Assert.False(be.StructureComplete);

    Block quern = TestBlocks.Configure(new Block(), "game:quern-granite", 2000);
    world.Place(pos.AddCopy(0, 1, 0), quern);

    world.AdvanceBlockEntityTime(3000);
    Assert.True(be.StructureComplete);
  }
}
