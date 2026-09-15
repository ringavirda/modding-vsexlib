using System;
using ExpandedLib.Blocks;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// Tests <see cref="ExBlockEntityBehavior"/>'s <c>[Persist]</c>/<c>DeclareState</c> support,
/// sharing its host's flat attribute tree and key space.
/// </summary>
public class ExBlockEntityBehaviorTests {
  private sealed class AttributedBehavior(BlockEntity be)
    : ExBlockEntityBehavior(be) {
    [Persist("attrValue")]
    public int Value;
  }

  private sealed class DeclaredBehavior(BlockEntity be)
    : ExBlockEntityBehavior(be) {
    public float Level;

    protected override void DeclareState(ExBlockState state) =>
      state.Float("level", () => Level, v => Level = v);
  }

  private sealed class DuplicateKeyBehavior(BlockEntity be)
    : ExBlockEntityBehavior(be) {
    [Persist("shared")]
    public int A;

    protected override void DeclareState(ExBlockState state) =>
      state.Int("shared", () => A, v => A = v);
  }

  private sealed class Host : ExBlockEntity {
    [Persist("hostValue")]
    public int HostValue;

    protected override void DeclareState(ExBlockState state) { }
  }

  private static Host NewHost() {
    var host = new Host();
    host.Pos = new BlockPos(0, 0, 0);
    host.Block = TestBlocks.Configure(new Block(), "test:exbehaviorhost", 1);
    return host;
  }

  [Fact]
  public void A_Persist_field_round_trips_through_its_host() {
    Host host = NewHost();
    var behavior = new AttributedBehavior(host) { Value = 5 };
    host.Behaviors.Add(behavior);

    var tree = new TreeAttribute();
    host.ToTreeAttributes(tree);

    Host targetHost = NewHost();
    var targetBehavior = new AttributedBehavior(targetHost);
    targetHost.Behaviors.Add(targetBehavior);
    targetHost.FromTreeAttributes(tree, new TestWorld().World);

    Assert.Equal(5, targetBehavior.Value);
  }

  [Fact]
  public void A_DeclareState_entry_round_trips_through_its_host() {
    Host host = NewHost();
    var behavior = new DeclaredBehavior(host) { Level = 2.5f };
    host.Behaviors.Add(behavior);

    var tree = new TreeAttribute();
    host.ToTreeAttributes(tree);
    Assert.Equal(2.5f, tree.GetFloat("level"));

    Host targetHost = NewHost();
    var targetBehavior = new DeclaredBehavior(targetHost);
    targetHost.Behaviors.Add(targetBehavior);
    targetHost.FromTreeAttributes(tree, new TestWorld().World);

    Assert.Equal(2.5f, targetBehavior.Level);
  }

  [Fact]
  public void Host_and_behaviour_keys_coexist() {
    Host host = NewHost();
    host.HostValue = 3;
    var behavior = new AttributedBehavior(host) { Value = 7 };
    host.Behaviors.Add(behavior);

    var tree = new TreeAttribute();
    host.ToTreeAttributes(tree);

    Assert.Equal(3, tree.GetInt("hostValue"));
    Assert.Equal(7, tree.GetInt("attrValue"));
  }

  [Fact]
  public void A_duplicate_key_inside_one_state_throws() {
    Host host = NewHost();
    var behavior = new DuplicateKeyBehavior(host);
    host.Behaviors.Add(behavior);

    Assert.Throws<InvalidOperationException>(() =>
      host.ToTreeAttributes(new TreeAttribute())
    );
  }
}
