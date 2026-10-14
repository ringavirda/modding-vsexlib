using Vintagestory.API.MathTools;

namespace ExpandedLib.Helpers;

/// <summary>The reference numbers the view rule copies from vanilla items: hand scales, rotations, and the
/// grip each reference holds as fractions of its drawn box.</summary>
internal static class ViewReferences {
  /// <summary>What a kind's views copy: the reference box's axes (longest, middle, shortest, in its shape's
  /// frame) and shortest extent in voxels, its hand rotation, the grip as box fractions along those axes and
  /// its distance in voxels from the nearest drawn element, and the slot rotation and ground scale.</summary>
  internal sealed record HandReference(
    Vec3d[] Axes,
    double Thickness,
    Vec3d Rotation,
    double Scale,
    double[] Grip,
    double OffDrawn,
    Vec3d GuiRotation,
    double GroundScale
  );

  private static readonly Vec3d X = new(1, 0, 0);
  private static readonly Vec3d Y = new(0, 1, 0);
  private static readonly Vec3d Z = new(0, 0, 1);

  internal static readonly HandReference Spear = new(
    [X, Y, Z],
    2.3478,
    new Vec3d(-90, 0, 0),
    0.80,
    [0.3842, 0.5325, 0.7043],
    0,
    new Vec3d(33, 0, -127),
    3.4
  );

  internal static readonly HandReference MetalPlate = new(
    [X, Z, Y],
    1,
    new Vec3d(94, 0, 0),
    0.75,
    [0.4407, 0.0692, -0.3209],
    0.3209,
    new Vec3d(-30, -44, -180),
    3.31
  );

  internal static readonly HandReference Ingot = new(
    [Z, X, Y],
    2,
    new Vec3d(0, -71, 18),
    0.75,
    [0.7432, 0.4847, 0.9476],
    0,
    new Vec3d(149, -36, 0),
    4.8
  );

  internal static readonly HandReference Rod = new(
    [X, Y, Z],
    2,
    new Vec3d(85, 0, 2),
    0.68,
    [0.2394, 0.4369, 0.8673],
    0,
    new Vec3d(-48, 134, 171),
    4
  );

  /// <summary>The game item whose hold a declared tip copies for the kinds a handle holds.</summary>
  internal const string TipHoldCode = "game:knife-generic-iron";

  /// <summary>The working end of <see cref="TipHoldCode"/> as a sign and an axis letter in its shape's frame.</summary>
  internal const string TipHoldEnd = "+x";

  /// <summary>The rotation about Y in degrees that the seraph's right-hand attachment point carries and a hand rotation adds to.</summary>
  internal const double HandYaw = 180;

  /// <summary>A fraction of the last stretch of a longtool's length that decides which end works.</summary>
  internal const double TipFraction = 0.1;

  /// <summary>The slot's item size in pixels at GUI scale 1, and the slot's own size.</summary>
  internal const double SlotItemPixels = 25.6;

  internal const double SlotPixels = 48;

  /// <summary>The share of the slot the largest projected extent of an item fills.</summary>
  internal const double SlotSpan = 0.8;

  /// <summary>The pixels the game shifts an item from the slot centre along x and y.</summary>
  internal const double SlotShiftX = 3;

  internal const double SlotShiftY = 1;

  /// <summary>The spear's slot scale and the length in voxels of the spear it was authored on; a longtool's
  /// slot scale is this close-up scaled by the ratio of lengths.</summary>
  internal const double SpearSlotScale = 2.26;

  internal const double SpearLength = 46.7475;

  /// <summary>How far along a longtool's length, from the butt, its slot origin lies.</summary>
  internal const double SpearSlotOrigin = 0.8394;

  /// <summary>Blocks per mesh unit of a ground scale of 1.</summary>
  internal const double GroundUnit = 0.2;

  internal const string SpearIdle = "spearidle";
  internal const string SpearHit = "spearhit";
  internal const string BulkHold = "holdbothhandslarge";

  /// <summary>A partial block's reference extents: the slot span as a share of the slot, and the longest
  /// extent in blocks on the ground and in the hand.</summary>
  internal const double PartialSlotSpan = 0.7101;

  internal const double PartialGroundLong = 0.300;
  internal const double PartialHeldLong = 0.400;

  /// <summary>A multicell block's reference extents, as for a partial block.</summary>
  internal const double MulticellSlotSpan = 0.8884;

  internal const double MulticellGroundLong = 0.8629;
  internal const double MulticellHeldLong = 1.200;

  /// <summary>A block lies on its largest face on the ground when its shortest extent is at most this
  /// fraction of its longest.</summary>
  internal const double LeafRatio = 4;

  /// <summary>The game's hit and use clips, which a collectible keeps when it sets none.</summary>
  internal const string DefaultHit = "breakhand";

  internal const string DefaultUse = "interactstatic";

  internal const string DefaultReady = "helditemready";

  internal const string BlockReady = "heldblockready";
  internal const string BlockUse = "twohandplaceblock";
}
