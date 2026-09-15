using System.Collections.Generic;
using BurdenMaker.BlockEntities;
using ExpandedLib.Definitions;
using ExpandedLib.Helpers;
using ExpandedLib.Registries;
using ExpandedLib.Structures;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace BurdenMaker.Blocks;

/// <summary>A 9-cell mega-block stock house: two hoppers drain together into a shared basin
/// through one sliding gate. Has no mechanism, no MP port.</summary>
[BlockRegister]
public partial class BlockBurdenmaker
  : BlockFilledMegastructure,
    IFillerHost,
    IFillerInteractionTarget,
    IExBlockDefProvider {
  #region Code-first definition

  /// <summary>The burdenmaker blocktype: a 9-cell footprint and a five-stage right-click
  /// construction matching the shape's element groups.</summary>
  public static IEnumerable<ExBlockDef> Definitions(string domain) =>
    [
      ExBlockDef
        .Create(domain, "burdenmaker", "ore/burdenmaker")
        .Class<BlockBurdenmaker>()
        .EntityClass<BlockEntityBurdenmaker>()
        .Material(EnumBlockMaterial.Ceramic)
        .MiningTier(0)
        .Resistance(3.5f)
        .MaxStackSize(1)
        .NoDrops()
        .FillerOffsets(
          StructureFootprint.Layout(f =>
            f.Origin(-1, -1)
              .Layer(
                0,
                """
                # # #
                # O #
                """
              )
              .Layer(
                1,
                """
                # # #
                . . .
                """
              )
          )
        )
        .Behavior("ExOrientable")
        .Behavior("BlockEntityInteract")
        .EntityBehavior("Animatable")
        .Construction(c =>
          c.Stage(s => s.AddElements("Root/Base"))
            .Stage(s =>
              s.Require(
                  "game:burnedbrick-{brick}",
                  12,
                  "burdenmaker:rcc-ingredient-brick"
                )
                .AddElements("Root/BaseExtension")
            )
            .Stage(s =>
              s.Require(
                  "game:burnedbrick-{brick}",
                  16,
                  "burdenmaker:rcc-ingredient-brick"
                )
                .AddElements("Root/HopperMasonry")
            )
            .Stage(s =>
              s.Require(
                  "game:metalplate-iron",
                  6,
                  "burdenmaker:rcc-ingredient-hopperplate"
                )
                .AddElements("Root/Hoppers")
            )
            .Stage(s =>
              s.Require(
                  "game:metalplate-iron",
                  3,
                  "burdenmaker:rcc-ingredient-lidplate"
                )
                .Require(
                  "game:ingot-iron",
                  2,
                  "burdenmaker:rcc-ingredient-lidrails"
                )
                .AddElements("Root/Lids")
            )
        )
        .VariantGroup(
          "brick",
          "black",
          "brown",
          "cream",
          "gray",
          "orange",
          "red",
          "tan"
        )
        .SideVariant()
        .CreativeTab("general", "*-cream-n")
        .CreativeTab("burdenmaker", "*-cream-n")
        .ShapeSpunPerOrientation("burdenmaker:ore/burdenmaker")
        // The placed shell before any stage completes: basin floor only.
        .ShapeSelectiveElements("Root/Base/*")
        .Texture(
          "fire1",
          "game:block/clay/brick/four/running/cream1",
          "game:block/clay/brick/four/running/{brick}1"
        )
        .SingleSelectionBox(0f, 0f, 0f, 1f, 1f, 1f)
        .SingleCollisionBox(0f, 0f, 0f, 1f, 1f, 1f)
        .SideSolid(false)
        .SideOpaque(false)
        .Sound("place", "game:block/ceramicplace")
        .Sound("break", "game:block/ceramic")
        .Sound("hit", "game:block/ceramic")
        .Sound("walk", "game:walk/stone"),
    ];

  #endregion

  /// <summary>Structure/filler rotation, with no extra offset.</summary>
  public override int StructureAngle =>
    ExOrientation.AngleFromSide(Variant["side"]);

  #region Drops

  // Placement, the filler footprint and break-time filler removal are handled by BlockFilledMegastructure.

  // Returns the construction materials and both hopper/basin contents; never the block itself.
  public override ItemStack[] GetDrops(
    IWorldAccessor world,
    BlockPos pos,
    IPlayer? byPlayer,
    float dropQuantityMultiplier = 1f
  ) => [];

  #endregion

  #region Cell classification

  /// <summary>What a footprint cell of the burdenmaker is.</summary>
  public enum BurdenmakerCell {
    /// <summary>The wide upper hopper - crushed iron ore. Two cells.</summary>
    OreHopper,

    /// <summary>The narrow upper hopper - lime. One cell.</summary>
    FluxHopper,

    /// <summary>The principal: the sliding lid under both hoppers.</summary>
    Gate,

    /// <summary>The shared basin the burden collects in. Five cells.</summary>
    Bunker,

    /// <summary>Not part of this machine.</summary>
    Outside,
  }

  /// <summary>Classifies the world cell <paramref name="clicked"/> relative to a burdenmaker at
  /// <paramref name="principal"/> with structure angle <paramref name="structureAngle"/>.</summary>
  public static BurdenmakerCell Classify(
    BlockPos principal,
    BlockPos clicked,
    int structureAngle
  ) {
    // World delta, rotated into the authored north frame; RotateOffset normalises negative angles.
    Vec3i local = ExOrientation.RotateOffset(
      clicked.X - principal.X,
      clicked.Y - principal.Y,
      clicked.Z - principal.Z,
      -structureAngle
    );

    // y = 1 is the hopper row (z = -1); y = 0 is the basin, principal is the gate.
    return (local.X, local.Y, local.Z) switch {
      ( >= -1 and <= 0, 1, -1) => BurdenmakerCell.OreHopper,
      (1, 1, -1) => BurdenmakerCell.FluxHopper,
      (0, 0, 0) => BurdenmakerCell.Gate,
      ( >= -1 and <= 1, 0, -1 or 0) => BurdenmakerCell.Bunker,
      _ => BurdenmakerCell.Outside,
    };
  }

  #endregion

  #region Interaction

  /// <summary>Handles a click on the principal cell, the gate; other cells arrive through the
  /// fillers.</summary>
  public override bool OnBlockInteractStart(
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection blockSel
  ) =>
    HandleInteract(world, byPlayer, blockSel, blockSel.Position)
    ?? base.OnBlockInteractStart(world, byPlayer, blockSel);

  /// <summary>Routes a click to its cell's action. Returns null before construction completes.</summary>
  private bool? HandleInteract(
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection sel,
    BlockPos clickedCell
  ) {
    if (
      world.BlockAccessor.GetBlockEntity(sel.Position)
        is not BlockEntityBurdenmaker be
      || !be.IsConstructed
    )
      return null; // pre-construction clicks drive the RCC behaviour

    BurdenmakerCell cell = Classify(be.Pos, clickedCell, StructureAngle);
    if (cell == BurdenmakerCell.Outside)
      return null;

    if (world.Side == EnumAppSide.Client)
      return true; // the server owns every mutation below; the click is still consumed

    ItemSlot? active = byPlayer.InventoryManager?.ActiveHotbarSlot;
    // Ctrl, not sneak: vanilla ground-storage placement already uses sneak + right-click.
    bool wholeStack = byPlayer.Entity.Controls.CtrlKey;

    switch (cell) {
      case BurdenmakerCell.Gate:
        if (!be.ToggleGate(out string? error) && error != null)
          (byPlayer as IServerPlayer)?.SendIngameError(error);
        break;

      case BurdenmakerCell.OreHopper:
        if (active?.Empty == false)
          be.TryLoadOre(active, wholeStack);
        else
          GiveBack(world, byPlayer, sel, be.TryTakeOre());
        break;

      case BurdenmakerCell.FluxHopper:
        if (active?.Empty == false)
          be.TryLoadFlux(active, wholeStack);
        else
          GiveBack(world, byPlayer, sel, be.TryTakeFlux());
        break;

      case BurdenmakerCell.Bunker:
        // Take-only, whatever is held: the basin is filled by the gate and by nothing else.
        GiveBack(world, byPlayer, sel, be.TryWithdrawBurden());
        break;
    }

    // Swallowed on both sides; no block is placed against the machine's face.
    return true;
  }

  private static void GiveBack(
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection sel,
    ItemStack? taken
  ) {
    if (taken == null)
      return;
    if (byPlayer.InventoryManager?.TryGiveItemstack(taken) != true)
      world.SpawnItemEntity(taken, sel.Position.ToVec3d().Add(0.5, 1.0, 0.5));
  }

  #endregion

  #region Interaction help

  // Resolved once and cached: walks every collectible in the game.
  private ItemStack[]? _oreStacks;
  private ItemStack[]? _fluxStacks;

  /// <summary>Help for the principal (gate) cell; other cells arrive through
  /// <see cref="IFillerInteractionTarget.GetFillerInteractionHelp"/>.</summary>
  public override WorldInteraction[] GetPlacedBlockInteractionHelp(
    IWorldAccessor world,
    BlockSelection selection,
    IPlayer forPlayer
  ) => BuildInteractionHelp(world, selection, forPlayer, BurdenmakerCell.Gate);

  /// <summary>Hints for the cell being looked at, routed through <see cref="Classify"/>. Defers to
  /// the base help before construction completes.</summary>
  private WorldInteraction[] BuildInteractionHelp(
    IWorldAccessor world,
    BlockSelection selection,
    IPlayer forPlayer,
    BurdenmakerCell cell
  ) {
    WorldInteraction[] baseHelp = base.GetPlacedBlockInteractionHelp(
      world,
      selection,
      forPlayer
    );

    if (
      world.BlockAccessor.GetBlockEntity(selection.Position)
        is not BlockEntityBurdenmaker be
      || !be.IsConstructed
    )
      return baseHelp; // the RCC behaviour supplies the construction help

    var help = new List<WorldInteraction>();
    switch (cell) {
      case BurdenmakerCell.OreHopper:
        AddLoadHints(
          help,
          "burdenmaker:burdenmaker-help-addore",
          "burdenmaker:burdenmaker-help-addore-stack",
          _oreStacks ??= ResolveStacks(BlockEntityBurdenmaker.IsOre)
        );
        break;

      case BurdenmakerCell.FluxHopper:
        AddLoadHints(
          help,
          "burdenmaker:burdenmaker-help-addflux",
          "burdenmaker:burdenmaker-help-addflux-stack",
          _fluxStacks ??= ResolveStacks(BlockEntityBurdenmaker.IsFlux)
        );
        break;

      case BurdenmakerCell.Gate:
        help.Add(
          new WorldInteraction {
            ActionLangCode = "burdenmaker:burdenmaker-help-gate",
            MouseButton = EnumMouseButton.Right,
          }
        );
        break;
    }

    // Every cell except the gate hands something back to an empty hand.
    if (cell != BurdenmakerCell.Gate && cell != BurdenmakerCell.Outside)
      help.Add(
        new WorldInteraction {
          ActionLangCode = "burdenmaker:burdenmaker-help-take",
          MouseButton = EnumMouseButton.Right,
        }
      );

    return [.. help, .. baseHelp];
  }

  private static void AddLoadHints(
    List<WorldInteraction> help,
    string one,
    string stack,
    ItemStack[] accepted
  ) {
    help.Add(
      new WorldInteraction {
        ActionLangCode = one,
        MouseButton = EnumMouseButton.Right,
        Itemstacks = accepted,
      }
    );
    // Ctrl, matching the interaction itself; vanilla ground-storage placement holds sneak.
    help.Add(
      new WorldInteraction {
        ActionLangCode = stack,
        MouseButton = EnumMouseButton.Right,
        HotKeyCode = "ctrl",
        Itemstacks = accepted,
      }
    );
  }

  /// <summary>A representative stack of every collectible the hopper's role check accepts.</summary>
  private ItemStack[] ResolveStacks(System.Func<ItemStack?, bool> accepts) {
    if (api?.World?.Collectibles is not { } collectibles)
      return [];

    var stacks = new List<ItemStack>();
    foreach (CollectibleObject collectible in collectibles) {
      if (collectible?.Code == null)
        continue;
      var stack = new ItemStack(collectible);
      if (accepts(stack))
        stacks.Add(stack);
    }
    return [.. stacks];
  }

  #endregion

  #region Filler interaction forwarding

  // A click on any reserved footprint cell drives the principal; the clicked cell selects the action.
  bool IFillerInteractionTarget.OnFillerInteractStart(
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection principalSel,
    BlockPos clickedCell
  ) =>
    HandleInteract(world, byPlayer, principalSel, clickedCell)
    ?? base.OnBlockInteractStart(world, byPlayer, principalSel);

  bool IFillerInteractionTarget.OnFillerInteractStep(
    float secondsUsed,
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection principalSel,
    BlockPos clickedCell
  ) => base.OnBlockInteractStep(secondsUsed, world, byPlayer, principalSel);

  void IFillerInteractionTarget.OnFillerInteractStop(
    float secondsUsed,
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection principalSel,
    BlockPos clickedCell
  ) => base.OnBlockInteractStop(secondsUsed, world, byPlayer, principalSel);

  // Help is classified from the clicked cell, same as the interaction.
  WorldInteraction[] IFillerInteractionTarget.GetFillerInteractionHelp(
    IWorldAccessor world,
    BlockSelection principalSel,
    IPlayer forPlayer,
    BlockPos clickedCell
  ) =>
    BuildInteractionHelp(
      world,
      principalSel,
      forPlayer,
      Classify(principalSel.Position, clickedCell, StructureAngle)
    );

  #endregion
}
