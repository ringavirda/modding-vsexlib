using System.Collections.Generic;
using System.IO;
using System.Linq;
using ExpandedLib.Industry.Casting;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Xunit;

namespace SashWeights.Tests;

/// <summary>
/// The sample's casting content after one game load: its station kind is registered by Industry's
/// loaders and its pattern parses and validates against it.
/// </summary>
public class PatternLoadTests {
  private static readonly string Sample = Path.Combine(
    RepoPaths.Root,
    "samples",
    "SashWeights"
  );

  private static TestWorld Load() {
    var world = new TestWorld();
    world.LoadAsGame([Sample]);
    return world;
  }

  private static CollectibleObject Pattern(TestWorld world) =>
    world.Api.World.Collectibles.First(c =>
      c?.Code?.ToString() == "sashweights:pattern-sashweight"
    )!;

  [Fact]
  public void Boxflask_is_registered_with_its_moulds_and_sand() {
    using TestWorld world = Load();

    Assert.True(
      CastingStationRegistry.Shared.TryGet(
        "boxflask",
        out CastingStationKind? kind
      )
    );
    Assert.Equal([MouldKind.Open], kind!.Moulds);
    Assert.Equal("sashweights:block/boxflask/sand-base", kind.SandBase);
    Assert.Equal("sashweights:block/boxflask/sand-half", kind.SandHalf);
  }

  [Fact]
  public void Pattern_parses_to_one_four_piece_boxflask_spec() {
    using TestWorld world = Load();

    Assert.True(
      MoldSpec.TryParseAll(
        Pattern(world).Attributes[MoldSpec.AttributeKey],
        out IReadOnlyList<MoldSpec> specs,
        out string? error
      ),
      error
    );
    MoldSpec spec = Assert.Single(specs);
    Assert.Equal("boxflask", spec.Station);
    Assert.Equal(4, spec.Pieces);
    Assert.Equal(60, spec.Capacity);
    Assert.Equal("sashweights:sashweight", spec.Output.Code.ToString());
    Assert.Equal(MouldKind.Open, spec.Mould);
  }

  [Fact]
  public void Every_pattern_validates_and_the_load_logs_no_invalid_pattern() {
    using TestWorld world = Load();

    Assert.Empty(CastingPatterns.Validate(world.Api.World.Collectibles));
    Assert.DoesNotContain(
      world.Log.Errors,
      line => line.Contains("invalid mold pattern")
    );
  }
}
