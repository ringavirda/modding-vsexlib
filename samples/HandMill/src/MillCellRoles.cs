using ExpandedLib.Structures;

namespace HandMill;

/// <summary>The mill core's own cell roles, declared once so the layout and the block entity share
/// one key.</summary>
public static class MillCellRoles {
  /// <summary>The one cell the shaft occupies: the layout's own way of saying "the drive line joins
  /// here" without pinning the node's orientation variant.</summary>
  public static readonly CellRole Axle = CellRole.Of("axle", single: true);
}
