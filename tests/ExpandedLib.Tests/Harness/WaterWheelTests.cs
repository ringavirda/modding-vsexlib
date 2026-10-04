#if GAME_GE_1_22
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Vintagestory.GameContent.Mechanics;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>A vanilla waterwheel over the game's own water blocks, held in the fluid layer, driving
/// a vanilla axle through a started <see cref="MechanicalPowerMod"/>.</summary>
public class WaterWheelTests {
  private static BlockPos Hub => new(10, 10, 10);

  private static BlockPos AxlePos => Hub.SouthCopy();

  /// <summary>The speed vanilla's <c>MechanicalNetwork.updateNetwork</c> holds a network with no free
  /// torque at, rad per tick-step.</summary>
  private const float IdleSpeed = 0.000001f;

  // Fails when the world drops the tick listener a mod system registers through World, or when the
  // accessor's fluid-layer read answers the solid layer.
  [Fact]
  public void A_wheel_in_water_flowing_above_its_minimum_turns_its_line() {
    using TestWorld world = Wheel("rapidwater-e-7");
    Assert.True(
      FlowSpeed(world, "rapidwater-e-7") > MinFlowSpeed(world),
      "rapid water flows faster than the wheel's minimum"
    );

    Run(world, 20);

    BEBehaviorMPBase wheel = Mp(world, Hub);
    BEBehaviorMPBase axle = Mp(world, AxlePos);
    Assert.Same(wheel.Network, axle.Network);
    Assert.InRange(axle.Network.Speed, 0.29f, 0.31f);
    Assert.Equal(1f, wheel.GearedRatio);
  }

  // Fails when the still variant's water is placed as its flowing east variant.
  [Fact]
  public void A_wheel_in_still_water_stays_at_rest() {
    using TestWorld world = Wheel("rapidwater-still-7");

    Run(world, 20);

    BEBehaviorMPBase axle = Mp(world, AxlePos);
    Assert.NotNull(axle.Network);
    Assert.InRange(axle.Network.Speed, 0f, IdleSpeed);
  }

  // Fails when the slow water is placed as rapid water.
  [Fact]
  public void A_wheel_in_water_flowing_below_its_minimum_stays_at_rest() {
    using TestWorld world = Wheel("water-e-7");
    Assert.True(FlowSpeed(world, "water-e-7") <= MinFlowSpeed(world));

    Run(world, 20);

    Assert.InRange(Mp(world, AxlePos).Network.Speed, 0f, IdleSpeed);
  }

  // Fails when the accessor's SetBlock into the fluid layer writes nothing.
  [Fact]
  public void A_turning_wheel_slows_the_rapid_water_leaving_it() {
    using TestWorld world = Wheel("rapidwater-e-7");
    BlockPos exit = Hub.AddCopy(2, -1, 0);

    Run(world, 2);

    Assert.Equal(
      "game:water-e-7",
      world.GetBlock(exit, BlockLayersAccess.Fluid).Code.ToString()
    );
  }

  /// <summary>A built north-side wheel at <see cref="Hub"/> on an axle south of it, over a channel
  /// of <paramref name="water"/> along X under the wheel and two cells past it each way.</summary>
  private static TestWorld Wheel(string water) {
    var world = new TestWorld();
    world.RegisterVanillaClasses();
    var power = new MechanicalPowerMod();
    world.Mods.Register(power);
    power.Start(world.Api);
    world.LoadVanilla(["water", "rapidwater", "waterwheel", "axle"], []);

    Block waterBlock = Get(world, water);
    for (int dx = -2; dx <= 2; dx++)
      world.Place(Hub.AddCopy(dx, -1, 0), waterBlock);
    world.Accessor.SetBlock(Get(world, "woodenaxle-ns").BlockId, AxlePos);
    world.Accessor.SetBlock(Get(world, "waterwheel-3m-north").BlockId, Hub);

    var construction = world
      .GetBlockEntity(Hub)!
      .GetBehavior<BEBehaviorRightClickConstructable>();
    var tree = new TreeAttribute();
    tree.SetInt("currentStage", construction.Stages);
    construction.FromTreeAttributes(tree, world.World);
    Assert.True(construction.IsComplete);
    return world;
  }

  /// <summary>Runs every listener <paramref name="seconds"/> seconds in 20 ms steps, the interval of
  /// the mechanical power tick.</summary>
  private static void Run(TestWorld world, int seconds) {
    for (int i = 0; i < seconds * 50; i++)
      world.AdvanceBlockEntityTime(20);
  }

  private static Block Get(TestWorld world, string code) =>
    world.World.GetBlock(new AssetLocation("game", code));

  private static BEBehaviorMPBase Mp(TestWorld world, BlockPos pos) =>
    world.GetBlockEntity(pos)!.GetBehavior<BEBehaviorMPBase>();

  private static float FlowSpeed(TestWorld world, string code) =>
    Get(world, code).Attributes?["flowSpeed"].AsFloat(0) ?? 0;

  private static float MinFlowSpeed(TestWorld world) =>
    (float)
      ReflectionHelpers.GetField(
        world.GetBlockEntity(Hub)!.GetBehavior<BEBehaviorMPWaterWheel>(),
        "requiresMinFlowSpeed"
      )!;
}
#endif
