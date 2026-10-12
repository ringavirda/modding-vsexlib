#if GAME_GE_1_22
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using ExpandedLib.Structures;
using ExpandedLib.Testing;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests.Setups;

/// <summary>What <c>SetupSceneDump</c> writes for a built scene (setups.md, The dump).</summary>
[Collection("Setups")]
public class SetupSceneDumpTests : IDisposable
{
  public void Dispose() => SetupPlacers.Clear();

  private static string ModPath(string id) =>
    Path.Combine(
      RepoPaths.Root,
      "samples",
      id == "platedpipes" ? "PlatedPipes" : "TwinTubBlower"
    );

  private static SetupScene Build(string? variant)
  {
    SetupFile file = SetupFile.Load(
      Path.Combine(
        RepoPaths.Root,
        "tests",
        "ExpandedLib.Tests",
        "Setups",
        "Files",
        "scene-dump.json"
      )
    );
    if (variant != null)
      file = file.ForVariant(variant);
    return SetupScene.Build(file, [.. file.Mods.Select(ModPath)]);
  }

  private static JsonDocument Dump(SetupScene scene, string? variant = null) =>
    JsonDocument.Parse(SetupSceneDump.Of(scene, "scene-dump", variant));

  private static int[] At(JsonElement cell) =>
    [.. cell.GetProperty("at").EnumerateArray().Select(n => n.GetInt32())];

  private static JsonElement CellAt(JsonDocument dump, int x, int y, int z) =>
    dump
      .RootElement.GetProperty("cells")
      .EnumerateArray()
      .Single(c => At(c).SequenceEqual([x, y, z]));

  // Fails when the enumeration drops the fillers: the blowers' footprints stand as fillers, so
  // the listed cells fall short of the solid cells in the world.
  [Fact]
  public void Every_non_air_cell_is_listed_in_y_z_x_order()
  {
    using SetupScene scene = Build(null);
    using JsonDocument dump = Dump(scene);

    int[][] listed =
    [
      .. dump.RootElement.GetProperty("cells").EnumerateArray().Select(At),
    ];

    int[][] solid =
    [
      .. from y in Enumerable.Range(-6, 13)
      from z in Enumerable.Range(-6, 13)
      from x in Enumerable.Range(-6, 13)
      where scene.World.GetBlock(new BlockPos(x, y, z)).Id != 0
      select new[] { x, y, z },
    ];
    Assert.True(solid.Length > 12);
    Assert.Equal(solid, listed);
  }

  // Fails when a filler's principal is left out of the file.
  [Fact]
  public void Each_filler_names_its_blowers_principal()
  {
    using SetupScene scene = Build(null);
    using JsonDocument dump = Dump(scene);
    BlockPos first = scene.PositionOf("first");
    BlockPos second = scene.PositionOf("second");

    JsonElement[] fillers =
    [
      .. dump
        .RootElement.GetProperty("cells")
        .EnumerateArray()
        .Where(c => c.GetProperty("role").GetString() is "filler" or "port"),
    ];

    Assert.Equal(10, fillers.Length);
    Assert.All(
      fillers,
      c =>
      {
        Assert.False(c.TryGetProperty("id", out _));
        int[] principal =
        [
          .. c.GetProperty("principal")
            .EnumerateArray()
            .Select(n => n.GetInt32()),
        ];
        bool ofFirst = At(c)[0] == first.X;
        BlockPos owner = ofFirst ? first : second;
        Assert.Equal([owner.X, owner.Y, owner.Z], principal);
      }
    );
    Assert.Equal(
      "core",
      CellAt(dump, first.X, first.Y, first.Z).GetProperty("role").GetString()
    );
  }

  // Fails when keys follow BlockNetworkModSystem.AllNetworks order: the pipes are placed east
  // first, so that order calls the east pipe's network A.
  [Fact]
  public void Network_keys_follow_cell_order_not_creation_order()
  {
    using SetupScene scene = Build(null);
    using JsonDocument dump = Dump(scene);

    Assert.Equal("A", CellAt(dump, 2, 0, 0).GetProperty("network").GetString());
    Assert.Equal("B", CellAt(dump, 5, 0, 0).GetProperty("network").GetString());
    JsonElement[] networks =
    [
      .. dump.RootElement.GetProperty("networks").EnumerateArray(),
    ];
    Assert.Equal(
      ["A", "B"],
      networks.Take(2).Select(n => n.GetProperty("key").GetString())
    );
    Assert.Equal(
      ["low-west"],
      networks[0].GetProperty("ids").EnumerateArray().Select(i => i.GetString())
    );
    Assert.Equal(
      ["low-east"],
      networks[1].GetProperty("ids").EnumerateArray().Select(i => i.GetString())
    );
  }

  // Fails when the line keys are not written: the blowers back to back share one line, and the
  // cut variant's moved blower leaves each alone on none.
  [Fact]
  public void The_mechanical_line_gets_a_key_and_the_cut_variant_has_none()
  {
    using SetupScene whole = Build(null);
    using SetupScene cut = Build("cut");
    using JsonDocument wholeDump = Dump(whole);
    using JsonDocument cutDump = Dump(cut, "cut");

    JsonElement line = Assert.Single(
      wholeDump.RootElement.GetProperty("lines").EnumerateArray()
    );

    Assert.Equal("M1", line.GetProperty("key").GetString());
    Assert.Equal(2, line.GetProperty("members").GetInt32());
    Assert.Equal(
      ["first", "second"],
      line.GetProperty("ids")
        .EnumerateArray()
        .Select(i => i.GetString())
        .Order()
    );
    Assert.Empty(cutDump.RootElement.GetProperty("lines").EnumerateArray());
    Assert.Equal("cut", cutDump.RootElement.GetProperty("variant").GetString());
  }
}
#endif
