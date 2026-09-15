using System.Collections.Generic;
using ExpandedLib.Definitions;
using ExpandedLib.Industry.Pipes;
using ExpandedLib.Registries;
using SmokeStack.BlockEntities;
using Vintagestory.API.Common;

namespace SmokeStack.Blocks;

/// <summary>Intake and anchor block of the smoke-stack multiblock; draws surplus gas from the
/// network it stands in and vents it to the sky.</summary>
[BlockRegister]
public partial class BlockSmokeStackIntake
  : BlockPipePassthrough,
    IExBlockDefProvider {
  #region Code-first definition

  /// <summary>The smoke-stack intake blocktype, anchor of the 72-cell chimney multiblock.</summary>
  public static new IEnumerable<ExBlockDef> Definitions(string domain) =>
    [
      ExBlockDef
        .Create(domain, "smokestack", "smokestack/intake")
        .Class<BlockSmokeStackIntake>()
        .EntityClass<BlockEntitySmokeStack>()
        .Material(EnumBlockMaterial.Ceramic)
        .Sound("walk", "game:walk/stone")
        .Sound("place", "game:block/ceramicplace")
        .SoundByTool(
          EnumTool.Pickaxe,
          "game:block/rock-hit-pickaxe",
          "game:block/rock-break-pickaxe"
        )
        .MaxStackSize(1)
        .Handbook("smokestack-intake-*")
        .MultiblockLayout(s =>
          s.Origin(-1, 0)
            .Core('I')
            .Legend('#', VanillaCodes.Refractory)
            .Legend('I', "smokestack:smokestack-intake-*")
            .Legend('a', VanillaCodes.Air)
            .Legend('B', VanillaCodes.AnyBricks)
            .Layer(
              -1,
              """
              # # #
              # # #
              # # #
              """
            )
            .Layer(
              0,
              """
              # I #
              # a #
              # # #
              """
            )
            .Layer(
              1,
              """
              # # #
              # a #
              # # #
              """
            )
            .Layer(
              2,
              """
              . B .
              B a B
              . B .
              """
            )
            .Layer(
              3,
              """
              . B .
              B a B
              . B .
              """
            )
            .Layer(
              4,
              """
              . B .
              B a B
              . B .
              """
            )
            .Layer(
              5,
              """
              . B .
              B a B
              . B .
              """
            )
            .Layer(
              6,
              """
              . B .
              B a B
              . B .
              """
            )
            .Layer(
              7,
              """
              . B .
              B a B
              . B .
              """
            )
            .Layer(
              8,
              """
              . B .
              B a B
              . B .
              """
            )
            .Layer(
              9,
              """
              . B .
              B a B
              . B .
              """
            )
            .Layer(
              10,
              """
              . B .
              B a B
              . B .
              """
            )
        )
        .CreativeCommon("*-intake-*-n")
        .Behavior("MultiblockStructure")
        .Behavior("Lockable")
        .VariantGroup("type", "intake")
        .VariantGroup("refractory", "tier1", "tier2", "tier3")
        .VariantGroup("orientation", "n", "s", "w", "e")
        .NetworkOriented()
        .ShapeByType("*-intake-*-s", "smokestack:smokestack/intake")
        .ShapeByType(
          "*-intake-*-e",
          "smokestack:smokestack/intake",
          rotateY: 90
        )
        .ShapeByType(
          "*-intake-*-n",
          "smokestack:smokestack/intake",
          rotateY: 180
        )
        .ShapeByType(
          "*-intake-*-w",
          "smokestack:smokestack/intake",
          rotateY: 270
        )
        .Texture("front1", "game:block/clay/refractory/{refractory}/front1")
        .SideSolid(false)
        .SideOpaque(false),
    ];

  #endregion
}
