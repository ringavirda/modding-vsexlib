// The sweep covers the current game version; 1.20 ships no blocktypes/aquatic/coral-brain.json, which the source column test names.
#if GAME_GE_1_22
using System;
using System.IO;
using System.Linq;
using ExpandedLib.Helpers;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>The kind each vanilla collectible boxes as, over the whole vanilla game with the views fixture mod.</summary>
[Collection("ItemViews")]
public class ViewSweepTests
{
  private const string Variable = "EXLIB_TEST_VIEW_SWEEP";

  private static string Fixture =>
    Path.Combine(
      RepoPaths.Root,
      "tests",
      "ExpandedLib.Tests",
      "Helpers",
      "Views",
      "Fixtures",
      "viewsfixture"
    );

  private static TestWorld World => GameWorlds.Get([Fixture]);

  private static T With<T>(string variable, string? value, Func<T> run)
  {
    string? before = Environment.GetEnvironmentVariable(variable);
    Environment.SetEnvironmentVariable(variable, value);
    try
    {
      return run();
    }
    finally
    {
      Environment.SetEnvironmentVariable(variable, before);
    }
  }

  // Fails when a texture-only item is dropped without a row, and when an item and a block with one code share a group. EX_ITEM_VIEWS_SWEEP=<file> also writes the sweep there.
  [Fact]
  [Trait("Speed", "Slow")]
  public void Sweep_covers_every_vanilla_collectible()
  {
    var rows = ViewSweep.Rows(World, "game");

    Assert.Equal(
      World
        .World.Collectibles.Where(c => c.Code?.Domain == "game")
        .Select(c => c.Code.ToString())
        .Order(StringComparer.Ordinal),
      rows.Select(r => r.Code)
    );
    Assert.All(
      rows,
      r => Assert.True((r.Kind == null) != (r.Skip == null), r.Code)
    );
    Assert.DoesNotContain(
      rows,
      r =>
        r.Type == "block"
        && r.Kind != null
        && r.Kind is not ("cube" or "multicell" or "partial")
    );
    Assert.DoesNotContain(
      rows,
      r => r.Type == "item" && r.Kind is "cube" or "multicell" or "partial"
    );
    ViewSweep.Write(World);
  }

  // Fails when a texture-only item's rules are not read: the texture plate on its edge then fails G4 alone, and the
  // flat one passes them all.
  [Fact]
  public void A_texture_only_item_on_edge_fails_G4()
  {
    var item = (Item)
      World.World.GetItem(new AssetLocation("viewsfixture:texonly"));
    ModelTransform authored = item.GroundTransform;
    try
    {
      Assert.Empty(
        ViewSweep
          .Rows(World, "viewsfixture")
          .Single(r => r.Code == item.Code.ToString())
          .Failed
      );

      ModelTransform edge = ViewMatrices
        .Transform(item, ViewTarget.Ground)
        .Clone();
      edge.Rotation = new FastVec3f(0, edge.Rotation.Y, edge.Rotation.Z);
      item.GroundTransform = edge;

      Assert.Equal(
        ["G4"],
        ViewSweep
          .Rows(World, "viewsfixture")
          .Single(r => r.Code == item.Code.ToString())
          .Failed
      );
    }
    finally
    {
      item.GroundTransform = authored;
    }
  }

  // Fails when a texture-only box reads its height from the texture's width.
  [Fact]
  public void A_texture_only_item_is_boxed_from_its_opaque_pixels()
  {
    SweepRow row = ViewSweep
      .Rows(World, "viewsfixture")
      .Single(r => r.Code == "viewsfixture:texonly");
    var item = (Item)World.World.GetItem(new AssetLocation(row.Code));

    CollectibleViews.ViewBox box = TextureBox.Of(World.Api, item)!;

    Assert.Equal("plate", row.Kind);
    Assert.Equal([4, 4, 8], [box.From.X, box.From.Y, box.From.Z]);
    Assert.Equal([16, 16, 9], [box.To.X, box.To.Y, box.To.Z]);
  }

