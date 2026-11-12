using System;
using ExpandedLib.Storage;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// <see cref="ExStorage"/>: the class table, the pose, the pile and the lean.
/// </summary>
public class ExStorageTests {
  private static StorageFacts Facts(
    double a,
    double b = 1,
    double c = 1,
    double fill = 0.5,
    int maxStack = 1,
    bool stock = false
  ) => new(a, b, c, fill, maxStack, StockPiece: stock);

  private static StorageClass ClassOf(StorageFacts f) =>
    ExStorage.Classify(f).Class;

  private static Cuboidf Box(
    float x,
    float y,
    float z,
    float w,
    float h,
    float d
  ) => new(x, y, z, x + w, y + h, z + d);

  private static readonly Cuboidf Cell = new(0, 0, 0, 16, 16, 16);

  #region Classify

  // Fails when a row of the table moves or its edge shifts: Quadrants past 8, Halves past b 8,
  // Single past 17, SingleLong past 24, a bar below 2.5, a plate under 0.85 full, a stack under 64,
  // or a stock code tested after the loose and quadrant rows.
  [Fact]
  public void Class_follows_the_table() {
    Assert.Equal(StorageClass.Quadrants, ClassOf(Facts(8, 8)));
    Assert.Equal(StorageClass.Halves, ClassOf(Facts(8.01, 8)));
    Assert.Equal(StorageClass.Halves, ClassOf(Facts(16, 8)));
    Assert.Equal(StorageClass.Single, ClassOf(Facts(16, 8.01)));
    Assert.Equal(StorageClass.Single, ClassOf(Facts(17, 16)));
    Assert.Equal(StorageClass.SingleLong, ClassOf(Facts(17.01, 2)));
    Assert.Equal(StorageClass.SingleLong, ClassOf(Facts(24, 2)));
    Assert.Equal(StorageClass.Exempt, ClassOf(Facts(24.01, 2)));

    Assert.Equal(StorageClass.Pile, ClassOf(Facts(7.5, 3, 1, 0.6, 64)));
    Assert.NotEqual(StorageClass.Pile, ClassOf(Facts(7.49, 3, 1, 0.6, 64)));
    Assert.NotEqual(StorageClass.Pile, ClassOf(Facts(7.5, 3, 1, 0.59, 64)));
    Assert.Equal(StorageClass.Pile, ClassOf(Facts(12, 10, 2, 0.85, 64)));
    Assert.NotEqual(StorageClass.Pile, ClassOf(Facts(12, 10, 2, 0.84, 64)));
    Assert.NotEqual(StorageClass.Pile, ClassOf(Facts(7.5, 3, 1, 0.6, 1)));

    Assert.Equal(StorageClass.Loose, ClassOf(Facts(6, 6, 6, 1, 64)));
    Assert.NotEqual(StorageClass.Loose, ClassOf(Facts(6, 6, 6, 1, 63)));
    Assert.Equal(
      StorageClass.Piece,
      ClassOf(Facts(6, 6, 6, 1, 64, stock: true))
    );
    Assert.Equal(
      StorageClass.Piece,
      ClassOf(Facts(8, 4, 4, 1, 1, stock: true))
    );
  }

  // Fails when the stock row cannot reach slot size or hands an over-long piece a layout.
  [Fact]
  public void A_stock_piece_is_classed_by_its_largest_piece() {
    Assert.Equal(
      "Quadrants",
      ExStorage.Classify(Facts(8, 4, 4, stock: true)).Layout
    );
    Assert.Equal(
      "Halves",
      ExStorage.Classify(Facts(16, 8, 4, stock: true)).Layout
    );
    Assert.Equal(
      "SingleCenter",
      ExStorage.Classify(Facts(17, 9, 4, stock: true)).Layout
    );
    StorageSpec over = ExStorage.Classify(Facts(17.01, 2, 2, stock: true));
    Assert.Equal(StorageClass.Exempt, over.Class);
    Assert.Equal(ExStorage.RackReason, over.Reason);
  }

  // Fails when Kept, a tool or an unplaceable def is read as a size.
  [Fact]
  public void Kept_tools_and_unplaceables_come_before_the_size_rows() {
    Assert.Equal(StorageClass.Kept, ClassOf(Facts(30) with { Kept = true }));
    Assert.Equal(
      StorageClass.Lean,
      ClassOf(Facts(30) with { UnderTools = true })
    );
    StorageSpec none = ExStorage.Classify(
      Facts(0) with {
        Unplaceable = "no shape",
      }
    );
    Assert.Equal(StorageClass.Exempt, none.Class);
    Assert.Equal("no shape", none.Reason);
  }

  #endregion

  #region Pose

  // Fails when the translation ignores where the box sits or what the turn did to it.
  [Fact]
  public void Pose_centres_and_floors() {
    Cuboidf bounds = new(1, 2, 3, 4, 7, 12);
    ModelTransform t = ExStorage.Pose(bounds, Cell);
    Cuboidf posed = ExStorage.Posed(bounds, t);

    Assert.Equal(8, (posed.X1 + posed.X2) / 2, 3);
    Assert.Equal(8, (posed.Z1 + posed.Z2) / 2, 3);
    Assert.Equal(0, posed.Y1, 3);
    Assert.Equal(3, posed.Y2 - posed.Y1, 3);
    Assert.Equal(9, posed.X2 - posed.X1, 3);
    Assert.Equal(5, posed.Z2 - posed.Z1, 3);
  }

