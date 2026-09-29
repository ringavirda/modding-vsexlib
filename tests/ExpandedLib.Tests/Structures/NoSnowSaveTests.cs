using System;
using System.Collections.Generic;
using ExpandedLib.Structures;
using ExpandedLib.Testing;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary><see cref="NoSnowCells"/> saved with the world: written at <c>GameWorldSave</c>, read back
/// at <c>SaveGameLoaded</c>, and a saved mark released at its column's <c>ChunkColumnLoaded</c> when its
/// structure is gone. The save is serialised as the game's <see cref="SerializerUtil"/> does. Runs
/// alone: a save and a load carry every owner's marks.</summary>
[Collection(ExHarmonyCollection.Name)]
public class NoSnowSaveTests {
  private const string Key = "exlib:nosnowcells";

  // A fresh server world over the save in store, NoSnowCells attached to it.
  private static TestWorld Started(Dictionary<string, byte[]> store) {
    var world = Fresh(store);
    NoSnowCells.Attach(world.Api);
    return world;
  }

  private static TestWorld Fresh(Dictionary<string, byte[]> store) {
    var world = new TestWorld();
    var save = Substitute.For<ISaveGame>();
    save.When(s => s.StoreData(Arg.Any<string>(), Arg.Any<int[]>()))
      .Do(ci =>
        store[ci.ArgAt<string>(0)] = SerializerUtil.Serialize(
          ci.ArgAt<int[]>(1)
        )
      );
    save.GetData(Arg.Any<string>(), Arg.Any<int[]?>())
      .Returns(ci =>
        store.TryGetValue(ci.ArgAt<string>(0), out byte[]? bytes)
          ? SerializerUtil.Deserialize<int[]>(bytes)
          : ci.ArgAt<int[]?>(1)
      );
    world.Api.WorldManager.SaveGame.Returns(save);
    return world;
  }

  private static Dictionary<string, byte[]> Saved(params int[] data) =>
    new() { [Key] = SerializerUtil.Serialize(data) };

  private static void Load(TestWorld world) =>
    world.Api.Event.SaveGameLoaded += Raise.Event<Action>();

  // Raises ChunkColumnLoaded for the column holding pos, its chunks reading world's block entities.
  private static void LoadColumn(TestWorld world, BlockPos pos) {
    var chunks = new IWorldChunk[8];
    for (int i = 0; i < chunks.Length; i++) {
      chunks[i] = Substitute.For<IWorldChunk>();
      chunks[i]
        .GetLocalBlockEntityAtBlockPos(Arg.Any<BlockPos>())
        .Returns(ci => world.GetBlockEntity(ci.Arg<BlockPos>()));
    }
    world.Api.Event.ChunkColumnLoaded += Raise.Event<ChunkColumnLoadedDelegate>(
      new Vec2i(pos.X / 32, pos.Z / 32),
      chunks
    );
  }

  // Fails when exlib's server Start does not attach NoSnowCells, or SaveGameLoaded reads nothing
  // back.
  [Fact]
  public void A_save_and_load_restore_the_marks_before_any_chunk_loads() {
    var store = new Dictionary<string, byte[]>();
    var anchor = new BlockPos(9000, 10, 0);
    BlockPos floor = anchor.AddCopy(2, 0, 0);
    TestWorld before = Started(store);
    NoSnowCellsTests.Formed(anchor);
    before.Api.Event.GameWorldSave += Raise.Event<Action>();
    NoSnowCells.Unmark(anchor);
    Assert.False(NoSnowCells.IsMarked(floor));

    TestWorld after = Fresh(store);
    var exlib = new ExpandedLibModSystem();
    ReflectionHelpers.SetProperty(
      exlib,
      nameof(ModSystem.Mod),
      after.Mods.GetMod("exlib")!
    );
    try {
      exlib.Start(after.Api);
      Load(after);

      Assert.Null(after.GetBlockEntity(anchor));
      Assert.True(NoSnowCells.IsMarked(floor));
      Assert.False(NoSnowCells.IsMarked(anchor.AddCopy(1, 0, 0)));
    } finally {
      exlib.Dispose();
      NoSnowCells.Unmark(anchor);
    }
  }

