namespace ExpandedLib.Storage;

/// <summary>The ground-storage class an item variant falls in, in the order
/// <see cref="ExStorage.Classify"/> tests them.</summary>
public enum StorageClass {
  /// <summary>Already carries GroundStorable; left as written.</summary>
  Kept,

  /// <summary>A tool leaning on a wall.</summary>
  Lean,

  /// <summary>A stock piece, one to a slot.</summary>
  Piece,

  /// <summary>A small or powdered stack in a loose heap, twelve to a block.</summary>
  Loose,

  /// <summary>A bar or plate stacked in layers.</summary>
  Pile,

  /// <summary>Four to a block.</summary>
  Quadrants,

  /// <summary>Two to a block.</summary>
  Halves,

  /// <summary>One, centred.</summary>
  Single,

  /// <summary>One, centred, overhanging its cell by up to 4 voxels at each end.</summary>
  SingleLong,

  /// <summary>Never stored; <see cref="StorageSpec.Reason"/> says why.</summary>
  Exempt,
}
