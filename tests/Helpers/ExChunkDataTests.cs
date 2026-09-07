using ExpandedLib.Helpers;
using NSubstitute;
using Vintagestory.API.Common;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// The per-chunk side-band data rung over <see cref="IWorldChunk.GetModdata{T}"/>/
/// <see cref="IWorldChunk.SetModdata{T}"/>, keyed <c>{domain}:{key}</c> the same way as
/// <see cref="ExWorldData"/>.
/// </summary>
public class ExChunkDataTests {
  [Fact]
  public void Get_reads_the_domain_prefixed_key() {
    var chunk = Substitute.For<IWorldChunk>();
    chunk.GetModdata("mymod:flag", false).Returns(true);

    bool value = ExChunkData.Get(chunk, "mymod", "flag", false);

    Assert.True(value);
  }

  [Fact]
  public void Set_writes_the_domain_prefixed_key() {
    var chunk = Substitute.For<IWorldChunk>();

    ExChunkData.Set(chunk, "mymod", "flag", true);

    chunk.Received(1).SetModdata("mymod:flag", true);
  }
}
