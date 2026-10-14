#if GAME_GE_1_22
using ExpandedLib.Scenarios;
using ExpandedLib.Testing;
using ExpandedLib.Tests.Setups;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests.Scenarios.Live;

/// <summary>A molten run recorded through the live view, whose plot origin is not zero.</summary>
public class MoltenRunLiveTests
{
  private sealed class Entered : Reading
  {
    public override object Read(ReadingContext ctx) => 0f;
  }

  // Fails when RecordedMoltenRun.Cells hands the network's absolute node positions to MoltenCellAt,
  // which applies the plot origin a second time, so the cell reads as holding nothing.
  [Fact]
  public void A_molten_run_on_a_plot_with_an_origin_reads_its_cell()
  {
    var fixture = new LiveFixture();
    fixture.Test.RegisterNetwork("molten", s => new StubNetwork(s, "molten"));
    BlockPos at = fixture.Context.Setup.Absolute(new BlockPos(1, 0, 2));
    var cell = new SetupMoltenEntity
    {
      CellAmount = 37,
      CellMetalType = "game:ingot-copper",
      CellTemperature = 1090f,
    };
    fixture.Test.Attach(cell);
    fixture.Test.Place(
      at,
      TestBlocks.Configure(
        new Vintagestory.API.Common.Block(),
        "test:canal",
        5
      ),
      cell
    );
    fixture.Test.AddNode(at, "molten");
    SetupRecording recording = new("setup", "1.22", [("exlib", "0.9.0")], 6);
    recording.MoltenRun("canal", new BlockPos(1, 0, 2), new Entered());
    var ctx = new ReadingContext(fixture.Context.Setup);
    recording.Start(ctx);
    for (int i = 0; i < 6; i++)
      recording.Sample(recording.Samples, ctx);

    var canal = System.Text.Json.Nodes.JsonNode.Parse(recording.Text())![
      "runs"
    ]!["canal"]!;

    Assert.Equal(
      "[[0,\"molten-copper\"]]",
      canal["series"]!["medium"]!.ToJsonString()
    );
    Assert.Equal(
      "[[0,1090.0]]",
      canal["series"]!["temperature"]!.ToJsonString()
    );
    Assert.Equal("[[0,37.00]]", canal["series"]!["volume"]!.ToJsonString());
  }
}
#endif
