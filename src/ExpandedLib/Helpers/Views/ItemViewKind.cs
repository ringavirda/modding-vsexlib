namespace ExpandedLib.Helpers;

/// <summary>The kind an item or block's views are laid out as. Read from the drawn box of its shape,
/// or declared as <c>attributes.itemViews.kind</c> in lower case.</summary>
public enum ItemViewKind {
  /// <summary>Declared as <c>none</c>: the load pass leaves the collectible's transforms alone.</summary>
  None,

  /// <summary>A work item, held as the vanilla workitem is.</summary>
  Workpiece,

  /// <summary>A long tool or a route stage: its longest extent is at least 32 voxels and at least six times its middle extent.</summary>
  Longtool,

  /// <summary>An item with a <c>tool</c> field.</summary>
  Tool,

  /// <summary>A flat sheet: its shortest extent is at most a third of its middle, and its middle at least half its longest.</summary>
  Plate,

  /// <summary>A short, thick piece: its middle extent is at least 10 voxels and its longest under three times its middle.</summary>
  Bulk,

  /// <summary>A long piece: its longest extent is at least three times its middle.</summary>
  Bar,

  /// <summary>Any other item.</summary>
  Lump,

  /// <summary>A block whose every extent is between 15 and 16.5 voxels.</summary>
  Cube,

  /// <summary>A block with an extent beyond 16.5 voxels.</summary>
  Multicell,

  /// <summary>Any other block.</summary>
  Partial,
}
