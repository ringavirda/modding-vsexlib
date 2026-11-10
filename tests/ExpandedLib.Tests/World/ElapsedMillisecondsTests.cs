using ExpandedLib.Testing;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary><see cref="TestWorld"/>'s <c>ElapsedMilliseconds</c> following the harness clock.</summary>
public class ElapsedMillisecondsTests {
  // Fails when the world's ElapsedMilliseconds is the constant 0 again.
  [Fact]
  public void ElapsedMilliseconds_follows_advanced_block_entity_time() {
    var w = new TestWorld();
    Assert.Equal(0, w.World.ElapsedMilliseconds);

    w.AdvanceBlockEntityTime(1500);
    Assert.Equal(1500, w.World.ElapsedMilliseconds);

    w.AdvanceBlockEntityTime(250);
    Assert.Equal(1750, w.World.ElapsedMilliseconds);
  }
}
