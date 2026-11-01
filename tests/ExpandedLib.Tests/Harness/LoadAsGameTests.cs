// BurdenMaker builds only for the current game version.
#if GAME_GE_1_22
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ExpandedLib.Catalogues;
using ExpandedLib.Helpers;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.Common;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary><see cref="TestWorld.LoadAsGame"/>: the finalize stage, the art in the asset manager,
/// the cached world and the PNG reader.</summary>
[Collection("ItemViews")]
public class LoadAsGameTests
{
  private static string Burden =>
    Path.Combine(RepoPaths.Root, "samples", "BurdenMaker");

  private static string PngFixture =>
    Path.Combine(
      RepoPaths.Root,
      "tests",
      "ExpandedLib.Tests",
      "Harness",
      "Fixtures",
      "png"
    );

  private static byte[] Texture(string name) =>
    File.ReadAllBytes(
      Path.Combine(PngFixture, "assets", "pngfixture", "textures", name)
    );

  private sealed class OrderedSystem(double order) : ModSystem
  {
    public override double ExecuteOrder() => order;
  }

  // Fails when the sort is dropped: the systems come back in list order.
  [Fact]
  public void Finalize_order_is_execute_order_across_mods()
  {
    var late = new OrderedSystem(0.1);
    var early = new OrderedSystem(0.05);
    var tiedFirst = new OrderedSystem(0.06);
    var tiedSecond = new OrderedSystem(0.06);

    List<ModSystem> ordered = TestWorld.FinalizeOrder([
      late,
      tiedFirst,
      tiedSecond,
      early,
    ]);

    Assert.Equal([early, tiedFirst, tiedSecond, late], ordered);
  }

  // Fails when the finalize call is removed: the registry stays as StartPre emptied it.
  [Fact]
  public void LoadAsGame_runs_the_catalogue_load()
  {
    using var world = new TestWorld();

    world.LoadAsGame([Burden]);

    Assert.Contains(
      MaterialRoleRegistry.OfRole("ironore"),
      d => d.PathPrefix == "crushed-iron"
    );
    Assert.Contains(
      MaterialRoleRegistry.OfRole("flux"),
      d => d.Code == "game:lime"
    );
  }

  // Fails when survival's shapes, the mods' shapes or the mods' textures are not mirrored.
  [Fact]
  public void Shapes_and_textures_reach_the_manager()
  {
    TestWorld world = GameWorlds.Get([Burden, PngFixture]);

    Assert.NotNull(
      ExMeshCache.LoadShape(
        world.Api,
        new AssetLocation("game:shapes/item/tool/spear/metal-copper.json")
      )
    );
    Assert.NotNull(
      ExMeshCache.LoadShape(
        world.Api,
        new AssetLocation("game:shapes/entity/humanoid/seraph-faceless.json")
      )
    );
    Assert.NotNull(
      ExMeshCache.LoadShape(
        world.Api,
        new AssetLocation("burdenmaker:shapes/ore/burden.json")
      )
    );
    Assert.NotNull(
      world.Api.Assets.TryGet(new AssetLocation("pngfixture:textures/rgba.png"))
    );
  }

  // Fails when the cache returns a world for a different mod list, or builds again for one list.
  [Fact]
  public void The_cache_holds_one_world_and_rebuilds_for_another_list()
  {
    TestWorld first = GameWorlds.Get([PngFixture]);
    first.ConfigFiles.Write("held.json", new { });
    string firstDir = first.ConfigFiles.Directory;

    TestWorld again = GameWorlds.Get([PngFixture]);
    TestWorld other = GameWorlds.Get([Burden, PngFixture]);

    Assert.Same(first, again);
    Assert.NotSame(first, other);
    Assert.False(Directory.Exists(firstDir));
  }

  // Fails when the key is the joined paths as written: the same folder spelled another way builds again.
  [Fact]
  public void The_cache_keys_on_full_paths()
  {
    TestWorld first = GameWorlds.Get([PngFixture]);

    TestWorld again = GameWorlds.Get([
      Path.Combine(PngFixture, "..", "png") + Path.DirectorySeparatorChar,
    ]);

    Assert.Same(first, again);
  }

  // Fails when the key leaves out the game path: another install gets the held world.
  [Fact]
  public void The_cache_keys_on_the_game_path()
  {
    TestWorld first = GameWorlds.Get([PngFixture]);

    TestWorld other = GameWorlds.Get(
      [PngFixture],
      gamePath: VsAssemblyResolver.InstallPath
    );

    Assert.NotSame(first, other);
  }

  // Fails when the cache ignores the load generation: another load emptied the registries under it.
  [Fact]
  public void The_cache_rebuilds_after_another_load_and_restores()
  {
    TestWorld first = GameWorlds.Get([PngFixture]);
    using TestWorld bump = new TestWorld().LoadAssets([]);
    int restored = 0;

    TestWorld again = GameWorlds.Get([PngFixture], restore: _ => restored++);
    TestWorld cached = GameWorlds.Get([PngFixture], restore: _ => restored++);

    Assert.NotSame(first, again);
    Assert.Same(again, cached);
    Assert.Equal(1, restored);
  }

