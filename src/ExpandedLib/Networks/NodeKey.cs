using System;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Networks;

/// <summary>Identity of one node in a network that holds several nodes per cell: a cell position and a slot.</summary>
/// <remarks>
/// Slot 0 is the cell's centre, 1..6 a piece lying on face N, E, S, W, U, D (bits 0-2) with its lane in
/// bits 3-4 (0 is the middle lane), and <see cref="PortBase"/> + i is port i of the machine at the cell.
/// A network type that does not use slots has one node per cell, at slot 0.
/// </remarks>
/// <param name="Pos">The cell. Callers must not mutate it after building the key.</param>
/// <param name="Slot">The slot within the cell.</param>
public readonly record struct NodeKey(BlockPos Pos, byte Slot) {
  /// <summary>The first machine-port slot.</summary>
  public const byte PortBase = 0x80;

  /// <summary>Voxels across a face, the unit of <see cref="LaneOffset"/> and <see cref="BendOffsets"/>.</summary>
  public const int FaceVoxels = 16;

  private const byte FaceMask = 0b111;
  private const int LaneShift = 3;
  private const byte LaneMask = 0b11;

  /// <summary>The node at the centre of <paramref name="pos"/>.</summary>
  public static NodeKey Centre(BlockPos pos) => new(pos, 0);

  /// <summary>The piece lying on <paramref name="face"/> of <paramref name="pos"/> on <paramref name="lane"/> (0..2).</summary>
  /// <exception cref="ArgumentOutOfRangeException"><paramref name="lane"/> is not 0, 1 or 2.</exception>
  public static NodeKey OnFace(BlockPos pos, BlockFacing face, int lane = 0) {
    if (lane is < 0 or > 2)
      throw new ArgumentOutOfRangeException(
        nameof(lane),
        lane,
        "Lane is 0..2."
      );
    return new(pos, (byte)((lane << LaneShift) | (1 + face.Index)));
  }

  /// <summary>Port <paramref name="index"/> (0..127) of the machine at <paramref name="pos"/>.</summary>
  /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is outside 0..127.</exception>
  public static NodeKey Port(BlockPos pos, int index) {
    if (index is < 0 or >= 0x80)
      throw new ArgumentOutOfRangeException(
        nameof(index),
        index,
        "Port index is 0..127."
      );
    return new(pos, (byte)(PortBase + index));
  }

  /// <summary>True for a piece lying on a face (slots 1..6 with any lane).</summary>
  public bool IsFace => Slot < PortBase && (Slot & FaceMask) is >= 1 and <= 6;

  /// <summary>True for a machine port.</summary>
  public bool IsPort => Slot >= PortBase;

  /// <summary>The face a face piece lies on, or null when the key is not a face piece.</summary>
  public BlockFacing? Face =>
    IsFace ? BlockFacing.ALLFACES[(Slot & FaceMask) - 1] : null;

  /// <summary>The lane of a face piece, 0 for every other key.</summary>
  public int Lane => IsFace ? (Slot >> LaneShift) & LaneMask : 0;

  /// <summary>The port index of a port key, or -1.</summary>
  public int PortIndex => IsPort ? Slot - PortBase : -1;

  /// <summary>
  /// Where a straight or corner piece on <paramref name="lane"/> crosses a face, in voxels from the face's low
  /// edge along its fixed in-plane axis: 8, 4, 12 for lanes 0, 1, 2.
  /// </summary>
  public static int LaneOffset(int lane) =>
    lane switch {
      0 => 8,
      1 => 4,
      2 => 12,
      _ => throw new ArgumentOutOfRangeException(
        nameof(lane),
        lane,
        "Lane is 0..2."
      ),
    };

  /// <summary>
  /// The crossing offsets of an in-plane bend on <paramref name="lane"/>, in voxels from the low edge of each
  /// arm's face. A lane is a track: it is the bend's radius about its inside corner, so the arm leaving at the
  /// far edge crosses at <see cref="FaceVoxels"/> minus the radius, not at the lane's straight offset.
  /// </summary>
  /// <param name="lane">0..2.</param>
  /// <returns>The offset at the arm whose inside corner is at the low edge, then at the arm whose corner is at the high edge.</returns>
  public static (int Low, int High) BendOffsets(int lane) {
    int radius = LaneOffset(lane);
    return (radius, FaceVoxels - radius);
  }

  public override string ToString() => $"{Pos}#{Slot}";
}
