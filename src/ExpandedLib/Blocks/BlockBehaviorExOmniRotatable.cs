using System;
using System.Linq;
using ExpandedLib.Helpers;
using ExpandedLib.Registries;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.ServerMods;
#if GAME_GE_1_22
using CraftingRecipe = Vintagestory.API.Common.IRecipeBase;
#else
using CraftingRecipe = Vintagestory.API.Common.GridRecipe;
#endif

namespace ExpandedLib.Blocks;

/// <summary>Vanilla's <c>OmniRotatable</c> (the slab behaviour: <c>rot</c> group, the same JSON
/// properties and place modes) for a block whose code has a dash; every code it places, rotates
/// or flips to is built from the whole code rather than its first dash-segment.</summary>
[BlockBehaviorRegister("ExOmniRotatable", PrefixModId = false)]
public class BlockBehaviorExOmniRotatable : BlockBehaviorOmniRotatable {
  private const string RotVariant = "rot";
  private const string PlaceModeAttribute = "slabPlaceMode";

  private bool _rotateH;
  private bool _rotateV;
  private bool _rotateV4;
  private bool _rotateSides;
  private string _facing = "player";

  /// <summary>Attaches the behaviour to <paramref name="block"/>.</summary>
  /// <param name="block">The block carrying the behaviour.</param>
  public BlockBehaviorExOmniRotatable(Block block)
    : base(block) { }

  /// <summary>Reads vanilla's properties: <c>rotateH</c>, <c>rotateV</c>, <c>rotateV4</c>,
  /// <c>rotateSides</c> (all false by default), <c>facing</c> (<c>player</c> or <c>block</c>,
  /// <c>player</c> by default) and <c>dropChance</c>.</summary>
  /// <param name="properties">The behaviour's JSON properties.</param>
  public override void Initialize(JsonObject properties) {
    base.Initialize(properties);
    _rotateH = properties["rotateH"].AsBool(_rotateH);
    _rotateV = properties["rotateV"].AsBool(_rotateV);
    _rotateV4 = properties["rotateV4"].AsBool(_rotateV4);
    _rotateSides = properties["rotateSides"].AsBool(_rotateSides);
    _facing = properties["facing"].AsString(_facing);
  }

  /// <summary>Places the variant vanilla's rules pick from the stack's place mode, the clicked
  /// face, the hit position and the player's yaw, after that variant's <c>CanPlaceBlock</c>
  /// allows it.</summary>
  /// <returns>False, with <paramref name="failureCode"/> set by <c>CanPlaceBlock</c> or to
  /// <c>cantplace</c> when the picked variant is not registered (a skipped variant).</returns>
  public override bool TryPlaceBlock(
    IWorldAccessor world,
    IPlayer byPlayer,
    ItemStack itemstack,
    BlockSelection blockSel,
    ref EnumHandling handling,
    ref string failureCode
  ) {
    handling = EnumHandling.PreventDefault;
    var mode =
      itemstack.Attributes == null
        ? EnumSlabPlaceMode.Auto
        : (EnumSlabPlaceMode)itemstack.Attributes.GetInt(PlaceModeAttribute, 0);

    Block? oriented = world.BlockAccessor.GetBlock(
      PlacedCode(mode, byPlayer, blockSel)
    );
    if (oriented == null) {
      failureCode = "cantplace";
      return false;
    }
    if (!oriented.CanPlaceBlock(world, byPlayer, blockSel, ref failureCode))
      return false;
    world.BlockAccessor.SetBlock(oriented.BlockId, blockSel.Position);
    return true;
  }

  private AssetLocation PlacedCode(
    EnumSlabPlaceMode mode,
    IPlayer byPlayer,
    BlockSelection blockSel
  ) {
    BlockFacing face = blockSel.Face;
    Vec3d hit = blockSel.HitPosition;
    string Yaw() => BlockFacing.HorizontalFromYaw(byPlayer.Entity.Pos.Yaw).Code;

    if (mode == EnumSlabPlaceMode.Horizontal)
      return Rot(
        face.IsVertical ? face.Opposite.Code
        : hit.Y < 0.5 ? "down"
        : "up"
      );
    if (mode == EnumSlabPlaceMode.Vertical)
      return Rot(
        face.IsHorizontal
          ? face.Opposite.Code
          : Block.SuggestedHVOrientation(byPlayer, blockSel)[0].Code
      );

    if (_rotateSides) {
      if (!_facing.Equals("block", StringComparison.CurrentCultureIgnoreCase))
        return Rot(face.IsVertical ? face.Opposite.Code : Yaw());
      double x = Math.Abs(hit.X - 0.5);
      double y = Math.Abs(hit.Y - 0.5);
      double z = Math.Abs(hit.Z - 0.5);
      string WestEast() => hit.X < 0.5 ? "west" : "east";
      string DownUp() => hit.Y < 0.5 ? "down" : "up";
      string NorthSouth() => hit.Z < 0.5 ? "north" : "south";
      return face.Axis switch {
        EnumAxis.X => Rot(
          z < 0.3 && y < 0.3 ? face.Opposite.Code
          : z > y ? NorthSouth()
          : DownUp()
        ),
        EnumAxis.Y => Rot(
          z < 0.3 && x < 0.3 ? face.Opposite.Code
          : z > x ? NorthSouth()
          : WestEast()
        ),
        _ => Rot(
          x < 0.3 && y < 0.3 ? face.Opposite.Code
          : x > y ? WestEast()
          : DownUp()
        ),
      };
    }

    if (!_rotateH && !_rotateV)
      return block.Code;

    string h = "north";
    string v;
    if (face.IsVertical) {
      v = face.Code;
      h = Yaw();
    } else if (_rotateV4) {
      h = _facing == "block" ? face.Opposite.Code : Yaw();
      double across = face.Axis == EnumAxis.X ? hit.Z - 0.5 : hit.X - 0.5;
      v =
        Math.Abs(across) > Math.Abs(hit.Y - 0.5)
          ? (across < 0 ? "left" : "right")
          : (hit.Y < 0.5 ? "up" : "down");
    } else
      v = hit.Y < 0.5 ? "up" : "down";

    if (_rotateH && _rotateV)
      return block.WithVariants(["v", RotVariant], [v, h]);
    return Rot(_rotateH ? h : v);
  }

