// exlib-owned right-click construction behavior under the JSON behavior name
// "ExRightClickConstructable"; on 1.22 subclasses vanilla, on legacy is a full reimplementation.
using ExpandedLib.Machines;
using ExpandedLib.Registries;
using Vintagestory.API.Common;

namespace ExpandedLib.Blocks;

#if GAME_GE_1_22
using System;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

[BlockEntityBehaviorRegister("ExRightClickConstructable", PrefixModId = false)]
public class ExRightClickConstructable(BlockEntity blockentity)
  : BEBehaviorRightClickConstructable(blockentity),
    IProductionReadiness
{
  /// <summary>The materials this block would scatter at <paramref name="ratio"/> (0..1) of the
  /// consumed stacks, across every completed stage.</summary>
  public ItemStack[] GetConstructionDrops(float ratio, Random rand)
  {
    int built = rcc.CurrentCompletedStage;
    rcc.CurrentCompletedStage = built + 1;
    try
    {
      return rcc.GetDrops(ratio, rand);
    }
    finally
    {
      rcc.CurrentCompletedStage = built;
    }
  }

  /// <summary>Whether construction gates production, read from the <c>gatesProduction</c> JSON
  /// property (default true).</summary>
  public bool GatesProduction { get; private set; } = true;

  public override void Initialize(ICoreAPI api, JsonObject properties)
  {
    base.Initialize(api, properties);
    GatesProduction = properties["gatesProduction"].AsBool(true);
  }

  /// <summary>Ready once construction is complete, or always when <see cref="GatesProduction"/>
  /// opts out.</summary>
  public bool IsReadyToProduce => !GatesProduction || IsComplete;

  /// <summary>Stops the production tick while unfinished, unless <see cref="GatesProduction"/>
  /// opts out.</summary>
  public bool StopsProductionWhenNotReady => GatesProduction;

  // The salvage fraction, from the owning mod's config when it registered one, else JSON/default.
  private float EffectiveBrokenDropsRatio =>
    ExRccSettings.BrokenDropsRatio(Block.Code.Domain) ?? brokenDropsRatio;

  /// <summary>Replaces vanilla's break handler so a broken structure refunds all completed stages
  /// at the configured salvage fraction.</summary>
  public override void OnBlockBroken(IPlayer? byPlayer = null)
  {
    if (byPlayer?.WorldData.CurrentGameMode == EnumGameMode.Creative)
      return;
    foreach (
      var drop in GetConstructionDrops(
        EffectiveBrokenDropsRatio,
        Api.World.Rand
      )
    )
      Api.World.SpawnItemEntity(drop, Pos, null);
  }
}
#else
using System;
using Vintagestory.API.Client;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

