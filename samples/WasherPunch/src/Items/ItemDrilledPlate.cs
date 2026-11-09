using System.Collections.Generic;
using ExpandedLib.Definitions;
using ExpandedLib.Registries;
using Vintagestory.API.Common;

namespace WasherPunch.Items;

/// <summary>A vanilla plate with four holes drilled through it, one variant per plate metal.</summary>
[ItemRegister]
public partial class ItemDrilledPlate : Item, IExItemDefProvider {
  public static IEnumerable<ExItemDef> Definitions(string domain) =>
    [
      ExItemDef
        .Create(domain, "drilledplate")
        .Class<ItemDrilledPlate>()
        .MaxStackSize(8)
        .VariantGroup("metal", "iron", "meteoriciron")
        .Shape($"{domain}:item/drilledplate")
        .Texture("metal", "game:block/metal/ingot/{metal}")
        .CreativeTab("general", "*")
        .CreativeTab("items", "*")
        .CreativeTab(domain, "*"),
    ];
}
