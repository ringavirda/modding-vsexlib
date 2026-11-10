#if GAME_GE_1_22
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ExpandedLib.Industry.MechanicalPower;
using ExpandedLib.Networks;
using ExpandedLib.Scenarios;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests.Setups;

internal sealed class SetupMpMember : BEBehaviorNetworkMember
{
  public SetupMpMember(BlockEntity be, string connectors)
    : base(be)
  {
    NetworkType = "mpshaft";
    DeclareConnectors(connectors);
  }
}

internal sealed class SetupMpLinkMember
  : BEBehaviorNetworkMember,
    INetworkMember
{
  public SetupMpLinkMember(BlockEntity be)
    : base(be)
  {
    NetworkType = "mpshaft";
    DeclareConnectors("ws");
  }

  IReadOnlyList<byte> INetworkMember.Slots(
    IBlockAccessor world,
    BlockPos pos
  ) => [NodeKey.PortBase, NodeKey.PortBase + 1];

  IEnumerable<NodeCrossing> INetworkMember.Crossings(
    IBlockAccessor world,
    NodeKey key
  )
  {
    yield return new NodeCrossing(
      key.Slot == NodeKey.PortBase ? BlockFacing.WEST : BlockFacing.SOUTH
    );
  }
}

internal sealed class SetupMpNode
  : BlockEntity,
    IMpEnergyStorage,
    IMpEnergyConsumer
{
  public float Load;

  public float Inertia => 10f;

  public float LoadTorque(float speed) => Load;
}

internal sealed class SetupMpLink : BlockEntity;

internal sealed class SetupMpMark : Block
{
  public override void OnBlockBroken(
    IWorldAccessor world,
    BlockPos pos,
    IPlayer byPlayer,
    float dropQuantityMultiplier = 1
  )
  {
    var marked = new ItemStack(this);
    marked.Attributes.SetString("hook", "broken");
    world.SpawnItemEntity(marked, pos.ToVec3d());
    base.OnBlockBroken(world, pos, byPlayer, dropQuantityMultiplier);
  }
}

/// <summary>What the <c>power</c> step on an <c>mpshaft</c> run, the <c>break</c> step by the world and the
/// per-cell torque readings of the <c>network</c> reading do in the harness (setups.md, What the harness runs).</summary>
[Collection("Setups")]
public class SetupMpShaftTests : IDisposable
{
  public void Dispose() => SetupPlacers.Clear();

  private static readonly string FixturePath = Path.Combine(
    RepoPaths.Root,
    "tests",
    "ExpandedLib.Tests",
    "Setups",
    "Fixtures",
    "SetupMp"
  );

  private static SetupFile Inline(string steps) =>
    SetupFile.Parse(
      "{\"setup\": \"t\", \"description\": \"d\", \"mods\": [\"setupmp\"], \"steps\": "
        + steps
        + ", \"seconds\": 10, \"record\": {}}",
      "t.json"
    );

  private static SetupScene Build(string steps) =>
    SetupScene.Build(
      Inline(steps),
      [FixturePath],
      null,
      w =>
        SetupTickOrderTests
          .RegisterTick(w, [])
          .RegisterClass("SetupMpMark", typeof(SetupMpMark))
          .RegisterClass("SetupMpNode", typeof(BlockEntity))
          .RegisterClass("SetupMpLink", typeof(BlockEntity))
          .RegisterBlockEntityFactory(
            "SetupMpNode",
            () =>
            {
              var be = new SetupMpNode();
              be.Behaviors.Add(new SetupMpMember(be, "nesw"));
              return be;
            }
          )
          .RegisterBlockEntityFactory(
            "SetupMpLink",
            () =>
            {
              var be = new SetupMpLink();
              be.Behaviors.Add(new SetupMpLinkMember(be));
              be.Behaviors.Add(new BEBehaviorMpClutch(be) { Engaged = true });
              return be;
            }
          )
    );

  private static string Node(int x, int z = 0) =>
    $"{{\"place\": {{\"code\": \"setupmp:node\", \"at\": [{x}, 0, {z}]}}}}";

  private static string Load(int x, int z, int nm) =>
    $"{{\"set\": {{\"at\": [{x}, 0, {z}], \"member\": {{\"Load\": {nm}}}}}}}";

  private static string Read(int x, int z, string member, string test) =>
    $"{{\"expect\": {{\"at\": [{x}, 0, {z}], \"network\": \"mpshaft\", \"member\": \"{member}\", {test}}}}}";

  private const string Source =
    "{\"power\": {\"at\": [0, 0, 0], \"network\": \"mpshaft\", \"speed\": 10, \"torque\": 100}}";

  private static string Message(string steps) =>
    Assert
      .Throws<InvalidOperationException>(() => Build(steps).Dispose())
      .Message;

  // Fails when the source's torque is ignored: the loaded line settles at the load's speed with no torque on its joints.
  [Fact]
  public void A_power_step_drives_the_line_at_its_torque_and_speed()
  {
    string line =
      "["
      + Node(0)
      + ", "
      + Node(1)
      + ", "
      + Node(2)
      + ", "
      + Load(2, 0, 40)
      + ", "
      + Source
      + ", {\"wait\": 40}, ";

    using SetupScene scene = Build(
      line
        + Read(1, 0, "NodeTorqueNm()", "\"min\": 39.5, \"max\": 40.5")
        + ", "
        + Read(1, 0, "Speed", "\"min\": 5.9, \"max\": 6.1")
        + "]"
    );

    Assert.NotNull(scene);
  }