[BlockEntityBehaviorRegister("ExRightClickConstructable", PrefixModId = false)]
public class ExRightClickConstructable
  : BlockEntityBehavior,
    IInteractable,
    IProductionReadiness {
  private readonly ExRightClickConstruction rcc = new();
  private float brokenDropsRatio = 1f;

  public CompositeShape shape { get; protected set; }
  public bool IsComplete => rcc.CurrentCompletedStage == rcc.Stages.Length - 1;
  public event Action<CompositeShape>? OnShapeChanged;

  /// <summary>Whether construction gates production, read from the <c>gatesProduction</c> JSON
  /// property (default true).</summary>
  public bool GatesProduction { get; private set; } = true;

  /// <summary>Ready once construction is complete, or always when <see cref="GatesProduction"/>
  /// opts out.</summary>
  public bool IsReadyToProduce => !GatesProduction || IsComplete;

  /// <summary>Stops the production tick while unfinished, unless <see cref="GatesProduction"/>
  /// opts out.</summary>
  public bool StopsProductionWhenNotReady => GatesProduction;

  public ExRightClickConstructable(BlockEntity blockentity)
    : base(blockentity) {
    shape = blockentity.Block.Shape;
  }

  public override void Initialize(ICoreAPI api, JsonObject properties) {
    base.Initialize(api, properties);
    brokenDropsRatio = properties["brokenDropsRatio"].AsFloat(1f);
    GatesProduction = properties["gatesProduction"].AsBool(true);
    var stages = properties["stages"].AsObject<ExConstructionStage[]>(null);
    rcc.LateInit(stages, api, "Block " + Block.Code);
    UpdateShape();
  }

  public bool OnBlockInteractStart(
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection blockSel,
    ref EnumHandling handling
  ) {
    handling = EnumHandling.PreventDefault;
    if (rcc.OnInteract(byPlayer.Entity, byPlayer.Entity.RightHandItemSlot)) {
      UpdateShape();
      Blockentity.MarkDirty(true);
    }
    return true;
  }

  public override void FromTreeAttributes(
    ITreeAttribute tree,
    IWorldAccessor world
  ) {
    base.FromTreeAttributes(tree, world);
    rcc.FromTreeAttributes(tree);
  }

  public override void ToTreeAttributes(ITreeAttribute tree) {
    rcc.ToTreeAttributes(tree);
    base.ToTreeAttributes(tree);
    UpdateShape();
  }

  public override bool OnTesselation(
    ITerrainMeshPool mesher,
    ITesselatorAPI tessThreadTesselator
  ) => true;

  public override void GetBlockInfo(
    IPlayer forPlayer,
    System.Text.StringBuilder dsc
  ) {
    base.GetBlockInfo(forPlayer, dsc);
    if (Api.World.EntityDebugMode)
      dsc.AppendLine(
        $"<font color='#ccc'>construction stage= {rcc.CurrentCompletedStage} of {rcc.Stages.Length}</font>"
      );
  }

  // The salvage fraction, from the owning mod's config when it registered one, else JSON/default.
  private float EffectiveBrokenDropsRatio =>
    ExRccSettings.BrokenDropsRatio(Block.Code.Domain) ?? brokenDropsRatio;

  public override void OnBlockBroken(IPlayer? byPlayer = null) {
    if (byPlayer?.WorldData.CurrentGameMode != EnumGameMode.Creative)
      foreach (
        var drop in rcc.GetDrops(EffectiveBrokenDropsRatio, Api.World.Rand)
      )
        Api.World.SpawnItemEntity(drop, Pos.ToVec3d());
  }

  /// <summary>The materials this block would scatter at <paramref name="ratio"/> (0..1) of the
  /// consumed stacks.</summary>
  public ItemStack[] GetConstructionDrops(float ratio, Random rand) =>
    rcc.GetDrops(ratio, rand);

  /// <summary>The next-stage build-material hover help.</summary>
  public WorldInteraction[]? GetConstructionInteractionHelp() =>
    rcc.GetInteractionHelp();

  /// <summary>Prepends the construction help of the block-entity at the selection (if it has this
  /// behavior) to <paramref name="baseHelp"/>.</summary>
  public static WorldInteraction[] AppendConstructionHelp(
    IWorldAccessor world,
    BlockSelection selection,
    WorldInteraction[] baseHelp
  ) {
    var help = world
      .BlockAccessor.GetBlockEntity(selection.Position)
      ?.GetBehavior<ExRightClickConstructable>()
      ?.GetConstructionInteractionHelp();
    if (help == null || help.Length == 0)
      return baseHelp;
    if (baseHelp == null || baseHelp.Length == 0)
      return help;
    var combined = new WorldInteraction[help.Length + baseHelp.Length];
    help.CopyTo(combined, 0);
    baseHelp.CopyTo(combined, help.Length);
    return combined;
  }

  private void UpdateShape() {
    shape = new CompositeShape {
      Base = Block.Shape.Base,
      rotateY = Block.Shape.rotateY,
      SelectiveElements = rcc.getShapeElements(),
    };
    OnShapeChanged?.Invoke(shape);
  }
}
#endif
