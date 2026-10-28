using System;
using System.Runtime.CompilerServices;
using ExpandedLib.Testing;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>A disposed <see cref="TestWorld"/> is not kept reachable by the substitutes it created.</summary>
public class TestWorldDisposeTests {
  // Fails under the mutation "Dispose without the substitute clearing" (the loop over
  // _substitutes removed) and under "Dispose without releasing the call handlers": either way
  // the first substitute of a proxied type keeps its call router in the proxy-type cache, and
  // the router reaches the world.
  [Fact]
  public void Disposed_world_is_collected_while_a_later_world_holds_a_substitute_of_the_same_type() {
    WeakReference first = UseAndDispose();
    var laterWorld = new TestWorld();
    TestPlayer later = laterWorld.Player("later");

    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();

    Assert.False(first.IsAlive);
    GC.KeepAlive(later);
    GC.KeepAlive(laterWorld);
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private static WeakReference UseAndDispose() {
    var world = new TestWorld();
    TestPlayer player = world.Player("first");
    _ = player.ActiveSlot;
    var reference = new WeakReference(world);
    world.Dispose();
    return reference;
  }
}
