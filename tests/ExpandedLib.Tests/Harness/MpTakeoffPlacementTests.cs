#if GAME_GE_1_22
using ExpandedLib.Industry.MechanicalPower;
using ExpandedLib.Structures;
using ExpandedLib.Testing;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent.Mechanics;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>A vanilla axle placed beside a standing take-off cell, through a started
/// <see cref="MechanicalPowerMod"/>: it joins the take-off's network and turns with it.</summary>
[Collection(ExHarmonyCollection.Name)]
public class MpTakeoffPlacementTests
{
  private sealed class Port(BlockEntity be)
    : BEBehaviorMpTakeoff(be),
      IFillerHostedBehavior
  {
    public void ConfigureFromFiller(
      BlockPos? principal,
      BlockFacing? connectorFace,
      JsonObject? properties
    )
    {
      if (connectorFace != null)
        Configure(3f, 3280f, connectorFace, connectorFace.Opposite);
    }
  }

  private static readonly BlockPos PortPos = new(10, 10, 10);

  private static TestWorld Standing(BlockFacing face)
  {
    var world = new TestWorld();
    world.RegisterVanillaClasses();
    var power = new MechanicalPowerMod();
    world.Mods.Register(power);
    power.Start(world.Api);
    world.LoadVanilla(["axle"], []);
    world.RegisterBlockEntityBehaviorFactory("test.Port", be => new Port(be));
    world.PlaceFiller(
      PortPos,
      [
        new FillerBehavior(
          "test.Port",
          face,
          new JsonObject(JObject.Parse("{}"))
        ),
      ],
      PortPos
    );
    return world;
  }

  // Fails when the filler answers vanilla's axle, asking from the axle's own cell after a neighbour changed, from that cell
  // and not the take-off's: the axle then reads no connector, breaks, and the network holds one node.
  [Theory]
  [InlineData("north")]
  [InlineData("south")]
  [InlineData("east")]
  [InlineData("west")]
  public void An_axle_placed_after_the_block_joins_its_network_and_turns(
    string discovery
  )
  {
    BlockFacing discoveryFace = BlockFacing.FromCode(discovery)!;
    foreach (
      BlockFacing face in new[] { discoveryFace, discoveryFace.Opposite }
    )
    {
      using TestWorld world = Standing(discoveryFace);
      var link = (BEBehaviorMpTakeoff)
        world.GetBlockEntity(PortPos)!.GetBehavior<BEBehaviorMPBase>();
      Assert.NotNull(link.Network);
      BlockPos axlePos = PortPos.AddCopy(face);
      string code = face.Axis == EnumAxis.X ? "woodenaxle-we" : "woodenaxle-ns";
      Block axle = world.World.GetBlock(new AssetLocation("game", code));

      bool placed = axle.TryPlaceBlock(
        world.World,
        world.Player().Player,
        new ItemStack(axle),
        new BlockSelection { Position = axlePos, Face = face },
        ref code
      );

      Assert.True(placed, face.Code);
      var mp = world.GetBlockEntity(axlePos)!.GetBehavior<BEBehaviorMPBase>();
      Assert.NotNull(mp.Network);
      Assert.Same(link.Network, mp.Network);
      axle.OnNeighbourBlockChange(
        world.World,
        axlePos,
        axlePos.AddCopy(face.Opposite)
      );
      Assert.Equal(axle.BlockId, world.Accessor.GetBlock(axlePos).BlockId);
      for (int i = 0; i < 500; i++)
      {
        if (i % 10 == 0)
          link.LoadTorque(2.24f);
        world.AdvanceBlockEntityTime(20);
      }
      Assert.InRange(mp.Network.Speed, 0.13f, 0.16f);
    }
  }
}
#endif
