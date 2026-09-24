using System.Collections.Generic;
using System.Reflection;
using ExpandedLib.Definitions;
using ExpandedLib.Testing;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary><see cref="DefinitionAssets.MissingShapes"/> over planted blocks naming exlib shapes that
/// do and do not exist.</summary>
public class DefinitionAssetsTests {
  public DefinitionAssetsTests() => TestModDomain.Register();

  private const string Missing = "plantedassets";
  private const string Present = "plantedassetsclean";
  private static readonly Assembly Here =
    typeof(DefinitionAssetsTests).Assembly;

  /// <summary>Declares its blocks only under <see cref="Missing"/> and <see cref="Present"/>, so
  /// other scans of this assembly never see them.</summary>
  private sealed class Shaped : IExBlockDefProvider {
    public static IEnumerable<ExBlockDef> Definitions(string domain) =>
      domain switch {
        Missing =>
        [
          ExBlockDef
            .Create(domain, "shapeless")
            .Shape("exlib:block/nosuchshape"),
        ],
        Present =>
        [
          ExBlockDef.Create(domain, "shaped").Shape("exlib:block/empty"),
          ExBlockDef.Create(domain, "vanilla").Shape("game:block/nosuchshape"),
        ],
        _ => [],
      };
  }

  [Fact]
  [PlantedDefect(
    typeof(DefinitionAssets),
    nameof(DefinitionAssets.MissingShapes)
  )]
  public void A_shape_with_no_file_behind_it_is_reported() {
    Assert.Equal(
      [
        "plantedassets:blocktypes/shapeless.json -> 'exlib:block/nosuchshape' "
          + "(expected assets/exlib/shapes/block/nosuchshape.json)",
      ],
      DefinitionAssets.MissingShapes(Missing, Here)
    );
  }

  [Fact]
  public void A_shipped_shape_and_a_game_shape_pass() {
    Assert.Empty(DefinitionAssets.MissingShapes(Present, Here));
  }
}
