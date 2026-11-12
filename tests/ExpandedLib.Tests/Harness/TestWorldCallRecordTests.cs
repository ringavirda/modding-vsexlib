using System;
using System.Linq;
using ExpandedLib.Testing;
using NSubstitute;
using NSubstitute.Core;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>A <see cref="TestWorld"/>'s substitutes keep a bounded record of the calls they
/// received, still read by <c>Received</c> and still configurable on the last call.</summary>
public class TestWorldCallRecordTests {
  private static readonly BlockPos Cell = new(1, 2, 3);

  // Fails under "Made without BoundedCallCollection.Install": the accessor then keeps every read.
  [Fact]
  public void A_long_run_of_reads_keeps_a_bounded_record() {
    using var world = new TestWorld();

    for (int i = 0; i < 3 * BoundedCallCollection.Kept; i++)
      world.Accessor.GetBlock(Cell);

    int kept = world.Accessor.ReceivedCalls().Count();
    Assert.InRange(
      kept,
      BoundedCallCollection.Kept,
      2 * BoundedCallCollection.Kept
    );
  }

  // Fails under "Add drops every call when full": the reads before the newest command then go too.
  [Fact]
  public void The_newest_calls_after_a_long_run_are_received() {
    using var world = new TestWorld();
    for (int i = 0; i < 2 * BoundedCallCollection.Kept; i++)
      world.Accessor.GetBlock(Cell);

    world.Accessor.MarkBlockDirty(Cell);

    world.Accessor.Received(1).MarkBlockDirty(Cell);
    world.Accessor.Received().GetBlock(Cell);
  }

  // Fails under "Install leaves the call-spec record unchanged": configuring a return on the last
  // call then deletes it from a record that never held it and throws.
  [Fact]
  public void A_return_configured_on_the_last_call_is_answered() {
    using var world = new TestWorld();

    world.Accessor.GetLightLevel(Cell, EnumLightLevelType.MaxLight).Returns(7);

    Assert.Equal(
      7,
      world.Accessor.GetLightLevel(Cell, EnumLightLevelType.MaxLight)
    );
    Assert.Single(world.Accessor.ReceivedCalls());
  }

  // Fails under "Delete without its found guard": removing a dropped call then throws.
  [Fact]
  public void Deleting_a_dropped_call_does_nothing() {
    var record = new BoundedCallCollection();
    ICall kept = Substitute.For<ICall>();
    record.Add(kept);

    record.Delete(Substitute.For<ICall>());

    Assert.Same(kept, Assert.Single(record.AllCalls()));
  }

  // Fails when a missing field passes unnamed: Install would then null-deref instead.
  [Fact]
  public void A_holder_without_the_field_is_named() {
    var error = Assert.Throws<InvalidOperationException>(() =>
      BoundedCallCollection.FieldOf<ICallCollection>(new object())
    );

    Assert.Contains("System.Object", error.Message);
  }
}
