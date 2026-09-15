using ExpandedLib.Definitions;
using ExpandedLib.Structures;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// Declarative filler ports: a footprint cell can carry a passive network port (a face plus a
/// network type), replacing the old imperative <c>BlockBoiler.MarkSteamPort</c> pattern.
/// </summary>
public class FillerPortTests {
  [Fact]
  public void PortGlyphCarriesFaceAndNetwork() {
    var cells = StructureFootprint.Layout(f =>
      f.Origin(-1, 0)
        .Port('S', BlockFacing.UP, "pipe")
        .Layer(
          0,
          """
          # O #
          # S #
          """
        )
    );

    FillerCellSpec port = Assert.Single(cells, c => c.PortFace != null);
    Assert.Equal(0, port.X);
    Assert.Equal(1, port.Z);
    Assert.Equal("u", port.PortFace);
    Assert.Equal("pipe", port.PortNetworkType);
    Assert.False(port.AllowAttach);
  }

  [Fact]
  public void PlainGlyphCarriesNoPort() {
    var cells = StructureFootprint.Layout(f =>
      f.Origin(-1, 0)
        .Layer(
          0,
          """
          # O #
          """
        )
    );
    Assert.All(cells, c => Assert.Null(c.PortFace));
  }

  [Fact]
  public void PortSurvivesDefinitionRoundTrip() {
    JObject json = ExBlockDef
      .Create("exlib", "porttest", "porttest")
      .FillerOffsets(
        StructureFootprint.Layout(f =>
          f.Origin(-1, 0)
            .Port('S', BlockFacing.UP, "pipe")
            .Layer(
              0,
              """
              # O #
              # S #
              """
            )
        )
      )
      .ToJson();

    var offsetsNode = new JsonObject((JObject)json["attributes"]!)[
      "fillerOffsets"
    ];
    var cells = StructureFillers.ReadOffsets(offsetsNode);
    var port = Assert.Single(cells, c => c.PortFace == "u");
    Assert.Equal("pipe", port.PortNetworkType);
  }

  // UP is vertical and rotation-invariant; this drives a horizontal port face through every quarter turn.
  [Theory]
  [InlineData(0, "e")]
  [InlineData(90, "n")]
  [InlineData(180, "w")]
  [InlineData(270, "s")]
  public void PortFaceRotatesWithTheStructureAngle(int angle, string expected) {
    var host = new Host(
      Offsets(
        "[{ \"x\": 1, \"y\": 0, \"z\": 0, \"portFace\": \"e\", \"portNetwork\": \"pipe\" }]"
      )
    );

    var cell = Assert.Single(
      StructureFillers.FootprintCells(host, new BlockPos(0, 0, 0), angle)
    );
    Assert.Equal(expected, cell.PortFace);
    Assert.Equal("pipe", cell.PortNetworkType);
  }

  private static JsonObject Offsets(string json) => new(JArray.Parse(json));

  private sealed class Host(JsonObject? offsets) : IFillerHost {
    public JsonObject? FillerOffsets { get; } = offsets;
  }
}
