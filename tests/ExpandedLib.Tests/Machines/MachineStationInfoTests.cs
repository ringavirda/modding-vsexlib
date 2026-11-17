using System.Text;
using ExpandedLib.Machines;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>Pins <see cref="BlockEntityMachineStation"/>'s block info: the behaviours' lines
/// without vanilla's stored-food perish line.</summary>
public class MachineStationInfoTests {
  private sealed class Station : BlockEntityMachineStation {
    protected override MachineSlotSpec[] SlotSpecs => [];

    public override string InventoryClassName => "test-station";
  }

  private sealed class LineBehavior(BlockEntity be) : BlockEntityBehavior(be) {
    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc) =>
      dsc.AppendLine("behaviour line");
  }

  // Fails when GetBlockInfo calls base, whose container adds the "Stored food perish speed" line.
  [Fact]
  public void Block_info_carries_a_behaviour_line_and_no_food_perish_line() {
    TestWorld world = new();
    TestLang.Init();
    var station = new Station {
      Pos = new BlockPos(0, 0, 0, 0),
      Block = TestBlocks.Configure(new Block(), "test:infostation", 5101),
    };
    station.Behaviors.Add(new LineBehavior(station));
    station.Initialize(world.Api);
    var sb = new StringBuilder();

    station.GetBlockInfo(world.Player().Player, sb);

    Assert.Contains("behaviour line", sb.ToString());
    Assert.DoesNotContain("perish", sb.ToString());
  }
}