  // Fails when a load past the source's stall torque still reads as the stall torque, or the source is not a limit.
  [Fact]
  public void A_power_step_torque_is_the_most_the_line_can_carry()
  {
    string message = Message(
      "["
        + Node(0)
        + ", "
        + Node(1)
        + ", "
        + Load(1, 0, 400)
        + ", "
        + Source
        + ", {\"wait\": 20}, "
        + Read(0, 0, "NodeTorqueNm()", "\"min\": 101")
        + "]"
    );

    Assert.Contains("NodeTorqueNm()", message);
  }

  // Fails when the node reading takes the group's torque rather than the joints of the cell asked about.
  [Fact]
  public void A_node_reading_is_the_torque_of_its_own_cell()
  {
    string line =
      "["
      + Node(0)
      + ", "
      + Node(1)
      + ", "
      + Node(2)
      + ", "
      + Load(1, 0, 25)
      + ", "
      + Load(2, 0, 40)
      + ", "
      + Source
      + ", {\"wait\": 40}, ";

    using SetupScene scene = Build(
      line
        + Read(0, 0, "NodeTorqueNm()", "\"min\": 64.5, \"max\": 65.5")
        + ", "
        + Read(2, 0, "NodeTorqueNm()", "\"min\": 39.5, \"max\": 40.5")
        + "]"
    );

    Assert.NotNull(scene);
  }

  // Fails when the link reading is the run's total rather than the torque the link carries.
  [Fact]
  public void A_link_reading_is_the_torque_the_link_carries()
  {
    string line =
      "["
      + Node(0)
      + ", {\"place\": {\"code\": \"setupmp:link\", \"at\": [1, 0, 0]}}, "
      + Node(1, 1)
      + ", "
      + Load(0, 0, 20)
      + ", "
      + Load(1, 1, 30)
      + ", "
      + Source
      + ", {\"wait\": 60}, ";

    using SetupScene scene = Build(
      line + Read(1, 0, "CarriedNm()", "\"min\": 29, \"max\": 31") + "]"
    );

    Assert.NotNull(scene);
  }

  // Fails when a break by the world removes the cell without the block's own hook, so no drop is spawned.
  [Fact]
  public void A_break_step_by_the_world_runs_the_blocks_break_hook()
  {
    using SetupScene scene = Build(
      "[{\"place\": {\"code\": \"setupmp:mark\", \"at\": [0, 0, 0]}}, {\"break\": {\"at\": [0, 0, 0], \"by\": \"world\"}}]"
    );

    Assert.Equal(0, scene.World.GetBlock(new BlockPos(0, 0, 0)).Id);
    Assert.Contains(
      scene.World.Drops,
      s => s.Attributes.GetString("hook") == "broken"
    );
  }

  public static TheoryData<string, string?> Lacked =>
    new()
    {
      {
        "{\"power\": {\"at\": [0, 0, 0], \"speed\": 1, \"torque\": 1}}",
        "power"
      },
      {
        "{\"power\": {\"at\": [0, 0, 0], \"network\": \"pipe\", \"speed\": 1, \"torque\": 1}}",
        "power"
      },
      { Source, null },
      { "{\"break\": {\"at\": [0, 0, 0]}}", "break" },
      { "{\"break\": {\"at\": [0, 0, 0], \"by\": \"player\"}}", "break" },
      { "{\"break\": {\"at\": [0, 0, 0], \"by\": \"world\"}}", null },
      {
        "{\"break\": {\"at\": [0, 0, 0], \"by\": \"world\", \"sneak\": true}}",
        "break"
      },
      {
        "{\"break\": {\"at\": [0, 0, 0], \"by\": \"world\", \"take\": true}}",
        "break"
      },
      {
        "{\"break\": {\"at\": [0, 0, 0], \"by\": \"world\", \"with\": {\"stack\": \"setupmp:mark\"}}}",
        "break"
      },
    };

  // Fails when the harness runs a power step with no mpshaft network, a break by the player, or a break
  // by the world with one of with, sneak or take, instead of leaving the file to the live runner.
  [Theory]
  [MemberData(nameof(Lacked))]
  public void Only_the_steps_the_fake_world_can_run_stay_in_the_harness(
    string step,
    string? verb
  ) =>
    Assert.Equal(
      verb,
      SetupScene.LackedVerb(
        SetupFile.Parse(
          "{\"setup\": \"t\", \"description\": \"d\", \"mods\": [\"setupmp\"], \"steps\": ["
            + step
            + "], \"seconds\": 10, \"record\": {}}",
          "t.json"
        )
      )
    );

  // Fails when the harness lets a power step with a face through, or a speed or torque that is no number.
  [Theory]
  [InlineData(
    "\"face\": \"n\", \"speed\": 10, \"torque\": 100",
    "\"face\" is not built"
  )]
  [InlineData("\"speed\": 0, \"torque\": 100", "\"speed\" is above 0")]
  [InlineData("\"speed\": 10, \"torque\": -1", "\"torque\" is a number")]
  [InlineData("\"speed\": 10", "\"torque\" is a number")]
  public void A_power_step_is_refused_for_what_it_cannot_stand(
    string body,
    string reason
  ) =>
    Assert.Contains(
      reason,
      Message(
        "["
          + Node(0)
          + ", {\"power\": {\"at\": [0, 0, 0], \"network\": \"mpshaft\", "
          + body
          + "}}]"
      )
    );
}
#endif
