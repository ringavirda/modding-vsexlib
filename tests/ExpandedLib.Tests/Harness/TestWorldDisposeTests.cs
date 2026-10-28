using System;
using System.Runtime.CompilerServices;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>A disposed <see cref="TestWorld"/> is not kept reachable by the substitutes it created.</summary>
public class TestWorldDisposeTests {
  // Fails under the mutation "Dispose without the substitute clearing" (the loop over
  // _substitutes removed) and under "Dispose without releasing the call handlers": the held
  // accessor still reaches the world through its Returns closures or its call handler. A world
  // built first keeps the proxy-type cache pinned, so the result does not depend on test order.
  [Fact]
  public void Disposed_world_is_collected_while_one_of_its_substitutes_is_held() {
    using var warm = new TestWorld();
    _ = warm.Player("warm");

    (WeakReference first, IBlockAccessor held) = UseAndDispose();

    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();

    Assert.False(first.IsAlive);
    GC.KeepAlive(held);
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private static (WeakReference, IBlockAccessor) UseAndDispose() {
    var world = new TestWorld();
    TestPlayer player = world.Player("first");
    _ = player.ActiveSlot;
    IBlockAccessor accessor = world.Accessor;
    var reference = new WeakReference(world);
    world.Dispose();
    return (reference, accessor);
  }
}
