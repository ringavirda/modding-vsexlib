using Vintagestory.API.MathTools;

namespace ExpandedLib.Industry.MechanicalPower;

/// <summary>A set of a block's shape elements that only turn, drawn by the instanced renderer at the
/// angle of the run that holds one of the block's nodes.</summary>
/// <param name="Elements">Element paths in the block's shape, as <see cref="ExpandedLib.Helpers.ExShapeElements.Matches"/> reads them; a path names its element and everything under it.</param>
/// <param name="Slot">Slot of the node whose run turns the part.</param>
/// <param name="Ratio">Turns of the part per turn of the node, above zero.</param>
/// <param name="Sign">+1 when the part turns the way its node does, -1 against it.</param>
/// <param name="Axis">The shape-space axis the part turns about.</param>
/// <param name="Pivot">A point on the axis in shape-space voxels (16 to the block); null for the cell centre, (8, 8, 8).</param>
public sealed record TurningPart(
  string[] Elements,
  byte Slot = 0,
  float Ratio = 1f,
  int Sign = 1,
  EnumAxis Axis = EnumAxis.Z,
  Vec3f? Pivot = null
);
