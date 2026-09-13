using System.Linq;
using ExpandedLib.Registries;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace Grains;

/// <summary>
/// Names the grains a block accepts in its placed-block info, so a mill core reads
/// <c>.Behavior&lt;BlockBehaviorGrainInfo&gt;()</c> and gets the catalogue's own wording rather than
/// duplicating it. Resolves to <c>grains.BlockBehaviorGrainInfo</c> through this assembly's own
/// <c>[assembly: ExDomain]</c> rather than the calling block's - the cross-assembly key path this
/// sample exists to prove.
/// </summary>
[BlockBehaviorRegister]
public class BlockBehaviorGrainInfo : BlockBehavior {
  public BlockBehaviorGrainInfo(Block block)
    : base(block) { }

  public override string GetPlacedBlockInfo(
    IWorldAccessor world,
    BlockPos pos,
    IPlayer forPlayer
  ) =>
    Lang.Get(
      "grains:accepts",
      string.Join(", ", GrainCatalogue.All.Select(g => g.Code))
    );
}
