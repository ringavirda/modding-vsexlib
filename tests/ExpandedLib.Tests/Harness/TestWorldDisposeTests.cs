using System;
using System.Runtime.CompilerServices;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>A disposed <see cref="TestWorld"/> is not kept reachable by the substitutes it created.</summary>
public class TestWorldDisposeTests {
  // Fails under "Dispose without the substitute clearing" and "Dispose without releasing the call
  // handlers". The warm world takes the proxy-type cache pin, so test order does not matter.
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