  // Fails when the register callback is skipped, or runs after the load.
  [Fact]
  public void The_cache_registers_classes_before_the_load()
  {
    bool ranBeforeLoad = false;

    GameWorlds.Get(
      [Burden + "/"],
      register: w =>
        ranBeforeLoad = w.Api.Assets.GetType().Name != "AssetManager"
    );

    Assert.True(ranBeforeLoad);
  }

  // Fails when alpha is ignored: the whole image comes back.
  [Fact]
  public void Png_bounds_are_the_opaque_pixels()
  {
    Assert.Equal(
      new OpaqueRect(2, 1, 6, 4),
      PngReader.OpaqueBounds(Texture("rgba.png"))
    );
    Assert.Equal(
      new OpaqueRect(1, 2, 4, 4),
      PngReader.OpaqueBounds(Texture("palette.png"))
    );
  }

  private static byte[] Encode(
    int width,
    int height,
    int colorType,
    byte[][] rows,
    byte[]? trns = null,
    int depth = 8,
    int interlace = 0
  )
  {
    var ms = new MemoryStream();
    void Chunk(string type, byte[] data)
    {
      var len = new byte[4];
      System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(
        len,
        data.Length
      );
      ms.Write(len);
      ms.Write(System.Text.Encoding.ASCII.GetBytes(type));
      ms.Write(data);
      ms.Write(new byte[4]);
    }
    ms.Write([137, 80, 78, 71, 13, 10, 26, 10]);
    var ihdr = new byte[13];
    System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(ihdr, width);
    System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(
      ihdr.AsSpan(4),
      height
    );
    ihdr[8] = (byte)depth;
    ihdr[9] = (byte)colorType;
    ihdr[12] = (byte)interlace;
    Chunk("IHDR", ihdr);
    if (colorType == 3)
      Chunk("PLTE", [0, 0, 0, 255, 255, 255]);
    if (trns != null)
      Chunk("tRNS", trns);
    var raw = new MemoryStream();
    foreach (byte[] row in rows)
    {
      raw.WriteByte(0);
      raw.Write(row);
    }
    var packed = new MemoryStream();
    using (
      var z = new System.IO.Compression.ZLibStream(
        packed,
        System.IO.Compression.CompressionLevel.Fastest,
        true
      )
    )
      z.Write(raw.ToArray());
    Chunk("IDAT", packed.ToArray());
    Chunk("IEND", []);
    return ms.ToArray();
  }

  // Fails when a colour type reads the wrong channel for alpha: type 4 reads byte 1 of 2, type 0 and
  // 2 are opaque throughout.
  [Fact]
  public void Png_bounds_read_each_colour_type()
  {
    Assert.Equal(
      new OpaqueRect(1, 0, 2, 1),
      PngReader.OpaqueBounds(
        Encode(
          3,
          1,
          4,
          [
            [200, 0, 7, 255, 7, 0],
          ]
        )
      )
    );
    Assert.Equal(
      new OpaqueRect(0, 0, 2, 2),
      PngReader.OpaqueBounds(
        Encode(
          2,
          2,
          0,
          [
            [0, 5],
            [0, 0],
          ]
        )
      )
    );
    Assert.Equal(
      new OpaqueRect(0, 0, 2, 1),
      PngReader.OpaqueBounds(
        Encode(
          2,
          1,
          2,
          [
            [0, 0, 0, 1, 2, 3],
          ]
        )
      )
    );
    Assert.Null(
      PngReader.OpaqueBounds(
        Encode(
          2,
          1,
          6,
          [
            [1, 2, 3, 0, 4, 5, 6, 0],
          ]
        )
      )
    );
  }

  // Fails when a PNG the reader cannot read is read as if it could.
  [Fact]
  public void Png_the_reader_cannot_read_is_a_listed_skip()
  {
    byte[] sixteen = Encode(
      1,
      1,
      0,
      [
        [0, 0],
      ],
      depth: 16
    );
    byte[] interlaced = Encode(
      1,
      1,
      0,
      [
        [0],
      ],
      interlace: 1
    );
    byte[] keyed = Encode(
      1,
      1,
      0,
      [
        [0],
      ],
      trns: [0, 0]
    );

    Assert.Contains(
      "Bit depth 16",
      Assert
        .Throws<NotSupportedException>(() => PngReader.OpaqueBounds(sixteen))
        .Message
    );
    Assert.Contains(
      "Interlaced",
      Assert
        .Throws<NotSupportedException>(() => PngReader.OpaqueBounds(interlaced))
        .Message
    );
    Assert.Contains(
      "colour key",
      Assert
        .Throws<NotSupportedException>(() => PngReader.OpaqueBounds(keyed))
        .Message
    );
    Assert.Throws<InvalidDataException>(() =>
      PngReader.OpaqueBounds([1, 2, 3])
    );

    string[] viewsFixture =
    [
      Path.Combine(
        RepoPaths.Root,
        "tests",
        "ExpandedLib.Tests",
        "Helpers",
        "Views",
        "Fixtures",
        "viewsfixture"
      ),
    ];
    SweepRow row = ViewSweep
      .Rows(GameWorlds.Get(viewsFixture), "viewsfixture")
      .Single(r => r.Code == "viewsfixture:texbad");
    Assert.Null(row.Kind);
    Assert.Contains("Bit depth 16", row.Skip);
  }
}
#endif
