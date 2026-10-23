using Vintagestory.API.MathTools;

namespace ExpandedLib.Helpers;

/// <summary>Where an item is held by each hand, as points of its shape in voxels.</summary>
/// <param name="Right">The point the right hand holds, which lands on the right anchor.</param>
/// <param name="Left">The point the left hand holds, which the idle clip puts the left anchor on.</param>
internal sealed record ItemGrips(Vec3d Right, Vec3d Left);