  // Fails when Restore records an owner without marking its cells.
  [Fact]
  public void The_snow_patch_refuses_a_cell_marked_from_the_save() {
    using var fixture = new HarmonyFixture(
      "exlibtest.nosnowsave",
      typeof(NoSnowCells).Assembly
    );
    var owner = new BlockPos(9100, 10, 0);
    BlockPos floor = owner.AddCopy(2, 0, 0);
    Load(Started(Saved(9100, 10, 0, 1, 9102, 10, 0)));
    var brick = new Block();

    Assert.False(brick.AllowSnowCoverage(null!, floor));
    Assert.True(brick.AllowSnowCoverage(null!, floor.EastCopy()));
    NoSnowCells.Unmark(owner);
  }

  // Fails when Restore reads a missing save as data.
  [Fact]
  public void A_world_saved_without_marks_loads_none() {
    TestWorld world = Started([]);

    Load(world);

    Assert.False(NoSnowCells.IsMarked(new BlockPos(9150, 10, 0)));
  }

  // Fails when Restore reads an owner whose cells run past the end or whose count is negative.
  [Theory]
  [InlineData(2)]
  [InlineData(-1)]
  public void A_save_cut_short_restores_the_owners_before_the_cut(int count) {
    Load(
      Started(
        Saved(9200, 10, 0, 1, 9201, 10, 0, 9210, 10, 0, count, 9211, 10, 0)
      )
    );

    Assert.True(NoSnowCells.IsMarked(new BlockPos(9201, 10, 0)));
    Assert.False(NoSnowCells.IsMarked(new BlockPos(9211, 10, 0)));
    NoSnowCells.Unmark(new BlockPos(9200, 10, 0));
    NoSnowCells.Unmark(new BlockPos(9210, 10, 0));
  }

  // Fails when a column load releases nothing.
  [Fact]
  public void A_saved_mark_whose_structure_is_gone_is_released_when_its_column_loads() {
    var owner = new BlockPos(9300, 10, 0);
    TestWorld world = Started(Saved(9300, 10, 0, 1, 9302, 10, 0));
    Load(world);
    Assert.True(NoSnowCells.IsMarked(owner.AddCopy(2, 0, 0)));

    LoadColumn(world, owner);

    Assert.False(NoSnowCells.IsMarked(owner.AddCopy(2, 0, 0)));
  }

  // Fails when a column load releases an owner without asking for its block entity.
  [Fact]
  public void A_column_load_keeps_the_marks_of_a_structure_standing_in_it() {
    var anchor = new BlockPos(9400, 10, 0);
    var (world, _) = NoSnowCellsTests.Formed(anchor);
    NoSnowCells.Attach(world.Api);

    LoadColumn(world, anchor);

    Assert.True(NoSnowCells.IsMarked(anchor.AddCopy(2, 0, 0)));
    NoSnowCells.Unmark(anchor);
  }

  // Fails when a column load releases owners outside its column.
  [Fact]
  public void A_column_load_keeps_the_marks_of_owners_in_other_columns() {
    var owner = new BlockPos(9500, 10, 0);
    TestWorld world = Started(Saved(9500, 10, 0, 1, 9502, 10, 0));
    Load(world);

    LoadColumn(world, owner.AddCopy(32, 0, 0));

    Assert.True(NoSnowCells.IsMarked(owner.AddCopy(2, 0, 0)));
    NoSnowCells.Unmark(owner);
  }

  // Fails when a column load reads a chunk for an owner above its chunks, in another dimension.
  [Fact]
  public void A_column_load_keeps_the_marks_of_owners_in_other_dimensions() {
    var owner = new BlockPos(9600, 10, 0, 1);
    var cell = new BlockPos(9602, 10, 0, 1);
    TestWorld world = Started([]);
    NoSnowCells.Mark(owner, [cell]);

    LoadColumn(world, owner);

    Assert.True(NoSnowCells.IsMarked(cell));
    NoSnowCells.Unmark(owner);
  }
}
