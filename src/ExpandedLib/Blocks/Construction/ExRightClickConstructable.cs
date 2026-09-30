// exlib-owned right-click construction behavior under the JSON behavior name
// "ExRightClickConstructable", one implementation on every game version.
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using ExpandedLib.Machines;
using ExpandedLib.Registries;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ExpandedLib.Blocks;

[BlockEntityBehaviorRegister("ExRightClickConstructable", PrefixModId = false)]
public class ExRightClickConstructable
  : BlockEntityBehavior,
#if GAME_GE_1_22
    IInteractableWithHelp,
#else
    IInteractable,
#endif
    IProductionReadiness {
  private readonly ExRightClickConstruction rcc = new();
  private float brokenDropsRatio = 1f;

  /// <summary>The block's shape with the elements of the stages built so far selected.</summary>
  public CompositeShape shape { get; protected set; }

  /// <summary>Whether every stage is built.</summary>
  public bool IsComplete => rcc.CurrentCompletedStage == rcc.Stages.Length - 1;

  /// <summary>The index of the last stage built, 0 before any is paid.</summary>
  public int CurrentCompletedStage => rcc.CurrentCompletedStage;

  /// <summary>Raised with the new <see cref="shape"/> whenever a stage is built or the state is
  /// saved.</summary>
  public event Action<CompositeShape>? OnShapeChanged;

  /// <summary>Asked before a stage is paid; a false answer refuses the interaction and takes
  /// nothing. The default refuses a stage paid with one stored wildcard key in two variants,
  /// except for a creative player holding Ctrl; null admits every payment.</summary>
  public System.Func<IPlayer, BlockSelection, bool>? OnAttemptConstruct;

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
    OnAttemptConstruct = AdmitsOneMaterial;
  }

  /// <summary>Reads <c>brokenDropsRatio</c>, <c>gatesProduction</c> and <c>stages</c> from
  /// <paramref name="properties"/>.</summary>
  public override void Initialize(ICoreAPI api, JsonObject properties) {
    base.Initialize(api, properties);
    brokenDropsRatio = properties["brokenDropsRatio"].AsFloat(1f);
    GatesProduction = properties["gatesProduction"].AsBool(true);
    var stages = properties["stages"].AsObject<ExConstructionStage[]>(null);
    rcc.LateInit(
      stages,
      api,
      () => Pos.ToVec3d().Add(0.5, 0.5, 0.5),
      "Block " + Block.Code
    );
    UpdateShape();
  }

  private bool AdmitsOneMaterial(IPlayer byPlayer, BlockSelection blockSel) {
    if (rcc.CurrentCompletedStage >= rcc.Stages.Length - 1)
      return true;
    if (
      byPlayer.WorldData.CurrentGameMode == EnumGameMode.Creative
      && byPlayer.Entity.Controls.CtrlKey
    )
      return true;
    ExConstructionIngredient[]? required = rcc.Stages[
      rcc.CurrentCompletedStage + 1
    ].RequireStacks;
    if (required == null)
      return true;
    var ingredients = new List<(CraftingRecipeIngredient, string?)>();
    foreach (ExConstructionIngredient ingredient in required) {
      if (ingredient.StoreWildCard == null)
        continue;
      ExConstructionIngredient filled = ingredient.Clone();
      foreach ((string key, string value) in rcc.StoredWildCards)
        filled.FillPlaceHolder(key, value);
      filled.Resolve(Api.World, "construction stage of " + Block.Code);
      ingredients.Add((filled, filled.StoreWildCard));
    }
    if (
      ConstructionPayment.MixedVariant(
        ingredients,
        byPlayer.InventoryManager.GetHotbarInventory()
      )
      is not { } mixed
    )
      return true;
    ConstructionPayment.Refuse(Api, this, mixed);
    return false;
  }

  /// <summary>Pays the next stage from the player's hotbar when <see cref="OnAttemptConstruct"/>
  /// admits it, then updates <see cref="shape"/> and marks the block entity dirty. Always
  /// prevents the default handling.</summary>
  /// <returns>False when <see cref="OnAttemptConstruct"/> refused; otherwise true, whether or
  /// not a stage was built.</returns>
  public bool OnBlockInteractStart(
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection blockSel,
    ref EnumHandling handling
  ) {
    handling = EnumHandling.PreventDefault;
    if (OnAttemptConstruct != null && !OnAttemptConstruct(byPlayer, blockSel))
      return false;
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
    if (
      Api.World.EntityDebugMode
      && forPlayer?.WorldData?.CurrentGameMode == EnumGameMode.Creative
    )
      dsc.AppendLine(
        $"<font color='#ccc'>construction stage= {rcc.CurrentCompletedStage} of {rcc.Stages.Length}</font>"
      );
  }

  // The salvage fraction, from the owning mod's config when it registered one, else JSON/default.
  private float EffectiveBrokenDropsRatio =>
    ExRccSettings.BrokenDropsRatio(Block.Code.Domain) ?? brokenDropsRatio;

  /// <summary>Scatters the materials of every completed stage at the configured salvage fraction;
  /// nothing for a creative player.</summary>
  /// <param name="byPlayer">Who broke the block; may be null.</param>
  public override void OnBlockBroken(IPlayer? byPlayer = null) {
    if (byPlayer?.WorldData.CurrentGameMode == EnumGameMode.Creative)
      return;
    foreach (
      var drop in GetConstructionDrops(
        EffectiveBrokenDropsRatio,
        Api.World.Rand
      )
    )
#if GAME_GE_1_22
      Api.World.SpawnItemEntity(drop, Pos, null);
#else
      Api.World.SpawnItemEntity(drop, Pos.ToVec3d());
#endif
  }

  /// <summary>The materials this block would scatter at <paramref name="ratio"/> (0..1) of the
  /// consumed stacks, across every completed stage.</summary>
  [MethodImpl(MethodImplOptions.NoInlining)]
  public ItemStack[] GetConstructionDrops(float ratio, Random rand) =>
    rcc.GetDrops(ratio, rand);

  /// <summary>The next-stage build-material hover help.</summary>
  /// <returns>Null when construction is complete or the next stage requires nothing. The
  /// interactions are shared; callers do not mutate them.</returns>
  public WorldInteraction[]? GetConstructionInteractionHelp() =>
    rcc.GetInteractionHelp();

  /// <summary>The hover help of a placed block: the next construction stage's materials.</summary>
  /// <returns>Null when construction is complete or the next stage requires nothing.</returns>
  public WorldInteraction[]? GetPlacedBlockInteractionHelp(
    IWorldAccessor world,
    BlockSelection selection,
    IPlayer forPlayer,
    ref EnumHandling handling
  ) => rcc.GetInteractionHelp();

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