  private AssetLocation Rot(string value) =>
    block.WithVariant(RotVariant, value);

  /// <summary>Cycles the output stack's place mode (auto, horizontal, vertical) when the first
  /// filled input is a block carrying this behaviour, as vanilla's slab mode recipes do.</summary>
  /// <param name="allInputslots">The grid's input slots.</param>
  /// <param name="outputSlot">The slot holding the crafted stack, whose attributes change.</param>
  /// <param name="byRecipe">The recipe crafted.</param>
  /// <param name="handled">Passed on to the base behaviour.</param>
  public override void OnCreatedByCrafting(
    ItemSlot[] allInputslots,
    ItemSlot outputSlot,
    CraftingRecipe byRecipe,
    ref EnumHandling handled
  ) {
    ItemSlot? input = allInputslots.FirstOrDefault(s => !s.Empty);
    if (
      input?.Itemstack.Block?.HasBehavior<BlockBehaviorExOmniRotatable>()
      == true
    ) {
      int mode =
        (input.Itemstack.Attributes.GetInt(PlaceModeAttribute, 0) + 1) % 3;
      if (mode == 0)
        outputSlot.Itemstack.Attributes.RemoveAttribute(PlaceModeAttribute);
      else
        outputSlot.Itemstack.Attributes.SetInt(PlaceModeAttribute, mode);
    }
    base.OnCreatedByCrafting(allInputslots, outputSlot, byRecipe, ref handled);
  }

  /// <summary>The variant a rotation by <paramref name="angle"/> degrees about the vertical axis
  /// turns a horizontal <c>rot</c> to, <c>v</c>'s left and right swapped where vanilla swaps them
  /// under <c>rotateV4</c>.</summary>
  /// <param name="angle">Degrees, a multiple of 90 in 0..270.</param>
  /// <param name="handling">Set to prevent the default when this behaviour answers.</param>
  /// <returns>The block's own code, unhandled, for a vertical or absent <c>rot</c>.</returns>
  public override AssetLocation GetRotatedBlockCode(
    int angle,
    ref EnumHandling handling
  ) {
    BlockFacing? current = BlockFacing.FromCode(block.Variant[RotVariant]);
    if (current == null || current.IsVertical)
      return block.Code;

    handling = EnumHandling.PreventDefault;
    BlockFacing turned = BlockFacing.HORIZONTALS_ANGLEORDER[
      ((360 - angle) / 90 + current.HorizontalAngleIndex) % 4
    ];
    if (!_rotateV4)
      return Rot(turned.Code);

    string v = block.Variant["v"];
    if (
      (
        angle == 90
        && (current == BlockFacing.WEST || current == BlockFacing.EAST)
      ) || (angle == 270 && current == BlockFacing.SOUTH)
    )
      v = v switch {
        "left" => "right",
        "right" => "left",
        _ => v,
      };
    return block.WithVariants([RotVariant, "v"], [turned.Code, v]);
  }

  /// <summary>The variant mirrored across <paramref name="axis"/>: <c>rot</c> reversed when it lies
  /// on that axis.</summary>
  /// <param name="axis">The axis mirrored across.</param>
  /// <param name="handling">Set to prevent the default.</param>
  /// <returns>The block's own code when <c>rot</c> lies on another axis.</returns>
  /// <exception cref="NullReferenceException">The block's <c>rot</c> is no facing.</exception>
  public override AssetLocation GetHorizontallyFlippedBlockCode(
    EnumAxis axis,
    ref EnumHandling handling
  ) {
    handling = EnumHandling.PreventDefault;
    BlockFacing current = BlockFacing.FromCode(block.Variant[RotVariant]);
    return current.Axis == axis ? Rot(current.Opposite.Code) : block.Code;
  }

  /// <summary>The variant turned upside down: a vertical <c>rot</c> reversed, else a vertical
  /// <c>v</c> reversed.</summary>
  /// <param name="handling">Set to prevent the default.</param>
  /// <returns>The block's own code when neither group is vertical.</returns>
  public override AssetLocation GetVerticallyFlippedBlockCode(
    ref EnumHandling handling
  ) {
    handling = EnumHandling.PreventDefault;
    BlockFacing? current = BlockFacing.FromCode(block.Variant[RotVariant]);
    if (current?.IsVertical == true)
      return Rot(current.Opposite.Code);
    current = BlockFacing.FromCode(block.Variant["v"]);
    return current?.IsVertical == true
      ? block.WithVariant("v", current.Opposite.Code)
      : block.Code;
  }
}
