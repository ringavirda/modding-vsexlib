#if GAME_GE_1_22
using System.Linq;
using ExpandedLib.Structures;
using ExpandedLib.Testing;
using ExpandedLib.Tests.Scenarios.Live;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests.Setups;

/// <summary>The frame a setup scene records for a host that is not a megablock matches the live
/// scenario frame of the same host (setups.md, Placement kinds).</summary>
[Collection("Setups")]
public class SetupAngleTests
{
  private sealed class RolledHost : Block, IFillerHost
  {
    public JsonObject? FillerOffsets => Attributes?["fillerOffsets"];

    public int StructureAngle => 90;
  }

  // Fails when AngleOf reads AngleFromSide of a non-megablock host instead of its StructureAngle,
  // so the harness frames it at 0 where live frames it at 90.
  [Fact]
  public void A_host_that_is_not_a_megablock_frames_at_its_own_structure_angle()
  {
    var fixture = new LiveFixture();
    fixture.Test.Filler.EntityClass = "structurefiller";
    RolledHost host = TestBlocks.Configure(
      new RolledHost
      {
        Attributes = new JsonObject(
          JToken.Parse(
            "{ \"fillerOffsets\": [ { \"x\": 0, \"y\": 0, \"z\": -1 } ] }"
          )
        ),
      },
      "exlib:rolledhost-north",
      25,
      ("side", "north")
    );
    fixture.Test.Register(fixture.Test.Filler).Register(host);
    var at = new BlockPos(0, 0, 1);

    using SetupScene scene = SetupScene.Over(fixture.Test, "angle");
    scene.PlaceAt("host", host, fixture.Context.Setup.Absolute(at));
    var harness = scene.Placements["host"].Frame!;
    var live = fixture.Context.Setup.FrameAt(at)!;

    Assert.Equal(90, live.Angle);
    Assert.Equal(live.Angle, harness.Angle);
    Assert.Equal(
      live.Footprint.Select(c => fixture.Context.Setup.Absolute(c.Pos)),
      harness.Footprint.Select(c => c.Pos)
    );
  }
}
#endif