  // Fails when a slot centre or floor other than the block's is ignored.
  [Fact]
  public void Pose_follows_the_slot_box() {
    Cuboidf slot = new(2, 4, 6, 10, 9, 12);
    Cuboidf bounds = Box(0, 0, 0, 3, 2, 6);
    Cuboidf posed = ExStorage.Posed(bounds, ExStorage.Pose(bounds, slot));

    Assert.Equal(6, (posed.X1 + posed.X2) / 2, 3);
    Assert.Equal(9, (posed.Z1 + posed.Z2) / 2, 3);
    Assert.Equal(4, posed.Y1, 3);
    Assert.Equal(6, posed.X2 - posed.X1, 3);
  }

  // Fails when a turn is not a multiple of 90 degrees: a square plan box would turn 45.
  [Fact]
  public void Pose_turns_in_quarters() {
    Cuboidf[] boxes =
    [
      Box(0, 0, 0, 5, 1, 5),
      Box(0, 0, 0, 3, 5, 9),
      Box(2, 1, 0, 9, 3, 5),
      Box(0, 0, 0, 4, 4, 4),
    ];
    foreach (Cuboidf box in boxes) {
      ModelTransform t = ExStorage.Pose(box, Cell);
      foreach (
        float angle in new[] { t.Rotation.X, t.Rotation.Y, t.Rotation.Z }
      )
        Assert.Equal(0, Math.Abs(angle) % 90, 3);
    }
  }

  #endregion

  #region Pile

  // Fails when layers round up or the 64 cap is dropped.
  [Fact]
  public void Pile_capacity() {
    Assert.Equal(64, ExStorage.Classify(Facts(15.25, 2, 1, 0.9, 64)).Capacity);
    Assert.Equal(16, ExStorage.Classify(Facts(12, 10, 1, 1, 64)).Capacity);
    Assert.Equal((8, 16), ExStorage.Pile(15.25, 2, 1, bar: true));
    Assert.Equal((1, 5), ExStorage.Pile(12, 10, 3, bar: false));
  }

  // Fails when the layer scale is set by the thickness or the layers are not counted into the height.
  [Fact]
  public void Pile_box_height() {
    StorageSpec pigs = ExStorage.Classify(Facts(15.25, 2, 1, 0.9, 64));
    Assert.Equal(0.125, pigs.CbScaleYByLayer, 6);
    Assert.Equal(0.5, ExStorage.BoxHeight(pigs, 64), 6);
    Assert.Equal(0.0625, ExStorage.BoxHeight(pigs, 1), 6);

    StorageSpec plates = ExStorage.Classify(Facts(12, 10, 2, 1, 64));
    Assert.Equal(1, plates.ItemsPerLayer);
    Assert.Equal(1.0, ExStorage.BoxHeight(plates, 8), 6);
  }

  #endregion

  #region Lean

  // Fails when the lean returns the lying pose.
  [Fact]
  public void Lean_stands_the_item() {
    Cuboidf bounds = Box(0, 0, 0, 12, 2, 1);
    ModelTransform t = ExStorage.Lean(bounds);
    Cuboidf posed = ExStorage.Posed(bounds, t);
    float height = posed.Y2 - posed.Y1;

    Assert.InRange(height, 11.5, 12.5);
    Assert.True(height > posed.X2 - posed.X1);
    Assert.True(height > posed.Z2 - posed.Z1);
    Assert.Equal(0, posed.Y1, 3);
    Assert.Equal(0, posed.Z1, 3);
    Assert.Equal(8, (posed.X1 + posed.X2) / 2, 3);
  }

  #endregion

  #region Shelf

  // Fails when the shelf rule picks a bigger slot than the smallest that holds the item.
  [Fact]
  public void Shelf_takes_the_smallest_slot_that_holds() {
    Cuboidf q = Box(0, 0, 0, 6, 6, 5);
    Cuboidf h = Box(0, 0, 0, 12, 6, 5);
    Cuboidf s = Box(0, 0, 0, 14, 6, 6);
    Assert.Equal("Quadrants", ExStorage.ShelfFor(6, 5, 5, q, h, s));
    Assert.Equal("Halves", ExStorage.ShelfFor(12, 5, 5, q, h, s));
    Assert.Equal("SingleCenter", ExStorage.ShelfFor(14, 6, 6, q, h, s));
    Assert.Null(ExStorage.ShelfFor(14.1, 6, 6, q, h, s));
    Assert.Null(ExStorage.ShelfFor(6, 5, 6.1, q, h, s));
  }

  #endregion

  #region ExFitsOneBlock

  private sealed class Sized(double length) : Item, IStoredSize {
    public double PosedLength(ItemStack stack) => length;
  }

  private static EnumHandling Placed(double length) {
    var world = new ExpandedLib.Testing.TestWorld();
    ExpandedLib.Testing.TestPlayer player = world.Player();
    player.Entity.Controls.ShiftKey = true;
    var item = new Sized(length);
    var slot = new DummySlot(new ItemStack(item));
    var behavior = new ExFitsOneBlock(item);
    var handHandling = EnumHandHandling.NotHandled;
    var handling = EnumHandling.PassThrough;
    behavior.OnHeldInteractStart(
      slot,
      player.Entity,
      new BlockSelection { Position = new BlockPos(0, 1, 0) },
      null!,
      true,
      ref handHandling,
      ref handling
    );
    return handling;
  }

  // Fails when a piece of exactly one block is refused or a longer one is let through.
  [Fact]
  public void Too_long_a_piece_is_refused() {
    Assert.Equal(EnumHandling.PreventSubsequent, Placed(17.5));
    Assert.Equal(EnumHandling.PassThrough, Placed(17));
  }

  #endregion
}
