using ExpandedLib.Blocks;
using ExpandedLib.Machines;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>Pins <see cref="BlockEntityMachineStation"/>'s <c>ToTreeAttributes</c>/<c>FromTreeAttributes</c>
/// pair, carrying a declared field alongside vanilla's own inventory save.</summary>
public class MachineStationStateTests {
  private sealed class StatefulStation : BlockEntityMachineStation {
    public int Setting;

    protected override MachineSlotSpec[] SlotSpecs => [];

    public override string InventoryClassName => "test-station";

    protected override void DeclareState(ExBlockState state) =>
      state.Int("setting", () => Setting, v => Setting = v);
  }

  [Fact]
  public void A_field_declared_through_State_round_trips() {
    // ToTreeAttributes reads Block.IsMissing before it reaches Persisted; the station needs a real block.
    var source = new StatefulStation {
      Setting = 3,
      Pos = new BlockPos(0, 0, 0),
      Block = TestBlocks.Configure(new Block(), "test:station", 1),
    };
    var tree = new TreeAttribute();
    source.ToTreeAttributes(tree);

    var target = new StatefulStation();
    target.FromTreeAttributes(tree, new TestWorld().World);

    Assert.Equal(3, target.Setting);
  }
}