  private static int IdOf(SweepRow row)
  {
    var location = new AssetLocation(row.Code);
    return row.Type == "block"
      ? World.World.GetBlock(location).BlockId
      : World.World.GetItem(location).ItemId;
  }

  // Fails when each source file's first variant is not the one with the lowest id, and when a file that shares its code
  // with another takes the other's variants, whether it names its groups, loads them from properties or adds to one.
  [Fact]
  [Trait("Speed", "Slow")]
  public void Each_source_file_has_one_first_variant_the_lowest_id()
  {
    var rows = ViewSweep.Rows(World, "game");

    Assert.All(
      rows.Where(r => r.Source == ""),
      r => Assert.False(r.First, r.Code)
    );
    Assert.Contains(rows, r => r.Source == "blocktypes/multiblock.json");
    Assert.Equal(
      "itemtypes/resource/ore-ungraded.json",
      rows.Single(r => r.Type == "item" && r.Code == "game:ore-alum").Source
    );
    Assert.All(
      rows.Where(r => r.Source != "").GroupBy(r => r.Source),
      g => Assert.Equal(1, g.Count(r => r.First))
    );
    Assert.All(
      rows.Where(r => r.Source != "").GroupBy(r => r.Source),
      g => Assert.Equal(g.Min(IdOf), IdOf(g.Single(r => r.First)))
    );
    foreach (
      (string code, string source) in new[]
      {
        ("game:door-2x2gate-pine", "blocktypes/wood/woodtyped/door.json"),
        ("game:door-1x2heavy-aged", "blocktypes/metal/door.json"),
        ("game:door-plank-east-up-opened-right", "blocktypes/legacy/door.json"),
        (
          "game:brickstairs-eight-black-down-east-free",
          "blocktypes/clay/brickstairscourse.json"
        ),
        (
          "game:brickstairs-fire-down-east-free",
          "blocktypes/clay/brickstairs.json"
        ),
        ("game:mushroom-commonmorel-normal", "blocktypes/plant/mushroom.json"),
        (
          "game:mushroom-beardedtooth-harvested-north",
          "blocktypes/plant/mushroom-side.json"
        ),
      }
    )
      Assert.Equal(
        source,
        rows.Single(r => r.Type == "block" && r.Code == code).Source
      );
    Assert.Equal(
      "blocktypes/aquatic/coral-brain.json",
      rows.Single(r =>
        r.Type == "block" && r.Code == "game:coral-brain-blue-1"
      ).Source
    );
    Assert.Equal(
      "blocktypes/aquatic/coral-tube.json",
      rows.Single(r =>
        r.Type == "block" && r.Code == "game:coral-tube-brown-1"
      ).Source
    );
  }

  // Fails when the writer ignores its variable and writes anyway, or writes without the counts.
  [Fact]
  [Trait("Speed", "Slow")]
  public void The_sweep_is_written_where_its_variable_says()
  {
    string path = Path.Combine(
      Directory.CreateTempSubdirectory("viewsweep").FullName,
      "kinds.tsv"
    );

    Assert.Null(With(Variable, null, () => ViewSweep.Write(World, Variable)));
    Assert.Null(With(Variable, "", () => ViewSweep.Write(World, Variable)));
    Assert.False(File.Exists(path));
    Assert.Equal(
      path,
      With(Variable, path, () => ViewSweep.Write(World, Variable))
    );

    string[] lines = File.ReadAllLines(path);
    Assert.Equal(
      "code\ttype\tsource\tfirst\tkind\tskip\tnote\tfailed",
      lines[0]
    );
    Assert.Contains(
      lines,
      l =>
        l.StartsWith("game:ingot-copper\titem\titemtypes/resource/ingot.json\t")
    );
    Assert.Contains(lines, l => l.StartsWith("#kind\titem lump\t"));
    Assert.Contains(lines, l => l.StartsWith("#skipped\t"));
  }
}
#endif
