using System.Collections.Generic;
using ExpandedLib.Assembling;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>The cap record of <see cref="BEBehaviorAssembled"/>: its tree format and what a paste keeps.</summary>
public class CapRecordTests {
  private static (
    TestWorld World,
    BlockEntityAssembled Entity,
    BEBehaviorAssembled Record,
    Item Cap,
    Item Bolt
  ) Standing() {
    var world = new TestWorld();
    var entity = new BlockEntityAssembled();
    var record = new BEBehaviorAssembled(entity);
    entity.Behaviors.Add(record);
    return (
      world,
      entity,
      record,
      world.RegisterItem("test:cap"),
      world.RegisterItem("test:bolt")
    );
  }

  // Fails when the cap keys are not read back, so a capped face reloads open.
  [Fact]
  public void Cap_RecordSurvivesReload() {
    var (world, entity, record, cap, bolt) = Standing();
    record.SetCap(
      BlockFacing.UP,
      new ItemStack(cap, 1),
      new ItemStack(bolt, 2)
    );
    record.SetCap(
      BlockFacing.WEST,
      new ItemStack(cap, 1),
      new ItemStack(bolt, 3)
    );
    var tree = new TreeAttribute();

    record.ToTreeAttributes(tree);
    var saved = new TreeAttribute();
    saved.FromBytes(tree.ToBytes());
    var read = new BEBehaviorAssembled(entity);
    read.FromTreeAttributes(saved, world.World);

    Assert.Same(cap, read.CapAt(BlockFacing.UP)!.Item);
    Assert.Equal(2, read.CapFastenersAt(BlockFacing.UP)!.StackSize);
    Assert.Same(bolt, read.CapFastenersAt(BlockFacing.UP)!.Item);
    Assert.Same(cap, read.CapAt(BlockFacing.WEST)!.Item);
    Assert.Equal(3, read.CapFastenersAt(BlockFacing.WEST)!.StackSize);
    Assert.Null(read.CapAt(BlockFacing.NORTH));
    Assert.Null(read.CapFastenersAt(BlockFacing.NORTH));
  }

  // Fails when a null cap leaves the face's fasteners recorded.
  [Fact]
  public void Cap_NullClearsBothRecordsOfTheFace() {
    var (_, _, record, cap, bolt) = Standing();
    record.SetCap(
      BlockFacing.UP,
      new ItemStack(cap, 1),
      new ItemStack(bolt, 2)
    );

    record.SetCap(BlockFacing.UP, null, new ItemStack(bolt, 2));

    Assert.Null(record.CapAt(BlockFacing.UP));
    Assert.Null(record.CapFastenersAt(BlockFacing.UP));
  }

  // Fails when a stack that no longer resolves is kept, so a face reads a cap with no collectible.
  [Fact]
  public void Cap_UnresolvedStackIsNotRead() {
    var (world, entity, record, cap, bolt) = Standing();
    record.SetCap(
      BlockFacing.UP,
      new ItemStack(cap, 1) { Id = 9002 },
      new ItemStack(bolt, 2) { Id = 9003 }
    );
    var tree = new TreeAttribute();
    record.ToTreeAttributes(tree);
    var read = new BEBehaviorAssembled(entity);

    read.FromTreeAttributes(tree, world.World);

    Assert.Null(read.CapAt(BlockFacing.UP));
    Assert.Null(read.CapFastenersAt(BlockFacing.UP));
  }

  // Fails when a paste keeps the cap under the id of the world it was saved in.
  [Fact]
  public void Cap_PasteKeepsCapDropsFasteners() {
    var (world, entity, record, cap, bolt) = Standing();
    var stored = new ItemStack(cap, 1) { Id = 9000 };
    record.SetCap(BlockFacing.UP, stored, new ItemStack(bolt, 2));

    entity.OnLoadCollectibleMappings(
      world.World,
      [],
      new Dictionary<int, AssetLocation> { [9000] = cap.Code },
      0,
      true
    );

    Assert.Same(cap, record.CapAt(BlockFacing.UP)!.Item);
    Assert.Equal(cap.Id, record.CapAt(BlockFacing.UP)!.Id);
    Assert.Null(record.CapFastenersAt(BlockFacing.UP));
  }

  // Fails when a cap whose item the destination world lacks is kept.
  [Fact]
  public void Cap_PasteDropsCapOfMissingItem() {
    var (world, entity, record, cap, _) = Standing();
    var stored = new ItemStack(cap, 1) { Id = 9001 };
    record.SetCap(BlockFacing.UP, stored, null);
    world.Log.Expect(EnumLogType.Warning, "test:gone");

    entity.OnLoadCollectibleMappings(
      world.World,
      [],
      new Dictionary<int, AssetLocation> {
        [9001] = new AssetLocation("test:gone"),
      },
      0,
      true
    );

    Assert.Null(record.CapAt(BlockFacing.UP));
  }
}
