using ExpandedLib.Industry.MechanicalPower;
using ExpandedLib.Networks;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// Covers the "mpshaft" node store's implicit group step and Coulomb rest:
/// <see cref="MpShaftStore.Step"/>.
/// </summary>
public class MpShaftStepTests {
  private const float MaxSpeed = 100f;

  private static NodeKey Key(int index) =>
    NodeKey.Centre(new BlockPos(index, 0, 0));

  private sealed class BareProducer : IMpEnergyProducer {
    public float DriveTorque(float speed) => 1f;
  }

  private sealed class BareConsumer : IMpEnergyConsumer {
    public float LoadTorque(float speed) => 1f;
  }

  [Fact]
  public void Step_ContractDefaultsAreZero() {
    IMpEnergyProducer producer = new BareProducer();
    IMpEnergyConsumer consumer = new BareConsumer();

    Assert.Equal(0f, producer.DriveSlope(3f));
    Assert.Equal(0f, consumer.LoadSlope(3f));
    Assert.Equal(0f, consumer.StandingNm);
  }

  [Fact]
  public void Step_ShaftOnlyRunIsStable() {
    var store = new MpShaftStore();
    int group = store.AddGroup();
    int node = store.AddNode(Key(0), group, 1.8f, 0f);
    const float target = 2f;
    const float stiffness = 8000f;
    float previous = 0f;

    for (int step = 0; step < 20; step++) {
      float w = store.NodeSpeed(node);
      store.SetDrive(node, stiffness * (target - w), -stiffness);
      store.Step(0.2f, MaxSpeed);
      float next = store.NodeSpeed(node);
      Assert.InRange(next, previous, target + 1e-3f);
      previous = next;
    }

    Assert.Equal(target, previous, 3);
  }

  [Fact]
  public void Step_CoulombHoldsAtRest() {
    var store = new MpShaftStore();
    int group = store.AddGroup();
    int node = store.AddNode(Key(0), group, 5f, 40f);

    for (int step = 0; step < 10; step++) {
      store.SetDrive(node, 39f);
      store.Step(0.2f, MaxSpeed);
    }

    Assert.Equal(0f, store.Speed(group));

    store.SetDrive(node, 60f);
    store.Step(0.2f, MaxSpeed);

    Assert.Equal(0.2f * (60f - 40f) / 5f, store.Speed(group), 5);
  }

  [Fact]
  public void Step_StalledLoadNeverReverses() {
    var store = new MpShaftStore();
    int group = store.AddGroup(2f);
    int node = store.AddNode(Key(0), group, 5f, 10f);

    for (int step = 0; step < 30; step++) {
      store.SetLoad(node, 100f);
      store.Step(0.2f, MaxSpeed);
      Assert.True(store.Speed(group) >= 0f);
    }

    Assert.Equal(0f, store.Speed(group));
  }

  [Theory]
  [InlineData(50f)]
  [InlineData(-50f)]
  public void Step_GearLossFollowsPowerFlow(float carriedNm) {
    var store = new MpShaftStore();
    int group = store.AddGroup(4f);
    int flywheel = store.AddNode(Key(0), group, 10f, 0f);
    int station = store.AddNode(Key(1), group, 0f, 0f);
    store.SetMesh(station, 0.98f, carriedNm);

    store.Step(0.2f, MaxSpeed);

    Assert.Equal(4f - 0.2f * (0.02f * 50f) / 10f, store.Speed(group), 5);
    Assert.Equal(store.Speed(group), store.NodeSpeed(flywheel));
  }

  [Fact]
  public void Step_InertiaReflectsAcrossRatio() {
    var store = new MpShaftStore();
    int group = store.AddGroup();
    int fast = store.AddNode(Key(0), group, 1f, 0f);
    store.AddNode(Key(1), group, 3f, 0f, 2f);
    store.SetDrive(fast, 10f);

    store.Step(0.2f, MaxSpeed);

    Assert.Equal(0.2f * 10f / (1f + 3f * 4f), store.Speed(group), 5);
  }

  [Fact]
  public void Step_ClampsToMaxSpeed() {
    var store = new MpShaftStore();
    int group = store.AddGroup();
    int node = store.AddNode(Key(0), group, 1f, 0f);
    store.SetDrive(node, 1000f);

    store.Step(1f, 2f);

    Assert.Equal(2f, store.Speed(group));
  }
}
