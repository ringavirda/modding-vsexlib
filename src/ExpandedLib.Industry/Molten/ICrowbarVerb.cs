using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Industry.Molten;

/// <summary>
/// A block a crowbar acts on: it breaks something off or out of the cell on a hold. The third-person use
/// animation of a held crowbar plays as it does on a vanilla support beam while the selection is such a block
/// and <see cref="CrowbarActs"/> is true.
/// </summary>
public interface ICrowbarVerb {
  /// <summary>Whether a crowbar used on the cell at <paramref name="pos"/> would act now.</summary>
  bool CrowbarActs(IBlockAccessor blocks, BlockPos pos);
}
